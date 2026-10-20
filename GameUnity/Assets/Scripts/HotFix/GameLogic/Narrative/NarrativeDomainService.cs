using System;
using System.Collections.Generic;
using System.Linq;
using DGame;
using GameProto;

namespace GameLogic
{
    /// <summary>
    /// 承载剧情 Turn 的同步结算规则。所有持久状态先完整更新，再统一写入存档。
    /// </summary>
    public sealed class NarrativeDomainService
    {
        private readonly NarrativeActionService m_actionService = new NarrativeActionService();
        public static NarrativeTurnResult LastCommittedTurn { get; private set; }

        public event Action<NarrativeTurnResult> TurnCommitted;
        public event Action<string, float, float> AffectionChanged;
        public event Action<string> MemoryAdded;
        public event Action<string> EmotionEmerged;
        public event Action<string, string> StoryNodeChanged;

        public IReadOnlyList<string> GetAvailableEmotions(ReplyOptionConfig reply,
            NarrativeSaveData saveData = null)
        {
            if (reply == null)
            {
                throw new ArgumentNullException(nameof(reply));
            }

            saveData = saveData ?? NarrativeSaveData.Get;
            var result = new List<string>();
            bool restrictToAllowed = reply.AllowedEmotion != null && reply.AllowedEmotion.Count > 0;
            IReadOnlyList<string> candidates = restrictToAllowed
                ? reply.AllowedEmotion
                : saveData.GetEmotionIdsInStableOrder();

            for (int i = 0; i < candidates.Count; i++)
            {
                string emotionId = candidates[i];
                if (IsNoneEmotionId(emotionId) || string.IsNullOrWhiteSpace(emotionId))
                {
                    continue;
                }

                if (saveData.HasEmotion(emotionId) && !result.Contains(emotionId))
                {
                    result.Add(emotionId);
                }
            }

            return result;
        }

        public IReadOnlyList<string> GetLockedEmotions(ReplyOptionConfig reply,
            NarrativeSaveData saveData = null)
        {
            if (reply == null)
            {
                throw new ArgumentNullException(nameof(reply));
            }

            saveData = saveData ?? NarrativeSaveData.Get;
            var result = new List<string>();
            if (reply.AllowedEmotion == null)
            {
                return result;
            }

            for (int i = 0; i < reply.AllowedEmotion.Count; i++)
            {
                string emotionId = reply.AllowedEmotion[i];
                if (IsNoneEmotionId(emotionId) || string.IsNullOrWhiteSpace(emotionId) ||
                    saveData.HasEmotion(emotionId) || result.Contains(emotionId))
                {
                    continue;
                }

                if (NarrativeConfigMgr.Instance.HasEmotion(emotionId))
                {
                    result.Add(emotionId);
                }
            }

            return result;
        }

