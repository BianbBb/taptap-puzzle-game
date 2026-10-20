using System;
using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>
    /// 玩家名字：随机候选池、存档读写、剧本占位符替换。
    ///
    /// 剧本里的占位符有两种写法（历史遗留）：
    ///   $PlayerName —— 回复选项表用的正确拼写；
    ///   $PLayerName —— 幕间独白表里的旧拼写（L 大写，别删，删了老数据就不替换了）。
    /// 所有进入 UI 的剧本文本都要先过 <see cref="Resolve"/>。
    /// </summary>
    public static class NarrativePlayerName
    {
        /// <summary>玩家没填名字时的兜底（老存档 / 首帧未初始化）。</summary>
        public const string Fallback = "小满";

        /// <summary>输入上限，和 UI 的字数提示保持一致。</summary>
        public const int MaxLength = 8;

        private const string Placeholder = "$PlayerName";
        private const string LegacyPlaceholder = "$PLayerName";

        /// <summary>
        /// 随机候选池。都是两个字以内、带点旧时光味的名字，
        /// 和「旧货铺 / 磁带 / 手写标签」这套美术更搭。
        /// </summary>
        private static readonly string[] s_candidates =
        {
            "小满", "阿棠", "知夏", "谷雨", "南栀", "立冬",
            "晚照", "青禾", "阿汀", "小舟", "素秋", "杏子",
        };

        private static readonly Random s_random = new Random();

        /// <summary>候选池只读访问（UI 一次摆三个）。</summary>
        public static IReadOnlyList<string> Candidates => s_candidates;

        /// <summary>当前存档里的名字；没填过返回空串。</summary>
        public static string Current => NarrativeSaveData.Get.PlayerName;

        /// <summary>渲染用名字：没填过时退回兜底名，绝不会把占位符漏到画面上。</summary>
        public static string DisplayName
        {
            get
            {
                string current = Current;
                return string.IsNullOrWhiteSpace(current) ? Fallback : current.Trim();
            }
        }

        /// <summary>洗掉首尾空白并截断到上限；纯空白返回空串。</summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            string trimmed = name.Trim();
            return trimmed.Length <= MaxLength ? trimmed : trimmed.Substring(0, MaxLength);
        }

        /// <summary>
        /// 抽一个随机名字。传入 exclude 时保证不会抽到它，
        /// 这样连点骰子不会出现「看起来没反应」的情况。
        /// </summary>
        public static string PickRandom(string exclude = null)
        {
            string normalizedExclude = Normalize(exclude);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                string candidate = s_candidates[s_random.Next(s_candidates.Length)];
                if (!string.Equals(candidate, normalizedExclude, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            // 池子里只有一个名字时才会走到这里
            return s_candidates[0];
        }

        /// <summary>一次抽 count 个互不相同的候选。</summary>
        public static List<string> PickCandidates(int count)
        {
            var picked = new List<string>(count);
            if (count <= 0)
            {
                return picked;
            }

            var pool = new List<string>(s_candidates);
            while (picked.Count < count && pool.Count > 0)
            {
                int index = s_random.Next(pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }

            return picked;
        }

        /// <summary>
        /// 把剧本里的占位符换成玩家名。没有 $ 的文本原样返回，不产生额外分配。
        /// </summary>
        public static string Resolve(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('$') < 0)
            {
                return text;
            }

            string name = DisplayName;
            if (text.IndexOf(Placeholder, StringComparison.Ordinal) >= 0)
            {
                text = text.Replace(Placeholder, name);
            }

            if (text.IndexOf(LegacyPlaceholder, StringComparison.Ordinal) >= 0)
            {
                text = text.Replace(LegacyPlaceholder, name);
            }

            return text;
        }
    }
}
