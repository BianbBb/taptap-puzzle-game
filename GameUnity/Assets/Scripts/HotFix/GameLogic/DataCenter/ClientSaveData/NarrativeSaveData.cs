using System.Collections.Generic;
using System.Linq;
using DGame;
using GameProto;
using Newtonsoft.Json;
using UnityEngine;

namespace GameLogic
{
    [ClientSaveData("NarrativeSaveData")]
    public sealed class NarrativeSaveData : BaseClientSaveData
    {
        private const string SaveSlotKeyPrefix = "NarrativeSaveSlot.";
        [JsonProperty("OwnedMemoryIds")]
        private HashSet<string> m_ownedMemoryIds = new HashSet<string>();

        [JsonProperty("OwnedMemoryOrder")]
        private List<string> m_ownedMemoryOrder = new List<string>();

        [JsonProperty("OwnedEmotionIds")]
        private HashSet<string> m_ownedEmotionIds = new HashSet<string>();

        [JsonProperty("EmotionUseCounts")]
        private Dictionary<string, int> m_emotionUseCounts = new Dictionary<string, int>();

        [JsonProperty("RuleTriggerCounts")]
        private Dictionary<string, int> m_ruleTriggerCounts = new Dictionary<string, int>();

        [JsonProperty("NpcAffection")]
        private Dictionary<string, float> m_npcAffection = new Dictionary<string, float>();

        [JsonProperty("StoryFlags")]
        private HashSet<string> m_storyFlags = new HashSet<string>();

        [JsonProperty("CompletedSessionIds")]
        private HashSet<string> m_completedSessionIds = new HashSet<string>();

        [JsonProperty("LastCompletedSessionId")]
        private string m_lastCompletedSessionId = string.Empty;

        [JsonProperty("OwnedNarrativeItemIds")]
        private HashSet<string> m_ownedNarrativeItemIds = new HashSet<string>();

        [JsonProperty("ExecutedActionKeys")]
        private HashSet<string> m_executedActionKeys = new HashSet<string>();

        [JsonProperty("CommittedTurnKeys")]
        private HashSet<string> m_committedTurnKeys = new HashSet<string>();

        [JsonProperty("CurrentDay")]
        private int m_currentDay = 1;

        [JsonProperty("CurrentTimeSlot")]
        private int m_currentTimeSlot;

        [JsonProperty("CurrentSessionId")]
        private string m_currentSessionId = string.Empty;

        [JsonProperty("CurrentNodeId")]
        private string m_currentNodeId = string.Empty;

        [JsonProperty("TurnIndex")]
        private int m_turnIndex;

        [JsonProperty("DayPhase")]
        private NarrativeDayPhase m_dayPhase = NarrativeDayPhase.Active;

        /// <summary>玩家在「新游戏」时填的名字，剧本里的 $PlayerName 会替换成它。</summary>
        [JsonProperty("PlayerName")]
        private string m_playerName = string.Empty;

        [JsonProperty("LastCommittedTurnKey")]
        private string m_lastCommittedTurnKey = string.Empty;

        protected override int CurrentSaveDataVersion => 8;

        public static NarrativeSaveData Get => BaseClientSaveData.Get<NarrativeSaveData>();

        public const int ManualSaveSlotCount = 6;

        public bool HasSaveSlot(int slot)
            => slot >= 1 && slot <= ManualSaveSlotCount && PlayerPrefs.HasKey(SaveSlotKeyPrefix + slot);

        public void SaveToSlot(int slot)
        {
            if (slot < 1 || slot > ManualSaveSlotCount) return;
            PlayerPrefs.SetString(SaveSlotKeyPrefix + slot, JsonConvert.SerializeObject(this));
            PlayerPrefs.Save();
        }

