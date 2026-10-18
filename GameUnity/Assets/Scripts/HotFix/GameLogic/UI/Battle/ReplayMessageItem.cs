using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DGame;
using GameProto;

namespace GameLogic
{
	public partial class ReplayMessageItem
	{
		public string ReplyId { get; private set; }
		public bool IsUnlocked { get; private set; }

		public void SetData(string replyId, string displayText, bool isUnlocked)
		{
			ReplyId = replyId;
			IsUnlocked = isUnlocked;
			const float width = 500f;
			const float horizontalInset = 30f;
			const float verticalInset = 20f;
			m_tmpContent.text = displayText ?? string.Empty;
			m_tmpContent.fontSize = 26f;
			m_tmpContent.enableWordWrapping = true;
			m_tmpContent.overflowMode = TextOverflowModes.Overflow;
			m_tmpContent.enableAutoSizing = false;
			m_tmpContent.alignment = TextAlignmentOptions.Center;
			m_tmpContent.color = isUnlocked
				? new Color(0.28f, 0.22f, 0.16f, 1f)
				: new Color(0.45f, 0.42f, 0.4f, 1f);
			float textHeight = Mathf.Ceil(m_tmpContent.GetPreferredValues(m_tmpContent.text,
				width - horizontalInset * 2f, 10000f).y);
			float height = Mathf.Max(118f, textHeight + verticalInset * 2f);
			rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
			rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
			RectTransform contentRect = m_tmpContent.rectTransform;
			contentRect.anchorMin = Vector2.zero;
			contentRect.anchorMax = Vector2.one;
			contentRect.offsetMin = new Vector2(horizontalInset, verticalInset);
			contentRect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
			Transform bgTransform = transform.Find("Bg");
			Image bg = bgTransform != null
				? bgTransform.GetComponent<Image>()
				: gameObject.GetComponent<Image>();
			if (bg != null)
			{
				string location = isUnlocked ? "dialogue_reply_option_bg" : "dialogue_reply_option_disabled";
				bg.SetSprite(location, false, image =>
				{
					if (image == null)
					{
						return;
					}

					image.color = Color.white;
					image.type = Image.Type.Sliced;
				}, gameObject.GetCancellationTokenOnDestroy());
			}
		}

		#region 事件

		#endregion
	}
}
