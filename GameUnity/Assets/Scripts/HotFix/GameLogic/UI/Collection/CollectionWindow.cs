using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DGame;
using GameProto;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 图鉴（记忆 / 情绪）。
	/// 布局数值取自美术参考图《记忆页面.jpg》《情绪界面.jpg》的像素测量，基准 1920×1080（与 UICanvas 参考分辨率一致）。
	/// </summary>
	public partial class CollectionWindow : UIWindow
	{
		protected override UILayer windowLayer => UILayer.UI;
		public override bool FullScreen => true;

		// ---- 记忆卡片网格：卡 221×298，列距 265、行距 329，首卡中心 (540,386)（画布左上原点） ----
		private const int CardColumns = 4;
		private const float CardPitchX = 265f;
		private const float CardPitchY = 329f;
		private const float CardFirstX = -385f;
		private const float CardFirstY = -188f;
		private const float ContentWidth = 1090f;
		private const int CardTopPad = 188;
		private const int CardBottomPad = 40;

		// ---- 页签 ----
		// 选中 / 常态两套图已作为子物体预置在 prefab 里（选中态 207×180 @(-723,±)，常态 168×140 内缩 20,-11），
		// Button 热区固定按选中态尺寸铺满，两种状态只切图不重算布局 —— 避免运行时按资源名加载新切图。


		// ---- 情绪圆（沿用对话界面情绪条的一套参数） ----
		private const float EmotionRowWidth = 900f;
		private const float EmotionSpacing = 24f;
		private const float EmotionMinDiameter = 56f;
		private const float EmotionMaxDiameterCap = 128f;
		private const float EmotionFullCount = 9f;

		private readonly List<CollectionCardItem> m_cards = new List<CollectionCardItem>();
		private GameObject m_emotionRow;
		private bool m_showEmotion;
		private int m_pageVersion;

		// prefab 里滚动结构节点已改名为 m_goScroll / m_goContent（绑定表给的是 GameObject），
		// 这里就地还原出组件引用，避免滚动相关代码到处写 GetComponent。
		private ScrollRect m_scrollRectCache;
		private RectTransform m_rtContentCache;

		private ScrollRect m_scrollRect
		{
			get
			{
				if (m_scrollRectCache == null && m_goScroll != null)
				{
					m_scrollRectCache = m_goScroll.GetComponent<ScrollRect>();
				}
				return m_scrollRectCache;
			}
		}

		private RectTransform m_rtContent
		{
			get
			{
				if (m_rtContentCache == null && m_goContent != null)
				{
					m_rtContentCache = m_goContent.GetComponent<RectTransform>();
				}
				return m_rtContentCache;
			}
		}

		#region 生命周期

		protected override void OnCreate()
		{
			// 点遮罩关闭详情（Button 是 prefab 里给遮罩加的，不进绑定表）
			if (m_imgDetailMask != null)
			{
				Button maskBtn = m_imgDetailMask.GetComponent<Button>();
				if (maskBtn != null) maskBtn.onClick.AddListener(HideDetail);
			}

			HideDetail();
		}

		protected override void OnRefresh()
		{
			SwitchPage(m_showEmotion);
		}

		protected override void OnDestroy()
		{
			ClearCards();
			DestroyEmotionRow();
		}

		#endregion

		#region 页签

		private partial void OnClickTabMemoryBtn()
		{
			if (!m_showEmotion) return;
			SwitchPage(false);
		}

		private partial void OnClickTabEmotionBtn()
		{
			if (m_showEmotion) return;
			SwitchPage(true);
		}

		private partial void OnClickCloseBtn()
		{
			Close();
		}

		private partial void OnClickDetailCloseBtn()
		{
			HideDetail();
		}

		private void SwitchPage(bool emotions)
		{
			m_showEmotion = emotions;
			m_pageVersion++;
			HideDetail();
			ApplyTabVisual(emotions);

			if (emotions)
			{
				ClearCards();
				BuildEmotionRowAsync(m_pageVersion).Forget();
			}
			else
			{
				DestroyEmotionRow();
				BuildMemoryCards();
			}
		}

		private void ApplyTabVisual(bool emotions)
		{
			if (m_imgTabMemorySlc != null) m_imgTabMemorySlc.gameObject.SetActive(!emotions);
			if (m_imgTabMemoryNorm != null) m_imgTabMemoryNorm.gameObject.SetActive(emotions);
			if (m_imgTabEmotionSlc != null) m_imgTabEmotionSlc.gameObject.SetActive(emotions);
			if (m_imgTabEmotionNorm != null) m_imgTabEmotionNorm.gameObject.SetActive(!emotions);
		}

		#endregion

		#region 记忆页

		private void BuildMemoryCards()
		{
			if (m_rtContent == null) return;

			IReadOnlyList<MemoryConfig> entries = NarrativeConfigMgr.Instance.AllMemories;
			int count = entries?.Count ?? 0;
			int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)CardColumns));
			float contentHeight = CardTopPad + (rows - 1) * CardPitchY + 298f * 0.5f + CardBottomPad;
			m_rtContent.sizeDelta = new Vector2(ContentWidth, Mathf.Max(700f, contentHeight));
			m_rtContent.anchoredPosition = Vector2.zero;
			m_scrollRect.verticalNormalizedPosition = 1f;

			if (count == 0) return;

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			for (int i = 0; i < count; i++)
			{
				MemoryConfig entry = entries[i];
				if (entry == null) continue;

				CollectionCardItem card = CreateWidgetByType<CollectionCardItem>(m_rtContent, true);
				if (card == null) continue;

				RectTransform rect = card.rectTransform;
				rect.anchorMin = new Vector2(0.5f, 1f);
				rect.anchorMax = new Vector2(0.5f, 1f);
				rect.pivot = new Vector2(0.5f, 0.5f);
				rect.sizeDelta = new Vector2(221f, 298f);
				rect.anchoredPosition = new Vector2(
					CardFirstX + (i % CardColumns) * CardPitchX,
					CardFirstY - (i / CardColumns) * CardPitchY);

				bool owned = saveData.HasMemory(entry.Id);
				MemoryConfig captured = entry;
				card.SetData(captured, owned, _ => ShowDetail(captured, owned));
				m_cards.Add(card);
			}
		}

		private void ClearCards()
		{
			for (int i = 0; i < m_cards.Count; i++)
			{
				CollectionCardItem card = m_cards[i];
				if (card != null) card.Destroy();
			}

			m_cards.Clear();
		}

		#endregion

		#region 情绪页

		private async UniTaskVoid BuildEmotionRowAsync(int version)
		{
			IReadOnlyList<EmotionConfig> all = NarrativeConfigMgr.Instance.AllEmotions;
			var entries = new List<EmotionConfig>(all.Count);
			for (int i = 0; i < all.Count; i++)
			{
				EmotionConfig entry = all[i];
				if (entry == null || IsNoneEmotionConfig(entry.Id)) continue;
				entries.Add(entry);
			}

			if (entries.Count == 0) return;

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			float maxDiameter = Mathf.Max(EmotionMinDiameter,
				Mathf.Min(EmotionMaxDiameterCap,
					(EmotionRowWidth - (entries.Count - 1) * EmotionSpacing) / entries.Count));

			DestroyEmotionRow();
			m_emotionRow = new GameObject("EmotionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
			m_emotionRow.transform.SetParent(m_scrollRect.viewport, false);
			m_emotionRow.layer = gameObject.layer;
			RectTransform rowRect = m_emotionRow.GetComponent<RectTransform>();
			rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 0.5f);
			rowRect.pivot = new Vector2(0.5f, 0.5f);
			rowRect.anchoredPosition = Vector2.zero;
			rowRect.sizeDelta = new Vector2(EmotionRowWidth, maxDiameter);

			HorizontalLayoutGroup layout = m_emotionRow.GetComponent<HorizontalLayoutGroup>();
			layout.childAlignment = TextAnchor.MiddleCenter;
			layout.spacing = EmotionSpacing;
			layout.childControlWidth = false;
			layout.childControlHeight = false;
			layout.childForceExpandWidth = false;
			layout.childForceExpandHeight = false;

			for (int i = 0; i < entries.Count; i++)
			{
				EmotionConfig emotion = entries[i];
				ReplayEmoItem item = await CreateWidgetByTypeAsync<ReplayEmoItem>(rowRect, false);
				// 等待期间可能已切页或关窗，丢弃本次构建。
				if (IsDestroyed || version != m_pageVersion || item == null || m_emotionRow == null) return;

				bool owned = saveData.HasEmotion(emotion.Id);
				item.SetCompactLabel();
				if (owned) item.SetData(emotion);
				else item.SetPlaceholder(emotion);

				float diameter = Mathf.Lerp(EmotionMinDiameter, maxDiameter,
					Mathf.Clamp01(saveData.GetEmotionUseCount(emotion.Id) / EmotionFullCount));
				item.rectTransform.sizeDelta = new Vector2(diameter, diameter);

				// 情绪圆只做展示：不可点击，悬停轻微放大。
				item.gameObject.AddComponent<UIHoverScale>();
				item.Show(true);
			}
		}

		private void DestroyEmotionRow()
		{
			if (m_emotionRow == null) return;
			UnityEngine.Object.Destroy(m_emotionRow);
			m_emotionRow = null;
		}

		private static bool IsNoneEmotionConfig(string emotionId)
			=> string.Equals(emotionId, NarrativeRules.NoneEmotionId, StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(emotionId, NarrativeRules.NoneEmotionConfigId, StringComparison.OrdinalIgnoreCase);

		#endregion

		#region 详情弹层

		// 详情版式：白纸大卡 + 齿孔邮票 + 金属圆章/关闭钮 + 标题点阵 + 分隔线 + 米黄正文框。
		// 所有尺寸与颜色都落在 CollectionWindow.prefab 的 DetailCard 下，运行时代码只填文本与配图。
		private GameObject DetailRoot => m_imgDetailMask != null ? m_imgDetailMask.transform.parent.gameObject : null;

		private void ShowDetail(MemoryConfig entry, bool owned)
		{
			GameObject root = DetailRoot;
			if (root == null || entry == null) return;
			root.SetActive(true);

			if (m_tmpDetailTitle != null)
			{
				m_tmpDetailTitle.text = owned ? entry.MemoryName : "未解锁";
			}

			if (m_tmpDetailBody != null)
			{
				m_tmpDetailBody.text = owned
					? entry.Description
					: "检查物品上的线索可发现这段记忆。";
			}

			if (m_imgDetailContent == null) return;
			m_imgDetailContent.color = new Color(1f, 1f, 1f, 0f);
			if (!owned || string.IsNullOrWhiteSpace(entry.ImageId)) return;
			if (!GameModule.ResourceModule.CheckLocationValid(entry.ImageId)) return;

			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			m_imgDetailContent.SetSprite(entry.ImageId, false, loaded =>
			{
				if (loaded == null || IsDestroyed || loaded.sprite == null) return;
				loaded.color = Color.white;
				loaded.preserveAspect = true;
			}, token);
		}

		private void HideDetail()
		{
			GameObject root = DetailRoot;
			if (root == null) return;
			// 清空文本，避免下次打开时先闪一帧上一条的内容。
			if (m_tmpDetailTitle != null) m_tmpDetailTitle.text = string.Empty;
			if (m_tmpDetailBody != null) m_tmpDetailBody.text = string.Empty;
			root.SetActive(false);
		}

		#endregion
	}
}
