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
		/// <summary>名字的装饰副本（阴影、描边）靠名字里的这些片段识别，颜色不跟 npcId 走。</summary>
		private static readonly string[] NpcSpeakerDecorLabelKeywords = { "Shadow", "Outline" };
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
		// 对话框九切内缩量：左右上都是拼贴装饰带的像素宽度（左绿板 96 / 右便签 250 / 顶横条 116），
		// 消息区必须落在这圈之内，否则气泡会压到装饰上。
		private const float MESSAGE_AREA_LEFT_INSET = 96f;
		private const float MESSAGE_AREA_RIGHT_INSET = 250f;
		private const float MESSAGE_AREA_TOP_INSET = 116f;
		// 选项面板隐藏时消息区只留这点底边距，否则 441 的预留会让对话框下半一直空着。
		private const float MESSAGE_AREA_BOTTOM_INSET = 24f;
		private const string NpcPortraitPlaceholderText = "NPC角色素材";
		private static readonly Color HudPaperText = new Color(0.28f, 0.22f, 0.16f, 1f);
		private static readonly Color HudPaperPanel = new Color(0.96f, 0.93f, 0.86f, 1f);
		private static readonly Color HudPaperButton = new Color(0.93f, 0.82f, 0.62f, 1f);

		// 物品栏（背包按钮）：空背包半透明，有物品时换成打开的包，并让 slot 与物品一起做呼吸缩放。
		private const string ItemSlotBagClosedLocation = "dialogue_item_btn";
		private const string ItemSlotBagOpenLocation = "dialogue_item_btn_open";
		/// <summary>空背包的透明度，比有物品时淡一档，暗示「这里还没东西」。</summary>
		private const float ItemSlotEmptyAlpha = 0.45f;
		/// <summary>物品图标尺寸。故意大于 slot，让物品顶出纸面，用来提示「刚拿到一件可交互的东西」。</summary>
		private const float ItemSlotItemSize = 124f;
		/// <summary>物品图标相对 slot 中心的 Y 偏移（正值向上）。对齐到开口处。</summary>
		private const float ItemSlotItemOffsetY = 34f;
		private const float ItemSlotBreathAmplitude = 0.055f;
		private const float ItemSlotBreathPeriod = 2.4f;
		/// <summary>鼠标悬停时的金色描边。</summary>
		private static readonly Color ItemSlotHoverColor = new Color(1f, 0.79f, 0.28f, 1f);
		private const float ItemSlotHoverOutlineWidth = 3f;

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
		/// <summary>BubbleColor 的 alpha 为 0 表示这条消息不染情绪色。</summary>
		private readonly List<(bool IsPlayer, string Text, Color BubbleColor)> m_messages =
			new List<(bool, string, Color)>();
		private readonly List<Image> m_memorySlotImages = new List<Image>();
		private readonly List<Image> m_memorySlotThumbs = new List<Image>();
		private readonly List<Image> m_memorySlotLabelBacks = new List<Image>();
		private readonly List<int> m_memorySlotVersions = new List<int>();
		private readonly List<TextMeshProUGUI> m_memorySlotLabels = new List<TextMeshProUGUI>();
		/// <summary>槽位右上角的删除热区。不是 Button：删除要靠「按住 1 秒」，交给 EventTrigger 收按下/抬起。</summary>
		private readonly List<Image> m_memoryDeleteHitAreas = new List<Image>();
		/// <summary>热区里的红色进度环，跟着长按从 0 填到 1（与 m_memoryDeleteHitAreas 同索引）。</summary>
		private readonly List<Image> m_memoryDeleteProgressRings = new List<Image>();
		private RectTransform m_memoryTooltip;
		private TextMeshProUGUI m_memoryTooltipName;
		private TextMeshProUGUI m_memoryTooltipDescription;
		private RectTransform m_memoryTooltipFrame;
		private Image m_memoryTooltipImage;
		private int m_memoryTooltipImageVersion;
		private int m_hoveredMemorySlot = -1;
		private int m_messageScrollVersion;
		private int m_dialoguePresentVersion;
		private bool m_messagePinnedToLatest = true;
		private Button m_btnItemCollection;
		private Image m_imgItemSlot;
		private string m_itemSlotImageId;
		private int m_itemSlotVersion;
		private string m_itemSlotBagLocation;
		private int m_itemSlotBagVersion;
		private bool m_itemSlotHasItem;
		private bool m_itemSlotBreathing;
		private Outline m_itemSlotOutline;
		private bool m_itemSlotHovered;
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
		// 同框角色槽：一幕最多 3 人在场（S07 老周/陈先生/陈果）。槽位按 npcSet 顺序固定，
		// 「谁在说话」只决定这一槽播什么表情，不再决定其余人要不要显示。
		private const int MaxSceneCharacterSlots = 3;
		private readonly List<SceneCharacterSlot> m_characterSlots = new List<SceneCharacterSlot>();
		private string m_currentSpeakerNpcId;

		/// <summary>
		/// 一个角色槽的完整状态。立绘与 Spine 各留一套「已加载 / 已请求」记录，
		/// 换人时能立刻清干净——否则异步回来的旧图和旧骨骼会在新人物加载完之前继续站着。
		/// </summary>
		private sealed class SceneCharacterSlot
		{
			public int Index;
			public RectTransform Rect;
			public Image Image;
			public string NpcId;
			public string LoadedSprite;
			public string RequestedSprite;
			public float RequestTime;
			public CancellationTokenSource SpriteRequest;
			public int Version;
#if SPINE_UNITY && SPINE_CSHARP
			public UISpineWidget Spine;
			public string LoadedSpine;
			// 这一槽当前正在播的姿势（骨骼动画名），用于「同一表情不重播」和「空表情沿用上一句」
			public string LoadedPose;
#endif
		}

		private int m_sceneBackgroundVersion;
		// 缺素材（既无骨骼也无立绘）的角色只提示一次，别每句都刷屏
		private readonly HashSet<string> m_warnedMissingNpcVisuals = new HashSet<string>();
#if SPINE_UNITY && SPINE_CSHARP
		private readonly HashSet<string> m_unavailableNpcSpines = new HashSet<string>();
		// 缺表情的动作只在控制台提示一次，MapNpcSpinePose 是静态方法，所以这里也用静态集合
		private static readonly HashSet<string> s_warnedSpinePoseGaps = new HashSet<string>();
