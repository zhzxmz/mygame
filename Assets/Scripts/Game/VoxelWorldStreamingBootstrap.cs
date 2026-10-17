using System;
using UnityEngine;
using VoxelWorld;
using VoxelWorld.Adapters;
using VoxelWorld.Generation;
using VoxelWorld.Mesh;
using VoxelWorld.Runtime;
using VoxelWorld.Runtime.Streaming;
using VoxelWorld.View;
using VoxelWorld.Generation.Geology;

// 命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld ⇒ 用别名消除解析歧义
using VoxelWorldData = VoxelWorld.VoxelWorld;

/// <summary>
/// VoxelWorldStreamingBootstrap：**游戏层组装点 + 每帧驱动**（Runtime_Interface §2.4）。
///
/// 完整链路（玩家驱动）：
/// <code>
/// Player.position ──▶ WorldPositionToVoxelCoordinate（floor）
///                     ──▶ ChunkStreamingDriver.Update（VoxelCore 换算 → focusChunk → 请求求解）
///                     ──▶ VoxelRuntimeCore.Tick（加载 → 生成 → 脏 → Mesh → View → Ready）
/// </code>
///
/// 职责边界（严格遵守）：
///   · 本类是**唯一**允许 <c>new</c> 具体实现的位置（组装点）：World / Generator / Mesher 适配器 /
///     Presenter 适配器 / Core / 流式驱动；
///   · **不含世界规则**：不实现地形、几何、挖掘；不做几何加工；不写 Voxel 数据；
///   · **不决定"加载哪些 Chunk"**：该判定在 <see cref="ChunkStreamingDriver"/>（Runtime 层，纯 C#）；
///   · **不隐式查找**：Player / ChunkRoot / 材质全部为显式字段（禁止 Camera.main / FindObjectOfType）。
///
/// V1 范围：同步、单线程、每帧一次 <c>Update</c>；不实现存档、异步、Job、Mining、Structure。
/// </summary>
[DisallowMultipleComponent]
public sealed class VoxelWorldStreamingBootstrap : MonoBehaviour
{
    [Header("焦点（显式引用；未设置时使用 FocusWorldPosition）")]
    [Tooltip("玩家 / 相机载体。每帧读取其 position 作为流式焦点。")]
    public Transform Player;

    [Tooltip("未设置 Player 时使用该世界坐标作为焦点（离线/测试用）。")]
    public Vector3 FocusWorldPosition = Vector3.zero;

    [Header("流式参数（显式）")]
    [Tooltip("水平加载半径 R_load（Chunk 单位）。")]
    public int ViewDistanceChunks = 2;

    [Tooltip("卸载滞回：R_unload = R_load + Hysteresis（≥ 1 可避免相邻 Chunk 间移动时反复卸载/重载）。")]
    public int UnloadHysteresisChunks = 1;

    [Tooltip("垂直 Chunk 数量；必须与 Core 的一致（本类统一构造，保证一致）。")]
    public int VerticalChunkCount = 4;

    [Header("每帧预算（显式）")]
    public int MaxLoadsPerFrame = 2;
    public int MaxChunkPairsPerFrame = 2;
    public int MaxUnloadsPerFrame = 2;
    public int MaxRetriesPerChunk = 3;

    [Header("世界生成参数（显式）")]
    public int Seed = 12345;
    public int TerrainHeight = 24;
    public int SoilDepth = 3;
    public int WorldHeight = 64;
    public float TerrainFrequency = 0.06f;
    public float TerrainAmplitude = 6f;

    [Header("地质生成（Stage E 组装点：全关 = 与 Stage 1 旧世界逐体素一致）")]
    [Tooltip("启用洞穴（P1：只改变密度、可覆盖岩石；不生成几何/Entity）")]
    public bool EnableGeologyCaves = true;

    [Tooltip("洞穴阈值（噪声范围 [-1,1]；越大洞穴越少）")]
    public float GeologyCaveThreshold = 0f;

    [Tooltip("洞穴噪声频率（合法集合 1,2,4,8,16,32,64）")]
    public int GeologyCaveFrequency = 8;

    [Tooltip("启用矿藏属性（不改变体素；仅供只读查询，供未来 Entity 系统消费）")]
    public bool EnableGeologyDeposits = true;

    [Tooltip("矿藏噪声频率（合法集合 1,2,4,8,16,32,64）")]
    public int GeologyDepositFrequency = 8;

    [Tooltip("矿藏丰度（0..100）")]
    public int GeologyDepositRichness = 50;

    [Tooltip("启用群系修正（只影响地表材料选择；本类总是创建 biome provider）")]
    public bool EnableGeologyBiomeModifiers = false;

    [Header("表示（显式）")]
    [Tooltip("所有 Chunk 容器的父节点（其父链 lossyScale 必须为 1）。")]
    public Transform ChunkRoot;

    [Tooltip("3 个共享材质槽：0=Grass / 1=Dirt / 2=Stone（顺序固定）。")]
    public Material[] SubmeshMaterials = new Material[3];

    public bool BuildColliders = true;

