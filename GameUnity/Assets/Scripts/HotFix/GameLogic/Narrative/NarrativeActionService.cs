using System;
using System.Collections.Generic;
using GameProto;

namespace GameLogic
{
    /// <summary>执行配置白名单内的叙事物品动作，并用存档键保证重载幂等。</summary>
    public sealed class NarrativeActionService
    {
        public bool Execute(IReadOnlyList<string> actionIds, string executionKey,
            NarrativeSaveData saveData = null, bool saveImmediately = true)
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

                bool supported = string.Equals(action.ActionType, "GrantItem", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(action.ActionType, "RemoveItem", StringComparison.OrdinalIgnoreCase);
                if (!supported)
                    throw new InvalidOperationException($"Unsupported narrative action type: {action.ActionType}");
                if (!config.HasNarrativeItem(action.TargetId))
                    throw new InvalidOperationException($"Narrative action {id} references unknown item {action.TargetId}.");
                actions.Add(action);
            }

            foreach (NarrativeActionConfig action in actions)
                saveData.ApplyNarrativeItemActionWithoutSaving(action.ActionType, action.TargetId);

            saveData.MarkActionKeyWithoutSaving(executionKey);
            if (saveImmediately)
                saveData.Save();
            return true;
        }
    }
}
