using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using DGame;
using GameProto;
using SuperScrollView;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace GameLogic
{
	public partial class BattleMainUI
	{
		private const int TYPEWRITER_MS_PER_CHAR = 100;
		private const int TYPEWRITER_PUNCTUATION_EXTRA_MS = 220;
		private const float MESSAGE_SCROLL_OVERFLOW_EPSILON = 1f;
		private const float MESSAGE_BOTTOM_PADDING = 16f;
		private const float MESSAGE_SCROLL_SENSITIVITY = 0.3f;
		private const float REPLY_PANEL_HEIGHT = 300f;
		private const int HUD_MEMORY_SLOT_COUNT = 6;
		private const string NpcPortraitPlaceholderText = "NPC角色素材";
		private static readonly Color HudPaperText = new Color(0.28f, 0.22f, 0.16f, 1f);
		private static readonly Color HudPaperPanel = new Color(0.96f, 0.93f, 0.86f, 1f);
		private static readonly Color HudPaperButton = new Color(0.93f, 0.82f, 0.62f, 1f);
		private readonly List<ReplayMessageItem> m_replyItems = new List<ReplayMessageItem>();
		private readonly List<ReplayEmoItem> m_emotionItems = new List<ReplayEmoItem>();
		private readonly List<(bool IsPlayer, string Text)> m_messages = new List<(bool, string)>();
		private readonly List<Image> m_memorySlotImages = new List<Image>();
		private readonly List<TextMeshProUGUI> m_memorySlotLabels = new List<TextMeshProUGUI>();
		private int m_messageScrollVersion;
		private int m_npcPortraitVersion;
		private int m_dialoguePresentVersion;
		private bool m_messagePinnedToLatest = true;
		private Button m_btnItemCollection;
		private readonly NarrativeDomainService m_narrativeService = new NarrativeDomainService();
		private readonly NarrativeActionService m_actionService = new NarrativeActionService();
		private readonly NarrativeStoryScheduler m_storyScheduler = new NarrativeStoryScheduler();
		private UILoopListViewWidget m_messageList;
		private GameObject m_overlay;
		private TMP_InputField m_debugNodeInput;
		private StoryNodeConfig m_currentStoryNode;
		private ReplyOptionConfig m_selectedReply;
		private string m_selectedEmotionId;
		private Image m_imgNpcPortrait;
		private TextMeshProUGUI m_tmpNpcPlaceholder;
		private Vector2 m_npcPortraitSlotSize = new Vector2(600f, 830f);
		private string m_loadedNpcPortraitLocation;
		private RectTransform m_rtSceneCharacter;
		private Image m_imgSceneCharacter;
		private string m_loadedSceneCharacterLocation;
		private int m_sceneBackgroundVersion;
#if SPINE_UNITY && SPINE_CSHARP
		private UISpineWidget m_npcSpine;
		private string m_loadedNpcSpineLocation;
		private readonly HashSet<string> m_unavailableNpcSpines = new HashSet<string>();
#endif
		private Image m_imgSceneRoot;
		private readonly List<Image> m_sceneLayerImages = new List<Image>();
		private string m_loadedSceneKey;
		private RectTransform m_overlayBodyContent;
		private GameObject m_dialoguePanel;
		private TextMeshProUGUI m_tmpNpcSubtitle;
		private Button m_btnRestart;
		private Button m_btnDialogueAdvance;
		private bool m_advanceClickPending;
		private bool m_isBusy;
		private bool m_hasReplied;
		private bool m_playerReplyShown;
		private bool m_pendingRestart;

		#region Override

		public override bool FullScreen => true;

		protected override void OnCreate()
		{
			EnsureEventSystem();
			BindNpcPortraitView();
			BindSceneBackgroundView();
			BindDialoguePanel();
			ApplyDialogueHudLayout();
			ApplyDialogueHudArt();
			BuildMemorySlots();
			ApplyChatListLayout();
			EnsureNpcSubtitle();
			EnsureRestartButton();
			EnsureDialogueAdvanceCatcher();
			InitializeLayoutEditing();

			m_tfReplayMessageNode.gameObject.SetActive(false);
			m_tfReplayEmoNode.gameObject.SetActive(false);
			m_messageList = CreateWidget<UILoopListViewWidget>(m_scrollMessage.gameObject);
			m_messageList.LoopRectView.InitListView(0, OnGetMessageItemByIndex);
			ScrollRect messageScroll = m_messageList.LoopRectView.ScrollRect;
			messageScroll.movementType = ScrollRect.MovementType.Clamped;
			messageScroll.scrollSensitivity = MESSAGE_SCROLL_SENSITIVITY;
			messageScroll.inertia = false;
			m_messageList.LoopRectView.mOnBeginDragAction = OnMessageListBeginDrag;
			m_messageList.LoopRectView.mOnEndDragAction = OnMessageListEndDrag;
			RaiseHudAboveAdvanceCatcher();

			StartStoryAsync().Forget();
		}

		protected override void OnRefresh()
		{
			RefreshUI();
		}

		protected override void OnDestroy()
		{
			m_messageScrollVersion++;
			m_npcPortraitVersion++;
			m_dialoguePresentVersion++;
			m_messagePinnedToLatest = true;
			m_memorySlotImages.Clear();
			m_memorySlotLabels.Clear();
			m_btnItemCollection = null;
			if (m_overlay != null) UnityEngine.Object.Destroy(m_overlay);
			m_replyItems.Clear();
			m_emotionItems.Clear();
			m_messages.Clear();
			m_messageList = null;
			m_currentStoryNode = null;
			m_selectedReply = null;
			m_selectedEmotionId = null;
			m_playerReplyShown = false;
			HideNpcSpine();
			m_imgNpcPortrait = null;
			m_tmpNpcPlaceholder = null;
			m_loadedNpcPortraitLocation = null;
			m_imgSceneRoot = null;
			m_sceneLayerImages.Clear();
			m_loadedSceneKey = null;
			m_rtSceneCharacter = null;
			m_imgSceneCharacter = null;
			m_loadedSceneCharacterLocation = null;
			m_overlayBodyContent = null;
			m_dialoguePanel = null;
			m_tmpNpcSubtitle = null;
			m_btnRestart = null;
			m_btnDialogueAdvance = null;
			m_advanceClickPending = false;
			m_pendingRestart = false;
			m_layoutDragTargets.Clear();
			m_layoutHint = null;
		}

		#endregion

		/// <summary>记忆变化或窗口重新显示时刷新回复解锁状态。</summary>
		public void RefreshUI()
		{
			RefreshMemorySlots();
			if (IsDestroyed || m_isBusy || m_hasReplied || m_selectedReply != null)
			{
				return;
			}

			foreach (ReplayMessageItem item in m_replyItems)
			{
				if (item != null)
				{
					ReplyOptionConfig reply = NarrativeConfigMgr.Instance.GetReply(item.ReplyId);
					if (reply != null)
					{
						SetReplyItemData(item, reply, NarrativeSaveData.Get.HasAllMemories(reply.RequiredMemory));
					}
				}
			}
		}

		private async UniTask StartStoryAsync()
		{
			if (m_isBusy)
			{
				return;
			}

			m_isBusy = true;
			try
			{
				DestroyOverlay();
				NarrativeSaveData saveData = NarrativeSaveData.Get;
				saveData.ClearInvalidCurrentSession();
				IReadOnlyList<NarrativeValidationIssue> issues = NarrativeConfigValidator.Validate(saveData);
				bool hasConfigError = false;
				foreach (NarrativeValidationIssue issue in issues)
				{
					if (issue.Severity == ENarrativeValidationSeverity.Error)
					{
						hasConfigError = true;
						DLogger.Error("Narrative config: {0}", issue);
					}
					else
					{
						DLogger.Warning("Narrative config: {0}", issue);
					}
				}

				if (hasConfigError)
				{
					ShowOverlay("剧情配置错误", "配置校验发现错误，请查看 Unity Console。", new[]
					{
						("返回标题", (Action)(() => ReturnToMainMenu()))
					});
					return;
				}

				if (saveData.DayPhase == NarrativeDayPhase.Monologue)
				{
					ShowDayMonologue();
					return;
				}

				if (saveData.DayPhase == NarrativeDayPhase.MemoryCleanup)
				{
					ShowMemoryCleanup();
					return;
				}

				if (saveData.DayPhase == NarrativeDayPhase.GameComplete)
				{
					ShowGameComplete();
					return;
				}

				if (saveData.DayPhase == NarrativeDayPhase.PrototypeContentExhausted)
				{
					ShowPrototypeContentExhausted();
					return;
				}

				if (string.IsNullOrWhiteSpace(saveData.CurrentSessionId))
				{
					NarrativeSessionConfig session = m_storyScheduler.FindNextSession(saveData);
					if (session == null)
					{
						EnterDayEnd();
						return;
					}

					string entryNodeId = string.IsNullOrWhiteSpace(saveData.CurrentNodeId)
						? session.StartNodeId : saveData.CurrentNodeId;
					if (saveData.CurrentDay < session.DayIndex)
					{
						saveData.SetDayTime(session.DayIndex, 0);
					}

					saveData.BeginSession(session.Id, entryNodeId);
				}

				string currentNodeId = saveData.CurrentNodeId;
				StoryNodeConfig story = NarrativeConfigMgr.Instance.GetStoryNode(currentNodeId);
				if (story == null)
				{
					DLogger.Error("Story node not found: {0}", currentNodeId);
					return;
				}

				if (m_currentStoryNode != null && m_currentStoryNode.Id == currentNodeId)
				{
					return;
				}

				await PresentStoryNodeAsync(story);
			}
			finally
			{
				FinishBusy();
			}
		}

		private async UniTask PresentStoryNodeAsync(StoryNodeConfig story)
		{
			if (IsDestroyed || story == null)
			{
				return;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			int skippedByGate = 0;
			while (story.MinAffection > 0 && saveData.GetAffection(story.NpcId) < story.MinAffection)
			{
				if (string.IsNullOrWhiteSpace(story.DefaultNextNodeId))
				{
					DLogger.Error("Affection-gated node has no fallback: {0}", story.Id);
					return;
				}

				string skippedNodeId = story.Id;
				string fallbackNodeId = story.DefaultNextNodeId;
				saveData.AdvanceStoryNode(fallbackNodeId, false);
				story = NarrativeConfigMgr.Instance.GetStoryNode(fallbackNodeId);
				if (story == null || ++skippedByGate > NarrativeConfigMgr.Instance.StoryNodeCount)
				{
					DLogger.Error("Invalid or cyclic affection fallback from node {0}.", skippedNodeId);
					return;
				}
			}

			m_currentStoryNode = story;
			m_playerReplyShown = false;
			m_dialoguePresentVersion++;
			int presentVersion = m_dialoguePresentVersion;
			RefreshNpcPortrait(story);
			RefreshSceneBackground(story);
			RefreshMemorySlots();
			try
			{
				m_actionService.Execute(story.OnEnterActionIds,
					NarrativeSaveData.Get.BuildTurnKey(story.Id) + "|node-enter");
			}
			catch (Exception exception)
			{
				DLogger.Error("Narrative node entry action failed: {0}", exception);
				ShowOverlay("剧情动作错误", exception.Message, new[] { ("关闭", (Action)(() => DestroyOverlay())) });
				return;
			}
			m_selectedReply = null;
			m_selectedEmotionId = null;
			m_hasReplied = false;
			SetReplyItemsActive(false);
			SetEmotionItemsActive(false);
			m_tfReplayEmoNode.gameObject.SetActive(false);
			m_tfReplayMessageNode.gameObject.SetActive(false);
			bool npcDialogue = IsNpcDialogueTurn(story) && !UsesPaperDialogue(story.NpcId);
			SetDialoguePresentationMode(npcDialogue);
			m_advanceClickPending = false;
			SetDialogueAdvanceCatcherActive(true);
			if (npcDialogue)
			{
				await PresentNpcSubtitleAsync(story, presentVersion);
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return;
				}

				await AutoAdvanceStoryNodeAsync(story, presentVersion);
				return;
			}

			await PresentNpcMessageAsync(story.NpcText, presentVersion);
			if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
			{
				return;
			}

			if (story.Replies == null || story.Replies.Count == 0)
			{
				await AutoAdvanceStoryNodeAsync(story, presentVersion);
				return;
			}

			SetDialogueAdvanceCatcherActive(false);
			m_tfReplayMessageNode.gameObject.SetActive(true);

			IEnumerable<string> orderedReplyIds = story.Replies
				.OrderBy(id => NarrativeConfigMgr.Instance.GetReply(id)?.DisplayOrder ?? int.MaxValue)
				.ThenBy(id => id, StringComparer.Ordinal);
			foreach (string replyId in orderedReplyIds)
			{
				ReplyOptionConfig reply = NarrativeConfigMgr.Instance.GetReply(replyId);
				if (reply == null)
				{
					DLogger.Error("Reply option not found: {0}", replyId);
					continue;
				}

				ReplayMessageItem item = await CreateWidgetByTypeAsync<ReplayMessageItem>(m_tfReplayMessageNode, false);
				if (IsDestroyed || item == null)
				{
					return;
				}

				SetReplyItemData(item, reply, NarrativeSaveData.Get.HasAllMemories(reply.RequiredMemory));
				item.BindClickEvent(OnClickReply, needEmptyImg: true);
				m_replyItems.Add(item);
				item.Show(true);
			}
		}

		private void OnClickReply(ReplayMessageItem item)
		{
			if (IsDestroyed || m_isBusy || m_hasReplied || m_selectedReply != null || item == null)
			{
				return;
			}

			ReplyOptionConfig reply = NarrativeConfigMgr.Instance.GetReply(item.ReplyId);
			if (reply == null)
			{
				DLogger.Error("Reply option not found: {0}", item.ReplyId);
				return;
			}

			// 点击时再次检查，避免 UI 展示状态与存档状态不一致。
			SetReplyItemData(item, reply, NarrativeSaveData.Get.HasAllMemories(reply.RequiredMemory));
			if (!item.IsUnlocked)
			{
				return;
			}

			SelectReplyAsync(reply).Forget();
		}

		private static void SetReplyItemData(ReplayMessageItem item, ReplyOptionConfig reply, bool isUnlocked)
		{
			string displayText = isUnlocked ? reply.ReplyText : reply.LockedText;
			if (!isUnlocked && string.IsNullOrWhiteSpace(displayText))
			{
				displayText = "（暂不可选）";
			}

			item.SetData(reply.Id, displayText, isUnlocked);
		}

		private async UniTask SelectReplyAsync(ReplyOptionConfig reply)
		{
			m_isBusy = true;
			m_selectedReply = reply;
			try
			{
				SetDialogueAdvanceCatcherActive(false);
				SetEmotionItemsActive(false);
				m_tfReplayMessageNode.gameObject.SetActive(false);
				ShowPlayerReplyMessage(reply.ReplyText);
				IReadOnlyList<string> available = m_narrativeService.GetAvailableEmotions(reply);
				IReadOnlyList<string> locked = m_narrativeService.GetLockedEmotions(reply);
				m_selectedEmotionId = m_narrativeService.GetDefaultEmotion(reply);
				m_tfReplayEmoNode.gameObject.SetActive(true);
				m_tfReplayEmoNode.SetAsLastSibling();
				RectTransform emoRect = m_tfReplayEmoNode as RectTransform;
				if (emoRect != null)
				{
					emoRect.sizeDelta = new Vector2(emoRect.sizeDelta.x, 120f);
				}

				HorizontalLayoutGroup emoLayout = m_tfReplayEmoNode.GetComponent<HorizontalLayoutGroup>();
				if (emoLayout == null)
				{
					emoLayout = m_tfReplayEmoNode.gameObject.AddComponent<HorizontalLayoutGroup>();
				}

				emoLayout.childAlignment = TextAnchor.MiddleCenter;
				emoLayout.spacing = 18f;
				emoLayout.childControlWidth = false;
				emoLayout.childControlHeight = false;
				emoLayout.childForceExpandWidth = false;
				emoLayout.childForceExpandHeight = false;
				if (m_btnEmoCollection != null)
				{
					m_btnEmoCollection.interactable = false;
				}

				if (m_btnItemCollection != null)
				{
					m_btnItemCollection.interactable = false;
				}

				for (int i = 0; i < available.Count; i++)
				{
					if (!await SpawnEmotionItemAsync(available[i], false))
					{
						return;
					}
				}

				for (int i = 0; i < locked.Count; i++)
				{
					if (!await SpawnEmotionItemAsync(locked[i], true))
					{
						return;
					}
				}

				ReplayEmoItem noneItem = await CreateWidgetByTypeAsync<ReplayEmoItem>(m_tfReplayEmoNode, false);
				if (IsDestroyed || noneItem == null)
				{
					return;
				}

				noneItem.SetNoneData();
				noneItem.BindClickEvent(OnClickEmotion, needEmptyImg: true);
				m_emotionItems.Add(noneItem);
				noneItem.Show(true);
			}
			finally
			{
				FinishBusy();
			}
		}

		private async UniTask<bool> SpawnEmotionItemAsync(string emotionId, bool locked)
		{
			EmotionConfig emotion = NarrativeConfigMgr.Instance.GetEmotion(emotionId);
			if (emotion == null)
			{
				DLogger.Error("Emotion config not found: {0}", emotionId);
				return true;
			}

			ReplayEmoItem item = await CreateWidgetByTypeAsync<ReplayEmoItem>(m_tfReplayEmoNode, false);
			if (IsDestroyed || item == null)
			{
				return false;
			}

			item.SetData(emotion, locked);
			item.BindClickEvent(OnClickEmotion, needEmptyImg: true);
			m_emotionItems.Add(item);
			item.Show(true);
			return true;
		}

		private async UniTask AutoAdvanceStoryNodeAsync(StoryNodeConfig story, int presentVersion)
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (story.IsSessionEnd)
			{
				ExecuteNodeExitActions(saveData, story);
				saveData.AdvanceStoryNode(string.Empty, true);
				m_currentStoryNode = null;
				await ContinueAfterSessionAsync();
				return;
			}

			if (string.IsNullOrWhiteSpace(story.DefaultNextNodeId))
			{
				DLogger.Error("Non-ending story node has no reply or default next node: {0}", story.Id);
				return;
			}

			TypewriterWaitResult wait = await WaitForAdvanceClick(presentVersion);
			if (IsDestroyed || presentVersion != m_dialoguePresentVersion ||
				wait == TypewriterWaitResult.Cancelled)
			{
				return;
			}

			string nextNodeId = story.DefaultNextNodeId;
			ExecuteNodeExitActions(saveData, story);
			saveData.AdvanceStoryNode(nextNodeId, false);
			StoryNodeConfig nextStory = NarrativeConfigMgr.Instance.GetStoryNode(nextNodeId);
			if (nextStory == null)
			{
				DLogger.Error("Default next story node not found: {0}", nextNodeId);
				return;
			}

			await PresentStoryNodeAsync(nextStory);
		}

		private void OnClickEmotion(ReplayEmoItem item)
		{
			if (IsDestroyed || m_isBusy || m_hasReplied || m_selectedReply == null || item == null)
			{
				return;
			}

			if (item.IsLocked)
			{
				ShowOverlay("情绪未解锁", "这份情绪还没有在剧情中获得。可以先选择“无”，继续推进故事。",
					new[] { ("关闭", (Action)(() => DestroyOverlay())) });
				return;
			}

			string emotionId = item.IsNone ? NarrativeRules.NoneEmotionId : item.Emotion?.Id;
			if (emotionId == null || (!item.IsNone &&
				!m_narrativeService.GetAvailableEmotions(m_selectedReply).Contains(emotionId)))
			{
				return;
			}

			m_selectedEmotionId = emotionId;
			CommitReplyAsync().Forget();
		}

		private async UniTaskVoid CommitReplyAsync()
		{
			if (m_currentStoryNode == null || m_selectedReply == null || m_hasReplied || m_isBusy)
			{
				return;
			}

			m_isBusy = true;
			try
			{
				await CommitSelectedReplyAsync();
			}
			finally
			{
				FinishBusy();
			}
		}

		private void FinishBusy()
		{
			m_isBusy = false;
			if (!m_pendingRestart)
			{
				return;
			}

			m_pendingRestart = false;
			StartStoryAsync().Forget();
		}

		private async UniTask CommitSelectedReplyAsync()
		{
			if (m_currentStoryNode == null || m_selectedReply == null || m_hasReplied)
			{
				return;
			}

			m_hasReplied = true;
			string replyText = m_selectedReply.ReplyText;
			try
			{
				NarrativeTurnResult result = m_narrativeService.CommitTurn(
					m_currentStoryNode, m_selectedReply, m_selectedEmotionId);
				DLogger.Info("{0}", result);
				m_selectedReply = null;
				m_tfReplayEmoNode.gameObject.SetActive(false);
				SetEmotionItemsActive(false);
				ShowPlayerReplyMessage(replyText);

				if (result.SessionEnded)
				{
					m_currentStoryNode = null;
					await ContinueAfterSessionAsync();
					return;
				}

				StoryNodeConfig nextNode = NarrativeConfigMgr.Instance.GetStoryNode(result.NextNodeId);
				if (nextNode == null)
				{
					DLogger.Error("Resolved story node not found: {0}", result.NextNodeId);
					return;
				}

				await PresentStoryNodeAsync(nextNode);
			}
			catch (Exception exception)
			{
				m_hasReplied = false;
				DLogger.Error("Narrative turn failed: {0}", exception);
				m_selectedReply = null;
				m_tfReplayEmoNode.gameObject.SetActive(false);
				SetEmotionItemsActive(false);
				if (m_tfReplayMessageNode != null)
				{
					m_tfReplayMessageNode.gameObject.SetActive(true);
				}
			}
			finally
			{
				if (m_btnEmoCollection != null)
				{
					m_btnEmoCollection.interactable = true;
				}

				if (m_btnItemCollection != null)
				{
					m_btnItemCollection.interactable = true;
				}
			}
		}

		private void SetReplyItemsActive(bool active)
		{
			for (int i = 0; i < m_replyItems.Count; i++)
			{
				if (m_replyItems[i] != null)
				{
					m_replyItems[i].gameObject.SetActive(active);
				}
			}

			if (!active)
			{
				m_replyItems.Clear();
			}
		}

		private void SetEmotionItemsActive(bool active)
		{
			for (int i = 0; i < m_emotionItems.Count; i++)
			{
				if (m_emotionItems[i] != null)
				{
					m_emotionItems[i].gameObject.SetActive(active);
				}
			}

			if (!active)
			{
				m_emotionItems.Clear();
			}
		}

		private LoopListViewItem2 OnGetMessageItemByIndex(LoopListView2 listView, int index)
		{
			if (IsDestroyed || index < 0 || index >= m_messages.Count)
			{
				return null;
			}

			var message = m_messages[index];
			UILoopItemWidget item;
			if (message.IsPlayer)
			{
				RightMessageItem right = m_messageList.CreateItem<RightMessageItem>();
				if (right == null)
				{
					return null;
				}

				right.SetMessage(message.Text);
				item = right;
			}
			else
			{
				LeftMessageItem left = m_messageList.CreateItem<LeftMessageItem>();
				if (left == null)
				{
					return null;
				}

				left.SetMessage(message.Text);
				item = left;
			}

			item.SetItemIndex(index);
			item.UpdateItem(index);
			item.LoopItem.Padding = 20f;
			// SuperScrollView TopToBottom 把 anchoredPosition.y 当成相对 Content 顶边的偏移。
			// 气泡预制体锚点在中心时，条目会整体下移半个 Content 高，底会被裁切。
			RectTransform itemRect = item.rectTransform;
			itemRect.anchorMin = new Vector2(itemRect.anchorMin.x, 1f);
			itemRect.anchorMax = new Vector2(itemRect.anchorMax.x, 1f);
			itemRect.pivot = new Vector2(itemRect.pivot.x, 1f);
			itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
				m_scrollMessage.GetComponent<RectTransform>().rect.width);

			LayoutRebuilder.ForceRebuildLayoutImmediate(itemRect);
			return item.LoopItem;
		}

		private void ApplyChatListLayout()
		{
			RectTransform emotionRect = m_tfReplayEmoNode as RectTransform;
			if (emotionRect != null)
			{
				emotionRect.anchorMin = emotionRect.anchorMax = new Vector2(0.5f, 0f);
				emotionRect.pivot = new Vector2(0.5f, 0f);
				emotionRect.anchoredPosition = new Vector2(-47f, 14f);
				emotionRect.sizeDelta = new Vector2(680f, 120f);
			}

			RectTransform replyRect = m_tfReplayMessageNode as RectTransform;
			if (replyRect != null)
			{
				replyRect.anchorMin = replyRect.anchorMax = new Vector2(0.5f, 0f);
				replyRect.pivot = new Vector2(0.5f, 0f);
				replyRect.anchoredPosition = new Vector2(-47f, 14f);
				replyRect.sizeDelta = new Vector2(680f, REPLY_PANEL_HEIGHT);
			}

			RectTransform scrollRect = m_scrollMessage != null
				? m_scrollMessage.transform as RectTransform
				: null;
			if (scrollRect == null)
			{
				return;
			}

			// The artwork's pale paper occupies the left part below the header;
			// the right-hand notes and the header are decoration, not message space.
			scrollRect.anchorMin = Vector2.zero;
			scrollRect.anchorMax = Vector2.one;
			scrollRect.offsetMin = new Vector2(96f, REPLY_PANEL_HEIGHT + 20f);
			scrollRect.offsetMax = new Vector2(-196f, -116f);
		}

		private void ApplyDialogueHudLayout()
		{
			PlaceCornerButton(m_btnSettings, new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(88f, 88f));
			HideButtonLabel(m_btnSettings);
			PlaceCornerButton(m_btnEmoCollection, new Vector2(0.5f, 0.5f),
				ReadLayoutPosition("Collection", new Vector2(750f, -360f)), new Vector2(150f, 160f));
			HideButtonLabel(m_btnEmoCollection);

			if (m_btnItemCollection == null)
			{
				var go = new GameObject("m_btnItemCollection", typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(transform, false);
				m_btnItemCollection = go.GetComponent<Button>();
				m_btnItemCollection.onClick.AddListener(ShowItems);
			}

			PlaceCornerButton(m_btnItemCollection, new Vector2(0.5f, 0.5f),
				ReadLayoutPosition("Items", new Vector2(750f, -150f)), new Vector2(150f, 160f));

			if (m_scrollMemoryList != null)
			{
				RectTransform memoryRect = m_scrollMemoryList.transform as RectTransform;
				memoryRect.anchorMin = memoryRect.anchorMax = new Vector2(0.5f, 1f);
				memoryRect.pivot = new Vector2(0.5f, 1f);
				memoryRect.anchoredPosition = new Vector2(180f, -24f);
				memoryRect.sizeDelta = new Vector2(920f, 110f);
			}
		}

		private static void PlaceCornerButton(Button button, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
		{
			if (button == null)
			{
				return;
			}

			RectTransform rect = button.transform as RectTransform;
			rect.anchorMin = rect.anchorMax = anchor;
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = anchoredPosition;
			rect.sizeDelta = size;
		}

		private static void HideButtonLabel(Button button)
		{
			if (button == null)
			{
				return;
			}

			TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
			if (label != null)
			{
				label.text = string.Empty;
			}
		}

		private void ApplyDialogueHudArt()
		{
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			ApplyHudSprite(m_btnSettings != null ? m_btnSettings.image : null, "dialogue_menu_button", true, token);
			ApplyHudSprite(m_btnEmoCollection != null ? m_btnEmoCollection.image : null, "dialogue_collection_btn", true, token);
			ApplyHudSprite(m_btnItemCollection != null ? m_btnItemCollection.image : null, "dialogue_item_btn", true, token);

			if (m_scrollMessage != null)
			{
				Image chatScrollImage = m_scrollMessage.GetComponent<Image>();
				if (chatScrollImage != null)
				{
					chatScrollImage.color = new Color(1f, 1f, 1f, 0f);
					chatScrollImage.raycastTarget = true;
				}

				Transform panel = m_scrollMessage.transform.parent;
				Image panelImage = panel != null ? panel.GetComponent<Image>() : null;
				ApplyHudSprite(panelImage, "dialogue_panel", false, token, true);
			}

			if (m_scrollMemoryList != null)
			{
				Image memoryBg = m_scrollMemoryList.GetComponent<Image>();
				ApplyHudSprite(memoryBg, "dialogue_memory_panel", false, token, true);
			}
		}

		private void ApplyHudSprite(Image image, string location, bool nativeSize, CancellationToken token, bool sliced = false)
		{
			if (image == null || string.IsNullOrEmpty(location))
			{
				return;
			}

			image.SetSprite(location, nativeSize, loaded =>
			{
				if (loaded == null || IsDestroyed)
				{
					return;
				}

				loaded.color = Color.white;
				loaded.preserveAspect = nativeSize;
				loaded.raycastTarget = true;
				if (sliced)
				{
					loaded.type = Image.Type.Sliced;
				}
			}, token);
		}

		private void BuildMemorySlots()
		{
			m_memorySlotImages.Clear();
			m_memorySlotLabels.Clear();
			if (m_scrollMemoryList == null || m_scrollMemoryList.content == null)
			{
				return;
			}

			RectTransform content = m_scrollMemoryList.content;
			for (int i = content.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
			}

			HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
			if (layout == null)
			{
				layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
			}

			layout.childAlignment = TextAnchor.MiddleCenter;
			layout.spacing = 12f;
			layout.padding = new RectOffset(24, 24, 16, 16);
			layout.childControlWidth = false;
			layout.childControlHeight = false;
			layout.childForceExpandWidth = false;
			layout.childForceExpandHeight = false;
			content.pivot = new Vector2(0.5f, 0.5f);
			content.anchorMin = Vector2.zero;
			content.anchorMax = Vector2.one;
			content.offsetMin = Vector2.zero;
			content.offsetMax = Vector2.zero;

			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			for (int i = 0; i < HUD_MEMORY_SLOT_COUNT; i++)
			{
				var slot = new GameObject("MemorySlot" + i, typeof(RectTransform), typeof(Image));
				slot.transform.SetParent(content, false);
				RectTransform slotRect = slot.GetComponent<RectTransform>();
				slotRect.sizeDelta = new Vector2(78f, 78f);
				Image slotImage = slot.GetComponent<Image>();
				slotImage.color = new Color(1f, 1f, 1f, 0.35f);
				ApplyHudSprite(slotImage, "dialogue_memory_slot_bg", false, token, true);
				var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
				labelGo.transform.SetParent(slot.transform, false);
				RectTransform labelRect = labelGo.GetComponent<RectTransform>();
				labelRect.anchorMin = Vector2.zero;
				labelRect.anchorMax = Vector2.one;
				labelRect.offsetMin = Vector2.zero;
				labelRect.offsetMax = Vector2.zero;
				TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
				label.text = "×";
				label.fontSize = 28;
				label.color = new Color(0.45f, 0.4f, 0.35f, 0.7f);
				label.alignment = TextAlignmentOptions.Center;
				label.raycastTarget = false;
				label.enableWordWrapping = true;
				label.overflowMode = TextOverflowModes.Ellipsis;
				label.enableAutoSizing = true;
				label.fontSizeMin = 10f;
				label.fontSizeMax = 28f;
				m_memorySlotImages.Add(slotImage);
				m_memorySlotLabels.Add(label);
			}

			RefreshMemorySlots();
		}

		private void RefreshMemorySlots()
		{
			if (m_memorySlotLabels.Count == 0)
			{
				return;
			}

			IReadOnlyList<string> ids = NarrativeSaveData.Get.GetMemoryIdsInStableOrder();
			for (int i = 0; i < m_memorySlotLabels.Count; i++)
			{
				TextMeshProUGUI label = m_memorySlotLabels[i];
				if (label == null)
				{
					continue;
				}

				if (i < ids.Count)
				{
					MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(ids[i]);
					label.text = memory == null ? ids[i] : memory.MemoryName;
					label.enableAutoSizing = true;
					label.fontSizeMin = 10f;
					label.fontSizeMax = 16f;
					label.overflowMode = TextOverflowModes.Ellipsis;
					label.enableWordWrapping = true;
					label.color = HudPaperText;
				}
				else
				{
					label.text = "×";
					label.enableAutoSizing = true;
					label.fontSizeMin = 16f;
					label.fontSizeMax = 28f;
					label.color = new Color(0.45f, 0.4f, 0.35f, 0.7f);
				}
			}
		}

		private void ShowItems()
		{
			if (m_layoutEditMode) return;
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			var buttons = new List<(string Label, Action Action)>();
			foreach (string itemId in saveData.GetNarrativeItemIdsInStableOrder())
			{
				NarrativeItemConfig item = NarrativeConfigMgr.Instance.GetNarrativeItem(itemId);
				if (item == null)
				{
					continue;
				}

				string capturedId = itemId;
				buttons.Add((item.ItemName, (Action)(() => ShowNarrativeItem(capturedId))));
			}

			buttons.Add(("返回", (Action)(() => DestroyOverlay())));
			string body = buttons.Count <= 1 ? "还没有获得物品。" : "选择一件物品检查部件。";
			ShowOverlay("物品", body, buttons);
		}

		private void ShowPlayerReplyMessage(string replyText)
		{
			if (m_playerReplyShown || string.IsNullOrEmpty(replyText))
			{
				return;
			}

			m_playerReplyShown = true;
			AddMessage(true, replyText);
		}

		private static bool IsNpcDialogueTurn(StoryNodeConfig story)
		{
			if (story == null || string.IsNullOrWhiteSpace(story.TurnType))
			{
				return false;
			}

			string turnType = story.TurnType.Trim();
			return turnType.Equals("NpcDialogue", StringComparison.OrdinalIgnoreCase) ||
				turnType.Equals("NPCDialog", StringComparison.OrdinalIgnoreCase);
		}

		private static bool UsesPaperDialogue(string npcId)
		{
			return npcId == "老周" || npcId == "陈先生";
		}

		private void BindDialoguePanel()
		{
			if (m_scrollMessage == null || m_scrollMessage.transform.parent == null)
			{
				m_dialoguePanel = m_scrollMessage == null ? null : m_scrollMessage.gameObject;
				return;
			}

			m_dialoguePanel = m_scrollMessage.transform.parent.gameObject;
		}

		private void SetDialoguePresentationMode(bool npcDialogue)
		{
			if (m_dialoguePanel != null)
			{
				m_dialoguePanel.SetActive(!npcDialogue);
			}

			if (m_tmpNpcSubtitle != null)
			{
				m_tmpNpcSubtitle.gameObject.SetActive(npcDialogue);
				if (!npcDialogue)
				{
					m_tmpNpcSubtitle.text = string.Empty;
				}
			}
		}

		private void EnsureNpcSubtitle()
		{
			if (m_tmpNpcSubtitle != null)
			{
				return;
			}

			var go = new GameObject("NpcSubtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(transform, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.5f, 0f);
			rect.anchorMax = new Vector2(0.5f, 0f);
			rect.pivot = new Vector2(0.5f, 0f);
			rect.anchoredPosition = new Vector2(0f, 56f);
			rect.sizeDelta = new Vector2(1480f, 200f);
			TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
			text.text = string.Empty;
			text.fontSize = 36f;
			text.color = Color.black;
			text.alignment = TextAlignmentOptions.Bottom;
			text.enableWordWrapping = true;
			text.overflowMode = TextOverflowModes.Overflow;
			text.raycastTarget = false;
			ApplySubtitleOutline(text);
			m_tmpNpcSubtitle = text;
			go.SetActive(false);
		}

		private static void ApplySubtitleOutline(TextMeshProUGUI text)
		{
			if (text == null)
			{
				return;
			}

			text.outlineColor = Color.white;
			text.outlineWidth = 0.28f;
			Material material = text.fontMaterial;
			if (material == null)
			{
				return;
			}

			material.EnableKeyword(ShaderUtilities.Keyword_Outline);
			material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.28f);
			material.SetColor(ShaderUtilities.ID_OutlineColor, Color.white);
			material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.08f);
		}

		private async UniTask PresentNpcSubtitleAsync(StoryNodeConfig story, int presentVersion)
		{
			EnsureNpcSubtitle();
			if (m_tmpNpcSubtitle == null)
			{
				return;
			}

			string speaker = story.NpcId ?? string.Empty;
			string body = story.NpcText ?? string.Empty;
			string full = string.IsNullOrEmpty(speaker) ? body : speaker + "\n" + body;
			m_tmpNpcSubtitle.gameObject.SetActive(true);
			if (string.IsNullOrEmpty(full))
			{
				m_tmpNpcSubtitle.text = string.Empty;
				return;
			}

			m_tmpNpcSubtitle.text = full.Substring(0, 1);
			ApplySubtitleOutline(m_tmpNpcSubtitle);
			for (int i = 1; i <= full.Length; i++)
			{
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return;
				}

				m_tmpNpcSubtitle.text = full.Substring(0, i);
				int delayMs = TYPEWRITER_MS_PER_CHAR;
				if (IsTypewriterPunctuation(full[i - 1]))
				{
					delayMs += TYPEWRITER_PUNCTUATION_EXTRA_MS;
				}

				TypewriterWaitResult wait = await WaitTypewriterOrSkip(delayMs, presentVersion);
				if (wait == TypewriterWaitResult.Cancelled)
				{
					return;
				}

				if (wait == TypewriterWaitResult.Skip)
				{
					m_tmpNpcSubtitle.text = full;
					return;
				}
			}
		}

		private async UniTask PresentNpcMessageAsync(string text, int presentVersion)
		{
			if (string.IsNullOrEmpty(text))
			{
				AddMessage(false, string.Empty);
				return;
			}

			AddMessage(false, text.Substring(0, 1));
			int index = m_messages.Count - 1;
			for (int i = 1; i <= text.Length; i++)
			{
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return;
				}

				if (i > 1)
				{
					UpdateShownMessageText(index, text.Substring(0, i));
				}

				int delayMs = TYPEWRITER_MS_PER_CHAR;
				if (IsTypewriterPunctuation(text[i - 1]))
				{
					delayMs += TYPEWRITER_PUNCTUATION_EXTRA_MS;
				}

				TypewriterWaitResult wait = await WaitTypewriterOrSkip(delayMs, presentVersion);
				if (wait == TypewriterWaitResult.Cancelled)
				{
					return;
				}

				if (wait == TypewriterWaitResult.Skip)
				{
					UpdateShownMessageText(index, text);
					return;
				}
			}
		}

		private enum TypewriterWaitResult
		{
			Continue,
			Skip,
			Cancelled
		}

		private async UniTask<TypewriterWaitResult> WaitTypewriterOrSkip(int delayMs, int presentVersion)
		{
			float remain = delayMs / 1000f;
			while (remain > 0f)
			{
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return TypewriterWaitResult.Cancelled;
				}

				if (ConsumeAdvanceClick() || WasPrimaryClickThisFrame())
				{
					return TypewriterWaitResult.Skip;
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
				remain -= Time.unscaledDeltaTime;
			}

			return TypewriterWaitResult.Continue;
		}

		private async UniTask<TypewriterWaitResult> WaitForAdvanceClick(int presentVersion)
		{
			await UniTask.Yield(PlayerLoopTiming.Update);
			while (true)
			{
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return TypewriterWaitResult.Cancelled;
				}

				if (ConsumeAdvanceClick() ||
					(WasPrimaryClickThisFrame() && CanAdvanceDialogueByClick()))
				{
					return TypewriterWaitResult.Continue;
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
			}
		}

		private bool CanAdvanceDialogueByClick()
		{
			if (m_overlay != null && m_overlay.activeInHierarchy)
			{
				return false;
			}

			EventSystem eventSystem = EventSystem.current;
			if (eventSystem == null)
			{
				return true;
			}

			var pointer = new PointerEventData(eventSystem)
			{
				position = GetPointerScreenPosition()
			};
			var results = new List<RaycastResult>();
			eventSystem.RaycastAll(pointer, results);
			for (int i = 0; i < results.Count; i++)
			{
				GameObject hit = results[i].gameObject;
				if (hit == null)
				{
					continue;
				}

				Button button = hit.GetComponentInParent<Button>();
				if (button != null && button != m_btnDialogueAdvance)
				{
					return false;
				}
			}

			return true;
		}

		private static Vector2 GetPointerScreenPosition()
		{
#if ENABLE_INPUT_SYSTEM
			if (Mouse.current != null)
			{
				return Mouse.current.position.ReadValue();
			}

			return Vector2.zero;
#else
			return Input.mousePosition;
#endif
		}

		private void UpdateShownMessageText(int index, string text)
		{
			if (m_messageList == null || index < 0 || index >= m_messages.Count)
			{
				return;
			}

			m_messages[index] = (m_messages[index].IsPlayer, text);
			LoopListView2 listView = m_messageList.LoopRectView;
			LoopListViewItem2 shown = listView.GetShownItemByItemIndex(index);
			if (shown != null)
			{
				TextMeshProUGUI tmp = shown.GetComponentInChildren<TextMeshProUGUI>(true);
				if (tmp != null)
				{
					tmp.text = text;
					tmp.ForceMeshUpdate();
				}

				LayoutRebuilder.ForceRebuildLayoutImmediate(shown.CachedRectTransform);
				listView.OnItemSizeChanged(index);
			}

			if (m_messagePinnedToLatest)
			{
				PinMessageListToLatest(listView);
			}
		}

		private static bool IsTypewriterPunctuation(char c)
		{
			return c == '，' || c == '。' || c == '！' || c == '？' || c == '、' ||
				c == '；' || c == '：' || c == '…' || c == ',' || c == '.' ||
				c == '!' || c == '?' || c == ';' || c == ':';
		}

		private static bool WasPrimaryClickThisFrame()
		{
#if ENABLE_INPUT_SYSTEM
			return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
			return Input.GetMouseButtonDown(0);
#endif
		}

		private void AddMessage(bool isPlayer, string text)
		{
			bool wasPinned = m_messagePinnedToLatest;
			if (m_messages.Count > 0)
			{
				LoopListViewItem2 prevLast = m_messageList.LoopRectView.GetShownItemByItemIndex(m_messages.Count - 1);
				wasPinned = wasPinned || prevLast != null;
			}

			m_messages.Add((isPlayer, text));
			m_messageScrollVersion++;
			LoopListView2 listView = m_messageList.LoopRectView;
			listView.ClearAutoMoveToItemData();
			listView.ScrollRect.StopMovement();
			listView.SetListItemCount(m_messages.Count, false);
			listView.UpdateListViewContent();

			if (!wasPinned)
			{
				return;
			}

			m_messagePinnedToLatest = true;
			PinMessageListToLatest(listView);
		}

		private static void PinMessageListToLatest(LoopListView2 listView)
		{
			int lastIndex = listView.ItemTotalCount - 1;
			if (lastIndex < 0)
			{
				return;
			}

			listView.MovePanelToItemIndex(lastIndex, 0f);
			LoopListViewItem2 lastItem = listView.GetShownItemByItemIndex(lastIndex);
			if (lastItem == null)
			{
				return;
			}

			listView.OnItemSizeChanged(lastIndex);
			lastItem = listView.GetShownItemByItemIndex(lastIndex);
			if (lastItem == null)
			{
				return;
			}

			float itemHeight = lastItem.CachedRectTransform.rect.height;
			float offset = listView.ViewPortSize - itemHeight - MESSAGE_BOTTOM_PADDING;
			listView.MovePanelToItemIndex(lastIndex, offset);
		}

		private static float GetLastMessageOverflow(LoopListView2 listView, LoopListViewItem2 lastItem)
		{
			Vector3 itemBottom = listView.GetItemCornerPosInViewPort(lastItem, ItemCornerEnum.LeftBottom);
			Vector3[] viewportCorners = new Vector3[4];
			listView.ViewPortTrans.GetWorldCorners(viewportCorners);
			Vector3 viewportBottom = listView.ViewPortTrans.InverseTransformPoint(viewportCorners[0]);
			return viewportBottom.y - itemBottom.y;
		}

		private async UniTask ContinueAfterSessionAsync()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			NarrativeSessionConfig next = m_storyScheduler.FindNextSession(saveData);
			if (next != null)
			{
				saveData.BeginSession(next.Id, next.StartNodeId);
				StoryNodeConfig firstNode = NarrativeConfigMgr.Instance.GetStoryNode(next.StartNodeId);
				if (firstNode == null)
				{
					DLogger.Error("Session start node not found: {0}", next.StartNodeId);
					EnterDayEnd();
					return;
				}

				await PresentStoryNodeAsync(firstNode);
				return;
			}

			if (m_storyScheduler.HasUncompletedSessions(saveData) &&
				m_storyScheduler.FindNextScheduledDay(saveData) <= 0)
			{
				ShowOverlay("剧情配置异常", "当前 Session 没有可用的显式后继，但仍有未完成 Session。请检查剧情配置。", new[]
				{
					("回到标题", (Action)(() => ReturnToMainMenu()))
				});
				return;
			}

			EnterDayEnd();
		}

		private void ExecuteNodeExitActions(NarrativeSaveData saveData, StoryNodeConfig story)
		{
			if (story == null) return;
			m_actionService.Execute(story.OnExitActionIds,
				saveData.BuildTurnKey(story.Id) + "|node-exit", saveData);
		}

		private void EnterDayEnd()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (saveData.DayPhase == NarrativeDayPhase.Active && string.IsNullOrWhiteSpace(saveData.CurrentSessionId))
				saveData.BeginDayMonologue();
			ShowDayMonologue();
		}

		private void ShowDayMonologue()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			NarrativeSessionConfig session = NarrativeConfigMgr.Instance.GetSession(saveData.LastCompletedSessionId);
			string body = session != null && !string.IsNullOrWhiteSpace(session.DayMonologue)
				? session.DayMonologue
				: $"第 {saveData.CurrentDay} 天结束。\n今天获得 {saveData.MemoryCount} 段记忆，学会 {saveData.OwnedEmotionIds.Count} 种情绪。\n回想过后，新的日子将继续。";
			string title = session != null ? session.Name + " · 日终" : "日终独白";
			ShowOverlay(title, body, new[]
			{
				("继续", (Action)(() => ContinueAfterDayMonologue()))
			});
		}

		private void ContinueAfterDayMonologue()
		{
			if (m_isBusy)
			{
				return;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			DestroyOverlay();
			if (saveData.DayPhase == NarrativeDayPhase.Monologue)
			{
				int nextDay = m_storyScheduler.FindNextScheduledDay(saveData);
				saveData.CompleteDayMonologue(nextDay);
				m_currentStoryNode = null;
			}

			AdvanceFromDayPhase(saveData);
		}

		private void AdvanceFromDayPhase(NarrativeSaveData saveData)
		{
			if (saveData.DayPhase == NarrativeDayPhase.MemoryCleanup)
			{
				ShowMemoryCleanup();
				return;
			}

			if (saveData.DayPhase == NarrativeDayPhase.GameComplete)
			{
				ShowGameComplete();
				return;
			}

			if (saveData.DayPhase == NarrativeDayPhase.PrototypeContentExhausted)
			{
				ShowPrototypeContentExhausted();
				return;
			}

			StartStoryAsync().Forget();
		}

		private void ShowMemoryCleanup()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			var buttons = new List<(string Label, Action Action)>();
			foreach (string id in saveData.GetCleanupMemoryIds())
			{
				MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(id);
				string label = memory == null ? id : memory.MemoryName;
				string memoryId = id;
				buttons.Add(("移除：" + label, (Action)(() =>
				{
					saveData.RemoveMemoryDuringCleanup(memoryId);
					ShowMemoryCleanup();
				})));
			}

			if (saveData.CanFinishMemoryCleanup)
			{
				buttons.Add(("完成整理并继续", (Action)(() =>
				{
					if (m_isBusy)
					{
						return;
					}

					int nextDay = m_storyScheduler.FindNextScheduledDay(saveData);
					saveData.FinishMemoryCleanupAndAdvanceDay(nextDay);
					DestroyOverlay();
					AdvanceFromDayPhase(saveData);
				})));
			}

			ShowOverlay("整理记忆", $"进入整理时至少移除一段记忆，整理后最多保留 {NarrativeRules.MemoryCapacity} 段。当前 {saveData.MemoryCount} 段。已学会的情绪会永久保留。", buttons);
		}

		private void ShowGameComplete()
		{
			ShowOverlay("成为自己", "主线故事已完成。一路留下的记忆与情绪已保存在本机存档中。", new[]
			{
				("从头开始", (Action)(() => ConfirmRestartFromBeginning())),
				("查看记忆与情绪", (Action)(() => ShowCollection())),
				("回到标题", (Action)(() => ReturnToMainMenu()))
			});
		}

		private void ShowPrototypeContentExhausted()
		{
			ShowOverlay("试玩内容结束", "当前配置的原型剧情已全部走完；正式剧情结局尚未配置。你的记忆与情绪保存在本机存档中。", new[]
			{
				("从头开始", (Action)(() => ConfirmRestartFromBeginning())),
				("查看记忆与情绪", (Action)(() => ShowCollection())),
				("回到标题", (Action)(() => ReturnToMainMenu()))
			});
		}

		private void ShowCollection()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			var lines = new List<string> { "记忆：" };
			foreach (string id in saveData.GetMemoryIdsInStableOrder())
			{
				MemoryConfig item = NarrativeConfigMgr.Instance.GetMemory(id);
				lines.Add("• " + (item == null ? id : item.MemoryName + "：" + item.Description));
			}
			lines.Add("\n情绪：");
			foreach (string id in saveData.GetEmotionIdsInStableOrder())
			{
				EmotionConfig item = NarrativeConfigMgr.Instance.GetEmotion(id);
				lines.Add("• " + (item == null ? id : item.EmotionName));
			}
			var buttons = new List<(string Label, Action Action)>();
			foreach (string itemId in saveData.GetNarrativeItemIdsInStableOrder())
			{
				NarrativeItemConfig item = NarrativeConfigMgr.Instance.GetNarrativeItem(itemId);
				if (item == null) continue;
				string capturedId = itemId;
				buttons.Add(("检查物品：" + item.ItemName, (Action)(() => ShowNarrativeItem(capturedId))));
			}
			buttons.Add(("返回", (Action)(() => DestroyOverlay())));
			ShowOverlay("记忆与情绪", string.Join("\n", lines) + "\n\n已持有物品可点击检查部件，发现与物品相关的记忆。", buttons);
		}

		private void ShowNarrativeItem(string itemId)
		{
			NarrativeItemConfig item = NarrativeConfigMgr.Instance.GetNarrativeItem(itemId);
			if (item == null)
			{
				ShowOverlay("物品不存在", itemId ?? string.Empty, new[] { ("返回", (Action)(() => ShowCollection())) });
				return;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			var buttons = new List<(string Label, Action Action)>();
			var visualParts = new List<NarrativeItemPartConfig>();
			bool hasVisualImage = !string.IsNullOrWhiteSpace(item.ImageId) && item.ImageId != "placeholder" &&
				GameModule.ResourceModule.CheckLocationValid(item.ImageId);
			if (item.PartIds != null)
			{
				foreach (string partId in item.PartIds)
				{
					NarrativeItemPartConfig part = NarrativeConfigMgr.Instance.GetNarrativeItemPart(partId);
					if (part == null) continue;
					if (hasVisualImage && part.HotspotX >= 0f && part.HotspotX <= 1f &&
						part.HotspotY >= 0f && part.HotspotY <= 1f)
					{
						visualParts.Add(part);
						continue;
					}

					string memoryId = part.MemoryId;
					string partName = part.PartName;
					string label = string.IsNullOrWhiteSpace(memoryId)
						? "查看部件：" + partName
						: (saveData.HasMemory(memoryId) ? "已发现：" : "检查部件：") + partName;
					buttons.Add((label, (Action)(() => InspectNarrativeItemPart(itemId, memoryId))));
				}
			}

			buttons.Add(("返回收藏", (Action)(() => ShowCollection())));
			string inspectHint = visualParts.Count > 0
				? "\n\n点击物品上的不同部位，发现与它有关的记忆。发现过的记忆不会重复获得。"
				: "\n\n选择一个部件进行检查。发现过的记忆不会重复获得。";
			ShowOverlay(item.ItemName, item.Description + inspectHint, buttons);
			ShowNarrativeItemImage(item.ImageId, visualParts, itemId);
		}

		private void InspectNarrativeItemPart(string itemId, string memoryId)
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (!string.IsNullOrWhiteSpace(memoryId) && saveData.AddMemories(new[] { memoryId }))
			{
				m_narrativeService.RunEmotionScanForDebug(saveData);
				RefreshUI();
				MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(memoryId);
				ShowOverlay("发现记忆", memory == null ? memoryId : memory.MemoryName + "\n" + memory.Description,
					new[] { ("继续检查", (Action)(() => ShowNarrativeItem(itemId))) });
			}
			else
			{
				ShowNarrativeItem(itemId);
			}
		}

		private void ShowNarrativeItemImage(string imageId, IReadOnlyList<NarrativeItemPartConfig> parts, string itemId)
		{
			if (string.IsNullOrWhiteSpace(imageId) || imageId == "placeholder" || m_overlayBodyContent == null ||
				!GameModule.ResourceModule.CheckLocationValid(imageId))
			{
				return;
			}

			var imageObject = new GameObject("NarrativeItemImage", typeof(RectTransform), typeof(Image));
			imageObject.transform.SetParent(m_overlayBodyContent, false);
			RectTransform imageRect = imageObject.GetComponent<RectTransform>();
			imageRect.anchorMin = new Vector2(0.5f, 1f);
			imageRect.anchorMax = new Vector2(0.5f, 1f);
			imageRect.pivot = new Vector2(0.5f, 1f);
			imageRect.anchoredPosition = new Vector2(0f, -8f);
			imageRect.sizeDelta = new Vector2(400f, 240f);
			Image image = imageObject.GetComponent<Image>();
			image.preserveAspect = true;
			image.raycastTarget = false;
			image.SetSprite(imageId, false, loaded =>
			{
				if (loaded != null && loaded.sprite != null)
				{
					loaded.preserveAspect = true;
					loaded.raycastTarget = false;
					float fit = Mathf.Min(imageRect.rect.width / loaded.sprite.rect.width,
						imageRect.rect.height / loaded.sprite.rect.height);
					Vector2 displaySize = loaded.sprite.rect.size * fit;
					CreateNarrativeItemHotspots(imageRect, displaySize, parts, itemId);
				}
			}, gameObject.GetCancellationTokenOnDestroy());

			TextMeshProUGUI bodyText = m_overlayBodyContent.GetComponentInChildren<TextMeshProUGUI>(true);
			if (bodyText == null)
			{
				return;
			}

			bodyText.rectTransform.anchoredPosition = new Vector2(0f, -280f);
			bodyText.rectTransform.offsetMin = new Vector2(24f, bodyText.rectTransform.offsetMin.y);
			bodyText.rectTransform.offsetMax = new Vector2(-24f, bodyText.rectTransform.offsetMax.y);
			Canvas.ForceUpdateCanvases();
			bodyText.ForceMeshUpdate();
			float textHeight = Mathf.Max(bodyText.preferredHeight + 24f, 80f);
			bodyText.rectTransform.sizeDelta = new Vector2(bodyText.rectTransform.sizeDelta.x, textHeight);
			m_overlayBodyContent.sizeDelta = new Vector2(0f, textHeight + 300f);
		}

		private void CreateNarrativeItemHotspots(RectTransform imageRect, Vector2 displaySize,
			IReadOnlyList<NarrativeItemPartConfig> parts, string itemId)
		{
			if (parts == null) return;
			for (int i = 0; i < parts.Count; i++)
			{
				NarrativeItemPartConfig part = parts[i];
				if (part == null) continue;
				var go = new GameObject("Hotspot_" + part.Id, typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(imageRect, false);
				RectTransform rect = go.GetComponent<RectTransform>();
				rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
				rect.pivot = new Vector2(0.5f, 0.5f);
				rect.anchoredPosition = new Vector2((part.HotspotX - 0.5f) * displaySize.x,
					(part.HotspotY - 0.5f) * displaySize.y);
				float hotspotSize = part.Id == "CLOCK_PART_01" ? 132f : 88f;
				rect.sizeDelta = new Vector2(hotspotSize, hotspotSize);
				Image hotspotImage = go.GetComponent<Image>();
				hotspotImage.color = new Color(1f, 1f, 1f, 0f);
				Button button = go.GetComponent<Button>();
				button.transition = Selectable.Transition.ColorTint;
				ColorBlock colors = button.colors;
				colors.normalColor = new Color(1f, 1f, 1f, 0f);
				colors.highlightedColor = new Color(1f, 0.82f, 0.48f, 0.16f);
				colors.pressedColor = new Color(1f, 0.72f, 0.28f, 0.24f);
				colors.selectedColor = new Color(1f, 0.82f, 0.48f, 0.16f);
				button.colors = colors;
				string memoryId = part.MemoryId;
				button.onClick.AddListener(() => InspectNarrativeItemPart(itemId, memoryId));
			}
		}

		private void ShowSettings()
		{
			ShowOverlay("设置", "游戏进度会在每次剧情提交和阶段变化时自动保存。", new[]
			{
				("继续游戏", (Action)(() => DestroyOverlay())),
				("调整人物、按钮与图层", (Action)(() => { DestroyOverlay(); SetLayoutEditMode(true); })),
				("查看记忆", (Action)(() => ShowCollection())),
				("从头开始", (Action)(() => ConfirmRestartFromBeginning())),
				("回到标题", (Action)(() => ReturnToMainMenu()))
			});
		}

		private void ShowDebugPanel()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			var buttons = new List<(string Label, Action Action)>
			{
				("复制存档", (Action)(() => GUIUtility.systemCopyBuffer = saveData.ExportRuntimeStateJson())),
				("扫描情绪", (Action)(() =>
				{
					IReadOnlyList<string> emotions = m_narrativeService.RunEmotionScanForDebug(saveData);
					ShowOverlay("情绪扫描", "新增情绪：" + (emotions.Count == 0 ? "无" : string.Join("、", emotions)), new[] { ("返回", (Action)(() => ShowDebugPanel())) });
				})),
				("清除剧情标记", (Action)(() => { saveData.ClearStoryFlags(); ShowDebugPanel(); })),
				("增加一天", (Action)(() => { saveData.SetDayTime(saveData.CurrentDay + 1, 0); StartStoryAsync().Forget(); })),
				("时间 +1", (Action)(() => { saveData.SetDayTime(saveData.CurrentDay, saveData.CurrentTimeSlot + 1); ShowDebugPanel(); })),
				("跳转 Turn…", (Action)(() => ShowDebugJumpNode())),
				("关闭", (Action)(() => DestroyOverlay()))
			};

			MemoryConfig missingMemory = NarrativeConfigMgr.Instance.AllMemories.FirstOrDefault(x => !saveData.HasMemory(x.Id));
			if (missingMemory != null)
				buttons.Add(("添加记忆：" + missingMemory.MemoryName, (Action)(() => { saveData.AddMemories(new[] { missingMemory.Id }); ShowDebugPanel(); })));
			string ownedMemoryId = saveData.GetMemoryIdsInStableOrder().FirstOrDefault();
			if (ownedMemoryId != null)
				buttons.Add(("移除记忆：" + ownedMemoryId, (Action)(() => { saveData.RemoveMemoryForDebug(ownedMemoryId); ShowDebugPanel(); })));
			EmotionConfig missingEmotion = NarrativeConfigMgr.Instance.AllEmotions.FirstOrDefault(x => !saveData.HasEmotion(x.Id));
			if (missingEmotion != null)
				buttons.Add(("授予情绪：" + missingEmotion.EmotionName, (Action)(() => { saveData.GrantEmotion(missingEmotion.Id); ShowDebugPanel(); })));
			if (m_currentStoryNode != null && !string.IsNullOrWhiteSpace(m_currentStoryNode.NpcId))
			{
				string npcId = m_currentStoryNode.NpcId;
				buttons.Add(("当前 NPC 好感 +5", (Action)(() => { saveData.SetAffectionForDebug(npcId, saveData.GetAffection(npcId) + 5); ShowDebugPanel(); })));
			}
			NarrativeSessionConfig currentSession = NarrativeConfigMgr.Instance.GetSession(saveData.CurrentSessionId);
			if (currentSession != null)
				buttons.Add(("跳到本段起点", (Action)(() => { saveData.JumpToStoryNodeForDebug(currentSession.StartNodeId); DestroyOverlay(); StartStoryAsync().Forget(); })));

			ShowOverlay("调试工具", saveData.ExportRuntimeStateJson(), buttons);
		}

		private void ShowDebugJumpNode()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			ShowOverlay("跳转剧情节点", "输入配置中的 Turn ID，例如 T0203。", new[]
			{
				("跳转", (Action)(() =>
				{
					string id = m_debugNodeInput == null ? string.Empty : m_debugNodeInput.text.Trim();
					if (!saveData.JumpToStoryNodeForDebug(id))
					{
						ShowOverlay("节点不存在", "没有找到节点 " + id, new[] { ("返回", (Action)(() => ShowDebugJumpNode())) });
						return;
					}
					DestroyOverlay();
					StartStoryAsync().Forget();
				})),
				("取消", (Action)(() => ShowDebugPanel()))
			});
			RectTransform bodyContent = m_overlayBodyContent;
			m_debugNodeInput = CreateOverlayInputField(
				bodyContent != null ? bodyContent : (m_overlay != null ? m_overlay.transform : transform),
				"T0203", Vector2.zero, new Vector2(520, 44));
			RectTransform inputRect = m_debugNodeInput.transform as RectTransform;
			inputRect.anchorMin = inputRect.anchorMax = new Vector2(0.5f, 1f);
			inputRect.pivot = new Vector2(0.5f, 1f);
			float textHeight = 48f;
			if (bodyContent != null)
			{
				TextMeshProUGUI bodyText = bodyContent.GetComponentInChildren<TextMeshProUGUI>(true);
				if (bodyText != null)
				{
					textHeight = Mathf.Max(bodyText.preferredHeight + 16f, 48f);
				}

				inputRect.anchoredPosition = new Vector2(0f, -textHeight);
				bodyContent.sizeDelta = new Vector2(0f, textHeight + 56f);
			}
		}

		private void ShowOverlay(string title, string body, IReadOnlyList<(string Label, Action Action)> buttons)
		{
			DestroyOverlay();
			EnsureEventSystem();
			var blocker = new GameObject("NarrativeOverlay", typeof(RectTransform), typeof(Image));
			blocker.transform.SetParent(transform, false);
			blocker.transform.SetAsLastSibling();
			RectTransform blockerRect = blocker.GetComponent<RectTransform>();
			blockerRect.anchorMin = Vector2.zero;
			blockerRect.anchorMax = Vector2.one;
			blockerRect.offsetMin = Vector2.zero;
			blockerRect.offsetMax = Vector2.zero;
			Image blockerImage = blocker.GetComponent<Image>();
			blockerImage.color = new Color(0.12f, 0.1f, 0.08f, 0.45f);
			blockerImage.raycastTarget = true;
			m_overlay = blocker;
			ApplyHudSprite(blockerImage, "setting_mask", false, gameObject.GetCancellationTokenOnDestroy());

			var root = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			root.transform.SetParent(blocker.transform, false);
			RectTransform panel = root.GetComponent<RectTransform>();
			panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
			panel.pivot = new Vector2(0.5f, 0.5f);
			panel.sizeDelta = new Vector2(780, 720);
			Image panelImage = root.GetComponent<Image>();
			panelImage.color = HudPaperPanel;
			ApplyHudSprite(panelImage, "setting_save_popup_bg", false, gameObject.GetCancellationTokenOnDestroy(), true);

			TextMeshProUGUI titleText = CreateOverlayText(root.transform, title, 26, Vector2.zero,
				new Vector2(680, 52), TextAlignmentOptions.Center);
			titleText.enableWordWrapping = false;
			titleText.overflowMode = TextOverflowModes.Ellipsis;
			RectTransform titleRect = titleText.rectTransform;
			titleRect.anchorMin = new Vector2(0f, 1f);
			titleRect.anchorMax = new Vector2(1f, 1f);
			titleRect.pivot = new Vector2(0.5f, 1f);
			titleRect.offsetMin = new Vector2(64f, -128f);
			titleRect.offsetMax = new Vector2(-64f, -56f);

			int count = buttons == null ? 0 : buttons.Count;
			int columns = count > 6 ? 2 : 1;
			int rows = count == 0 ? 0 : (count + columns - 1) / columns;
			const float buttonHeight = 42f;
			const float buttonGap = 8f;
			float neededBar = rows == 0 ? 0f : 8f + rows * (buttonHeight + buttonGap);
			float buttonBarHeight = rows == 0 ? 16f : Mathf.Clamp(neededBar, 56f, 220f);

			RectTransform buttonViewport = CreateOverlayScrollViewport(root.transform, "ButtonScroll");
			buttonViewport.anchorMin = new Vector2(0f, 0f);
			buttonViewport.anchorMax = new Vector2(1f, 0f);
			buttonViewport.pivot = new Vector2(0.5f, 0f);
			buttonViewport.offsetMin = new Vector2(64f, 20f);
			buttonViewport.offsetMax = new Vector2(-64f, 20f + buttonBarHeight);
			RectTransform buttonContent = CreateOverlayScrollContent(buttonViewport);
			float buttonContentHeight = Mathf.Max(neededBar, buttonBarHeight);
			buttonContent.sizeDelta = new Vector2(0f, buttonContentHeight);
			for (int i = 0; i < count; i++)
			{
				var entry = buttons[i];
				int row = i / columns;
				int col = i % columns;
				float width = columns == 1 ? 600f : 280f;
				float x = columns == 1 ? 0f : (col == 0 ? -150f : 150f);
				float y = -8f - row * (buttonHeight + buttonGap);
				CreateOverlayButton(buttonContent, entry.Label, new Vector2(x, y),
					new Vector2(width, buttonHeight), entry.Action, true);
			}

			RectTransform bodyViewport = CreateOverlayScrollViewport(root.transform, "BodyScroll");
			bodyViewport.anchorMin = new Vector2(0f, 0f);
			bodyViewport.anchorMax = new Vector2(1f, 1f);
			bodyViewport.offsetMin = new Vector2(64f, 28f + buttonBarHeight);
			bodyViewport.offsetMax = new Vector2(-64f, -248f);
			RectTransform bodyContent = CreateOverlayScrollContent(bodyViewport);
			m_overlayBodyContent = bodyContent;
			TextMeshProUGUI bodyText = CreateOverlayText(bodyContent, body, 17, Vector2.zero,
				new Vector2(0f, 40f), TextAlignmentOptions.TopLeft);
			bodyText.overflowMode = TextOverflowModes.Overflow;
			bodyText.enableWordWrapping = true;
			RectTransform bodyTextRect = bodyText.rectTransform;
			bodyTextRect.anchorMin = new Vector2(0f, 1f);
			bodyTextRect.anchorMax = new Vector2(1f, 1f);
			bodyTextRect.pivot = new Vector2(0.5f, 1f);
			bodyTextRect.anchoredPosition = Vector2.zero;
			bodyTextRect.offsetMin = new Vector2(12f, bodyTextRect.offsetMin.y);
			bodyTextRect.offsetMax = new Vector2(-12f, 0f);
			bodyTextRect.sizeDelta = new Vector2(bodyTextRect.sizeDelta.x, 40f);
			Canvas.ForceUpdateCanvases();
			bodyText.ForceMeshUpdate();
			float bodyHeight = Mathf.Max(bodyText.preferredHeight + 24f, 80f);
			bodyContent.sizeDelta = new Vector2(0f, bodyHeight);
			bodyTextRect.sizeDelta = new Vector2(bodyTextRect.sizeDelta.x, bodyHeight);
		}

		private static RectTransform CreateOverlayScrollViewport(Transform parent, string name)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
			go.transform.SetParent(parent, false);
			Image image = go.GetComponent<Image>();
			image.color = new Color(1f, 1f, 1f, 0.01f);
			image.raycastTarget = true;
			ScrollRect scroll = go.GetComponent<ScrollRect>();
			scroll.horizontal = false;
			scroll.vertical = true;
			scroll.movementType = ScrollRect.MovementType.Clamped;
			scroll.inertia = true;
			scroll.scrollSensitivity = 28f;
			scroll.viewport = go.GetComponent<RectTransform>();
			return go.GetComponent<RectTransform>();
		}

		private static RectTransform CreateOverlayScrollContent(RectTransform viewport)
		{
			var go = new GameObject("Content", typeof(RectTransform));
			go.transform.SetParent(viewport, false);
			RectTransform content = go.GetComponent<RectTransform>();
			content.anchorMin = new Vector2(0f, 1f);
			content.anchorMax = new Vector2(1f, 1f);
			content.pivot = new Vector2(0.5f, 1f);
			content.anchoredPosition = Vector2.zero;
			content.sizeDelta = Vector2.zero;
			ScrollRect scroll = viewport.GetComponent<ScrollRect>();
			scroll.content = content;
			return content;
		}

		private static TextMeshProUGUI CreateOverlayText(Transform parent, string value, float fontSize,
			Vector2 position, Vector2 size, TextAlignmentOptions alignment)
		{
			var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
			text.text = value ?? string.Empty;
			text.fontSize = fontSize;
			text.color = HudPaperText;
			text.alignment = alignment;
			text.enableWordWrapping = true;
			text.overflowMode = TextOverflowModes.Ellipsis;
			text.raycastTarget = false;
			return text;
		}

		private static void CreateOverlayButton(Transform parent, string label, Vector2 position,
			Vector2 size, Action action, bool fromTop = false)
		{
			var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			if (fromTop)
			{
				rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
				rect.pivot = new Vector2(0.5f, 1f);
			}
			else
			{
				rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
				rect.pivot = new Vector2(0.5f, 0.5f);
			}

			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			go.GetComponent<Image>().color = HudPaperButton;
			go.GetComponent<Button>().onClick.AddListener(() => action?.Invoke());
			TextMeshProUGUI text = CreateOverlayText(go.transform, label, 17, Vector2.zero, size,
				TextAlignmentOptions.Center);
			text.overflowMode = TextOverflowModes.Ellipsis;
			text.enableWordWrapping = true;
			RectTransform textRect = text.rectTransform;
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = new Vector2(8f, 2f);
			textRect.offsetMax = new Vector2(-8f, -2f);
		}

		private static TMP_InputField CreateOverlayInputField(Transform parent, string initialValue,
			Vector2 position, Vector2 size)
		{
			var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			Image image = go.GetComponent<Image>();
			image.color = new Color(0.94f, 0.94f, 0.94f, 1f);
			TextMeshProUGUI text = CreateOverlayText(go.transform, string.Empty, 18, Vector2.zero,
				new Vector2(size.x - 24, size.y - 8), TextAlignmentOptions.Left);
			text.color = Color.black;
			text.overflowMode = TextOverflowModes.Overflow;
			TMP_InputField input = go.GetComponent<TMP_InputField>();
			input.targetGraphic = image;
			input.textComponent = text;
			input.text = initialValue ?? string.Empty;
			input.caretColor = Color.black;
			return input;
		}

		private static void EnsureEventSystem()
		{
			EventSystem eventSystem = EventSystem.current;
			if (eventSystem == null)
			{
				eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
			}

			if (eventSystem == null)
			{
#if ENABLE_INPUT_SYSTEM
				var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
#else
				var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
				UnityEngine.Object.DontDestroyOnLoad(eventSystemObject);
				eventSystem = eventSystemObject.GetComponent<EventSystem>();
			}

#if ENABLE_INPUT_SYSTEM
			StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
			if (legacyModule != null)
			{
				legacyModule.enabled = false;
			}

			InputSystemUIInputModule inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
			if (inputSystemModule == null)
			{
				inputSystemModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
			}

			inputSystemModule.enabled = true;
#else
			StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
			if (legacyModule == null)
			{
				eventSystem.gameObject.AddComponent<StandaloneInputModule>();
			}
#endif
		}

		private void DestroyOverlay()
		{
			if (m_overlay != null)
			{
				m_overlay.SetActive(false);
				UnityEngine.Object.Destroy(m_overlay);
			}

			m_overlay = null;
			m_debugNodeInput = null;
			m_overlayBodyContent = null;
		}

		private void OnMessageListBeginDrag()
		{
			m_messageScrollVersion++;
			m_messagePinnedToLatest = false;
		}

		private void OnMessageListEndDrag()
		{
			if (m_messageList == null || m_messages.Count == 0)
			{
				return;
			}

			LoopListView2 listView = m_messageList.LoopRectView;
			LoopListViewItem2 lastItem = listView.GetShownItemByItemIndex(m_messages.Count - 1);
			if (lastItem == null)
			{
				return;
			}

			if (GetLastMessageOverflow(listView, lastItem) <= MESSAGE_SCROLL_OVERFLOW_EPSILON * 8f)
			{
				m_messagePinnedToLatest = true;
			}
		}

		private void BindNpcPortraitView()
		{
			TextMeshProUGUI[] labels = gameObject.GetComponentsInChildren<TextMeshProUGUI>(true);
			for (int i = 0; i < labels.Length; i++)
			{
				TextMeshProUGUI label = labels[i];
				if (label == null || label.text != NpcPortraitPlaceholderText)
				{
					continue;
				}

				m_tmpNpcPlaceholder = label;
				m_imgNpcPortrait = label.transform.parent.GetComponent<Image>();
				if (m_imgNpcPortrait != null)
				{
					Vector2 slot = m_imgNpcPortrait.rectTransform.sizeDelta;
					if (slot.x >= 1f && slot.y >= 1f)
					{
						m_npcPortraitSlotSize = slot;
					}
				}

				return;
			}
		}

		private void RefreshNpcPortrait(StoryNodeConfig story)
		{
			RefreshNpcPortraitAsync(story).Forget();
		}

		private async UniTaskVoid RefreshNpcPortraitAsync(StoryNodeConfig story)
		{
			if (story == null)
			{
				return;
			}

			EnsureSceneCharacterView();

			int version = ++m_npcPortraitVersion;
#if SPINE_UNITY && SPINE_CSHARP
			string spineLocation = ResolveNpcSpineLocation(story.NpcId);
			if (!string.IsNullOrEmpty(spineLocation) && IsPortraitLocationReady(spineLocation) &&
				!m_unavailableNpcSpines.Contains(spineLocation))
			{
				if (await ShowNpcSpineAsync(spineLocation, story.AnimationAsset, version))
				{
					return;
				}
			}

			HideNpcSpine();
#endif
			ShowNpcPortraitSprite(story, version);
		}

		private void ShowNpcPortraitSprite(StoryNodeConfig story, int version)
		{
			string location = ResolveNpcPortraitLocation(story.NpcId, story.AnimationAsset);
			if (string.IsNullOrEmpty(location))
			{
				HideNpcSpine();
				if (m_rtSceneCharacter != null) m_rtSceneCharacter.gameObject.SetActive(false);
				if (m_imgNpcPortrait != null) m_imgNpcPortrait.enabled = false;
				if (m_tmpNpcPlaceholder != null) m_tmpNpcPlaceholder.gameObject.SetActive(false);
				return;
			}

			EnsureSceneCharacterView();
			if (m_rtSceneCharacter == null || m_imgSceneCharacter == null)
			{
				return;
			}

			m_rtSceneCharacter.gameObject.SetActive(true);
			if (location == m_loadedSceneCharacterLocation && m_imgSceneCharacter.sprite != null)
			{
				FitSceneCharacterSprite(m_imgSceneCharacter);
				return;
			}

			HideNpcSpine();
			m_loadedSceneCharacterLocation = location;
			m_loadedNpcPortraitLocation = location;
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			m_imgSceneCharacter.SetSprite(location, false, image =>
			{
				if (version != m_npcPortraitVersion || image == null || image.sprite == null || IsDestroyed)
				{
					return;
				}

				FitSceneCharacterSprite(image);
				HideLegacyNpcPortrait();
			}, token);
		}

		private void EnsureSceneCharacterView()
		{
			if (m_imgSceneRoot == null) BindSceneBackgroundView();
			if (m_imgSceneRoot == null) return;

			if (m_rtSceneCharacter == null)
			{
				Transform existing = m_imgSceneRoot.transform.Find("SceneCharacter");
				if (existing != null) m_rtSceneCharacter = existing as RectTransform;
			}
			if (m_rtSceneCharacter == null)
			{
				var go = new GameObject("SceneCharacter", typeof(RectTransform), typeof(Image));
				go.transform.SetParent(m_imgSceneRoot.transform, false);
				m_rtSceneCharacter = go.GetComponent<RectTransform>();
			}

			m_rtSceneCharacter.anchorMin = new Vector2(0.5f, 0f);
			m_rtSceneCharacter.anchorMax = new Vector2(0.5f, 0f);
			m_rtSceneCharacter.pivot = new Vector2(0.5f, 0f);
			m_rtSceneCharacter.anchoredPosition = ReadLayoutPosition(GetCharacterLayoutPart(), new Vector2(-650f, 0f));
			m_rtSceneCharacter.sizeDelta = new Vector2(600f, 960f);
			m_rtSceneCharacter.localScale = Vector3.one * ReadCharacterScale();
			m_imgSceneCharacter = m_rtSceneCharacter.GetComponent<Image>();
			if (m_imgSceneCharacter == null) m_imgSceneCharacter = m_rtSceneCharacter.gameObject.AddComponent<Image>();
			m_imgSceneCharacter.raycastTarget = m_layoutEditMode;
			m_imgSceneCharacter.preserveAspect = true;
			m_imgSceneCharacter.color = Color.white;
			RegisterLayoutDragHandle(m_rtSceneCharacter, "Character");
		}

		private void FitSceneCharacterSprite(Image image)
		{
			if (image == null) return;
			image.enabled = true;
			image.type = Image.Type.Simple;
			image.useSpriteMesh = false;
			image.preserveAspect = true;
			image.raycastTarget = m_layoutEditMode;
			image.color = Color.white;
		}

		private void FitNpcPortrait(Image image)
		{
			if (image == null)
			{
				return;
			}

			image.enabled = true;
			image.type = Image.Type.Simple;
			image.useSpriteMesh = false;
			image.preserveAspect = true;
			image.color = Color.white;
			if (m_npcPortraitSlotSize.x >= 1f && m_npcPortraitSlotSize.y >= 1f)
			{
				image.rectTransform.sizeDelta = m_npcPortraitSlotSize;
			}
		}

