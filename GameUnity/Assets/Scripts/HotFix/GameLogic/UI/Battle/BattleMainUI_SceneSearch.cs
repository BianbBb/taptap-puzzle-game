using System;
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
	/// 场景探索玩法：某一幕要求在场景里点东西找目标（例：第二幕在仓库翻出老式座钟）。
	///
	/// 走 `StoryNode.TurnType = "SceneSearch"` 这一档，节点本身不播对白，
	/// 进入后把全屏点击拦截器关掉、接管输入，玩家点中 `isTarget` 的交互点后推进到
	/// `DefaultNextNodeId`。交互点来自 `场景交互点配置表`，坐标是 0~1 归一化，与屏幕分辨率无关。
	///
	/// 三条约定（详见 docs/物品栏与场景拾取约定.md）：
	/// 1. **只有 ActionId 会动物品栏**。干扰项配 itemId 只弹"看一眼"的详情浮层，物品栏不变。
	/// 2. 目标点**不弹物品详情**：它的使命是进物品栏 + 结束探索，紧接着的节点会让玩家自己打开物品栏拆部件。
	/// 3. 每个交互点点过一次就记 `hotspot:`&lt;id&gt; 的 flag，重进不会重复给记忆，也不会重复呼吸闪烁。
	/// </summary>
	public partial class BattleMainUI
	{
		private const string SceneSearchTurnType = "SceneSearch";
		private const string SceneHotspotRootName = "SceneHotspots";
		private const string SceneHotspotFlagPrefix = "hotspot:";
		private const string SceneSearchHintText = "点击场景中可疑的物件";

		private RectTransform m_rtSceneHotspotRoot;
		private readonly List<GameObject> m_sceneHotspotViews = new List<GameObject>();
		private bool m_sceneSearchActive;
		private bool m_sceneSearchTargetFound;
		// 每次开探索 +1。重开/销毁时把它推走，正在轮询的那一层 await 就会自己退出且不去推进剧情。
		private int m_sceneSearchVersion;

		private static bool IsSceneSearchTurn(StoryNodeConfig story)
		{
			if (story == null || string.IsNullOrWhiteSpace(story.TurnType))
			{
				return false;
			}

			return string.Equals(story.TurnType.Trim(), SceneSearchTurnType, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// 交互点表里这个场景一个点都没有时不接管输入 —— 否则玩家会面对一个没有任何可点物的空场景。
		/// 这种情况按普通节点走 <see cref="AutoAdvanceStoryNodeAsync"/>，配表问题会在日志里留痕。
		/// </summary>
		private bool ShouldRunSceneSearch(StoryNodeConfig story)
		{
			if (!IsSceneSearchTurn(story))
			{
				return false;
			}

			string backgroundId = ResolveSceneHotspotBackgroundId(story);
			int count = NarrativeConfigMgr.Instance.GetSceneHotspots(backgroundId).Count;
			if (count == 0)
			{
				DLogger.Warning("SceneSearch node {0} has no hotspots for background {1}; falling back to normal flow.",
					story.Id, backgroundId);
				return false;
			}

			return true;
		}

		/// <summary>与 RefreshSceneBackgroundAsync 同一套取法：节点级优先，回落 Session 级。</summary>
		private static string ResolveSceneHotspotBackgroundId(StoryNodeConfig story)
		{
			string backgroundId = story == null ? string.Empty : story.BackgroundId;
			if (string.IsNullOrWhiteSpace(backgroundId))
			{
				backgroundId = ResolveCurrentSessionBackgroundId();
			}

			return backgroundId ?? string.Empty;
		}

		private async UniTask RunSceneSearchAsync(StoryNodeConfig story)
		{
			if (IsDestroyed || story == null)
			{
				return;
			}

			string backgroundId = ResolveSceneHotspotBackgroundId(story);
			List<SceneHotspotConfig> hotspots = NarrativeConfigMgr.Instance.GetSceneHotspots(backgroundId);

			// 探索期间对话区整个让出来：气泡和字幕都不留，否则会挡住场景里的可点物。
			if (m_dialoguePanel != null) m_dialoguePanel.SetActive(false);
			if (m_rtNpcSubtitle != null) m_rtNpcSubtitle.gameObject.SetActive(false);
			SetContinueHintVisible(false, true);
			// 关键：不关掉这个全屏点击拦截器，点哪里都只是「推进对白」，热点永远收不到点击。
			SetDialogueAdvanceCatcherActive(false);

			// 断点续玩：上一次已经点中过目标（点完就退出游戏），这里直接推进，不要把人卡在探索里。
			if (IsSceneTargetFound(hotspots))
			{
				DLogger.Info("SceneSearch: target already found, skipping exploration for node {0}.", story.Id);
				await FinishSceneSearchAsync(story);
				return;
			}

			BuildSceneHotspots(hotspots);
			ShowSceneSearchHint(SceneSearchHintText);

			// 用轮询而不是 UniTaskCompletionSource：探索中途重开/关窗口时不会有永远不返回的 await 挂着。
			// version 同时管两件事：重开时把这一层推走，以及防止旧循环在 finally 里清掉新一轮的热点。
			int version = ++m_sceneSearchVersion;
			m_sceneSearchTargetFound = false;
			m_sceneSearchActive = true;
			try
			{
				while (!m_sceneSearchTargetFound && !IsDestroyed && m_sceneSearchVersion == version)
				{
					await UniTask.Yield(PlayerLoopTiming.Update);
				}
			}
			finally
			{
				if (m_sceneSearchVersion == version)
				{
					m_sceneSearchActive = false;
					ClearSceneHotspots();
				}
			}

			if (IsDestroyed || !m_sceneSearchTargetFound || m_sceneSearchVersion != version)
			{
				return;
			}

			await FinishSceneSearchAsync(story);
		}

		/// <summary>
		/// 从探索里强行退出（重开一局、窗口销毁）。只负责把这一层 await 和屏幕上的热点收掉，
		/// **不推进剧情** —— 谁调用谁决定接下来去哪。
		/// </summary>
		private void AbortSceneSearch()
		{
			m_sceneSearchVersion++;
			m_sceneSearchTargetFound = false;
			m_sceneSearchActive = false;
			ClearSceneHotspots();
		}

		private async UniTask FinishSceneSearchAsync(StoryNodeConfig story)
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			string nextNodeId = story.DefaultNextNodeId;
			if (string.IsNullOrWhiteSpace(nextNodeId))
			{
				DLogger.Error("SceneSearch node has no DefaultNextNodeId: {0}", story.Id);
				return;
			}

			ExecuteNodeExitActions(saveData, story);
			await PlayPendingEmergeCutscenesAsync();
			if (IsDestroyed)
			{
				return;
			}

			saveData.AdvanceStoryNode(nextNodeId, false);
			StoryNodeConfig next = NarrativeConfigMgr.Instance.GetStoryNode(nextNodeId);
			if (next == null)
			{
				DLogger.Error("SceneSearch next node not found: {0}", nextNodeId);
				return;
			}

			await PresentStoryNodeAsync(next);
		}

		private static bool IsSceneTargetFound(IReadOnlyList<SceneHotspotConfig> hotspots)
		{
			if (hotspots == null)
			{
				return false;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			for (int i = 0; i < hotspots.Count; i++)
			{
				SceneHotspotConfig hotspot = hotspots[i];
				if (hotspot != null && hotspot.IsTarget &&
					saveData.HasStoryFlag(SceneHotspotFlagPrefix + hotspot.Id))
				{
					return true;
				}
			}

			return false;
		}

		private void BuildSceneHotspots(IReadOnlyList<SceneHotspotConfig> hotspots)
		{
			ClearSceneHotspots();
			if (hotspots == null || hotspots.Count == 0 || m_layoutEditMode)
			{
				return;
			}

			var root = new GameObject(SceneHotspotRootName, typeof(RectTransform));
			root.transform.SetParent(transform, false);
			RectTransform rootRect = root.GetComponent<RectTransform>();
			rootRect.anchorMin = Vector2.zero;
			rootRect.anchorMax = Vector2.one;
			rootRect.offsetMin = Vector2.zero;
			rootRect.offsetMax = Vector2.zero;
			m_rtSceneHotspotRoot = rootRect;
			// 压在所有场景图层和角色之上；HUD 再由 RaiseHudAboveAdvanceCatcher 提到更上层。
			rootRect.SetAsLastSibling();

			for (int i = 0; i < hotspots.Count; i++)
			{
				CreateSceneHotspot(hotspots[i]);
			}

			RaiseHudAboveAdvanceCatcher();
			DLogger.Info("SceneSearch: built {0} hotspots for background {1}.", hotspots.Count,
				hotspots[0] == null ? string.Empty : hotspots[0].BackgroundId);
		}

		private void CreateSceneHotspot(SceneHotspotConfig hotspot)
		{
			if (hotspot == null || m_rtSceneHotspotRoot == null)
			{
				return;
			}

			bool seen = NarrativeSaveData.Get.HasStoryFlag(SceneHotspotFlagPrefix + hotspot.Id);
			// 点过的干扰项不再呼吸，但仍然可点（玩家想再看一眼不用重开存档）。
			bool breathe = !seen || hotspot.IsTarget;

			var go = new GameObject("Hotspot_" + hotspot.Id, typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(m_rtSceneHotspotRoot, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			float halfWidth = Mathf.Max(0.005f, hotspot.Width) * 0.5f;
			float halfHeight = Mathf.Max(0.005f, hotspot.Height) * 0.5f;
			float centerX = Mathf.Clamp(hotspot.HotspotX, 0f, 1f);
			float centerY = Mathf.Clamp(hotspot.HotspotY, 0f, 1f);
			// 表的 y 轴向上为 0（画面顶部），UGUI anchor 的 y 也以底部为 0，这里翻一次。
			rect.anchorMin = new Vector2(centerX - halfWidth, 1f - (centerY + halfHeight));
			rect.anchorMax = new Vector2(centerX + halfWidth, 1f - (centerY - halfHeight));
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;

			Image image = go.GetComponent<Image>();
			image.color = new Color(1f, 0.86f, 0.52f, 0f);
			image.raycastTarget = true;

			Button button = go.GetComponent<Button>();
			button.transition = Selectable.Transition.None;
			button.targetGraphic = image;

			if (breathe)
			{
				image.DOFade(0.26f, 1.1f).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(go);
			}

			string hotspotId = hotspot.Id;
			button.onClick.AddListener(() => OnClickSceneHotspot(hotspotId, rect));
			m_sceneHotspotViews.Add(go);
		}

		private void OnClickSceneHotspot(string hotspotId, RectTransform source)
		{
			if (IsDestroyed || !m_sceneSearchActive)
			{
				return;
			}

			// 物品详情浮层开着时不该再响应场景点击；点穿的情况在 overlay 里已经挡住，这里是第二道保险。
			if (m_overlay != null && m_overlay.activeInHierarchy)
			{
				return;
			}

			SceneHotspotConfig hotspot = FindSceneHotspot(hotspotId);
			if (hotspot == null)
			{
				return;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			string flag = SceneHotspotFlagPrefix + hotspot.Id;
			bool firstTime = !saveData.HasStoryFlag(flag);

			// ActionId 是唯一会动物品栏的入口（目标的 GrantItem 走这里）。
			// 用 flag 当幂等键：重复点同一个点不会把物品栏刷新一遍。
			if (!string.IsNullOrWhiteSpace(hotspot.ActionId))
			{
				try
				{
					var grantedItems = new List<string>();
					m_actionService.Execute(new[] { hotspot.ActionId }, flag + "|action", null, true, null,
						grantedItems);
					// 物品就从玩家点的那一下起飞，落袋时才是「进背包」。
					if (grantedItems.Count > 0)
					{
						PlayItemGrantFlyIn(grantedItems[grantedItems.Count - 1], source);
					}
					else
					{
						RefreshItemSlot();
					}
				}
				catch (Exception exception)
				{
					DLogger.Error("Scene hotspot action failed: {0}", exception.Message);
				}
			}

			if (firstTime && !string.IsNullOrWhiteSpace(hotspot.MemoryId))
			{
				GrantMemoryFromSceneHotspot(hotspot.MemoryId, source);
			}

			if (firstTime)
			{
				saveData.AddStoryFlag(flag);
			}

			// 目标点不弹详情：它的下一步是「进物品栏 + 结束探索」，
			// 紧接着的节点会让玩家自己打开物品栏拆部件（这条链路比弹窗更顺）。
			if (hotspot.IsTarget)
			{
				m_sceneSearchTargetFound = true;
				return;
			}

			if (!string.IsNullOrWhiteSpace(hotspot.ItemId))
			{
				SoundConfigMgr.Instance.PlaySfx(SfxNames.ItemPanelIn);
				ShowNarrativeItem(hotspot.ItemId);
			}
		}

		/// <summary>
		/// 和 <see cref="InspectNarrativeItemPart"/> 同一套链路：加记忆 → 扫涌现 → 飞入记忆槽 → 播涌现过场。
		/// 场景热点绕过了 CommitTurn，涌现不会派事件，所以过场要在这里补触发。
		/// </summary>
		private void GrantMemoryFromSceneHotspot(string memoryId, RectTransform source)
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (!saveData.AddMemories(new[] { memoryId }))
			{
				return;
			}

			SoundConfigMgr.Instance.PlaySfx(SfxNames.ItemInspect);
			IReadOnlyList<string> emergedEmotions = m_narrativeService.ScanEmergenceAndApply(saveData);
			RefreshUI();
			PlayItemMemoryFlyIn(memoryId, source);
			for (int i = 0; i < emergedEmotions.Count; i++)
			{
				EnqueueEmergeCutscene(emergedEmotions[i]);
			}

			if (emergedEmotions.Count > 0)
			{
				PlayPendingEmergeCutscenesAsync().Forget();
			}
		}

		private static SceneHotspotConfig FindSceneHotspot(string hotspotId)
		{
			if (string.IsNullOrWhiteSpace(hotspotId))
			{
				return null;
			}

			IReadOnlyList<SceneHotspotConfig> all = NarrativeConfigMgr.Instance.AllSceneHotspots;
			for (int i = 0; i < all.Count; i++)
			{
				if (all[i] != null && string.Equals(all[i].Id, hotspotId, StringComparison.Ordinal))
				{
					return all[i];
				}
			}

			return null;
		}

		/// <summary>底部居中的一行淡提示，挂在热点根节点下，探索结束时一起销毁。</summary>
		private void ShowSceneSearchHint(string text)
		{
			if (string.IsNullOrWhiteSpace(text) || m_rtSceneHotspotRoot == null)
			{
				return;
			}

			var go = new GameObject("SceneSearchHint", typeof(RectTransform), typeof(TextMeshProUGUI),
				typeof(CanvasGroup));
			go.transform.SetParent(m_rtSceneHotspotRoot, false);
			go.transform.SetAsLastSibling();
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.5f, 0f);
			rect.anchorMax = new Vector2(0.5f, 0f);
			rect.pivot = new Vector2(0.5f, 0f);
			rect.anchoredPosition = new Vector2(0f, 48f);
			rect.sizeDelta = new Vector2(720f, 96f);

			TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
			label.text = text;
			label.fontSize = 48f;
			label.alignment = TextAlignmentOptions.Center;
			// 提示压在场景图上，纸色（深褐）在仓库里几乎看不清，这里直接用白字。
			label.color = Color.white;
			label.raycastTarget = false;

			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
			// 白字比褐字显眼，呼吸的低点抬到 0.6，否则淡下去那一瞬间像糊了一层灰。
			group.DOFade(1f, 0.4f).SetUpdate(true).SetLink(go)
				.OnComplete(() => group.DOFade(0.6f, 1.2f).SetUpdate(true).SetLink(go));
		}

		private void ClearSceneHotspots()
		{
			for (int i = 0; i < m_sceneHotspotViews.Count; i++)
			{
				GameObject view = m_sceneHotspotViews[i];
				if (view == null)
				{
					continue;
				}

				view.transform.DOKill();
				// 基类自己有个无参 Destroy()，这里必须写全名才拿得到 MonoBehaviour 的静态版本。
				UnityEngine.Object.Destroy(view);
			}

			m_sceneHotspotViews.Clear();
			if (m_rtSceneHotspotRoot != null)
			{
				m_rtSceneHotspotRoot.DOKill();
				UnityEngine.Object.Destroy(m_rtSceneHotspotRoot.gameObject);
				m_rtSceneHotspotRoot = null;
			}
		}
	}
}