        private static bool IsNoneEmotionId(string emotionId)
        {
            return string.Equals(emotionId, NarrativeRules.NoneEmotionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(emotionId, NarrativeRules.NoneEmotionConfigId, StringComparison.OrdinalIgnoreCase);
        }

        public string GetDefaultEmotion(ReplyOptionConfig reply, NarrativeSaveData saveData = null)
        {
            IReadOnlyList<string> available = GetAvailableEmotions(reply, saveData);
            return available.Count == 0 ? NarrativeRules.NoneEmotionId : available[0];
        }

        public NarrativeTurnResult CommitTurn(StoryNodeConfig node, ReplyOptionConfig reply,
            string selectedEmotionId, NarrativeSaveData saveData = null)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (reply == null)
            {
                throw new ArgumentNullException(nameof(reply));
            }

            saveData = saveData ?? NarrativeSaveData.Get;
            EnsureReplyBelongsToNode(node, reply);
            if (string.IsNullOrWhiteSpace(saveData.CurrentSessionId) || saveData.CurrentNodeId != node.Id)
            {
                throw new InvalidOperationException(
                    $"Active session/node is {saveData.CurrentSessionId}/{saveData.CurrentNodeId}; cannot commit a reply for {node.Id}.");
            }

            if (!NarrativeRules.HasAllMemories(reply.RequiredMemory, saveData.HasMemory))
            {
                throw new InvalidOperationException($"Reply {reply.Id} is locked by missing memories.");
            }

            string emotionId = string.IsNullOrWhiteSpace(selectedEmotionId)
                ? NarrativeRules.NoneEmotionId : selectedEmotionId;
            EnsureEmotionAllowed(reply, emotionId, saveData);

            string turnKey = saveData.BuildTurnKey(node.Id);
            if (saveData.HasCommittedTurn(turnKey))
            {
                throw new InvalidOperationException($"Turn was already committed: {turnKey}");
            }

            float affectionBefore = saveData.GetAffection(node.NpcId);
            float affectionAfter = affectionBefore;
            var affectionChanges = new Dictionary<string, (float Before, float After)>();
            bool hasEmotionRule;
            float affectionDelta = GetAffectionDelta(reply, emotionId, out hasEmotionRule);
            if (!hasEmotionRule)
            {
                affectionDelta = reply.AffectionDelta;
            }

            string affectionNpcId = string.IsNullOrWhiteSpace(reply.AffectionNpcId)
                ? node.NpcId : reply.AffectionNpcId;
            if (!string.IsNullOrWhiteSpace(affectionNpcId) && affectionDelta != 0f)
            {
                float before = saveData.GetAffection(affectionNpcId);
                float after = NarrativeRules.ClampAffection(before + affectionDelta);
                affectionChanges[affectionNpcId] = (before, after);
                if (affectionNpcId == node.NpcId)
                    affectionAfter = after;
            }
            string sessionId = saveData.CurrentSessionId;

            // 分支只能读取本 Turn 已更新的好感与已选 Reply；奖励内容尚未写入。
            bool sessionEnded = node.IsSessionEnd || reply.IsSessionEnd;
            string nextNodeId = sessionEnded
                ? string.Empty
                : ResolveNextNode(node, reply.Id, affectionAfter);

            foreach (var change in affectionChanges)
                saveData.SetAffectionWithoutSaving(change.Key, change.Value.After);

            ValidateRewardMemories(reply.RewardMemory);
            m_actionService.Execute(node.OnExitActionIds, turnKey + "|node-exit", saveData, false);
            m_actionService.Execute(reply.ActionIds, turnKey + "|reply", saveData, false);
            List<string> addedMemories = saveData.AddMemoriesWithoutSaving(reply.RewardMemory);

            // 图鉴情绪圆的直径取这个计数，必须在 ApplyTurnState（唯一落盘点）之前累加。
            if (!IsNoneEmotionId(emotionId))
            {
                saveData.AddEmotionUseWithoutSaving(emotionId);
            }

            // 情绪值累积到规则阈值、且前置记忆齐备时激活新记忆；必须在情绪计数之后、落盘之前。
            List<string> ruleMemories = ActivateMemoriesByEmotionRule(saveData);
            if (ruleMemories.Count > 0)
            {
                addedMemories.AddRange(ruleMemories);
            }

            List<string> emergedEmotions = FindEmergentEmotions(saveData);
            saveData.AddEmotionsWithoutSaving(emergedEmotions);

            saveData.ApplyTurnState(node.NpcId, affectionAfter, nextNodeId, turnKey,
                sessionEnded, null);

            var result = new NarrativeTurnResult
            {
                SessionId = sessionId,
                NodeId = node.Id,
                ReplyId = reply.Id,
                SelectedEmotionId = emotionId,
                AffectionBefore = affectionBefore,
                AffectionDelta = affectionAfter - affectionBefore,
                AffectionAfter = affectionAfter,
                NextNodeId = nextNodeId,
                RewardMemoryIds = addedMemories.AsReadOnly(),
                NewlyEmergedEmotionIds = emergedEmotions.AsReadOnly(),
                SessionEnded = sessionEnded,
            };
            LastCommittedTurn = result;

            // 所有通知都在 SaveData 已一致更新之后发送；演出层失败不回滚业务状态。
            foreach (var change in affectionChanges)
            {
                InvokeSafely(AffectionChanged, handler =>
                    ((Action<string, float, float>)handler)(change.Key, change.Value.Before, change.Value.After));
            }
            foreach (string memoryId in addedMemories)
            {
                InvokeSafely(MemoryAdded, handler => ((Action<string>)handler)(memoryId));
            }

            foreach (string newEmotionId in emergedEmotions)
            {
                InvokeSafely(EmotionEmerged, handler => ((Action<string>)handler)(newEmotionId));
            }

            if (!sessionEnded)
            {
                InvokeSafely(StoryNodeChanged,
                    handler => ((Action<string, string>)handler)(node.Id, nextNodeId));
            }

            InvokeSafely(TurnCommitted, handler => ((Action<NarrativeTurnResult>)handler)(result));
            return result;
        }

        /// <summary>
        /// 外部先写入记忆（物品部件调查、调试面板）后补做的涌现扫描：加分、落盘、返回本次新获得的情绪。
        /// 正常 Turn 走 CommitTurn 内部的同一步，不需要调这里。
        /// </summary>
        public IReadOnlyList<string> ScanEmergenceAndApply(NarrativeSaveData saveData = null)
        {
            saveData = saveData ?? NarrativeSaveData.Get;
            List<string> emerged = FindEmergentEmotions(saveData);
            if (saveData.AddEmotionsWithoutSaving(emerged).Count > 0)
            {
                saveData.Save();
            }

            return emerged.AsReadOnly();
        }

