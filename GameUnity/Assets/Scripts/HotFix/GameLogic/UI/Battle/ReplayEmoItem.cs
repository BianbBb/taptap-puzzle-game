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

		/// <summary>图鉴里的圆只有 40~100 直径，字号需要比对话条小。</summary>
		private bool m_compactLabel;

		private const float NormalFontSizeMin = 16f;
		private const float NormalFontSizeMax = 28f;
		private const float CompactFontSizeMin = 11f;
		private const float CompactFontSizeMax = 18f;

		public void SetData(EmotionConfig emotion, bool locked = false)
		{
			Emotion = emotion;
			IsNone = false;
			IsLocked = locked;
			SetNameLabel(emotion == null ? string.Empty :
				locked ? emotion.EmotionName + "\n未解锁" : emotion.EmotionName);
			ApplyCircleArt(locked, EmotionVisual.ResolveColor(emotion, locked));
		}

		/// <summary>未获得的占位圆：灰底、不写字（小圆里放不下"未解锁"）。</summary>
		public void SetPlaceholder(EmotionConfig emotion)
		{
			Emotion = emotion;
			IsNone = false;
			IsLocked = true;
			SetNameLabel(string.Empty);
			ApplyCircleArt(true, EmotionVisual.LockedColor);
		}

		/// <summary>切到图鉴用的小字号；与 SetData 的调用先后无关。</summary>
		public void SetCompactLabel()
		{
			m_compactLabel = true;
			ApplyFontSize();
		}

		public void SetNoneData()
		{
			Emotion = null;
			IsNone = true;
			IsLocked = false;
			SetNameLabel("无");
			ApplyCircleArt(false, EmotionVisual.NoneColor);
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
			ApplyFontSize();
			m_tmpName.alignment = TextAlignmentOptions.Center;
			m_tmpName.raycastTarget = false;
		}

		private void ApplyFontSize()
		{
			if (m_tmpName == null)
			{
				return;
			}

			m_tmpName.fontSizeMin = m_compactLabel ? CompactFontSizeMin : NormalFontSizeMin;
			m_tmpName.fontSizeMax = m_compactLabel ? CompactFontSizeMax : NormalFontSizeMax;
		}

		private void ApplyCircleArt(bool locked, Color tint)
		{
			if (m_imgBg == null)
			{
				return;
			}

			if (locked)
			{
				tint = EmotionVisual.LockedColor;
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

		#region 事件

		#endregion
	}
}