        public bool LoadFromSlot(int slot)
        {
            if (!HasSaveSlot(slot)) return false;
            string json = PlayerPrefs.GetString(SaveSlotKeyPrefix + slot);
            try
            {
                NarrativeSaveData loaded = JsonConvert.DeserializeObject<NarrativeSaveData>(json);
                if (loaded == null) return false;
                JsonConvert.PopulateObject(json, this, new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                });
                m_ownedMemoryOrder = loaded.GetMemoryIdsInAcquisitionOrder().ToList();
                Save();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public void DeleteSaveSlot(int slot)
        {
            if (slot < 1 || slot > ManualSaveSlotCount) return;
            PlayerPrefs.DeleteKey(SaveSlotKeyPrefix + slot);
            PlayerPrefs.Save();
        }

        public bool HasMemory(string memoryId)
            => !string.IsNullOrWhiteSpace(memoryId) && m_ownedMemoryIds.Contains(memoryId);

        public bool HasEmotion(string emotionId)
            => !string.IsNullOrWhiteSpace(emotionId) && m_ownedEmotionIds.Contains(emotionId);

        /// <summary>该情绪被玩家在对话中表达过的累计次数，用于图鉴圆圈的直径。</summary>
        public int GetEmotionUseCount(string emotionId)
            => !string.IsNullOrWhiteSpace(emotionId) &&
               m_emotionUseCounts.TryGetValue(emotionId, out int count)
                ? count
                : 0;

        [JsonIgnore]
        public IReadOnlyCollection<string> OwnedMemoryIds => m_ownedMemoryIds;

        [JsonIgnore]
        public IReadOnlyCollection<string> OwnedEmotionIds => m_ownedEmotionIds;

        [JsonIgnore]
        public int CurrentDay => m_currentDay;

        [JsonIgnore]
        public int CurrentTimeSlot => m_currentTimeSlot;

        [JsonIgnore]
        public string CurrentSessionId => m_currentSessionId;

        [JsonIgnore]
        public string CurrentNodeId => m_currentNodeId;

        [JsonIgnore]
        public int TurnIndex => m_turnIndex;

        [JsonIgnore]
        public NarrativeDayPhase DayPhase => m_dayPhase;

        /// <summary>玩家名字；没填过为空串，渲染前请走 NarrativePlayerName.Resolve。</summary>
        [JsonIgnore]
        public string PlayerName => m_playerName ?? string.Empty;

        /// <summary>写入玩家名字（会去空白、截断长度）；StartNewGame 不会清它。</summary>
        public void SetPlayerName(string playerName)
        {
            string normalized = NarrativePlayerName.Normalize(playerName);
            if (normalized == m_playerName)
            {
                return;
            }

            m_playerName = normalized;
            Save();
        }

        public int MemoryCount => m_ownedMemoryIds.Count;

        public bool NeedsMemoryCleanup => MemoryCount > NarrativeRules.MemoryCapacity;

        [JsonIgnore]
        public bool CanFinishMemoryCleanup => m_dayPhase == NarrativeDayPhase.MemoryCleanup &&
            MemoryCount <= NarrativeRules.MemoryCapacity;

        [JsonIgnore]
        public IReadOnlyCollection<string> StoryFlags => m_storyFlags;

        [JsonIgnore]
        public IReadOnlyCollection<string> CompletedSessionIds => m_completedSessionIds;

        [JsonIgnore]
        public string LastCompletedSessionId => m_lastCompletedSessionId;

        [JsonIgnore]
        public IReadOnlyCollection<string> OwnedNarrativeItemIds => m_ownedNarrativeItemIds;

        public bool HasNarrativeItem(string itemId)
            => !string.IsNullOrWhiteSpace(itemId) && m_ownedNarrativeItemIds.Contains(itemId);

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
            bool changed = AddMemoriesWithoutSaving(memoryIds).Count > 0;

            if (changed)
            {
                Save();
            }

            return changed;
        }

        public void BeginSession(string sessionId, string entryNodeId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(entryNodeId))
            {
                throw new System.ArgumentException("Session ID and entry node ID are required.");
            }

            m_currentSessionId = sessionId;
            m_currentNodeId = entryNodeId;
            m_turnIndex = 0;
            m_lastCommittedTurnKey = string.Empty;
            m_dayPhase = NarrativeDayPhase.Active;
            Save();
        }

        public void StartNewGame()
        {
            m_ownedMemoryIds.Clear();
            m_ownedMemoryOrder.Clear();
            m_ownedEmotionIds.Clear();
            m_emotionUseCounts.Clear();
            m_ruleTriggerCounts.Clear();
            m_npcAffection.Clear();
            m_storyFlags.Clear();
            m_completedSessionIds.Clear();
            m_ownedNarrativeItemIds.Clear();
            m_executedActionKeys.Clear();
            m_committedTurnKeys.Clear();
            m_currentDay = 1;
            m_currentTimeSlot = 0;
            m_currentSessionId = string.Empty;
            m_currentNodeId = string.Empty;
            m_lastCompletedSessionId = string.Empty;
            m_turnIndex = 0;
            m_dayPhase = NarrativeDayPhase.Active;
            m_lastCommittedTurnKey = string.Empty;
            Save();
        }

