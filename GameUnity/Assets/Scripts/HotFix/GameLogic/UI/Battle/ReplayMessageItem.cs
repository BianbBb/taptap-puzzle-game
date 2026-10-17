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
			rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 650f);
			rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 88f);
			m_tmpContent.text = displayText ?? string.Empty;
			m_tmpContent.fontSize = 26f;
			m_tmpContent.enableWordWrapping = true;
			m_tmpContent.overflowMode = TextOverflowModes.Ellipsis;
			m_tmpContent.color = isUnlocked
				? new Color(0.28f, 0.22f, 0.16f, 1f)
				: new Color(0.45f, 0.42f, 0.4f, 1f);
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
