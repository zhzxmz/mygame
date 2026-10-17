using System;
using System.Collections.Generic;
using System.Text;

namespace VoxelWorld
{
    /// <summary>
    /// VoxelCore 自检（Phase 1 测试辅助，纯数据层 —— 不依赖 UnityEngine / UnityEditor）。
    ///
    /// 覆盖 Voxel World Core V1 中 Phase 1 要求的全部数据契约：
    ///   · VoxelType 取值与 Air=0；
    ///   · Voxel.IsSolid()（Air ⇒ false，其它 ⇒ true）；
    ///   · VoxelCoordinate 的相等 / hash / 作为 Dictionary key；
    ///   · VoxelChunk 的 ChunkSize=16、局部坐标边界检查、脏标记；
    ///   · VoxelWorld 的 world → chunk → local 换算（含负坐标 floor 除法）、懒创建、读写与脏 Chunk 收集。
    ///
    /// 该类型只做断言与文本报告，不修改任何数据（除自己创建的临时世界）。
    /// </summary>
    public static class VoxelWorldDebug
    {
        public static bool RunSelfCheck(out string report)
        {
            List<string> lines = new List<string>();
            int checks = 0;
            int failures = 0;

            Action<bool, string> check = (condition, message) =>
            {
                checks++;
                if (condition)
                {
                    lines.Add("PASS - " + message);
                }
                else
                {
                    failures++;
                    lines.Add("FAIL - " + message);
                }
            };

            // -------------------------------------------------------------
            // VoxelType / Voxel
            // -------------------------------------------------------------
            check((int)VoxelType.Air == 0,
                "VoxelType.Air = 0（default(Voxel) 为空气）");
            check(Enum.GetValues(typeof(VoxelType)).Length == 4,
                "VoxelType 共 4 种：Air / Grass / Dirt / Stone（实际 " +
                Enum.GetValues(typeof(VoxelType)).Length + "）");

            check(!new Voxel(VoxelType.Air).IsSolid(), "Air.IsSolid() = false");
            check(!default(Voxel).IsSolid(), "default(Voxel).IsSolid() = false（Air）");
            check(new Voxel(VoxelType.Grass).IsSolid(), "Grass.IsSolid() = true");
            check(new Voxel(VoxelType.Dirt).IsSolid(), "Dirt.IsSolid() = true");
            check(new Voxel(VoxelType.Stone).IsSolid(), "Stone.IsSolid() = true");
            check(new Voxel(VoxelType.Stone).IsAir() == false, "Stone.IsAir() = false");
            check(Voxel.Air.IsAir(), "Voxel.Air.IsAir() = true");

            check(new Voxel(VoxelType.Stone) == new Voxel(VoxelType.Stone),
                "Voxel == 判定（同类型）");
            check(new Voxel(VoxelType.Stone) != new Voxel(VoxelType.Dirt),
                "Voxel != 判定（不同类型）");
            check(
                new Voxel(VoxelType.Grass).GetHashCode() ==
                new Voxel(VoxelType.Grass).GetHashCode(),
                "Voxel.GetHashCode 对等值一致");

            // -------------------------------------------------------------
            // VoxelCoordinate：相等 / hash / Dictionary key
            // -------------------------------------------------------------
            VoxelCoordinate a = new VoxelCoordinate(10, 5, 20);
            VoxelCoordinate b = new VoxelCoordinate(10, 5, 20);
            VoxelCoordinate c = new VoxelCoordinate(10, 5, 21);
            VoxelCoordinate negative = new VoxelCoordinate(-3, -4, -5);

            check(a.Equals(b) && a == b, "VoxelCoordinate 相等判定（同值）");
            check(!a.Equals(c) && a != c, "VoxelCoordinate 不相等判定（不同值）");
            check(a.GetHashCode() == b.GetHashCode(),
                "VoxelCoordinate.GetHashCode 对等值一致");
            check(a.GetHashCode() != negative.GetHashCode(),
                "VoxelCoordinate 负坐标 hash 与正坐标不同");
            check(a.ToString() == "(10, 5, 20)",
                "VoxelCoordinate.ToString 格式（实际 " + a + "）");

            Dictionary<VoxelCoordinate, string> map =
                new Dictionary<VoxelCoordinate, string>();
            map[a] = "positive";
            map[negative] = "negative";
            check(map.Count == 2, "VoxelCoordinate 可作为 Dictionary key（2 项）");
            check(map[b] == "positive", "Dictionary 以同值 key 命中：" + map[b]);
            check(map[new VoxelCoordinate(-3, -4, -5)] == "negative",
                "Dictionary 命中负坐标 key");

            // -------------------------------------------------------------
            // VoxelChunk：尺寸 / 边界检查 / 读写 / 脏标记
            // -------------------------------------------------------------
            check(VoxelChunk.ChunkSize == 16, "ChunkSize = 16");
            check(VoxelChunk.VoxelCount == 4096, "VoxelCount = 16³ = 4096");

            VoxelChunk chunk = new VoxelChunk(new VoxelCoordinate(0, 0, 0));

            check(chunk.IsDirty, "新 Chunk 初始为 dirty");
            check(chunk.GetVoxel(0, 0, 0).IsAir(),
                "新 Chunk 默认体素为 Air");
            check(chunk.GetVoxel(15, 15, 15).IsAir(),
                "边界内 (15,15,15) 可读");

            chunk.SetVoxel(0, 0, 0, VoxelType.Stone);
            check(chunk.GetVoxel(0, 0, 0) == new Voxel(VoxelType.Stone),
                "SetVoxel / GetVoxel 往返（Stone）");
            chunk.ClearDirty();
            check(!chunk.IsDirty, "ClearDirty 生效");
            check(chunk.SetVoxel(5, 5, 5, VoxelType.Air) == false,
                "写入相同类型不视为变化（SetVoxel 返回 false）");

            chunk.SetVoxel(5, 5, 5, VoxelType.Dirt);
            check(chunk.IsDirty, "写入不同类型会标记 dirty");

            check(ThrowsOutOfRange(chunk, 16, 0, 0), "localX=16 越界抛异常");
            check(ThrowsOutOfRange(chunk, -1, 0, 0), "localX=-1 越界抛异常");
            check(ThrowsOutOfRange(chunk, 0, 16, 0), "localY=16 越界抛异常");
            check(ThrowsOutOfRange(chunk, 0, 0, -1), "localZ=-1 越界抛异常");

            int lx, ly, lz;
            check(chunk.TryWorldToLocalCoordinate(
                    new VoxelCoordinate(3, 4, 5), out lx, out ly, out lz) &&
                  lx == 3 && ly == 4 && lz == 5,
                "Chunk.TryWorldToLocalCoordinate 命中本 Chunk");
            check(!chunk.TryWorldToLocalCoordinate(
                    new VoxelCoordinate(16, 0, 0), out lx, out ly, out lz),
                "Chunk.TryWorldToLocalCoordinate 拒绝 Chunk 外坐标");
            check(chunk.LocalToWorldCoordinate(15, 14, 13) ==
                  new VoxelCoordinate(15, 14, 13),
                "Chunk.LocalToWorldCoordinate 往返");

            // -------------------------------------------------------------
            // VoxelWorld：坐标换算（含负坐标）/ 懒创建 / 读写 / 脏收集
            // -------------------------------------------------------------
            check(VoxelWorld.FloorDiv(15, 16) == 0, "FloorDiv(15,16) = 0");
            check(VoxelWorld.FloorDiv(16, 16) == 1, "FloorDiv(16,16) = 1");
            check(VoxelWorld.FloorDiv(-1, 16) == -1, "FloorDiv(-1,16) = -1（负数向下取整）");
            check(VoxelWorld.FloorDiv(-16, 16) == -1, "FloorDiv(-16,16) = -1");
            check(VoxelWorld.FloorDiv(-17, 16) == -2, "FloorDiv(-17,16) = -2");

            CheckConversion(check, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            CheckConversion(check, 15, 15, 15, 0, 0, 0, 15, 15, 15);
            CheckConversion(check, 16, 0, 0, 1, 0, 0, 0, 0, 0);
            CheckConversion(check, -1, -1, -1, -1, -1, -1, 15, 15, 15);
            CheckConversion(check, -16, 0, 0, -1, 0, 0, 0, 0, 0);
            CheckConversion(check, -17, 0, 0, -2, 0, 0, 15, 0, 0);
            CheckConversion(check, 31, 32, 33, 1, 2, 2, 15, 0, 1);

            VoxelWorld world = new VoxelWorld();
            check(world.ChunkCount == 0, "空世界 ChunkCount = 0");

            Voxel missing;
            check(!world.TryGetVoxel(100, 100, 100, out missing) && missing.IsAir(),
                "未生成区域的 TryGetVoxel 返回 false 且体素为 Air");
            check(world.GetVoxel(100, 100, 100).IsAir(),
                "未生成区域的 GetVoxel 返回 Air");
            check(world.ChunkCount == 0, "读取未生成区域不会创建 Chunk");

            bool changed = world.SetVoxel(20, -5, 33, VoxelType.Stone);
            check(changed && world.ChunkCount == 1,
                "SetVoxel 懒创建 Chunk（ChunkCount=" + world.ChunkCount + "）");
            check(world.GetVoxel(20, -5, 33) == new Voxel(VoxelType.Stone),
                "世界坐标读写往返（含负坐标）");
            check(world.GetVoxel(-1, -1, -1).IsAir(),
                "负坐标未生成区域读取为 Air");

            VoxelCoordinate expectedChunk =
                VoxelWorld.WorldToChunkCoordinate(new VoxelCoordinate(20, -5, 33));
            VoxelChunk lazyChunk = null;
            check(expectedChunk == new VoxelCoordinate(1, -1, 2) &&
                  world.TryGetChunk(expectedChunk, out lazyChunk),
                "SetVoxel 创建的 Chunk 坐标正确：" + expectedChunk);

            check(world.SetVoxel(20, -5, 33, VoxelType.Stone) == false,
                "重复写入相同类型返回 false");
            check(world.SetVoxel(20, -5, 33, VoxelType.Dirt) == true,
                "写入不同类型返回 true");

            world.ClearDirtyFlags();
            check(lazyChunk != null && !lazyChunk.IsDirty,
                "ClearDirtyFlags 清除 Chunk 脏标记");

            world.SetVoxel(0, 0, 0, VoxelType.Grass);
            List<VoxelChunk> dirty = new List<VoxelChunk>();
            world.CollectDirtyChunks(dirty);
            check(dirty.Count == 1 && dirty[0] != null &&
                  dirty[0].Coordinate == new VoxelCoordinate(0, 0, 0),
                "CollectDirtyChunks 只返回被改动的 Chunk（" + dirty.Count + " 个）");

            world.RemoveChunk(new VoxelCoordinate(0, 0, 0));
            check(world.ChunkCount == 1, "RemoveChunk 生效（ChunkCount=1）");

            // -------------------------------------------------------------
            // 报告
            // -------------------------------------------------------------
            StringBuilder text = new StringBuilder();
            text.AppendLine("[VoxelWorldCore] ===== SELF CHECK =====");

            for (int i = 0; i < lines.Count; i++)
            {
                text.AppendLine(lines[i]);
            }

            text.AppendLine("Checks = " + checks);
            text.AppendLine("Failures = " + failures);
            text.AppendLine("Result = " + (failures == 0 ? "PASS" : "FAIL"));

            report = text.ToString();
            return failures == 0;
        }

        private static void CheckConversion(
            Action<bool, string> check,
            int worldX,
            int worldY,
            int worldZ,
            int chunkX,
            int chunkY,
            int chunkZ,
            int localX,
            int localY,
            int localZ)
        {
            VoxelCoordinate world = new VoxelCoordinate(worldX, worldY, worldZ);
            VoxelCoordinate chunk = VoxelWorld.WorldToChunkCoordinate(world);
            VoxelCoordinate local = VoxelWorld.WorldToLocalCoordinate(world);

            bool ok =
                chunk.x == chunkX && chunk.y == chunkY && chunk.z == chunkZ &&
                local.x == localX && local.y == localY && local.z == localZ;

            check(
                ok,
                "世界 " + world + " ⇒ chunk(" + chunkX + "," + chunkY + "," +
                chunkZ + ") local(" + localX + "," + localY + "," + localZ +
                ")（实际 " + chunk + " " + local + "）"
            );
        }

        private static bool ThrowsOutOfRange(
            VoxelChunk chunk,
            int localX,
            int localY,
            int localZ)
        {
            try
            {
                chunk.GetVoxel(localX, localY, localZ);
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }
    }
}
