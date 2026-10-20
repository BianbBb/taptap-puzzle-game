using System;
using Cysharp.Threading.Tasks;
using DGame;
using DG.Tweening;
using GameProto;
using TMPro;
using UnityEngine;

namespace GameLogic
{
	/// <summary>
	/// 物品检查门：节点台词播完后，强制把物品页翻出来，玩家必须看过指定数量的部位才放行。
	///
	/// 为什么需要它：物品是 OnEnter 自动进背包的，原来玩家一路点屏幕就能越过整个「检查」环节，
	/// 而下一步 NPC 已经在问「你看出来什么了」。配置侧只写一个词（<c>inspectGate</c>）：
	/// - 空    = 不拦
	/// - Any   = 至少看 1 个部位
	/// - All   = 全部部位
	/// 只对「本节点 OnEnter 刚授予的物品」生效，避免误拦手里原本就有的旧物件。
	///
	/// 放行条件是**还差几个**而不是绝对数量：重进节点时已经看过的部位不重复要求，
	/// 全部看过就直接放行，不会卡存档。
	/// </summary>
	public partial class BattleMainUI
	{
		private const string InspectGateAny = "Any";
		private const string InspectGateAll = "All";
		/// <summary>等页面淡出关闭的上限：关闭动画是 0.14s，留一点余量。</summary>
		private const float InspectGateCloseTimeout = 0.6f;

		private sealed class ItemInspectGateState
		{
			public string ItemId;
			/// <summary>本次还需要发现几个部位。</summary>
			public int Required;
			public int Discovered;
			public bool Satisfied;
		}

		private ItemInspectGateState m_itemInspectGate;

