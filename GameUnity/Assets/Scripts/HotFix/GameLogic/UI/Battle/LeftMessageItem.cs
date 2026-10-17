using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DGame;

namespace GameLogic
{
	public partial class LeftMessageItem
	{
		private bool m_artApplied;

		public void SetMessage(string message)
		{
			m_tmpMessage.text = message;
			m_tmpMessage.fontSize = 26f;
			m_tmpMessage.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 440f);
			m_tmpMessage.color = new Color(0.28f, 0.22f, 0.16f, 1f);
			m_tmpMessage.enableWordWrapping = true;
			m_tmpMessage.overflowMode = TextOverflowModes.Overflow;
			m_tmpMessage.ForceMeshUpdate();
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
				(bg as RectTransform)?.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500f);
				Image bgImage = bg.GetComponent<Image>();
				if (bgImage != null)
				{
					bgImage.SetSprite("dialogue_npc_dialogue_bubble_01", false, image =>
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
				m_imgIcon.SetSprite("dialogue_npc_01_portrait", true, image =>
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