        /// <summary>
        /// 按获得顺序返回 EmotionCardSet 含该情绪的已拥有记忆，用于过场里还原「哪两段记忆共同指认了这个情绪」。
        /// 口径与 <see cref="FindEmergentEmotions"/> 一致：一段记忆对同一情绪只算一次。
        /// </summary>
        public IReadOnlyList<string> GetMemorySourcesForEmotion(string emotionId,
            NarrativeSaveData saveData = null)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(emotionId))
            {
                return result;
            }

            saveData = saveData ?? NarrativeSaveData.Get;
            IReadOnlyList<string> ownedMemoryIds = saveData.GetMemoryIdsInAcquisitionOrder();
            for (int i = 0; i < ownedMemoryIds.Count; i++)
            {
                MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(ownedMemoryIds[i]);
                if (memory?.EmotionCardSet == null)
                {
                    continue;
                }

                if (memory.EmotionCardSet.Contains(emotionId))
                {
                    result.Add(ownedMemoryIds[i]);
                }
            }

            return result;
        }

        private static void InvokeSafely(Delegate handlers, Action<Delegate> invoke)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    invoke(handler);
                }
                catch (Exception exception)
                {
                    DLogger.Warning("Narrative presentation listener failed: {0}", exception);
                }
            }
        }

        private static void EnsureReplyBelongsToNode(StoryNodeConfig node, ReplyOptionConfig reply)
        {
            if (node.Replies == null || !node.Replies.Contains(reply.Id))
            {
                throw new InvalidOperationException($"Reply {reply.Id} does not belong to node {node.Id}.");
            }
        }

        private void EnsureEmotionAllowed(ReplyOptionConfig reply, string emotionId, NarrativeSaveData saveData)
        {
            if (IsNoneEmotionId(emotionId))
            {
                return;
            }

            if (!saveData.HasEmotion(emotionId))
            {
                throw new InvalidOperationException($"Emotion {emotionId} has not been learned.");
            }

            if (!NarrativeConfigMgr.Instance.HasEmotion(emotionId))
            {
                throw new InvalidOperationException($"Emotion definition does not exist: {emotionId}");
            }

            if (reply.AllowedEmotion != null && reply.AllowedEmotion.Count > 0 &&
                !reply.AllowedEmotion.Contains(emotionId))
            {
                throw new InvalidOperationException($"Emotion {emotionId} is not allowed by reply {reply.Id}.");
            }
        }

        private static float GetAffectionDelta(ReplyOptionConfig reply, string emotionId, out bool hasRule)
        {
            hasRule = false;
            if (reply.AffectionRules == null)
            {
                return 0f;
            }

            EmotionAffectionRule noneRule = null;
            for (int i = 0; i < reply.AffectionRules.Count; i++)
            {
                EmotionAffectionRule rule = reply.AffectionRules[i];
                if (rule == null)
                {
                    continue;
                }

                if (rule.Emotion == emotionId)
                {
                    hasRule = true;
                    return rule.AffectionDelta;
                }

                if (rule.Emotion == NarrativeRules.NoneEmotionId)
                {
                    noneRule = rule;
                }
            }

            hasRule = noneRule != null;
            return noneRule == null ? 0f : noneRule.AffectionDelta;
        }

        private static string ResolveNextNode(StoryNodeConfig node, string replyId, float affection)
        {
            ReplyOptionConfig reply = NarrativeConfigMgr.Instance.GetReply(replyId);
            if (reply != null && !string.IsNullOrWhiteSpace(reply.NextNodeId))
            {
                EnsureNextNodeExists(node, reply.NextNodeId);
                return reply.NextNodeId;
            }

            if (node.ExtraNextNodeIds != null && node.ExtraNextNodeIds.Count > 0)
            {
                var candidates = node.ExtraNextNodeIds
                    .Select((id, index) => new { Id = id, Index = index, Config = NarrativeConfigMgr.Instance.GetStoryNode(id) })
                    .Where(item => item.Config != null && affection >= item.Config.MinAffection)
                    .OrderByDescending(item => item.Config.Priority)
                    .ThenBy(item => item.Index);
                foreach (var candidate in candidates)
                {
                    EnsureNextNodeExists(node, candidate.Id);
                    return candidate.Id;
                }
            }

            if (node.Branches != null)
            {
                // 明确回复分支优先于 ANY 通用分支；同一层仍按 Luban 列表顺序（配置优先级）执行。
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < node.Branches.Count; i++)
                    {
                        BranchRule rule = node.Branches[i];
                        bool isAny = rule != null && (rule.Reply == NarrativeRules.AnyReplyId || rule.Reply == "*");
                        if (rule == null || (pass == 0 && (isAny || rule.Reply != replyId)) ||
                            (pass == 1 && !isAny))
                            continue;

                        if (affection < rule.MinAffection || affection >= rule.MaxAffection)
                            continue;

                        if (string.IsNullOrWhiteSpace(rule.NextNode))
                            throw new InvalidOperationException($"Branch in node {node.Id} has no next node.");

                        EnsureNextNodeExists(node, rule.NextNode);

                        return rule.NextNode;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(node.DefaultNextNodeId))
            {
                EnsureNextNodeExists(node, node.DefaultNextNodeId);
                return node.DefaultNextNodeId;
            }

            throw new InvalidOperationException($"No branch matched node={node.Id}, reply={replyId}, affection={affection:0.##}.");
        }

        private static void EnsureNextNodeExists(StoryNodeConfig node, string nextNodeId)
        {
            if (!NarrativeConfigMgr.Instance.HasStoryNode(nextNodeId))
            {
                throw new InvalidOperationException($"Next node does not exist: {nextNodeId} (from {node.Id}).");
            }
        }

        private void ValidateRewardMemories(IReadOnlyList<string> memoryIds)
        {
            if (memoryIds == null)
            {
                return;
            }

            for (int i = 0; i < memoryIds.Count; i++)
            {
                string memoryId = memoryIds[i];
                if (string.IsNullOrWhiteSpace(memoryId) || !NarrativeConfigMgr.Instance.HasMemory(memoryId))
                {
                    throw new InvalidOperationException($"Reward memory definition does not exist: {memoryId}");
                }
            }
        }

        /// <summary>
        /// 按情绪记忆规则激活记忆：情绪累计使用次数达到阈值，且前置记忆全部持有时，
        /// 获得规则配置的奖励记忆。任一条件不满足都不触发。
        /// 规则按 Priority 降序执行；一条规则产出的记忆可能满足另一条规则的前置，
        /// 因此循环到不再有新记忆产出为止。
        /// </summary>
        private List<string> ActivateMemoriesByEmotionRule(NarrativeSaveData saveData)
        {
            var activated = new List<string>();
            IReadOnlyList<EmotionMemoryRuleConfig> rules = NarrativeConfigMgr.Instance.AllEmotionMemoryRules;
            if (rules == null || rules.Count == 0)
            {
                return activated;
            }

            List<EmotionMemoryRuleConfig> ordered = rules
                .Where(rule => rule != null && !string.IsNullOrWhiteSpace(rule.EmotionId))
                .OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .ToList();

            // 防止规则互相引用导致死循环；正常配置远小于该上限。
            int guard = ordered.Count + 1;
            bool progressed = true;
            while (progressed && guard-- > 0)
            {
                progressed = false;
                foreach (EmotionMemoryRuleConfig rule in ordered)
                {
                    if (saveData.GetEmotionUseCount(rule.EmotionId) < rule.Threshold)
                    {
                        continue;
                    }

                    if (!NarrativeRules.HasAllMemories(rule.RequiredMemoryIds, saveData.HasMemory))
                    {
                        continue;
                    }

                    int triggered = saveData.GetRuleTriggerCount(rule.Id);
                    if (rule.MaxTriggerCount > 0 && triggered >= rule.MaxTriggerCount)
                    {
                        continue;
                    }

                    // OncePerRun / OncePerDay 在本实现里都只触发一次；跨天重置于当前版本没有调用点。
                    if (!string.Equals(rule.RepeatPolicy, "Repeatable", StringComparison.OrdinalIgnoreCase) &&
                        triggered > 0)
                    {
                        continue;
                    }

                    List<string> granted = saveData.AddMemoriesWithoutSaving(rule.RewardMemoryIds);
                    if (granted.Count == 0)
                    {
                        continue;
                    }

                    saveData.MarkRuleTriggeredWithoutSaving(rule.Id);
                    activated.AddRange(granted);
                    progressed = true;
                }
            }

            return activated;
        }

        private List<string> FindEmergentEmotions(NarrativeSaveData saveData)
        {
            var sourcesByEmotion = new Dictionary<string, int>();
            IReadOnlyList<string> ownedMemoryIds = saveData.GetMemoryIdsInStableOrder();
            for (int i = 0; i < ownedMemoryIds.Count; i++)
            {
                MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(ownedMemoryIds[i]);
                if (memory == null || memory.EmotionCardSet == null)
                {
                    continue;
                }

                // 一段记忆对同一种情绪最多贡献一次。
                foreach (string emotionId in memory.EmotionCardSet.Distinct())
                {
                    if (string.IsNullOrWhiteSpace(emotionId))
                    {
                        continue;
                    }

                    sourcesByEmotion.TryGetValue(emotionId, out int count);
                    sourcesByEmotion[emotionId] = count + 1;
                }
            }

            return sourcesByEmotion
                .Where(pair => pair.Value >= 2 && !saveData.HasEmotion(pair.Key) &&
                    NarrativeConfigMgr.Instance.HasEmotion(pair.Key))
                .Select(pair => pair.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }
    }
}
