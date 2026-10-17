using System;
using System.Collections.Generic;
using VoxelWorld.Generation;
using VoxelWorld.Mesh;
using VoxelWorld.Runtime.Abstractions;

// 说明：命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld。
// 直接书写 VoxelWorld.VoxelWorld 会被解析成"命名空间.成员"，因此用别名显式指向"世界真值容器"。
using VoxelWorldData = global::VoxelWorld.VoxelWorld;

namespace VoxelWorld.Runtime
{
    /// <summary>
    /// VoxelRuntimeCore：Runtime Core V1（**纯 C# 调度核心**）。
    ///
    /// 规范来源：
    ///   · VoxelWorld_Runtime_Core_Implementation.md（实现约束：§3 类规范、§4 Tick 阶段、§5 安装、§6 Mesh/View）；
    ///   · VoxelWorld_Runtime_Core.md（结构：状态机 / 注册表 / 阶段 A–H / 纪律 / 不变量）；
    ///   · VoxelWorld_Runtime_Interface.md（抽象：三个接口、依赖倒置、所有权）；
    ///   · VoxelWorld_Runtime.md（策略：数据先行 / 帧末闭合 / 唯一清脏位置）。
    ///
    /// 职责（只有三项，实现规范 §3.1）：
    ///   R1 Chunk 注册表管理（记录创建/注册/更新/删除 + 对账）
    ///   R2 Tick 调度（固定 A–H 阶段、每帧上限、状态机、重试判据、诊断报告）
    ///   R3 Generation / Mesh / View 编排（按 §5/§6 时序调用三个抽象，把结果翻译成状态转移与脏处理）
    ///
    /// 禁止（实现规范 §3.2）：
    ///   P1 生成规则    P2 Mesh 规则    P3 View 细节    P4 Unity 生命周期
    ///
    /// V1 范围（实现规范 §7）：单线程、同步 <c>Tick()</c>、**手动 Chunk 请求**、
    /// Generation/Mesh/View 三条连接；**不实现**距离流式加载、异步、Job、多线程、存档、Mining、Building。
    /// 本类型不出现任何 UnityEngine 标识符（Runtime_Core §1.1 RC-P1），可在无场景环境下完整运行（RC-P3）。
    /// </summary>
    public sealed class VoxelRuntimeCore
    {
        // -----------------------------------------------------------------
        // 依赖（全部为抽象；由组装点显式注入，Core 不 new 任何实现）
        // -----------------------------------------------------------------

        private readonly VoxelWorldData world;
        private readonly IWorldGenerator generator;
        private readonly IChunkMesher mesher;
        private readonly IChunkPresenter presenter;
        private readonly RuntimeCoreSettings settings;

        // -----------------------------------------------------------------
        // 内部状态（只有四类，实现规范 §3.5）
        // -----------------------------------------------------------------

        private readonly Dictionary<VoxelCoordinate, ChunkRecord> registry =
            new Dictionary<VoxelCoordinate, ChunkRecord>();

        private readonly HashSet<VoxelCoordinate> requests =
            new HashSet<VoxelCoordinate>();

        private readonly RuntimeChangeSet changeSet = new RuntimeChangeSet();

        private readonly RuntimeCoreDiagnostics diagnostics = new RuntimeCoreDiagnostics();

        private int tickIndex;

        // 复用缓冲（只避免每帧分配；不承载跨 Tick 语义）
        private readonly List<VoxelCoordinate> requestedThisTick = new List<VoxelCoordinate>();
        private readonly List<VoxelCoordinate> targetSet = new List<VoxelCoordinate>();
        private readonly List<VoxelCoordinate> loadTargets = new List<VoxelCoordinate>();
        private readonly List<VoxelCoordinate> unloadTargets = new List<VoxelCoordinate>();
        private readonly List<VoxelCoordinate> workBuffer = new List<VoxelCoordinate>();
        private readonly HashSet<VoxelCoordinate> targetLookup = new HashSet<VoxelCoordinate>();
        private readonly List<VoxelChunk> dirtyBuffer = new List<VoxelChunk>();

