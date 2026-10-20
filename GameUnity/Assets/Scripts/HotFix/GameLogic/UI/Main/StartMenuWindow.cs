using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DGame;

namespace GameLogic
{
	/// <summary>
	/// 开始界面二级菜单（抽屉版）：四行文字菜单 + 一条可复用的高亮条。
	/// 图鉴本阶段置灰，不接多语言。
	/// </summary>
	public partial class StartMenuWindow
	{
		private static readonly Color ItemNormalColor = new Color(0.96f, 0.89f, 0.74f, 1f);
		private static readonly Color ItemHoverColor = new Color(0.29f, 0.20f, 0.15f, 1f);
		private static readonly Color ItemDisabledColor = new Color(0.96f, 0.89f, 0.74f, 0.35f);

		private bool m_hoverBound;
		private TextMeshProUGUI m_hoverLabel;

		public override bool FullScreen => true;

		public override bool CanEscClose => false;

		protected override void OnCreate()
		{
			BindHover();
			RefreshUI();
		}

		protected override void OnRefresh()
		{
			RefreshUI();
		}

		public void RefreshUI()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			bool hasProgress = !string.IsNullOrWhiteSpace(saveData.CurrentNodeId) ||
				saveData.CompletedSessionIds.Count > 0;

			SetItemState(m_btnContinue, m_tmpContinue, hasProgress);

			// 图鉴入口依赖玩法内的数据上下文，本阶段先置灰。
			SetItemState(m_btnGallery, m_tmpGallery, false);

			SetItemState(m_btnStart, m_tmpStart, true);
			SetItemState(m_btnQuit, m_tmpQuit, true);
		}

		private static void SetItemState(Button button, TextMeshProUGUI label, bool interactable)
		{
			if (button != null)
			{
				button.interactable = interactable;
			}
			if (label != null)
			{
				label.color = interactable ? ItemNormalColor : ItemDisabledColor;
			}
		}

		#region 悬停高亮

		private void BindHover()
		{
			if (m_hoverBound)
			{
				return;
			}
			m_hoverBound = true;

			BindHoverItem(m_btnStart);
			BindHoverItem(m_btnContinue);
			BindHoverItem(m_btnGallery);
			BindHoverItem(m_btnQuit);

			HideHighlight();
		}

		private void BindHoverItem(Button button)
		{
			if (button == null)
			{
				return;
			}
			if (button.GetComponent<EventTrigger>() != null)
			{
				return;
			}

			EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();

			EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
			enter.callback.AddListener(_ => ShowHighlightFor(button));
			trigger.triggers.Add(enter);

			EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
			exit.callback.AddListener(_ => HideHighlight());
			trigger.triggers.Add(exit);
		}

		private void ShowHighlightFor(Button button)
		{
			if (m_imgHighlight == null || button == null)
			{
				return;
			}

			// 置灰项（如图鉴、无存档时的继续游戏）不显示高亮。
			if (!button.interactable)
			{
				HideHighlight();
				return;
			}

			RectTransform itemRect = button.transform as RectTransform;
			if (itemRect == null)
			{
				return;
			}

			RestoreHoverLabel();

			m_imgHighlight.gameObject.SetActive(true);

			// 高亮条与四个按钮同属 MenuGroup 容器，容器的 Z 旋转（5.7°）统一承担整体倾斜，
			// 容器内部是一套「正的」坐标系：行与行之间只有纵向间距，没有任何旋转与投影换算。
			// 所以这里只要把高亮条搬到目标行的位置即可；两者宽度不同时按右边缘对齐
			// （参考图里条的右端略长于文字，左端出画）。高亮贴图本身是平的长方形，
			// 它的倾斜完全来自父级容器，千万不要再给这个 Image 单独设 Z 旋转。
			RectTransform highlightRect = m_imgHighlight.rectTransform;
			float rightAlignOffset = (itemRect.rect.width - highlightRect.rect.width) * 0.5f;
			highlightRect.anchoredPosition = new Vector2(
				itemRect.anchoredPosition.x + rightAlignOffset,
				itemRect.anchoredPosition.y);

			m_hoverLabel = button.GetComponentInChildren<TextMeshProUGUI>(true);
			if (m_hoverLabel != null)
			{
				m_hoverLabel.color = ItemHoverColor;
			}
		}

		private void HideHighlight()
		{
			RestoreHoverLabel();

			if (m_imgHighlight == null)
			{
				return;
			}
			m_imgHighlight.gameObject.SetActive(false);
		}

		private void RestoreHoverLabel()
		{
			if (m_hoverLabel == null)
			{
				return;
			}
			m_hoverLabel.color = ItemNormalColor;
			m_hoverLabel = null;
		}

		#endregion

		#region 事件

		private partial void OnClickStartBtn()
		{
			EnterGame();
		}

		private partial void OnClickContinueBtn()
		{
			EnterGame();
		}

		private partial void OnClickGalleryBtn()
		{
			// 图鉴本阶段置灰，保留回调以便后续接独立界面。
		}

		private partial void OnClickQuitBtn()
		{
			Application.Quit();
		}

		private void EnterGame()
		{
			Close();
			GameModule.UIModule.ShowWindowAsync<BattleMainUI>();
		}

		#endregion
	}
}