    [Header("驱动")]
    [Tooltip("是否在 Update() 中自动执行（关闭时可手动调用 Step()）。")]
    public bool TickEveryFrame = true;

    // ---- 运行时对象（组装点的产物；全部由本类拥有）----
    private VoxelWorldData world;
    private ChunkViewPresenter presenter;
    private VoxelRuntimeCore core;
    private ChunkStreamingDriver streaming;
    private RuntimeCoreRequestSink sink;

    private bool buildFailed;
    private string buildError;

    // -----------------------------------------------------------------
    // 只读暴露（诊断 / 自动化验证）
    // -----------------------------------------------------------------

    public bool IsBuilt { get { return core != null; } }

    public string BuildError { get { return buildError; } }

    public VoxelWorldData World { get { return world; } }

    public VoxelRuntimeCore Core { get { return core; } }

    public ChunkStreamingDriver Streaming { get { return streaming; } }

    public ChunkViewPresenter Presenter { get { return presenter; } }

    /// <summary>
    /// 世界生成器（只读）：**当前组装点注入的具体实现**（Stage E 起为 <see cref="GeologyWorldGenerator"/>）。
    /// 暴露类型为抽象 <see cref="IWorldGenerator"/> —— 调用方（含 Runtime Core）不需要知道具体类型。
    /// </summary>
    public IWorldGenerator Generator { get; private set; }

    /// <summary>地质参数（只读；Stage E 组装点的配置结果）。</summary>
    public GeologySettings Geology { get; private set; }

    /// <summary>群系查询接口（只读；供未来 Entity / 玩法层消费）。</summary>
    public IBiomeProvider Biomes { get; private set; }

    /// <summary>资源查询接口（只读；供未来 Entity 系统消费）。</summary>
    public IDepositProvider Deposits { get; private set; }

    public ChunkStreamingReport LastStreamingReport { get; private set; }

    public RuntimeCoreTickReport LastTickReport { get; private set; }

    // -----------------------------------------------------------------
    // Unity 生命周期（只做转发，不含任何世界规则）
    // -----------------------------------------------------------------

    private void Awake()
    {
        Build();
    }

    private void Update()
    {
        if (!TickEveryFrame) return;
        Step();
    }

    private void OnDestroy()
    {
        Teardown();
    }

    // -----------------------------------------------------------------
    // 组装点
    // -----------------------------------------------------------------

    /// <summary>
    /// 显式装配（幂等）。失败时记录 <see cref="BuildError"/> 并不产生任何对象
    /// （禁止运行中静默修正非法参数）。
    /// </summary>
    public void Build()
    {
        if (core != null) return;
        if (buildFailed) return;

        if (ChunkRoot == null)
        {
            buildFailed = true;
            buildError = "ChunkRoot 未设置（容器父节点必须显式指定）";
            Debug.LogError("[VoxelWorldStreamingBootstrap] " + buildError);
            return;
        }

        string reason;

        WorldGenerationSettings generationSettings = new WorldGenerationSettings(
            Seed, TerrainHeight, SoilDepth, WorldHeight, TerrainFrequency, TerrainAmplitude);

        if (!generationSettings.IsValid(out reason))
        {
            buildFailed = true;
            buildError = "WorldGenerationSettings 非法：" + reason;
            Debug.LogError("[VoxelWorldStreamingBootstrap] " + buildError);
            return;
        }

        RuntimeCoreSettings coreSettings = new RuntimeCoreSettings
        {
            VerticalChunkCount = VerticalChunkCount,
            MaxLoadsPerFrame = MaxLoadsPerFrame,
            MaxChunkPairsPerFrame = MaxChunkPairsPerFrame,
            MaxUnloadsPerFrame = MaxUnloadsPerFrame,
            MaxRetriesPerChunk = MaxRetriesPerChunk
        };

        if (!coreSettings.IsValid(out reason))
        {
            buildFailed = true;
            buildError = "RuntimeCoreSettings 非法：" + reason;
            Debug.LogError("[VoxelWorldStreamingBootstrap] " + buildError);
            return;
        }

        ChunkStreamingSettings streamingSettings = new ChunkStreamingSettings
        {
            ViewDistanceChunks = ViewDistanceChunks,
            UnloadHysteresisChunks = UnloadHysteresisChunks,
            VerticalChunkCount = VerticalChunkCount
        };

        if (!streamingSettings.IsValid(out reason))
        {
            buildFailed = true;
            buildError = "ChunkStreamingSettings 非法：" + reason;
            Debug.LogError("[VoxelWorldStreamingBootstrap] " + buildError);
            return;
        }

        // 流式层与 Core 的垂直范围必须一致，否则越界请求会被 Core 拒绝（会计数，不会静默）
        if (streamingSettings.VerticalChunkCount != coreSettings.VerticalChunkCount)
        {
            buildFailed = true;
            buildError = "流式层与 Core 的 VerticalChunkCount 必须一致";
            Debug.LogError("[VoxelWorldStreamingBootstrap] " + buildError);
            return;
        }

        Material[] materials = (SubmeshMaterials != null &&
                                SubmeshMaterials.Length == VoxelMeshData.SubmeshCount)
            ? SubmeshMaterials
            : new Material[VoxelMeshData.SubmeshCount];

        ChunkViewSettings viewSettings = new ChunkViewSettings
        {
            Root = ChunkRoot,
            SubmeshMaterials = materials,
            BuildCollider = BuildColliders,
            Layer = ChunkRoot.gameObject.layer
        };

        if (!viewSettings.IsValid(out reason))
        {
            buildFailed = true;
            buildError = "ChunkViewSettings 非法：" + reason;
            Debug.LogError("[VoxelWorldStreamingBootstrap] " + buildError);
            return;
        }

        // ---- 组装（唯一允许 new 具体实现的位置）----
        world = new VoxelWorldData();

        // 世界生成组装点（Stage E）：地质生成器（实现 IWorldGenerator，替换原 BasicTerrainGenerator）
        GeologySettings geologySettings = new GeologySettings
        {
            EnableCaves = EnableGeologyCaves,
            CaveThreshold = GeologyCaveThreshold,
            CaveFrequency = GeologyCaveFrequency,
            EnableDeposits = EnableGeologyDeposits,
            DepositFrequency = GeologyDepositFrequency,
            DepositRichness = GeologyDepositRichness,
            EnableBiomeModifiers = EnableGeologyBiomeModifiers
        };

        if (!geologySettings.IsValid(out reason))
        {
            buildFailed = true;
            buildError = "GeologySettings 非法：" + reason;
            Debug.LogError("[VoxelWorldStreamingBootstrap] " + buildError);
            return;
        }

        // provider 使用的采样器（与生成器内部采样器参数一致；两者都是无状态纯函数）
        GeologySampler geologySampler = new GeologySampler(generationSettings, geologySettings);

        IBiomeProvider biomeProvider = new GeologyBiomeProvider(geologySampler);
        IDepositProvider depositProvider = new GeologyDepositProvider(geologySampler);

        IWorldGenerator generator = new GeologyWorldGenerator(
            generationSettings, geologySettings, biomeProvider, depositProvider);

        Generator = generator;
        Geology = geologySettings;
        Biomes = biomeProvider;
        Deposits = depositProvider;

        VoxelMeshGeneratorMesher mesher = new VoxelMeshGeneratorMesher();
        presenter = new ChunkViewPresenter(viewSettings);

        // Runtime Core 只认识 IWorldGenerator：Generate → Validate → Install → Register 流程不变
        core = new VoxelRuntimeCore(world, generator, mesher, presenter, coreSettings);

        sink = new RuntimeCoreRequestSink(core);
        streaming = new ChunkStreamingDriver(sink, streamingSettings);

        buildError = null;
    }