        public VoxelRuntimeCore(
            VoxelWorldData world,
            IWorldGenerator generator,
            IChunkMesher mesher,
            IChunkPresenter presenter,
            RuntimeCoreSettings settings)
        {
            if (world == null) throw new ArgumentNullException("world");
            if (generator == null) throw new ArgumentNullException("generator");
            if (mesher == null) throw new ArgumentNullException("mesher");
            if (presenter == null) throw new ArgumentNullException("presenter");
            if (settings == null) throw new ArgumentNullException("settings");

            string reason;
            if (!settings.IsValid(out reason))
            {
                throw new ArgumentException("RuntimeCoreSettings 非法：" + reason, "settings");
            }

            this.world = world;
            this.generator = generator;
            this.mesher = mesher;
            this.presenter = presenter;
            this.settings = settings;
        }

        // -----------------------------------------------------------------
        // 只读诊断
        // -----------------------------------------------------------------

        public RuntimeCoreDiagnostics Diagnostics
        {
            get { return diagnostics; }
        }

        /// <summary>改动集（V1 恒为空；最小可持久化状态的一部分，Runtime §7.1）。</summary>
        public RuntimeChangeSet ChangeSet
        {
            get { return changeSet; }
        }

        /// <summary>已执行的 Tick 次数（Core 自有的单调序号，不是时间）。</summary>
        public int TickIndex
        {
            get { return tickIndex; }
        }

        /// <summary>已加载 Chunk 数（记录中 Chunk != null 的数量）。</summary>
        public int LoadedChunkCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
                {
                    if (pair.Value.Chunk != null) count++;
                }

