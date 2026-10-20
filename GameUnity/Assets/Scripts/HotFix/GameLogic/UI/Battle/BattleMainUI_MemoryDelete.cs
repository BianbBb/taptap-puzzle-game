using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 记忆删除的「按住才碎」交互，以及清理阶段屏幕正中的呼吸提示。
	///
	/// 删除不再是一点就没：按住删除按钮，卡片开始抖且越抖越狠；满 1 秒「啵」一声，
	/// 卡片在 0.1 秒内放大到 1.5 倍并淡出消失。中途松手或指针移出即取消，
	/// 所以误触不会丢记忆，长按本身也成了「你确定要让它碎掉吗」的那一拍。
	/// </summary>
	public partial class BattleMainUI
	{
		// ---- 长按节奏（秒，走 unscaled）----
		private const float MemoryDeleteHoldSeconds = 1f;
		private const float MemoryDeleteBreakSeconds = 0.1f;
		private const float MemoryDeleteBreakScale = 1.5f;
		/// <summary>抖动角度从 1.2° 涨到 2.4°：越接近碎，抖得越狠。</summary>
		private const float MemoryDeleteShakeMinDegrees = 1.2f;
		private const float MemoryDeleteShakeMaxDegrees = 2.4f;
		private const float MemoryDeleteShakeMaxScale = 1.04f;
		/// <summary>16Hz 才是「抖」，低于 10Hz 会变成果冻式的晃。</summary>
		private const float MemoryDeleteShakeHertz = 16f;
		/// <summary>破碎淡出的起点：前 35% 只放大不下沉，「突然变大」那一下要看得见。</summary>
		private const float MemoryDeleteBreakFadeStart = 0.35f;
		/// <summary>消散尾音的音量。配表里没有这两条 sfx，PlaySfx 的 volume 入参才会生效。</summary>
		private const float MemoryDeleteTailVolume = 0.55f;

		// ---- 中央提示 ----
		private const float CleanupHintBreathPeriod = 2.2f;
		private const float CleanupHintMinAlpha = 0.45f;
		private const float CleanupHintFontSize = 30f;
		private const int CleanupHintNumberFontSize = 42;
		private const float CleanupHintWidth = 1200f;
		private const float CleanupHintHeight = 120f;

		private int m_memoryDeleteHoldSlot = -1;
		private int m_memoryDeleteHoldVersion;
		private bool m_memoryDeleteHolding;

		private TextMeshProUGUI m_memoryCleanupHint;
		private CanvasGroup m_memoryCleanupHintGroup;
		private int m_memoryCleanupHintVersion;
		private int m_memoryCleanupHintCount = -1;

		/// <summary>按下删除按钮：开始计时 + 抖动。只有记忆清理阶段才允许。</summary>
		private void BeginMemoryDeleteHold(int slotIndex)
		{
			if (m_layoutEditMode || m_overlay != null || m_memoryDeleteHolding)
			{
				return;
			}

			if (NarrativeSaveData.Get.DayPhase != NarrativeDayPhase.MemoryCleanup)
			{
				return;
			}

			if (GetMemorySlotRect(slotIndex) == null)
			{
				return;
			}

			m_memoryDeleteHolding = true;
			m_memoryDeleteHoldSlot = slotIndex;
			int version = ++m_memoryDeleteHoldVersion;
			HoldMemoryDeleteAsync(slotIndex, version).Forget();
		}

		/// <summary>松手 / 指针移出：取消这次长按，卡片立刻回正。</summary>
		private void CancelMemoryDeleteHold(int slotIndex)
		{
			if (!m_memoryDeleteHolding || slotIndex != m_memoryDeleteHoldSlot)
			{
				return;
			}

			m_memoryDeleteHolding = false;
			m_memoryDeleteHoldSlot = -1;
			m_memoryDeleteHoldVersion++;
			ResetMemorySlotShake(slotIndex);
		}

		private async UniTask HoldMemoryDeleteAsync(int slotIndex, int version)
		{
			RectTransform slotRect = GetMemorySlotRect(slotIndex);
			if (slotRect == null)
			{
				m_memoryDeleteHolding = false;
				return;
			}

			float elapsed = 0f;
			while (elapsed < MemoryDeleteHoldSeconds)
			{
				if (!IsMemoryDeleteHoldAlive(slotRect, version))
				{
					return;
				}

				ApplyMemorySlotShake(slotRect, elapsed / MemoryDeleteHoldSeconds, elapsed);
				await UniTask.Yield(PlayerLoopTiming.Update);
				elapsed += Time.unscaledDeltaTime;
			}

			if (!IsMemoryDeleteHoldAlive(slotRect, version))
			{
				return;
			}

			// 满 1 秒：锁已解开，后面这段不再接受「松手取消」——已经碎了。
			m_memoryDeleteHolding = false;
			m_memoryDeleteHoldSlot = -1;
			await BreakMemorySlotAsync(slotIndex, slotRect);
		}

		private bool IsMemoryDeleteHoldAlive(RectTransform slotRect, int version)
			=> !IsDestroyed && m_memoryDeleteHolding && version == m_memoryDeleteHoldVersion &&
				slotRect != null;

		/// <summary>抖动：绕 Z 轴摆 + 轻微膨胀。只动 rotation/scale，不碰位置，避免和 HorizontalLayoutGroup 打架。</summary>
		private static void ApplyMemorySlotShake(RectTransform slotRect, float progress, float elapsed)
		{
			if (slotRect == null)
			{
				return;
			}

			float amplitude = Mathf.Lerp(MemoryDeleteShakeMinDegrees, MemoryDeleteShakeMaxDegrees, progress);
			float angle = Mathf.Sin(elapsed * Mathf.PI * 2f * MemoryDeleteShakeHertz) * amplitude;
			slotRect.localRotation = Quaternion.Euler(0f, 0f, angle);
			slotRect.localScale = Vector3.one * Mathf.Lerp(1f, MemoryDeleteShakeMaxScale, progress);
		}

		/// <summary>破碎：0.1s 内放大到 1.5 倍并淡出，然后才真正把记忆从存档里删掉。</summary>
		private async UniTask BreakMemorySlotAsync(int slotIndex, RectTransform slotRect)
		{
			CanvasGroup group = slotRect == null ? null : slotRect.GetComponent<CanvasGroup>();
			// 两层音：pop 是「碎」的那一下，Puff 是紧跟的「散」——视觉 0.1s 就没了，
			// 声音留 0.5s 的余韵才像真的消散掉。
			SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryBreak);
			SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryDelete, volume: MemoryDeleteTailVolume);

			float elapsed = 0f;
			while (elapsed < MemoryDeleteBreakSeconds)
			{
				if (IsDestroyed || slotRect == null)
				{
					return;
				}

				float progress = Mathf.Clamp01(elapsed / MemoryDeleteBreakSeconds);
				slotRect.localRotation = Quaternion.identity;
				slotRect.localScale = Vector3.one * Mathf.Lerp(1f, MemoryDeleteBreakScale, EaseOutCubic(progress));
				if (group != null)
				{
					group.alpha = 1f - Mathf.Clamp01(
						(progress - MemoryDeleteBreakFadeStart) / (1f - MemoryDeleteBreakFadeStart));
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
				elapsed += Time.unscaledDeltaTime;
			}

			// 先复位再刷新：槽位是复用的，残留的 1.5 倍/透明会粘到下一条记忆上。
			ResetMemorySlotShake(slotIndex);

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			IReadOnlyList<string> ids = saveData.GetMemoryIdsInAcquisitionOrder();
			if (slotIndex < 0 || slotIndex >= ids.Count || !saveData.RemoveMemoryDuringCleanup(ids[slotIndex]))
			{
				return;
			}

			HideMemoryTooltip();
			RefreshUI();
			ShowMemoryCleanup();
		}

		private void ResetMemorySlotShake(int slotIndex)
		{
			RectTransform slotRect = GetMemorySlotRect(slotIndex);
			if (slotRect == null)
			{
				return;
			}

			slotRect.localRotation = Quaternion.identity;
			slotRect.localScale = Vector3.one;
			CanvasGroup group = slotRect.GetComponent<CanvasGroup>();
			if (group != null)
			{
				group.alpha = 1f;
			}
		}

		private RectTransform GetMemorySlotRect(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= m_memorySlotImages.Count)
			{
				return null;
			}

			Image slotImage = m_memorySlotImages[slotIndex];
			return slotImage == null ? null : slotImage.rectTransform;
		}

		/// <summary>清理阶段的中央提示：白字呼吸，数字随删除实时变化。返回是否是「本次刚进入清理阶段」。</summary>
		private bool ShowMemoryCleanupHint()
		{
			bool firstShow = m_memoryCleanupHint == null || !m_memoryCleanupHint.gameObject.activeInHierarchy;
			EnsureMemoryCleanupHint();
			if (m_memoryCleanupHint == null)
			{
				return false;
			}

			m_memoryCleanupHint.gameObject.SetActive(true);
			m_memoryCleanupHint.transform.SetAsLastSibling();
			RefreshMemoryCleanupHintText();
			// 已经亮着就别重启呼吸循环：每删一段都会再调到这里，重来会让呼吸相位一直跳回起点。
			if (!firstShow)
			{
				return false;
			}

			int version = ++m_memoryCleanupHintVersion;
			BreatheMemoryCleanupHintAsync(version).Forget();
			return true;
		}

		private void HideMemoryCleanupHint()
		{
			m_memoryCleanupHintVersion++;
			if (m_memoryCleanupHint != null)
			{
				m_memoryCleanupHint.gameObject.SetActive(false);
			}
		}

		private void EnsureMemoryCleanupHint()
		{
			if (m_memoryCleanupHint != null)
			{
				return;
			}

			var root = new GameObject("MemoryCleanupHint", typeof(RectTransform), typeof(CanvasGroup));
			root.transform.SetParent(transform, false);
			RectTransform rootRect = root.GetComponent<RectTransform>();
			rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
			rootRect.pivot = new Vector2(0.5f, 0.5f);
			rootRect.anchoredPosition = Vector2.zero;
			rootRect.sizeDelta = new Vector2(CleanupHintWidth, CleanupHintHeight);

			m_memoryCleanupHintGroup = root.GetComponent<CanvasGroup>();
			m_memoryCleanupHintGroup.blocksRaycasts = false;
			m_memoryCleanupHintGroup.interactable = false;
			m_memoryCleanupHintGroup.alpha = 1f;

			var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
			textGo.transform.SetParent(root.transform, false);
			RectTransform textRect = textGo.GetComponent<RectTransform>();
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = Vector2.zero;
			textRect.offsetMax = Vector2.zero;

			m_memoryCleanupHint = textGo.GetComponent<TextMeshProUGUI>();
			m_memoryCleanupHint.fontSize = CleanupHintFontSize;
			m_memoryCleanupHint.color = Color.white;
			m_memoryCleanupHint.alignment = TextAlignmentOptions.Center;
			m_memoryCleanupHint.enableWordWrapping = false;
			m_memoryCleanupHint.overflowMode = TextOverflowModes.Overflow;
			m_memoryCleanupHint.raycastTarget = false;
			// 场景底色偏亮时白字会糊，压一层很淡的黑描边只做可读性，不改变「白字」的观感。
			m_memoryCleanupHint.outlineWidth = 0.12f;
			m_memoryCleanupHint.outlineColor = new Color32(0, 0, 0, 90);
		}

		private void RefreshMemoryCleanupHintText()
		{
			if (m_memoryCleanupHint == null)
			{
				return;
			}

			m_memoryCleanupHintCount = NarrativeSaveData.Get.MemoryCount;
			m_memoryCleanupHint.text =
				$"<size={CleanupHintNumberFontSize}>（{m_memoryCleanupHintCount}/{NarrativeRules.MemoryCapacity}）</size>　" +
				"记忆数目超限，需要删除部分记忆";
		}

		/// <summary>呼吸灯：alpha 在 0.45~1 之间来回，数字一变就就地刷新文本。</summary>
		private async UniTask BreatheMemoryCleanupHintAsync(int version)
		{
			float elapsed = 0f;
			while (true)
			{
				if (IsDestroyed || version != m_memoryCleanupHintVersion ||
					m_memoryCleanupHint == null || m_memoryCleanupHintGroup == null ||
					!m_memoryCleanupHint.gameObject.activeInHierarchy)
				{
					return;
				}

				float phase = (elapsed % CleanupHintBreathPeriod) / CleanupHintBreathPeriod;
				m_memoryCleanupHintGroup.alpha = Mathf.Lerp(CleanupHintMinAlpha, 1f,
					0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f));

				if (NarrativeSaveData.Get.MemoryCount != m_memoryCleanupHintCount)
				{
					RefreshMemoryCleanupHintText();
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
				elapsed += Time.unscaledDeltaTime;
			}
		}
	}
}
