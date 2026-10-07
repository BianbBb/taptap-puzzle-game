using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameLogic
{
    [ClientSaveData("NarrativeSaveData")]
    public sealed class NarrativeSaveData : BaseClientSaveData
    {
        [JsonProperty("OwnedMemoryIds")]
        private HashSet<string> m_ownedMemoryIds = new HashSet<string>();

        public static NarrativeSaveData Get => BaseClientSaveData.Get<NarrativeSaveData>();

        public bool HasMemory(string memoryId) => m_ownedMemoryIds.Contains(memoryId);

        public bool HasAllMemories(IReadOnlyList<string> memoryIds)
        {
            if (memoryIds == null)
            {
                return true;
            }

            for (int i = 0; i < memoryIds.Count; i++)
            {
                if (!HasMemory(memoryIds[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 发放记忆并保存；重复获得同一记忆不会重复存储。
        /// </summary>
        public bool AddMemories(IReadOnlyList<string> memoryIds)
        {
            if (memoryIds == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < memoryIds.Count; i++)
            {
                changed |= m_ownedMemoryIds.Add(memoryIds[i]);
            }

            if (changed)
            {
                Save();
            }

            return changed;
        }
    }
}
