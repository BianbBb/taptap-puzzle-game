using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DGame;
using GameProto;

namespace GameLogic
{
	public partial class ReplayEmoItem
	{
		public EmotionConfig Emotion { get; private set; }
		public bool IsNone { get; private set; }
		public bool IsLocked { get; private set; }

		public void SetData(EmotionConfig emotion, bool locked = false)
		{
			Emotion = emotion;
			IsNone = false;
			IsLocked = locked;
			SetNameLabel(emotion == null ? string.Empty :
				locked ? emotion.EmotionName + "\n未解锁" : emotion.EmotionName);
			ApplyCircleArt(locked, ResolveEmotionColor(emotion));
		}

		public void SetNoneData()
		{
			Emotion = null;
			IsNone = true;
			IsLocked = false;
			SetNameLabel("无");
			ApplyCircleArt(false, new Color(0.78f, 0.78f, 0.8f, 1f));
		}

		private void SetNameLabel(string value)
		{
			if (m_tmpName == null)
			{
				return;
			}

			m_tmpName.text = value ?? string.Empty;
			m_tmpName.color = Color.black;
			m_tmpName.enableWordWrapping = false;
			m_tmpName.overflowMode = TextOverflowModes.Ellipsis;
			m_tmpName.enableAutoSizing = true;
			m_tmpName.fontSizeMin = 16f;
			m_tmpName.fontSizeMax = 28f;
			m_tmpName.alignment = TextAlignmentOptions.Center;
			m_tmpName.raycastTarget = false;
		}

		private void ApplyCircleArt(bool locked, Color tint)
		{
			if (m_imgBg == null)
			{
				return;
			}

			if (locked)
			{
				tint = new Color(0.72f, 0.72f, 0.72f, 1f);
			}
			m_imgBg.color = tint;
			m_imgBg.raycastTarget = true;
			string location = locked ? "dialogue_colour_btn_diabled" : "dialogue_colour_btn";
			m_imgBg.SetSprite(location, true, image =>
			{
				if (image == null)
				{
					return;
				}

				image.color = tint;
				image.preserveAspect = true;
				image.raycastTarget = true;
			}, gameObject.GetCancellationTokenOnDestroy());
		}

		private static Color ResolveEmotionColor(EmotionConfig emotion)
		{
			if (emotion != null && !string.IsNullOrEmpty(emotion.DisplayColor) &&
				ColorUtility.TryParseHtmlString(emotion.DisplayColor, out Color parsed))
			{
				parsed.a = 1f;
				return parsed;
			}

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

		#region 事件

		#endregion
	}
}
