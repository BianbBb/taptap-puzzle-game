using System;
using System.Collections.Generic;
using GameProto;

namespace GameLogic
{
    /// <summary>
    /// 执行配置白名单内的叙事动作（物品增删 / 记忆授予 / 情绪授予），并用存档键保证重载幂等。
    /// </summary>
    public sealed class NarrativeActionService
    {
        private const string GrantItemAction = "GrantItem";
        private const string RemoveItemAction = "RemoveItem";
        private const string GrantEmotionAction = "GrantEmotion";
        private const string GrantMemoryAction = "GrantMemory";

        /// <summary>
        /// <paramref name="grantedEmotions"/> 不为 null 时，把本次**新授予**的情绪 ID 追加进去。
        /// 情绪只写内存态（不落盘），落盘与演出时机由调用方决定：
        /// CommitTurn 会把它并进涌现列表走 <c>EmotionEmerged</c>；UI 路径则自己触发过场。
        ///
        /// <paramref name="grantedItems"/> 同理，收集本次执行到的 <c>GrantItem</c> 目标物品 ID。
        /// 物品是**立刻写进存档**的，这个列表只给 UI 判断「要不要播入库演出」用；
        /// 同一批动作里多次授予只保留顺序，容量 1 的语义由 SaveData 负责（后一个顶掉前一个）。
        ///
        /// <paramref name="grantedMemories"/> 收集本次**新增**的记忆 ID。
        /// 记忆一授予就落盘，但它可能凑出新的情绪涌现 —— 调用方拿到这个列表后
        /// 要自己补一次 <c>ScanEmergenceAndApply</c>，否则情绪不会出现（见 ExecuteNodeExitActions）。
        /// </summary>
        public bool Execute(IReadOnlyList<string> actionIds, string executionKey,
            NarrativeSaveData saveData = null, bool saveImmediately = true,
            List<string> grantedEmotions = null, List<string> grantedItems = null,
            List<string> grantedMemories = null)
        {
            if (actionIds == null || actionIds.Count == 0)
                return false;

            saveData = saveData ?? NarrativeSaveData.Get;
            if (saveData.HasExecutedActionKey(executionKey))
                return false;

            NarrativeConfigMgr config = NarrativeConfigMgr.Instance;
            var actions = new List<NarrativeActionConfig>(actionIds.Count);
            for (int i = 0; i < actionIds.Count; i++)
            {
                string id = actionIds[i];
                NarrativeActionConfig action = config.GetAction(id);
                if (action == null)
                    throw new InvalidOperationException($"Narrative action does not exist: {id}");

                bool isItemAction = IsType(action.ActionType, GrantItemAction) ||
                    IsType(action.ActionType, RemoveItemAction);
                bool isEmotionAction = IsType(action.ActionType, GrantEmotionAction);
                bool isMemoryAction = IsType(action.ActionType, GrantMemoryAction);
                if (!isItemAction && !isEmotionAction && !isMemoryAction)
                    throw new InvalidOperationException($"Unsupported narrative action type: {action.ActionType}");

                // GrantEmotion / GrantMemory 的 TargetId 分别是情绪 ID / 记忆 ID，校验要分开走。
                if (isItemAction && !config.HasNarrativeItem(action.TargetId))
                    throw new InvalidOperationException($"Narrative action {id} references unknown item {action.TargetId}.");
                if (isEmotionAction && !config.HasEmotion(action.TargetId))
                    throw new InvalidOperationException($"Narrative action {id} references unknown emotion {action.TargetId}.");
                if (isMemoryAction && !config.HasMemory(action.TargetId))
                    throw new InvalidOperationException($"Narrative action {id} references unknown memory {action.TargetId}.");

                actions.Add(action);
            }

            foreach (NarrativeActionConfig action in actions)
            {
                if (IsType(action.ActionType, GrantEmotionAction))
                {
                    List<string> added = saveData.AddEmotionsWithoutSaving(new[] { action.TargetId });
                    if (added.Count > 0)
                        grantedEmotions?.Add(added[0]);
                    continue;
                }

                if (IsType(action.ActionType, GrantMemoryAction))
                {
                    // 已经持有就不算新增：GetNarrativeItemIdsInStableOrder 那条链上的调用方靠这个判断要不要刷 UI。
                    List<string> added = saveData.AddMemoriesWithoutSaving(new[] { action.TargetId });
                    if (added.Count > 0)
                        grantedMemories?.Add(added[0]);
                    continue;
                }

                saveData.ApplyNarrativeItemActionWithoutSaving(action.ActionType, action.TargetId);
                if (grantedItems != null && IsType(action.ActionType, GrantItemAction))
                    grantedItems.Add(action.TargetId);
            }

            saveData.MarkActionKeyWithoutSaving(executionKey);
            if (saveImmediately)
                saveData.Save();
            return true;
        }

        private static bool IsType(string actionType, string expected)
            => string.Equals(actionType, expected, StringComparison.OrdinalIgnoreCase);
    }
}
