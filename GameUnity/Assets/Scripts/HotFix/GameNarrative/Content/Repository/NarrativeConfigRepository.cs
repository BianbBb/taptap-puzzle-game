using System.Collections.Generic;
using DGame;
using GameProto;

namespace GameNarrative
{
    /// <summary>
    /// 叙事配置访问层。
    /// 统一负责读取叙事相关配置，不包含任何玩法逻辑。
    /// </summary>
    public class NarrativeConfigRepository
    {
        private static NarrativeConfigRepository m_instance;

        public static NarrativeConfigRepository Instance
        {
            get
            {
                if (m_instance == null)
                    m_instance = new NarrativeConfigRepository();

                return m_instance;
            }
        }

        private NarrativeConfigRepository()
        {
        }

        private static Tables Tables => ConfigSystem.Instance.Tables;

        #region Get

        public EmotionConfig GetEmotion(string id)
        {
            return Tables.TbEmotionConfig.GetOrDefaultNoStatic(id);
        }

        public MemoryConfig GetMemory(string id)
        {
            return Tables.TbMemoryConfig.GetOrDefaultNoStatic(id);
        }

        public ReplyOptionConfig GetReply(string id)
        {
            return Tables.TbReplyOptionConfig.GetOrDefaultNoStatic(id);
        }

        public StoryNodeConfig GetStoryNode(string id)
        {
            return Tables.TbStoryNodeConfig.GetOrDefaultNoStatic(id);
        }

        #endregion

        #region Has

        public bool HasEmotion(string id)
        {
            return Tables.TbEmotionConfig.dataMap.ContainsKey(id);
        }

        public bool HasMemory(string id)
        {
            return Tables.TbMemoryConfig.dataMap.ContainsKey(id);
        }

        public bool HasReply(string id)
        {
            return Tables.TbReplyOptionConfig.dataMap.ContainsKey(id);
        }

        public bool HasStoryNode(string id)
        {
            return Tables.TbStoryNodeConfig.dataMap.ContainsKey(id);
        }

        #endregion

        #region All

        public IReadOnlyList<EmotionConfig> AllEmotions
            => Tables.TbEmotionConfig.dataList;

        public IReadOnlyList<MemoryConfig> AllMemories
            => Tables.TbMemoryConfig.dataList;

        public IReadOnlyList<ReplyOptionConfig> AllReplies
            => Tables.TbReplyOptionConfig.dataList;

        public IReadOnlyList<StoryNodeConfig> AllStoryNodes
            => Tables.TbStoryNodeConfig.dataList;

        #endregion

        #region Count

        public int EmotionCount => Tables.TbEmotionConfig.dataList.Count;

        public int MemoryCount => Tables.TbMemoryConfig.dataList.Count;

        public int ReplyCount => Tables.TbReplyOptionConfig.dataList.Count;

        public int StoryNodeCount => Tables.TbStoryNodeConfig.dataList.Count;

        #endregion
    }
}