		private static bool IsInspectGateEnabled(StoryNodeConfig story)
		{
			if (story == null || string.IsNullOrWhiteSpace(story.InspectGate))
			{
				return false;
			}

			string gate = story.InspectGate.Trim();
			return string.Equals(gate, InspectGateAny, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(gate, InspectGateAll, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// 跑一次检查门。返回 false 表示这次呈现已被打断（切场景 / 重开），调用方应直接返回。
		/// <paramref name="grantedItemId"/> 是本节点刚授予的物品；为空时说明这一句没有新东西，直接放行。
		/// </summary>
		private async UniTask<bool> RunItemInspectGateAsync(StoryNodeConfig story, string grantedItemId,
			int presentVersion)
		{
			if (!IsInspectGateEnabled(story) || string.IsNullOrWhiteSpace(grantedItemId))
			{
				return true;
			}

			// 入库演出还没落袋就别抢画面：物品页会盖住背包，飞行图标会飞到看不见的地方。
			while (m_itemGrantFlyActive && !IsDestroyed && presentVersion == m_dialoguePresentVersion)
			{
				await UniTask.Yield(PlayerLoopTiming.Update);
			}

			if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
			{
				return false;
			}

			NarrativeItemConfig item = NarrativeConfigMgr.Instance.GetNarrativeItem(grantedItemId);
			if (item == null)
			{
				return true;
			}

			int remaining = CountRemainingInspectParts(item, story);
			if (remaining <= 0)
			{
				// 部位都看过了 / 这个物件根本没有可查的部件：不拦。
				return true;
			}

			m_itemInspectGate = new ItemInspectGateState
			{
				ItemId = grantedItemId,
				Required = remaining,
			};

			try
			{
				SoundConfigMgr.Instance.PlaySfx(SfxNames.ItemPanelIn);
				// 走和玩家自己点背包完全相同的入口，两边的页面/热点才不会是两套 UI。
				ShowNarrativeItem(grantedItemId);
				ShowInspectGateProgressHint();

				while (!IsDestroyed && presentVersion == m_dialoguePresentVersion &&
					m_itemInspectGate != null && !m_itemInspectGate.Satisfied)
				{
					// 页面被别处销毁（重开 / 剧情动作失败）就别等了，避免挂在半空。
					if (m_overlay == null || !m_overlay.activeInHierarchy)
					{
						break;
					}

					await UniTask.Yield(PlayerLoopTiming.Update);
				}

				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return false;
				}

				// 等页面淡出收干净再放行，否则下一句会对着一张还在渐隐的纸说话。
				float waited = 0f;
				while (m_overlay != null && !IsDestroyed && waited < InspectGateCloseTimeout)
				{
					waited += Time.unscaledDeltaTime;
					await UniTask.Yield(PlayerLoopTiming.Update);
				}

				return !IsDestroyed && presentVersion == m_dialoguePresentVersion;
			}
			finally
			{
				m_itemInspectGate = null;
			}
		}

		/// <summary>
		/// 按「本次还需要发现几个记忆」算还差几个部位。重进节点时已经看过的部位不重复要求，
		/// 所以这个函数返回 0 表示可以直接放行，不会卡存档。
		/// </summary>
		private static int CountRemainingInspectParts(NarrativeItemConfig item, StoryNodeConfig story)
		{
			if (item == null || item.PartIds == null || item.PartIds.Count == 0)
			{
				return 0;
			}

			bool all = story != null && !string.IsNullOrWhiteSpace(story.InspectGate) &&
				string.Equals(story.InspectGate.Trim(), InspectGateAll, StringComparison.OrdinalIgnoreCase);
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			int total = 0;
			int alreadyKnown = 0;
			for (int i = 0; i < item.PartIds.Count; i++)
			{
				NarrativeItemPartConfig part = NarrativeConfigMgr.Instance.GetNarrativeItemPart(item.PartIds[i]);
				if (part == null || string.IsNullOrWhiteSpace(part.MemoryId))
				{
					continue;
				}

				total++;
				if (saveData.HasMemory(part.MemoryId))
				{
					alreadyKnown++;
				}
			}

			int remaining = total - alreadyKnown;
			if (remaining <= 0)
			{
				return 0;
			}

			// Any 只要再发现一个；All 要补全所有没看过的。
			return all ? remaining : 1;
		}

		/// <summary>
		/// 部件被点开时调这个。返回 true 表示这次点击刚好把这个检查门走完了。
		/// </summary>
		private bool NotifyInspectGateProgress()
		{
			if (m_itemInspectGate == null || m_itemInspectGate.Satisfied)
			{
				return false;
			}

			m_itemInspectGate.Discovered++;
			RefreshInspectGateProgressHint();
			if (m_itemInspectGate.Discovered < m_itemInspectGate.Required)
			{
				return false;
			}

			m_itemInspectGate.Satisfied = true;
			CloseItemPage();
			return true;
		}

		/// <summary>
		/// 关闭按钮的拦截：还没看完就不许关，只提示还差几个。
		/// 返回 true 表示已放行（可以正常关闭）。
		/// </summary>
		private bool CanCloseItemPage()
		{
			if (m_itemInspectGate == null || m_itemInspectGate.Satisfied)
			{
				return true;
			}

			int remaining = m_itemInspectGate.Required - m_itemInspectGate.Discovered;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiError, DGame.AudioType.UISound);
			ShowInspectGateHint("还有 " + Mathf.Max(1, remaining) + " 处没有查看");
			return false;
		}

		private void ShowInspectGateProgressHint()
		{
			if (m_itemInspectGate == null)
			{
				return;
			}

			ShowInspectGateHint("点击圈出的部位继续检查");
		}

		private void RefreshInspectGateProgressHint()
		{
			if (m_itemInspectGate == null || m_itemInspectGate.Satisfied)
			{
				return;
			}

			int remaining = m_itemInspectGate.Required - m_itemInspectGate.Discovered;
			if (remaining > 0)
			{
				ShowInspectGateHint("还有 " + remaining + " 处没有查看");
			}
		}

		/// <summary>物品页底部那一行临时提示：1.6s 自己淡掉，不建常驻节点。</summary>
		private void ShowInspectGateHint(string text)
		{
			if (string.IsNullOrWhiteSpace(text) || m_overlay == null)
			{
				return;
			}

			Transform page = m_overlay.transform.Find("Panel");
			if (page == null)
			{
				return;
			}

			Transform existing = page.Find("InspectGateHint");
			if (existing != null)
			{
				UnityEngine.Object.Destroy(existing.gameObject);
			}

			TextMeshProUGUI label = CreateOverlayText(page, text, 20f, Vector2.zero,
				new Vector2(560f, 30f), TextAlignmentOptions.Center);
			if (label == null)
			{
				return;
			}

			GameObject go = label.gameObject;
			go.name = "InspectGateHint";
			RectTransform rect = label.rectTransform;
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
			rect.pivot = new Vector2(0.5f, 0f);
			rect.anchoredPosition = new Vector2(0f, 96f);
			rect.sizeDelta = new Vector2(560f, 30f);
			label.color = new Color(0.62f, 0.36f, 0.22f, 1f);
			label.raycastTarget = false;
			go.transform.SetAsLastSibling();

			CanvasGroup group = go.AddComponent<CanvasGroup>();
			group.blocksRaycasts = false;
			group.interactable = false;
			group.alpha = 0f;
			DOTweenTweenFade(group);
		}

		private static void DOTweenTweenFade(CanvasGroup group)
		{
			if (group == null)
			{
				return;
			}

			DOTween.To(() => group.alpha, alpha =>
				{
					if (group != null)
					{
						group.alpha = alpha;
					}
				}, 1f, 0.15f)
				.SetUpdate(true)
				.SetLink(group.gameObject)
				.OnComplete(() =>
				{
					if (group == null)
					{
						return;
					}

					DOTween.To(() => group.alpha, alpha =>
						{
							if (group != null)
							{
								group.alpha = alpha;
							}
						}, 0f, 0.45f)
						.SetDelay(1.15f)
						.SetUpdate(true)
						.SetLink(group.gameObject)
						.OnComplete(() =>
						{
							if (group != null)
							{
								UnityEngine.Object.Destroy(group.gameObject);
							}
						});
				});
		}
	}
}
