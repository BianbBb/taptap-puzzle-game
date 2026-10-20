using System;
using System.Collections.Generic;
using System.Linq;
using GameProto;

namespace GameLogic
{
    public enum ENarrativeValidationSeverity
    {
        Warning,
        Error,
    }

    public sealed class NarrativeValidationIssue
    {
        public ENarrativeValidationSeverity Severity { get; }
        public string Code { get; }
        public string ConfigId { get; }
        public string Message { get; }

        public NarrativeValidationIssue(ENarrativeValidationSeverity severity, string code,
            string configId, string message)
        {
            Severity = severity;
            Code = code;
            ConfigId = configId;
            Message = message;
        }

        public override string ToString() => $"[{Severity}][{Code}] {ConfigId}: {Message}";
    }

    /// <summary>对当前加载的 Luban 叙事表做引用和推进性检查。</summary>
    public static class NarrativeConfigValidator
    {
        public static IReadOnlyList<NarrativeValidationIssue> Validate(
            NarrativeSaveData saveData = null)
        {
            saveData = saveData ?? NarrativeSaveData.Get;
            NarrativeConfigMgr config = NarrativeConfigMgr.Instance;
            var issues = new List<NarrativeValidationIssue>();
            var usedEmotions = new HashSet<string>();
            var usedMemories = new HashSet<string>();

            ValidateUniqueIds(config.AllEmotions, item => item.Id, "EMOTION_ID_DUPLICATE", issues);
            ValidateUniqueIds(config.AllMemories, item => item.Id, "MEMORY_ID_DUPLICATE", issues);
            ValidateUniqueIds(config.AllReplies, item => item.Id, "REPLY_ID_DUPLICATE", issues);
            ValidateUniqueIds(config.AllStoryNodes, item => item.Id, "NODE_ID_DUPLICATE", issues);
            ValidateUniqueIds(config.AllSessions, item => item.Id, "SESSION_ID_DUPLICATE", issues);
            ValidateUniqueIds(config.AllActions, item => item.Id, "ACTION_ID_DUPLICATE", issues);
            ValidateUniqueIds(config.AllNarrativeItems, item => item.Id, "ITEM_ID_DUPLICATE", issues);
            ValidateUniqueIds(config.AllNarrativeItemParts, item => item.Id, "ITEM_PART_ID_DUPLICATE", issues);

            foreach (NarrativeItemConfig item in config.AllNarrativeItems)
            {
                if (item.PartIds == null) continue;
                foreach (string partId in item.PartIds)
                    if (!config.HasNarrativeItemPart(partId))
                        AddError(issues, "ITEM_PART_MISSING", item.Id, $"references unknown item part {partId}.");
            }

            foreach (NarrativeItemPartConfig part in config.AllNarrativeItemParts)
            {
				if (part.HotspotX < 0f || part.HotspotX > 1f || part.HotspotY < 0f || part.HotspotY > 1f)
					AddError(issues, "ITEM_PART_HOTSPOT_OUT_OF_RANGE", part.Id,
						$"hotspot coordinates must be within 0..1; received ({part.HotspotX}, {part.HotspotY}).");

                if (!string.IsNullOrWhiteSpace(part.MemoryId))
                {
                    usedMemories.Add(part.MemoryId);
                    if (!config.HasMemory(part.MemoryId))
                        AddError(issues, "ITEM_PART_MEMORY_MISSING", part.Id, $"references unknown memory {part.MemoryId}.");
                }
            }

            var grantedItems = new HashSet<string>(StringComparer.Ordinal);
            var removedItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (NarrativeActionConfig action in config.AllActions)
            {
                bool isItemAction = string.Equals(action.ActionType, "GrantItem", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(action.ActionType, "RemoveItem", StringComparison.OrdinalIgnoreCase);
                bool isEmotionAction = string.Equals(action.ActionType, "GrantEmotion", StringComparison.OrdinalIgnoreCase);

                if (!isItemAction && !isEmotionAction)
                {
                    AddError(issues, "ACTION_TYPE_UNSUPPORTED", action.Id, $"unsupported action type {action.ActionType}.");
                    continue;
                }

                // GrantEmotion 的 TargetId 指的是情绪 ID，不走物品校验。
                if (isItemAction && !config.HasNarrativeItem(action.TargetId))
                    AddError(issues, "ACTION_ITEM_MISSING", action.Id, $"references unknown item {action.TargetId}.");

                if (isItemAction)
                {
                    if (string.Equals(action.ActionType, "GrantItem", StringComparison.OrdinalIgnoreCase))
                        grantedItems.Add(action.TargetId);
                    else
                        removedItems.Add(action.TargetId);
                }

                if (isEmotionAction)
                {
                    usedEmotions.Add(action.TargetId);
                    if (!config.HasEmotion(action.TargetId))
                        AddError(issues, "ACTION_EMOTION_MISSING", action.Id,
                            $"references unknown emotion {action.TargetId}.");
                }
            }

            // 物品栏容量 1：只发放却从不回收的物品会永久占着槽位，下一次 GrantItem 直接把它顶掉，
            // 玩家看不到任何提示 —— 所以配表层面就该成对（现有剧情都是同节点 OnEnter/OnExit 配对）。
            foreach (string grantedItemId in grantedItems)
            {
                if (!removedItems.Contains(grantedItemId))
                {
                    AddWarning(issues, "ITEM_GRANT_WITHOUT_REMOVE", grantedItemId,
                        "item is granted by an action but never removed; the single-slot bag will silently replace it on the next GrantItem.");
                }
            }

            foreach (NarrativeSessionConfig session in config.AllSessions)
            {
                if (!config.HasStoryNode(session.StartNodeId))
                {
                    AddError(issues, "SESSION_START_MISSING", session.Id,
                        $"references unknown start node {session.StartNodeId}.");
                }

                if (!string.IsNullOrWhiteSpace(session.NextSessionId) && !config.HasSession(session.NextSessionId))
                {
                    AddError(issues, "SESSION_NEXT_MISSING", session.Id,
                        $"references unknown next session {session.NextSessionId}.");
                }
            }

            foreach (MemoryConfig memory in config.AllMemories)
            {
                if (memory.EmotionCardSet == null)
                {
                    continue;
                }

                foreach (string emotionId in memory.EmotionCardSet)
                {
                    usedEmotions.Add(emotionId);
                    if (!config.HasEmotion(emotionId))
                    {
                        AddError(issues, "MEMORY_EMOTION_MISSING", memory.Id,
                            $"references unknown emotion {emotionId}.");
                    }
                }
            }

            foreach (ReplyOptionConfig reply in config.AllReplies)
            {
                ValidateMemoryReferences(reply.Id, "required", reply.RequiredMemory, config, usedMemories, issues);
                ValidateMemoryReferences(reply.Id, "reward", reply.RewardMemory, config, usedMemories, issues);

                if (reply.AllowedEmotion != null)
                {
                    foreach (string emotionId in reply.AllowedEmotion)
                    {
                        usedEmotions.Add(emotionId);
                        if (!config.HasEmotion(emotionId))
                        {
                            AddError(issues, "REPLY_EMOTION_MISSING", reply.Id,
                                $"allows unknown emotion {emotionId}.");
                        }
                    }
                }

                if (reply.AffectionRules != null)
                {
                    foreach (EmotionAffectionRule rule in reply.AffectionRules)
                    {
                        if (rule != null && rule.Emotion != NarrativeRules.NoneEmotionId)
                        {
                            usedEmotions.Add(rule.Emotion);
                            if (!config.HasEmotion(rule.Emotion))
                            {
                                AddError(issues, "AFFECTION_EMOTION_MISSING", reply.Id,
                                    $"affection rule references unknown emotion {rule.Emotion}.");
                            }
                        }
                    }
                }

                if (reply.RewardMemory != null && reply.RewardMemory.Count != reply.RewardMemory.Distinct().Count())
                {
                    AddWarning(issues, "REWARD_MEMORY_DUPLICATE", reply.Id,
                        "reward list contains duplicate memory IDs; runtime will deduplicate them.");
                }

                ValidateActionReferences(reply.Id, reply.ActionIds, config, issues);

                if (!string.IsNullOrWhiteSpace(reply.NextNodeId) && !config.HasStoryNode(reply.NextNodeId))
                {
                    AddError(issues, "REPLY_NEXT_NODE_MISSING", reply.Id,
                        $"references unknown next node {reply.NextNodeId}.");
                }
            }

            foreach (StoryNodeConfig node in config.AllStoryNodes)
            {
                ValidateActionReferences(node.Id, node.OnEnterActionIds, config, issues);
                ValidateActionReferences(node.Id, node.OnExitActionIds, config, issues);
                if (node.ExtraNextNodeIds != null)
                    foreach (string nextId in node.ExtraNextNodeIds)
                        if (!config.HasStoryNode(nextId))
                            AddError(issues, "NODE_EXTRA_NEXT_MISSING", node.Id, $"references unknown extra node {nextId}.");

                var unlockedCount = 0;
                var memorylessCount = 0;
                var replyCount = 0;
                if (node.Replies != null)
                {
                    foreach (string replyId in node.Replies)
                    {
                        ReplyOptionConfig reply = config.GetReply(replyId);
                        if (reply == null)
                        {
                            AddError(issues, "NODE_REPLY_MISSING", node.Id,
                                $"references unknown reply {replyId}.");
                            continue;
                        }

                        replyCount++;

                        if (reply.RequiredMemory == null || reply.RequiredMemory.Count == 0)
                        {
                            memorylessCount++;
                        }

                        if (NarrativeRules.HasAllMemories(reply.RequiredMemory, saveData.HasMemory))
                        {
                            unlockedCount++;
                        }
                    }
                }

                // 无回复选项的节点（自动推进 / Session 收尾）走 DefaultNextNodeId，
                // 不存在“选项全锁”的问题，跳过两条检查避免淹没真正的配置错误。
                if (replyCount > 0)
                {
                    if (unlockedCount == 0)
                    {
                        // 新档当前状态可能尚未持有前置 Memory；仍需允许 UI 展示 Locked 回复。
                        AddWarning(issues, "NODE_ALL_REPLIES_LOCKED", node.Id,
                            "all replies are locked in the current memory state.");
                    }

                    if (memorylessCount == 0 && !GrantsItemOnEnter(node, config))
                    {
                        AddWarning(issues, "NODE_NO_MEMORYLESS_REPLY", node.Id,
                            "the node has no reply that can be selected before earning memories.");
                    }
                }

                if (!node.IsSessionEnd && string.IsNullOrWhiteSpace(node.DefaultNextNodeId) &&
                    (node.Branches == null || node.Branches.Count == 0) &&
                    (node.Replies == null || node.Replies.Count == 0))
                {
                    AddError(issues, "NODE_NO_ROUTE", node.Id,
                        "a non-ending node has no reply or route to a next node.");
                }

                if (!string.IsNullOrWhiteSpace(node.DefaultNextNodeId) && !config.HasStoryNode(node.DefaultNextNodeId))
                {
                    AddError(issues, "NODE_DEFAULT_NEXT_MISSING", node.Id,
                        $"references unknown default next node {node.DefaultNextNodeId}.");
                }

                if (node.MinAffection > 0 && string.IsNullOrWhiteSpace(node.DefaultNextNodeId))
                {
                    AddError(issues, "NODE_AFFECTION_FALLBACK_MISSING", node.Id,
                        "an affection-gated node needs a default next node for the below-threshold route.");
                }

                if (node.Branches != null)
                {
                    foreach (BranchRule branch in node.Branches)
                    {
                        if (branch == null)
                        {
                            continue;
                        }

                        if (branch.Reply != NarrativeRules.AnyReplyId && branch.Reply != "*" &&
                            !config.HasReply(branch.Reply))
                        {
                            AddError(issues, "BRANCH_REPLY_MISSING", node.Id,
                                $"references unknown reply {branch.Reply}.");
                        }

                        if (!config.HasStoryNode(branch.NextNode))
                        {
                            AddError(issues, "BRANCH_NODE_MISSING", node.Id,
                                $"references unknown next node {branch.NextNode}.");
                        }

                        if (branch.MinAffection >= branch.MaxAffection)
                        {
                            AddError(issues, "BRANCH_RANGE_EMPTY", node.Id,
                                $"branch range [{branch.MinAffection}, {branch.MaxAffection}) is empty.");
                        }

                        if (branch.NextNode == node.Id)
                        {
                            AddWarning(issues, "BRANCH_SELF_LOOP", node.Id,
                                "branch returns to the same node; verify an exit path exists.");
                        }
                    }
                }
            }

            foreach (EmotionConfig emotion in config.AllEmotions)
            {
                // E00 / NONE 是“无情绪”占位行，本来就不该被任何记忆或选项引用。
                if (NarrativeRules.IsNoneEmotion(emotion.Id))
                {
                    continue;
                }

                if (!usedEmotions.Contains(emotion.Id))
                {
                    AddWarning(issues, "EMOTION_UNUSED", emotion.Id,
                        "emotion is not referenced by any memory or reply.");
                }
            }

            foreach (MemoryConfig memory in config.AllMemories)
            {
                if (!usedMemories.Contains(memory.Id))
                {
                    AddWarning(issues, "MEMORY_UNUSED", memory.Id,
                        "memory is not required or rewarded by any reply.");
                }
            }

            return issues;
        }

        /// <summary>
        /// 节点进场就发物品时，玩家可以在本节点拆解物品拿到前置记忆，
        /// 因此“没有免记忆选项”属于设计意图，不算推进性风险。
        /// </summary>
        private static bool GrantsItemOnEnter(StoryNodeConfig node, NarrativeConfigMgr config)
        {
            if (node.OnEnterActionIds == null)
            {
                return false;
            }

            foreach (string actionId in node.OnEnterActionIds)
            {
                NarrativeActionConfig action = config.GetAction(actionId);
                if (action == null)
                {
                    continue;
                }

                if (string.Equals(action.ActionType, "GrantItem", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateMemoryReferences(string configId, string kind,
            IReadOnlyList<string> memoryIds, NarrativeConfigMgr config, ISet<string> usedMemoryIds,
            List<NarrativeValidationIssue> issues)
        {
            if (memoryIds == null)
            {
                return;
            }

            foreach (string memoryId in memoryIds)
            {
                usedMemoryIds.Add(memoryId);
                if (!config.HasMemory(memoryId))
                {
                    AddError(issues, "REPLY_MEMORY_MISSING", configId,
                        $"{kind} list references unknown memory {memoryId}.");
                }
            }
        }

        private static void ValidateActionReferences(string configId, IReadOnlyList<string> actionIds,
            NarrativeConfigMgr config, List<NarrativeValidationIssue> issues)
        {
            if (actionIds == null) return;
            foreach (string actionId in actionIds)
                if (!config.HasAction(actionId))
                    AddError(issues, "ACTION_MISSING", configId, $"references unknown action {actionId}.");
        }

        private static void ValidateUniqueIds<T>(IReadOnlyList<T> definitions,
            Func<T, string> getId, string code, List<NarrativeValidationIssue> issues)
        {
            var seen = new HashSet<string>();
            foreach (T definition in definitions)
            {
                string id = getId(definition);
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    AddError(issues, code, id ?? string.Empty,
                        string.IsNullOrWhiteSpace(id) ? "definition has an empty ID." : "ID is duplicated.");
                }
            }
        }

        private static void AddError(List<NarrativeValidationIssue> issues, string code,
            string configId, string message)
            => issues.Add(new NarrativeValidationIssue(ENarrativeValidationSeverity.Error,
                code, configId, message));

        private static void AddWarning(List<NarrativeValidationIssue> issues, string code,
            string configId, string message)
            => issues.Add(new NarrativeValidationIssue(ENarrativeValidationSeverity.Warning,
                code, configId, message));
    }
}
