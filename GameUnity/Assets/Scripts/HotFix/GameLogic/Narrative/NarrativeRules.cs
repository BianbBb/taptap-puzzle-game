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
        /// <summary>
        /// 玩家自己的说话人 ID。主角就是那台评估机器，配表里统一写 AI，
        /// 气泡靠它分边（AI 走玩家侧，其余走 NPC 侧）。
        /// </summary>
        public const string PlayerSpeakerId = "AI";
        public const int MemoryCapacity = 5;

        /// <summary>
        /// 物品栏容量。玩家扮演的是"上门评估的顾问"，手上一次只拿一件待评估的物件，
        /// 交给物主或判断完毕后立刻还回去，所以不放行囊、不攒收集品。
        /// 只有 GrantItem 动作会占用这个槽位；场景热点、部件查看这类"只是看一眼"的交互不占。
        /// </summary>
        public const int NarrativeItemCapacity = 1;
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

        /// <summary>是否是「无情绪」哨兵值（运行时占位 NONE，或配表里的 E00 行）。</summary>
        public static bool IsNoneEmotion(string emotionId)
        {
            return string.Equals(emotionId, NoneEmotionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(emotionId, NoneEmotionConfigId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