#endif
		private Image m_imgSceneRoot;
		private readonly List<Image> m_sceneLayerImages = new List<Image>();
		private string m_loadedSceneKey;
		// 场景切换过渡层：整屏纯色，插在 BG 之后（盖住场景与角色，不盖 UI）
		private Image m_imgSceneTransition;
		private CanvasGroup m_cgSceneTransition;
		private bool m_sceneTransitionActive;
		// 每幕开场的「日期 + 地点」黑场过场卡
		private GameObject m_titleCardRoot;
		private CanvasGroup m_cgTitleCard;
		private RectTransform m_overlayBodyContent;
		private GameObject m_dialoguePanel;
		private RectTransform m_rtNpcSubtitle;
		private TextMeshProUGUI m_tmpNpcSubtitle;
		private readonly List<TextMeshProUGUI> m_npcSpeakerLabels = new List<TextMeshProUGUI>();
		/// <summary>名字的阴影/描边副本：文本跟着同步，颜色留在 prefab 里的原值。</summary>
		private readonly List<TextMeshProUGUI> m_npcSpeakerDecorLabels = new List<TextMeshProUGUI>();
		private CanvasGroup m_cgNpcContinueHint;
		private float m_subtitleBodyTopInset;
		private float m_subtitleBodyBottomInset;
		private float m_subtitleBodySideInset;
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
			// 左上角常驻「从头开始」按钮已下线；重开入口只剩结局弹窗里的那个选项。
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
				SfxNames.StoryText, SfxNames.StoryBubbleIn, SfxNames.MemoryGet, SfxNames.MemoryLand,
				SfxNames.EmergeRise, SfxNames.EmergeBloom);
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
			// 事件在 CommitTurn 内部同步派发，这里只入队；播放由调用方 await，避免盖住下一句对白。
			EnqueueEmergeCutscene(emotionId);
		}

		protected override void OnRefresh()
		{
			RefreshUI();
		}

		protected override void OnDestroy()
		{
			m_narrativeService.AffectionChanged -= OnAffectionChanged;
			m_narrativeService.EmotionEmerged -= OnEmotionEmerged;
			for (int i = 0; i < m_characterSlots.Count; i++)
			{
				SceneCharacterSlot slot = m_characterSlots[i];
				if (slot == null) continue;
				CancelSpriteRequest(ref slot.SpriteRequest);
				slot.Version++;
			}
			m_characterSlots.Clear();
			m_messageScrollVersion++;
			m_dialoguePresentVersion++;
			m_messagePinnedToLatest = true;
			m_memorySlotImages.Clear();
			m_memorySlotThumbs.Clear();
			m_memorySlotVersions.Clear();
			m_memorySlotLabels.Clear();
			m_memoryDeleteHitAreas.Clear();
			m_memoryDeleteProgressRings.Clear();
			m_memoryCleanupHint = null;
			m_memoryCleanupHintGroup = null;
			m_memoryCleanupHintVersion++;
			m_memoryDeleteHoldVersion++;
			m_memoryDeleteHolding = false;
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
			m_itemSlotBagLocation = null;
			m_itemSlotBagVersion++;
			m_itemSlotHasItem = false;
			m_itemSlotBreathing = false;
			m_itemSlotOutline = null;
			m_itemSlotHovered = false;
			DestroyEmergenceCutscene();
			m_pendingEmergeEmotionIds.Clear();
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
			m_imgSceneRoot = null;
			m_sceneLayerImages.Clear();
			m_loadedSceneKey = null;
			m_imgSceneTransition = null;
			m_cgSceneTransition = null;
			m_sceneTransitionActive = false;
			AbortSceneSearch();
			m_titleCardRoot = null;
			m_cgTitleCard = null;
			m_currentSpeakerNpcId = null;
			m_overlayBodyContent = null;
			m_dialoguePanel = null;
			m_rtNpcSubtitle = null;
			m_tmpNpcSubtitle = null;
			m_npcSpeakerLabels.Clear();
			m_npcSpeakerDecorLabels.Clear();
			m_cgNpcContinueHint = null;
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
					await PlaySessionTitleCardAsync(session);
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
			await RefreshSceneBackgroundAsync(story);
			await RevealSessionTitleCardAsync();
			var enteredGrantedEmotions = new List<string>();
			var enteredGrantedItems = new List<string>();
			try
			{
				m_actionService.Execute(story.OnEnterActionIds,
					NarrativeSaveData.Get.BuildTurnKey(story.Id) + "|node-enter",
					null, true, enteredGrantedEmotions, enteredGrantedItems);
				RefreshMemorySlots();
				// 有东西入库就播「飞进物品栏」，落袋那一刻才点亮槽位；
				// 容量 1，同一批给了多件也只演最后一件（前面那件已经被顶掉）。
				if (enteredGrantedItems.Count > 0)
				{
					PlayItemGrantFlyIn(enteredGrantedItems[enteredGrantedItems.Count - 1]);
				}
				else
				{
					RefreshItemSlot();
				}
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

			// 结语卡：正片最后一句不进气泡，整块画面渐暗后只在黑底上出字。
			// HUD 不单独收，让它被黑幕一起吃掉，比先闪一下空对话面板自然。
			if (IsEndingCardTurn(story))
			{
				SetDialogueAdvanceCatcherActive(false);
				await PlayEndingCardAsync(NarrativePlayerName.Resolve(story.NpcText));
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
				{
					return;
				}

				await AutoAdvanceStoryNodeAsync(story, presentVersion);
				return;
			}

			bool npcDialogue = IsNpcDialogueTurn(story);
			SetDialoguePresentationMode(npcDialogue);
			m_advanceClickPending = false;
			SetDialogueAdvanceCatcherActive(true);

			// 场景探索节点：不播对白，把输入整个交给场景热点。
			// RunSceneSearchAsync 自己负责关掉推进拦截器，点中目标后推进到 DefaultNextNodeId。
			if (ShouldRunSceneSearch(story))
			{
				await RunSceneSearchAsync(story);
				return;
			}

			// 进节点就授予的情绪：先把过场播完再显示这句对白，否则会和打字机抢画面。
			// 放在状态重置之后，中途被打断也不会留下上一次的选中态。
			if (enteredGrantedEmotions.Count > 0)
			{
				for (int i = 0; i < enteredGrantedEmotions.Count; i++)
					EnqueueEmergeCutscene(enteredGrantedEmotions[i]);

				await PlayPendingEmergeCutscenesAsync();
				if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
					return;
			}

			if (npcDialogue)
				await PresentNpcSubtitleAsync(story, presentVersion);
			else
				await PresentNpcMessageAsync(NarrativePlayerName.Resolve(story.NpcText), presentVersion);
			if (IsDestroyed || presentVersion != m_dialoguePresentVersion)
			{
				return;
			}

			// 物品检查门：这一句刚好把物件交到玩家手上时，强制翻物品页，看够部位才往下走。
			// 没有配 inspectGate / 这句没给东西 / 部件都看过了，都会直接放行。
			if (enteredGrantedItems.Count > 0)
			{
				bool passed = await RunItemInspectGateAsync(story,
					enteredGrantedItems[enteredGrantedItems.Count - 1], presentVersion);
				if (!passed)
				{
					return;
				}
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
			displayText = NarrativePlayerName.Resolve(displayText);
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
				ApplyMessageAreaBottomInset(MESSAGE_AREA_BOTTOM_INSET);
				ShowPlayerReplyMessage(NarrativePlayerName.Resolve(reply.ReplyText));
				IReadOnlyList<string> available = m_narrativeService.GetAvailableEmotions(reply);
				IReadOnlyList<string> locked = m_narrativeService.GetLockedEmotions(reply);
				m_selectedEmotionId = m_narrativeService.GetDefaultEmotion(reply);

				// 除「无」以外没有可选情绪时，情绪面板只剩一个能点的「无」，
				// 这一步没有选择意义：直接用默认情绪提交，让玩家回复直接接回对话。
				if (available.Count == 0)
				{
					await CommitSelectedReplyAsync();
					return;
				}

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
				await PlayPendingEmergeCutscenesAsync();
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
			await PlayPendingEmergeCutscenesAsync();
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
			string replyText = NarrativePlayerName.Resolve(m_selectedReply.ReplyText);
			string emotionId = m_selectedEmotionId;
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
				TintShownPlayerReply(ResolveReplyBubbleColor(emotionId));

				// 先让玩家看清自己说的话（含情绪底色），点一下/回车才继续，否则下一句会把它顶走。
				await WaitPlayerReplyAcknowledgeAsync();

				// 涌现过场必须在下一节点表现和日终转场之前播完，否则会被盖掉。
				await PlayPendingEmergeCutscenesAsync();
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
					ResizeReplyPanel();
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
				((RightMessageItem)item).SetMessage(message.Text, message.BubbleColor);
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
				emotionRect.anchoredPosition = ReadLayoutPosition("EmotionPanel", new Vector2(-77f, 14f));
				emotionRect.sizeDelta = new Vector2(644f, 120f);
			}

			RectTransform replyRect = m_tfReplayMessageNode as RectTransform;
			if (replyRect != null)
			{
				replyRect.anchorMin = replyRect.anchorMax = new Vector2(0.5f, 0f);
				replyRect.pivot = new Vector2(0.5f, 0f);
				replyRect.anchoredPosition = ReadLayoutPosition("ReplyPanel", new Vector2(-77f, 14f));
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
			scrollRect.offsetMin = new Vector2(MESSAGE_AREA_LEFT_INSET, MESSAGE_AREA_BOTTOM_INSET);
			scrollRect.offsetMax = new Vector2(-MESSAGE_AREA_RIGHT_INSET, -MESSAGE_AREA_TOP_INSET);
		}

		/// <summary>单独改消息区底边距：选项面板显示时让位给它，隐藏时收回小留白。</summary>
		private void ApplyMessageAreaBottomInset(float inset)
		{
			RectTransform scrollRect = m_scrollMessage != null ? m_scrollMessage.transform as RectTransform : null;
			if (scrollRect == null)
			{
				return;
			}

			scrollRect.offsetMin = new Vector2(scrollRect.offsetMin.x, inset);
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
			// 物品栏底图不写死：有物品用打开的包，没物品用关闭的包（半透明），统一走 RefreshItemSlot。
			RefreshItemSlot();

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

		// ---- 记忆栏槽位 ----
		// 旧值 78×78 是照 dialogue_memory_slot_bg.png 的原生 76×78 一比一摆的，代价是名字只能挤到 8~11 号，
		// 在手机上就是一坨墨。底图是九切（spriteBorder 18），放大不会变形，所以直接按「名字读得清」来定尺寸：
		// 最长记忆名是 6 个字（收录机记忆1），24 号单行 ≈ 144，槽宽 150 刚好一行放下。
		private const float MemorySlotWidth = 150f;
		private const float MemorySlotHeight = 108f;
		private const float MemorySlotGap = 14f;
		/// <summary>有缩略图时名字带的厚度：24 号中文单行 ≈ 30 高，36 既兜得住又不至于把插画压掉一半。</summary>
		private const float MemorySlotLabelBandHeight = 36f;
		private const float MemorySlotLabelFontSizeMax = 24f;
		private const float MemorySlotLabelFontSizeMin = 14f;

		private void BuildMemorySlots()
		{
			m_memorySlotImages.Clear();
			m_memorySlotThumbs.Clear();
			m_memorySlotVersions.Clear();
			m_memorySlotLabels.Clear();
			m_memoryDeleteHitAreas.Clear();
			m_memoryDeleteProgressRings.Clear();
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
			layout.spacing = MemorySlotGap;
			// 上下各留 10：131 高的纸带里塞得下 108 的槽，槽位仍然垂直居中。
			layout.padding = new RectOffset(24, 144, 10, 10);
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
				m_memoryDeleteHitAreas.RemoveAt(last);
				m_memoryDeleteProgressRings.RemoveAt(last);
				slot.SetActive(false);
				UnityEngine.Object.Destroy(slot);
			}

			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			for (int i = m_memorySlotLabels.Count; i < count; i++)
			{
				var slot = new GameObject("MemorySlot" + i, typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
				slot.transform.SetParent(content, false);
				slot.transform.SetAsFirstSibling();
				// CanvasGroup 给破碎动画用：整卡（底图 + 缩略图 + 名字 + 删除叉）一起淡出。
				CanvasGroup slotGroup = slot.GetComponent<CanvasGroup>();
				slotGroup.alpha = 1f;
				slotGroup.blocksRaycasts = true;
				slotGroup.interactable = true;
				RectTransform slotRect = slot.GetComponent<RectTransform>();
				slotRect.sizeDelta = new Vector2(MemorySlotWidth, MemorySlotHeight);
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

				// 名字底板：有图时压住槽位底部一条，给 24 号的字一个干净的底。
				var labelBackGo = new GameObject("LabelBack", typeof(RectTransform), typeof(Image));
				labelBackGo.transform.SetParent(slot.transform, false);
				RectTransform labelBackRect = labelBackGo.GetComponent<RectTransform>();
				labelBackRect.anchorMin = new Vector2(0f, 0f);
				labelBackRect.anchorMax = new Vector2(1f, 0f);
				labelBackRect.pivot = new Vector2(0.5f, 0f);
				labelBackRect.anchoredPosition = new Vector2(0f, 2f);
				labelBackRect.sizeDelta = new Vector2(0f, MemorySlotLabelBandHeight);
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
				label.fontSizeMin = MemorySlotLabelFontSizeMin;
				label.fontSizeMax = MemorySlotLabelFontSizeMax;
				var hover = slot.AddComponent<EventTrigger>();
				var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
				int slotIndex = i;
				enter.callback.AddListener(_ => ShowMemoryTooltip(slotIndex));
				hover.triggers.Add(enter);
				var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
				exit.callback.AddListener(_ => HideMemoryTooltip(slotIndex));
				hover.triggers.Add(exit);
				// 删除热区：不是 Button —— 删除要「按住 1 秒」才生效，按下即开始抖，抬起/移出即取消。
				var delete = new GameObject("Delete", typeof(RectTransform), typeof(Image));
				delete.transform.SetParent(slot.transform, false);
				RectTransform deleteRect = delete.GetComponent<RectTransform>();
				deleteRect.anchorMin = deleteRect.anchorMax = Vector2.one;
				deleteRect.pivot = new Vector2(0.5f, 0.5f);
				deleteRect.anchoredPosition = new Vector2(-9f, -9f);
				deleteRect.sizeDelta = new Vector2(40f, 40f);
				Image deleteHitArea = delete.GetComponent<Image>();
				deleteHitArea.color = new Color(1f, 1f, 1f, 0.001f);
				EventTrigger holdTrigger = delete.AddComponent<EventTrigger>();
				var deleteDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
				deleteDown.callback.AddListener(_ => BeginMemoryDeleteHold(slotIndex));
				holdTrigger.triggers.Add(deleteDown);
				var deleteUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
				deleteUp.callback.AddListener(_ => CancelMemoryDeleteHold(slotIndex));
				holdTrigger.triggers.Add(deleteUp);
				// 不监听 PointerExit：卡片抖动会让热区自己跟着摆，移出就取消的话长按可能永远走不完。
				// 松手才是明确的「不删了」。
				// 长按进度环：围着叉从 0 顺时针填满，红色；满格那一刻才碎。
				var ringGo = new GameObject("ProgressRing", typeof(RectTransform), typeof(Image));
				ringGo.transform.SetParent(delete.transform, false);
				RectTransform ringRect = ringGo.GetComponent<RectTransform>();
				ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
				ringRect.pivot = new Vector2(0.5f, 0.5f);
				ringRect.anchoredPosition = Vector2.zero;
				ringRect.sizeDelta = new Vector2(MemoryDeleteRingSize, MemoryDeleteRingSize);
				Image ringImage = ringGo.GetComponent<Image>();
				ringImage.sprite = GetMemoryDeleteRingSprite();
				ringImage.color = MemoryDeleteRingColor;
				ringImage.type = Image.Type.Filled;
				ringImage.fillMethod = Image.FillMethod.Radial360;
				ringImage.fillOrigin = (int)Image.Origin360.Top;
				ringImage.fillClockwise = true;
				ringImage.fillAmount = 0f;
				ringImage.raycastTarget = false;
				ringGo.SetActive(false);
				CreateMemoryDeleteStroke(delete.transform, "CrossA", 45f);
				CreateMemoryDeleteStroke(delete.transform, "CrossB", -45f);
				m_memorySlotImages.Add(slotImage);
				m_memorySlotThumbs.Add(thumb);
				m_memorySlotLabelBacks.Add(labelBack);
				m_memorySlotVersions.Add(0);
				m_memorySlotLabels.Add(label);
				m_memoryDeleteHitAreas.Add(deleteHitArea);
				m_memoryDeleteProgressRings.Add(ringImage);
			}

			float viewportWidth = m_scrollMemoryList.viewport != null
				? m_scrollMemoryList.viewport.rect.width : m_scrollMemoryList.GetComponent<RectTransform>().rect.width;
			HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
			float slotsWidth = count * MemorySlotWidth +
				Mathf.Max(0, count - 1) * MemorySlotGap +
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
			rect.sizeDelta = new Vector2(28f, 5f);
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

		private void RefreshMemorySlots()
		{
			if (m_memorySlotLabels.Count == 0)
			{
				return;
			}

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			IReadOnlyList<string> ids = saveData.GetMemoryIdsInAcquisitionOrder();
			// 记忆删除只属于「记忆清理」阶段：其余阶段（对局、独白、结算等）一律不放出删除按钮。
			bool canDeleteMemory = saveData.DayPhase == NarrativeDayPhase.MemoryCleanup;
			EnsureMemorySlotCount(Mathf.Max(NarrativeRules.MemoryCapacity, ids.Count));
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			for (int i = 0; i < m_memorySlotLabels.Count; i++)
			{
				TextMeshProUGUI label = m_memorySlotLabels[i];
				if (label == null)
				{
					continue;
				}
				if (i < m_memoryDeleteHitAreas.Count && m_memoryDeleteHitAreas[i] != null)
					m_memoryDeleteHitAreas[i].gameObject.SetActive(canDeleteMemory && i < ids.Count);

				if (i < ids.Count)
				{
					MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(ids[i]);
					label.text = memory == null ? ids[i] : memory.MemoryName;
					label.enableAutoSizing = true;
					label.fontSizeMin = MemorySlotLabelFontSizeMin;
					label.fontSizeMax = MemorySlotLabelFontSizeMax;
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
					labelRect.anchoredPosition = new Vector2(0f, 2f);
					labelRect.sizeDelta = new Vector2(-12f, MemorySlotLabelBandHeight);
					// 有图时名字钉在带子里必须单行：6 个字的「收录机记忆1」一换行，TMP 会为了塞下两行
					// 一路缩到 14 号，等于又回到看不清。关掉折行让它只按宽度缩，最多 24 → 22。
					label.enableWordWrapping = false;
					label.fontSizeMin = MemorySlotLabelFontSizeMin;
					label.fontSizeMax = MemorySlotLabelFontSizeMax;
				}
				else
				{
					labelRect.anchorMin = Vector2.zero;
					labelRect.anchorMax = Vector2.one;
					labelRect.pivot = new Vector2(0.5f, 0.5f);
					labelRect.anchoredPosition = Vector2.zero;
					labelRect.offsetMin = Vector2.zero;
					labelRect.offsetMax = Vector2.zero;
					label.enableWordWrapping = true;
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
			rect.sizeDelta = new Vector2(ItemSlotItemSize, ItemSlotItemSize);
			rect.anchoredPosition = new Vector2(0f, ItemSlotItemOffsetY);
			m_imgItemSlot = go.GetComponent<Image>();
			m_imgItemSlot.preserveAspect = true;
			m_imgItemSlot.raycastTarget = false;
			m_imgItemSlot.enabled = false;
			// 描边用 UGUI 自带的 Outline（4 份偏移副本垫在图形下面），不需要额外材质，也不会被裁到 slot 里。
			m_itemSlotOutline = go.AddComponent<Outline>();
			m_itemSlotOutline.effectColor = ItemSlotHoverColor;
			m_itemSlotOutline.effectDistance = new Vector2(ItemSlotHoverOutlineWidth, -ItemSlotHoverOutlineWidth);
			m_itemSlotOutline.useGraphicAlpha = true;
			m_itemSlotOutline.enabled = false;
			EnsureItemSlotHover();
		}

		/// <summary>物品栏的鼠标悬停：只在「已经有物品」时点亮金色轮廓。</summary>
		private void EnsureItemSlotHover()
		{
			if (m_btnItemCollection == null || m_btnItemCollection.gameObject.GetComponent<EventTrigger>() != null)
			{
				return;
			}

			EventTrigger trigger = m_btnItemCollection.gameObject.AddComponent<EventTrigger>();
			EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
			enter.callback.AddListener(_ => SetItemSlotHovered(true));
			trigger.triggers.Add(enter);
			EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
			exit.callback.AddListener(_ => SetItemSlotHovered(false));
			trigger.triggers.Add(exit);
		}

		private void SetItemSlotHovered(bool hovered)
		{
			m_itemSlotHovered = hovered;
			RefreshItemSlotOutline();
		}

		private void RefreshItemSlotOutline()
		{
			if (m_itemSlotOutline != null)
			{
				m_itemSlotOutline.enabled = m_itemSlotHovered && m_itemSlotHasItem &&
					m_imgItemSlot != null && m_imgItemSlot.enabled;
			}
		}

		private void RefreshItemSlot()
		{
			EnsureItemSlot();
			if (m_imgItemSlot == null) return;
			// 入库演出进行中：槽位保持空态，等 PlayItemGrantFlyIn 落袋时再点亮。
			// 否则「场景拾取 → GrantItem → GrantMemory → RefreshUI」这条链会先把图标亮起来，
			// 飞行图标再飞过去，看起来像同时拿到了两件东西。
			if (m_itemGrantFlyActive)
			{
				ApplyItemSlotBagVisual(false);
				m_itemSlotImageId = null;
				m_itemSlotVersion++;
				m_imgItemSlot.enabled = false;
				RefreshItemSlotOutline();
				return;
			}
			// 物品栏容量 1（NarrativeRules.NarrativeItemCapacity），存的就是唯一那一件；
			// 这里取 [0] 和 ShowItems() 的取法保持一致，不要按字典序取末位。
			IReadOnlyList<string> ids = NarrativeSaveData.Get.GetNarrativeItemIdsInStableOrder();
			NarrativeItemConfig item = ids.Count == 0 ? null : NarrativeConfigMgr.Instance.GetNarrativeItem(ids[0]);
			string imageId = item == null ? null : item.ImageId;
			bool hasItem = !string.IsNullOrWhiteSpace(imageId) && GameModule.ResourceModule.CheckLocationValid(imageId);
			ApplyItemSlotBagVisual(hasItem);
			if (!hasItem)
			{
				m_itemSlotImageId = null;
				m_itemSlotVersion++;
				m_imgItemSlot.enabled = false;
				RefreshItemSlotOutline();
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
				RefreshItemSlotOutline();
			}, gameObject.GetCancellationTokenOnDestroy());
		}

		/// <summary>
		/// 背包底图：有物品用「打开的包」+ 呼吸缩放，没物品用「关闭的包」+ 半透明。
		/// 底图挂在按钮自身的 Image 上，因此缩放按钮就等于缩放整个 slot（含物品图标）。
		/// </summary>
		private void ApplyItemSlotBagVisual(bool hasItem)
		{
			m_itemSlotHasItem = hasItem;
			RefreshItemSlotOutline();
			Image bag = m_btnItemCollection != null ? m_btnItemCollection.image : null;
			if (bag != null)
			{
				string location = hasItem ? ItemSlotBagOpenLocation : ItemSlotBagClosedLocation;
				if (location == m_itemSlotBagLocation && bag.sprite != null)
				{
					bag.preserveAspect = true;
					bag.raycastTarget = true;
					bag.color = new Color(1f, 1f, 1f, hasItem ? 1f : ItemSlotEmptyAlpha);
				}
				else
				{
					m_itemSlotBagLocation = location;
					int version = ++m_itemSlotBagVersion;
					// 不能用 nativeSize：打开的包是 269x337，会顶大按钮，交给 preserveAspect 缩放进 slot。
					bag.SetSprite(location, false, loaded =>
					{
						if (IsDestroyed || version != m_itemSlotBagVersion || loaded == null || loaded.sprite == null) return;
						loaded.preserveAspect = true;
						loaded.raycastTarget = true;
						loaded.color = new Color(1f, 1f, 1f, m_itemSlotHasItem ? 1f : ItemSlotEmptyAlpha);
					}, gameObject.GetCancellationTokenOnDestroy());
				}
			}

			if (hasItem)
			{
				if (!m_itemSlotBreathing)
				{
					m_itemSlotBreathing = true;
					RunItemSlotBreathingAsync(gameObject.GetCancellationTokenOnDestroy()).Forget();
				}
			}
			else
			{
				StopItemSlotBreathing();
			}
		}

		private void StopItemSlotBreathing()
		{
			m_itemSlotBreathing = false;
			if (m_btnItemCollection != null)
			{
				m_btnItemCollection.transform.localScale = Vector3.one;
			}
		}

		/// <summary>物品栏呼吸：1 → 1+amplitude 的正弦缩放，摆布局时不抖。</summary>
		private async UniTaskVoid RunItemSlotBreathingAsync(CancellationToken token)
		{
			Transform target = m_btnItemCollection != null ? m_btnItemCollection.transform : null;
			float time = 0f;
			while (m_itemSlotBreathing && target != null && !token.IsCancellationRequested)
			{
				time += Time.unscaledDeltaTime;
				float scale = 1f;
				if (!m_layoutEditMode)
				{
					float phase = 0.5f - 0.5f * Mathf.Cos(time * Mathf.PI * 2f / ItemSlotBreathPeriod);
					scale = 1f + ItemSlotBreathAmplitude * phase;
				}
				target.localScale = new Vector3(scale, scale, 1f);
				await UniTask.Yield(PlayerLoopTiming.Update);
			}

			if (target != null)
			{
				target.localScale = Vector3.one;
			}
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

		/// <summary>给刚显示的那条玩家回复染上情绪色；颜色 alpha 为 0 表示不染。</summary>
		private void TintShownPlayerReply(Color bubbleColor)
		{
			if (m_messageList == null || m_messages.Count == 0)
			{
				return;
			}

			int index = m_messages.Count - 1;
			if (!m_messages[index].IsPlayer)
			{
				return;
			}

			string text = m_messages[index].Text;
			m_messages[index] = (true, text, bubbleColor);
			LoopListViewItem2 shown = m_messageList.LoopRectView.GetShownItemByItemIndex(index);
			if (shown != null)
			{
				m_messageList.GetItem<RightMessageItem>(shown.GoId)?.SetMessage(text, bubbleColor);
			}
		}

		/// <summary>情绪 ID → 气泡底色；「无」和未知情绪返回透明，保持米色纸片。</summary>
		private static Color ResolveReplyBubbleColor(string emotionId)
		{
			if (string.IsNullOrWhiteSpace(emotionId) ||
				string.Equals(emotionId, NarrativeRules.NoneEmotionId, StringComparison.OrdinalIgnoreCase))
			{
				return Color.clear;
			}

			EmotionConfig emotion = NarrativeConfigMgr.Instance.GetEmotion(emotionId);
			return emotion == null ? Color.clear : EmotionVisual.ResolveColor(emotion);
		}

		/// <summary>玩家回复先停在屏幕上，等一次点击/回车再往下推进剧情。</summary>
		private async UniTask WaitPlayerReplyAcknowledgeAsync()
		{
			int version = m_dialoguePresentVersion;
			// 回复选项面板期间 catcher 是关着的，这里重新打开才接得到点击。
			SetDialogueAdvanceCatcherActive(true);
			m_advanceClickPending = false;
			while (true)
			{
				if (IsDestroyed || version != m_dialoguePresentVersion)
				{
					return;
				}

				if (ConsumeAdvanceClick() || WasDialogueAdvanceKeyPressed())
				{
					m_advanceClickPending = false;
					return;
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
			}
		}

		private static bool WasDialogueAdvanceKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			if (Keyboard.current == null) return false;
			return Keyboard.current.enterKey.wasPressedThisFrame ||
				Keyboard.current.numpadEnterKey.wasPressedThisFrame;
#else
			return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
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
		/// 其中阴影/描边副本单独归到装饰层：文本跟着走，颜色留在 prefab（白色描边压在米白面板上是「提亮」，
		/// 换成角色色反而会糊成一团）。
		/// </summary>
		private void CollectSpeakerLabels(Transform root)
		{
			m_npcSpeakerLabels.Clear();
			m_npcSpeakerDecorLabels.Clear();
			TextMeshProUGUI[] candidates = root.GetComponentsInChildren<TextMeshProUGUI>(true);
			for (int i = 0; i < candidates.Length; i++)
			{
				TextMeshProUGUI candidate = candidates[i];
				if (candidate == null)
				{
					continue;
				}

				string nodeName = candidate.gameObject.name;
				if (!nodeName.StartsWith(NpcSpeakerLabelPrefix, StringComparison.Ordinal))
				{
					continue;
				}

				if (IsNpcSpeakerDecorLabel(nodeName))
				{
					m_npcSpeakerDecorLabels.Add(candidate);
				}
				else
				{
					m_npcSpeakerLabels.Add(candidate);
				}
			}
		}

		private static bool IsNpcSpeakerDecorLabel(string nodeName)
		{
			for (int i = 0; i < NpcSpeakerDecorLabelKeywords.Length; i++)
			{
				if (nodeName.IndexOf(NpcSpeakerDecorLabelKeywords[i], StringComparison.Ordinal) >= 0)
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>说话人名字按 npcId 上色，阴影副本只同步文本。</summary>
		private void SetNpcSpeaker(string speaker)
		{
			speaker = speaker ?? string.Empty;
			Color color = NpcSpeakerPalette.Resolve(speaker);
			for (int i = 0; i < m_npcSpeakerLabels.Count; i++)
			{
				TextMeshProUGUI label = m_npcSpeakerLabels[i];
				label.text = speaker;
				label.color = color;
			}

			for (int i = 0; i < m_npcSpeakerDecorLabels.Count; i++)
			{
				m_npcSpeakerDecorLabels[i].text = speaker;
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
			string body = NarrativePlayerName.Resolve(story.NpcText) ?? string.Empty;
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

			m_messages[index] = (m_messages[index].IsPlayer, text, m_messages[index].BubbleColor);
			LoopListView2 listView = m_messageList.LoopRectView;
			LoopListViewItem2 shown = listView.GetShownItemByItemIndex(index);
			if (shown != null)
			{
				if (m_messages[index].IsPlayer)
					m_messageList.GetItem<RightMessageItem>(shown.GoId)
						?.SetMessage(text, m_messages[index].BubbleColor);
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

		private void AddMessage(bool isPlayer, string text, Color bubbleColor = default)
		{
			bool wasPinned = m_messagePinnedToLatest;
			if (m_messages.Count > 0)
			{
				LoopListViewItem2 prevLast = m_messageList.LoopRectView.GetShownItemByItemIndex(m_messages.Count - 1);
				wasPinned = wasPinned || prevLast != null;
			}

			m_messages.Add((isPlayer, text, bubbleColor));
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
			var grantedEmotions = new List<string>();
			var grantedMemories = new List<string>();
			m_actionService.Execute(story.OnExitActionIds,
				saveData.BuildTurnKey(story.Id) + "|node-exit", saveData, true, grantedEmotions, null,
				grantedMemories);
			// GrantMemory 动作写进来的记忆要补一次涌现扫描：离场动作不走 CommitTurn，
			// 没有别的地方会做「新记忆 → 凑够两段 → 情绪涌现」这一步（例：S04 结尾 M17 起死回生）。
			if (grantedMemories.Count > 0)
			{
				RefreshMemorySlots();
				IReadOnlyList<string> emerged = m_narrativeService.ScanEmergenceAndApply(saveData);
				for (int i = 0; i < emerged.Count; i++)
				{
					if (!grantedEmotions.Contains(emerged[i]))
						grantedEmotions.Add(emerged[i]);
				}
			}

			// 自动推进的节点（没有回复选项）不经过 CommitTurn，不会派发 EmotionEmerged，这里补入队；
			// 真正的播放由调用方在推进/转场之前 await，避免被下一句或日终盖掉。
			for (int i = 0; i < grantedEmotions.Count; i++)
				EnqueueEmergeCutscene(grantedEmotions[i]);
		}

		private void EnterDayEnd()
		{
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryDayEnd);
			SoundConfigMgr.Instance.PlayBgm(SfxNames.BgmDayEnd, fadeIn: 1.5f);
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (saveData.DayPhase == NarrativeDayPhase.Active && string.IsNullOrWhiteSpace(saveData.CurrentSessionId))
				saveData.BeginDayMonologue();

			// 正式结局本来就没有「明天」：S09 没写日终独白时，反思页只会显示一句程序兜底文案，
			// 把刚出的结尾字幕顶掉。这里直接结算到结局（记忆清理仍照常走）；
			// 哪天把该 Session 的 dayMonologue 补上，就会自动恢复反思页。
			NarrativeSessionConfig completedSession =
				NarrativeConfigMgr.Instance.GetSession(saveData.LastCompletedSessionId);
			if (saveData.DayPhase == NarrativeDayPhase.Monologue && completedSession != null &&
				completedSession.IsFormalEnding && string.IsNullOrWhiteSpace(completedSession.DayMonologue))
			{
				saveData.CompleteDayMonologue(m_storyScheduler.FindNextScheduledDay(saveData));
				AdvanceFromDayPhase(saveData);
				return;
			}

			ShowDayMonologue();
		}

		private void ShowDayMonologue()
		{
			SetMonologuePresentation(true);
			NarrativeSaveData saveData = NarrativeSaveData.Get;
			NarrativeSessionConfig session = NarrativeConfigMgr.Instance.GetSession(saveData.LastCompletedSessionId);
			string body = session != null && !string.IsNullOrWhiteSpace(session.DayMonologue)
				? NarrativePlayerName.Resolve(session.DayMonologue)
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
			if (m_btnLayoutEdit != null) m_btnLayoutEdit.gameObject.SetActive(showHud);
			if (m_scrollMemoryList != null) m_scrollMemoryList.gameObject.SetActive(showHud);
			if (m_dialoguePanel != null) m_dialoguePanel.SetActive(showHud);
			if (m_rtNpcSubtitle != null) m_rtNpcSubtitle.gameObject.SetActive(false);
			if (m_tfReplayMessageNode != null) m_tfReplayMessageNode.gameObject.SetActive(false);
			if (m_tfReplayEmoNode != null) m_tfReplayEmoNode.gameObject.SetActive(false);
			SetDialogueAdvanceCatcherActive(false);
			if (visible)
			{
				// 日终反思是整屏演出：所有同框角色一起离场，回来时由下一幕重新排
				for (int i = 0; i < m_characterSlots.Count; i++)
				{
					HideCharacterSlot(m_characterSlots[i]);
				}
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

			// 任何离开清理阶段的路径都先把中央提示收掉，避免它挂在新一天的对白上面。
			HideMemoryCleanupHint();

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
			SetMonologuePresentation(false);
			SetDialogueAdvanceCatcherActive(false);
			if (m_dialoguePanel != null) m_dialoguePanel.SetActive(false);
			if (m_scrollMemoryList != null) m_scrollMemoryList.gameObject.SetActive(true);
			// 清理提示不再借用 NPC 对话框：改成屏幕正中的白字呼吸，数字随删除实时变化。
			if (m_rtNpcSubtitle != null) m_rtNpcSubtitle.gameObject.SetActive(false);
			SetContinueHintVisible(false, true);
			// 「记忆溢出」只在刚进入清理阶段时响一次；删完一段后 ShowMemoryCleanup 还会被调，
			// 那时正在播破碎音，再叠一条卡林巴会把破碎那一下盖掉。
			if (ShowMemoryCleanupHint()) SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryOverflow);
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

			// 部件分两路：能在物品图上圈出来的走热点；圈不住的（没坐标、或这件物品没有图）
			// 在纸面底部列成小按钮。两路都不进物品栏，只加记忆。
			var hotParts = new List<NarrativeItemPartConfig>();
			var listParts = new List<NarrativeItemPartConfig>();
			if (item.PartIds != null)
			{
				foreach (string partId in item.PartIds)
				{
					NarrativeItemPartConfig part = NarrativeConfigMgr.Instance.GetNarrativeItemPart(partId);
					if (part == null)
					{
						continue;
					}

					if (HasItemPageHotspot(item, part))
					{
						hotParts.Add(part);
					}
					else
					{
						listParts.Add(part);
					}
				}
			}

			ShowItemPage(item, hotParts, listParts);
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
			IReadOnlyList<string> emergedEmotions = m_narrativeService.ScanEmergenceAndApply(saveData);
			RefreshUI();
			PlayItemMemoryFlyIn(memoryId, source);
			// 检查门走完最后一个部位时，这里会把物品页关掉并放行剧情（见 BattleMainUI_ItemInspectGate）。
			NotifyInspectGateProgress();
			// 调查部件绕过 CommitTurn，涌现不会派事件，这里补上过场触发。
			for (int i = 0; i < emergedEmotions.Count; i++)
			{
				EnqueueEmergeCutscene(emergedEmotions[i]);
			}

			if (emergedEmotions.Count > 0)
			{
				PlayPendingEmergeCutscenesAsync().Forget();
			}
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
					IReadOnlyList<string> emotions = m_narrativeService.ScanEmergenceAndApply(saveData);
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
				buttons.Add(("添加记忆：" + missingMemory.MemoryName, (Action)(() =>
				{
					saveData.AddMemories(new[] { missingMemory.Id });
					IReadOnlyList<string> emerged = m_narrativeService.ScanEmergenceAndApply(saveData);
					for (int i = 0; i < emerged.Count; i++)
					{
						EnqueueEmergeCutscene(emerged[i]);
					}

					if (emerged.Count > 0)
					{
						PlayPendingEmergeCutscenesAsync().Forget();
					}

					ShowDebugPanel();
				})));
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

		/// <summary>纸面按钮。返回按钮上的文字，调用方要放大/自适应用字号时自己改。</summary>
		private static TextMeshProUGUI CreateOverlayButton(Transform parent, string label, Vector2 position,
			Vector2 size, Action action, bool fromTop = false, float fontSize = 17f)
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
			TextMeshProUGUI text = CreateOverlayText(go.transform, label, fontSize, Vector2.zero, size,
				TextAlignmentOptions.Center);
			text.overflowMode = TextOverflowModes.Ellipsis;
			text.enableWordWrapping = true;
			RectTransform textRect = text.rectTransform;
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = new Vector2(10f, 2f);
			textRect.offsetMax = new Vector2(-10f, -2f);
			return text;
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
			RefreshSceneCharacters(story);
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

		/// <summary>
		/// 本幕该同时在场的角色。npcSet 决定常驻阵容（顺序即站位），节点说话人不在阵容里
		/// （AI 内心话、陈父、妻子这类）时补进来——「谁说话」不再影响「谁站着」。
		/// </summary>
		private List<string> ResolvePresentNpcIds(StoryNodeConfig story)
		{
			var present = new List<string>();
			string speaker = story != null && !string.IsNullOrWhiteSpace(story.NpcId)
				? story.NpcId.Trim()
				: null;
			// 节点级「这位中途走了」：只留说话人，其余槽收起
			if (story != null && story.HideOtherNpc)
			{
				if (!string.IsNullOrEmpty(speaker)) present.Add(speaker);
				return present;
			}

			List<string> npcSet = GetSessionNpcSet();
			for (int i = 0; i < npcSet.Count && present.Count < MaxSceneCharacterSlots; i++)
				present.Add(npcSet[i]);

			if (!string.IsNullOrEmpty(speaker) && !IsSilentNpcId(speaker) && !present.Contains(speaker) &&
				present.Count < MaxSceneCharacterSlots)
				present.Add(speaker);
			return present;
		}

		/// <summary>AI / 旁白 只是「心里话」的 npcId，不该真的站到场景里。</summary>
		private static bool IsSilentNpcId(string npcId)
			=> string.Equals(npcId, "AI", StringComparison.OrdinalIgnoreCase) || npcId == "旁白";

		/// <summary>
		/// 按「在场角色」刷新所有槽。槽位分配只看 npcSet 顺序，与当前是谁在说话无关，
		/// 所以换说话人时人不会互换位置、也不会整块消失。
		/// </summary>
		private void RefreshSceneCharacters(StoryNodeConfig story)
		{
			if (IsDestroyed) return;
			if (m_imgSceneRoot == null) BindSceneBackgroundView();
			if (m_imgSceneRoot == null) return;

			m_currentSpeakerNpcId = story != null ? story.NpcId : null;
			List<string> present = ResolvePresentNpcIds(story);
			int count = Mathf.Min(present.Count, MaxSceneCharacterSlots);
			for (int i = 0; i < MaxSceneCharacterSlots; i++)
			{
				if (i >= count)
				{
					SceneCharacterSlot spare = GetCharacterSlot(i);
					if (spare != null) HideCharacterSlot(spare);
					continue;
				}

				SceneCharacterSlot slot = EnsureCharacterSlot(i);
				if (slot != null) ShowCharacterSlot(slot, i, count, present[i], story);
			}
		}

		private SceneCharacterSlot GetCharacterSlot(int index)
			=> index >= 0 && index < m_characterSlots.Count ? m_characterSlots[index] : null;

		private SceneCharacterSlot EnsureCharacterSlot(int index)
		{
			if (m_imgSceneRoot == null) BindSceneBackgroundView();
			if (m_imgSceneRoot == null) return null;
			while (m_characterSlots.Count <= index) m_characterSlots.Add(null);
			SceneCharacterSlot slot = m_characterSlots[index];
			if (slot != null) return slot;

			// 第一槽沿用旧节点名，第二槽兼容旧的 OtherSceneCharacter，层级里已有的节点不重建
			string objectName = index == 0 ? "SceneCharacter" : "SceneCharacter" + index;
			Transform existing = m_imgSceneRoot.transform.Find(objectName);
			if (existing == null && index == 1) existing = m_imgSceneRoot.transform.Find("OtherSceneCharacter");
			GameObject go;
			if (existing != null)
			{
				go = existing.gameObject;
			}
			else
			{
				go = new GameObject(objectName, typeof(RectTransform));
				go.transform.SetParent(m_imgSceneRoot.transform, false);
			}

			slot = new SceneCharacterSlot
			{
				Index = index,
				Rect = go.GetComponent<RectTransform>(),
			};
			slot.Image = go.GetComponent<Image>();
			if (slot.Image == null) slot.Image = go.AddComponent<Image>();
			slot.Image.type = Image.Type.Simple;
			slot.Image.useSpriteMesh = false;
			slot.Image.preserveAspect = true;
			slot.Image.color = Color.white;
			slot.Image.raycastTarget = false;
			// 没有贴图时先别让它画白色方块
			if (!HasUsableSprite(slot.Image)) slot.Image.enabled = false;
			slot.Rect.anchorMin = slot.Rect.anchorMax = new Vector2(0.5f, 0f);
			slot.Rect.pivot = new Vector2(0.5f, 0f);
			slot.Rect.sizeDelta = new Vector2(600f, CharacterSlotHeight);
			m_characterSlots[index] = slot;
			RegisterCharacterSlotDragHandle(slot);
			return slot;
		}

		private void ShowCharacterSlot(SceneCharacterSlot slot, int index, int count, string npcId,
			StoryNodeConfig story)
		{
			bool npcChanged = slot.NpcId != npcId;
			slot.NpcId = npcId;
			slot.Rect.gameObject.SetActive(true);
			string part = GetCharacterSlotLayoutPart(slot);
			slot.Rect.anchoredPosition = ReadLayoutPosition(part,
				GetCharacterDefaultPosition(npcId, index, count));
			slot.Rect.sizeDelta = new Vector2(600f, CharacterSlotHeight);
			slot.Rect.localScale = ReadCharacterVisualScale(part);
			slot.Rect.pivot = new Vector2(0.5f, 0f);
			slot.Rect.anchorMin = slot.Rect.anchorMax = new Vector2(0.5f, 0f);
			slot.Image.raycastTarget = m_layoutEditMode;

			if (npcChanged)
			{
				// 换人必须当场清干净：旧图留在槽里等异步加载，只要慢一拍，
				// 画面上就是「字幕写着 A、站着的是 B」——这正是要修的那个 bug。
				ClearCharacterSlotVisual(slot);
			}

			// 只有说话人播这一句的表情，其他人回待机，免得两个人同时抢动作
			string poseAsset = IsCurrentSpeaker(npcId) ? story?.AnimationAsset : "normal";
			RefreshCharacterSlotVisualAsync(slot, npcId, poseAsset).Forget();
		}

		private bool IsCurrentSpeaker(string npcId)
			=> !string.IsNullOrEmpty(npcId) && npcId == m_currentSpeakerNpcId;

		private string GetCharacterSlotLayoutPart(SceneCharacterSlot slot)
			=> GetCharacterLayoutPart(slot != null ? slot.NpcId : null, true);

		private void HideCharacterSlot(SceneCharacterSlot slot)
		{
			if (slot == null) return;
			slot.NpcId = null;
			ClearCharacterSlotVisual(slot);
			if (slot.Rect != null) slot.Rect.gameObject.SetActive(false);
		}

		/// <summary>
		/// 把一个槽的可见内容清空（立绘 + 骨骼），并让所有在途的异步请求作废。
		/// 只清画面，不动 Rect 的位置与缩放。
		/// </summary>
		private void ClearCharacterSlotVisual(SceneCharacterSlot slot)
		{
			if (slot == null) return;
			CancelSpriteRequest(ref slot.SpriteRequest);
			slot.RequestedSprite = null;
			slot.LoadedSprite = null;
			slot.Version++;
			if (slot.Image != null)
			{
				slot.Image.sprite = null;
				slot.Image.enabled = false;
			}
#if SPINE_UNITY && SPINE_CSHARP
			HideCharacterSlotSpine(slot);
#endif
		}

		private async UniTaskVoid RefreshCharacterSlotVisualAsync(SceneCharacterSlot slot, string npcId,
			string animationAsset)
		{
			if (slot == null || slot.Rect == null) return;
			int version = ++slot.Version;
#if SPINE_UNITY && SPINE_CSHARP
			string spineLocation = ResolveNpcSpineLocation(npcId);
			if (!string.IsNullOrEmpty(spineLocation) && IsPortraitLocationReady(spineLocation) &&
				!m_unavailableNpcSpines.Contains(spineLocation))
			{
				if (await ShowCharacterSlotSpineAsync(slot, spineLocation, npcId, animationAsset, version))
					return;
				if (IsDestroyed || slot.Version != version) return;
			}
#endif
			ShowCharacterSlotSprite(slot, npcId, animationAsset, version);
		}

		private void ShowCharacterSlotSprite(SceneCharacterSlot slot, string npcId, string animationAsset, int version)
		{
			if (slot == null || slot.Image == null) return;
			string location = ResolveNpcPortraitLocation(npcId, animationAsset);
			if (string.IsNullOrEmpty(location))
			{
				// 这个角色还没有骨骼也没有立绘：槽位留在原位、只是不画东西。
				// 旧实现会把整槽 SetActive(false)，画面里只剩另一个人，看起来就像「说话的是别人」。
				WarnMissingNpcVisual(npcId);
				slot.Image.enabled = false;
				return;
			}

			if (location == slot.LoadedSprite && HasUsableSprite(slot.Image))
			{
				FitSceneCharacterSprite(slot.Image);
				return;
			}
			if (location == slot.RequestedSprite && Time.unscaledTime - slot.RequestTime < 5f) return;

			SoundConfigMgr.Instance.PlaySfx(SfxNames.PortraitSwitch);
			CancelSpriteRequest(ref slot.SpriteRequest);
			slot.RequestedSprite = location;
			slot.RequestTime = Time.unscaledTime;
			slot.SpriteRequest = CancellationTokenSource.CreateLinkedTokenSource(
				gameObject.GetCancellationTokenOnDestroy());
			slot.Image.SetSprite(location, false, image =>
			{
				if (IsDestroyed || slot.Version != version || image == null || image.sprite == null ||
					m_monologuePresentation)
				{
					return;
				}

				slot.LoadedSprite = location;
				slot.RequestedSprite = null;
				FitSceneCharacterSprite(image);
				HideLegacyNpcPortrait();
			}, slot.SpriteRequest.Token);
		}

		private void WarnMissingNpcVisual(string npcId)
		{
			if (string.IsNullOrEmpty(npcId) || !m_warnedMissingNpcVisuals.Add(npcId)) return;
			DLogger.Warning("角色「{0}」既没有 Spine 也没有立绘，场景里会空一个位置；请补素材", npcId);
		}

		private static void CancelSpriteRequest(ref CancellationTokenSource request)
		{
			if (request == null) return;
			request.Cancel();
			request.Dispose();
			request = null;
		}

		/// <summary>
		/// 这张 Image 现在能不能画。Unity 的 Image 在没有 sprite（或 sprite 的贴图已被卸载）时
		/// 画的是白色实心矩形——全屏层/立绘框都会变成一屏白，所以开显示前统一用这个判断。
		/// </summary>
		private static bool HasUsableSprite(Image image)
		{
			return image != null && image.sprite != null && image.sprite.texture != null;
		}

		private void FitSceneCharacterSprite(Image image)
		{
			if (image == null) return;
			image.enabled = HasUsableSprite(image);
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
		private async UniTask<bool> ShowCharacterSlotSpineAsync(SceneCharacterSlot slot, string location,
			string npcId, string animationAsset, int version)
		{
			if (slot == null || slot.Rect == null || slot.Image == null)
			{
				return false;
			}

			if (location == slot.LoadedSpine && slot.Spine != null && !slot.Spine.IsDestroyed)
			{
				if (!slot.Spine.HasValidSkeleton)
				{
					m_unavailableNpcSpines.Add(location);
					return false;
				}

				ApplyCharacterSlotSpinePose(slot, npcId, animationAsset, false);
				slot.Spine.FitToSlot(slot.Rect.sizeDelta);
				slot.Spine.SetRaycastTarget(m_layoutEditMode);
				slot.Image.enabled = false;
				return true;
			}

			HideCharacterSlotSpine(slot);
			slot.Image.enabled = false;
			UISpineWidget widget = await CreateWidgetByPathAsync<UISpineWidget>(
				slot.Rect.transform, location);
			if (IsDestroyed || slot.Version != version)
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

			slot.Spine = widget;
			slot.LoadedSpine = location;
			slot.LoadedPose = null;
			ApplyCharacterSlotSpinePose(slot, npcId, animationAsset, true);
			widget.FitToSlot(slot.Rect.sizeDelta);
			widget.SetRaycastTarget(m_layoutEditMode);
			slot.Image.enabled = false;
			return true;
		}

		private void ApplyCharacterSlotSpinePose(SceneCharacterSlot slot, string npcId, string animationAsset,
			bool forceReplay)
		{
			if (slot == null || slot.Spine == null)
			{
				return;
			}

			string pose = MapNpcSpinePose(npcId, animationAsset);
			if (string.IsNullOrEmpty(pose))
			{
				// 本句没标表情：沿用上一句的姿势，别把正在播的动作打断
				pose = string.IsNullOrEmpty(slot.LoadedPose) ? DefaultNpcSpinePose : slot.LoadedPose;
			}

			if (!forceReplay && pose == slot.LoadedPose)
			{
				return;
			}

			slot.LoadedPose = pose;
			// 美术把每个表情的贴图做成同名皮肤（zhengchang/sikao/jingya…），陈先生和陈果没有 default 皮肤，
			// 不切皮肤就一个格子都挂不上。没有同名皮肤的（如悲伤/气愤只做了动作）退回基准皮肤，
			// 靠同名动作里的换图关键帧表达。
			if (slot.Spine.HasSkin(pose))
			{
				slot.Spine.SwitchSkin(pose);
			}
			else if (slot.Spine.HasSkin(BaseNpcSpineSkin))
			{
				slot.Spine.SwitchSkin(BaseNpcSpineSkin);
			}
			slot.Spine.SetAnimation(pose, true, true);
		}

		private void HideCharacterSlotSpine(SceneCharacterSlot slot)
		{
			if (slot == null)
			{
				return;
			}

			if (slot.Spine != null && !slot.Spine.IsDestroyed)
			{
				slot.Spine.Destroy();
			}

			slot.Spine = null;
			slot.LoadedSpine = null;
			slot.LoadedPose = null;
		}

		private const string DefaultNpcSpinePose = "zhengchang";
		private const string BaseNpcSpineSkin = "zhengchang";

		private static bool IsChenXiansheng(string npcId)
		{
			return npcId == "陈先生";
		}

		private static bool IsChenGuo(string npcId)
		{
			return npcId == "陈果" || npcId == "女孩";
		}

		/// <summary>
		/// 剧情表的 animationAsset 存的是**语义表情键**（think/sad/angry/happy/moved/surprised），
		/// 不是骨骼动画名——三个角色这版各只给了 4 个动作，能表达的表情并不一样：
		/// 老周 = 平静/悲伤/气愤/思考，陈果 = 平静/悲伤/喜悦·感动/思考，陈先生 = 平静/伤心·失落/惊讶/思考。
		/// 缺的表情在这里退到该角色最接近的一个，并在首次遇到时打一条警告给美术补动作。
		/// 返回空串表示「本句没有表情，沿用上一句」。
		/// </summary>
		private static string MapNpcSpinePose(string npcId, string animationAsset)
		{
			string emotion = NormalizeEmotionKey(animationAsset);
			if (emotion == "inherit")
			{
				return string.Empty;
			}

			string pose;
			switch (emotion)
			{
				case "think":
					pose = "sikao";
					break;
				case "sad":
					pose = IsChenXiansheng(npcId) ? "shangxin" : "beishang";
					break;
				case "angry":
					// 陈先生、陈果没有「气愤」动作，先用情绪强度最接近的顶上
					pose = npcId == "老周" ? "shengqi" : (IsChenXiansheng(npcId) ? "jingya" : "beishang");
					break;
				case "happy":
					pose = IsChenGuo(npcId) ? "kaixin" : DefaultNpcSpinePose;
					break;
				case "moved":
					pose = IsChenGuo(npcId) ? "kaixin" : (IsChenXiansheng(npcId) ? "shangxin" : DefaultNpcSpinePose);
					break;
				case "surprised":
					pose = IsChenXiansheng(npcId) ? "jingya" : DefaultNpcSpinePose;
					break;
				default:
					pose = DefaultNpcSpinePose;
					break;
			}

			WarnNpcSpinePoseGap(npcId, emotion, pose);
			return pose;
		}

		/// <summary>本版动作里没有对应表情的角色，首次遇到时提示一次，方便美术补齐 clip。</summary>
		private static void WarnNpcSpinePoseGap(string npcId, string emotion, string pose)
		{
			bool missing;
			switch (emotion)
			{
				case "angry":
					missing = npcId != "老周";
					break;
				case "happy":
					missing = !IsChenGuo(npcId);
					break;
				case "moved":
					missing = npcId == "老周";
					break;
				case "surprised":
					missing = !IsChenXiansheng(npcId);
					break;
				default:
					missing = false;
					break;
			}

			if (!missing || !s_warnedSpinePoseGaps.Add(npcId + "." + emotion))
			{
				return;
			}

			DLogger.Warning("角色 {0} 没有「{1}」动作，暂用 {2} 代替，需要美术补一个 clip", npcId, emotion, pose);
		}

		/// <summary>把表里的演出资源（兼容中英文与旧值）归一成语义表情键；inherit = 沿用上一句。</summary>
		private static string NormalizeEmotionKey(string animationAsset)
		{
			if (string.IsNullOrWhiteSpace(animationAsset))
			{
				return "inherit";
			}

			switch (animationAsset.Trim().ToLowerInvariant())
			{
				case "think":
				case "thoughtful":
				case "sikao":
					return "think";
				case "sad":
				case "beishang":
				case "shangxin":
					return "sad";
				case "angry":
				case "shengqi":
					return "angry";
				case "happy":
				case "joyful":
				case "kaixin":
				case "xiyue":
					return "happy";
				case "moved":
				case "gandong":
					return "moved";
				case "surprised":
				case "shock":
				case "jingya":
					return "surprised";
				default:
					return "normal";
			}
		}

		private static string ResolveNpcSpineLocation(string npcId)
		{
			// 三个主要角色的骨骼动画（各 4 句待机动作）放在 BundleAssets/Prefabs/UISpineModel 下，
			// 打包地址按文件名走（AddressByFileName），所以这里直接给预制体名。
			switch (npcId)
			{
				case "老周":
					return "uispine_laozhou";
				case "陈先生":
					return "uispine_chenxiansheng";
				case "陈果":
				case "女孩":
					return "uispine_chenguo";
				default:
					return string.Empty;
			}
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

		/// <summary>销毁所有槽的骨骼（日终反思、清场时用）。</summary>
		private void HideNpcSpine()
		{
			for (int i = 0; i < m_characterSlots.Count; i++)
			{
				HideCharacterSlotSpine(m_characterSlots[i]);
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
				case "happy":
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

		/// <summary>
		/// 场景刷新。backgroundId 优先取节点级（幕内换景），为空回退 Session 级。
		/// 场景真变化时播过渡：店铺之间走「亮白」，其他场景走「渐黑」。
		/// </summary>
		private async UniTask RefreshSceneBackgroundAsync(StoryNodeConfig story)
		{
			if (m_imgSceneRoot == null)
			{
				BindSceneBackgroundView();
			}

			string npcId = NarrativeSaveData.Get.CurrentSessionId == "S01" ? "陈先生" :
				story == null ? string.Empty : story.NpcId;
			// 节点级 backgroundId 优先（幕内换景），为空回退 Session 级；都取不到再走旧的 npcId 规则。
			string backgroundId = story == null ? string.Empty : story.BackgroundId;
			if (string.IsNullOrWhiteSpace(backgroundId))
			{
				backgroundId = ResolveCurrentSessionBackgroundId();
			}
			string[] backgrounds = Array.Empty<string>();
			string[] foregrounds = Array.Empty<string>();
			bool knownScene = !string.IsNullOrWhiteSpace(backgroundId) &&
				TryResolveBackgroundLocations(backgroundId, out backgrounds, out foregrounds);
			if (!knownScene)
			{
				if (!string.IsNullOrWhiteSpace(backgroundId))
				{
					DLogger.Warning("Unknown scene backgroundId: {0}; falling back to npcId layout.", backgroundId);
				}

				backgrounds = ResolveSceneBackgroundLocations(npcId);
				foregrounds = ResolveSceneForegroundLocations(npcId);
			}

			// 人物插层由场景决定：0=所有前景盖住人物（站在柜台/书桌后面），1=第 1 层前景在人物身后。
			m_sceneCharacterLayer = knownScene
				? ResolveSceneCharacterLayer(backgroundId)
				: (npcId == "陈果" || npcId == "女孩" ? 1 : 0);
			string[] layers = new string[backgrounds.Length + foregrounds.Length];
			Array.Copy(backgrounds, 0, layers, 0, backgrounds.Length);
			Array.Copy(foregrounds, 0, layers, backgrounds.Length, foregrounds.Length);
			string key = string.Join("|", layers);
			EnsureSceneLayerImages(layers.Length);
			for (int i = 0; i < m_characterSlots.Count; i++)
			{
				if (m_characterSlots[i] != null) EnsureCharacterSlot(i);
			}
			if (key == m_loadedSceneKey)
			{
				PlaceSceneCharacterBetweenLayers(backgrounds.Length, layers.Length);
				return;
			}

			string previousKey = m_loadedSceneKey;
			bool firstLoad = string.IsNullOrWhiteSpace(previousKey);
			// 开场过场卡已经把画面盖黑：直接换图，不再叠一层颜色过渡。
			bool coveredByTitleCard = m_titleCardRoot != null;
			bool skipTransition = firstLoad || coveredByTitleCard;
			// 店铺之间（旧↔新）用亮白过渡，其余场景用渐黑过渡。
			bool useBrightness = !skipTransition && IsShopSceneKey(previousKey) && IsShopSceneKey(key);
			m_loadedSceneKey = key;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.SceneSwitch);
			int version = ++m_sceneBackgroundVersion;

			if (!skipTransition)
			{
				await FadeSceneTransitionAsync(useBrightness ? Color.white : Color.black, 0f, 1f,
					SceneTransitionDuration, version);
				if (IsDestroyed || version != m_sceneBackgroundVersion)
				{
					return;
				}
			}

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
					loaded.enabled = loaded.sprite != null;
				}, token);
			}
			PlaceSceneCharacterBetweenLayers(backgrounds.Length, layers.Length);

			if (!skipTransition)
			{
				// 给新图一点加载时间再从纯色退回，避免淡出时露出空白。
				await UniTask.Delay(TimeSpan.FromSeconds(SceneLayerReadyDelay), DelayType.UnscaledDeltaTime,
					cancellationToken: token);
				if (IsDestroyed || version != m_sceneBackgroundVersion)
				{
					return;
				}

				await FadeSceneTransitionAsync(useBrightness ? Color.white : Color.black, 1f, 0f,
					SceneTransitionDuration, version);
			}
		}

		/// <summary>场景切换过渡的单项时长（秒）。线性 alpha，不走缓动。</summary>
		private const float SceneTransitionDuration = 0.55f;

		/// <summary>换图后停留多久再从纯色退回（等贴图加载）。</summary>
		private const float SceneLayerReadyDelay = 0.2f;

		private static bool IsShopSceneKey(string key)
		{
			return !string.IsNullOrEmpty(key) && key.IndexOf("scene_shop_", StringComparison.Ordinal) >= 0;
		}

		/// <summary>整屏纯色过渡层，插在 BG 之后，盖住场景与角色但不盖 UI。</summary>
		private CanvasGroup EnsureSceneTransitionLayer()
		{
			if (m_cgSceneTransition != null && m_imgSceneTransition != null)
			{
				return m_cgSceneTransition;
			}

			Transform bg = m_imgSceneRoot != null ? m_imgSceneRoot.transform : transform;
			Transform parent = bg.parent != null ? bg.parent : transform;
			var go = new GameObject("SceneTransition", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			m_imgSceneTransition = go.GetComponent<Image>();
			m_imgSceneTransition.raycastTarget = false;
			m_sceneTransitionActive = false;
			m_cgSceneTransition = go.GetComponent<CanvasGroup>();
			m_cgSceneTransition.alpha = 0f;
			m_cgSceneTransition.blocksRaycasts = false;
			m_cgSceneTransition.interactable = false;
			go.transform.SetSiblingIndex(Mathf.Min(bg.GetSiblingIndex() + 1, parent.childCount - 1));
			return m_cgSceneTransition;
		}

		/// <summary>把过渡层 alpha 从 from 线性推到 to。version 变化即中止（被新一次切场景顶掉）。</summary>
		private async UniTask FadeSceneTransitionAsync(Color tint, float from, float to, float duration, int version)
		{
			CanvasGroup group = EnsureSceneTransitionLayer();
			if (group == null || m_imgSceneTransition == null)
			{
				return;
			}

			m_imgSceneTransition.color = new Color(tint.r, tint.g, tint.b, 1f);
			group.alpha = from;
			m_sceneTransitionActive = true;
			float elapsed = 0f;
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			while (elapsed < duration)
			{
				if (IsDestroyed || version != m_sceneBackgroundVersion)
				{
					break;
				}

				elapsed += Time.unscaledDeltaTime;
				group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
				await UniTask.Yield(PlayerLoopTiming.Update, token);
			}

			group.alpha = to;
			m_sceneTransitionActive = false;
		}

		/// <summary>
		/// 每幕开场的过场卡：整屏黑底 + 当天日期 + 地点，淡入后停在黑场。
		/// 场景由 RefreshSceneBackgroundAsync 在黑场下换好，再调 RevealSessionTitleCardAsync 淡出。
		/// titleDate 为空的 Session（如 S04 与 S03 同一天）不播。
		/// </summary>
		private async UniTask PlaySessionTitleCardAsync(NarrativeSessionConfig session)
		{
			if (session == null || string.IsNullOrWhiteSpace(session.TitleDate) || IsDestroyed)
			{
				return;
			}

			DestroySessionTitleCard();
			var go = new GameObject("SessionTitleCard", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(transform, false);
			go.transform.SetAsLastSibling();
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			Image backdrop = go.GetComponent<Image>();
			backdrop.color = Color.black;
			backdrop.raycastTarget = true;
			m_titleCardRoot = go;
			m_cgTitleCard = go.GetComponent<CanvasGroup>();
			m_cgTitleCard.alpha = 0f;

			TextMeshProUGUI dateText = CreateOverlayText(go.transform, session.TitleDate, 46f,
				new Vector2(0f, 34f), new Vector2(1400f, 84f), TextAlignmentOptions.Center);
			dateText.enableWordWrapping = false;
			dateText.color = new Color(0.94f, 0.90f, 0.82f, 1f);
			if (!string.IsNullOrWhiteSpace(session.TitleLocation))
			{
				TextMeshProUGUI locationText = CreateOverlayText(go.transform, session.TitleLocation, 28f,
					new Vector2(0f, -50f), new Vector2(1400f, 56f), TextAlignmentOptions.Center);
				locationText.enableWordWrapping = false;
				locationText.color = new Color(0.70f, 0.66f, 0.58f, 1f);
			}

			SoundConfigMgr.Instance.PlaySfx(SfxNames.SceneSwitch);
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			const float fadeIn = 0.5f;
			float elapsed = 0f;
			while (elapsed < fadeIn)
			{
				if (IsDestroyed || m_cgTitleCard == null)
				{
					return;
				}

				elapsed += Time.unscaledDeltaTime;
				m_cgTitleCard.alpha = Mathf.Clamp01(elapsed / fadeIn);
				await UniTask.Yield(PlayerLoopTiming.Update, token);
			}

			if (m_cgTitleCard != null)
			{
				m_cgTitleCard.alpha = 1f;
			}

			await UniTask.Delay(TimeSpan.FromSeconds(1.3f), DelayType.UnscaledDeltaTime, cancellationToken: token);
		}

		/// <summary>场景换好后淡出过场卡；没有过场卡时立即返回。</summary>
		private async UniTask RevealSessionTitleCardAsync()
		{
			if (m_titleCardRoot == null || m_cgTitleCard == null)
			{
				return;
			}

			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			const float fadeOut = 0.7f;
			float elapsed = 0f;
			while (elapsed < fadeOut)
			{
				if (IsDestroyed || m_cgTitleCard == null)
				{
					return;
				}

				elapsed += Time.unscaledDeltaTime;
				m_cgTitleCard.alpha = 1f - Mathf.Clamp01(elapsed / fadeOut);
				await UniTask.Yield(PlayerLoopTiming.Update, token);
			}

			DestroySessionTitleCard();
		}

		private void DestroySessionTitleCard()
		{
			if (m_titleCardRoot != null)
			{
				UnityEngine.Object.Destroy(m_titleCardRoot);
			}

			m_titleCardRoot = null;
			m_cgTitleCard = null;
		}


		/// <summary>
		/// 把每个在场角色按自己的图层插进场景层之间。同层多人时按槽位顺序叠，
		/// 后面的槽压在前面的槽上，保证三人同框时左右关系稳定。
		/// </summary>
		private void PlaceSceneCharacterBetweenLayers(int backgroundCount, int layerCount)
		{
			if (m_characterSlots.Count == 0) return;
			int foregroundCount = layerCount - backgroundCount;
			for (int i = 0; i <= layerCount; i++)
			{
				// 先插中间那位（index 2），左右两人后插，于是三人重叠时中间那个在最后面
				for (int pass = 0; pass < 2; pass++)
				{
					for (int s = 0; s < m_characterSlots.Count; s++)
					{
						bool middle = s >= 2;
						if ((pass == 0) != middle) continue;
						SceneCharacterSlot slot = m_characterSlots[s];
						if (slot == null || slot.Rect == null || !slot.Rect.gameObject.activeSelf) continue;
						int at = CharacterInsertIndex(backgroundCount, foregroundCount,
							ReadCharacterLayer(GetCharacterSlotLayoutPart(slot)));
						if (at == i) slot.Rect.SetAsLastSibling();
					}
				}
				if (i < layerCount && m_sceneLayerImages[i] != null)
					m_sceneLayerImages[i].transform.SetAsLastSibling();
			}
		}

		private static int CharacterInsertIndex(int backgroundCount, int foregroundCount, int layer)
		{
			// layer 1 = 「第 1 层前景在人物身后」，不是「一半前景」。
			// 陈果房间：椅子在身后，桌面 / 猫玩偶 / 台历都盖在人物前面。
			// 别用 foregroundCount / 2 —— 前景一层层往上加（比如后来拆出台历），
			// 除出来的插入点会跟着漂，人物会突然跑到桌子前面。
			return backgroundCount + (layer == 0 ? 0 :
				layer == 1 ? Mathf.Min(1, foregroundCount) : foregroundCount);
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
				case "scene_chenguo_calendar": bounds = new Vector4(1825f, 1673f, 582f, 447f); break;
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
				// 仓库：坐标来自 docs/场景搭建与配表接入.md §2.5（SIFT + 遮挡感知搜索得出，整图回归误差 0.19）
				case "scene_warehouse_city": bounds = new Vector4(139f, 0f, 3701f, 1904f); break;
				case "scene_warehouse_wall": bounds = new Vector4(0f, 0f, 1891f, 2160f); break;
				case "scene_warehouse_wallclock": bounds = new Vector4(527f, 143f, 480f, 603f); break;
				case "scene_warehouse_floorclock": bounds = new Vector4(3461f, 546f, 336f, 1107f); break;
				case "scene_warehouse_boxes": bounds = new Vector4(3496f, 720f, 340f, 1128f); break;
				case "scene_warehouse_chair": bounds = new Vector4(1656f, 1476f, 924f, 413f); break;
				case "scene_warehouse_desk": bounds = new Vector4(0f, 242f, 3840f, 1918f); break;
				case "scene_warehouse_alarmclock": bounds = new Vector4(1336f, 1467f, 373f, 482f); break;
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
				// 新 Image 没有 sprite 时 Unity 画的是整块白色，先关掉，等贴图回来再开
				image.enabled = false;
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
				// 旧店铺 + 完好收音机：收音机已修好的幕（第六幕录音段）用，避免画面里摆着坏收音机
				case "scene_shop_old_fixed":
					backgrounds = new[] { "scene_shop_old_window", "scene_shop_old_bg" };
					foregrounds = new[]
					{
						"scene_shop_old_plant", "scene_shop_new_radio", "scene_shop_old_desk", "scene_shop_old_items"
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
					// 前景自下而上：椅子（在人物身后）→ 桌面 → 台历 → 猫玩偶（最上）。
					// 台历和猫玩偶都是「摆在桌子上的东西」，必须压住桌面；猫再压台历一层，
					// 免得以后两者挪到重叠时被台历切一刀。
					foregrounds = new[]
					{
						"scene_chenguo_chair", "scene_chenguo_desk", "scene_chenguo_calendar", "scene_chenguo_cat"
					};
					return true;
				case "scene_father":
					backgrounds = new[] { "scene_father_bg" };
					foregrounds = new[] { "scene_father_fg" };
					return true;
				// 仓库：前 6 层在人物后面，桌面与桌上闹钟盖住人物（人站在桌子后）。
				// 对应 docs 的 characterLayerIndex=6，这里靠「6 个 background + 2 个 foreground」表达，
				// 所以 ResolveSceneCharacterLayer 返回 0 就正好插在第 6 层之后。
				case "scene_warehouse":
					backgrounds = new[]
					{
						"scene_warehouse_city", "scene_warehouse_wall", "scene_warehouse_wallclock",
						"scene_warehouse_floorclock", "scene_warehouse_boxes", "scene_warehouse_chair"
					};
					foregrounds = new[] { "scene_warehouse_desk", "scene_warehouse_alarmclock" };
					return true;
				default:
					backgrounds = Array.Empty<string>();
					foregrounds = Array.Empty<string>();
					return false;
			}
		}

		/// <summary>
		/// 人物在场景里的插入位置：
		/// 0 = 所有前景都在人物前面（人站在柜台 / 书桌后面，腿被挡住）；
		/// 1 = 第 1 层前景在人物身后、其余盖住人物（陈果房间：椅子在身后，书桌 / 猫 / 台历在身前）；
		/// 2 = 人物压在所有前景上面（一般不用）。
		/// </summary>
		private static int ResolveSceneCharacterLayer(string backgroundId)
		{
			switch (backgroundId)
			{
				case "scene_chenguo":
					return 1;
				case "scene_shop_old":
				case "scene_shop_old_fixed":
				case "scene_shop_new":
				case "scene_zhou":
				case "scene_father":
				// 仓库把「人物之前」的 6 层都放进了 backgrounds，这里 0 就等价于「插在第 6 层之后」。
				case "scene_warehouse":
					return 0;
				default:
					return 0;
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
				return new[] { "scene_chenguo_chair", "scene_chenguo_desk", "scene_chenguo_calendar", "scene_chenguo_cat" };
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
			if (m_cutscenePlaying || m_sceneTransitionActive ||
				(m_overlay != null && m_overlay.activeInHierarchy))
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
			// 重开时如果正卡在场景探索里，这一步是唯一能把那层轮询 await 收掉的地方；
			// 不写的话热点根节点会一直压在最上层，新一局点什么都被它吃掉。
			AbortSceneSearch();
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