#if SPINE_UNITY && SPINE_CSHARP
		private async UniTask<bool> ShowNpcSpineAsync(string location, string animationAsset, int version)
		{
			EnsureSceneCharacterView();
			if (m_rtSceneCharacter == null || m_imgSceneCharacter == null)
			{
				return false;
			}

			if (location == m_loadedNpcSpineLocation && m_npcSpine != null && !m_npcSpine.IsDestroyed)
			{
				if (!m_npcSpine.HasValidSkeleton)
				{
					m_unavailableNpcSpines.Add(location);
					return false;
				}

				ApplyNpcSpinePose(m_npcSpine, animationAsset);
				m_npcSpine.FitToSlot(m_rtSceneCharacter.sizeDelta);
				HideNpcPortraitSprite();
				return true;
			}

			HideNpcSpine();
			m_rtSceneCharacter.gameObject.SetActive(true);
			m_imgSceneCharacter.enabled = false;
			UISpineWidget widget = await CreateWidgetByPathAsync<UISpineWidget>(
				m_rtSceneCharacter.transform, location);
			if (IsDestroyed || version != m_npcPortraitVersion)
			{
				widget?.Destroy();
				return false;
			}

			if (widget == null)
			{
				m_unavailableNpcSpines.Add(location);
				DLogger.Warning("NPC spine prefab not found: {0}", location);
				return false;
			}

			if (!widget.HasValidSkeleton)
			{
				m_unavailableNpcSpines.Add(location);
				widget.Destroy();
				DLogger.Warning("NPC spine data is invalid; using portrait sprite instead: {0}", location);
				return false;
			}

			m_npcSpine = widget;
			m_loadedNpcSpineLocation = location;
			m_loadedNpcPortraitLocation = null;
			ApplyNpcSpinePose(widget, animationAsset);
			widget.FitToSlot(m_rtSceneCharacter.sizeDelta);
			HideNpcPortraitSprite();
			return true;
		}

		private void ApplyNpcSpinePose(UISpineWidget widget, string animationAsset)
		{
			if (widget == null)
			{
				return;
			}

			string pose = MapNpcSpinePose(animationAsset);
			widget.SwitchSkin(pose);
			widget.SetAnimation(pose, true, true);
		}

		private static string MapNpcSpinePose(string animationAsset)
		{
			if (string.IsNullOrWhiteSpace(animationAsset))
			{
				return "zhengchang";
			}

			string key = animationAsset.Trim().ToLowerInvariant();
			if (key == "thoughtful" || key == "think" || key == "sikao")
			{
				return "sikao";
			}

			if (key == "jingya" || key == "surprised" || key == "shock")
			{
				return "jingya";
			}

			return "zhengchang";
		}

		private static string ResolveNpcSpineLocation(string npcId)
		{
			// These three characters use the approved PSD cutouts in this dialogue UI.
			return string.Empty;
		}

		private void HideNpcPortraitSprite()
		{
			if (m_imgSceneCharacter != null)
			{
				m_imgSceneCharacter.enabled = false;
			}
			HideLegacyNpcPortrait();
		}

		private void HideLegacyNpcPortrait()
		{
			if (m_imgNpcPortrait != null)
			{
				m_imgNpcPortrait.enabled = false;
			}

			if (m_tmpNpcPlaceholder != null)
			{
				m_tmpNpcPlaceholder.gameObject.SetActive(false);
			}
		}

		private void HideNpcSpine()
		{
			if (m_npcSpine != null && !m_npcSpine.IsDestroyed)
			{
				m_npcSpine.Destroy();
			}

			m_npcSpine = null;
			m_loadedNpcSpineLocation = null;
			if (m_imgSceneCharacter != null)
			{
				m_imgSceneCharacter.enabled = true;
			}
		}
