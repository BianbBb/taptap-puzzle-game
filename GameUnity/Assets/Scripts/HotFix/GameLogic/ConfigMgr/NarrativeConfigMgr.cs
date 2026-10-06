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
            => Tables.TbEmotionConfig.GetOrDefaultNoStatic(id);

        public MemoryConfig GetMemory(string id)
            => Tables.TbMemoryConfig.GetOrDefaultNoStatic(id);

        public ReplyOptionConfig GetReply(string id)
            => Tables.TbReplyOptionConfig.GetOrDefaultNoStatic(id);

        public StoryNodeConfig GetStoryNode(string id)
            => Tables.TbStoryNodeConfig.GetOrDefaultNoStatic(id);

        public bool HasEmotion(string id) => Tables.TbEmotionConfig.dataMap.ContainsKey(id);

        public bool HasMemory(string id) => Tables.TbMemoryConfig.dataMap.ContainsKey(id);

        public bool HasReply(string id) => Tables.TbReplyOptionConfig.dataMap.ContainsKey(id);

        public bool HasStoryNode(string id) => Tables.TbStoryNodeConfig.dataMap.ContainsKey(id);

        public IReadOnlyList<EmotionConfig> AllEmotions => Tables.TbEmotionConfig.dataList;

        public IReadOnlyList<MemoryConfig> AllMemories => Tables.TbMemoryConfig.dataList;

        public IReadOnlyList<ReplyOptionConfig> AllReplies => Tables.TbReplyOptionConfig.dataList;

        public IReadOnlyList<StoryNodeConfig> AllStoryNodes => Tables.TbStoryNodeConfig.dataList;

        public int EmotionCount => Tables.TbEmotionConfig.dataList.Count;

        public int MemoryCount => Tables.TbMemoryConfig.dataList.Count;

        public int ReplyCount => Tables.TbReplyOptionConfig.dataList.Count;

        public int StoryNodeCount => Tables.TbStoryNodeConfig.dataList.Count;
    }
}
