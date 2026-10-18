using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using DGame;

namespace GameLogic
{
	public partial class RightMessageItem
	{
		private bool m_artApplied;

		public void SetMessage(string message)
		{
			MessageBubbleLayout.Apply(rectTransform, m_tmpMessage, m_imgIcon,
				transform.Find("Bg") as RectTransform, message, true);
			ApplyBubbleArt();
		}

		private void ApplyBubbleArt()
		{
			if (m_artApplied)
			{
				return;
			}

			m_artApplied = true;
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			Transform bg = transform.Find("Bg");
			if (bg != null)
			{
				Image bgImage = bg.GetComponent<Image>();
				if (bgImage != null)
				{
					bgImage.SetSprite("dialogue_reply_bubble_01", false, image =>
					{
						if (image == null)
						{
							return;
						}

						image.color = Color.white;
						image.type = Image.Type.Sliced;
					}, token);
				}
			}

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

		#region 事件

		#endregion
	}
}