        /// <summary>清除旧版原型节点已不在新配置中的会话游标，保留收集物与好感存档。</summary>
        public void ClearInvalidCurrentSession()
        {
            if (string.IsNullOrWhiteSpace(m_currentSessionId) && string.IsNullOrWhiteSpace(m_currentNodeId))
            {
                return;
            }

            if (NarrativeConfigMgr.Instance.HasSession(m_currentSessionId) &&
                NarrativeConfigMgr.Instance.HasStoryNode(m_currentNodeId))
            {
                return;
            }

            m_currentSessionId = string.Empty;
            m_currentNodeId = string.Empty;
            m_turnIndex = 0;
            m_lastCommittedTurnKey = string.Empty;
            Save();
        }

        /// <summary>自动对白推进或好感门槛跳过节点时，更新当前剧情位置。</summary>
        public void AdvanceStoryNode(string nextNodeId, bool sessionEnded)
        {
            m_turnIndex++;
            if (sessionEnded)
            {
                if (!string.IsNullOrWhiteSpace(m_currentSessionId))
                {
                    m_completedSessionIds.Add(m_currentSessionId);
                    m_lastCompletedSessionId = m_currentSessionId;
                }

                m_currentSessionId = string.Empty;
                m_currentNodeId = string.Empty;
            }
            else
            {
                m_currentNodeId = nextNodeId ?? string.Empty;
            }

            Save();
        }

        internal void SetAffectionWithoutSaving(string npcId, float value)
        {
            if (!string.IsNullOrWhiteSpace(npcId))
            {
                m_npcAffection[npcId] = NarrativeRules.ClampAffection(value);
            }
        }

        internal bool HasExecutedActionKey(string key)
            => !string.IsNullOrWhiteSpace(key) && m_executedActionKeys.Contains(key);

        internal void ApplyNarrativeItemActionWithoutSaving(string actionType, string itemId)
        {
            if (string.Equals(actionType, "GrantItem", System.StringComparison.OrdinalIgnoreCase))
            {
                // 容量固定为 NarrativeRules.NarrativeItemCapacity（=1）：新物件顶替旧物件是既定语义。
                // 正常剧情里每个 GrantItem 都在同一节点的 OnExit 配了成对的 RemoveItem，
                // 走到这里还留着上一件，说明配表漏了一对 add/remove —— 打日志，别让道具静默丢失。
                if (m_ownedNarrativeItemIds.Count > 0 && !m_ownedNarrativeItemIds.Contains(itemId))
                {
                    DLogger.Warning("Narrative item slot overflow: granting {0} while [{1}] is still held. " +
                        "Check the OnExit RemoveItem pair of the previous node.", itemId,
                        string.Join(",", m_ownedNarrativeItemIds));
                }

                m_ownedNarrativeItemIds.Clear();
                m_ownedNarrativeItemIds.Add(itemId);
            }
            else if (string.Equals(actionType, "RemoveItem", System.StringComparison.OrdinalIgnoreCase))
                m_ownedNarrativeItemIds.Remove(itemId);
        }

        internal void MarkActionKeyWithoutSaving(string key)
        {
            if (!string.IsNullOrWhiteSpace(key)) m_executedActionKeys.Add(key);
        }

        public float GetAffection(string npcId)
            => !string.IsNullOrWhiteSpace(npcId) && m_npcAffection.TryGetValue(npcId, out float value)
                ? value : 0f;

        public bool HasStoryFlag(string flag) => !string.IsNullOrWhiteSpace(flag) && m_storyFlags.Contains(flag);

        public bool HasCompletedSession(string sessionId)
            => !string.IsNullOrWhiteSpace(sessionId) && m_completedSessionIds.Contains(sessionId);

        public void SetDayTime(int day, int timeSlot)
        {
            if (day < 1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(day));
            }

