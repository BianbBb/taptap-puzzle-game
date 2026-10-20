using GameProto;
using UnityEngine;

namespace GameLogic
{
	/// <summary>
	/// 情绪颜色的唯一真源。对话里选情绪的情绪圆、图鉴里的情绪圆都走这里，
	/// 颜色取自《情绪配置表》的 displayColor，因此两处必然逐像素一致。
	/// </summary>
	public static class EmotionVisual
	{
		/// <summary>未获得情绪的占位灰。</summary>
		public static readonly Color LockedColor = new Color(0.72f, 0.72f, 0.72f, 1f);

		/// <summary>"无情绪"按钮的灰，不在配表里。</summary>
		public static readonly Color NoneColor = new Color(0.78f, 0.78f, 0.8f, 1f);

		public static Color ResolveColor(EmotionConfig emotion, bool locked = false)
		{
			if (locked)
			{
				return LockedColor;
			}

			if (emotion != null && !string.IsNullOrEmpty(emotion.DisplayColor) &&
				ColorUtility.TryParseHtmlString(emotion.DisplayColor, out Color parsed))
			{
				parsed.a = 1f;
				return parsed;
			}

			return ResolveFallbackColor(emotion);
		}

		/// <summary>配表缺颜色时的确定性兜底色，保证同一条情绪每次取到同一个色。</summary>
		private static Color ResolveFallbackColor(EmotionConfig emotion)
		{
			string key = emotion == null ? string.Empty : emotion.Id + emotion.EmotionName;
			int hash = 0;
			for (int i = 0; i < key.Length; i++)
			{
				hash = (hash * 31) + key[i];
			}

			int index = (hash & int.MaxValue) % 4;
			if (index == 0) return Color.white;
			if (index == 1) return new Color(0.95f, 0.84f, 0.42f, 1f);
			if (index == 2) return new Color(0.49f, 0.78f, 0.78f, 1f);
			return new Color(0.91f, 0.63f, 0.66f, 1f);
		}
	}
}
