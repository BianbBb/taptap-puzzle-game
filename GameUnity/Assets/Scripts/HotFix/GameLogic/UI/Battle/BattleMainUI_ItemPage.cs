using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DGame;
using DG.Tweening;
using GameProto;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 物品详情页：点物品栏 → 把对话纸翻成「物品页」。
	///
	/// 与通用 ShowOverlay 的区别：
	/// - 不套设置弹窗那张底图，直接用 <c>dialogue_panel</c> 九切当页面壳，
	///   位置抄当前对话纸（<see cref="m_dialoguePanel"/>）的 rect，看上去就是同一本手账翻了一页；
	/// - 纸面铺 <c>dialogue_item_panel_dec</c> 的横线，物品图居中、名称居中、描述两行；
	/// - 部件不做按钮列表，直接在图上圈金色虚线圈（圈不住、没有坐标的部件才在纸面底部列出来）。
	/// </summary>
	public partial class BattleMainUI
	{
		// ---- 九切内衬：dialogue_panel 的 spriteBorder = 左96 / 下24 / 右250 / 上96 ----
		/// <summary>纸面左边距（装订线那一侧）。</summary>
		private const float ItemPagePadLeft = 96f;
		/// <summary>纸面上边距（顶部那条牛皮纸带）。</summary>
		private const float ItemPagePadTop = 96f;
		/// <summary>纸面右边距（右侧物品/图鉴页签带，内容不能压过去）。</summary>
		private const float ItemPagePadRight = 250f;
		/// <summary>纸面下边距。</summary>
		private const float ItemPagePadBottom = 24f;
		/// <summary>对话纸找不回来时的兜底页面尺寸/位置（取自 BattleMainUI.prefab）。</summary>
		private static readonly Vector2 ItemPageFallbackSize = new Vector2(990f, 830f);
		private static readonly Vector2 ItemPageFallbackPosition = new Vector2(263.63f, -59.45f);

		// ---- 版式（纸面坐标，左上为原点）----
		// 实机量出来的纸面是 860×753（Panel 1206×873 扣掉九切内衬），下面这些值按它排。
		private const float ItemPageImageTop = 48f;
		/// <summary>物品图区域宽度占纸面宽的比例。</summary>
		private const float ItemPageImageWidthRatio = 0.7f;
		/// <summary>物品图区域高度：物品按「内容框最长边 = 这个高度」缩放。</summary>
		private const float ItemPageImageHeight = 330f;
		private const float ItemPageNameTop = 392f;
		private const float ItemPageNameBoxHeight = 68f;
		private const float ItemPageNameFontSize = 52f;
		private const float ItemPageNameMinFontSize = 34f;
		private const float ItemPageRuleTop = 474f;
		/// <summary>分隔虚线占纸面宽的比例。</summary>
		private const float ItemPageRuleWidthRatio = 0.46f;
		private const float ItemPageDescTop = 496f;
		/// <summary>描述盒高：字放到 28 之后，两行中文 ≈ 92，三行 ≈ 138，留到 150 兜住最长的条目。</summary>
		private const float ItemPageDescBoxHeight = 150f;
		private const float ItemPageDescWidthRatio = 0.86f;
		/// <summary>描述字号。旧值 17.5 在 1920 参考分辨率下只有 17.5 单位（≈ 手机上 10px），
		/// 和名称 52 放一起像脚注；对齐对话正文的量级（正文 ≈ 30）才读得动。</summary>
		private const float ItemPageDescFontSize = 28f;
		private const float ItemPageDescMinFontSize = 21f;
		private const float ItemPageHintTop = 656f;
		private const float ItemPageHintFontSize = 21f;
		private const float ItemPageHintBoxHeight = 30f;
		private const float ItemPageChipTop = 696f;
		/// <summary>没有物品图时，文字块整体上提这么多（约等于物品图区域的高度）。
		/// 注意纸面 y 是「向下为负」，所以提起来是加正数。</summary>
		private const float ItemPageNoImageLift = 272f;
		/// <summary>没有物品图时，部件按钮提到提示下面，不再钉在页脚。</summary>
		private const float ItemPageChipTopNoImage = 432f;
		private const float ItemPageChipHeight = 48f;
		private const float ItemPageChipGap = 10f;
		/// <summary>部件按钮宽度：按数量在 150（多）/196（少）之间自适应，字大了不能再钉死 132。</summary>
		private const float ItemPageChipMinWidth = 150f;
		private const float ItemPageChipMaxWidth = 196f;
		private const float ItemPageChipFontSize = 22f;
		private const float ItemPageChipMinFontSize = 15f;
		private const float ItemPageFadeSeconds = 0.18f;
		private const float ItemPageCloseFadeSeconds = 0.14f;
		/// <summary>热点圈的直径。</summary>
		private const float ItemPageHotspotSize = 74f;
		private const float ItemPageHotspotPulseSeconds = 0.75f;
		/// <summary>圈下面那张写部件名的小纸片。字号跟着整体放大，12 号在实机上是一团墨。</summary>
		private const float ItemPageHotspotTagFontSize = 19f;
		private const float ItemPageHotspotTagHeight = 31f;
		private const float ItemPageHotspotTagMaxWidth = 210f;

		private static readonly Color ItemPageRuledColor = new Color(1f, 1f, 1f, 0.32f);
		private static readonly Color ItemPageHotspotColor = new Color(0.85f, 0.66f, 0.30f, 0.95f);
		private static readonly Color ItemPageTagColor = new Color(0.99f, 0.96f, 0.89f, 0.92f);

		private static Sprite s_itemPageRingSprite;
		private static Sprite s_itemPageDotSprite;

		/// <summary>
		/// 物品图的「实际内容框」（归一化，原点左上）。素材画布 1024² 但钟表只占 27% 宽、
		/// 眼镜只占 27% 高，直接按画布缩会缩成一条，所以按内容框定尺寸。
		/// 素材重导出后要更新这里，量法：
		/// <c>python3 -c "from PIL import Image;print(Image.open('x.png').split()[3].getbbox())"</c>
		/// 没登记的物品按整张画布算。
		/// </summary>
		private static readonly Dictionary<string, Rect> ItemPageArtTrim = new Dictionary<string, Rect>
		{
			// x, y, width, height（都除以画布边长）
			{"narrative_clock", new Rect(0.3662f, 0.0596f, 0.2676f, 0.8809f)},
			{"narrative_camera", new Rect(0.1514f, 0.2334f, 0.6973f, 0.5303f)},
			{"narrative_glasses", new Rect(0.1484f, 0.3633f, 0.7031f, 0.2734f)},
			// 收录机 2026-10-21 换新图（1024² 画布，内容 940×470 居中）。
			{"narrative_radio", new Rect(0.0410f, 0.2705f, 0.9180f, 0.4590f)},
		};

		/// <summary>
		/// 搭出整页物品详情。遮罩透明（场景照常亮着），页面本身盖在对话纸的位置上。
		/// </summary>
		private void ShowItemPage(NarrativeItemConfig item, List<NarrativeItemPartConfig> hotParts,
			List<NarrativeItemPartConfig> listParts)
		{
			HideMemoryTooltip();
			DiscardSuspendedOverlay();
			DestroyOverlay();
			EnsureEventSystem();
			SoundConfigMgr.Instance.PlaySfx(SfxNames.ItemPanelIn);

			var blocker = new GameObject("NarrativeOverlay", typeof(RectTransform), typeof(Image));
			blocker.transform.SetParent(transform, false);
			blocker.transform.SetAsLastSibling();
			RectTransform blockerRect = blocker.GetComponent<RectTransform>();
			blockerRect.anchorMin = Vector2.zero;
			blockerRect.anchorMax = Vector2.one;
			blockerRect.offsetMin = Vector2.zero;
			blockerRect.offsetMax = Vector2.zero;
			// 透明但吃点击：物品页不是全屏弹窗，左侧人物和场景要保持原亮度。
			Image blockerImage = blocker.GetComponent<Image>();
			blockerImage.color = new Color(1f, 1f, 1f, 0f);
			blockerImage.raycastTarget = true;
			m_overlay = blocker;

			// 页面壳：位置/尺寸直接抄对话纸，保证「同一本手账」的观感；名字必须叫 Panel，
			// 物品部件的记忆飞入（PlayItemMemoryFlyIn）和 DestroyOverlay 都按这个名字找它。
			var page = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			page.transform.SetParent(blocker.transform, false);
			RectTransform pageRect = page.GetComponent<RectTransform>();
			RectTransform host = m_dialoguePanel != null ? m_dialoguePanel.transform as RectTransform : null;
			pageRect.anchorMin = host != null ? host.anchorMin : new Vector2(0.5f, 0.5f);
			pageRect.anchorMax = host != null ? host.anchorMax : new Vector2(0.5f, 0.5f);
			pageRect.pivot = host != null ? host.pivot : new Vector2(0.5f, 0.5f);
			pageRect.sizeDelta = host != null ? host.sizeDelta : ItemPageFallbackSize;
			pageRect.anchoredPosition = host != null ? host.anchoredPosition : ItemPageFallbackPosition;
			pageRect.localScale = Vector3.one;
			pageRect.localRotation = Quaternion.identity;

			Image pageImage = page.GetComponent<Image>();
			pageImage.color = Color.white;
			pageImage.raycastTarget = true;
			ApplyHudSprite(pageImage, "dialogue_panel", false, gameObject.GetCancellationTokenOnDestroy(), true);

			CanvasGroup pageGroup = page.GetComponent<CanvasGroup>();
			SetGroup(pageGroup, 0f);
			TweenItemPageFade(pageGroup, 1f, ItemPageFadeSeconds);

			RectTransform paper = CreateItemPagePaper(pageRect);
			float paperWidth = paper.sizeDelta.x;

			AddItemPageRuled(paper);
			bool hasImage = AddItemPageImage(paper, item, hotParts, paperWidth);
			// 没有物品图的物品（还没画）纸面会空掉小半页，把文字块整体提上去，按钮跟着提到提示下面，
			// 别让玩家以为这是张没加载完的页面。
			float offsetY = hasImage ? 0f : ItemPageNoImageLift;
			AddItemPageTitle(paper, item, paperWidth, offsetY);
			AddItemPageDescription(paper, item, paperWidth, offsetY);
			AddItemPageChips(paper, listParts, hasImage ? ItemPageChipTop : ItemPageChipTopNoImage);
			AddItemPageCloseButton(pageRect);
		}

		/// <summary>纸面容器：把九切的内衬扣掉，之后所有元素都按「纸」来排版。</summary>
		private static RectTransform CreateItemPagePaper(RectTransform pageRect)
		{
			var paper = new GameObject("Paper", typeof(RectTransform));
			paper.transform.SetParent(pageRect, false);
			RectTransform rect = paper.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0f, 1f);
			rect.anchorMax = new Vector2(0f, 1f);
			rect.pivot = new Vector2(0f, 1f);
			rect.anchoredPosition = new Vector2(ItemPagePadLeft, -ItemPagePadTop);
			rect.sizeDelta = new Vector2(
				Mathf.Max(1f, pageRect.sizeDelta.x - ItemPagePadLeft - ItemPagePadRight),
				Mathf.Max(1f, pageRect.sizeDelta.y - ItemPagePadTop - ItemPagePadBottom));
			return rect;
		}

		/// <summary>横线纸。压在内容下面，透明度压到 0.32，别把字吃掉。</summary>
		private void AddItemPageRuled(RectTransform paper)
		{
			var go = new GameObject("Ruled", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(paper, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			Image image = go.GetComponent<Image>();
			image.raycastTarget = false;
			image.preserveAspect = false;
			image.SetSprite("dialogue_item_panel_dec", false, loaded =>
			{
				if (loaded == null || IsDestroyed)
				{
					return;
				}

				loaded.color = ItemPageRuledColor;
				loaded.preserveAspect = false;
				loaded.raycastTarget = false;
			}, gameObject.GetCancellationTokenOnDestroy());
			// 放到最底下，别盖住后面加的物品图和文字。
			go.transform.SetAsFirstSibling();
		}

		/// <summary>物品图 + 图上的部件热点。返回是否真的放了图（没图时上面的文字块要提上来）。</summary>
		private bool AddItemPageImage(RectTransform paper, NarrativeItemConfig item,
			List<NarrativeItemPartConfig> hotParts, float paperWidth)
		{
			if (string.IsNullOrWhiteSpace(item.ImageId) || item.ImageId == "placeholder" ||
				!GameModule.ResourceModule.CheckLocationValid(item.ImageId))
			{
				return false;
			}

			var go = new GameObject("ItemImage", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(paper, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.5f, 1f);
			rect.anchorMax = new Vector2(0.5f, 1f);
			rect.pivot = new Vector2(0.5f, 1f);
			rect.anchoredPosition = new Vector2(0f, -ItemPageImageTop);
			float boxWidth = paperWidth * ItemPageImageWidthRatio;
			float boxHeight = ItemPageImageHeight;
			rect.sizeDelta = new Vector2(boxWidth, boxHeight);
			Image image = go.GetComponent<Image>();
			image.preserveAspect = true;
			image.raycastTarget = false;
			image.SetSprite(item.ImageId, false, loaded =>
			{
				if (loaded == null || IsDestroyed || loaded.sprite == null)
				{
					return;
				}

				loaded.preserveAspect = true;
				loaded.raycastTarget = false;
				// 按内容框（不是整张画布）缩：最长边顶到 boxHeight / boxWidth 里较小的那个上限，
				// 再把内容框的中心对齐到区域中心，物品才不会因为四周留白而缩成一条。
				Vector2 texture = loaded.sprite.rect.size;
				Rect trim = ItemPageArtTrim.TryGetValue(item.ImageId, out Rect registered)
					? registered
					: new Rect(0f, 0f, 1f, 1f);
				float contentWidth = Mathf.Max(1f, trim.width * texture.x);
				float contentHeight = Mathf.Max(1f, trim.height * texture.y);
				float fit = Mathf.Min(boxWidth / contentWidth, boxHeight / contentHeight);
				Vector2 displaySize = texture * fit;
				// 内容框中心相对画布中心的偏移（UI 的 y 轴向上，所以要用 0.5 - cy）。
				float offsetX = (trim.center.x - 0.5f) * texture.x * fit;
				float offsetY = (0.5f - trim.center.y) * texture.y * fit;
				rect.sizeDelta = displaySize;
				rect.anchoredPosition = new Vector2(-offsetX,
					-ItemPageImageTop - (boxHeight - displaySize.y) * 0.5f - offsetY);
				CreateItemPageHotspots(rect, displaySize, hotParts);
			}, gameObject.GetCancellationTokenOnDestroy());
			return true;
		}

		/// <summary>名称 + 名称下面那条「点点 + 虚线」的分隔。</summary>
		private void AddItemPageTitle(RectTransform paper, NarrativeItemConfig item, float paperWidth,
			float offsetY)
		{
			TextMeshProUGUI title = CreateOverlayText(paper, item.ItemName, ItemPageNameFontSize,
				Vector2.zero, new Vector2(paperWidth, ItemPageNameBoxHeight), TextAlignmentOptions.Center);
			RectTransform titleRect = title.rectTransform;
			titleRect.anchorMin = new Vector2(0.5f, 1f);
			titleRect.anchorMax = new Vector2(0.5f, 1f);
			titleRect.pivot = new Vector2(0.5f, 1f);
			titleRect.anchoredPosition = new Vector2(0f, -ItemPageNameTop + offsetY);
			title.enableWordWrapping = false;
			title.overflowMode = TextOverflowModes.Overflow;
			title.color = HudPaperText;
			title.characterSpacing = 14f;
			// 名字长了自动缩字，不要往两边顶出纸面。
			title.enableAutoSizing = true;
			title.fontSizeMin = ItemPageNameMinFontSize;
			title.fontSizeMax = ItemPageNameFontSize;

			// AddItemPageDashedRule 收的是「离顶部多少」，跟上面带负号的 anchoredPosition 反着来。
			AddItemPageDashedRule(paper, ItemPageRuleTop - offsetY, paperWidth * ItemPageRuleWidthRatio);
		}

		/// <summary>手绘感的分隔线：两端各一颗圆点，中间一串短横。</summary>
		private static void AddItemPageDashedRule(RectTransform paper, float top, float width)
		{
			var rule = new GameObject("Rule", typeof(RectTransform));
			rule.transform.SetParent(paper, false);
			RectTransform ruleRect = rule.GetComponent<RectTransform>();
			ruleRect.anchorMin = new Vector2(0.5f, 1f);
			ruleRect.anchorMax = new Vector2(0.5f, 1f);
			ruleRect.pivot = new Vector2(0.5f, 1f);
			ruleRect.anchoredPosition = new Vector2(0f, -top);
			ruleRect.sizeDelta = new Vector2(width, 8f);

			const float dashWidth = 9f;
			const float dashGap = 7f;
			const float thickness = 1.5f;
			int count = Mathf.Max(1, Mathf.FloorToInt((width + dashGap) / (dashWidth + dashGap)));
			float totalWidth = count * dashWidth + (count - 1) * dashGap;
			float startX = -totalWidth * 0.5f + dashWidth * 0.5f;
			for (int i = 0; i < count; i++)
			{
				var dash = new GameObject("Dash", typeof(RectTransform), typeof(Image));
				dash.transform.SetParent(ruleRect, false);
				RectTransform dashRect = dash.GetComponent<RectTransform>();
				dashRect.anchorMin = dashRect.anchorMax = new Vector2(0.5f, 0.5f);
				dashRect.pivot = new Vector2(0.5f, 0.5f);
				dashRect.anchoredPosition = new Vector2(startX + i * (dashWidth + dashGap), 0f);
				dashRect.sizeDelta = new Vector2(dashWidth, thickness);
				Image dashImage = dash.GetComponent<Image>();
				dashImage.color = ItemPageHotspotColor;
				dashImage.raycastTarget = false;
			}

			// 两端圆点稍微压出虚线的范围，像用笔戳出来的。
			for (int side = -1; side <= 1; side += 2)
			{
				var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image));
				dot.transform.SetParent(ruleRect, false);
				RectTransform dotRect = dot.GetComponent<RectTransform>();
				dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
				dotRect.pivot = new Vector2(0.5f, 0.5f);
				dotRect.anchoredPosition = new Vector2(side * (totalWidth * 0.5f + 6f), 0f);
				dotRect.sizeDelta = new Vector2(6f, 6f);
				Image dotImage = dot.GetComponent<Image>();
				dotImage.sprite = GetItemPageDotSprite();
				dotImage.color = ItemPageHotspotColor;
				dotImage.raycastTarget = false;
			}
		}

		/// <summary>描述 + 底部那句「点圈看记忆」的提示。</summary>
		private void AddItemPageDescription(RectTransform paper, NarrativeItemConfig item, float paperWidth,
			float offsetY)
		{
			TextMeshProUGUI desc = CreateOverlayText(paper, item.Description, ItemPageDescFontSize,
				Vector2.zero, new Vector2(paperWidth * ItemPageDescWidthRatio, ItemPageDescBoxHeight),
				// TMP 没有 TopCenter：`Top` 本身就是「水平居中 + 垂直顶部」。
				TextAlignmentOptions.Top);
			RectTransform descRect = desc.rectTransform;
			descRect.anchorMin = new Vector2(0.5f, 1f);
			descRect.anchorMax = new Vector2(0.5f, 1f);
			descRect.pivot = new Vector2(0.5f, 1f);
			descRect.anchoredPosition = new Vector2(0f, -ItemPageDescTop + offsetY);
			desc.overflowMode = TextOverflowModes.Overflow;
			desc.color = new Color(0.42f, 0.34f, 0.25f, 1f);
			// 描述可能很长（比如布布那件），给它一条下限：字缩到 21 还放不下才溢出。
			desc.enableAutoSizing = true;
			desc.fontSizeMin = ItemPageDescMinFontSize;
			desc.fontSizeMax = ItemPageDescFontSize;
			// 行距按字号给：28 的字配 12 会挤成一团。
			desc.lineSpacing = 14f;

			string hint = BuildItemPageHint(item);
			if (string.IsNullOrEmpty(hint))
			{
				return;
			}

			TextMeshProUGUI hintText = CreateOverlayText(paper, hint, ItemPageHintFontSize,
				Vector2.zero, new Vector2(paperWidth * 0.94f, ItemPageHintBoxHeight),
				TextAlignmentOptions.Center);
			RectTransform hintRect = hintText.rectTransform;
			hintRect.anchorMin = new Vector2(0.5f, 1f);
			hintRect.anchorMax = new Vector2(0.5f, 1f);
			hintRect.pivot = new Vector2(0.5f, 1f);
			hintRect.anchoredPosition = new Vector2(0f, -ItemPageHintTop + offsetY);
			hintText.enableWordWrapping = false;
			hintText.overflowMode = TextOverflowModes.Overflow;
			hintText.color = new Color(0.63f, 0.51f, 0.34f, 1f);
			hintText.characterSpacing = 6f;
		}

		/// <summary>
		/// 底部提示。有热点就教玩家点圈；没有热点但有可检查部件就指一下下面的按钮；
		/// 纯粹是「捡起来看一眼」的干扰项就什么也不写（它没有部件，写这句反而误导）。
		/// </summary>
		private string BuildItemPageHint(NarrativeItemConfig item)
		{
			int partCount = item.PartIds == null ? 0 : item.PartIds.Count;
			if (partCount == 0)
			{
				return string.Empty;
			}

			int hotCount = 0;
			int listCount = 0;
			foreach (string partId in item.PartIds)
			{
				NarrativeItemPartConfig part = NarrativeConfigMgr.Instance.GetNarrativeItemPart(partId);
				if (part == null)
				{
					continue;
				}

				if (HasItemPageHotspot(item, part))
				{
					hotCount++;
				}
				else
				{
					listCount++;
				}
			}

			if (hotCount > 0 && listCount > 0)
			{
				return "点击圈出的部位，看看与它有关的记忆　·　另有 " + listCount + " 处见下方";
			}

			if (hotCount > 0)
			{
				return "点击圈出的部位，看看与它有关的记忆";
			}

			if (listCount > 0)
			{
				return "选择一个部件继续检查　·　发现过的记忆不会重复获得";
			}

			return string.Empty;
		}

		/// <summary>纸上放不下的部件（没有热点坐标 / 没有物品图）排成一排小按钮。</summary>
		private void AddItemPageChips(RectTransform paper, List<NarrativeItemPartConfig> listParts,
			float chipTop)
		{
			if (listParts == null || listParts.Count == 0)
			{
				return;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			// 字放大之后按钮也得跟着长：按数量均分纸面可用宽度，再夹在 150~196 之间。
			// 4 个部件（收录机）落在 194，2 个（磁带）落在 196。
			float available = paper.sizeDelta.x * 0.94f;
			float chipWidth = Mathf.Clamp(
				(available - (listParts.Count - 1) * ItemPageChipGap) / listParts.Count,
				ItemPageChipMinWidth, ItemPageChipMaxWidth);
			float totalWidth = listParts.Count * chipWidth + (listParts.Count - 1) * ItemPageChipGap;
			float startX = -totalWidth * 0.5f + chipWidth * 0.5f;
			for (int i = 0; i < listParts.Count; i++)
			{
				NarrativeItemPartConfig part = listParts[i];
				string memoryId = part.MemoryId;
				string label = string.IsNullOrWhiteSpace(memoryId)
					? "查看部件：" + part.PartName
					: (saveData.HasMemory(memoryId) ? "已发现：" : "检查部件：") + part.PartName;
				// 这些部件在图上没有位置，记忆飞入就从页面中心飞（source 传 null，落到 Panel 上取中心）。
				TextMeshProUGUI chipText = CreateOverlayButton(paper, label,
					new Vector2(startX + i * (chipWidth + ItemPageChipGap), -chipTop),
					new Vector2(chipWidth, ItemPageChipHeight),
					() => InspectNarrativeItemPart(memoryId, null),
					true, ItemPageChipFontSize);
				// 「检查部件：磁带仓」8 个字在 22 号下 ≈ 176，边距一扣就顶出来，给条缩字下限。
				chipText.enableAutoSizing = true;
				chipText.fontSizeMin = ItemPageChipMinFontSize;
				chipText.fontSizeMax = ItemPageChipFontSize;
			}
		}

		/// <summary>右上角的关闭贴纸。压在纸面右上角，不占页签带。</summary>
		private void AddItemPageCloseButton(RectTransform pageRect)
		{
			var go = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(pageRect, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
			rect.pivot = new Vector2(1f, 1f);
			rect.anchoredPosition = new Vector2(-(ItemPagePadRight + 14f), -(ItemPagePadTop + 8f));
			rect.sizeDelta = new Vector2(66f, 41f);
			Image image = go.GetComponent<Image>();
			image.preserveAspect = false;
			image.raycastTarget = true;
			ApplyHudSprite(image, "setting_close_btn", false, gameObject.GetCancellationTokenOnDestroy());
			Button button = go.GetComponent<Button>();
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1f, 0.95f, 0.82f, 1f);
			colors.pressedColor = new Color(0.9f, 0.8f, 0.62f, 1f);
			colors.selectedColor = colors.highlightedColor;
			button.colors = colors;
			button.onClick.AddListener(CloseItemPage);
		}

		/// <summary>部件热点：金色虚线圈 + 下面一张写着部件名的小纸片。</summary>
		private void CreateItemPageHotspots(RectTransform imageRect, Vector2 displaySize,
			List<NarrativeItemPartConfig> parts)
		{
			if (parts == null)
			{
				return;
			}

			for (int i = 0; i < parts.Count; i++)
			{
				NarrativeItemPartConfig part = parts[i];
				if (part == null)
				{
					continue;
				}

				var go = new GameObject("Hotspot_" + part.Id, typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(imageRect, false);
				RectTransform rect = go.GetComponent<RectTransform>();
				rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
				rect.pivot = new Vector2(0.5f, 0.5f);
				rect.anchoredPosition = new Vector2((part.HotspotX - 0.5f) * displaySize.x,
					(part.HotspotY - 0.5f) * displaySize.y);
				rect.sizeDelta = new Vector2(ItemPageHotspotSize, ItemPageHotspotSize);
				// 点击区域自己完全透明，显眼的是下面那两个子节点。
				Image hit = go.GetComponent<Image>();
				hit.color = new Color(1f, 1f, 1f, 0f);
				hit.raycastTarget = true;

				var glow = new GameObject("Glow", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
				glow.transform.SetParent(rect, false);
				RectTransform glowRect = glow.GetComponent<RectTransform>();
				glowRect.anchorMin = Vector2.zero;
				glowRect.anchorMax = Vector2.one;
				glowRect.offsetMin = new Vector2(-10f, -10f);
				glowRect.offsetMax = new Vector2(10f, 10f);
				Image glowImage = glow.GetComponent<Image>();
				glowImage.sprite = GetEmergeHaloSprite();
				glowImage.color = new Color(1f, 0.86f, 0.58f, 0.28f);
				glowImage.raycastTarget = false;
				CanvasGroup glowGroup = glow.GetComponent<CanvasGroup>();
				glowGroup.blocksRaycasts = false;

				var ring = new GameObject("Ring", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
				ring.transform.SetParent(rect, false);
				RectTransform ringRect = ring.GetComponent<RectTransform>();
				ringRect.anchorMin = Vector2.zero;
				ringRect.anchorMax = Vector2.one;
				ringRect.offsetMin = new Vector2(-4f, -4f);
				ringRect.offsetMax = new Vector2(4f, 4f);
				Image ringImage = ring.GetComponent<Image>();
				ringImage.sprite = GetItemPageRingSprite();
				ringImage.color = ItemPageHotspotColor;
				ringImage.raycastTarget = false;
				CanvasGroup ringGroup = ring.GetComponent<CanvasGroup>();
				ringGroup.blocksRaycasts = false;
				// 呼吸：圈和光一起亮一下暗一下，别做成转圈 —— 转圈在静态画面上像加载中。
				DOTween.To(() => ringGroup.alpha, alpha =>
					{
						if (ringGroup != null)
						{
							ringGroup.alpha = alpha;
						}
					}, 0.45f, ItemPageHotspotPulseSeconds)
					.SetEase(Ease.InOutSine)
					.SetLoops(-1, LoopType.Yoyo)
					.SetUpdate(true)
					.SetLink(ring);
				DOTween.To(() => glowGroup.alpha, alpha =>
					{
						if (glowGroup != null)
						{
							glowGroup.alpha = alpha;
						}
					}, 1f, ItemPageHotspotPulseSeconds)
					.SetEase(Ease.InOutSine)
					.SetLoops(-1, LoopType.Yoyo)
					.SetUpdate(true)
					.SetLink(glow);

				AddItemPageHotspotTag(rect, part.PartName);

				Button button = go.GetComponent<Button>();
				button.transition = Selectable.Transition.ColorTint;
				ColorBlock colors = button.colors;
				colors.normalColor = Color.white;
				colors.highlightedColor = new Color(1f, 0.93f, 0.72f, 1f);
				colors.pressedColor = new Color(0.92f, 0.8f, 0.55f, 1f);
				colors.selectedColor = colors.highlightedColor;
				button.colors = colors;
				string memoryId = part.MemoryId;
				button.onClick.AddListener(() => InspectNarrativeItemPart(memoryId, rect));
			}
		}

		/// <summary>圈下面那张写部件名的小纸片。宽度按文字量算，别固定死。</summary>
		private static void AddItemPageHotspotTag(RectTransform hotspot, string partName)
		{
			if (string.IsNullOrWhiteSpace(partName))
			{
				return;
			}

			var go = new GameObject("Tag", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(hotspot, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 1f);
			Image background = go.GetComponent<Image>();
			background.color = ItemPageTagColor;
			background.raycastTarget = false;

			TextMeshProUGUI label = CreateOverlayText(go.transform, partName, ItemPageHotspotTagFontSize,
				Vector2.zero, new Vector2(ItemPageHotspotTagMaxWidth, ItemPageHotspotTagHeight),
				TextAlignmentOptions.Center);
			label.enableWordWrapping = false;
			label.overflowMode = TextOverflowModes.Overflow;
			label.color = new Color(0.48f, 0.36f, 0.2f, 1f);
			label.ForceMeshUpdate();
			float width = Mathf.Clamp(label.preferredWidth + 20f, 56f, ItemPageHotspotTagMaxWidth);
			rect.sizeDelta = new Vector2(width, ItemPageHotspotTagHeight);
			rect.anchoredPosition = new Vector2(0f, -ItemPageHotspotSize * 0.5f - 5f);
			RectTransform labelRect = label.rectTransform;
			labelRect.sizeDelta = new Vector2(width, ItemPageHotspotTagHeight);
		}

		/// <summary>部件是否能在图上圈出来（有物品图 + 坐标落在 0~1）。</summary>
		private static bool HasItemPageHotspot(NarrativeItemConfig item, NarrativeItemPartConfig part)
		{
			if (part == null || item == null)
			{
				return false;
			}

			if (string.IsNullOrWhiteSpace(item.ImageId) || item.ImageId == "placeholder" ||
				!GameModule.ResourceModule.CheckLocationValid(item.ImageId))
			{
				return false;
			}

			return part.HotspotX >= 0f && part.HotspotX <= 1f && part.HotspotY >= 0f && part.HotspotY <= 1f;
		}

		private void CloseItemPage()
		{
			if (m_overlay == null)
			{
				return;
			}

			// 检查门还没走完就不许关，只提示还差几处（见 BattleMainUI_ItemInspectGate）。
			if (!CanCloseItemPage())
			{
				return;
			}

			Transform panel = m_overlay.transform.Find("Panel");
			CanvasGroup group = panel != null ? panel.GetComponent<CanvasGroup>() : null;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiPanelClose, DGame.AudioType.UISound);
			if (group == null)
			{
				DestroyOverlay();
				return;
			}

			TweenItemPageFade(group, 0f, ItemPageCloseFadeSeconds, DestroyOverlay);
		}

		/// <summary>整页淡入/淡出。翻页不该用位移，页面和对话纸是同一个位置，滑动会穿帮。</summary>
		private static void TweenItemPageFade(CanvasGroup group, float target, float seconds,
			TweenCallback onComplete = null)
		{
			if (group == null)
			{
				return;
			}

			group.DOKill();
			DOTween.To(() => group.alpha, alpha =>
				{
					if (group != null)
					{
						group.alpha = alpha;
					}
				}, target, seconds)
				.SetEase(Ease.OutQuad)
				.SetUpdate(true)
				.SetLink(group.gameObject)
				.OnComplete(onComplete);
		}

		/// <summary>虚线圆环：18 段，实 62%、空 38%。缩到 74px 显示时正好是一圈手画的虚线圈。</summary>
		private static Sprite GetItemPageRingSprite()
		{
			if (s_itemPageRingSprite != null)
			{
				return s_itemPageRingSprite;
			}

			const int size = 256;
			const int dashCount = 18;
			const float dashFill = 0.62f;
			var pixels = new Color32[size * size];
			float center = size * 0.5f;
			float radius = center - 5f;
			float inner = radius - 5f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = x + 0.5f - center;
					float dy = y + 0.5f - center;
					float distance = Mathf.Sqrt(dx * dx + dy * dy);
					float band = Mathf.Min(Mathf.Clamp01((radius - distance) / 1.5f),
						Mathf.Clamp01((distance - inner) / 1.5f));
					float angle = Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f;
					float phase = Mathf.Repeat(angle * dashCount, 1f);
					float alpha = phase < dashFill ? band : 0f;
					pixels[y * size + x] = new Color32(255, 255, 255,
						(byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
				}
			}

			s_itemPageRingSprite = CreateSprite("ItemPageHotspotRing", size, pixels);
			return s_itemPageRingSprite;
		}

		/// <summary>分隔线两端的小圆点。</summary>
		private static Sprite GetItemPageDotSprite()
		{
			if (s_itemPageDotSprite != null)
			{
				return s_itemPageDotSprite;
			}

			const int size = 32;
			var pixels = new Color32[size * size];
			float center = size * 0.5f;
			float radius = center - 1f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float alpha = Mathf.Clamp01((radius - Distance(x, y, center)) / 1.2f);
					pixels[y * size + x] = new Color32(255, 255, 255,
						(byte)Mathf.RoundToInt(alpha * 255f));
				}
			}

			s_itemPageDotSprite = CreateSprite("ItemPageDot", size, pixels);
			return s_itemPageDotSprite;
		}
	}
}
