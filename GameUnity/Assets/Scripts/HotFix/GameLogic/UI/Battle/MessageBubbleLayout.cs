using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
	internal static class MessageBubbleLayout
	{
		private const float IconSize = 100f;
		private const float BubbleGap = 18f;
		private const float TopInset = 18f;
		private const float BottomInset = 18f;
		private const float TailInset = 38f;
		private const float PlainInset = 26f;
		// 气泡九切上下边框各 58px，低于 116 高时 Unity 会压缩边框、圆角变椭圆，故留 2px 余量。
		private const float MinBubbleHeight = 118f;

		internal static void Apply(RectTransform row, TextMeshProUGUI text, Image icon,
			RectTransform background, string message, bool isPlayer)
		{
			if (row == null || text == null || icon == null || background == null) return;

			ContentSizeFitter fitter = row.GetComponent<ContentSizeFitter>();
			if (fitter != null) fitter.enabled = false;
			HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
			if (group != null) group.enabled = false;

			message = message ?? string.Empty;
			text.text = message;
			text.fontSize = 26f;
			text.color = new Color(0.28f, 0.22f, 0.16f, 1f);
			text.enableWordWrapping = true;
			text.overflowMode = TextOverflowModes.Overflow;
			text.alignment = isPlayer ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;

			float bubbleMaxWidth = Mathf.Min(560f, Mathf.Max(160f, row.rect.width - IconSize - BubbleGap));
			float innerMaxWidth = bubbleMaxWidth - TailInset - PlainInset;
			float naturalWidth = text.GetPreferredValues(message, 10000f, 10000f).x;
			float textWidth = Mathf.Clamp(Mathf.Ceil(naturalWidth) + 4f, 48f, innerMaxWidth);
			float textHeight = Mathf.Max(34f, Mathf.Ceil(text.GetPreferredValues(message, textWidth, 10000f).y) + 4f);
			float bubbleWidth = textWidth + TailInset + PlainInset;
			float bubbleHeight = Mathf.Max(MinBubbleHeight, textHeight + TopInset + BottomInset);
			float rowHeight = Mathf.Max(IconSize, bubbleHeight) + 14f;

			row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);
			background.SetAsFirstSibling();
			RectTransform iconRect = icon.rectTransform;
			RectTransform textRect = text.rectTransform;
			float side = isPlayer ? 1f : 0f;
			iconRect.anchorMin = iconRect.anchorMax = new Vector2(side, 1f);
			iconRect.pivot = new Vector2(side, 1f);
			iconRect.anchoredPosition = new Vector2(0f, -7f);
			iconRect.sizeDelta = new Vector2(IconSize, IconSize);

			background.anchorMin = background.anchorMax = new Vector2(side, 1f);
			background.pivot = new Vector2(side, 1f);
			background.anchoredPosition = new Vector2(isPlayer ? -IconSize - BubbleGap : IconSize + BubbleGap, -7f);
			background.sizeDelta = new Vector2(bubbleWidth, bubbleHeight);

			textRect.anchorMin = textRect.anchorMax = new Vector2(side, 1f);
			textRect.pivot = new Vector2(side, 1f);
			// 单行时文字在气泡内垂直居中（剩余空间大），多行时退化为固定 TopInset。
			float textTopInset = Mathf.Max(TopInset, (bubbleHeight - textHeight) * 0.5f);
			textRect.anchoredPosition = new Vector2(
				isPlayer ? -IconSize - BubbleGap - TailInset : IconSize + BubbleGap + TailInset,
				-7f - textTopInset);
			textRect.sizeDelta = new Vector2(textWidth, textHeight);
			text.ForceMeshUpdate();
		}
	}
}
