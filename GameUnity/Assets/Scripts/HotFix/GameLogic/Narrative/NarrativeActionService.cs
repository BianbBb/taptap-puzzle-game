using System;
using System.Collections.Generic;
using GameProto;

namespace GameLogic
{
    /// <summary>
    /// 执行配置白名单内的叙事动作（物品增删 / 情绪授予），并用存档键保证重载幂等。
    /// </summary>
    public sealed class NarrativeActionService
    {
        private const string GrantItemAction = "GrantItem";
        private const string RemoveItemAction = "RemoveItem";
        private const string GrantEmotionAction = "GrantEmotion";

        /// <summary>
        /// <paramref name="grantedEmotions"/> 不为 null 时，把本次**新授予**的情绪 ID 追加进去。
        /// 情绪只写内存态（不落盘），落盘与演出时机由调用方决定：
        /// CommitTurn 会把它并进涌现列表走 <c>EmotionEmerged</c>；UI 路径则自己触发过场。
        ///
        /// <paramref name="grantedItems"/> 同理，收集本次执行到的 <c>GrantItem</c> 目标物品 ID。
        /// 物品是**立刻写进存档**的，这个列表只给 UI 判断「要不要播入库演出」用；
        /// 同一批动作里多次授予只保留顺序，容量 1 的语义由 SaveData 负责（后一个顶掉前一个）。
        /// </summary>
        public bool Execute(IReadOnlyList<string> actionIds, string executionKey,
            NarrativeSaveData saveData = null, bool saveImmediately = true,
            List<string> grantedEmotions = null, List<string> grantedItems = null)
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
                if (!isItemAction && !isEmotionAction)
                    throw new InvalidOperationException($"Unsupported narrative action type: {action.ActionType}");

                // GrantEmotion 的 TargetId 是情绪 ID，不是物品 ID，校验要分开走。
                if (isItemAction && !config.HasNarrativeItem(action.TargetId))
                    throw new InvalidOperationException($"Narrative action {id} references unknown item {action.TargetId}.");
                if (isEmotionAction && !config.HasEmotion(action.TargetId))
                    throw new InvalidOperationException($"Narrative action {id} references unknown emotion {action.TargetId}.");

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
