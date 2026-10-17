using System.Collections.Generic;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Edit Layer。
    ///
    /// Step 1 职责：
    ///   保存 TerrainEditRecord，并提供基础存储查询。
    ///
    /// Step 1 尚未实现 TerrainEditGrid，因此 QueryEdits(Bounds) 暂时不执行
    /// Cell Bounds 空间过滤；当前返回全部 Record 并按 Sequence 排序。
    /// Cell Spatial Query 会在 TerrainEditGrid 实现后接入。
    ///
    /// Layer owns EditRecords；Chunk does NOT own EditRecords。
    /// </summary>
    public sealed class TerrainEditLayer
    {
        private readonly List<TerrainEditRecord> records =
            new List<TerrainEditRecord>();

        private readonly Dictionary<long, TerrainEditRecord> recordsByEditId =
            new Dictionary<long, TerrainEditRecord>();

        private long nextSequence = 1L;

        /// <summary>当前 Layer 中的 Edit Record 数量。</summary>
        public int Count
        {
            get { return records.Count; }
        }

        /// <summary>获取下一个建议 Sequence。</summary>
        public long NextSequence()
        {
            long sequence = nextSequence;
            nextSequence++;
            return sequence;
        }

        /// <summary>
        /// 添加一个 Edit Record。
        /// - 返回 false：edit 为空，或相同 EditId 已存在。
        /// - 已存在的 EditId 不会被覆盖，也不会产生第二份 authoritative Record。
        /// - Layer 不修改传入的 EditRecord。
        /// - Add 不进行 Density / Material / Mesh / Dirty / Save-Load。
        /// </summary>
        public bool Add(TerrainEditRecord edit)
        {
            if (edit == null)
            {
                return false;
            }

            if (recordsByEditId.ContainsKey(edit.EditId))
            {
                return false;
            }

            records.Add(edit);
            recordsByEditId.Add(edit.EditId, edit);
            return true;
        }

        /// <summary>
        /// Step 1 临时语义：返回全部 Record，按 Sequence ascending 排序。
        /// localBounds 参数保留用于后续 Cell Bounds 空间过滤。
        /// 返回的 Record 引用与 Layer 内部存储的是同一份 authoritative Record。
        /// </summary>
        public List<TerrainEditRecord> QueryEdits(Bounds localBounds)
        {
            List<TerrainEditRecord> result = new List<TerrainEditRecord>(records);
            result.Sort(CompareBySequence);
            return result;
        }

        private static int CompareBySequence(
            TerrainEditRecord a,
            TerrainEditRecord b)
        {
            return a.Sequence.CompareTo(b.Sequence);
        }
    }
}