            m_currentDay = day;
            m_currentTimeSlot = timeSlot;
            Save();
        }

        public void BeginDayMonologue()
        {
            if (m_dayPhase != NarrativeDayPhase.Active || !string.IsNullOrWhiteSpace(m_currentSessionId))
            {
                throw new System.InvalidOperationException(
                    "Day monologue can only start after the active session has ended.");
            }

            m_dayPhase = NarrativeDayPhase.Monologue;
            Save();
        }

        public bool CompleteDayMonologue()
            => CompleteDayMonologue(0);

        public bool CompleteDayMonologue(int nextScheduledDay)
        {
            if (m_dayPhase != NarrativeDayPhase.Monologue)
            {
                return false;
            }

            if (NeedsMemoryCleanup)
            {
                return BeginMemoryCleanup();
            }

            return FinishDayAndAdvance(nextScheduledDay);
        }

        public bool BeginMemoryCleanup()
        {
            if (m_dayPhase != NarrativeDayPhase.Monologue || !NeedsMemoryCleanup)
            {
                return false;
            }

            m_dayPhase = NarrativeDayPhase.MemoryCleanup;
            Save();
            return true;
        }

        public bool RemoveMemory(string memoryId)
        {
            if (string.IsNullOrWhiteSpace(memoryId) || !m_ownedMemoryIds.Remove(memoryId))
            {
                return false;
            }

            m_ownedMemoryOrder?.RemoveAll(id => id == memoryId);
            Save();
            return true;
        }

        public bool RemoveMemoryDuringCleanup(string memoryId)
            => m_dayPhase == NarrativeDayPhase.MemoryCleanup && RemoveMemory(memoryId);

        public bool FinishMemoryCleanupAndAdvanceDay()
            => FinishMemoryCleanupAndAdvanceDay(0);

        public bool FinishMemoryCleanupAndAdvanceDay(int nextScheduledDay)
        {
            if (!CanFinishMemoryCleanup)
            {
                return false;
            }

            return FinishDayAndAdvance(nextScheduledDay);
        }

        public bool AddStoryFlag(string flag)
        {
            if (string.IsNullOrWhiteSpace(flag) || !m_storyFlags.Add(flag))
            {
                return false;
            }

            Save();
            return true;
        }

        public bool ClearStoryFlags()
        {
            if (m_storyFlags.Count == 0)
            {
                return false;
            }

            m_storyFlags.Clear();
            Save();
            return true;
        }

        public bool RemoveMemoryForDebug(string memoryId)
        {
            if (string.IsNullOrWhiteSpace(memoryId) || !m_ownedMemoryIds.Remove(memoryId))
            {
                return false;
            }

            m_ownedMemoryOrder?.RemoveAll(id => id == memoryId);
            Save();
            return true;
        }

        public void MarkGameComplete()
        {
            m_currentSessionId = string.Empty;
            m_currentNodeId = string.Empty;
            m_dayPhase = NarrativeDayPhase.GameComplete;
            Save();
        }

        public void MarkPrototypeContentExhausted()
        {
            m_currentSessionId = string.Empty;
            m_currentNodeId = string.Empty;
            m_dayPhase = NarrativeDayPhase.PrototypeContentExhausted;
            Save();
        }

        public string ExportRuntimeStateJson()
            => JsonConvert.SerializeObject(new
            {
                SaveVersion = CurrentSaveDataVersion,
                CurrentDay = m_currentDay,
                CurrentTimeSlot = m_currentTimeSlot,
                DayPhase = m_dayPhase.ToString(),
                CurrentSessionId = m_currentSessionId,
                CurrentNodeId = m_currentNodeId,
                TurnIndex = m_turnIndex,
                OwnedMemoryIds = GetMemoryIdsInStableOrder(),
                OwnedEmotionIds = GetEmotionIdsInStableOrder(),
                EmotionUseCounts = m_emotionUseCounts,
                OwnedNarrativeItemIds = GetNarrativeItemIdsInStableOrder(),
                NpcAffection = m_npcAffection,
                StoryFlags = m_storyFlags.OrderBy(flag => flag, System.StringComparer.Ordinal).ToList(),
                CompletedSessionIds = m_completedSessionIds.OrderBy(id => id, System.StringComparer.Ordinal).ToList(),
                LastCommittedTurnKey = m_lastCommittedTurnKey,
            }, Formatting.Indented);

        public bool GrantEmotion(string emotionId)
        {
            if (string.IsNullOrWhiteSpace(emotionId) || !NarrativeConfigMgr.Instance.HasEmotion(emotionId) ||
                !m_ownedEmotionIds.Add(emotionId))
            {
                return false;
            }

            Save();
            return true;
        }

        public void SetAffectionForDebug(string npcId, float value)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                throw new System.ArgumentException("NPC ID is required.", nameof(npcId));
            }

            m_npcAffection[npcId] = NarrativeRules.ClampAffection(value);
            Save();
        }

        public bool JumpToStoryNodeForDebug(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || !NarrativeConfigMgr.Instance.HasStoryNode(nodeId))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(m_currentSessionId))
            {
                NarrativeSessionConfig session = NarrativeConfigMgr.Instance.AllSessions
                    .Where(item => item != null && !HasCompletedSession(item.Id))
                    .OrderBy(item => item.DayIndex)
                    .ThenByDescending(item => item.Priority)
                    .FirstOrDefault();
                if (session != null)
                {
                    m_currentSessionId = session.Id;
                    m_currentDay = System.Math.Max(m_currentDay, session.DayIndex);
                }
            }

            m_currentNodeId = nodeId;
            m_turnIndex++;
            m_dayPhase = NarrativeDayPhase.Active;
            Save();
            return true;
        }

        private bool FinishDayAndAdvance(int nextScheduledDay)
        {
            NarrativeSessionConfig completedSession = NarrativeConfigMgr.Instance.GetSession(m_lastCompletedSessionId);
            if (completedSession != null && completedSession.IsFormalEnding)
            {
                MarkGameComplete();
                return true;
            }

            if (nextScheduledDay <= 0)
            {
                MarkPrototypeContentExhausted();
                return true;
            }

            m_currentDay = System.Math.Max(m_currentDay + 1, nextScheduledDay);
            m_currentTimeSlot = 0;
            m_currentSessionId = string.Empty;
            m_currentNodeId = string.Empty;
            m_turnIndex = 0;
            m_dayPhase = NarrativeDayPhase.Active;
            Save();
            return true;
        }

        internal bool HasCommittedTurn(string turnKey)
            => !string.IsNullOrWhiteSpace(turnKey) &&
                (m_lastCommittedTurnKey == turnKey || m_committedTurnKeys.Contains(turnKey));

        internal List<string> AddMemoriesWithoutSaving(IReadOnlyList<string> memoryIds)
        {
            var added = new List<string>();
            if (memoryIds == null)
            {
                return added;
            }

            m_ownedMemoryOrder = GetMemoryIdsInAcquisitionOrder().ToList();

            for (int i = 0; i < memoryIds.Count; i++)
            {
                string id = memoryIds[i];
                if (!string.IsNullOrWhiteSpace(id) && m_ownedMemoryIds.Add(id))
                {
                    added.Add(id);
                    m_ownedMemoryOrder.Add(id);
                }
            }

            return added;
        }

        internal List<string> AddEmotionsWithoutSaving(IEnumerable<string> emotionIds)
        {
            var added = new List<string>();
            if (emotionIds == null)
            {
                return added;
            }

            foreach (string id in emotionIds)
            {
                if (!string.IsNullOrWhiteSpace(id) && m_ownedEmotionIds.Add(id))
                {
                    added.Add(id);
                }
            }

            return added;
        }

        internal void AddEmotionUseWithoutSaving(string emotionId)
        {
            if (string.IsNullOrWhiteSpace(emotionId))
            {
                return;
            }

            m_emotionUseCounts.TryGetValue(emotionId, out int count);
            m_emotionUseCounts[emotionId] = count + 1;
        }

        /// <summary>情绪记忆规则（EmotionMemoryRule）已触发次数，用于 OncePerRun / MaxTriggerCount 判定。</summary>
        public int GetRuleTriggerCount(string ruleId)
            => !string.IsNullOrWhiteSpace(ruleId) && m_ruleTriggerCounts.TryGetValue(ruleId, out int count)
                ? count
                : 0;

        internal void MarkRuleTriggeredWithoutSaving(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId))
            {
                return;
            }

            m_ruleTriggerCounts.TryGetValue(ruleId, out int count);
            m_ruleTriggerCounts[ruleId] = count + 1;
        }

        internal void ApplyTurnState(string npcId, float affection, string nextNodeId,
            string turnKey, bool sessionEnded, IEnumerable<string> addedFlags)
        {
            if (!string.IsNullOrWhiteSpace(npcId))
            {
                m_npcAffection[npcId] = affection;
            }

            m_lastCommittedTurnKey = turnKey ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(turnKey)) m_committedTurnKeys.Add(turnKey);
            m_turnIndex++;
            if (sessionEnded)
            {
                if (!string.IsNullOrWhiteSpace(m_currentSessionId))
                {
                    m_completedSessionIds.Add(m_currentSessionId);
                    m_lastCompletedSessionId = m_currentSessionId;
                }

                m_currentSessionId = string.Empty;
                m_currentNodeId = string.Empty;
            }
            else
            {
                m_currentNodeId = nextNodeId ?? string.Empty;
            }

            if (addedFlags != null)
            {
                foreach (string flag in addedFlags)
                {
                    if (!string.IsNullOrWhiteSpace(flag))
                    {
                        m_storyFlags.Add(flag);
                    }
                }
            }

            Save();
        }

        internal string BuildTurnKey(string nodeId)
            => $"{m_currentSessionId}|{nodeId}|{m_turnIndex}";

        protected override void OnUpgradeData(int oldVersion, int newVersion)
        {
            m_ownedMemoryIds = m_ownedMemoryIds ?? new HashSet<string>();
            m_ownedMemoryOrder = GetMemoryIdsInAcquisitionOrder().ToList();
            m_ownedEmotionIds = m_ownedEmotionIds ?? new HashSet<string>();
            m_emotionUseCounts = m_emotionUseCounts ?? new Dictionary<string, int>();
            m_ruleTriggerCounts = m_ruleTriggerCounts ?? new Dictionary<string, int>();
            m_npcAffection = m_npcAffection ?? new Dictionary<string, float>();
            m_storyFlags = m_storyFlags ?? new HashSet<string>();
            m_completedSessionIds = m_completedSessionIds ?? new HashSet<string>();
            m_ownedNarrativeItemIds = m_ownedNarrativeItemIds ?? new HashSet<string>();
            m_executedActionKeys = m_executedActionKeys ?? new HashSet<string>();
            m_committedTurnKeys = m_committedTurnKeys ?? new HashSet<string>();
            m_currentDay = System.Math.Max(1, m_currentDay);
            m_currentSessionId = m_currentSessionId ?? string.Empty;
            m_currentNodeId = m_currentNodeId ?? string.Empty;
            m_playerName = m_playerName ?? string.Empty;
            m_lastCommittedTurnKey = m_lastCommittedTurnKey ?? string.Empty;
            m_lastCompletedSessionId = m_lastCompletedSessionId ?? string.Empty;
            if (oldVersion < 5 && m_dayPhase == NarrativeDayPhase.GameComplete)
            {
                // Version 4 used GameComplete when configured content was merely exhausted.
                m_dayPhase = NarrativeDayPhase.PrototypeContentExhausted;
            }
        }

        public IReadOnlyList<string> GetMemoryIdsInStableOrder()
            => m_ownedMemoryIds.OrderBy(id => id, System.StringComparer.Ordinal).ToList();

        public IReadOnlyList<string> GetMemoryIdsInAcquisitionOrder()
        {
            var ordered = new List<string>();
            var seen = new HashSet<string>();
            if (m_ownedMemoryOrder != null)
            {
                foreach (string id in m_ownedMemoryOrder)
                {
                    if (m_ownedMemoryIds.Contains(id) && seen.Add(id)) ordered.Add(id);
                }
            }

            ordered.AddRange(m_ownedMemoryIds.Where(id => !seen.Contains(id))
                .OrderBy(id => id, System.StringComparer.Ordinal));
            return ordered;
        }

        public IReadOnlyList<string> GetEmotionIdsInStableOrder()
            => m_ownedEmotionIds.OrderBy(id => id, System.StringComparer.Ordinal).ToList();

        public IReadOnlyList<string> GetNarrativeItemIdsInStableOrder()
            => m_ownedNarrativeItemIds.OrderBy(id => id, System.StringComparer.Ordinal).ToList();

        public IReadOnlyList<string> GetCleanupMemoryIds()
            => m_dayPhase == NarrativeDayPhase.MemoryCleanup
                ? GetMemoryIdsInStableOrder() : new List<string>();
    }
}
