using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using DGame;
using DG.Tweening;
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
		private const string NpcSubtitleRootName = "NpcSubtitlePanel";
		private const string NpcSpeakerLabelPrefix = "SpeakerLabel";
		/// <summary>末行少于这个字数就算孤字，需要处理。</summary>
		private const int SubtitleWidowMinChars = 4;
		private const float SubtitleWidowInsetStep = 10f;
		private const float SubtitleWidowInsetMaxExtra = 60f;
		private const int TYPEWRITER_MS_PER_CHAR = 100;
		private const int TYPEWRITER_PUNCTUATION_EXTRA_MS = 220;
		/// <summary>打字机每 N 个字播一次音，避免长句变成机关枪。</summary>
		private const int TYPEWRITER_SOUND_EVERY_CHARS = 3;
		private const float TYPEWRITER_SOUND_VOLUME = 0.35f;
		private const float MESSAGE_SCROLL_OVERFLOW_EPSILON = 1f;
		private const float MESSAGE_BOTTOM_PADDING = 16f;
		private const float MESSAGE_SCROLL_SENSITIVITY = 0.3f;
		private const float REPLY_PANEL_HEIGHT = 441f;
		private const string NpcPortraitPlaceholderText = "NPC角色素材";
		private static readonly Color HudPaperText = new Color(0.28f, 0.22f, 0.16f, 1f);
		private static readonly Color HudPaperPanel = new Color(0.96f, 0.93f, 0.86f, 1f);
		private static readonly Color HudPaperButton = new Color(0.93f, 0.82f, 0.62f, 1f);

		// 日终 AI 反思：整屏底图 + 纸面排版。底图与纸面区域是绑定的，换图时要同步 ReflectionPaperArea。
		private const string ReflectionBackgroundLocation = "reflection_bg";
		private static readonly Rect ReflectionPaperArea = new Rect(0.296f, 0f, 0.444f, 0.815f);
		private const float ReflectionTextInsetLeft = 123f;
		private const float ReflectionTextInsetRight = 130f;
		private const float ReflectionTitleTop = 95f;
		private const float ReflectionTitleHeight = 64f;
		private const float ReflectionTitleFontSize = 44f;
		private const float ReflectionBodyTop = 180f;
		private const float ReflectionBodyBottom = 190f;
		private const float ReflectionBodyFontSize = 34f;
		private const float ReflectionBodyLineSpacing = 34f;
		private const float ReflectionContinueRight = 118f;
		private const float ReflectionContinueBottom = 96f;
		private const float ReflectionFadeInSeconds = 0.55f;
		private const float ReflectionContinueFadeSeconds = 0.2f;

		// 反思页专用打字节奏：对话系统是 100ms/字，这里贴近「LLM 流式输出」的观感 ——
		// 基线略快 + 每字小抖动 + 每几个字卡一下 + 句读处停得久一点。
		private const int ReflectionTypewriterMsPerChar = 30;
		private const int ReflectionTypewriterJitterMs = 8;
		private const int ReflectionTypewriterBurstMin = 4;
		private const int ReflectionTypewriterBurstMax = 11;
		private const int ReflectionTypewriterPauseMinMs = 90;
		private const int ReflectionTypewriterPauseMaxMs = 220;
		private const int ReflectionTypewriterPunctuationPauseMs = 240;
		private const int ReflectionTypewriterMinStepMs = 12;
		private const int ReflectionTypewriterSoundEvery = 6;
		private static readonly Color ReflectionIconColor = new Color(0.28f, 0.22f, 0.16f, 0.8f);

		// 反思页的输入/打字机状态：整屏点击或回车 = 跳过打字 → 再按一次继续。
		private bool m_reflectionOverlayActive;
		private bool m_reflectionTyping;
		private bool m_reflectionAdvanceLocked;
		private int m_reflectionInputFrame = -1;
		private int m_reflectionPresentVersion;
		private Action m_reflectionContinue;
		private GameObject m_reflectionOverlayRoot;
		private TextMeshProUGUI m_reflectionBody;
		private CanvasGroup m_reflectionContinueGroup;
		private CanvasGroup m_reflectionCoverGroup;
		private readonly List<ReplayMessageItem> m_replyItems = new List<ReplayMessageItem>();
		private readonly List<ReplayEmoItem> m_emotionItems = new List<ReplayEmoItem>();
		private readonly List<(bool IsPlayer, string Text)> m_messages = new List<(bool, string)>();
		private readonly List<Image> m_memorySlotImages = new List<Image>();
		private readonly List<Image> m_memorySlotThumbs = new List<Image>();
		private readonly List<Image> m_memorySlotLabelBacks = new List<Image>();
		private readonly List<int> m_memorySlotVersions = new List<int>();
		private readonly List<TextMeshProUGUI> m_memorySlotLabels = new List<TextMeshProUGUI>();
		private readonly List<Button> m_memoryDeleteButtons = new List<Button>();
		private RectTransform m_memoryTooltip;
		private TextMeshProUGUI m_memoryTooltipName;
		private TextMeshProUGUI m_memoryTooltipDescription;
		private RectTransform m_memoryTooltipFrame;
		private Image m_memoryTooltipImage;
		private int m_memoryTooltipImageVersion;
		private int m_hoveredMemorySlot = -1;
		private int m_messageScrollVersion;
		private int m_npcPortraitVersion;
		private int m_dialoguePresentVersion;
		private bool m_messagePinnedToLatest = true;
		private Button m_btnItemCollection;
		private Image m_imgItemSlot;
		private string m_itemSlotImageId;
		private int m_itemSlotVersion;
		private readonly NarrativeDomainService m_narrativeService = new NarrativeDomainService();
		private readonly NarrativeActionService m_actionService = new NarrativeActionService();
		private readonly NarrativeStoryScheduler m_storyScheduler = new NarrativeStoryScheduler();
		private UILoopListViewWidget m_messageList;
		private GameObject m_overlay;
		private GameObject m_suspendedOverlay;
		private RectTransform m_suspendedOverlayBodyContent;
		private TMP_InputField m_suspendedDebugNodeInput;
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
		private RectTransform m_rtOtherCharacter;
		private Image m_imgOtherCharacter;
		private string m_loadedOtherCharacterLocation;
		private string m_requestedOtherCharacterLocation;
		private CancellationTokenSource m_otherCharacterSpriteRequest;
		private float m_otherCharacterRequestTime;
		private string m_otherCharacterNpcId;
		private int m_otherCharacterVersion;
		private string m_loadedSceneCharacterLocation;
		private string m_requestedSceneCharacterLocation;
		private CancellationTokenSource m_sceneCharacterSpriteRequest;
		private float m_sceneCharacterRequestTime;
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
		private RectTransform m_rtNpcSubtitle;
		private TextMeshProUGUI m_tmpNpcSubtitle;
		private readonly List<TextMeshProUGUI> m_npcSpeakerLabels = new List<TextMeshProUGUI>();
		private CanvasGroup m_cgNpcContinueHint;
		private float m_subtitleBodyTopInset;
		private float m_subtitleBodyBottomInset;
		private float m_subtitleBodySideInset;
		private Button m_btnRestart;
		private Button m_btnDialogueAdvance;
		private bool m_advanceClickPending;
		private bool m_isBusy;
		private bool m_hasReplied;
		private bool m_playerReplyShown;
		private bool m_pendingRestart;
		private bool m_monologuePresentation;

		#region Override

		public override bool FullScreen => true;
		public override bool CanEscClose => false;

		protected override void OnCreate()
		{
			AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat("NarrativeMasterVolume", 1f));
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
			// 布局编辑入口按钮已下线；需要临时调人物大小/位置时按 F2 进入布局编辑
			// EnsureLayoutEditButton();
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

			BindAudioFeedback();
			StartStoryAsync().Forget();
		}

		/// <summary>订阅叙事玩法事件（好感 / 情绪涌现），并预热高频音效、起 BGM。</summary>
		private void BindAudioFeedback()
		{
			m_narrativeService.AffectionChanged += OnAffectionChanged;
			m_narrativeService.EmotionEmerged += OnEmotionEmerged;

			SoundConfigMgr.Instance.Preload(
				SfxNames.StoryText, SfxNames.StoryBubbleIn, SfxNames.MemoryGet, SfxNames.MemoryLand);
			SoundConfigMgr.Instance.PlayBgm(SfxNames.BgmStory, fadeIn: 1.5f);
		}

		private void OnAffectionChanged(string npcId, float before, float after)
		{
			if (after > before)
			{
				SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryAffectionUp);
			}
			else if (after < before)
			{
				SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryAffectionDown);
			}
		}

		private void OnEmotionEmerged(string emotionId)
		{
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryEmotionEmerge);
		}

		protected override void OnRefresh()
		{
			RefreshUI();
		}

		protected override void OnDestroy()
		{
			m_narrativeService.AffectionChanged -= OnAffectionChanged;
			m_narrativeService.EmotionEmerged -= OnEmotionEmerged;
			CancelSpriteRequest(ref m_otherCharacterSpriteRequest);
			CancelSpriteRequest(ref m_sceneCharacterSpriteRequest);
			m_messageScrollVersion++;
			m_npcPortraitVersion++;
			m_dialoguePresentVersion++;
			m_messagePinnedToLatest = true;
			m_memorySlotImages.Clear();
			m_memorySlotThumbs.Clear();
			m_memorySlotVersions.Clear();
			m_memorySlotLabels.Clear();
			m_memoryDeleteButtons.Clear();
			m_memoryTooltip = null;
			m_memoryTooltipName = null;
			m_memoryTooltipDescription = null;
			m_memoryTooltipFrame = null;
			m_memoryTooltipImage = null;
			m_hoveredMemorySlot = -1;
			m_btnItemCollection = null;
			m_imgItemSlot = null;
			m_itemSlotImageId = null;
			m_itemSlotVersion++;
			if (m_overlay != null) UnityEngine.Object.Destroy(m_overlay);
			if (m_suspendedOverlay != null) UnityEngine.Object.Destroy(m_suspendedOverlay);
			m_suspendedOverlay = null;
			m_suspendedOverlayBodyContent = null;
			m_suspendedDebugNodeInput = null;
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
			m_rtOtherCharacter = null;
			m_imgOtherCharacter = null;
			m_loadedOtherCharacterLocation = null;
			m_requestedOtherCharacterLocation = null;
			m_otherCharacterVersion++;
			m_loadedSceneCharacterLocation = null;
			m_requestedSceneCharacterLocation = null;
			m_overlayBodyContent = null;
			m_dialoguePanel = null;
			m_rtNpcSubtitle = null;
			m_tmpNpcSubtitle = null;
			m_npcSpeakerLabels.Clear();
			m_cgNpcContinueHint = null;
			m_btnRestart = null;
			m_btnLayoutEdit = null;
			m_layoutEditButtonLabel = null;
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
			RefreshItemSlot();
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
			// 日终 / 结局切过 BGM，回到叙事时切回来（同曲不会重播）
			SoundConfigMgr.Instance.PlayBgm(SfxNames.BgmStory, fadeIn: 1.5f);
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
			try
			{
				m_actionService.Execute(story.OnEnterActionIds,
					NarrativeSaveData.Get.BuildTurnKey(story.Id) + "|node-enter");
				RefreshMemorySlots();
				RefreshItemSlot();
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
			bool npcDialogue = IsNpcDialogueTurn(story);
			SetDialoguePresentationMode(npcDialogue);
			m_advanceClickPending = false;
			SetDialogueAdvanceCatcherActive(true);
			if (npcDialogue)
				await PresentNpcSubtitleAsync(story, presentVersion);
			else
				await PresentNpcMessageAsync(story.NpcText, presentVersion);
			if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
			{
				return;
			}
			if (npcDialogue)
			{
				await AutoAdvanceStoryNodeAsync(story, presentVersion);
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
			ResizeReplyPanel();
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryOptionAppear);
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
				SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryLocked, DGame.AudioType.UISound);
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
				if (IsNpcDialogueTurn(story))
				{
					TypewriterWaitResult endWait = await WaitForAdvanceClick(presentVersion);
					if (IsDestroyed || presentVersion != m_dialoguePresentVersion ||
						endWait == TypewriterWaitResult.Cancelled) return;
				}
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
				SoundConfigMgr.Instance.PlaySfx(SfxNames.UiError, DGame.AudioType.UISound);
				ShowOverlay("情绪未解锁", "这份情绪还没有在剧情中获得。可以先选择“无”，继续推进故事。",
					new[] { ("关闭", (Action)(() => DestroyOverlay())) }, playSound: false);
				return;
			}

			string emotionId = item.IsNone ? NarrativeRules.NoneEmotionId : item.Emotion?.Id;
			if (emotionId == null || (!item.IsNone &&
				!m_narrativeService.GetAvailableEmotions(m_selectedReply).Contains(emotionId)))
			{
				return;
			}

			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryEmotionSelect);
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
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryReplyConfirm);
			try
			{
				NarrativeTurnResult result = m_narrativeService.CommitTurn(
					m_currentStoryNode, m_selectedReply, m_selectedEmotionId);
				RefreshItemSlot();
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

				item = right;
			}
			else
			{
				LeftMessageItem left = m_messageList.CreateItem<LeftMessageItem>();
				if (left == null)
				{
					return null;
				}

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
			if (message.IsPlayer)
				((RightMessageItem)item).SetMessage(message.Text);
			else
				((LeftMessageItem)item).SetMessage(message.Text);
			return item.LoopItem;
		}

		private void ApplyChatListLayout()
		{
			RectTransform emotionRect = m_tfReplayEmoNode as RectTransform;
			if (emotionRect != null)
			{
				emotionRect.anchorMin = emotionRect.anchorMax = new Vector2(0.5f, 0f);
				emotionRect.pivot = new Vector2(0.5f, 0f);
				emotionRect.anchoredPosition = ReadLayoutPosition("EmotionPanel", new Vector2(0f, 14f));
				emotionRect.sizeDelta = new Vector2(680f, 120f);
			}

			RectTransform replyRect = m_tfReplayMessageNode as RectTransform;
			if (replyRect != null)
			{
				replyRect.anchorMin = replyRect.anchorMax = new Vector2(0.5f, 0f);
				replyRect.pivot = new Vector2(0.5f, 0f);
				replyRect.anchoredPosition = ReadLayoutPosition("ReplyPanel", new Vector2(0f, 14f));
				replyRect.sizeDelta = new Vector2(575f, REPLY_PANEL_HEIGHT);
				Image replyBackground = replyRect.GetComponent<Image>();
				if (replyBackground == null) replyBackground = replyRect.gameObject.AddComponent<Image>();
				replyBackground.raycastTarget = false;
				ApplyHudSprite(replyBackground, "dialogue_reply_option_bg", false,
					gameObject.GetCancellationTokenOnDestroy());
				VerticalLayoutGroup options = replyRect.GetComponent<VerticalLayoutGroup>();
				if (options != null)
				{
					options.padding = new RectOffset(37, 37, 34, 34);
					options.spacing = 10f;
					options.childAlignment = TextAnchor.UpperCenter;
				}
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

		private void ResizeReplyPanel()
		{
			RectTransform panel = m_tfReplayMessageNode as RectTransform;
			if (panel == null) return;
			float height = 68f;
			for (int i = 0; i < m_replyItems.Count; i++)
			{
				if (m_replyItems[i] == null) continue;
				if (i > 0) height += 10f;
				height += m_replyItems[i].rectTransform.rect.height;
			}
			panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(150f, height));
			RectTransform scrollRect = m_scrollMessage != null ? m_scrollMessage.transform as RectTransform : null;
			if (scrollRect != null)
				scrollRect.offsetMin = new Vector2(scrollRect.offsetMin.x, panel.rect.height + 20f);
			LayoutRebuilder.MarkLayoutForRebuild(panel);
		}

		private void ApplyDialogueHudLayout()
		{
			PlaceCornerButton(m_btnSettings, new Vector2(0f, 1f),
				ReadLayoutPosition("Menu", new Vector2(84.5f, -80.5f)), new Vector2(91f, 91f));
			HideButtonLabel(m_btnSettings);
			PlaceCornerButton(m_btnEmoCollection, new Vector2(0.5f, 0.5f),
				ReadLayoutPosition("Collection", new Vector2(753f, -362f)), new Vector2(182f, 166f));
			HideButtonLabel(m_btnEmoCollection);

			if (m_btnItemCollection == null)
			{
				var go = new GameObject("m_btnItemCollection", typeof(RectTransform), typeof(Image), typeof(Button));
				go.transform.SetParent(transform, false);
				m_btnItemCollection = go.GetComponent<Button>();
				m_btnItemCollection.onClick.AddListener(ShowItems);
			}

			PlaceCornerButton(m_btnItemCollection, new Vector2(0.5f, 0.5f),
				ReadLayoutPosition("Items", new Vector2(757f, -146f)), new Vector2(162f, 173f));
			EnsureItemSlot();
			if (m_dialoguePanel != null)
			{
				RectTransform panelRect = m_dialoguePanel.transform as RectTransform;
				panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
				panelRect.pivot = new Vector2(0.5f, 0.5f);
				panelRect.anchoredPosition = ReadLayoutPosition("DialoguePanel", new Vector2(287f, -103.5f));
				panelRect.sizeDelta = new Vector2(1206f, 873f);
			}

			if (m_scrollMemoryList != null)
			{
				RectTransform memoryRect = m_scrollMemoryList.transform as RectTransform;
				memoryRect.anchorMin = memoryRect.anchorMax = new Vector2(0.5f, 1f);
				memoryRect.pivot = new Vector2(0.5f, 1f);
				memoryRect.anchoredPosition = ReadLayoutPosition("MemoryBar", new Vector2(297f, -59f));
				memoryRect.sizeDelta = new Vector2(1126f, 131f);
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

			bool raycastTarget = image.raycastTarget;
			image.SetSprite(location, nativeSize, loaded =>
			{
				if (loaded == null || IsDestroyed)
				{
					return;
				}

				loaded.color = Color.white;
				loaded.preserveAspect = nativeSize;
				loaded.raycastTarget = raycastTarget;
				if (sliced)
				{
					loaded.type = Image.Type.Sliced;
				}
			}, token);
		}

		private void BuildMemorySlots()
		{
			m_memorySlotImages.Clear();
			m_memorySlotThumbs.Clear();
			m_memorySlotVersions.Clear();
			m_memorySlotLabels.Clear();
			m_memoryDeleteButtons.Clear();
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

			layout.childAlignment = TextAnchor.MiddleRight;
			layout.spacing = 12f;
			layout.padding = new RectOffset(24, 144, 16, 16);
			layout.childControlWidth = false;
			layout.childControlHeight = false;
			layout.childForceExpandWidth = false;
			layout.childForceExpandHeight = false;
			content.pivot = new Vector2(1f, 0.5f);
			content.anchorMin = new Vector2(1f, 0f);
			content.anchorMax = Vector2.one;
			content.anchoredPosition = new Vector2(0f, 10f);
			m_scrollMemoryList.horizontal = true;
			m_scrollMemoryList.vertical = false;
			EnsureMemorySlotCount(NarrativeRules.MemoryCapacity);
			EnsureMemoryTooltip(gameObject.GetCancellationTokenOnDestroy());
			RefreshMemorySlots();
		}

		private void EnsureMemorySlotCount(int count)
		{
			if (m_scrollMemoryList == null || m_scrollMemoryList.content == null) return;
			RectTransform content = m_scrollMemoryList.content;
			bool changed = m_memorySlotLabels.Count != count;
			while (m_memorySlotLabels.Count > count)
			{
				int last = m_memorySlotLabels.Count - 1;
				GameObject slot = m_memorySlotImages[last].gameObject;
				m_memorySlotImages.RemoveAt(last);
				m_memorySlotThumbs.RemoveAt(last);
				m_memorySlotLabelBacks.RemoveAt(last);
				m_memorySlotVersions.RemoveAt(last);
				m_memorySlotLabels.RemoveAt(last);
				m_memoryDeleteButtons.RemoveAt(last);
				slot.SetActive(false);
				UnityEngine.Object.Destroy(slot);
			}

			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			for (int i = m_memorySlotLabels.Count; i < count; i++)
			{
				var slot = new GameObject("MemorySlot" + i, typeof(RectTransform), typeof(Image));
				slot.transform.SetParent(content, false);
				slot.transform.SetAsFirstSibling();
				RectTransform slotRect = slot.GetComponent<RectTransform>();
				slotRect.sizeDelta = new Vector2(78f, 78f);
				Image slotImage = slot.GetComponent<Image>();
				slotImage.color = new Color(1f, 1f, 1f, 0.35f);
				ApplyHudSprite(slotImage, "dialogue_memory_slot_bg", false, token, true);

				// 记忆缩略图：压在底图之上、名字之下。无图时 disabled，回退原来的纯色底。
				var thumbGo = new GameObject("Thumb", typeof(RectTransform), typeof(Image));
				thumbGo.transform.SetParent(slot.transform, false);
				RectTransform thumbRect = thumbGo.GetComponent<RectTransform>();
				thumbRect.anchorMin = Vector2.zero;
				thumbRect.anchorMax = Vector2.one;
				thumbRect.offsetMin = new Vector2(3f, 3f);
				thumbRect.offsetMax = new Vector2(-3f, -3f);
				Image thumb = thumbGo.GetComponent<Image>();
				thumb.raycastTarget = false;
				thumb.enabled = false;

				// 名字底板：有图时只占底部 18px，避免文字压在插画上。
				var labelBackGo = new GameObject("LabelBack", typeof(RectTransform), typeof(Image));
				labelBackGo.transform.SetParent(slot.transform, false);
				RectTransform labelBackRect = labelBackGo.GetComponent<RectTransform>();
				labelBackRect.anchorMin = new Vector2(0f, 0f);
				labelBackRect.anchorMax = new Vector2(1f, 0f);
				labelBackRect.pivot = new Vector2(0.5f, 0f);
				labelBackRect.anchoredPosition = new Vector2(0f, 3f);
				labelBackRect.sizeDelta = new Vector2(0f, 18f);
				Image labelBack = labelBackGo.GetComponent<Image>();
				labelBack.color = new Color(HudPaperPanel.r, HudPaperPanel.g, HudPaperPanel.b, 0.88f);
				labelBack.raycastTarget = false;
				labelBack.enabled = false;

				var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
				labelGo.transform.SetParent(slot.transform, false);
				RectTransform labelRect = labelGo.GetComponent<RectTransform>();
				labelRect.anchorMin = Vector2.zero;
				labelRect.anchorMax = Vector2.one;
				labelRect.offsetMin = Vector2.zero;
				labelRect.offsetMax = Vector2.zero;
				TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
				label.text = string.Empty;
				label.fontSize = 28;
				label.color = new Color(0.45f, 0.4f, 0.35f, 0.7f);
				label.alignment = TextAlignmentOptions.Center;
				label.raycastTarget = false;
				label.enableWordWrapping = true;
				label.overflowMode = TextOverflowModes.Ellipsis;
				label.enableAutoSizing = true;
				label.fontSizeMin = 10f;
				label.fontSizeMax = 28f;
				var hover = slot.AddComponent<EventTrigger>();
				var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
				int slotIndex = i;
				enter.callback.AddListener(_ => ShowMemoryTooltip(slotIndex));
				hover.triggers.Add(enter);
				var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
				exit.callback.AddListener(_ => HideMemoryTooltip(slotIndex));
				hover.triggers.Add(exit);
				var delete = new GameObject("Delete", typeof(RectTransform), typeof(Image), typeof(Button));
				delete.transform.SetParent(slot.transform, false);
				RectTransform deleteRect = delete.GetComponent<RectTransform>();
				deleteRect.anchorMin = deleteRect.anchorMax = Vector2.one;
				deleteRect.pivot = new Vector2(0.5f, 0.5f);
				deleteRect.anchoredPosition = new Vector2(-7f, -7f);
				deleteRect.sizeDelta = new Vector2(30f, 30f);
				Image deleteHitArea = delete.GetComponent<Image>();
				deleteHitArea.color = new Color(1f, 1f, 1f, 0.001f);
				Button deleteButton = delete.GetComponent<Button>();
				deleteButton.transition = Selectable.Transition.None;
				deleteButton.targetGraphic = deleteHitArea;
				deleteButton.onClick.AddListener(() => DeleteMemoryFromSlot(slotIndex));
				CreateMemoryDeleteStroke(delete.transform, "CrossA", 45f);
				CreateMemoryDeleteStroke(delete.transform, "CrossB", -45f);
				m_memorySlotImages.Add(slotImage);
				m_memorySlotThumbs.Add(thumb);
				m_memorySlotLabelBacks.Add(labelBack);
				m_memorySlotVersions.Add(0);
				m_memorySlotLabels.Add(label);
				m_memoryDeleteButtons.Add(deleteButton);
			}

			float viewportWidth = m_scrollMemoryList.viewport != null
				? m_scrollMemoryList.viewport.rect.width : m_scrollMemoryList.GetComponent<RectTransform>().rect.width;
			HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
			float slotsWidth = count * 78f + Mathf.Max(0, count - 1) * 12f +
				layout.padding.left + layout.padding.right;
			content.sizeDelta = new Vector2(Mathf.Max(viewportWidth, slotsWidth), 0f);
			if (changed && slotsWidth > viewportWidth)
			{
				Canvas.ForceUpdateCanvases();
				m_scrollMemoryList.horizontalNormalizedPosition = 0f;
			}
		}

		private static void CreateMemoryDeleteStroke(Transform parent, string name, float angle)
		{
			var stroke = new GameObject(name, typeof(RectTransform), typeof(Image));
			stroke.transform.SetParent(parent, false);
			RectTransform rect = stroke.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = new Vector2(21f, 4f);
			rect.localRotation = Quaternion.Euler(0f, 0f, angle);
			Image image = stroke.GetComponent<Image>();
			image.color = HudPaperText;
			image.raycastTarget = false;
		}

		private void EnsureMemoryTooltip(CancellationToken token)
		{
			if (m_memoryTooltip != null) return;
			var tooltip = new GameObject("MemoryTooltip", typeof(RectTransform), typeof(Image));
			tooltip.transform.SetParent(transform, false);
			m_memoryTooltip = tooltip.GetComponent<RectTransform>();
			m_memoryTooltip.anchorMin = m_memoryTooltip.anchorMax = new Vector2(1f, 1f);
			m_memoryTooltip.pivot = new Vector2(1f, 1f);
			m_memoryTooltip.anchoredPosition = new Vector2(-72f, -64f);
			m_memoryTooltip.sizeDelta = new Vector2(280f, 281f);
			Image background = tooltip.GetComponent<Image>();
			background.raycastTarget = false;
			ApplyHudSprite(background, "dialogue_memory_tooltip", false, token);

			// 记忆图：用美术给的 imageframe（207×118）等比收到 160×91，512 方图会居中留边。
			var frameGo = new GameObject("ImageFrame", typeof(RectTransform), typeof(Image));
			frameGo.transform.SetParent(tooltip.transform, false);
			m_memoryTooltipFrame = frameGo.GetComponent<RectTransform>();
			m_memoryTooltipFrame.anchorMin = m_memoryTooltipFrame.anchorMax = new Vector2(0.5f, 1f);
			m_memoryTooltipFrame.pivot = new Vector2(0.5f, 1f);
			m_memoryTooltipFrame.anchoredPosition = new Vector2(0f, -8f);
			m_memoryTooltipFrame.sizeDelta = new Vector2(160f, 91f);
			Image frameImage = frameGo.GetComponent<Image>();
			frameImage.raycastTarget = false;
			ApplyHudSprite(frameImage, "dialogue_memory_imageframe", false, token);

			var imageGo = new GameObject("Image", typeof(RectTransform), typeof(Image));
			imageGo.transform.SetParent(frameGo.transform, false);
			RectTransform imageRect = imageGo.GetComponent<RectTransform>();
			imageRect.anchorMin = Vector2.zero;
			imageRect.anchorMax = Vector2.one;
			imageRect.offsetMin = new Vector2(7f, 7f);
			imageRect.offsetMax = new Vector2(-7f, -7f);
			m_memoryTooltipImage = imageGo.GetComponent<Image>();
			m_memoryTooltipImage.raycastTarget = false;
			m_memoryTooltipImage.preserveAspect = true;
			frameGo.SetActive(false);

			var description = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI));
			description.transform.SetParent(tooltip.transform, false);
			RectTransform descriptionRect = description.GetComponent<RectTransform>();
			descriptionRect.anchorMin = descriptionRect.anchorMax = new Vector2(0.5f, 1f);
			descriptionRect.pivot = new Vector2(0.5f, 1f);
			descriptionRect.anchoredPosition = new Vector2(0f, -104f);
			descriptionRect.sizeDelta = new Vector2(215f, 120f);
			m_memoryTooltipDescription = description.GetComponent<TextMeshProUGUI>();
			m_memoryTooltipDescription.fontSize = 17f;
			m_memoryTooltipDescription.enableAutoSizing = true;
			m_memoryTooltipDescription.fontSizeMin = 12f;
			m_memoryTooltipDescription.fontSizeMax = 17f;
			m_memoryTooltipDescription.color = HudPaperText;
			m_memoryTooltipDescription.alignment = TextAlignmentOptions.Center;
			m_memoryTooltipDescription.enableWordWrapping = true;
			m_memoryTooltipDescription.overflowMode = TextOverflowModes.Ellipsis;
			m_memoryTooltipDescription.raycastTarget = false;

			var name = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
			name.transform.SetParent(tooltip.transform, false);
			RectTransform nameRect = name.GetComponent<RectTransform>();
			nameRect.anchorMin = nameRect.anchorMax = new Vector2(0.5f, 0f);
			nameRect.pivot = new Vector2(0.5f, 0f);
			nameRect.anchoredPosition = new Vector2(0f, 10f);
			nameRect.sizeDelta = new Vector2(190f, 34f);
			m_memoryTooltipName = name.GetComponent<TextMeshProUGUI>();
			m_memoryTooltipName.fontSize = 20f;
			m_memoryTooltipName.enableAutoSizing = true;
			m_memoryTooltipName.fontSizeMin = 14f;
			m_memoryTooltipName.fontSizeMax = 20f;
			m_memoryTooltipName.color = HudPaperText;
			m_memoryTooltipName.alignment = TextAlignmentOptions.Center;
			m_memoryTooltipName.enableWordWrapping = true;
			m_memoryTooltipName.overflowMode = TextOverflowModes.Ellipsis;
			m_memoryTooltipName.raycastTarget = false;
			tooltip.SetActive(false);
		}

		private void ShowMemoryTooltip(int slotIndex)
		{
			if (m_overlay != null || m_monologuePresentation || m_scrollMemoryList == null ||
				!m_scrollMemoryList.gameObject.activeInHierarchy)
			{
				HideMemoryTooltip();
				return;
			}
			IReadOnlyList<string> ids = NarrativeSaveData.Get.GetMemoryIdsInAcquisitionOrder();
			if (m_memoryTooltip == null || slotIndex < 0 || slotIndex >= ids.Count)
			{
				HideMemoryTooltip();
				return;
			}

			MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(ids[slotIndex]);
			m_hoveredMemorySlot = slotIndex;
			m_memoryTooltipName.text = memory == null ? ids[slotIndex] : memory.MemoryName;
			m_memoryTooltipDescription.text = memory == null ? string.Empty : memory.Description;
			ApplyMemoryTooltipImage(memory == null ? null : memory.ImageId);
			m_memoryTooltip.gameObject.SetActive(true);
			m_memoryTooltip.SetAsLastSibling();
		}

		// tooltip 里的记忆图：无图时把整个 imageframe 收起来，退回纯文字 tooltip。
		private void ApplyMemoryTooltipImage(string imageId)
		{
			m_memoryTooltipImageVersion++;
			int version = m_memoryTooltipImageVersion;
			bool hasImage = !string.IsNullOrWhiteSpace(imageId)
				&& GameModule.ResourceModule.CheckLocationValid(imageId);
			if (m_memoryTooltipFrame != null) m_memoryTooltipFrame.gameObject.SetActive(hasImage);
			if (m_memoryTooltipImage == null) return;
			m_memoryTooltipImage.sprite = null;
			m_memoryTooltipImage.enabled = false;
			if (!hasImage) return;
			m_memoryTooltipImage.SetSprite(imageId, false, loaded =>
			{
				if (IsDestroyed || loaded == null || loaded.sprite == null) return;
				if (version != m_memoryTooltipImageVersion) return;
				loaded.enabled = true;
				loaded.preserveAspect = true;
				loaded.raycastTarget = false;
			}, gameObject.GetCancellationTokenOnDestroy());
		}

		private void HideMemoryTooltip(int slotIndex = -1)
		{
			if (slotIndex >= 0 && m_hoveredMemorySlot != slotIndex) return;
			m_hoveredMemorySlot = -1;
			if (m_memoryTooltip != null) m_memoryTooltip.gameObject.SetActive(false);
		}

		private void DeleteMemoryFromSlot(int slotIndex)
		{
			if (m_layoutEditMode || m_overlay != null) return;
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			IReadOnlyList<string> ids = saveData.GetMemoryIdsInAcquisitionOrder();
			if (slotIndex < 0 || slotIndex >= ids.Count || !saveData.RemoveMemory(ids[slotIndex])) return;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryDelete);
			HideMemoryTooltip();
			RefreshUI();
			if (saveData.DayPhase == NarrativeDayPhase.MemoryCleanup)
				ShowMemoryCleanup();
		}

		private void RefreshMemorySlots()
		{
			if (m_memorySlotLabels.Count == 0)
			{
				return;
			}

			IReadOnlyList<string> ids = NarrativeSaveData.Get.GetMemoryIdsInAcquisitionOrder();
			EnsureMemorySlotCount(Mathf.Max(NarrativeRules.MemoryCapacity, ids.Count));
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			for (int i = 0; i < m_memorySlotLabels.Count; i++)
			{
				TextMeshProUGUI label = m_memorySlotLabels[i];
				if (label == null)
				{
					continue;
				}
				if (i < m_memoryDeleteButtons.Count && m_memoryDeleteButtons[i] != null)
					m_memoryDeleteButtons[i].gameObject.SetActive(i < ids.Count);

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
					ApplyMemorySlotArt(i, memory == null ? null : memory.ImageId, token);
				}
				else
				{
					label.text = string.Empty;
					label.enableAutoSizing = true;
					label.fontSizeMin = 16f;
					label.fontSizeMax = 28f;
					label.color = new Color(0.45f, 0.4f, 0.35f, 0.7f);
					ApplyMemorySlotArt(i, null, token);
				}
			}
			if (m_hoveredMemorySlot >= 0) ShowMemoryTooltip(m_hoveredMemorySlot);
		}

		// 记忆槽位：有图走「缩略图 + 底部名字条」，无图回到原来的「纯色底 + 居中名字」。
		private void ApplyMemorySlotArt(int slotIndex, string imageId, CancellationToken token)
		{
			if (slotIndex < 0 || slotIndex >= m_memorySlotThumbs.Count) return;
			Image thumb = m_memorySlotThumbs[slotIndex];
			Image labelBack = m_memorySlotLabelBacks[slotIndex];
			TextMeshProUGUI label = m_memorySlotLabels[slotIndex];
			m_memorySlotVersions[slotIndex]++;
			int version = m_memorySlotVersions[slotIndex];

			bool hasImage = !string.IsNullOrWhiteSpace(imageId)
				&& GameModule.ResourceModule.CheckLocationValid(imageId);
			if (thumb != null)
			{
				thumb.enabled = false;
				thumb.sprite = null;
			}
			if (labelBack != null) labelBack.enabled = hasImage;

			if (label != null)
			{
				RectTransform labelRect = label.rectTransform;
				if (hasImage)
				{
					labelRect.anchorMin = new Vector2(0f, 0f);
					labelRect.anchorMax = new Vector2(1f, 0f);
					labelRect.pivot = new Vector2(0.5f, 0f);
					labelRect.anchoredPosition = new Vector2(0f, 3f);
					labelRect.sizeDelta = new Vector2(0f, 18f);
					label.fontSizeMin = 8f;
					label.fontSizeMax = 11f;
				}
				else
				{
					labelRect.anchorMin = Vector2.zero;
					labelRect.anchorMax = Vector2.one;
					labelRect.pivot = new Vector2(0.5f, 0.5f);
					labelRect.anchoredPosition = Vector2.zero;
					labelRect.offsetMin = Vector2.zero;
					labelRect.offsetMax = Vector2.zero;
				}
			}

			if (!hasImage || thumb == null) return;
			thumb.SetSprite(imageId, false, loaded =>
			{
				if (IsDestroyed || loaded == null || loaded.sprite == null) return;
				if (slotIndex >= m_memorySlotThumbs.Count || m_memorySlotThumbs[slotIndex] != loaded) return;
				if (m_memorySlotVersions[slotIndex] != version) return;
				loaded.enabled = true;
				loaded.preserveAspect = true;
				loaded.raycastTarget = false;
			}, token);
		}

		private void EnsureItemSlot()
		{
			if (m_btnItemCollection == null || m_imgItemSlot != null) return;
			var go = new GameObject("CurrentItemSlot", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(m_btnItemCollection.transform, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(62f, 62f);
			rect.anchoredPosition = new Vector2(0f, 15f);
			m_imgItemSlot = go.GetComponent<Image>();
			m_imgItemSlot.preserveAspect = true;
			m_imgItemSlot.raycastTarget = false;
			m_imgItemSlot.enabled = false;
		}

		private void RefreshItemSlot()
		{
			EnsureItemSlot();
			if (m_imgItemSlot == null) return;
			IReadOnlyList<string> ids = NarrativeSaveData.Get.GetNarrativeItemIdsInStableOrder();
			NarrativeItemConfig item = ids.Count == 0 ? null : NarrativeConfigMgr.Instance.GetNarrativeItem(ids[ids.Count - 1]);
			string imageId = item == null ? null : item.ImageId;
			if (string.IsNullOrWhiteSpace(imageId) || !GameModule.ResourceModule.CheckLocationValid(imageId))
			{
				m_itemSlotImageId = null;
				m_itemSlotVersion++;
				m_imgItemSlot.enabled = false;
				return;
			}
			if (imageId == m_itemSlotImageId && m_imgItemSlot.sprite != null) return;
			m_itemSlotImageId = imageId;
			int version = ++m_itemSlotVersion;
			m_imgItemSlot.SetSprite(imageId, false, loaded =>
			{
				if (IsDestroyed || version != m_itemSlotVersion || loaded == null || loaded.sprite == null) return;
				loaded.enabled = true;
				loaded.preserveAspect = true;
				loaded.raycastTarget = false;
			}, gameObject.GetCancellationTokenOnDestroy());
		}

		private void ShowItems()
		{
			if (m_layoutEditMode) return;
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			IReadOnlyList<string> ownedIds = saveData.GetNarrativeItemIdsInStableOrder();
			if (ownedIds.Count == 1)
			{
				ShowNarrativeItem(ownedIds[0]);
				return;
			}
			var buttons = new List<(string Label, Action Action)>();
			foreach (string itemId in ownedIds)
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

		private void BindDialoguePanel()
		{
			if (m_scrollMessage == null || m_scrollMessage.transform.parent == null)
			{
				m_dialoguePanel = m_scrollMessage == null ? null : m_scrollMessage.gameObject;
				return;
			}

			m_dialoguePanel = m_scrollMessage.transform.parent.gameObject;
		}

		private void SetDialoguePresentationMode(bool showSubtitle)
		{
			if (m_dialoguePanel != null)
			{
				m_dialoguePanel.SetActive(!showSubtitle);
			}
			if (m_scrollMessage != null) m_scrollMessage.gameObject.SetActive(!showSubtitle);

			if (m_rtNpcSubtitle != null)
			{
				m_rtNpcSubtitle.gameObject.SetActive(showSubtitle);
				if (!showSubtitle)
				{
					ClearNpcSubtitle();
				}
			}
		}

		private void EnsureNpcSubtitle()
		{
			if (m_rtNpcSubtitle != null)
			{
				return;
			}

			Transform root = transform.Find(NpcSubtitleRootName);
			if (root == null)
			{
				DLogger.Error($"BattleMainUI 缺少子节点 {NpcSubtitleRootName}，请执行 DGame Tools/剧情/重建 NPC 字幕 Prefab");
				return;
			}

			m_rtNpcSubtitle = root as RectTransform;
			if (m_rtNpcSubtitle != null)
			{
				m_rtNpcSubtitle.anchoredPosition = ReadLayoutPosition("NpcSubtitle", new Vector2(0f, 100f));
			}

			CollectSpeakerLabels(root);
			if (m_npcSpeakerLabels.Count == 0)
			{
				DLogger.Warning($"{NpcSubtitleRootName} 下没有名字以 {NpcSpeakerLabelPrefix} 开头的文本节点，说话人不会显示");
			}

			m_tmpNpcSubtitle = FindSubtitleText(root, "Frame/BodyText");
			if (m_tmpNpcSubtitle == null)
			{
				DLogger.Error($"{NpcSubtitleRootName} 缺少 Frame/BodyText，字幕无法显示");
				root.gameObject.SetActive(false);
				return;
			}

			m_subtitleBodyTopInset = -m_tmpNpcSubtitle.rectTransform.offsetMax.y;
			m_subtitleBodyBottomInset = m_tmpNpcSubtitle.rectTransform.offsetMin.y;
			m_subtitleBodySideInset = m_tmpNpcSubtitle.rectTransform.offsetMin.x;

			Transform hint = root.Find("Frame/ContinueHint");
			if (hint != null)
			{
				m_cgNpcContinueHint = hint.GetComponent<CanvasGroup>();
				if (m_cgNpcContinueHint == null)
				{
					m_cgNpcContinueHint = hint.gameObject.AddComponent<CanvasGroup>();
				}

				m_cgNpcContinueHint.alpha = 0f;
				m_cgNpcContinueHint.blocksRaycasts = false;
				m_cgNpcContinueHint.interactable = false;
			}

			root.gameObject.SetActive(false);
		}

		private static TextMeshProUGUI FindSubtitleText(Transform root, string path)
		{
			Transform target = root.Find(path);
			return target == null ? null : target.GetComponent<TextMeshProUGUI>();
		}

		/// <summary>
		/// 名字可能有多层（阴影、描边副本），全部按 SpeakerLabel 前缀收集，写入时一起同步，
		/// 在 prefab 里再加 SpeakerLabelShadow2 之类不用改代码。
		/// </summary>
		private void CollectSpeakerLabels(Transform root)
		{
			m_npcSpeakerLabels.Clear();
			TextMeshProUGUI[] candidates = root.GetComponentsInChildren<TextMeshProUGUI>(true);
			for (int i = 0; i < candidates.Length; i++)
			{
				TextMeshProUGUI candidate = candidates[i];
				if (candidate == null)
				{
					continue;
				}

				if (candidate.gameObject.name.StartsWith(NpcSpeakerLabelPrefix, StringComparison.Ordinal))
				{
					m_npcSpeakerLabels.Add(candidate);
				}
			}
		}

		private void SetNpcSpeaker(string speaker)
		{
			speaker = speaker ?? string.Empty;
			for (int i = 0; i < m_npcSpeakerLabels.Count; i++)
			{
				m_npcSpeakerLabels[i].text = speaker;
			}
		}

		private void ClearNpcSubtitle()
		{
			SetNpcSpeaker(string.Empty);

			if (m_tmpNpcSubtitle != null)
			{
				m_tmpNpcSubtitle.text = string.Empty;
				m_tmpNpcSubtitle.maxVisibleCharacters = 0;
			}

			SetContinueHintVisible(false, true);
		}

		private void SetContinueHintVisible(bool visible, bool immediate = false)
		{
			if (m_cgNpcContinueHint == null)
			{
				return;
			}

			m_cgNpcContinueHint.DOKill();
			if (immediate)
			{
				m_cgNpcContinueHint.alpha = visible ? 1f : 0f;
				return;
			}

			m_cgNpcContinueHint.DOFade(visible ? 1f : 0f, 0.18f);
		}

		/// <summary>框高由正文实际高度决定，名字与边框锚在顶边，会跟着一起移动。</summary>
		private void LayoutNpcSubtitle(float textHeight)
		{
			if (m_rtNpcSubtitle == null)
			{
				return;
			}

			float height = Mathf.Ceil(Mathf.Max(0f, textHeight)) + m_subtitleBodyTopInset + m_subtitleBodyBottomInset;
			m_rtNpcSubtitle.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
		}

		/// <summary>
		/// 正文排版并规避末行孤字（一行只剩一两个字）。做法和 InDesign 的多行复合器同思路：
		/// 先按 prefab 的基准宽度排，末行不足 <see cref="SubtitleWidowMinChars"/> 字就逐档收窄正文区重排，
		/// 只在行数不变时接受；收窄救不回来才退回基准宽度、插换行把字挤到末行。
		/// 收窄幅度只有几十像素且正文居中，实际看不出差别。
		/// </summary>
		private string ResolveSubtitleBodyLayout(string body)
		{
			ApplySubtitleSideInset(m_subtitleBodySideInset);
			m_tmpNpcSubtitle.text = body;
			Canvas.ForceUpdateCanvases();
			m_tmpNpcSubtitle.ForceMeshUpdate();

			int lineCount = m_tmpNpcSubtitle.textInfo.lineCount;
			if (lineCount < 2 || CountLastLineChars() >= SubtitleWidowMinChars)
			{
				return body;
			}

			for (float extra = SubtitleWidowInsetStep; extra <= SubtitleWidowInsetMaxExtra; extra += SubtitleWidowInsetStep)
			{
				ApplySubtitleSideInset(m_subtitleBodySideInset + extra);
				Canvas.ForceUpdateCanvases();
				m_tmpNpcSubtitle.ForceMeshUpdate();
				if (m_tmpNpcSubtitle.textInfo.lineCount != lineCount)
				{
					continue;
				}

				if (CountLastLineChars() >= SubtitleWidowMinChars)
				{
					return body;
				}
			}

			ApplySubtitleSideInset(m_subtitleBodySideInset);
			Canvas.ForceUpdateCanvases();
			m_tmpNpcSubtitle.ForceMeshUpdate();
			return PushCharsToLastLine(body);
		}

		private void ApplySubtitleSideInset(float inset)
		{
			if (m_tmpNpcSubtitle == null)
			{
				return;
			}

			RectTransform rect = m_tmpNpcSubtitle.rectTransform;
			Vector2 min = rect.offsetMin;
			Vector2 max = rect.offsetMax;
			min.x = inset;
			max.x = -inset;
			rect.offsetMin = min;
			rect.offsetMax = max;
		}

		private int CountLastLineChars()
		{
			TMP_TextInfo info = m_tmpNpcSubtitle == null ? null : m_tmpNpcSubtitle.textInfo;
			if (info == null || info.lineCount == 0)
			{
				return 0;
			}

			return info.lineInfo[info.lineCount - 1].characterCount;
		}

		/// <summary>
		/// 兜底：把倒数第二行末尾的几个字挪到末行。中文允许任意位置断行，所以总能补够字数；
		/// 代价是断点可能落在词中间，只在收窄宽度也救不回来时才用。
		/// </summary>
		private string PushCharsToLastLine(string body)
		{
			// 换行符和富文本标签会让 TMP 的字符索引与 string 索引错位，这两种情况不挪字。
			if (string.IsNullOrEmpty(body) || body.IndexOf('\n') >= 0 || body.IndexOf('<') >= 0)
			{
				return body;
			}

			TMP_TextInfo info = m_tmpNpcSubtitle.textInfo;
			if (info == null || info.lineCount < 2)
			{
				return body;
			}

			int originalLineCount = info.lineCount;
			int lastCount = info.lineInfo[originalLineCount - 1].characterCount;
			int secondLine = originalLineCount - 2;
			int secondStart = info.lineInfo[secondLine].firstCharacterIndex;
			int secondCount = info.lineInfo[secondLine].characterCount;
			int move = SubtitleWidowMinChars - lastCount;
			if (move <= 0 || secondCount - move < SubtitleWidowMinChars)
			{
				return body;
			}

			int insertAt = secondStart + secondCount - move;
			if (insertAt <= 0 || insertAt >= body.Length)
			{
				return body;
			}

			m_tmpNpcSubtitle.text = body.Insert(insertAt, "\n");
			Canvas.ForceUpdateCanvases();
			m_tmpNpcSubtitle.ForceMeshUpdate();

			// 补字后多出一行说明末行放不下，回退到未处理版本。
			if (m_tmpNpcSubtitle.textInfo.lineCount > originalLineCount)
			{
				m_tmpNpcSubtitle.text = body;
				Canvas.ForceUpdateCanvases();
				m_tmpNpcSubtitle.ForceMeshUpdate();
				return body;
			}

			return m_tmpNpcSubtitle.text;
		}

		private async UniTask PresentNpcSubtitleAsync(StoryNodeConfig story, int presentVersion)
		{
			EnsureNpcSubtitle();
			if (m_rtNpcSubtitle == null || m_tmpNpcSubtitle == null)
			{
				return;
			}

			string speaker = story.NpcId ?? string.Empty;
			string body = story.NpcText ?? string.Empty;
			m_rtNpcSubtitle.gameObject.SetActive(true);
			SetContinueHintVisible(false, true);

			SetNpcSpeaker(speaker);

			if (string.IsNullOrEmpty(body))
			{
				m_tmpNpcSubtitle.text = string.Empty;
				LayoutNpcSubtitle(0f);
				return;
			}

			// 先把整段排完版再逐字揭开，框高和居中位置一次定死，打字过程中不会抖。
			string layoutText = ResolveSubtitleBodyLayout(body);
			m_tmpNpcSubtitle.maxVisibleCharacters = 0;
			LayoutNpcSubtitle(m_tmpNpcSubtitle.preferredHeight);

			// 挪字兜底可能插了换行，标点节奏按去掉换行的文本算。
			string pacingText = layoutText.IndexOf('\n') >= 0 ? layoutText.Replace("\n", string.Empty) : layoutText;
			int total = m_tmpNpcSubtitle.textInfo.characterCount;
			for (int i = 1; i <= total; i++)
			{
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return;
				}

				m_tmpNpcSubtitle.maxVisibleCharacters = i;
				PlayTypewriterTick(i, pacingText);
				int delayMs = TYPEWRITER_MS_PER_CHAR;
				if (i - 1 < pacingText.Length && IsTypewriterPunctuation(pacingText[i - 1]))
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
					m_tmpNpcSubtitle.maxVisibleCharacters = total;
					break;
				}
			}

			if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
			{
				return;
			}

			SetContinueHintVisible(true);
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

				PlayTypewriterTick(i, text);
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

		/// <summary>打字机音效节流：每 N 个字一次，跳过空白与标点，音调轻微随机避免机械重复。</summary>
		private void PlayTypewriterTick(int charIndex, string text, int everyChars = TYPEWRITER_SOUND_EVERY_CHARS)
		{
			if (everyChars <= 0) return;
			if (charIndex % everyChars != 0) return;
			if (text == null || charIndex <= 0 || charIndex > text.Length) return;

			char current = text[charIndex - 1];
			if (char.IsWhiteSpace(current) || char.IsPunctuation(current)) return;

			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryText, DGame.AudioType.Sound,
				TYPEWRITER_SOUND_VOLUME, UnityEngine.Random.Range(0.96f, 1.04f));
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

				if (ConsumeAdvanceClick())
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

				if (ConsumeAdvanceClick())
				{
					return TypewriterWaitResult.Continue;
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
			}
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
				if (m_messages[index].IsPlayer)
					m_messageList.GetItem<RightMessageItem>(shown.GoId)?.SetMessage(text);
				else
					m_messageList.GetItem<LeftMessageItem>(shown.GoId)?.SetMessage(text);
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
			if (isPlayer)
			{
				SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryBubbleIn);
			}
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
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StorySessionEnd);
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			NarrativeSessionConfig next = m_storyScheduler.FindNextSession(saveData);
			if (next == null && m_storyScheduler.HasUncompletedSessions(saveData) &&
				m_storyScheduler.FindNextScheduledDay(saveData) <= 0)
			{
				ShowOverlay("剧情配置异常", "当前 Session 没有可用的显式后继，但仍有未完成 Session。请检查剧情配置。", new[]
				{
					("回到标题", (Action)(() => ReturnToMainMenu()))
				});
				return;
			}

			EnterDayEnd();
			await UniTask.CompletedTask;
		}

		private void ExecuteNodeExitActions(NarrativeSaveData saveData, StoryNodeConfig story)
		{
			if (story == null) return;
			m_actionService.Execute(story.OnExitActionIds,
				saveData.BuildTurnKey(story.Id) + "|node-exit", saveData);
		}

		private void EnterDayEnd()
		{
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryDayEnd);
			SoundConfigMgr.Instance.PlayBgm(SfxNames.BgmDayEnd, fadeIn: 1.5f);
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (saveData.DayPhase == NarrativeDayPhase.Active && string.IsNullOrWhiteSpace(saveData.CurrentSessionId))
				saveData.BeginDayMonologue();
			ShowDayMonologue();
		}

		private void ShowDayMonologue()
		{
			SetMonologuePresentation(true);
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			NarrativeSessionConfig session = NarrativeConfigMgr.Instance.GetSession(saveData.LastCompletedSessionId);
			string body = session != null && !string.IsNullOrWhiteSpace(session.DayMonologue)
				? session.DayMonologue
				: $"第 {saveData.CurrentDay} 天结束。\n今天获得 {saveData.MemoryCount} 段记忆，学会 {saveData.OwnedEmotionIds.Count} 种情绪。\n回想过后，新的日子将继续。";
			string title = $"DAY {saveData.CurrentDay}";
			ShowReflectionOverlay(title, body, ContinueAfterDayMonologue);
		}

		private void SetMonologuePresentation(bool visible)
		{
			if (m_monologuePresentation == visible) return;
			m_monologuePresentation = visible;
			bool showHud = !visible;
			if (m_btnSettings != null) m_btnSettings.gameObject.SetActive(showHud);
			if (m_btnEmoCollection != null) m_btnEmoCollection.gameObject.SetActive(showHud);
			if (m_btnItemCollection != null) m_btnItemCollection.gameObject.SetActive(showHud);
			if (m_btnRestart != null) m_btnRestart.gameObject.SetActive(showHud);
			if (m_btnLayoutEdit != null) m_btnLayoutEdit.gameObject.SetActive(showHud);
			if (m_scrollMemoryList != null) m_scrollMemoryList.gameObject.SetActive(showHud);
			if (m_dialoguePanel != null) m_dialoguePanel.SetActive(showHud);
			if (m_rtNpcSubtitle != null) m_rtNpcSubtitle.gameObject.SetActive(false);
			if (m_tfReplayMessageNode != null) m_tfReplayMessageNode.gameObject.SetActive(false);
			if (m_tfReplayEmoNode != null) m_tfReplayEmoNode.gameObject.SetActive(false);
			SetDialogueAdvanceCatcherActive(false);
			if (visible)
			{
				CancelSpriteRequest(ref m_sceneCharacterSpriteRequest);
				CancelSpriteRequest(ref m_otherCharacterSpriteRequest);
				m_requestedSceneCharacterLocation = null;
				m_requestedOtherCharacterLocation = null;
				m_npcPortraitVersion++;
				HideNpcSpine();
				if (m_rtSceneCharacter != null) m_rtSceneCharacter.gameObject.SetActive(false);
				if (m_rtOtherCharacter != null) m_rtOtherCharacter.gameObject.SetActive(false);
				if (m_imgNpcPortrait != null) m_imgNpcPortrait.enabled = false;
				if (m_tmpNpcPlaceholder != null) m_tmpNpcPlaceholder.gameObject.SetActive(false);
			}
		}

		private void ContinueAfterDayMonologue()
		{
			if (m_isBusy)
			{
				return;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			DestroyOverlay();
			SetMonologuePresentation(false);
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
			if (saveData.CanFinishMemoryCleanup)
			{
				int nextDay = m_storyScheduler.FindNextScheduledDay(saveData);
				saveData.FinishMemoryCleanupAndAdvanceDay(nextDay);
				if (m_isBusy) m_pendingRestart = true;
				else AdvanceFromDayPhase(saveData);
				return;
			}

			DestroyOverlay();
			HideMemoryTooltip();
			SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryOverflow);
			SetMonologuePresentation(false);
			SetDialogueAdvanceCatcherActive(false);
			if (m_dialoguePanel != null) m_dialoguePanel.SetActive(false);
			if (m_scrollMemoryList != null) m_scrollMemoryList.gameObject.SetActive(true);
			if (m_rtNpcSubtitle != null && m_tmpNpcSubtitle != null)
			{
				SetNpcSpeaker(string.Empty);
				m_tmpNpcSubtitle.text = $"记忆已满，请点击记忆卡片右上角的删除按钮，保留 {NarrativeRules.MemoryCapacity} 段记忆。";
				m_tmpNpcSubtitle.maxVisibleCharacters = int.MaxValue;
				m_tmpNpcSubtitle.ForceMeshUpdate();
				LayoutNpcSubtitle(m_tmpNpcSubtitle.preferredHeight);
				m_rtNpcSubtitle.gameObject.SetActive(true);
				SetContinueHintVisible(false, true);
			}
			RefreshMemorySlots();
		}

		private void ShowGameComplete()
		{
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryEnding);
			SoundConfigMgr.Instance.PlayBgm(SfxNames.BgmEnding, fadeIn: 2f);
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
			GameModule.UIModule.ShowWindowAsync<CollectionWindow>();
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
					buttons.Add((label, (Action)(() => InspectNarrativeItemPart(memoryId,
						EventSystem.current?.currentSelectedGameObject?.transform as RectTransform))));
				}
			}

			buttons.Add(("关闭", (Action)(() => CloseOverlayAnimated())));
			string inspectHint = visualParts.Count > 0
				? "\n\n点击物品上的不同部位，发现与它有关的记忆。发现过的记忆不会重复获得。"
				: "\n\n选择一个部件进行检查。发现过的记忆不会重复获得。";
			ShowOverlay(item.ItemName, item.Description + inspectHint, buttons, true);
			RectTransform panel = m_overlay == null ? null : m_overlay.transform.Find("Panel") as RectTransform;
			if (panel != null)
			{
				panel.DOKill();
				panel.sizeDelta = new Vector2(900f, 780f);
				panel.anchoredPosition = new Vector2(150f, -780f);
				SoundConfigMgr.Instance.PlaySfx(SfxNames.ItemPanelIn);
				panel.DOAnchorPos(new Vector2(150f, 0f), 0.25f).SetUpdate(true).SetLink(panel.gameObject);
			}
			ShowNarrativeItemImage(item.ImageId, visualParts);
		}

		private void InspectNarrativeItemPart(string memoryId, RectTransform source = null)
		{
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (string.IsNullOrWhiteSpace(memoryId))
			{
				return;
			}

			if (!saveData.AddMemories(new[] { memoryId })) return;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.ItemInspect);
			m_narrativeService.RunEmotionScanForDebug(saveData);
			RefreshUI();
			PlayItemMemoryFlyIn(memoryId, source);
		}

		private void PlayItemMemoryFlyIn(string memoryId, RectTransform source)
		{
			if (GetMemorySlotForId(memoryId) == null) return;
			Canvas.ForceUpdateCanvases();
			if (source == null && m_overlay != null) source = m_overlay.transform.Find("Panel") as RectTransform;
			if (source == null) return;

			var fly = new GameObject("ItemMemoryFlyIn", typeof(RectTransform), typeof(Image));
			fly.transform.SetParent(transform, false);
			fly.transform.SetAsLastSibling();
			RectTransform flyRect = fly.GetComponent<RectTransform>();
			flyRect.anchorMin = flyRect.anchorMax = new Vector2(0.5f, 0.5f);
			flyRect.sizeDelta = new Vector2(66f, 66f);
			Vector3 startPosition = source.TransformPoint(source.rect.center);
			flyRect.position = startPosition;
			Image flyImage = fly.GetComponent<Image>();
			flyImage.color = HudPaperButton;
			flyImage.raycastTarget = false;
			MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(memoryId);
			TextMeshProUGUI label = CreateOverlayText(fly.transform,
				memory == null ? memoryId : memory.MemoryName, 15f, Vector2.zero, flyRect.sizeDelta,
				TextAlignmentOptions.Center);
			label.raycastTarget = false;
			label.color = HudPaperText;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryGet);
			DOTween.Sequence().Append(DOVirtual.Float(0f, 1f, 0.5f, progress =>
			{
				RectTransform target = GetMemorySlotForId(memoryId);
				if (flyRect != null && target != null)
					flyRect.position = Vector3.Lerp(startPosition, target.TransformPoint(target.rect.center), progress);
			}).SetEase(Ease.InOutCubic))
				.Join(flyRect.DOScale(0.7f, 0.5f)).SetUpdate(true).SetLink(fly)
				.OnComplete(() =>
				{
					SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryLand);
					UnityEngine.Object.Destroy(fly);
				});
		}

		private RectTransform GetMemorySlotForId(string memoryId)
		{
			IReadOnlyList<string> ids = NarrativeSaveData.Get.GetMemoryIdsInAcquisitionOrder();
			for (int i = 0; i < ids.Count && i < m_memorySlotImages.Count; i++)
				if (ids[i] == memoryId && m_memorySlotImages[i] != null)
					return m_memorySlotImages[i].rectTransform;
			return null;
		}

		private void ShowNarrativeItemImage(string imageId, IReadOnlyList<NarrativeItemPartConfig> parts)
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
			imageRect.sizeDelta = new Vector2(480f, 300f);
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
					CreateNarrativeItemHotspots(imageRect, displaySize, parts);
				}
			}, gameObject.GetCancellationTokenOnDestroy());

			TextMeshProUGUI bodyText = m_overlayBodyContent.GetComponentInChildren<TextMeshProUGUI>(true);
			if (bodyText == null)
			{
				return;
			}

			bodyText.rectTransform.anchoredPosition = new Vector2(0f, -340f);
			bodyText.rectTransform.offsetMin = new Vector2(24f, bodyText.rectTransform.offsetMin.y);
			bodyText.rectTransform.offsetMax = new Vector2(-24f, bodyText.rectTransform.offsetMax.y);
			Canvas.ForceUpdateCanvases();
			bodyText.ForceMeshUpdate();
			float textHeight = Mathf.Max(bodyText.preferredHeight + 24f, 80f);
			bodyText.rectTransform.sizeDelta = new Vector2(bodyText.rectTransform.sizeDelta.x, textHeight);
			m_overlayBodyContent.sizeDelta = new Vector2(0f, textHeight + 360f);
		}

		private void CreateNarrativeItemHotspots(RectTransform imageRect, Vector2 displaySize,
			IReadOnlyList<NarrativeItemPartConfig> parts)
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
				hotspotImage.color = new Color(1f, 0.78f, 0.32f, 0.12f);
				var highlight = new GameObject("Highlight", typeof(RectTransform), typeof(CanvasGroup));
				highlight.transform.SetParent(rect, false);
				RectTransform highlightRect = highlight.GetComponent<RectTransform>();
				highlightRect.anchorMin = Vector2.zero;
				highlightRect.anchorMax = Vector2.one;
				highlightRect.offsetMin = new Vector2(-4f, -4f);
				highlightRect.offsetMax = new Vector2(4f, 4f);
				CreateItemHotspotBorder(highlightRect, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f));
				CreateItemHotspotBorder(highlightRect, "Bottom", Vector2.zero, new Vector2(1f, 0f));
				CreateItemHotspotBorder(highlightRect, "Left", Vector2.zero, new Vector2(0f, 1f));
				CreateItemHotspotBorder(highlightRect, "Right", new Vector2(1f, 0f), Vector2.one);
				CanvasGroup glow = highlight.GetComponent<CanvasGroup>();
				glow.blocksRaycasts = false;
				glow.DOFade(0.45f, 0.75f).SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(highlight);
				Button button = go.GetComponent<Button>();
				button.transition = Selectable.Transition.ColorTint;
				ColorBlock colors = button.colors;
				colors.normalColor = new Color(1f, 1f, 1f, 0f);
				colors.highlightedColor = new Color(1f, 0.82f, 0.48f, 0.16f);
				colors.pressedColor = new Color(1f, 0.72f, 0.28f, 0.24f);
				colors.selectedColor = new Color(1f, 0.82f, 0.48f, 0.16f);
				button.colors = colors;
				string memoryId = part.MemoryId;
				button.onClick.AddListener(() => InspectNarrativeItemPart(memoryId, rect));
			}
		}

		private static void CreateItemHotspotBorder(RectTransform parent, string name,
			Vector2 anchorMin, Vector2 anchorMax)
		{
			var edge = new GameObject(name, typeof(RectTransform), typeof(Image));
			edge.transform.SetParent(parent, false);
			RectTransform rect = edge.GetComponent<RectTransform>();
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = new Vector2(anchorMin.x == anchorMax.x ? 4f : 0f,
				anchorMin.y == anchorMax.y ? 4f : 0f);
			Image image = edge.GetComponent<Image>();
			image.color = new Color(1f, 0.78f, 0.32f, 0.9f);
			image.raycastTarget = false;
		}

		private void ShowSettings()
		{
			m_settingsLayoutEditMode = false;
			if (m_overlay != null && !IsPsdSettingsOverlay())
			{
				m_suspendedOverlay = m_overlay;
				m_suspendedOverlayBodyContent = m_overlayBodyContent;
				m_suspendedDebugNodeInput = m_debugNodeInput;
				m_suspendedOverlay.SetActive(false);
				m_overlay = null;
				m_overlayBodyContent = null;
				m_debugNodeInput = null;
			}
			ShowPsdSettings();
		}

		private void ShowVolumeSettings()
		{
			float volume = Mathf.Clamp01(PlayerPrefs.GetFloat("NarrativeMasterVolume", 1f));
			AudioListener.volume = volume;
			ShowOverlay("音量设置", $"当前音量：{Mathf.RoundToInt(volume * 100f)}%", new[]
			{
				("降低音量", (Action)(() => ChangeVolume(-0.1f))),
				("提高音量", (Action)(() => ChangeVolume(0.1f))),
				("返回设置", (Action)(() => ShowSettings()))
			});
			if (m_overlayBodyContent == null) return;
			var sliderObject = new GameObject("VolumeSlider", typeof(RectTransform), typeof(Image), typeof(Slider));
			sliderObject.transform.SetParent(m_overlayBodyContent, false);
			RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
			sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0.5f, 1f);
			sliderRect.pivot = new Vector2(0.5f, 1f);
			sliderRect.anchoredPosition = new Vector2(0f, -90f);
			sliderRect.sizeDelta = new Vector2(500f, 34f);
			sliderObject.GetComponent<Image>().color = HudPaperButton;
			var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
			fillObject.transform.SetParent(sliderObject.transform, false);
			RectTransform fillRect = fillObject.GetComponent<RectTransform>();
			fillRect.anchorMin = Vector2.zero;
			fillRect.anchorMax = Vector2.one;
			fillRect.offsetMin = Vector2.zero;
			fillRect.offsetMax = Vector2.zero;
			fillObject.GetComponent<Image>().color = new Color(0.72f, 0.8f, 0.68f, 1f);
			var handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
			handleObject.transform.SetParent(sliderObject.transform, false);
			RectTransform handleRect = handleObject.GetComponent<RectTransform>();
			handleRect.sizeDelta = new Vector2(46f, 46f);
			Image handleImage = handleObject.GetComponent<Image>();
			handleImage.color = Color.white;
			Slider slider = sliderObject.GetComponent<Slider>();
			slider.fillRect = fillRect;
			slider.handleRect = handleRect;
			slider.targetGraphic = handleImage;
			slider.minValue = 0f;
			slider.maxValue = 1f;
			slider.value = volume;
			m_overlayBodyContent.sizeDelta = new Vector2(0f, 150f);
			TextMeshProUGUI bodyText = m_overlayBodyContent.GetComponentInChildren<TextMeshProUGUI>(true);
			slider.onValueChanged.AddListener(value =>
			{
				AudioListener.volume = value;
				PlayerPrefs.SetFloat("NarrativeMasterVolume", value);
				PlayerPrefs.Save();
				if (bodyText != null) bodyText.text = $"当前音量：{Mathf.RoundToInt(value * 100f)}%";
			});
		}

		private void ChangeVolume(float delta)
		{
			float volume = Mathf.Clamp01(PlayerPrefs.GetFloat("NarrativeMasterVolume", 1f) + delta);
			PlayerPrefs.SetFloat("NarrativeMasterVolume", volume);
			PlayerPrefs.Save();
			AudioListener.volume = volume;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiSliderTick, DGame.AudioType.UISound);
			ShowVolumeSettings();
		}

		private void LoadSaveSlot(int slot)
		{
			if (m_isBusy)
			{
				ShowSettingsConfirmation("提示", "请等待当前对话结束后再读取存档。", null);
				return;
			}
			if (!NarrativeSaveData.Get.LoadFromSlot(slot))
			{
				ShowSettingsConfirmation("提示", "存档不存在或内容无法读取。", null);
				return;
			}
			m_dialoguePresentVersion++;
			m_currentStoryNode = null;
			m_selectedReply = null;
			m_selectedEmotionId = null;
			m_hasReplied = false;
			m_playerReplyShown = false;
			SetReplyItemsActive(false);
			SetEmotionItemsActive(false);
			m_messages.Clear();
			if (m_messageList != null && m_messageList.LoopRectView != null)
				m_messageList.LoopRectView.SetListItemCount(0, false);
			DiscardSuspendedOverlay();
			DestroyOverlay();
			SetMonologuePresentation(false);
			StartStoryAsync().Forget();
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
				("关闭", (Action)(() => CloseOverlayAnimated()))
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

		private void ShowOverlay(string title, string body, IReadOnlyList<(string Label, Action Action)> buttons,
			bool keepSceneVisible = false, bool playSound = true)
		{
			HideMemoryTooltip();
			DiscardSuspendedOverlay();
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
			blockerImage.color = keepSceneVisible ? Color.clear : new Color(0.12f, 0.1f, 0.08f, 0.45f);
			blockerImage.raycastTarget = true;
			m_overlay = blocker;
			if (!keepSceneVisible)
				ApplyHudSprite(blockerImage, "setting_mask", false, gameObject.GetCancellationTokenOnDestroy());

			var root = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			root.transform.SetParent(blocker.transform, false);
			RectTransform panel = root.GetComponent<RectTransform>();
			panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
			panel.pivot = new Vector2(0.5f, 0.5f);
			panel.sizeDelta = new Vector2(780, 720);
			panel.anchoredPosition = new Vector2(0f, -720f);
			if (playSound) SoundConfigMgr.Instance.PlaySfx(SfxNames.UiPanelOpen, DGame.AudioType.UISound);
			panel.DOAnchorPos(Vector2.zero, 0.25f).SetUpdate(true).SetLink(root);
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

		/// <summary>
		/// 日终 AI 反思：铺满整屏的底图，文字排进底图里的纸面区域，右下角只留「回车 + 继续」。
		/// 与通用 ShowOverlay 分开实现，避免影响图鉴、物品、设置等共用弹窗。
		/// </summary>
		private void ShowReflectionOverlay(string title, string body, Action onContinue)
		{
			HideMemoryTooltip();
			DiscardSuspendedOverlay();
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
			blockerImage.color = Color.white;
			blockerImage.raycastTarget = true;
			ApplyHudSprite(blockerImage, ReflectionBackgroundLocation, false,
				gameObject.GetCancellationTokenOnDestroy());
			m_overlay = blocker;
			m_reflectionOverlayRoot = blocker;
			m_reflectionPresentVersion++;

			// 整屏都可推进：点背景、点纸面、点「继续」走同一个入口。
			Button advance = blocker.AddComponent<Button>();
			advance.transition = Selectable.Transition.None;
			advance.targetGraphic = blockerImage;
			advance.onClick.AddListener(OnReflectionAdvanceInput);

			// 纸面容器：锚定到底图里的纸面范围，节点名保持 Panel 以兼容外层逻辑。
			var paperObject = new GameObject("Panel", typeof(RectTransform));
			paperObject.transform.SetParent(blocker.transform, false);
			RectTransform paper = paperObject.GetComponent<RectTransform>();
			paper.anchorMin = new Vector2(ReflectionPaperArea.xMin, ReflectionPaperArea.yMin);
			paper.anchorMax = new Vector2(ReflectionPaperArea.xMax, ReflectionPaperArea.yMax);
			paper.pivot = new Vector2(0.5f, 0.5f);
			paper.offsetMin = Vector2.zero;
			paper.offsetMax = Vector2.zero;

			TextMeshProUGUI titleText = CreateOverlayText(paper, title, ReflectionTitleFontSize,
				Vector2.zero, new Vector2(0f, ReflectionTitleHeight), TextAlignmentOptions.TopLeft);
			titleText.fontStyle = FontStyles.Bold;
			titleText.overflowMode = TextOverflowModes.Overflow;
			titleText.enableWordWrapping = false;
			RectTransform titleRect = titleText.rectTransform;
			titleRect.anchorMin = new Vector2(0f, 1f);
			titleRect.anchorMax = new Vector2(1f, 1f);
			titleRect.pivot = new Vector2(0.5f, 1f);
			titleRect.offsetMin = new Vector2(ReflectionTextInsetLeft,
				-(ReflectionTitleTop + ReflectionTitleHeight));
			titleRect.offsetMax = new Vector2(-ReflectionTextInsetRight, -ReflectionTitleTop);

			TextMeshProUGUI bodyText = CreateOverlayText(paper, body, ReflectionBodyFontSize,
				Vector2.zero, Vector2.zero, TextAlignmentOptions.TopLeft);
			bodyText.overflowMode = TextOverflowModes.Overflow;
			bodyText.enableWordWrapping = true;
			bodyText.lineSpacing = ReflectionBodyLineSpacing;
			RectTransform bodyRect = bodyText.rectTransform;
			bodyRect.anchorMin = Vector2.zero;
			bodyRect.anchorMax = Vector2.one;
			bodyRect.pivot = new Vector2(0.5f, 0.5f);
			bodyRect.offsetMin = new Vector2(ReflectionTextInsetLeft, ReflectionBodyBottom);
			bodyRect.offsetMax = new Vector2(-ReflectionTextInsetRight, -ReflectionBodyTop);
			bodyText.maxVisibleCharacters = 0;

			m_reflectionBody = bodyText;
			m_reflectionContinueGroup = CreateReflectionContinue(paper);
			m_reflectionContinueGroup.alpha = 0f;

			// 黑屏盖在最上层，随后渐隐露出底图；开场期间点一下可整块跳过。
			CreateReflectionCover(blocker.transform);

			m_reflectionOverlayActive = true;
			m_reflectionTyping = true;
			m_reflectionAdvanceLocked = false;
			m_reflectionContinue = onContinue;
			PlayReflectionIntroAsync(bodyText, body, m_reflectionPresentVersion).Forget();
		}

		private void CreateReflectionCover(Transform parent)
		{
			var go = new GameObject("BlackCover", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			go.transform.SetAsLastSibling();
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			Image image = go.GetComponent<Image>();
			image.color = Color.black;
			image.raycastTarget = false;
			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 1f;
			group.blocksRaycasts = false;
			m_reflectionCoverGroup = group;
		}

		private void ClearReflectionCover()
		{
			if (m_reflectionCoverGroup == null) return;
			GameObject cover = m_reflectionCoverGroup.gameObject;
			m_reflectionCoverGroup.DOKill();
			m_reflectionCoverGroup = null;
			if (cover != null) UnityEngine.Object.Destroy(cover);
		}

		private async UniTask PlayReflectionIntroAsync(TextMeshProUGUI body, string text, int version)
		{
			// 1) 黑屏渐隐（这期间点击 = 直接跳过，文字与底图一起出现）
			float elapsed = 0f;
			while (elapsed < ReflectionFadeInSeconds)
			{
				if (IsDestroyed || version != m_reflectionPresentVersion) return;
				if (!m_reflectionTyping) break;
				elapsed += Time.unscaledDeltaTime;
				if (m_reflectionCoverGroup != null)
				{
					m_reflectionCoverGroup.alpha = 1f - Mathf.Clamp01(elapsed / ReflectionFadeInSeconds);
				}
				await UniTask.Yield(PlayerLoopTiming.Update);
			}

			if (IsDestroyed || version != m_reflectionPresentVersion) return;
			ClearReflectionCover();
			if (!m_reflectionTyping) return;

			// 2) 逐字打出正文
			await RunReflectionTypewriterAsync(body, text, version);
		}

		private async UniTask RunReflectionTypewriterAsync(TextMeshProUGUI body, string text, int version)
		{
			if (body == null || string.IsNullOrEmpty(text))
			{
				m_reflectionTyping = false;
				ShowReflectionContinueHint();
				return;
			}

			Canvas.ForceUpdateCanvases();
			body.ForceMeshUpdate();
			int total = body.textInfo.characterCount;
			int burstLeft = NextReflectionBurstLength();
			for (int i = 1; i <= total; i++)
			{
				if (IsDestroyed || version != m_reflectionPresentVersion) return;
				if (!m_reflectionTyping) break;

				body.maxVisibleCharacters = i;
				PlayTypewriterTick(i, text, ReflectionTypewriterSoundEvery);

				// LLM 流式出来的字不是一个恒速：每字有小抖动，每几个字卡一下，句读处停得更久。
				int delayMs = ReflectionTypewriterMsPerChar +
					UnityEngine.Random.Range(-ReflectionTypewriterJitterMs, ReflectionTypewriterJitterMs + 1);
				if (i - 1 < text.Length && IsTypewriterPunctuation(text[i - 1]))
				{
					delayMs += ReflectionTypewriterPunctuationPauseMs;
					burstLeft = NextReflectionBurstLength();
				}
				else if (--burstLeft <= 0)
				{
					delayMs += UnityEngine.Random.Range(ReflectionTypewriterPauseMinMs,
						ReflectionTypewriterPauseMaxMs + 1);
					burstLeft = NextReflectionBurstLength();
				}

				delayMs = Mathf.Max(ReflectionTypewriterMinStepMs, delayMs);
				float remain = delayMs / 1000f;
				while (remain > 0f)
				{
					if (IsDestroyed || version != m_reflectionPresentVersion) return;
					if (!m_reflectionTyping) break;
					await UniTask.Yield(PlayerLoopTiming.Update);
					remain -= Time.unscaledDeltaTime;
				}
			}

			if (IsDestroyed || version != m_reflectionPresentVersion) return;
			body.maxVisibleCharacters = total;
			m_reflectionTyping = false;
			ShowReflectionContinueHint();
		}

		private static int NextReflectionBurstLength()
			=> UnityEngine.Random.Range(ReflectionTypewriterBurstMin, ReflectionTypewriterBurstMax + 1);

		/// <summary>跳过打字机：立刻出全文（若还在黑屏渐隐，也一并撤掉黑屏）。</summary>
		private void SkipReflectionTyping()		{
			m_reflectionTyping = false;
			ClearReflectionCover();
			if (m_reflectionBody != null)
			{
				m_reflectionBody.maxVisibleCharacters = int.MaxValue;
			}

			ShowReflectionContinueHint();
		}

		private void ShowReflectionContinueHint()
		{
			if (m_reflectionContinueGroup == null) return;
			if (m_reflectionContinueGroup.alpha >= 1f) return;
			m_reflectionContinueGroup.DOKill();
			m_reflectionContinueGroup.DOFade(1f, ReflectionContinueFadeSeconds).SetUpdate(true)
				.SetLink(m_reflectionContinueGroup.gameObject);
		}

		/// <summary>反思页唯一的推进入口：打字中 = 出全文，出完 = 继续流程。</summary>
		private void OnReflectionAdvanceInput()
		{
			if (!m_reflectionOverlayActive) return;
			if (m_overlay == null || m_overlay != m_reflectionOverlayRoot) return;
			// 同一次按键可能同时走 EventSystem 的 Submit 和 OnUpdate 的键检测，按帧去重。
			if (m_reflectionInputFrame == Time.frameCount) return;
			m_reflectionInputFrame = Time.frameCount;
			if (m_reflectionTyping)
			{
				SkipReflectionTyping();
				return;
			}

			if (m_reflectionAdvanceLocked) return;
			m_reflectionAdvanceLocked = true;
			m_reflectionOverlayActive = false;
			Action callback = m_reflectionContinue;
			callback?.Invoke();
			if (m_overlay == m_reflectionOverlayRoot)
			{
				// 回调没把这一屏关掉（例如当前正忙），恢复可交互，别把玩家卡住。
				m_reflectionContinue = callback;
				m_reflectionOverlayActive = true;
				m_reflectionAdvanceLocked = false;
				return;
			}

			m_reflectionContinue = null;
		}

		private static bool WasReflectionAdvanceKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			if (Keyboard.current == null) return false;
			return Keyboard.current.enterKey.wasPressedThisFrame ||
				Keyboard.current.numpadEnterKey.wasPressedThisFrame ||
				Keyboard.current.spaceKey.wasPressedThisFrame;
#else
			return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
				Input.GetKeyDown(KeyCode.Space);
#endif
		}

		private CanvasGroup CreateReflectionContinue(Transform parent)
		{
			var go = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button),
				typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
			rect.pivot = new Vector2(1f, 0f);
			rect.anchoredPosition = new Vector2(-ReflectionContinueRight, ReflectionContinueBottom);
			rect.sizeDelta = new Vector2(124f, 64f);
			Image hitArea = go.GetComponent<Image>();
			hitArea.color = Color.clear;
			hitArea.raycastTarget = true;
			Button button = go.GetComponent<Button>();
			button.transition = Selectable.Transition.None;
			button.targetGraphic = hitArea;
			button.onClick.AddListener(OnReflectionAdvanceInput);

			CreateReflectionEnterIcon(go.transform, 30f);

			TextMeshProUGUI label = CreateOverlayText(go.transform, "继续", 32f, Vector2.zero,
				Vector2.zero, TextAlignmentOptions.MidlineLeft);
			label.overflowMode = TextOverflowModes.Overflow;
			label.enableWordWrapping = false;
			RectTransform labelRect = label.rectTransform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.pivot = new Vector2(0.5f, 0.5f);
			labelRect.offsetMin = new Vector2(44f, 0f);
			labelRect.offsetMax = Vector2.zero;
			return go.GetComponent<CanvasGroup>();
		}

		// 回车（↵）标识：横笔 + 右侧竖笔 + 左侧箭头，全部用空白 Image 拼，不依赖字体字形。
		private static void CreateReflectionEnterIcon(Transform parent, float size)
		{
			var root = new GameObject("EnterIcon", typeof(RectTransform));
			root.transform.SetParent(parent, false);
			RectTransform rect = root.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
			rect.pivot = new Vector2(0f, 0.5f);
			rect.anchoredPosition = new Vector2(0f, 0f);
			rect.sizeDelta = new Vector2(size, size);

			float scale = size / 30f;
			float stroke = Mathf.Max(2f, 2.6f * scale);
			float barY = 9f * scale;
			CreateReflectionIconBar(root.transform, new Vector2(2f * scale, barY - stroke * 0.5f),
				new Vector2(24f * scale, stroke), 0f);
			CreateReflectionIconBar(root.transform, new Vector2(23f * scale, barY - stroke * 0.5f),
				new Vector2(stroke, 18f * scale), 0f);
			CreateReflectionIconBar(root.transform, new Vector2(2f * scale, barY),
				new Vector2(11f * scale, stroke), 45f);
			CreateReflectionIconBar(root.transform, new Vector2(2f * scale, barY),
				new Vector2(11f * scale, stroke), -45f);
		}

		private static void CreateReflectionIconBar(Transform parent, Vector2 position, Vector2 size,
			float rotation)
		{
			var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = Vector2.zero;
			rect.pivot = Vector2.zero;
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
			Image image = go.GetComponent<Image>();
			image.color = ReflectionIconColor;
			image.raycastTarget = false;
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
			Image buttonImage = go.GetComponent<Image>();
			buttonImage.color = HudPaperButton;
			Button button = go.GetComponent<Button>();
			ColorBlock colors = button.colors;
			colors.normalColor = Color.white;
			colors.highlightedColor = new Color(1f, 0.95f, 0.75f, 1f);
			colors.pressedColor = new Color(0.91f, 0.78f, 0.55f, 1f);
			colors.selectedColor = colors.highlightedColor;
			button.colors = colors;
			button.onClick.AddListener(() =>
			{
				if (!button.interactable) return;
				button.interactable = false;
				DOTween.Sequence().Append(rect.DOScale(0.94f, 0.08f))
					.Append(rect.DOScale(1f, 0.12f)).SetUpdate(true).SetLink(go)
					.OnComplete(() => action?.Invoke());
			});
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

			// 停掉还在跑的反思开场：打字循环靠版本号自取消。
			m_reflectionPresentVersion++;
			m_reflectionOverlayActive = false;
			m_reflectionTyping = false;
			m_reflectionAdvanceLocked = false;
			m_reflectionContinue = null;
			m_reflectionBody = null;
			m_reflectionContinueGroup = null;
			m_reflectionCoverGroup = null;
			m_reflectionOverlayRoot = null;
		}

		private bool IsPsdSettingsOverlay()
			=> m_overlay != null && m_overlay.transform.Find("VolumeSlider") != null;

		private void CloseSettingsOverlay()
		{
			m_settingsLayoutEditMode = false;
			DestroyOverlay();
			if (m_suspendedOverlay == null)
			{
				if (m_monologuePresentation && NarrativeSaveData.Get.DayPhase == NarrativeDayPhase.Monologue)
					ShowDayMonologue();
				return;
			}
			m_overlay = m_suspendedOverlay;
			m_overlayBodyContent = m_suspendedOverlayBodyContent;
			m_debugNodeInput = m_suspendedDebugNodeInput;
			m_suspendedOverlay = null;
			m_suspendedOverlayBodyContent = null;
			m_suspendedDebugNodeInput = null;
			m_overlay.SetActive(true);
			m_overlay.transform.SetAsLastSibling();
		}

		private void DiscardSuspendedOverlay()
		{
			if (m_suspendedOverlay != null)
			{
				m_suspendedOverlay.SetActive(false);
				UnityEngine.Object.Destroy(m_suspendedOverlay);
			}
			m_suspendedOverlay = null;
			m_suspendedOverlayBodyContent = null;
			m_suspendedDebugNodeInput = null;
		}

		private void CloseOverlayAnimated()
		{
			RectTransform panel = m_overlay == null ? null : m_overlay.transform.Find("Panel") as RectTransform;
			if (panel == null)
			{
				DestroyOverlay();
				return;
			}
			panel.DOKill();
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiPanelClose, DGame.AudioType.UISound);
			panel.DOAnchorPos(new Vector2(panel.anchoredPosition.x, -panel.rect.height), 0.18f)
				.SetUpdate(true).SetLink(panel.gameObject).OnComplete(DestroyOverlay);
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
			RefreshOtherCharacter(story);
			RefreshNpcPortraitAsync(story).Forget();
		}

		private List<string> GetSessionNpcSet()
		{
			NarrativeSessionConfig session = NarrativeConfigMgr.Instance.GetSession(
				NarrativeSaveData.Get.CurrentSessionId);
			if (session == null || string.IsNullOrWhiteSpace(session.NpcSet))
				return new List<string>();
			return session.NpcSet.Split(new[] { ',', '，', ';', '；' },
				StringSplitOptions.RemoveEmptyEntries)
				.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct().ToList();
		}

		private Vector2 GetActiveCharacterDefaultPosition()
		{
			if (!IsNpcDialogueTurn(m_currentStoryNode)) return new Vector2(-850f, 0f);
			List<string> npcSet = GetSessionNpcSet();
			return new Vector2(npcSet.IndexOf(m_currentStoryNode.NpcId) == 1 ? 850f : -850f, 0f);
		}

		private void RefreshOtherCharacter(StoryNodeConfig story)
		{
			List<string> npcSet = GetSessionNpcSet();
			if (story == null || !IsNpcDialogueTurn(story) || npcSet.Count < 2 ||
				!npcSet.Contains(story.NpcId))
			{
				CancelSpriteRequest(ref m_otherCharacterSpriteRequest);
				m_requestedOtherCharacterLocation = null;
				m_otherCharacterVersion++;
				m_otherCharacterNpcId = null;
				if (m_rtOtherCharacter != null) m_rtOtherCharacter.gameObject.SetActive(false);
				return;
			}
			string otherNpc = npcSet.FirstOrDefault(id => id != story.NpcId);
			if (string.IsNullOrWhiteSpace(otherNpc)) return;
			m_otherCharacterNpcId = otherNpc;

			EnsureSceneCharacterView();
			if (m_rtSceneCharacter == null || m_imgSceneRoot == null) return;
			if (m_rtOtherCharacter == null)
			{
				var go = new GameObject("OtherSceneCharacter", typeof(RectTransform), typeof(Image));
				go.transform.SetParent(m_imgSceneRoot.transform, false);
				m_rtOtherCharacter = go.GetComponent<RectTransform>();
				m_imgOtherCharacter = go.GetComponent<Image>();
				m_imgOtherCharacter.preserveAspect = true;
				m_imgOtherCharacter.raycastTarget = false;
			}
			m_rtOtherCharacter.anchorMin = m_rtOtherCharacter.anchorMax = new Vector2(0.5f, 0f);
			m_rtOtherCharacter.pivot = new Vector2(0.5f, 0f);
			m_rtOtherCharacter.anchoredPosition = ReadLayoutPosition(GetOtherCharacterLayoutPart(),
				new Vector2(npcSet.IndexOf(otherNpc) == 0 ? -850f : 850f, 0f));
			m_rtOtherCharacter.sizeDelta = new Vector2(600f, 960f);
			m_rtOtherCharacter.localScale = ReadCharacterVisualScale(GetOtherCharacterLayoutPart());
			m_imgOtherCharacter.raycastTarget = m_layoutEditMode;
			RegisterLayoutDragHandle(m_rtOtherCharacter, "OtherCharacter");
			string location = ResolveNpcPortraitLocation(otherNpc, null);
			if (string.IsNullOrWhiteSpace(location))
			{
				CancelSpriteRequest(ref m_otherCharacterSpriteRequest);
				m_requestedOtherCharacterLocation = null;
				m_rtOtherCharacter.gameObject.SetActive(false);
				return;
			}
			m_rtOtherCharacter.gameObject.SetActive(true);
			if (location == m_loadedOtherCharacterLocation && m_imgOtherCharacter.sprite != null)
			{
				CancelSpriteRequest(ref m_otherCharacterSpriteRequest);
				m_requestedOtherCharacterLocation = null;
				return;
			}
			if (location == m_requestedOtherCharacterLocation &&
				Time.unscaledTime - m_otherCharacterRequestTime < 5f) return;
			CancelSpriteRequest(ref m_otherCharacterSpriteRequest);
			m_requestedOtherCharacterLocation = location;
			m_otherCharacterRequestTime = Time.unscaledTime;
			m_otherCharacterSpriteRequest = CancellationTokenSource.CreateLinkedTokenSource(
				gameObject.GetCancellationTokenOnDestroy());
			int version = ++m_otherCharacterVersion;
			m_imgOtherCharacter.SetSprite(location, false, loaded =>
			{
				if (IsDestroyed || version != m_otherCharacterVersion || loaded == null || loaded.sprite == null) return;
				m_loadedOtherCharacterLocation = location;
				m_requestedOtherCharacterLocation = null;
				loaded.enabled = true;
				loaded.preserveAspect = true;
				loaded.raycastTarget = false;
			}, m_otherCharacterSpriteRequest.Token);
		}

		private async UniTaskVoid RefreshNpcPortraitAsync(StoryNodeConfig story)
		{
			if (story == null)
			{
				return;
			}

			EnsureSceneCharacterView();

			m_npcPortraitVersion++;
#if SPINE_UNITY && SPINE_CSHARP
			int version = m_npcPortraitVersion;
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
			ShowNpcPortraitSprite(story);
		}

		private void ShowNpcPortraitSprite(StoryNodeConfig story)
		{
			string location = ResolveNpcPortraitLocation(story.NpcId, story.AnimationAsset);
			if (string.IsNullOrEmpty(location))
			{
				CancelSpriteRequest(ref m_sceneCharacterSpriteRequest);
				m_requestedSceneCharacterLocation = null;
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
				CancelSpriteRequest(ref m_sceneCharacterSpriteRequest);
				m_requestedSceneCharacterLocation = null;
				FitSceneCharacterSprite(m_imgSceneCharacter);
				return;
			}
			if (location == m_requestedSceneCharacterLocation &&
				Time.unscaledTime - m_sceneCharacterRequestTime < 5f) return;

			SoundConfigMgr.Instance.PlaySfx(SfxNames.PortraitSwitch);
			HideNpcSpine();
			CancelSpriteRequest(ref m_sceneCharacterSpriteRequest);
			m_requestedSceneCharacterLocation = location;
			m_sceneCharacterRequestTime = Time.unscaledTime;
			m_sceneCharacterSpriteRequest = CancellationTokenSource.CreateLinkedTokenSource(
				gameObject.GetCancellationTokenOnDestroy());
			m_imgSceneCharacter.SetSprite(location, false, image =>
			{
				if (m_requestedSceneCharacterLocation != location || image == null || image.sprite == null ||
					IsDestroyed || m_monologuePresentation)
				{
					return;
				}

				m_loadedSceneCharacterLocation = location;
				m_loadedNpcPortraitLocation = location;
				m_requestedSceneCharacterLocation = null;
				FitSceneCharacterSprite(image);
				HideLegacyNpcPortrait();
			}, m_sceneCharacterSpriteRequest.Token);
		}

		private static void CancelSpriteRequest(ref CancellationTokenSource request)
		{
			if (request == null) return;
			request.Cancel();
			request.Dispose();
			request = null;
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
			m_rtSceneCharacter.anchoredPosition = ReadLayoutPosition(GetCharacterLayoutPart(),
				GetActiveCharacterDefaultPosition());
			m_rtSceneCharacter.sizeDelta = new Vector2(600f, 960f);
			m_rtSceneCharacter.localScale = ReadCharacterVisualScale(GetCharacterLayoutPart());
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

			string npcId = NarrativeSaveData.Get.CurrentSessionId == "S01" ? "陈先生" :
				story == null ? string.Empty : story.NpcId;
			// 优先按 Session.backgroundId 取场景；未配置或映射不到时回退到旧的 npcId 规则。
			string backgroundId = ResolveCurrentSessionBackgroundId();
			string[] backgrounds;
			string[] foregrounds;
			if (string.IsNullOrWhiteSpace(backgroundId) ||
				!TryResolveBackgroundLocations(backgroundId, out backgrounds, out foregrounds))
			{
				if (!string.IsNullOrWhiteSpace(backgroundId))
				{
					DLogger.Warning("Unknown session backgroundId: {0}; falling back to npcId layout.", backgroundId);
				}

				backgrounds = ResolveSceneBackgroundLocations(npcId);
				foregrounds = ResolveSceneForegroundLocations(npcId);
			}
			string[] layers = new string[backgrounds.Length + foregrounds.Length];
			Array.Copy(backgrounds, 0, layers, 0, backgrounds.Length);
			Array.Copy(foregrounds, 0, layers, backgrounds.Length, foregrounds.Length);
			string key = string.Join("|", layers);
			EnsureSceneLayerImages(layers.Length);
			EnsureSceneCharacterView();
			if (key == m_loadedSceneKey)
			{
				PlaceSceneCharacterBetweenLayers(backgrounds.Length, layers.Length);
				return;
			}

			m_loadedSceneKey = key;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.SceneSwitch);
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
			int activeAt = CharacterInsertIndex(backgroundCount, foregroundCount, ReadCharacterLayer());
			bool showOther = m_rtOtherCharacter != null && m_rtOtherCharacter.gameObject.activeSelf;
			int otherAt = showOther
				? CharacterInsertIndex(backgroundCount, foregroundCount, ReadOtherCharacterLayer())
				: -1;
			List<string> npcSet = showOther ? GetSessionNpcSet() : null;
			bool activeFirst = !showOther || npcSet.IndexOf(m_currentStoryNode.NpcId) <=
				npcSet.IndexOf(m_otherCharacterNpcId);
			for (int i = 0; i <= layerCount; i++)
			{
				if (i == activeAt && i == otherAt)
				{
					if (activeFirst)
					{
						m_rtSceneCharacter.SetAsLastSibling();
						m_rtOtherCharacter.SetAsLastSibling();
					}
					else
					{
						m_rtOtherCharacter.SetAsLastSibling();
						m_rtSceneCharacter.SetAsLastSibling();
					}
				}
				else
				{
					if (i == activeAt) m_rtSceneCharacter.SetAsLastSibling();
					if (i == otherAt) m_rtOtherCharacter.SetAsLastSibling();
				}
				if (i < layerCount && m_sceneLayerImages[i] != null)
					m_sceneLayerImages[i].transform.SetAsLastSibling();
			}
		}

		private static int CharacterInsertIndex(int backgroundCount, int foregroundCount, int layer)
		{
			return backgroundCount + (layer == 0 ? 0 :
				layer == 1 ? Mathf.Min(foregroundCount, Mathf.Max(1, foregroundCount / 2)) : foregroundCount);
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

		private static string ResolveCurrentSessionBackgroundId()
		{
			string sessionId = NarrativeSaveData.Get.CurrentSessionId;
			if (string.IsNullOrWhiteSpace(sessionId))
			{
				return string.Empty;
			}

			NarrativeSessionConfig session = NarrativeConfigMgr.Instance.GetSession(sessionId);
			return session == null ? string.Empty : session.BackgroundId;
		}

		/// <summary>
		/// 背景 ID 到图层资源的映射。美术按场景补齐图片后，
		/// 只需在配置表 backgroundId 填对应键，无需再改代码。
		/// </summary>
		private static bool TryResolveBackgroundLocations(string backgroundId,
			out string[] backgrounds, out string[] foregrounds)
		{
			switch (backgroundId)
			{
				case "scene_shop_old":
					backgrounds = new[] { "scene_shop_old_window", "scene_shop_old_bg" };
					foregrounds = new[]
					{
						"scene_shop_old_plant", "scene_shop_old_radio", "scene_shop_old_desk", "scene_shop_old_items"
					};
					return true;
				case "scene_shop_new":
					backgrounds = new[] { "scene_shop_new_outside", "scene_shop_new_bg" };
					foregrounds = new[]
					{
						"scene_shop_new_plant", "scene_shop_new_radio", "scene_shop_new_desk", "scene_shop_new_items"
					};
					return true;
				case "scene_zhou":
					backgrounds = new[] { "scene_zhou_window", "scene_zhou_bg" };
					foregrounds = new[]
					{
						"scene_zhou_plant", "scene_zhou_radio", "scene_zhou_desk", "scene_zhou_items", "scene_zhou_light"
					};
					return true;
				case "scene_chenguo":
					backgrounds = new[] { "scene_chenguo_bg" };
					foregrounds = new[] { "scene_chenguo_chair", "scene_chenguo_desk", "scene_chenguo_cat" };
					return true;
				case "scene_father":
					backgrounds = new[] { "scene_father_bg" };
					foregrounds = new[] { "scene_father_fg" };
					return true;
				default:
					backgrounds = Array.Empty<string>();
					foregrounds = Array.Empty<string>();
					return false;
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

			if (m_btnLayoutEdit != null)
			{
				m_btnLayoutEdit.transform.SetAsLastSibling();
			}

			if (m_scrollMemoryList != null)
			{
				m_scrollMemoryList.transform.SetAsLastSibling();
			}

			if (m_memoryTooltip != null && m_memoryTooltip.gameObject.activeSelf)
			{
				m_memoryTooltip.SetAsLastSibling();
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
			PlaceCornerButton(m_btnRestart, new Vector2(0f, 1f),
				ReadLayoutPosition("Restart", new Vector2(210f, -28f)), new Vector2(200f, 56f));
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
			ClearNpcSubtitle();

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
			// 对局内“返回标题”回到抽屉式开始界面（StartMenuWindow），不再回旧的 MainWindow。
			GameModule.UIModule.CloseWindow<BattleMainUI>();
			GameModule.UIModule.ShowWindowAsync<StartMenuWindow>();
		}

		#region 事件

		private partial void OnClickSettingsBtn()
		{
			if (m_layoutEditMode) return;
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
			bool toggleSettings = false;
			bool toggleLayout = false;
#if ENABLE_INPUT_SYSTEM
			toggleDebugPanel = Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
			toggleSettings = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
			toggleLayout = Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
#else
			toggleDebugPanel = Input.GetKeyDown(KeyCode.F1);
			toggleSettings = Input.GetKeyDown(KeyCode.Escape);
			toggleLayout = Input.GetKeyDown(KeyCode.F2);
#endif
			if (toggleLayout)
			{
				if (IsPsdSettingsOverlay())
					ToggleSettingsLayoutEdit();
				else if (m_overlay == null)
					SetLayoutEditMode(!m_layoutEditMode);
			}
			if (toggleSettings)
			{
				if (IsPsdSettingsOverlay())
				{
					CloseSettingsOverlay();
				}
				else
				{
					if (m_layoutEditMode) SetLayoutEditMode(false);
					ShowSettings();
				}
			}
			if (toggleDebugPanel)
			{
				if (m_overlay == null) ShowDebugPanel();
				else if (IsPsdSettingsOverlay()) CloseSettingsOverlay();
				else DestroyOverlay();
			}
			if (m_reflectionOverlayActive && m_reflectionOverlayRoot != null &&
				m_overlay == m_reflectionOverlayRoot && WasReflectionAdvanceKeyPressed())
			{
				OnReflectionAdvanceInput();
			}
			base.OnUpdate();
		}
	}
}
