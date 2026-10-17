using System.Collections.Generic;
using GameProto;

namespace GameLogic
{
    /// <summary>
    /// 叙事配置访问封装。
    /// </summary>
    public class NarrativeConfigMgr : Singleton<NarrativeConfigMgr>
    {
        private static Tables Tables => ConfigSystem.Instance.Tables;

        public EmotionConfig GetEmotion(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbEmotionConfig.GetOrDefaultNoStatic(id);

        public MemoryConfig GetMemory(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbMemoryConfig.GetOrDefaultNoStatic(id);

        public ReplyOptionConfig GetReply(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbReplyOptionConfig.GetOrDefaultNoStatic(id);

        public StoryNodeConfig GetStoryNode(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbStoryNodeConfig.GetOrDefaultNoStatic(id);

        public NarrativeSessionConfig GetSession(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbNarrativeSessionConfig.GetOrDefaultNoStatic(id);

        public NarrativeActionConfig GetAction(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbNarrativeActionConfig.GetOrDefaultNoStatic(id);

        public NarrativeItemConfig GetNarrativeItem(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbNarrativeItemConfig.GetOrDefaultNoStatic(id);

        public NarrativeItemPartConfig GetNarrativeItemPart(string id)
            => string.IsNullOrWhiteSpace(id) ? null : Tables.TbNarrativeItemPartConfig.GetOrDefaultNoStatic(id);

        public bool HasEmotion(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbEmotionConfig.dataMap.ContainsKey(id);

        public bool HasMemory(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbMemoryConfig.dataMap.ContainsKey(id);

        public bool HasReply(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbReplyOptionConfig.dataMap.ContainsKey(id);

        public bool HasStoryNode(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbStoryNodeConfig.dataMap.ContainsKey(id);

        public bool HasSession(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbNarrativeSessionConfig.dataMap.ContainsKey(id);

        public bool HasAction(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbNarrativeActionConfig.dataMap.ContainsKey(id);

        public bool HasNarrativeItem(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbNarrativeItemConfig.dataMap.ContainsKey(id);

        public bool HasNarrativeItemPart(string id)
            => !string.IsNullOrWhiteSpace(id) && Tables.TbNarrativeItemPartConfig.dataMap.ContainsKey(id);

        public IReadOnlyList<EmotionConfig> AllEmotions => Tables.TbEmotionConfig.dataList;

        public IReadOnlyList<MemoryConfig> AllMemories => Tables.TbMemoryConfig.dataList;

        public IReadOnlyList<ReplyOptionConfig> AllReplies => Tables.TbReplyOptionConfig.dataList;

        public IReadOnlyList<StoryNodeConfig> AllStoryNodes => Tables.TbStoryNodeConfig.dataList;

        public IReadOnlyList<NarrativeSessionConfig> AllSessions => Tables.TbNarrativeSessionConfig.dataList;

        public IReadOnlyList<NarrativeActionConfig> AllActions => Tables.TbNarrativeActionConfig.dataList;

        public IReadOnlyList<NarrativeItemConfig> AllNarrativeItems => Tables.TbNarrativeItemConfig.dataList;

        public IReadOnlyList<NarrativeItemPartConfig> AllNarrativeItemParts => Tables.TbNarrativeItemPartConfig.dataList;

        public int EmotionCount => Tables.TbEmotionConfig.dataList.Count;

        public int MemoryCount => Tables.TbMemoryConfig.dataList.Count;

        public int ReplyCount => Tables.TbReplyOptionConfig.dataList.Count;

        public int StoryNodeCount => Tables.TbStoryNodeConfig.dataList.Count;
    }
}
