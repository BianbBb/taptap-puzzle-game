using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DGame;
using GameProto;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 记忆涌现过场：黑底 + 左记忆图 / 中情绪圆 / 右记忆图。
	/// 三段节拍：记忆浮现 → 两卡各飞出同色光点汇聚成圆 → 情绪圆呼吸成形并浮出情绪名。
	/// 一次结算涌出多个情绪时按入队顺序逐条播放；整屏可点击跳过当前这一条。
	/// </summary>
	public partial class BattleMainUI
	{
		// ---- 版式 ----
		private const float EmergeCardSize = 360f;
		private const float EmergeCardOffsetX = 420f;
		private const float EmergeCardRise = 16f;
		private const float EmergeCardStartScale = 1.04f;
		private const float EmergeCircleSize = 280f;
		private const float EmergeRingSize = 280f;
		private const float EmergeHaloSize = 1600f;
		private const float EmergeDotSize = 14f;
		private const float EmergeDotStartX = 248f;
		private const float EmergeDotControlX = 150f;
		private const float EmergeDotArcHeight = 150f;
		private const float EmergeMemoryNameFontSize = 28f;
		private const float EmergeEmotionNameFontSize = 36f;

		// ---- 节拍（秒，全部走 unscaled）----
		private const float EmergeMemoryStart = 0.30f;
		private const float EmergeMemoryStagger = 0.18f;
		private const float EmergeMemoryFadeSeconds = 0.55f;
		private const float EmergeDotAppearStart = 1.00f;
		private const float EmergeDotAppearSeconds = 0.35f;
		private const float EmergeMergeStart = 1.60f;
		private const float EmergeMergeSeconds = 0.90f;
		private const float EmergeRingStart = 2.50f;
		private const float EmergeRingSeconds = 0.40f;
		private const float EmergeCircleStart = 2.60f;
		private const float EmergeCircleSeconds = 0.80f;
		private const float EmergeNameStart = 3.20f;
		private const float EmergeNameSeconds = 0.60f;
		private const float EmergeHoldStart = 3.80f;
		private const float EmergeFadeOutStart = 5.40f;
		private const float EmergeFadeOutSeconds = 0.50f;
		private const float EmergeTailSeconds = 0.30f;
		private const float EmergeSkipHoldSeconds = 0.45f;
		private const float EmergeBreathPeriod = 1.00f;
		private const float EmergeBreathScale = 0.03f;
		private const float EmergeHaloAlpha = 0.12f;

		// ---- 声音 ----
		/// <summary>光点开始汇聚：记忆飞向中心。</summary>
		private const float EmergeMergeCueAt = EmergeMergeStart;
		/// <summary>光点相撞落地。</summary>
		private const float EmergeLandCueAt = EmergeRingStart;
		/// <summary>情绪圆成形后浮出的那一刻，才是配表里那条「涌现」铃音。</summary>
		private const float EmergeChimeCueAt = 2.90f;
		private const float EmergeBgmDuckFactor = 0.4f;
		private const float EmergeBgmDuckSeconds = 0.35f;
		private const float EmergeBgmRestoreSeconds = 0.60f;

		private static readonly Color EmergeMemoryNameColor = new Color(1f, 1f, 1f, 0.55f);
		private static readonly Color EmergeDarkTextColor = new Color(0.13f, 0.12f, 0.12f, 0.88f);
		private static readonly Color EmergeLightTextColor = new Color(0.97f, 0.97f, 0.97f, 0.95f);

		/// <summary>运行时生成的白圆，避免把小尺寸的 dialogue_colour_btn 放大到 280 后发虚。</summary>
		private static Sprite s_emergeCircleSprite;
		/// <summary>运行时生成的柔边光晕，给纯黑底加一点「暖黑」。</summary>
		private static Sprite s_emergeHaloSprite;
		/// <summary>运行时生成的空心圆环，用作光点相撞的涟漪。</summary>
		private static Sprite s_emergeRingSprite;

		private readonly List<string> m_pendingEmergeEmotionIds = new List<string>();
		private GameObject m_emergeCutsceneRoot;
		private int m_emergeCutsceneVersion;
		private bool m_cutscenePlaying;
		private bool m_cutsceneSkipRequested;

		/// <summary>过场里需要逐帧驱动的节点，集中放一处，避免十几个散落的字段。</summary>
		private sealed class EmergeCutsceneView
		{
			public GameObject Root;
			public CanvasGroup RootGroup;
			public CanvasGroup HaloGroup;
			public RectTransform CardLeft;
			public CanvasGroup CardLeftGroup;
			public RectTransform CardRight;
			public CanvasGroup CardRightGroup;
			public RectTransform DotLeft;
			public CanvasGroup DotLeftGroup;
			public RectTransform DotRight;
			public CanvasGroup DotRightGroup;
			public RectTransform Ring;
			public CanvasGroup RingGroup;
			public RectTransform Circle;
			public CanvasGroup CircleGroup;
			public RectTransform NameRect;
			public CanvasGroup NameGroup;
		}

		/// <summary>情绪涌现事件只入队；真正播放由 CommitTurn / 物品调查的调用方 await，避免和下一句对白并发。</summary>
		private void EnqueueEmergeCutscene(string emotionId)
		{
			if (string.IsNullOrWhiteSpace(emotionId) || m_pendingEmergeEmotionIds.Contains(emotionId))
			{
				return;
			}

			m_pendingEmergeEmotionIds.Add(emotionId);
		}

		/// <summary>逐条播放待播的涌现过场：两条情绪就先后播两次，全程保持黑底，不闪回游戏画面。</summary>
		private async UniTask PlayPendingEmergeCutscenesAsync()
		{
			if (m_pendingEmergeEmotionIds.Count == 0)
			{
				return;
			}

			var pending = new List<string>(m_pendingEmergeEmotionIds);
			m_pendingEmergeEmotionIds.Clear();
			for (int i = 0; i < pending.Count; i++)
			{
				if (IsDestroyed)
				{
					return;
				}

				await PlayEmergenceCutsceneAsync(pending[i]);
			}
		}

		private async UniTask PlayEmergenceCutsceneAsync(string emotionId)
		{
			EmotionConfig emotion = NarrativeConfigMgr.Instance.GetEmotion(emotionId);
			if (emotion == null)
			{
				return;
			}

			DestroyEmergenceCutscene();

			int version = ++m_emergeCutsceneVersion;
			m_cutscenePlaying = true;
			m_cutsceneSkipRequested = false;

			// 来源按获得顺序：最早那段放左边，最新那段（本次刚拿到的）放右边。
			IReadOnlyList<string> sources = m_narrativeService.GetMemorySourcesForEmotion(emotionId);
			string earlierMemoryId = sources.Count > 0 ? sources[0] : null;
			string newerMemoryId = sources.Count > 1 ? sources[sources.Count - 1] : null;

			EmergeCutsceneView view = BuildEmergenceCutscene(emotion, earlierMemoryId, newerMemoryId);
			float musicVolumeBefore = GameModule.AudioModule.MusicVolume;
			bool duckBgm = musicVolumeBefore > 0.01f;
			float fadeStart = EmergeFadeOutStart;
			float total = fadeStart + EmergeFadeOutSeconds + EmergeTailSeconds;
			bool mergeCuePlayed = false;
			bool landCuePlayed = false;
			bool chimeCuePlayed = false;
			float elapsed = 0f;
			try
			{
				while (elapsed < total)
				{
					if (!IsCutsceneAlive(version))
					{
						return;
					}

					// 跳过 = 直接落到「情绪名已出现」的那一帧，再短暂停留收尾。
					if (m_cutsceneSkipRequested)
					{
						m_cutsceneSkipRequested = false;
						if (elapsed < EmergeHoldStart)
						{
							elapsed = EmergeHoldStart;
							fadeStart = elapsed + EmergeSkipHoldSeconds;
							total = fadeStart + EmergeFadeOutSeconds + EmergeTailSeconds;
						}
					}

					if (!mergeCuePlayed && elapsed >= EmergeMergeCueAt)
					{
						mergeCuePlayed = true;
						SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryGet);
					}

					if (!landCuePlayed && elapsed >= EmergeLandCueAt)
					{
						landCuePlayed = true;
						SoundConfigMgr.Instance.PlaySfx(SfxNames.MemoryLand);
					}

					if (!chimeCuePlayed && elapsed >= EmergeChimeCueAt)
					{
						chimeCuePlayed = true;
						SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryEmotionEmerge);
					}

					if (duckBgm)
					{
						GameModule.AudioModule.MusicVolume =
							musicVolumeBefore * GetBgmDuckFactor(elapsed, fadeStart);
					}

					ApplyEmergeCutsceneFrame(view, elapsed, fadeStart);
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

				if (version == m_emergeCutsceneVersion)
				{
					DestroyEmergenceCutscene();
				}
			}
		}

		private static float GetBgmDuckFactor(float elapsed, float fadeStart)
		{
			if (elapsed < fadeStart)
			{
				float duckIn = Mathf.Clamp01(elapsed / EmergeBgmDuckSeconds);
				return Mathf.Lerp(1f, EmergeBgmDuckFactor, EaseOutCubic(duckIn));
			}

			float restore = Mathf.Clamp01((elapsed - fadeStart) / EmergeBgmRestoreSeconds);
			return Mathf.Lerp(EmergeBgmDuckFactor, 1f, EaseInOut(restore));
		}

		/// <summary>按时间 t 把每一帧的所有属性算出来。整条时间线只有一个循环，不做串联 await。</summary>
		private static void ApplyEmergeCutsceneFrame(EmergeCutsceneView view, float t, float fadeStart)
		{
			ApplyCardFrame(view.CardLeft, view.CardLeftGroup, -EmergeCardOffsetX, t, EmergeMemoryStart);
			ApplyCardFrame(view.CardRight, view.CardRightGroup, EmergeCardOffsetX, t,
				EmergeMemoryStart + EmergeMemoryStagger);

			// 光点：先亮起，再沿弧线汇到中心。
			float appear = Mathf.Clamp01((t - EmergeDotAppearStart) / EmergeDotAppearSeconds);
			float merge = Mathf.Clamp01((t - EmergeMergeStart) / EmergeMergeSeconds);
			bool merging = t >= EmergeMergeStart;
			view.DotLeft.anchoredPosition = merging
				? QuadraticBezier(new Vector2(-EmergeDotStartX, 0f),
					new Vector2(-EmergeDotControlX, EmergeDotArcHeight), Vector2.zero, merge)
				: new Vector2(-EmergeDotStartX, 0f);
			view.DotRight.anchoredPosition = merging
				? QuadraticBezier(new Vector2(EmergeDotStartX, 0f),
					new Vector2(EmergeDotControlX, EmergeDotArcHeight), Vector2.zero, merge)
				: new Vector2(EmergeDotStartX, 0f);
			float dotAlpha = merging ? 1f - merge : appear;
			SetGroup(view.DotLeftGroup, dotAlpha);
			SetGroup(view.DotRightGroup, dotAlpha);
			float dotScale = Mathf.Lerp(0.7f, 1f, appear) * (merging ? Mathf.Lerp(1f, 0.7f, merge) : 1f);
			view.DotLeft.localScale = Vector3.one * dotScale;
			view.DotRight.localScale = Vector3.one * dotScale;

			// 涟漪：只在相撞那一下扩一圈。
			float ring = Mathf.Clamp01((t - EmergeRingStart) / EmergeRingSeconds);
			view.Ring.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.9f, ring);
			SetGroup(view.RingGroup, ring <= 0f ? 0f : 0.55f * (1f - ring));

			// 情绪圆：0.2 倍弹到 1.06 再回 1.0，随后缓慢呼吸。
			float circle = Mathf.Clamp01((t - EmergeCircleStart) / EmergeCircleSeconds);
			float circleScale;
			if (circle <= 0f)
			{
				circleScale = 0.2f;
			}
			else if (circle < 0.62f)
			{
				circleScale = Mathf.Lerp(0.2f, 1.06f, EaseOutCubic(circle / 0.62f));
			}
			else
			{
				circleScale = Mathf.Lerp(1.06f, 1f, (circle - 0.62f) / 0.38f);
			}

			float formedAt = EmergeCircleStart + EmergeCircleSeconds;
			if (t > formedAt)
			{
				float phase = (t - formedAt) / EmergeBreathPeriod;
				circleScale *= 1f + EmergeBreathScale * (0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f));
			}

			view.Circle.localScale = Vector3.one * circleScale;
			SetGroup(view.CircleGroup, Mathf.Clamp01(circle * 1.4f));

			// 暖黑：情绪色从圆心渗开，把纯黑染上一度温度。
			float halo = Mathf.Clamp01((t - EmergeCircleStart) / 1f);
			SetGroup(view.HaloGroup, EmergeHaloAlpha * EaseOutCubic(halo));

			// 情绪名比圆晚一点出现，像「先感觉到，再认出它叫什么」。
			float name = Mathf.Clamp01((t - EmergeNameStart) / EmergeNameSeconds);
			SetGroup(view.NameGroup, name);
			view.NameRect.anchoredPosition = new Vector2(0f, (1f - name) * 8f);

			// 整屏收尾。
			float fade = t <= fadeStart ? 0f : Mathf.Clamp01((t - fadeStart) / EmergeFadeOutSeconds);
			SetGroup(view.RootGroup, 1f - fade);
		}

		private static void ApplyCardFrame(RectTransform rect, CanvasGroup group, float offsetX, float t, float start)
		{
			float progress = Mathf.Clamp01((t - start) / EmergeMemoryFadeSeconds);
			float eased = EaseOutCubic(progress);
			SetGroup(group, eased);
			rect.anchoredPosition = new Vector2(offsetX, (1f - eased) * EmergeCardRise);
			rect.localScale = Vector3.one * Mathf.Lerp(EmergeCardStartScale, 1f, eased);
		}

		private static void SetGroup(CanvasGroup group, float alpha)
		{
			if (group != null)
			{
				group.alpha = alpha;
			}
		}

		private static Vector2 QuadraticBezier(Vector2 start, Vector2 control, Vector2 end, float t)
		{
			float inverse = 1f - t;
			return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
		}

		private static float EaseOutCubic(float t)
			=> 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

		private static float EaseInOut(float t)
		{
			t = Mathf.Clamp01(t);
			return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
		}

		private bool IsCutsceneAlive(int version)
			=> !IsDestroyed && version == m_emergeCutsceneVersion && m_emergeCutsceneRoot != null;

		private EmergeCutsceneView BuildEmergenceCutscene(EmotionConfig emotion,
			string earlierMemoryId, string newerMemoryId)
		{
			var root = new GameObject("MemoryEmergeCutscene",
				typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(Button));
			root.transform.SetParent(transform, false);
			root.transform.SetAsLastSibling();
			StretchFullScreen(root.GetComponent<RectTransform>());

			Image backdrop = root.GetComponent<Image>();
			backdrop.color = Color.black;
			backdrop.raycastTarget = true;

			CanvasGroup rootGroup = root.GetComponent<CanvasGroup>();
			rootGroup.alpha = 1f;
			rootGroup.blocksRaycasts = true;
			rootGroup.interactable = true;

			Button skip = root.GetComponent<Button>();
			skip.transition = Selectable.Transition.None;
			skip.targetGraphic = backdrop;
			skip.onClick.AddListener(OnClickEmergeCutscene);

			m_emergeCutsceneRoot = root;
			Color emotionColor = EmotionVisual.ResolveColor(emotion);

			var view = new EmergeCutsceneView
			{
				Root = root,
				RootGroup = rootGroup,
				HaloGroup = CreateCutsceneHalo(root.transform, emotionColor),
			};

			view.CardLeftGroup = CreateCutsceneMemoryCard(root.transform, -EmergeCardOffsetX,
				earlierMemoryId, out RectTransform cardLeft);
			view.CardLeft = cardLeft;
			view.CardRightGroup = CreateCutsceneMemoryCard(root.transform, EmergeCardOffsetX,
				newerMemoryId, out RectTransform cardRight);
			view.CardRight = cardRight;

			view.DotLeftGroup = CreateCutsceneDot(root.transform, emotionColor, out RectTransform dotLeft);
			view.DotLeft = dotLeft;
			view.DotRightGroup = CreateCutsceneDot(root.transform, emotionColor, out RectTransform dotRight);
			view.DotRight = dotRight;

			view.RingGroup = CreateCutsceneRing(root.transform, emotionColor, out RectTransform ring);
			view.Ring = ring;

			view.CircleGroup = CreateCutsceneCircle(root.transform, emotionColor, out RectTransform circle);
			view.Circle = circle;

			view.NameGroup = CreateCutsceneEmotionName(root.transform, emotion, emotionColor,
				out RectTransform nameRect);
			view.NameRect = nameRect;

			// 起始帧：除黑底外全部不可见，避免第一帧闪一下。
			ApplyEmergeCutsceneFrame(view, 0f, EmergeFadeOutStart);
			return view;
		}

		private void OnClickEmergeCutscene()
		{
			if (m_cutscenePlaying)
			{
				m_cutsceneSkipRequested = true;
			}
		}

		private static CanvasGroup CreateCutsceneHalo(Transform parent, Color color)
		{
			var go = new GameObject("Halo", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			CenterAt(rect, Vector2.zero, new Vector2(EmergeHaloSize, EmergeHaloSize));

			Image image = go.GetComponent<Image>();
			image.sprite = GetEmergeHaloSprite();
			image.color = color;
			image.raycastTarget = false;

			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			return group;
		}

		private CanvasGroup CreateCutsceneMemoryCard(Transform parent, float offsetX, string memoryId,
			out RectTransform cardRect)
		{
			var go = new GameObject("MemoryCard", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			cardRect = go.GetComponent<RectTransform>();
			CenterAt(cardRect, new Vector2(offsetX, 0f), new Vector2(EmergeCardSize, EmergeCardSize));

			Image image = go.GetComponent<Image>();
			image.preserveAspect = true;
			image.raycastTarget = false;

			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;

			MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(memoryId);
			string location = memory == null ? null : memory.ImageId;
			if (!string.IsNullOrWhiteSpace(location) && location != "placeholder" &&
				GameModule.ResourceModule.CheckLocationValid(location))
			{
				// 图还没加载完时保持全透明，避免渐显期间露出白块。
				image.color = new Color(1f, 1f, 1f, 0f);
				image.SetSprite(location, false, loaded =>
				{
					if (loaded == null || IsDestroyed)
					{
						return;
					}

					loaded.preserveAspect = true;
					loaded.color = Color.white;
				}, gameObject.GetCancellationTokenOnDestroy());
				return group;
			}

			// 缺图（M10 之后的记忆目前都没有 imageId）：不留白块，只把记忆名放在原位。
			image.color = new Color(1f, 1f, 1f, 0f);
			string label = memory == null ? memoryId : memory.MemoryName;
			if (string.IsNullOrWhiteSpace(label))
			{
				return group;
			}

			TextMeshProUGUI text = CreateOverlayText(go.transform, label, EmergeMemoryNameFontSize,
				Vector2.zero, new Vector2(EmergeCardSize, 96f), TextAlignmentOptions.Center);
			text.color = EmergeMemoryNameColor;
			text.enableWordWrapping = false;
			text.overflowMode = TextOverflowModes.Overflow;
			return group;
		}

		private static CanvasGroup CreateCutsceneDot(Transform parent, Color color, out RectTransform dotRect)
		{
			var go = new GameObject("EmotionDot", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			dotRect = go.GetComponent<RectTransform>();
			CenterAt(dotRect, Vector2.zero, new Vector2(EmergeDotSize, EmergeDotSize));

			Image image = go.GetComponent<Image>();
			image.sprite = GetEmergeCircleSprite();
			image.color = color;
			image.raycastTarget = false;

			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			return group;
		}

		private static CanvasGroup CreateCutsceneRing(Transform parent, Color color, out RectTransform ringRect)
		{
			var go = new GameObject("EmotionRing", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			ringRect = go.GetComponent<RectTransform>();
			CenterAt(ringRect, Vector2.zero, new Vector2(EmergeRingSize, EmergeRingSize));

			Image image = go.GetComponent<Image>();
			image.sprite = GetEmergeRingSprite();
			image.color = color;
			image.raycastTarget = false;

			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			return group;
		}

		private static CanvasGroup CreateCutsceneCircle(Transform parent, Color color, out RectTransform circleRect)
		{
			var go = new GameObject("EmotionCircle", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			circleRect = go.GetComponent<RectTransform>();
			CenterAt(circleRect, Vector2.zero, new Vector2(EmergeCircleSize, EmergeCircleSize));

			Image image = go.GetComponent<Image>();
			image.sprite = GetEmergeCircleSprite();
			image.type = Image.Type.Simple;
			image.color = color;
			image.preserveAspect = true;
			image.raycastTarget = false;

			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			return group;
		}

		private static CanvasGroup CreateCutsceneEmotionName(Transform parent, EmotionConfig emotion, Color color,
			out RectTransform nameRect)
		{
			TextMeshProUGUI text = CreateOverlayText(parent, emotion.EmotionName,
				EmergeEmotionNameFontSize, Vector2.zero,
				new Vector2(EmergeCircleSize - 40f, 96f), TextAlignmentOptions.Center);
			nameRect = text.rectTransform;
			text.color = CutsceneTextColorOn(color);
			text.enableWordWrapping = false;
			text.overflowMode = TextOverflowModes.Overflow;

			CanvasGroup group = text.gameObject.AddComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			return group;
		}

		private static void CenterAt(RectTransform rect, Vector2 position, Vector2 size)
		{
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
		}

		/// <summary>浅色底走深字、深色底走浅字，保证圆里的情绪名在任何配表颜色上都读得清。</summary>
		private static Color CutsceneTextColorOn(Color background)
		{
			float luminance = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
			return luminance >= 0.6f ? EmergeDarkTextColor : EmergeLightTextColor;
		}

		private void DestroyEmergenceCutscene()
		{
			m_emergeCutsceneVersion++;
			m_cutscenePlaying = false;
			m_cutsceneSkipRequested = false;
			if (m_emergeCutsceneRoot != null)
			{
				UnityEngine.Object.Destroy(m_emergeCutsceneRoot);
			}

			m_emergeCutsceneRoot = null;
		}

		private static void StretchFullScreen(RectTransform rect)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
		}

		private static Sprite GetEmergeCircleSprite()
		{
			if (s_emergeCircleSprite != null)
			{
				return s_emergeCircleSprite;
			}

			const int size = 512;
			var pixels = new Color32[size * size];
			float center = size * 0.5f;
			float radius = center - 1f;
			const float feather = 1.5f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float distance = Distance(x, y, center);
					byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((radius - distance) / feather) * 255f);
					pixels[y * size + x] = new Color32(255, 255, 255, alpha);
				}
			}

			s_emergeCircleSprite = CreateSprite("MemoryEmergeCircle", size, pixels);
			return s_emergeCircleSprite;
		}

		private static Sprite GetEmergeHaloSprite()
		{
			if (s_emergeHaloSprite != null)
			{
				return s_emergeHaloSprite;
			}

			// 柔边：中心实、边缘按幂次衰减，铺在纯黑上就是一层很淡的暖光，不会有硬边。
			const int size = 256;
			var pixels = new Color32[size * size];
			float center = size * 0.5f;
			float radius = center;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float normalized = Mathf.Clamp01(Distance(x, y, center) / radius);
					float falloff = Mathf.Pow(1f - normalized, 2.2f);
					pixels[y * size + x] = new Color32(255, 255, 255,
						(byte)Mathf.RoundToInt(falloff * 255f));
				}
			}

			s_emergeHaloSprite = CreateSprite("MemoryEmergeHalo", size, pixels);
			return s_emergeHaloSprite;
		}

		private static Sprite GetEmergeRingSprite()
		{
			if (s_emergeRingSprite != null)
			{
				return s_emergeRingSprite;
			}

			const int size = 512;
			var pixels = new Color32[size * size];
			float center = size * 0.5f;
			float radius = center - 2f;
			float inner = radius - 7f;
			const float feather = 2.5f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float distance = Distance(x, y, center);
					float outerEdge = Mathf.Clamp01((radius - distance) / feather);
					float innerEdge = Mathf.Clamp01((distance - inner) / feather);
					float alpha = Mathf.Min(outerEdge, innerEdge);
					pixels[y * size + x] = new Color32(255, 255, 255,
						(byte)Mathf.RoundToInt(alpha * 255f));
				}
			}

			s_emergeRingSprite = CreateSprite("MemoryEmergeRing", size, pixels);
			return s_emergeRingSprite;
		}

		private static float Distance(int x, int y, float center)
		{
			float dx = x + 0.5f - center;
			float dy = y + 0.5f - center;
			return Mathf.Sqrt(dx * dx + dy * dy);
		}

		private static Sprite CreateSprite(string name, int size, Color32[] pixels)
		{
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				name = name,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
			};
			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			// 纹理已设为不可读，必须显式 FullRect，否则 Tight 网格会退化成整块并打警告。
			Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
				new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
			sprite.name = name;
			return sprite;
		}
	}
}
