using Cysharp.Threading.Tasks;
using GameProto;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 结局结语卡：画面渐暗 → 黑场 → 出字。
	///
	/// 用在正片最后那一句独白上（配表 turnType=EndingCard，或正式结局 Session 的收尾玩家独白）。
	/// 它不走对话气泡、不上 HUD：整块游戏画面被黑幕慢慢吃掉，只留一句台词定格在屏幕中心。
	/// 点一次 = 直接落到「字已完全出现」的那一帧再短暂停留，不让玩家被整段仪式卡住。
	/// </summary>
	public partial class BattleMainUI
	{
		/// <summary>配表 turnType 取这个值，表示这一节点是结语卡而不是一句对白。</summary>
		private const string EndingCardTurnType = "EndingCard";

		// ---- 节拍（秒，全部走 unscaled）----
		/// <summary>画面渐暗到全黑。</summary>
		private const float EndingDimSeconds = 1.6f;
		/// <summary>黑场留白：让上一句「毕竟……」的余味先落地，再出字。</summary>
		private const float EndingBlackHoldSeconds = 0.5f;
		/// <summary>出字。</summary>
		private const float EndingTextFadeInSeconds = 1.25f;
		/// <summary>定格。</summary>
		private const float EndingTextHoldSeconds = 2.6f;
		/// <summary>收黑。</summary>
		private const float EndingTextFadeOutSeconds = 0.9f;
		/// <summary>跳过后的定格时长。</summary>
		private const float EndingSkipHoldSeconds = 0.55f;
		private const float EndingTailSeconds = 0.12f;

		// ---- 版式 ----
		private const float EndingTextFontSize = 68f;
		private const float EndingTextWidth = 1500f;
		private const float EndingTextHeight = 180f;
		/// <summary>出字时从下往上浮起的距离。只给一点点，多了就成了弹窗动画。</summary>
		private const float EndingTextRise = 10f;
		/// <summary>字距从张开收到正常：电影标题的「落定」感，靠这一下点出来。</summary>
		private const float EndingTextSpacingFrom = 12f;
		private const float EndingTextSpacingTo = 0.5f;
		private static readonly Color EndingTextColor = new Color(0.96f, 0.94f, 0.89f, 1f);

		// ---- 声音 ----
		/// <summary>渐暗期间 BGM 压到这个比例：留一点底噪，彻底静音反而像掉线。</summary>
		private const float EndingBgmLevel = 0.3f;

		private sealed class EndingCardView
		{
			public GameObject Root;
			public CanvasGroup BackdropGroup;
			public CanvasGroup TextGroup;
			public RectTransform TextRect;
			public TextMeshProUGUI Text;
		}

		private EndingCardView m_endingCardView;
		private int m_endingCardVersion;
		private bool m_endingCardPlaying;
		private bool m_endingCardSkipRequested;

		/// <summary>
		/// 是否走结语卡。优先认配表 turnType=EndingCard；
		/// 没配的话，正式结局 Session 的收尾独白也按结语卡处理 —— 正片最后一句本来就不该以气泡收尾。
		/// </summary>
		private bool IsEndingCardTurn(StoryNodeConfig story)
		{
			if (story == null || string.IsNullOrWhiteSpace(story.NpcText))
			{
				return false;
			}

			string turnType = (story.TurnType ?? string.Empty).Trim();
			if (turnType.Equals(EndingCardTurnType, System.StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (!story.IsSessionEnd || IsNpcDialogueTurn(story))
			{
				return false;
			}

			NarrativeSessionConfig session =
				NarrativeConfigMgr.Instance.GetSession(NarrativeSaveData.Get.CurrentSessionId);
			return session != null && session.IsFormalEnding;
		}

		private async UniTask PlayEndingCardAsync(string text)
		{
			if (IsDestroyed || string.IsNullOrWhiteSpace(text))
			{
				return;
			}

			DestroyEndingCard();
			int version = ++m_endingCardVersion;
			m_endingCardPlaying = true;
			m_endingCardSkipRequested = false;

			EndingCardView view = BuildEndingCard(text);

			// 时间轴：渐暗 → 黑场留白 → 出字 → 定格 → 收黑。
			float textStart = EndingDimSeconds + EndingBlackHoldSeconds;
			float textFormed = textStart + EndingTextFadeInSeconds;
			float fadeOutStart = textFormed + EndingTextHoldSeconds;
			float total = fadeOutStart + EndingTextFadeOutSeconds + EndingTailSeconds;

			float musicVolumeBefore = GameModule.AudioModule.MusicVolume;
			bool duckBgm = musicVolumeBefore > 0.01f;
			float elapsed = 0f;
			try
			{
				while (elapsed < total)
				{
					if (IsDestroyed || version != m_endingCardVersion)
					{
						return;
					}

					// 跳过 = 把时间轴直接推到「字已定格」，再短暂停留收黑。
					// 跳过时把 fadeOutStart 一起往后挪，否则推算出来的收黑时刻会落在过去，画面立刻黑掉。
					if (m_endingCardSkipRequested)
					{
						m_endingCardSkipRequested = false;
						if (elapsed < textFormed + EndingSkipHoldSeconds)
						{
							elapsed = textFormed;
							fadeOutStart = textFormed + EndingSkipHoldSeconds;
							total = fadeOutStart + EndingTextFadeOutSeconds + EndingTailSeconds;
						}
					}

					if (duckBgm)
					{
						GameModule.AudioModule.MusicVolume =
							musicVolumeBefore * GetEndingBgmFactor(elapsed, fadeOutStart);
					}

					ApplyEndingCardFrame(view, elapsed, textStart, fadeOutStart);
					await UniTask.Yield(PlayerLoopTiming.Update);
					elapsed += Time.unscaledDeltaTime;
				}
			}
			finally
			{
				if (duckBgm)
				{
					GameModule.AudioModule.MusicVolume = musicVolumeBefore;
				}

				if (version == m_endingCardVersion)
				{
					DestroyEndingCard();
				}
			}
		}

		/// <summary>BGM：渐暗期从原音量压到 0.3，定格期保持，收黑期推到 0，交还给日终 BGM。</summary>
		private static float GetEndingBgmFactor(float t, float fadeOutStart)
		{
			if (t < EndingDimSeconds)
			{
				return Mathf.Lerp(1f, EndingBgmLevel, EaseInOut(t / EndingDimSeconds));
			}

			if (t <= fadeOutStart)
			{
				return EndingBgmLevel;
			}

			float fade = Mathf.Clamp01((t - fadeOutStart) / EndingTextFadeOutSeconds);
			return Mathf.Lerp(EndingBgmLevel, 0f, EaseInOut(fade));
		}

		/// <summary>按时间 t 把这一帧全部算出来。整段只有一个循环，不做串联 await。</summary>
		private static void ApplyEndingCardFrame(EndingCardView view, float t, float textStart, float fadeOutStart)
		{
			if (view == null)
			{
				return;
			}

			// 渐暗：黑幕吃掉整个画面（连同对白 HUD 和人物，它们在黑幕下面）。
			float dim = Mathf.Clamp01(t / EndingDimSeconds);
			SetGroup(view.BackdropGroup, EaseInOut(dim));

			// 出字：淡入 + 上浮 + 字距收拢。
			float appear = Mathf.Clamp01((t - textStart) / EndingTextFadeInSeconds);
			float easedAppear = EaseOutCubic(appear);
			if (view.Text != null)
			{
				view.Text.characterSpacing =
					Mathf.Lerp(EndingTextSpacingFrom, EndingTextSpacingTo, easedAppear);
			}

			if (view.TextRect != null)
			{
				view.TextRect.anchoredPosition =
					new Vector2(0f, (1f - easedAppear) * -EndingTextRise);
			}

			// 收黑：字先走，黑幕留着 —— 后面的日终反思页自带黑底开场，不会闪回游戏画面。
			float fade = t <= fadeOutStart
				? 0f
				: Mathf.Clamp01((t - fadeOutStart) / EndingTextFadeOutSeconds);
			SetGroup(view.TextGroup, appear * (1f - EaseInOut(fade)));
		}

		private EndingCardView BuildEndingCard(string text)
		{
			// 根节点只负责兜住指针；黑幕和字各占一层，宽度/透明度互不牵连。
			var root = new GameObject("EndingCard", typeof(RectTransform), typeof(CanvasGroup));
			root.transform.SetParent(transform, false);
			root.transform.SetAsLastSibling();
			StretchFullScreen(root.GetComponent<RectTransform>());
			CanvasGroup rootGroup = root.GetComponent<CanvasGroup>();
			rootGroup.alpha = 1f;
			rootGroup.blocksRaycasts = true;
			rootGroup.interactable = true;

			var backdropObject = new GameObject("Backdrop", typeof(RectTransform), typeof(Image),
				typeof(CanvasGroup), typeof(Button));
			backdropObject.transform.SetParent(root.transform, false);
			RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
			StretchFullScreen(backdropRect);
			Image backdropImage = backdropObject.GetComponent<Image>();
			backdropImage.color = Color.black;
			backdropImage.raycastTarget = true;
			CanvasGroup backdropGroup = backdropObject.GetComponent<CanvasGroup>();
			backdropGroup.alpha = 0f;
			// 黑幕透明期间也要吃指针：否则点空会漏到下面的对白推进层。
			backdropGroup.blocksRaycasts = true;

			Button skip = backdropObject.GetComponent<Button>();
			skip.transition = Selectable.Transition.None;
			skip.targetGraphic = backdropImage;
			skip.onClick.AddListener(OnClickEndingCard);

			TextMeshProUGUI label = CreateOverlayText(root.transform, text, EndingTextFontSize,
				Vector2.zero, new Vector2(EndingTextWidth, EndingTextHeight), TextAlignmentOptions.Center);
			label.enableWordWrapping = false;
			label.overflowMode = TextOverflowModes.Overflow;
			label.color = EndingTextColor;
			label.characterSpacing = EndingTextSpacingFrom;
			CanvasGroup textGroup = label.gameObject.AddComponent<CanvasGroup>();
			textGroup.alpha = 0f;
			textGroup.blocksRaycasts = false;

			var view = new EndingCardView
			{
				Root = root,
				BackdropGroup = backdropGroup,
				TextGroup = textGroup,
				TextRect = label.rectTransform,
				Text = label,
			};

			// 起始帧：全透明，避免第一帧闪一下全黑或闪出字。
			ApplyEndingCardFrame(view, 0f, EndingDimSeconds + EndingBlackHoldSeconds,
				EndingDimSeconds + EndingBlackHoldSeconds + EndingTextFadeInSeconds + EndingTextHoldSeconds);

			m_endingCardView = view;
			return view;
		}

		private void OnClickEndingCard()
		{
			if (m_endingCardPlaying)
			{
				m_endingCardSkipRequested = true;
			}
		}

		private void DestroyEndingCard()
		{
			m_endingCardVersion++;
			m_endingCardPlaying = false;
			m_endingCardSkipRequested = false;
			if (m_endingCardView != null && m_endingCardView.Root != null)
			{
				UnityEngine.Object.Destroy(m_endingCardView.Root);
			}

			m_endingCardView = null;
		}
	}
}