#else
		private void HideNpcSpine()
		{
		}
#endif

		private static string ResolveNpcPortraitLocation(string npcId, string animationAsset)
		{
			if (string.IsNullOrWhiteSpace(npcId))
			{
				return string.Empty;
			}

			var candidates = new List<string>();
			if (!string.IsNullOrWhiteSpace(animationAsset))
			{
				candidates.Add(npcId + "_" + animationAsset);
				candidates.Add(MapNpcPortraitAlias(npcId) + "_" + animationAsset);
				string expression = MapNpcPortraitExpression(animationAsset);
				if (!string.IsNullOrEmpty(expression))
				{
					candidates.Add(MapNpcPortraitAlias(npcId) + "_" + expression);
				}
			}

			candidates.Add(npcId);
			candidates.Add(MapNpcPortraitAlias(npcId));

			for (int i = 0; i < candidates.Count; i++)
			{
				string location = candidates[i];
				if (IsPortraitLocationReady(location))
				{
					return location;
				}
			}

			return string.Empty;
		}

		private static string MapNpcPortraitExpression(string animationAsset)
		{
			if (string.IsNullOrWhiteSpace(animationAsset))
			{
				return string.Empty;
			}

			switch (animationAsset.Trim().ToLowerInvariant())
			{
				case "angry":
					return "shengqi";
				case "joyful":
					return "xiyue";
				case "moved":
					return "gandong";
				case "sad":
					return "beishang";
				case "thoughtful":
				case "think":
					return "sikao";
				case "surprised":
				case "shock":
					return "jingya";
				default:
					return string.Empty;
			}
		}

		private static bool IsPortraitLocationReady(string location)
		{
			return !string.IsNullOrEmpty(location) &&
				GameModule.ResourceModule.CheckLocationValid(location);
		}

		private static string MapNpcPortraitAlias(string npcId)
		{
			if (npcId == "陈先生")
			{
				return "npc_chen_xiansheng_psd";
			}

			if (npcId == "老周")
			{
				return "npc_lao_zhou_psd";
			}

			if (npcId == "陈果" || npcId == "女孩")
			{
				return "npc_chen_guo_cutout";
			}

			return "npc_" + npcId;
		}

		private void BindSceneBackgroundView()
		{
			Transform bg = transform.Find("BG");
			if (bg == null)
			{
				for (int i = 0; i < transform.childCount; i++)
				{
					Transform child = transform.GetChild(i);
					if (child != null && child.name == "BG")
					{
						bg = child;
						break;
					}
				}
			}

			if (bg == null)
			{
				return;
			}

			m_imgSceneRoot = bg.GetComponent<Image>();
			if (m_imgSceneRoot != null)
			{
				m_imgSceneRoot.enabled = false;
				m_imgSceneRoot.raycastTarget = false;
			}
		}

		private void RefreshSceneBackground(StoryNodeConfig story)
		{
			if (m_imgSceneRoot == null)
			{
				BindSceneBackgroundView();
			}

			string npcId = story == null ? string.Empty : story.NpcId;
			string[] backgrounds = ResolveSceneBackgroundLocations(npcId);
			string[] foregrounds = ResolveSceneForegroundLocations(npcId);
			string[] layers = new string[backgrounds.Length + foregrounds.Length];
			Array.Copy(backgrounds, 0, layers, 0, backgrounds.Length);
			Array.Copy(foregrounds, 0, layers, backgrounds.Length, foregrounds.Length);
			string key = npcId + "|" + string.Join("|", layers);
			EnsureSceneLayerImages(layers.Length);
			EnsureSceneCharacterView();
			if (key == m_loadedSceneKey)
			{
				PlaceSceneCharacterBetweenLayers(backgrounds.Length, layers.Length);
				return;
			}

			m_loadedSceneKey = key;
			int version = ++m_sceneBackgroundVersion;
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			for (int i = 0; i < m_sceneLayerImages.Count; i++)
			{
				Image image = m_sceneLayerImages[i];
				if (image == null)
				{
					continue;
				}
				int siblingIndex = i < backgrounds.Length ? i : i + 1;
				image.transform.SetSiblingIndex(Mathf.Min(siblingIndex, m_imgSceneRoot.transform.childCount - 1));

				if (i >= layers.Length || string.IsNullOrEmpty(layers[i]) ||
					!GameModule.ResourceModule.CheckLocationValid(layers[i]))
				{
					image.gameObject.SetActive(false);
					continue;
				}

				image.gameObject.SetActive(true);
				image.enabled = false;
				string location = layers[i];
				image.SetSprite(location, false, loaded =>
				{
					if (version != m_sceneBackgroundVersion || loaded == null || IsDestroyed)
					{
						return;
					}

					loaded.color = Color.white;
					loaded.preserveAspect = false;
					loaded.raycastTarget = false;
					loaded.type = Image.Type.Simple;
					ApplySceneLayerLayout(loaded, location);
					loaded.enabled = true;
				}, token);
			}
			PlaceSceneCharacterBetweenLayers(backgrounds.Length, layers.Length);
		}

		private void PlaceSceneCharacterBetweenLayers(int backgroundCount, int layerCount)
		{
			if (m_rtSceneCharacter == null) return;
			int foregroundCount = layerCount - backgroundCount;
			int layer = ReadCharacterLayer();
			int insertAt = backgroundCount + (layer == 0 ? 0 : layer == 1 ? Mathf.Min(foregroundCount, Mathf.Max(1, foregroundCount / 2)) : foregroundCount);
			for (int i = 0; i <= layerCount; i++)
			{
				if (i == insertAt) m_rtSceneCharacter.SetAsLastSibling();
				if (i < layerCount && m_sceneLayerImages[i] != null)
					m_sceneLayerImages[i].transform.SetAsLastSibling();
			}
		}

		private static void ApplySceneLayerLayout(Image image, string location)
		{
			if (image == null || string.IsNullOrEmpty(location)) return;

			// All scene exports share a 3840x2160 canvas. Reset full-canvas layers too:
			// a reused Image may still have the previous scene's cropped anchors.
			Vector4 bounds = new Vector4(0f, 0f, 3840f, 2160f);
			switch (location)
			{
				case "scene_chenguo_chair": bounds = new Vector4(61f, 1263f, 835f, 711f); break;
				case "scene_chenguo_desk": bounds = new Vector4(0f, 1583f, 3034f, 577f); break;
				case "scene_chenguo_cat": bounds = new Vector4(1086f, 1354f, 736f, 728f); break;
				case "scene_chenguo_cat_broken": bounds = new Vector4(1086f, 1354f, 753f, 728f); break;
				case "scene_father_bg": bounds = new Vector4(0f, 0f, 3840f, 1729f); break;
				case "scene_father_fg": bounds = new Vector4(0f, 1707f, 3840f, 453f); break;
				case "scene_shop_old_window": bounds = new Vector4(2325f, 0f, 1341f, 1467f); break;
				case "scene_shop_old_plant": bounds = new Vector4(1464f, 200f, 439f, 910f); break;
				case "scene_shop_old_radio": bounds = new Vector4(282f, 1263f, 1703f, 736f); break;
				case "scene_shop_old_desk": bounds = new Vector4(0f, 1333f, 3840f, 827f); break;
				case "scene_shop_old_items": bounds = new Vector4(1095f, 1827f, 800f, 201f); break;
				case "scene_shop_new_outside": bounds = new Vector4(2350f, 1f, 1341f, 1467f); break;
				case "scene_shop_new_plant": bounds = new Vector4(1450f, 99f, 484f, 1044f); break;
				case "scene_shop_new_radio": bounds = new Vector4(283f, 1265f, 1703f, 736f); break;
				case "scene_shop_new_desk": bounds = new Vector4(0f, 1340f, 3837f, 820f); break;
				case "scene_shop_new_items": bounds = new Vector4(1095f, 1827f, 800f, 201f); break;
				case "scene_zhou_window": bounds = new Vector4(2265f, 0f, 1419f, 1432f); break;
				case "scene_zhou_plant": bounds = new Vector4(1398f, 107f, 503f, 1042f); break;
				case "scene_zhou_radio": bounds = new Vector4(292f, 1215f, 1674f, 825f); break;
				case "scene_zhou_desk": bounds = new Vector4(0f, 1840f, 3840f, 320f); break;
				case "scene_zhou_items": bounds = new Vector4(0f, 1383f, 3840f, 777f); break;
				case "scene_zhou_light": bounds = new Vector4(0f, 0f, 2376f, 2160f); break;
			}

			RectTransform rect = image.rectTransform;
			rect.anchorMin = new Vector2(bounds.x / 3840f, 1f - (bounds.y + bounds.w) / 2160f);
			rect.anchorMax = new Vector2((bounds.x + bounds.z) / 3840f, 1f - bounds.y / 2160f);
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
		}

		private void EnsureSceneLayerImages(int count)
		{
			Transform parent = m_imgSceneRoot != null ? m_imgSceneRoot.transform : transform;
			while (m_sceneLayerImages.Count < count)
			{
				int index = m_sceneLayerImages.Count;
				var go = new GameObject("SceneLayer" + index, typeof(RectTransform), typeof(Image));
				go.transform.SetParent(parent, false);
				go.transform.SetSiblingIndex(index);
				RectTransform rect = go.GetComponent<RectTransform>();
				rect.anchorMin = Vector2.zero;
				rect.anchorMax = Vector2.one;
				rect.offsetMin = Vector2.zero;
				rect.offsetMax = Vector2.zero;
				Image image = go.GetComponent<Image>();
				image.raycastTarget = false;
				image.preserveAspect = false;
				m_sceneLayerImages.Add(image);
			}
		}

		private static string[] ResolveSceneBackgroundLocations(string npcId)
		{
			if (npcId == "老周")
			{
				return new[] { "scene_zhou_window", "scene_zhou_bg" };
			}

			if (npcId == "陈果" || npcId == "女孩")
			{
				return new[] { "scene_chenguo_bg" };
			}

			if (npcId == "父亲" || npcId == "陈父")
			{
				return new[] { "scene_father_bg" };
			}

			if (IsShopRenovated())
			{
				return new[] { "scene_shop_new_outside", "scene_shop_new_bg" };
			}

			return new[] { "scene_shop_old_window", "scene_shop_old_bg" };
		}

		private static string[] ResolveSceneForegroundLocations(string npcId)
		{
			if (npcId == "老周")
			{
				return new[] { "scene_zhou_plant", "scene_zhou_radio", "scene_zhou_desk", "scene_zhou_items", "scene_zhou_light" };
			}

			if (npcId == "陈果" || npcId == "女孩")
			{
				return new[] { "scene_chenguo_chair", "scene_chenguo_desk", "scene_chenguo_cat" };
			}

			if (npcId == "父亲" || npcId == "陈父")
			{
				return new[] { "scene_father_fg" };
			}

			if (IsShopRenovated())
			{
				return new[] { "scene_shop_new_plant", "scene_shop_new_radio", "scene_shop_new_desk", "scene_shop_new_items" };
			}

			return new[] { "scene_shop_old_plant", "scene_shop_old_radio", "scene_shop_old_desk", "scene_shop_old_items" };
		}

		private static bool IsShopRenovated()
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			foreach (string flag in saveData.StoryFlags)
			{
				if (string.IsNullOrEmpty(flag))
				{
					continue;
				}

				if (flag.IndexOf("放晴", StringComparison.Ordinal) >= 0 ||
					flag.IndexOf("修好", StringComparison.Ordinal) >= 0 ||
					flag.IndexOf("崭新", StringComparison.Ordinal) >= 0)
				{
					return true;
				}
			}

			return false;
		}

		private void EnsureDialogueAdvanceCatcher()
		{
			if (m_btnDialogueAdvance != null)
			{
				RaiseHudAboveAdvanceCatcher();
				return;
			}

			var go = new GameObject("DialogueAdvanceCatcher", typeof(RectTransform), typeof(EmptyGraph), typeof(Button));
			go.transform.SetParent(transform, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			EmptyGraph graphic = go.GetComponent<EmptyGraph>();
			graphic.raycastTarget = true;
			graphic.color = Color.clear;
			Button button = go.GetComponent<Button>();
			button.transition = Selectable.Transition.None;
			button.targetGraphic = graphic;
			button.onClick.AddListener(OnClickDialogueAdvance);
			m_btnDialogueAdvance = button;
			RaiseHudAboveAdvanceCatcher();
		}

		private void SetDialogueAdvanceCatcherActive(bool active)
		{
			EnsureDialogueAdvanceCatcher();
			if (m_btnDialogueAdvance == null)
			{
				return;
			}

			if (m_layoutEditMode)
			{
				m_layoutAdvanceWanted = active;
				active = false;
			}
			m_btnDialogueAdvance.gameObject.SetActive(active);
			if (active)
			{
				RaiseHudAboveAdvanceCatcher();
			}
		}

		private void RaiseHudAboveAdvanceCatcher()
		{
			if (m_btnDialogueAdvance != null)
			{
				m_btnDialogueAdvance.transform.SetAsLastSibling();
			}

			if (m_btnSettings != null)
			{
				m_btnSettings.transform.SetAsLastSibling();
			}

			if (m_btnEmoCollection != null)
			{
				m_btnEmoCollection.transform.SetAsLastSibling();
			}

			if (m_btnItemCollection != null)
			{
				m_btnItemCollection.transform.SetAsLastSibling();
			}

			if (m_btnRestart != null)
			{
				m_btnRestart.transform.SetAsLastSibling();
			}

			if (m_overlay != null)
			{
				m_overlay.transform.SetAsLastSibling();
			}
		}

		private void OnClickDialogueAdvance()
		{
			if (m_overlay != null && m_overlay.activeInHierarchy)
			{
				return;
			}

			m_advanceClickPending = true;
		}

		private bool ConsumeAdvanceClick()
		{
			if (!m_advanceClickPending)
			{
				return false;
			}

			m_advanceClickPending = false;
			return true;
		}

		private void EnsureRestartButton()
		{
			if (m_btnRestart != null)
			{
				return;
			}

			var go = new GameObject("m_btnRestart", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(transform, false);
			m_btnRestart = go.GetComponent<Button>();
			PlaceCornerButton(m_btnRestart, new Vector2(0f, 1f), new Vector2(210f, -28f), new Vector2(200f, 56f));
			Image image = go.GetComponent<Image>();
			image.color = HudPaperButton;
			image.raycastTarget = true;
			ApplyHudSprite(image, "setting_save_popup_bg", false, gameObject.GetCancellationTokenOnDestroy(), true);
			TextMeshProUGUI label = CreateOverlayText(go.transform, "从头开始", 22, Vector2.zero,
				new Vector2(180f, 48f), TextAlignmentOptions.Center);
			label.overflowMode = TextOverflowModes.Ellipsis;
			label.enableWordWrapping = false;
			RectTransform labelRect = label.rectTransform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(8f, 4f);
			labelRect.offsetMax = new Vector2(-8f, -4f);
			m_btnRestart.onClick.AddListener(ConfirmRestartFromBeginning);
			go.transform.SetAsLastSibling();
		}

		private void ConfirmRestartFromBeginning()
		{
			ShowOverlay("从头开始", "将清除当前进度，从第一天重新开始。确定吗？", new[]
			{
				("确定", (Action)(() => RestartFromBeginning())),
				("取消", (Action)(() => DestroyOverlay()))
			});
		}

		private void RestartFromBeginning()
		{
			m_pendingRestart = false;
			m_dialoguePresentVersion++;
			m_hasReplied = false;
			m_selectedReply = null;
			m_selectedEmotionId = null;
			m_playerReplyShown = false;
			m_currentStoryNode = null;
			SetReplyItemsActive(false);
			SetEmotionItemsActive(false);
			if (m_tfReplayEmoNode != null)
			{
				m_tfReplayEmoNode.gameObject.SetActive(false);
			}

			if (m_tfReplayMessageNode != null)
			{
				m_tfReplayMessageNode.gameObject.SetActive(false);
			}

			NarrativeSaveData.Get.StartNewGame();
			m_messages.Clear();
			if (m_messageList != null && m_messageList.LoopRectView != null)
			{
				m_messageList.LoopRectView.SetListItemCount(0, false);
			}

			SetDialoguePresentationMode(false);
			if (m_tmpNpcSubtitle != null)
			{
				m_tmpNpcSubtitle.text = string.Empty;
			}

			DestroyOverlay();
			if (m_isBusy)
			{
				m_pendingRestart = true;
				return;
			}

			StartStoryAsync().Forget();
		}

		private void ReturnToMainMenu()
		{
			GameModule.UIModule.CloseWindow<BattleMainUI>();
			GameModule.UIModule.ShowWindowAsync<MainWindow>();
		}

		#region 事件

		private partial void OnClickSettingsBtn()
		{
			ShowSettings();
		}

		private partial void OnClickEmoCollectionBtn()
		{
			if (m_layoutEditMode) return;
			ShowCollection();
		}

		#endregion

		protected override void OnUpdate()
		{
			bool toggleDebugPanel = false;
#if ENABLE_INPUT_SYSTEM
			toggleDebugPanel = Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
#else
			toggleDebugPanel = Input.GetKeyDown(KeyCode.F1);
#endif
			if (toggleDebugPanel)
			{
				if (m_overlay == null) ShowDebugPanel();
				else DestroyOverlay();
			}
			base.OnUpdate();
		}
	}
}