                return count;
            }
        }

        /// <summary>请求集合快照（升序）。</summary>
        public IList<VoxelCoordinate> GetRequestedCoordinates()
        {
            List<VoxelCoordinate> result = new List<VoxelCoordinate>(requests);
            result.Sort(RuntimeChangeSet.CompareCoordinate);
            return result;
        }

        /// <summary>已加载坐标快照（升序；只含 Chunk != null 的记录）。</summary>
        public IList<VoxelCoordinate> GetLoadedCoordinates()
        {
            List<VoxelCoordinate> result = new List<VoxelCoordinate>();
            foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
            {
                if (pair.Value.Chunk != null) result.Add(pair.Key);
            }

            result.Sort(RuntimeChangeSet.CompareCoordinate);
            return result;
        }

        /// <summary>失败坐标快照（升序）。</summary>
        public IList<VoxelCoordinate> GetFailedCoordinates()
        {
            List<VoxelCoordinate> result = new List<VoxelCoordinate>();
            foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
            {
                if (pair.Value.State == ChunkLifecycleState.Failed) result.Add(pair.Key);
            }

            result.Sort(RuntimeChangeSet.CompareCoordinate);
            return result;
        }

        /// <summary>取某个坐标的只读记录快照（未注册 ⇒ false）。</summary>
        public bool TryGetSnapshot(
            VoxelCoordinate chunkCoordinate,
            out ChunkRecordSnapshot snapshot)
        {
            ChunkRecord record;
            if (!registry.TryGetValue(chunkCoordinate, out record))
            {
                snapshot = null;
                return false;
            }

            snapshot = record.ToSnapshot();
            return true;
        }

        // -----------------------------------------------------------------
        // 手动 Chunk 请求（V1 的"加载集合"来源，实现规范 §3.4）
        // -----------------------------------------------------------------

        /// <summary>
        /// 请求加载一个 Chunk（幂等）。坐标超出垂直有效范围 ⇒ 拒绝并计数
        /// （Runtime §6.4：世界高度之外不加载、不生成、不呈现）。
        /// 本方法**不**触碰世界数据与注册表（判定发生在 B/D 阶段）。
        /// </summary>
        public bool RequestChunk(VoxelCoordinate chunkCoordinate)
        {
            if (!settings.IsWithinVerticalRange(chunkCoordinate))
            {
                diagnostics.RejectedRequests++;
                diagnostics.LastFailureReason =
                    "请求被拒（垂直范围外）：" + chunkCoordinate +
                    "，VerticalChunkCount=" + settings.VerticalChunkCount;
                return false;
            }

            return requests.Add(chunkCoordinate);
        }

        /// <summary>撤销请求（幂等）。已加载的坐标由下一次 Tick 的 C 阶段卸载。</summary>
        public bool ReleaseChunk(VoxelCoordinate chunkCoordinate)
        {
            return requests.Remove(chunkCoordinate);
        }

        /// <summary>清空请求集合（不直接删数据；数据由后续 Tick 的 C 阶段释放）。</summary>
        public void ClearRequests()
        {
            requests.Clear();
        }

        /// <summary>
        /// 归零全部 <c>Failed</c> 记录的失败计数，恢复自动重试（Runtime_Core §9.2-4）；
        /// 数据完整（NeedsReinstall == false）的记录直接回到 <c>Dirty</c>（T11），
        /// 需要重装的记录在下一次 Tick 的 D 阶段重新走 Generate→Validate→Install（T10）。
        /// </summary>
        public void RetryFailedChunks()
        {
            workBuffer.Clear();
            foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
            {
                if (pair.Value.State == ChunkLifecycleState.Failed)
                {
                    workBuffer.Add(pair.Key);
                }
            }

            workBuffer.Sort(RuntimeChangeSet.CompareCoordinate);

            for (int i = 0; i < workBuffer.Count; i++)
            {
                ChunkRecord record = registry[workBuffer[i]];
                record.FailureCount = 0;
                record.FailureReason = null;

                if (!record.NeedsReinstall && record.Chunk != null)
                {
                    record.State = ChunkLifecycleState.Dirty;
                    record.StateChangedTick = tickIndex;
                }
                // NeedsReinstall == true ⇒ 保持 Failed；下一次 Tick 的 D 阶段按 loadTargets 重新安装
            }
        }

        // -----------------------------------------------------------------
        // Tick（A–H，实现规范 §4；顺序不可交换）
        // -----------------------------------------------------------------

        public RuntimeCoreTickReport Tick()
        {
            tickIndex++;
            diagnostics.TickCount = tickIndex;

            RuntimeCoreTickReport report = new RuntimeCoreTickReport();
            report.TickIndex = tickIndex;

            // ---- A：更新请求集合（定格本帧工作集，只读快照）----
            BuildRequestSnapshot(report);

            // ---- B：计算目标 Chunk 集合 + 对账 ----
            Reconcile();
            BuildTargetSets();

            // ---- C：卸载 ----
            RunUnloadPass(report);

            // ---- D：生成（Generate → Validate → Install → Register）----
            RunLoadPass(report);

            // ---- E：编辑提交预留（V1 空操作；位置保留，V2 接入 Mining）----
            // 无副作用：V1 不提供编辑入口，改动集恒为空。

            // ---- F + G：Mesh 构建 + View 呈递（同 Tick 配对）----
            RunBuildPresentPass(report);

            // ---- H：诊断（只读）----
            FillCounts(report);
            CheckInvariants(report);
            report.RejectedRequestCount = diagnostics.RejectedRequests;

            return report;
        }

        // -----------------------------------------------------------------
        // A：更新请求集合
        // -----------------------------------------------------------------

        private void BuildRequestSnapshot(RuntimeCoreTickReport report)
        {
            requestedThisTick.Clear();
            requestedThisTick.AddRange(requests);
            requestedThisTick.Sort(RuntimeChangeSet.CompareCoordinate);
            report.RequestedCount = requestedThisTick.Count;
        }

        // -----------------------------------------------------------------
        // B：目标集合 + 差异集合 + 对账
        // -----------------------------------------------------------------

        private void BuildTargetSets()
        {
            targetSet.Clear();
            targetLookup.Clear();

            for (int i = 0; i < requestedThisTick.Count; i++)
            {
                VoxelCoordinate coordinate = requestedThisTick[i];
                if (!settings.IsWithinVerticalRange(coordinate)) continue;

                targetSet.Add(coordinate);
                targetLookup.Add(coordinate);
            }

            // loadTargets = 未注册 的坐标 ∪ 需要重装的 Failed 记录（且未超重试上限）
            loadTargets.Clear();
            for (int i = 0; i < targetSet.Count; i++)
            {
                VoxelCoordinate coordinate = targetSet[i];

                ChunkRecord record;
                if (!registry.TryGetValue(coordinate, out record))
                {
                    loadTargets.Add(coordinate);
                    continue;
                }

                if (record.NeedsReinstall &&
                    record.FailureCount <= settings.MaxRetriesPerChunk)
                {
                    loadTargets.Add(coordinate);
                }
            }

            // unloadTargets = 已注册 但不在目标集合中的坐标（升序）
            unloadTargets.Clear();
            foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
            {
                if (!targetLookup.Contains(pair.Key)) unloadTargets.Add(pair.Key);
            }

            unloadTargets.Sort(RuntimeChangeSet.CompareCoordinate);
        }

        /// <summary>
        /// 数据 / 注册表对账（Runtime_Core §8.5；只投影、不改数据）：
        ///   ① 世界有 Chunk 但注册表无记录（外部写入）⇒ 补登记为 Dirty，**保留其数据**；
        ///   ② 记录有 Chunk 但世界里已无该 Chunk ⇒ Failed(DataMissing)；
        ///   ③ State == Ready 但 Chunk.IsDirty ⇒ 投影回 Dirty。
        /// </summary>
        private void Reconcile()
        {
            // ① 外部写入 ⇒ 补登记（V1 收窄：不重新生成、不覆盖外部数据，只登记并呈现其真实数据；
            //    "全量补齐 + 改动集叠加"需要改动集，属于 V2 接入 Mining 后的事项。）
            foreach (VoxelChunk chunk in world.Chunks)
            {
                if (chunk == null) continue;

                VoxelCoordinate coordinate = chunk.Coordinate;
                if (registry.ContainsKey(coordinate)) continue;

                ChunkRecord record = new ChunkRecord(coordinate, tickIndex);
                record.Chunk = chunk;
                record.NeedsReinstall = false;

                // $3.3-2 投影要求：Dirty ⇒ IsDirty。没有任何表示与该数据一致 ⇒ 必须重建表示。
                if (!chunk.IsDirty) chunk.MarkDirty();

                record.State = ChunkLifecycleState.Dirty;
                record.StateChangedTick = tickIndex;
                registry.Add(coordinate, record);

                diagnostics.ReconcileRegistrations++;
            }

            // ② / ③ 需要修改记录：先收集坐标，再统一处理（避免遍历中修改集合）
            workBuffer.Clear();
            foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
            {
                ChunkRecord record = pair.Value;

                if (record.Chunk != null)
                {
                    VoxelChunk existing;
                    if (!world.TryGetChunk(pair.Key, out existing) || existing == null)
                    {
                        workBuffer.Add(pair.Key);
                        continue;
                    }
                }

                if (record.State == ChunkLifecycleState.Ready &&
                    record.Chunk != null &&
                    record.Chunk.IsDirty)
                {
                    workBuffer.Add(pair.Key);
                }
            }

            workBuffer.Sort(RuntimeChangeSet.CompareCoordinate);

            for (int i = 0; i < workBuffer.Count; i++)
            {
                ChunkRecord record = registry[workBuffer[i]];

                if (record.Chunk != null)
                {
                    VoxelChunk existing;
                    if (!world.TryGetChunk(record.Coordinate, out existing) || existing == null)
                    {
                        record.State = ChunkLifecycleState.Failed;
                        record.Chunk = null;
                        record.MeshData = null;
                        record.NeedsReinstall = true;
                        record.FailureReason =
                            "DataMissing：注册表有记录但世界里已无该 Chunk（" +
                            record.Coordinate + "）";
                        record.FailureCount++;
                        record.StateChangedTick = tickIndex;
                        diagnostics.DataMissingFailures++;
                        diagnostics.FailedTotal++;
                        diagnostics.LastFailureReason = record.FailureReason;
                        continue;
                    }
                }

                if (record.State == ChunkLifecycleState.Ready &&
                    record.Chunk != null &&
                    record.Chunk.IsDirty)
                {
                    record.State = ChunkLifecycleState.Dirty;
                    record.StateChangedTick = tickIndex;
                }
            }
        }

        // -----------------------------------------------------------------
        // C：卸载（释放表示 → 删除数据 → 删除记录）
        // -----------------------------------------------------------------

        private void RunUnloadPass(RuntimeCoreTickReport report)
        {
            int processed = 0;

            for (int i = 0; i < unloadTargets.Count; i++)
            {
                if (processed >= settings.MaxUnloadsPerFrame) break;

                UnloadChunk(unloadTargets[i]);
                processed++;
                report.UnloadedThisTick++;
            }
        }

        private void UnloadChunk(VoxelCoordinate coordinate)
        {
            ChunkRecord record;
            if (!registry.TryGetValue(coordinate, out record)) return;

            record.State = ChunkLifecycleState.Unloading;
            record.StateChangedTick = tickIndex;

            // 顺序固定：释放表示 → 删除数据 → 删除记录（Runtime_Core §7.1）
            // Release 无条件调用（即使没有数据）：失败路径可能已留下旧表示（Runtime_Core §7.2-4）
            try
            {
                presenter.Release(coordinate);
            }
            catch (Exception ex)
            {
                // 数据释放不能被表示层失败阻塞：计入计数 + 原因（可观测）
                diagnostics.ReleaseFailures++;
                diagnostics.LastFailureReason =
                    "Release 异常（" + coordinate + "）：" + ex.GetType().Name + ": " + ex.Message;
            }

            world.RemoveChunk(coordinate);
            registry.Remove(coordinate);

            diagnostics.UnloadedTotal++;
        }

        // -----------------------------------------------------------------
        // D：生成（Generate → Validate → Install → Register）
        // -----------------------------------------------------------------

        private void RunLoadPass(RuntimeCoreTickReport report)
        {
            int processed = 0;

            for (int i = 0; i < loadTargets.Count; i++)
            {
                if (processed >= settings.MaxLoadsPerFrame) break;

                if (LoadChunk(loadTargets[i])) report.LoadedThisTick++;
                processed++;
            }
        }

        /// <summary>加载一个坐标：Generate → Validate → Install → Register。返回是否成功注册。</summary>
        private bool LoadChunk(VoxelCoordinate coordinate)
        {
            // 重试路径会复用同一坐标 ⇒ 先移除旧记录；占位记录在 Register 步重新插入
            registry.Remove(coordinate);

            ChunkRecord record = new ChunkRecord(coordinate, tickIndex);

            // ---- Generate（此阶段**不得**触碰世界与注册表）----
            VoxelChunk source = null;
            try
            {
                source = generator.Generate(coordinate);
            }
            catch (Exception ex)
            {
                FailGeneration(record, "生成异常（" + coordinate + "）：" +
                                       ex.GetType().Name + ": " + ex.Message);
                return false;
            }

            // ---- Validate（坐标自洽 + 非空 + 体素类型合法）----
            if (source == null)
            {
                FailGeneration(record, "生成输出为 null（" + coordinate + "）");
                return false;
            }

            if (source.Coordinate != coordinate)
            {
                FailGeneration(record, "生成输出坐标不自洽：请求 " + coordinate +
                                       "，返回 " + source.Coordinate);
                return false;
            }

            string typeError;
            if (!TryValidateTypes(source, out typeError))
            {
                FailGeneration(record, "生成输出含非法体素类型（" + coordinate +
                                       "）：" + typeError);
                return false;
            }

            // ---- Install（世界唯一被触碰的时机）----
            string installError;
            VoxelChunk target;
            if (!TryInstall(coordinate, source, out target, out installError))
            {
                record.State = ChunkLifecycleState.Failed;
                record.Chunk = target;               // 可能部分写入 ⇒ 重试必须全量写入
                record.MeshData = null;
                record.NeedsReinstall = true;
                record.FailureReason = installError;
                record.FailureCount++;
                record.StateChangedTick = tickIndex;
                registry.Add(coordinate, record);

                diagnostics.FailedTotal++;
                diagnostics.LastFailureReason = installError;
                return false;
            }

            // ---- Register（权威注册表的唯一插入点）----
            if (registry.ContainsKey(coordinate))
            {
                // 防御：正常流程不可能发生（重复注册不得静默覆盖已有记录）
                diagnostics.FailedTotal++;
                diagnostics.LastFailureReason = "DuplicateRegistration：" + coordinate;
                return false;
            }

            record.Chunk = target;
            record.MeshData = null;
            record.FailureReason = null;
            record.FailureCount = 0;
            record.NeedsReinstall = false;
            record.State = ChunkLifecycleState.Dirty;
            record.StateChangedTick = tickIndex;
            registry.Add(coordinate, record);

            diagnostics.GeneratedTotal++;
            return true;
        }

        private void FailGeneration(ChunkRecord record, string reason)
        {
            record.State = ChunkLifecycleState.Failed;
            record.Chunk = null;                 // 世界未被触碰
            record.MeshData = null;
            record.NeedsReinstall = true;
            record.FailureReason = reason;
            record.FailureCount++;
            record.StateChangedTick = tickIndex;
            registry.Add(record.Coordinate, record);

            diagnostics.FailedTotal++;
            diagnostics.LastFailureReason = reason;
        }

        private static bool TryValidateTypes(VoxelChunk source, out string error)
        {
            int size = VoxelChunk.ChunkSize;

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int z = 0; z < size; z++)
                    {
                        int raw = (int)source.GetVoxel(x, y, z).Type;
                        if (raw < (int)VoxelType.Air || raw > (int)VoxelType.Stone)
                        {
                            error = "(" + x + ", " + y + ", " + z + ") 类型值越界：" + raw;
                            return false;
                        }
                    }
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 安装：把"游离的生成结果"写进**世界拥有**的 Chunk，并叠加改动集（实现规范 §5.3 R1–R8）。
        /// 写入只能经 <c>VoxelChunk.SetVoxel</c>；坐标换算只能经 VoxelCore。
        /// </summary>
        private bool TryInstall(
            VoxelCoordinate coordinate,
            VoxelChunk source,
            out VoxelChunk target,
            out string error)
        {
            target = null;

            try
            {
                VoxelChunk existing;
                bool existedBefore = world.TryGetChunk(coordinate, out existing) &&
                                     existing != null;
                target = existedBefore ? existing : world.GetOrCreateChunk(coordinate);

                // R3：只有"由 GetOrCreateChunk 新创建（必为空）"才允许跳过 Air 写入；
                //     目标已存在（外部懒创建 / 上次安装残留）⇒ 必须**全量写入（含 Air）**，
                //     否则会残留旧数据（R4：重试同样走全量写入）。
                bool skipAir = !existedBefore;

                int size = VoxelChunk.ChunkSize;
                for (int x = 0; x < size; x++)
                {
                    for (int y = 0; y < size; y++)
                    {
                        for (int z = 0; z < size; z++)
                        {
                            VoxelType type = source.GetVoxel(x, y, z).Type;
                            if (skipAir && type == VoxelType.Air) continue;
                            target.SetVoxel(x, y, z, type);
                        }
                    }
                }

                // R5：叠加改动集（必须在写入之后 —— 改动优先，生成不覆盖改动）
                ApplyChangeSet(coordinate, target);

                // R6：收尾保证脏（全 Air 的 Chunk 可能零次写入 ⇒ 依赖新 Chunk 的初始脏，显式兜底）
                if (!target.IsDirty) target.MarkDirty();

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "安装失败（" + coordinate + "）：" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private void ApplyChangeSet(VoxelCoordinate chunkCoordinate, VoxelChunk target)
        {
            workBuffer.Clear();
            changeSet.CollectForChunk(chunkCoordinate, workBuffer);

            for (int i = 0; i < workBuffer.Count; i++)
            {
                VoxelCoordinate worldCoordinate = workBuffer[i];

                VoxelType type;
                if (!changeSet.TryGet(worldCoordinate, out type)) continue;

                // 坐标换算必须用 VoxelCore（不得自实现坐标规则）
                VoxelCoordinate local =
                    VoxelWorldData.WorldToLocalCoordinate(worldCoordinate);

                target.SetVoxel(local.x, local.y, local.z, type);
            }
        }

        // -----------------------------------------------------------------
        // F + G：Mesh 构建 + View 呈递（同 Tick 配对）
        // -----------------------------------------------------------------

        private void RunBuildPresentPass(RuntimeCoreTickReport report)
        {
            dirtyBuffer.Clear();
            world.CollectDirtyChunks(dirtyBuffer);   // VoxelCore 已按 Chunk 坐标排序

            int processed = 0;

            for (int i = 0; i < dirtyBuffer.Count; i++)
            {
                if (processed >= settings.MaxChunkPairsPerFrame) break;

                VoxelChunk chunk = dirtyBuffer[i];
                if (chunk == null) continue;

                ChunkRecord record;
                if (!registry.TryGetValue(chunk.Coordinate, out record)) continue;   // 未加载的脏 Chunk 不构建
                if (record.State != ChunkLifecycleState.Dirty) continue;             // 只处理 Dirty
                if (record.Chunk == null) continue;

                BuildAndPresent(record, chunk, report);
                processed++;
            }
        }

        private void BuildAndPresent(
            ChunkRecord record,
            VoxelChunk chunk,
            RuntimeCoreTickReport report)
        {
            record.State = ChunkLifecycleState.Building;   // Tick 内瞬态；必须同 Tick 离开
            record.StateChangedTick = tickIndex;

            // ---- F：Mesh 构建（只读输入）----
            VoxelMeshData meshData = null;
            string error = null;
            bool built = false;

            try
            {
                built = mesher.TryBuild(chunk, out meshData, out error);
            }
            catch (Exception ex)
            {
                built = false;
                meshData = null;
                error = "Mesher 抛异常（" + record.Coordinate + "）：" +
                        ex.GetType().Name + ": " + ex.Message;
            }

            if (!built || meshData == null)
            {
                record.MeshData = null;
                diagnostics.BuildFailures++;
                FailRepresentation(record, error ?? "Mesher 返回失败但未给出原因");
                return;
            }

            record.MeshData = meshData;
            diagnostics.BuiltTotal++;
            report.BuiltThisTick++;

            // ---- G：View 呈递（与构建同 Tick 配对；成功后才是唯一清脏位置）----
            PresenterOutcome outcome = PresenterOutcome.Failed;
            string presentError = null;

            try
            {
                outcome = presenter.Apply(record.Coordinate, meshData, out presentError);
            }
            catch (Exception ex)
            {
                outcome = PresenterOutcome.Failed;
                presentError = "Presenter 抛异常（" + record.Coordinate + "）：" +
                               ex.GetType().Name + ": " + ex.Message;
            }

            if (outcome == PresenterOutcome.Failed)
            {
                record.MeshData = null;
                diagnostics.PresentFailures++;
                FailRepresentation(record, presentError ?? "Presenter 返回失败但未给出原因");
                return;
            }

            // 防御性交叉校验：空几何必须报 Empty，有几何必须报 Presented（Runtime_Interface §6.3）
            bool dataHasGeometry = meshData.HasGeometry;
            bool outcomeClaimsGeometry = outcome == PresenterOutcome.Presented;

            if (dataHasGeometry != outcomeClaimsGeometry)
            {
                record.MeshData = null;
                diagnostics.OutcomeMismatches++;
                diagnostics.PresentFailures++;
                FailRepresentation(
                    record,
                    "PresenterOutcome 与 HasGeometry 不一致（" + record.Coordinate +
                    "）：outcome=" + outcome + ", HasGeometry=" + dataHasGeometry);
                return;
            }

            // ---- 成功：清脏（唯一位置）→ Ready ----
            chunk.ClearDirty();
            record.MeshData = null;
            record.FailureReason = null;
            record.FailureCount = 0;
            record.State = ChunkLifecycleState.Ready;
            record.StateChangedTick = tickIndex;

            diagnostics.PresentedTotal++;
            report.PresentedThisTick++;

            if (outcome == PresenterOutcome.Empty)
            {
                diagnostics.EmptyPresentedTotal++;
                report.EmptyPresentedThisTick++;
            }
        }

        private void FailRepresentation(ChunkRecord record, string reason)
        {
            record.FailureReason = reason;
            record.FailureCount++;
            record.StateChangedTick = tickIndex;
            diagnostics.LastFailureReason = reason;

            if (record.FailureCount > settings.MaxRetriesPerChunk)
            {
                // 停止自动重试；但**必须**保持 Chunk.IsDirty == true（否则表示永久过期）
                record.State = ChunkLifecycleState.Failed;
                record.NeedsReinstall = false;   // 数据完整，重试只需重建表示（T11）
                diagnostics.FailedTotal++;
                return;
            }

            record.State = ChunkLifecycleState.Dirty;   // 下一 Tick 重试
        }

        // -----------------------------------------------------------------
        // H：诊断（只读；不改变世界、注册表、表示）
        // -----------------------------------------------------------------

        private void FillCounts(RuntimeCoreTickReport report)
        {
            int loaded = 0;
            int dirty = 0;
            int ready = 0;
            int failed = 0;

            foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
            {
                ChunkRecord record = pair.Value;

                if (record.Chunk != null) loaded++;

                if (record.State == ChunkLifecycleState.Dirty ||
                    record.State == ChunkLifecycleState.Building) dirty++;
                else if (record.State == ChunkLifecycleState.Ready) ready++;
                else if (record.State == ChunkLifecycleState.Failed) failed++;
            }

            report.LoadedCount = loaded;
            report.DirtyCount = dirty;
            report.ReadyCount = ready;
            report.FailedCount = failed;
        }

        /// <summary>
        /// 帧末不变量检查（实现规范 §4.8）：只记录违规，不抛异常、不改变状态。
        /// 违规数应为 0；非 0 视为实现缺陷（由验收层判定）。
        /// </summary>
        private void CheckInvariants(RuntimeCoreTickReport report)
        {
            int violations = 0;
            string first = null;
            int recordsWithChunk = 0;

            foreach (KeyValuePair<VoxelCoordinate, ChunkRecord> pair in registry)
            {
                ChunkRecord record = pair.Value;

                if (record.Chunk != null) recordsWithChunk++;

                // 帧末闭合：不得存在瞬态状态
                if (record.State == ChunkLifecycleState.Generating ||
                    record.State == ChunkLifecycleState.Building ||
                    record.State == ChunkLifecycleState.Unloading)
                {
                    violations++;
                    if (first == null)
                    {
                        first = "帧末存在瞬态状态：" + record.Coordinate + " = " + record.State;
                    }
                }

                // Ready ⟺ (Chunk != null && !IsDirty)
                if (record.State == ChunkLifecycleState.Ready &&
                    (record.Chunk == null || record.Chunk.IsDirty))
                {
                    violations++;
                    if (first == null)
                    {
                        first = "Ready 投影不一致：" + record.Coordinate;
                    }
                }

                // Dirty|Building ⇒ (Chunk != null && IsDirty)
                if ((record.State == ChunkLifecycleState.Dirty ||
                     record.State == ChunkLifecycleState.Building) &&
                    (record.Chunk == null || !record.Chunk.IsDirty))
                {
                    violations++;
                    if (first == null)
                    {
                        first = "Dirty 投影不一致：" + record.Coordinate;
                    }
                }

                // Dirty|Building|Ready ⇒ Chunk != null
                if ((record.State == ChunkLifecycleState.Dirty ||
                     record.State == ChunkLifecycleState.Building ||
                     record.State == ChunkLifecycleState.Ready) &&
                    record.Chunk == null)
                {
                    violations++;
                    if (first == null)
                    {
                        first = "状态要求数据但 Chunk == null：" + record.Coordinate;
                    }
                }

                // Failed ⇒ FailureReason 非空
                if (record.State == ChunkLifecycleState.Failed &&
                    string.IsNullOrEmpty(record.FailureReason))
                {
                    violations++;
                    if (first == null)
                    {
                        first = "Failed 但无失败原因：" + record.Coordinate;
                    }
                }

                // 构建负载不得跨 Tick 残留
                if (record.MeshData != null)
                {
                    violations++;
                    if (first == null)
                    {
                        first = "构建负载跨 Tick 残留：" + record.Coordinate;
                    }
                }
            }

            if (world.ChunkCount != recordsWithChunk)
            {
                violations++;
                if (first == null)
                {
                    first = "世界 Chunk 数与记录不一致：world=" + world.ChunkCount +
                            "，records=" + recordsWithChunk;
                }
            }

            report.InvariantViolationCount = violations;
            report.FirstInvariantViolation = first;
        }

        public override string ToString()
        {
            return "VoxelRuntimeCore(tick=" + tickIndex +
                   ", requested=" + requests.Count +
                   ", loaded=" + LoadedChunkCount +
                   ", " + diagnostics + ")";
        }
    }
}
