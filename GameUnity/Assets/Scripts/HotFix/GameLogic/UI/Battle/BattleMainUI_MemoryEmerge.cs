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
	/// 记忆涌现过场：黑底 + 左记忆图 / 中情绪圆 / 右记忆图，先两张图 0.3s 渐显，再情绪 0.5s 渐显。
	/// 一次结算涌出多个情绪时按入队顺序逐条播放。整屏可点击跳过当前这一条。
	/// </summary>
	public partial class BattleMainUI
	{
		private const float EmergeCardSize = 360f;
		private const float EmergeCardOffsetX = 420f;
		private const float EmergeCircleSize = 280f;
		private const float EmergeMemoryFadeSeconds = 0.3f;
		private const float EmergeGapSeconds = 0.15f;
		private const float EmergeEmotionFadeSeconds = 0.5f;
		private const float EmergeHoldSeconds = 1.2f;
		private const float EmergeFadeOutSeconds = 0.25f;
		private const float EmergeMemoryNameFontSize = 28f;
		private const float EmergeEmotionNameFontSize = 36f;
		private static readonly Color EmergeMemoryNameColor = new Color(1f, 1f, 1f, 0.55f);
		private static readonly Color EmergeDarkTextColor = new Color(0.13f, 0.12f, 0.12f, 0.88f);
		private static readonly Color EmergeLightTextColor = new Color(0.97f, 0.97f, 0.97f, 0.95f);

		/// <summary>运行时生成的白圆，避免把小尺寸的 dialogue_colour_btn 放大到 280 后发虚。</summary>
		private static Sprite s_emergeCircleSprite;

		private readonly List<string> m_pendingEmergeEmotionIds = new List<string>();
		private GameObject m_emergeCutsceneRoot;
		private CanvasGroup m_emergeCutsceneGroup;
		private int m_emergeCutsceneVersion;
		private bool m_cutscenePlaying;
		private bool m_cutsceneSkipRequested;

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
			try
			{
				// 来源按获得顺序：最早那段放左边，最新那段（本次刚拿到的）放右边。
				IReadOnlyList<string> sources = m_narrativeService.GetMemorySourcesForEmotion(emotionId);
				string earlierMemoryId = sources.Count > 0 ? sources[0] : null;
				string newerMemoryId = sources.Count > 1 ? sources[sources.Count - 1] : null;

				BuildEmergenceCutscene(emotion, earlierMemoryId, newerMemoryId,
					out CanvasGroup memoryGroup, out CanvasGroup emotionGroup);
				SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryEmotionEmerge);

				await FadeCutsceneGroupsAsync(new[] { memoryGroup }, 1f, EmergeMemoryFadeSeconds, version);
				if (!IsCutsceneAlive(version))
				{
					return;
				}

				await HoldCutsceneAsync(EmergeGapSeconds, version);
				if (!IsCutsceneAlive(version))
				{
					return;
				}

				await FadeCutsceneGroupsAsync(new[] { emotionGroup }, 1f, EmergeEmotionFadeSeconds, version);
				if (!IsCutsceneAlive(version))
				{
					return;
				}

				await HoldCutsceneAsync(EmergeHoldSeconds, version);
				if (!IsCutsceneAlive(version))
				{
					return;
				}

				m_cutsceneSkipRequested = false;
				await FadeCutsceneGroupsAsync(new[] { m_emergeCutsceneGroup }, 0f, EmergeFadeOutSeconds, version);
			}
			finally
			{
				if (version == m_emergeCutsceneVersion)
				{
					DestroyEmergenceCutscene();
				}
			}
		}

		private bool IsCutsceneAlive(int version)
			=> !IsDestroyed && version == m_emergeCutsceneVersion && m_emergeCutsceneRoot != null;

		private void BuildEmergenceCutscene(EmotionConfig emotion, string earlierMemoryId, string newerMemoryId,
			out CanvasGroup memoryGroup, out CanvasGroup emotionGroup)
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
			m_emergeCutsceneGroup = rootGroup;

			memoryGroup = CreateCutsceneGroup(root.transform, "Memories");
			emotionGroup = CreateCutsceneGroup(root.transform, "Emotion");

			CreateCutsceneMemoryCard(memoryGroup.transform, -EmergeCardOffsetX, earlierMemoryId);
			CreateCutsceneMemoryCard(memoryGroup.transform, EmergeCardOffsetX, newerMemoryId);
			CreateCutsceneEmotionCircle(emotionGroup.transform, emotion);
		}

		private void OnClickEmergeCutscene()
		{
			if (m_cutscenePlaying)
			{
				m_cutsceneSkipRequested = true;
			}
		}

		private static CanvasGroup CreateCutsceneGroup(Transform parent, string name)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
			go.transform.SetParent(parent, false);
			StretchFullScreen(go.GetComponent<RectTransform>());
			CanvasGroup group = go.GetComponent<CanvasGroup>();
			group.alpha = 0f;
			group.blocksRaycasts = false;
			group.interactable = false;
			return group;
		}

		private void CreateCutsceneMemoryCard(Transform parent, float offsetX, string memoryId)
		{
			var go = new GameObject("MemoryCard", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(EmergeCardSize, EmergeCardSize);
			rect.anchoredPosition = new Vector2(offsetX, 0f);

			Image image = go.GetComponent<Image>();
			image.preserveAspect = true;
			image.raycastTarget = false;

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
				return;
			}

			// 缺图（M10 之后的记忆目前都没有 imageId）：不留白块，只把记忆名放在原位。
			image.color = new Color(1f, 1f, 1f, 0f);
			string label = memory == null ? memoryId : memory.MemoryName;
			if (string.IsNullOrWhiteSpace(label))
			{
				return;
			}

			TextMeshProUGUI text = CreateOverlayText(go.transform, label, EmergeMemoryNameFontSize,
				Vector2.zero, new Vector2(EmergeCardSize, 96f), TextAlignmentOptions.Center);
			text.color = EmergeMemoryNameColor;
			text.enableWordWrapping = false;
			text.overflowMode = TextOverflowModes.Overflow;
		}

		private void CreateCutsceneEmotionCircle(Transform parent, EmotionConfig emotion)
		{
			var go = new GameObject("EmotionCircle", typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(EmergeCircleSize, EmergeCircleSize);
			rect.anchoredPosition = Vector2.zero;

			Color color = EmotionVisual.ResolveColor(emotion);
			Image image = go.GetComponent<Image>();
			image.sprite = GetEmergeCircleSprite();
			image.type = Image.Type.Simple;
			image.color = color;
			image.preserveAspect = true;
			image.raycastTarget = false;

			if (string.IsNullOrWhiteSpace(emotion.EmotionName))
			{
				return;
			}

			TextMeshProUGUI text = CreateOverlayText(go.transform, emotion.EmotionName,
				EmergeEmotionNameFontSize, Vector2.zero, new Vector2(EmergeCircleSize - 40f, 96f),
				TextAlignmentOptions.Center);
			text.color = CutsceneTextColorOn(color);
			text.enableWordWrapping = false;
			text.overflowMode = TextOverflowModes.Overflow;
		}

		/// <summary>浅色底走深字、深色底走浅字，保证圆里的情绪名在任何配表颜色上都读得清。</summary>
		private static Color CutsceneTextColorOn(Color background)
		{
			float luminance = 0.299f * background.r + 0.587f * background.g + 0.114f * background.b;
			return luminance >= 0.6f ? EmergeDarkTextColor : EmergeLightTextColor;
		}

		private async UniTask FadeCutsceneGroupsAsync(IReadOnlyList<CanvasGroup> groups, float target,
			float duration, int version)
		{
			if (groups == null || groups.Count == 0)
			{
				return;
			}

			var from = new float[groups.Count];
			for (int i = 0; i < groups.Count; i++)
			{
				from[i] = groups[i] != null ? groups[i].alpha : 1f;
			}

			if (duration <= 0f)
			{
				SetCutsceneAlpha(groups, target);
				return;
			}

			float elapsed = 0f;
			while (elapsed < duration)
			{
				if (!IsCutsceneAlive(version))
				{
					return;
				}

				if (m_cutsceneSkipRequested)
				{
					// 跳过的语义是「直接看见结果」：把这一段的终点值一次到位，后续段落照常收尾。
					SetCutsceneAlpha(groups, target);
					return;
				}

				elapsed += Time.unscaledDeltaTime;
				float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
				for (int i = 0; i < groups.Count; i++)
				{
					if (groups[i] != null)
					{
						groups[i].alpha = Mathf.LerpUnclamped(from[i], target, eased);
					}
				}

				await UniTask.Yield(PlayerLoopTiming.Update);
			}

			SetCutsceneAlpha(groups, target);
		}

		private static void SetCutsceneAlpha(IReadOnlyList<CanvasGroup> groups, float alpha)
		{
			for (int i = 0; i < groups.Count; i++)
			{
				if (groups[i] != null)
				{
					groups[i].alpha = alpha;
				}
			}
		}

		private async UniTask HoldCutsceneAsync(float seconds, int version)
		{
			float elapsed = 0f;
			while (elapsed < seconds)
			{
				if (!IsCutsceneAlive(version) || m_cutsceneSkipRequested)
				{
					return;
				}

				elapsed += Time.unscaledDeltaTime;
				await UniTask.Yield(PlayerLoopTiming.Update);
			}
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
			m_emergeCutsceneGroup = null;
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
			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				name = "MemoryEmergeCircle",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
			};

			var pixels = new Color32[size * size];
			float center = size * 0.5f;
			float radius = center - 1f;
			const float feather = 1.5f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = x + 0.5f - center;
					float dy = y + 0.5f - center;
					float distance = Mathf.Sqrt(dx * dx + dy * dy);
					byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((radius - distance) / feather) * 255f);
					pixels[y * size + x] = new Color32(255, 255, 255, alpha);
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply(false, true);
			s_emergeCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
			s_emergeCircleSprite.name = "MemoryEmergeCircle";
			return s_emergeCircleSprite;
		}
	}
}