    // -----------------------------------------------------------------
    // 每帧驱动
    // -----------------------------------------------------------------

    /// <summary>
    /// 一次完整的"玩家位置 → 请求集合 → Tick"：
    ///   ① 取焦点世界坐标（Player.position 或 FocusWorldPosition）；
    ///   ② 转成整数体素坐标（floor：体素 = [x, x+1)，与 VoxelCore 语义一致）；
    ///   ③ <see cref="ChunkStreamingDriver.Update"/> 求解并提交 Request/Release；
    ///   ④ <c>VoxelRuntimeCore.Tick()</c> 完成加载 / 生成 / 重建 / 呈递 / 卸载。
    /// </summary>
    public ChunkStreamingReport Step()
    {
        if (core == null) Build();
        if (core == null) return null;

        Vector3 focus = Player != null ? Player.position : FocusWorldPosition;

        LastStreamingReport = streaming.Update(WorldPositionToVoxelCoordinate(focus));
        LastTickReport = core.Tick();

        return LastStreamingReport;
    }

    /// <summary>显式设置焦点世界坐标（未使用 Player 时的手动/离线入口）。</summary>
    public void SetFocusPosition(Vector3 worldPosition)
    {
        FocusWorldPosition = worldPosition;
    }

    /// <summary>
    /// 浮点世界坐标 → 整数体素坐标：体素 (x,y,z) 占据 [x,x+1)×…（VoxelCore §5）⇒ 取 floor。
    /// </summary>
    public static VoxelCoordinate WorldPositionToVoxelCoordinate(Vector3 worldPosition)
    {
        return new VoxelCoordinate(
            Mathf.FloorToInt(worldPosition.x),
            Mathf.FloorToInt(worldPosition.y),
            Mathf.FloorToInt(worldPosition.z));
    }

    // -----------------------------------------------------------------
    // 拆除
    // -----------------------------------------------------------------

    /// <summary>
    /// 拆除：撤销全部请求并释放全部表示（幂等）。不落盘、不触碰存档（V1 无存档）。
    /// </summary>
    public void Teardown()
    {
        if (core != null)
        {
            core.ClearRequests();
        }

        if (presenter != null)
        {
            presenter.ReleaseAll();
        }

        if (streaming != null)
        {
            streaming.ResetTracking();
        }

        sink = null;
        streaming = null;
        core = null;
        presenter = null;
        world = null;

        Generator = null;
        Geology = null;
        Biomes = null;
        Deposits = null;

        LastStreamingReport = null;
        LastTickReport = null;
    }
}
