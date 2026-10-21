using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using DGame;

namespace GameLogic
{
	public partial class LeftMessageItem
	{
		/// <summary>没有专属头像的说话人（妻子、陈父、旁白…）用这张通用 NPC 图。</summary>
		private const string DefaultPortraitLocation = "dialogue_npc_01_portrait";

		/// <summary>
		/// npcId → 头像资源。头像是从立绘裁出来的圆图，生成脚本见
		/// `Tools/art/make_npc_portrait.py`；再补角色时先跑脚本，再把资源名登记到这里。
		/// </summary>
		private static readonly Dictionary<string, string> PortraitLocations =
			new Dictionary<string, string>
			{
				{ "老周", "dialogue_portrait_laozhou" },
				{ "陈先生", "dialogue_portrait_chenxiansheng" },
				{ "陈果", "dialogue_portrait_chenguo" },
			};

		private bool m_artApplied;
		/// <summary>当前头像资源名。列表 item 会被复用，换说话人时要重新加载。</summary>
		private string m_portraitLocation;

		public void SetMessage(string message, string npcId = null)
		{
			MessageBubbleLayout.Apply(rectTransform, m_tmpMessage, m_imgIcon,
				transform.Find("Bg") as RectTransform, message, false);
			ApplyBubbleArt();
			ApplyPortrait(npcId);
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
		}

		private void ApplyPortrait(string npcId)
		{
			if (m_imgIcon == null)
			{
				return;
			}

			string location = DefaultPortraitLocation;
			string key = npcId == null ? string.Empty : npcId.Trim();
			if (key.Length > 0 && PortraitLocations.TryGetValue(key, out string mapped))
			{
				location = mapped;
			}

			// 同一个 item 连着两句都是同一个人时不必重载贴图。
			if (location == m_portraitLocation)
			{
				return;
			}

			m_portraitLocation = location;
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			m_imgIcon.SetSprite(location, true, image =>
			{
				// 异步落地时 item 可能已经换人了，晚到的图不能盖掉新头像。
				if (image == null || m_portraitLocation != location)
				{
					return;
				}

				image.color = Color.white;
				image.preserveAspect = true;
			}, token);
		}

		#region 事件

		#endregion
	}
}
