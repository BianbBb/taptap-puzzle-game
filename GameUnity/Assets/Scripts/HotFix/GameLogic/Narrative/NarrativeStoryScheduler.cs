using System;
using System.Linq;
using GameProto;

namespace GameLogic
{
    /// <summary>按游戏内日期、完成状态、后续 Session 链和优先级挑选剧情。</summary>
    public sealed class NarrativeStoryScheduler
    {
        public NarrativeSessionConfig FindNextSession(NarrativeSaveData saveData)
        {
            if (saveData == null)
            {
                throw new ArgumentNullException(nameof(saveData));
            }

            NarrativeSessionConfig candidate = GetNextConfiguredSession(saveData);
            return IsEligible(candidate, saveData) ? candidate : null;
        }

        public int FindNextScheduledDay(NarrativeSaveData saveData)
        {
            if (saveData == null)
            {
                throw new ArgumentNullException(nameof(saveData));
            }

            NarrativeSessionConfig candidate = GetNextConfiguredSession(saveData);
            return candidate != null && !saveData.HasCompletedSession(candidate.Id) &&
                candidate.DayIndex > saveData.CurrentDay ? candidate.DayIndex : 0;
        }

        public bool HasUncompletedSessions(NarrativeSaveData saveData)
            => saveData != null && NarrativeConfigMgr.Instance.AllSessions
                .Any(item => item != null && !saveData.HasCompletedSession(item.Id));

        private static bool IsEligible(NarrativeSessionConfig session, NarrativeSaveData saveData)
            => session != null && session.DayIndex <= saveData.CurrentDay &&
                !saveData.HasCompletedSession(session.Id);

        private static NarrativeSessionConfig GetNextConfiguredSession(NarrativeSaveData saveData)
        {
            NarrativeConfigMgr config = NarrativeConfigMgr.Instance;
            if (!string.IsNullOrWhiteSpace(saveData.LastCompletedSessionId))
            {
                NarrativeSessionConfig completed = config.GetSession(saveData.LastCompletedSessionId);
                if (completed == null || string.IsNullOrWhiteSpace(completed.NextSessionId))
                {
                    return null;
                }

                return config.GetSession(completed.NextSessionId);
            }

            // The current schema has no explicit entry-session field. Select an initial
            // session only for a genuinely new save; later sessions must follow NextSessionId.
            if (saveData.CompletedSessionIds.Count > 0)
            {
                return null;
            }

            return config.AllSessions
                .Where(item => item != null)
                .OrderBy(item => item.DayIndex)
                .ThenByDescending(item => item.Priority)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }
}
