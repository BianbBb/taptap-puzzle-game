using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using DGame;

namespace GameLogic
{
	public partial class RightMessageItem
	{
		/// <summary>默认气泡底：美术画的米色纸片。</summary>
		private const string BubbleSpritePlain = "dialogue_reply_bubble_01";
		/// <summary>同一张图的纯白版（alpha 逐像素一致），专门用来乘情绪色。</summary>
		private const string BubbleSpriteTint = "dialogue_reply_bubble_tint_01";

		private bool m_iconApplied;
		private string m_bubbleSpriteLocation;
		/// <summary>气泡底色，alpha 为 0 表示不染色。</summary>
		private Color m_bubbleColor = Color.clear;

		public void SetMessage(string message, Color bubbleColor = default)
		{
			m_bubbleColor = bubbleColor;
			MessageBubbleLayout.Apply(rectTransform, m_tmpMessage, m_imgIcon,
				transform.Find("Bg") as RectTransform, message, true);
			ApplyBubbleArt();
		}

		private void ApplyBubbleArt()
		{
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			// 情绪色都是浅粉彩，乘在米色纸片上会被压暗成灰；染色时改用同一形状的纯白底图。
			string location = m_bubbleColor.a > 0f ? BubbleSpriteTint : BubbleSpritePlain;
			Transform bg = transform.Find("Bg");
			Image bgImage = bg == null ? null : bg.GetComponent<Image>();
			if (bgImage != null)
			{
				bgImage.color = ResolveBubbleTint();
				// 同一个 item 会被列表复用，换消息时底图可能要在米色和白色之间切换。
				if (m_bubbleSpriteLocation != location)
				{
					m_bubbleSpriteLocation = location;
					bgImage.SetSprite(location, false, image =>
					{
						if (image == null || m_bubbleSpriteLocation != location)
						{
							return;
						}

						image.color = ResolveBubbleTint();
						image.type = Image.Type.Sliced;
					}, token);
				}
			}

			if (m_iconApplied)
			{
				return;
			}

			m_iconApplied = true;
			if (m_imgIcon != null)
			{
				m_imgIcon.SetSprite("dialogue_player_portrait", true, image =>
				{
					if (image == null)
					{
						return;
					}

					image.color = Color.white;
					image.preserveAspect = true;
				}, token);
			}
		}

		private Color ResolveBubbleTint()
		{
			return m_bubbleColor.a > 0f ? m_bubbleColor : Color.white;
		}

		#region 事件

		#endregion
	}
}
