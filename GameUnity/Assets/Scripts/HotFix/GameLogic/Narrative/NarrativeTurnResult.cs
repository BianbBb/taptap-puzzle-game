using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>一次剧情回复结算的完整、可追踪结果。</summary>
    public sealed class NarrativeTurnResult
    {
        public string SessionId { get; internal set; }
        public string NodeId { get; internal set; }
        public string ReplyId { get; internal set; }
        public string SelectedEmotionId { get; internal set; }
        public float AffectionBefore { get; internal set; }
        public float AffectionDelta { get; internal set; }
        public float AffectionAfter { get; internal set; }
        public string NextNodeId { get; internal set; }
        public IReadOnlyList<string> RewardMemoryIds { get; internal set; }
        public IReadOnlyList<string> NewlyEmergedEmotionIds { get; internal set; }
        public bool SessionEnded { get; internal set; }

        public override string ToString()
            => $"Day turn: session={SessionId}, node={NodeId}, reply={ReplyId}, " +
               $"emotion={SelectedEmotionId}, affection={AffectionBefore:0.##}->{AffectionAfter:0.##} " +
               $"({AffectionDelta:+0.##;-0.##;0}), next={NextNodeId}, " +
               $"memory=[{string.Join(",", RewardMemoryIds ?? new string[0])}], " +
               $"emergence=[{string.Join(",", NewlyEmergedEmotionIds ?? new string[0])}], ended={SessionEnded}";
    }
}
