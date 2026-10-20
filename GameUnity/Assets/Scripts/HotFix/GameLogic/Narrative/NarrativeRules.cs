using System;
using System.Collections.Generic;

namespace GameLogic
{
    public enum NarrativeDayPhase
    {
        Active = 0,
        Monologue = 1,
        MemoryCleanup = 2,
        Transition = 3,
        GameComplete = 4,
        PrototypeContentExhausted = 5,
    }

    public static class NarrativeRules
    {
        public const string NoneEmotionId = "NONE";
        /// <summary>配表里"无情绪"那一行的 ID，不参与图鉴展示与表达计数。</summary>
        public const string NoneEmotionConfigId = "E00";
        public const string AnyReplyId = "ANY";
        public const int MemoryCapacity = 5;
        public const float MinimumAffection = 0f;
        public const float MaximumAffection = 100f;

        public static bool HasAllMemories(IReadOnlyList<string> requiredMemoryIds,
            Func<string, bool> hasMemory)
        {
            if (hasMemory == null)
            {
                throw new ArgumentNullException(nameof(hasMemory));
            }

            if (requiredMemoryIds == null)
            {
                return true;
            }

            for (int i = 0; i < requiredMemoryIds.Count; i++)
            {
                if (!hasMemory(requiredMemoryIds[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static float ClampAffection(float value)
            => Math.Max(MinimumAffection, Math.Min(MaximumAffection, value));
    }
}
