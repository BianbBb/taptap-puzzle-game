using System;
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
	/// 结局过场「记忆星图」。
	///
	/// 对应剧本 T0806「【我】是谁？」之后的那段演出：情绪是一颗灰圆，玩家依次点亮，
	/// 圆变成该情绪的配表色，它下面已解锁的记忆图从圆心飞出来围在外侧；两颗圆都亮起，
	/// 之间长出一条线。四颗全亮后中心那颗「？」才变成可以点的「自我」，点下去所有线收向中心，
	/// 白场，只剩一个字：我。
	///
	/// 为什么「自我」没有记忆图：配表里 E07 的 emotionCardSet 是空的。它不是漏配 ——
	/// 这条演出要说的正是"自我不来自某一段记忆，而来自把所有这些记忆放在一起看"。
	///
	/// 实现沿用项目里过场的既有做法：BattleMainUI 内代码搭 overlay、单循环 + 纯函数逐帧求值、
	/// 不建 prefab、不新建 UIWindow。
	/// </summary>
	public partial class BattleMainUI
	{
		/// <summary>配表 turnType 取这个值，表示这一节点是记忆星图过场（节点自身不播对白）。</summary>
		private const string MemoryStarMapTurnType = "MemoryStarMap";

		/// <summary>「自我」的情绪 ID（情绪配置表 E07）。</summary>
		private const string StarMapSelfEmotionId = "E07";

		// ---- 版式：坐标沿用 1280×720 设计稿，StarPos 换算到 1920×1080 的 UI 单位 ----
		private const float StarDesignWidth = 1280f;
		private const float StarDesignHeight = 720f;
		private const float StarDesignScale = 1.5f;

		/// <summary>情绪圆直径（设计稿 100px）。</summary>
		private const float StarCircleSize = 100f * StarDesignScale;
		/// <summary>记忆图边长（设计稿 48px）。</summary>
		private const float StarMemorySize = 48f * StarDesignScale;
		/// <summary>记忆图到圆心的距离（设计稿 138px）。</summary>
		private const float StarMemoryDistance = 138f * StarDesignScale;
		/// <summary>记忆图只排在该圆"朝外"的那一侧，撑开这么多度；内侧整段留给连线。</summary>
		private const float StarMemorySpread = 74f;
		/// <summary>单个情绪最多展示的记忆图张数，超了会挤在一起。</summary>
		private const int StarMemoryMaxPerEmotion = 6;

		private const float StarGlowSize = 340f * StarDesignScale * 2f;
		private const float StarSelfGlowSize = 560f * StarDesignScale * 2f;
		private const float StarLinkWidth = 2f;
		private const int StarLinkSegments = 16;

		private const float StarIntroFontSize = 50f;
		private const float StarNameFontSize = 26f;
		private const float StarSelfCharFontSize = 225f;
		private const float StarSubFontSize = 22f;

		// ---- 节拍（秒，全部走 unscaled）----
		private const float StarIntroStart = 0.35f;
		private const float StarIntroFadeIn = 0.8f;
		private const float StarIntroFadeOutAt = 1.45f;
		private const float StarIntroFadeOut = 1.05f;
		private const float StarCircleAppearAt = 1.55f;
		private const float StarCircleAppearSeconds = 1.1f;
		private const float StarColorSeconds = 0.5f;
		private const float StarMemoryDelay = 0.26f;
		private const float StarMemoryStagger = 0.085f;
		private const float StarMemoryInSeconds = 0.62f;
		private const float StarLinkGrowSeconds = 0.95f;

		private const float StarFinaleConvergeSeconds = 0.9f;
		/// <summary>单束光的飞行时长；四颗依次错开，最后一束正好在光球胀开的 0.75s 抵达中心。</summary>
		private const float StarBeamSeconds = 0.55f;
		private const float StarBeamStagger = 0.07f;
		private const float StarBeamHeadSize = 46f * StarDesignScale;
		private const float StarBeamWidth = 2.4f;
		private const float StarFinaleGlowAt = 0.75f;
		private const float StarFinaleGlowSeconds = 0.85f;
		private const float StarFinaleWhiteAt = 1.45f;
		private const float StarFinaleWhiteSeconds = 0.7f;
		private const float StarFinaleCharAt = 2.1f;
		private const float StarFinaleCharSeconds = 1.0f;
		private const float StarFinaleSubAt = 3.1f;
		private const float StarFinaleSubSeconds = 0.9f;
		private const float StarFinaleTailSeconds = 0.45f;

		private static readonly Color StarLockedDiscColor = new Color(0.255f, 0.239f, 0.224f, 1f);
		private static readonly Color StarDarkTextColor = new Color(0.18f, 0.16f, 0.145f, 1f);
		private static readonly Color StarLightTextColor = new Color(0.895f, 0.871f, 0.839f, 0.62f);
		private static readonly Color StarIntroColor = new Color(0.992f, 0.973f, 0.941f, 1f);
		private static readonly Color StarWhiteScreenColor = new Color(0.98f, 0.969f, 0.949f, 1f);
		private static readonly Color StarSelfCharColor = new Color(0.102f, 0.082f, 0.071f, 1f);

		private sealed class StarMapMemoryView
		{
			public string MemoryId;
			public CanvasGroup Group;
			public RectTransform Rect;
			/// <summary>该记忆图相对圆心飞出的角度（弧度）。</summary>
			public float Angle;
		}

		private sealed class StarMapCircleView
		{
			public string EmotionId;
			public bool IsSelf;
			public string EmotionName;
			public Color EmotionColor;
			public Color DiscColor;
			public Vector2 Position;
			public CanvasGroup Group;
			public RectTransform Rect;
			public Image Disc;
			public Image Glow;
			public CanvasGroup GlowGroup;
			public TextMeshProUGUI Label;
			public readonly List<StarMapMemoryView> Memories = new List<StarMapMemoryView>();
			public bool Lit;
			public float LitAt;
			public float RejectAt = -1f;
		}

		private sealed class StarMapLinkView
		{
			public string EmotionA;
			public string EmotionB;
			public CanvasGroup Group;
			public readonly List<RectTransform> Segments = new List<RectTransform>();
			/// <summary>每段长度，生长时按段截断。</summary>
			public readonly List<float> SegmentLengths = new List<float>();
			public float TotalLength;
		}

		/// <summary>终幕收束：每颗亮着的情绪圆射出一束光，走到中心。</summary>
		private sealed class StarMapBeamView
		{
			public string EmotionId;
			public RectTransform BeamRect;
			public Image Beam;
			public CanvasGroup BeamGroup;
			public RectTransform HeadRect;
			public CanvasGroup HeadGroup;
			public float Length;
			public float Delay;
		}

		private sealed class StarMapView
		{
			public GameObject Root;
			public CanvasGroup RootGroup;
			public CanvasGroup IntroGroup;
			public TextMeshProUGUI IntroText;
			public readonly List<StarMapCircleView> Circles = new List<StarMapCircleView>();
			public readonly List<StarMapLinkView> Links = new List<StarMapLinkView>();
			public readonly List<StarMapBeamView> Beams = new List<StarMapBeamView>();
			public Image FinaleGlow;
			public CanvasGroup FinaleGlowGroup;
			public CanvasGroup WhiteGroup;
			public CanvasGroup SelfCharGroup;
			public RectTransform SelfCharRect;
			public CanvasGroup SubGroup;
			public TextMeshProUGUI SubText;
		}

		private StarMapView m_starMapView;
		private int m_starMapVersion;
		private bool m_starMapPlaying;
		private bool m_starMapFinished;
		private float m_starMapTime;
		private float m_starMapFinaleAt = -1f;
		private int m_starMapLitCount;
		private bool m_starMapSelfUnlocked;
		/// <summary>调试预览：忽略存档，四颗情绪全给、每种取配表前几张记忆，且不写存档。</summary>
		private bool m_starMapPreview;

		/// <summary>配表 turnType=MemoryStarMap 时走这条过场。</summary>
		private static bool IsMemoryStarMapTurn(StoryNodeConfig story)
		{
			if (story == null || string.IsNullOrWhiteSpace(story.TurnType))
			{
				return false;
			}

			return story.TurnType.Trim().Equals(MemoryStarMapTurnType,
				StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>设计稿坐标（1280×720，y 向下）换算成 UI 单位（1920×1080，y 向上）。</summary>
		private static Vector2 StarPos(float designX, float designY)
		{
			return new Vector2(
				(designX - StarDesignWidth * 0.5f) * StarDesignScale,
				(StarDesignHeight * 0.5f - designY) * StarDesignScale);
		}

		// EaseOutCubic / QuadraticBezier 直接复用 BattleMainUI_MemoryEmerge.cs 里的，别在这边重复声明。

		private static float EaseInCubic(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t * t;
		}

		private static float EaseInOutQuad(float t)
		{
			t = Mathf.Clamp01(t);
			return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
		}

		/// <summary>
		/// 配表里的情绪色是很淡的粉彩，直接铺成 150px 的圆会像"白圆"，四个情绪分不出来。
		/// 保色相、提饱和、压一点明度，得到同一色相下能看清的圆面色。
		/// 光晕和连线仍用原色，所以"亮的雾"和"实的圆"是同一个色相的两档。
		/// </summary>
		private static Color BoostDiscColor(Color color, float saturationScale, float valueScale)
		{
			Color.RGBToHSV(color, out float h, out float s, out float v);
			s = Mathf.Clamp01(s * saturationScale);
			v = Mathf.Clamp01(v * valueScale);
			Color boosted = Color.HSVToRGB(h, s, v);
			boosted.a = 1f;
			return boosted;
		}

		// ============================================================
		// 入口
		// ============================================================
		/// <summary>
		/// 调试用：不看存档，直接用配表数据把整条演出跑一遍。
		/// 正式流程要走完三十天才到这一段，不给这个入口就没法验收画面。
		/// 只演不写档（不会凭空拿到「自我」）。
		/// </summary>
		public void DebugPreviewMemoryStarMap()
		{
			UniTask.Void(async () => await PlayMemoryStarMapAsync(true));
		}

		private async UniTask PlayMemoryStarMapAsync(bool preview = false)
		{
			if (IsDestroyed)
			{
				return;
			}

			DestroyMemoryStarMap();
			int version = ++m_starMapVersion;
			m_starMapPlaying = true;
			m_starMapFinished = false;
			m_starMapTime = 0f;
			m_starMapFinaleAt = -1f;
			m_starMapLitCount = 0;
			m_starMapSelfUnlocked = false;
			m_starMapPreview = preview;

			// 一颗情绪都没有（异常存档）：不播，直接放行，别把玩家卡在黑场里。
			if (CollectOwnedEmotionStars().Count == 0)
			{
				m_starMapPlaying = false;
				return;
			}

			m_starMapView = BuildMemoryStarMap();
			float total = StarFinaleCharAt + StarFinaleCharSeconds + StarFinaleSubSeconds
				+ StarFinaleTailSeconds;

			float musicVolumeBefore = GameModule.AudioModule.MusicVolume;
			bool duckBgm = musicVolumeBefore > 0.01f;
			float chimePlayed = -1f;
			bool convergeCue = false;
			try
			{
				while (!m_starMapFinished)
				{
					if (!IsStarMapAlive(version))
					{
						return;
					}

					float finale = m_starMapFinaleAt >= 0f ? m_starMapTime - m_starMapFinaleAt : -1f;

					if (duckBgm)
					{
						GameModule.AudioModule.MusicVolume = musicVolumeBefore *
							Mathf.Lerp(1f, 0.34f, Mathf.Clamp01(m_starMapTime / 1.2f));
					}

					if (m_starMapSelfUnlocked && chimePlayed < 0f)
					{
						chimePlayed = m_starMapTime;
						SoundConfigMgr.Instance.PlaySfx(SfxNames.EmergeBloom);
					}

					if (finale >= 0f && !convergeCue)
					{
						convergeCue = true;
						SoundConfigMgr.Instance.PlaySfx(SfxNames.EmergeRise);
					}

					ApplyMemoryStarMapFrame(m_starMapView, m_starMapTime, finale);

					if (finale >= 0f && finale >= total)
					{
						m_starMapFinished = true;
						break;
					}

					await UniTask.Yield(PlayerLoopTiming.Update);
					m_starMapTime += Time.unscaledDeltaTime;
				}
			}
			finally
			{
				if (duckBgm)
				{
					GameModule.AudioModule.MusicVolume = musicVolumeBefore;
				}

				if (version == m_starMapVersion)
				{
					DestroyMemoryStarMap();
				}
			}
		}

		/// <summary>按存档里真正拥有过的情绪组星图；没有记忆的情绪也给一颗圆，只是周围空着。</summary>
		private List<string> CollectOwnedEmotionStars()
		{
			var owned = new List<string>();
			if (m_starMapPreview)
			{
				owned.AddRange(StarEmotionOrder);
				return owned;
			}

			NarrativeSaveData save = NarrativeSaveData.Get;
			for (int i = 0; i < StarEmotionOrder.Length; i++)
			{
				string emotionId = StarEmotionOrder[i];
				if (save.HasEmotion(emotionId))
				{
					owned.Add(emotionId);
				}
			}

			return owned;
		}

		/// <summary>四颗情绪星，坐标取自设计稿。</summary>
		private static readonly string[] StarEmotionOrder = { "E04", "E02", "E06", "E03" };

		private static readonly float[] StarEmotionPosX = { 300f, 625f, 955f, 700f };
		private static readonly float[] StarEmotionPosY = { 300f, 200f, 300f, 490f };

		private const float StarSelfPosX = 640f;
		private const float StarSelfPosY = 348f;

		/// <summary>C(4,2) 全连：任意两颗点亮就有一条线。</summary>
		private static readonly string[,] StarLinkPairs =
		{
			{ "E04", "E02" },
			{ "E04", "E03" },
			{ "E04", "E06" },
			{ "E02", "E06" },
			{ "E02", "E03" },
			{ "E03", "E06" },
		};

		// ============================================================
		// 搭 UI
		// ============================================================
		private StarMapView BuildMemoryStarMap()
		{
			NarrativeSaveData save = NarrativeSaveData.Get;

			var root = new GameObject("MemoryStarMap",
				typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
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

			var view = new StarMapView
			{
				Root = root,
				RootGroup = rootGroup,
			};
			m_starMapView = view;

			// 记忆图先归到情绪下：按获得顺序，先拿到的先出现。
			Dictionary<string, List<string>> memoriesByEmotion =
				CollectMemoriesByEmotion(save);

			Transform linkLayer = CreateStarLayer(root.transform, "Links");
			Transform memoryLayer = CreateStarLayer(root.transform, "Memories");
			Transform circleLayer = CreateStarLayer(root.transform, "Circles");
			Transform beamLayer = CreateStarLayer(root.transform, "Beams");
			Transform introLayer = CreateStarLayer(root.transform, "Intro");
			Transform finaleLayer = CreateStarLayer(root.transform, "Finale");

			List<string> ownedStars = CollectOwnedEmotionStars();
			for (int i = 0; i < StarEmotionOrder.Length; i++)
			{
				string emotionId = StarEmotionOrder[i];
				if (!ownedStars.Contains(emotionId) || !NarrativeConfigMgr.Instance.HasEmotion(emotionId))
				{
					continue;
				}

				EmotionConfig config = NarrativeConfigMgr.Instance.GetEmotion(emotionId);
				var circle = new StarMapCircleView
				{
					EmotionId = emotionId,
					IsSelf = false,
					EmotionName = config == null ? emotionId : config.EmotionName,
					EmotionColor = EmotionVisual.ResolveColor(config),
					Position = StarPos(StarEmotionPosX[i], StarEmotionPosY[i]),
				};
				circle.DiscColor = BoostDiscColor(circle.EmotionColor, 2.6f, 0.92f);
				BuildStarCircle(circleLayer, circle, memoriesByEmotion, memoryLayer);
				view.Circles.Add(circle);
			}

			// 中心那颗「自我」：场上唯一没有记忆图的圆。
			EmotionConfig selfConfig = NarrativeConfigMgr.Instance.GetEmotion(StarMapSelfEmotionId);
			var self = new StarMapCircleView
			{
				EmotionId = StarMapSelfEmotionId,
				IsSelf = true,
				EmotionName = selfConfig == null ? "自我" : selfConfig.EmotionName,
				EmotionColor = EmotionVisual.ResolveColor(selfConfig),
				Position = StarPos(StarSelfPosX, StarSelfPosY),
			};
			self.DiscColor = BoostDiscColor(self.EmotionColor, 2.2f, 0.94f);
			BuildStarCircle(circleLayer, self, null, memoryLayer);
			view.Circles.Add(self);

			BuildStarLinks(view, linkLayer);
			BuildStarBeams(view, beamLayer);

			// 开场那三个字：字距由张到收，然后散开淡去。
			view.IntroText = CreateOverlayText(introLayer, "我是谁", StarIntroFontSize,
				Vector2.zero, new Vector2(900f, 120f), TextAlignmentOptions.Center);
			view.IntroText.color = StarIntroColor;
			view.IntroText.enableWordWrapping = false;
			view.IntroText.overflowMode = TextOverflowModes.Overflow;
			view.IntroGroup = view.IntroText.gameObject.AddComponent<CanvasGroup>();
			view.IntroGroup.alpha = 0f;
			view.IntroGroup.blocksRaycasts = false;

			// 终幕：光球 → 白幕 → 「我」→ 副句
			view.FinaleGlow = CreateStarImage(finaleLayer, "FinaleGlow", GetEmergeHaloSprite(),
				self.EmotionColor, Vector2.zero, Vector2.zero);
			view.FinaleGlowGroup = view.FinaleGlow.gameObject.AddComponent<CanvasGroup>();
			view.FinaleGlowGroup.alpha = 0f;
			view.FinaleGlowGroup.blocksRaycasts = false;

			Image white = CreateStarImage(finaleLayer, "WhiteScreen", null,
				StarWhiteScreenColor, Vector2.zero, Vector2.zero);
			StretchFullScreen(white.rectTransform);
			view.WhiteGroup = white.gameObject.AddComponent<CanvasGroup>();
			view.WhiteGroup.alpha = 0f;
			view.WhiteGroup.blocksRaycasts = false;

			view.SubText = CreateOverlayText(finaleLayer, "我是把这些都记住的人", StarSubFontSize,
				new Vector2(0f, -144f), new Vector2(1200f, 90f), TextAlignmentOptions.Center);
			view.SubText.color = new Color(0.102f, 0.082f, 0.071f, 0.62f);
			view.SubText.enableWordWrapping = false;
			view.SubGroup = view.SubText.gameObject.AddComponent<CanvasGroup>();
			view.SubGroup.alpha = 0f;
			view.SubGroup.blocksRaycasts = false;

			TextMeshProUGUI selfChar = CreateOverlayText(finaleLayer, "我", StarSelfCharFontSize,
				new Vector2(0f, 66f), new Vector2(600f, 340f), TextAlignmentOptions.Center);
			selfChar.color = StarSelfCharColor;
			selfChar.enableWordWrapping = false;
			selfChar.overflowMode = TextOverflowModes.Overflow;
			view.SelfCharRect = selfChar.rectTransform;
			view.SelfCharGroup = selfChar.gameObject.AddComponent<CanvasGroup>();
			view.SelfCharGroup.alpha = 0f;
			view.SelfCharGroup.blocksRaycasts = false;

			ApplyMemoryStarMapFrame(view, 0f, -1f);
			return view;
		}

		private Dictionary<string, List<string>> CollectMemoriesByEmotion(NarrativeSaveData save)
		{
			var map = new Dictionary<string, List<string>>();
			for (int i = 0; i < StarEmotionOrder.Length; i++)
			{
				map[StarEmotionOrder[i]] = new List<string>();
			}

			if (m_starMapPreview)
			{
				// 预览不读档：按配表顺序，每种情绪拿前几张，够把版式撑满就行。
				IReadOnlyList<MemoryConfig> all = NarrativeConfigMgr.Instance.AllMemories;
				for (int i = 0; i < all.Count; i++)
				{
					MemoryConfig memory = all[i];
					if (memory == null || memory.EmotionCardSet == null ||
						memory.EmotionCardSet.Count == 0)
					{
						continue;
					}

					if (!map.TryGetValue(memory.EmotionCardSet[0], out List<string> bucket) ||
						bucket.Count >= StarMemoryMaxPerEmotion)
					{
						continue;
					}

					bucket.Add(memory.Id);
				}

				return map;
			}

			IReadOnlyList<string> ownedMemories = save.GetMemoryIdsInAcquisitionOrder();
			for (int i = 0; i < ownedMemories.Count; i++)
			{
				MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(ownedMemories[i]);
				if (memory == null || memory.EmotionCardSet == null || memory.EmotionCardSet.Count == 0)
				{
					continue;
				}

				string emotionId = memory.EmotionCardSet[0];
				if (!map.TryGetValue(emotionId, out List<string> list) ||
					list.Count >= StarMemoryMaxPerEmotion)
				{
					continue;
				}

				list.Add(ownedMemories[i]);
			}

			return map;
		}

		private static Transform CreateStarLayer(Transform parent, string name)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			StretchFullScreen(go.GetComponent<RectTransform>());
			return go.transform;
		}

		private static Image CreateStarImage(Transform parent, string name, Sprite sprite,
			Color color, Vector2 position, Vector2 size)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			CenterAt(rect, position, size);

			Image image = go.GetComponent<Image>();
			image.sprite = sprite;
			image.color = color;
			image.raycastTarget = false;
			return image;
		}

		private void BuildStarCircle(Transform layer, StarMapCircleView circle,
			Dictionary<string, List<string>> memoriesByEmotion, Transform memoryLayer)
		{
			var go = new GameObject("StarCircle_" + circle.EmotionId,
				typeof(RectTransform), typeof(CanvasGroup));
			go.transform.SetParent(layer, false);
			circle.Rect = go.GetComponent<RectTransform>();
			CenterAt(circle.Rect, circle.Position, new Vector2(StarCircleSize * 2f, StarCircleSize * 2f));
			circle.Group = go.GetComponent<CanvasGroup>();
			circle.Group.alpha = 0f;
			circle.Group.blocksRaycasts = false;

			// 光晕（点亮后才显形）
			circle.Glow = CreateStarImage(go.transform, "Glow", GetEmergeHaloSprite(),
				circle.EmotionColor, Vector2.zero,
				new Vector2(circle.IsSelf ? StarSelfGlowSize : StarGlowSize,
					circle.IsSelf ? StarSelfGlowSize : StarGlowSize));
			circle.GlowGroup = circle.Glow.gameObject.AddComponent<CanvasGroup>();
			circle.GlowGroup.alpha = 0f;
			circle.GlowGroup.blocksRaycasts = false;

			// 圆面 + 点击热区
			var discGo = new GameObject("Disc", typeof(RectTransform), typeof(Image), typeof(Button));
			discGo.transform.SetParent(go.transform, false);
			RectTransform discRect = discGo.GetComponent<RectTransform>();
			CenterAt(discRect, Vector2.zero, new Vector2(StarCircleSize, StarCircleSize));
			circle.Disc = discGo.GetComponent<Image>();
			circle.Disc.sprite = GetEmergeCircleSprite();
			circle.Disc.color = StarLockedDiscColor;
			circle.Disc.raycastTarget = true;

			Button button = discGo.GetComponent<Button>();
			button.transition = Selectable.Transition.None;
			button.targetGraphic = circle.Disc;
			StarMapCircleView captured = circle;
			button.onClick.AddListener(() => OnClickStarCircle(captured));

			// 名字写在圆里
			TextMeshProUGUI label = CreateOverlayText(go.transform, circle.EmotionName,
				StarNameFontSize, Vector2.zero, new Vector2(StarCircleSize - 12f, 60f),
				TextAlignmentOptions.Center);
			label.color = StarLightTextColor;
			label.enableWordWrapping = false;
			label.overflowMode = TextOverflowModes.Overflow;
			circle.Label = label;

			if (circle.IsSelf || memoriesByEmotion == null ||
				!memoriesByEmotion.TryGetValue(circle.EmotionId, out List<string> memoryIds))
			{
				return;
			}

			// 记忆图只排"朝外"那一侧，内侧整段留给连线。
			float outward = Mathf.Atan2(circle.Position.y - StarPos(StarSelfPosX, StarSelfPosY).y,
				circle.Position.x - StarPos(StarSelfPosX, StarSelfPosY).x);
			int count = memoryIds.Count;
			for (int i = 0; i < count; i++)
			{
				float t = count <= 1 ? 0.5f : (float)i / (count - 1);
				float angle = outward + Mathf.Deg2Rad *
					(-StarMemorySpread + 2f * StarMemorySpread * t);

				var memGo = new GameObject("Memory_" + memoryIds[i],
					typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
				memGo.transform.SetParent(memoryLayer, false);
				RectTransform rect = memGo.GetComponent<RectTransform>();
				CenterAt(rect, circle.Position, new Vector2(StarMemorySize, StarMemorySize));

				Image image = memGo.GetComponent<Image>();
				image.raycastTarget = false;
				image.preserveAspect = true;
				image.color = new Color(1f, 1f, 1f, 0f);

				CanvasGroup group = memGo.GetComponent<CanvasGroup>();
				group.alpha = 0f;
				group.blocksRaycasts = false;

				MemoryConfig memory = NarrativeConfigMgr.Instance.GetMemory(memoryIds[i]);
				string location = memory == null ? null : memory.ImageId;
				if (!string.IsNullOrWhiteSpace(location) && location != "placeholder" &&
					GameModule.ResourceModule.CheckLocationValid(location))
				{
					image.SetSprite(location, false, loaded =>
					{
						if (loaded == null || IsDestroyed)
						{
							return;
						}

						loaded.preserveAspect = true;
						loaded.color = Color.white;
					}, gameObject.GetCancellationTokenOnDestroy());
				}

				circle.Memories.Add(new StarMapMemoryView
				{
					MemoryId = memoryIds[i],
					Group = group,
					Rect = rect,
					Angle = angle,
				});
			}
		}

		/// <summary>连线：先把曲线离散成若干段短 Image 摆好，播放时只按进度截断，每帧不动 transform。</summary>
		private void BuildStarLinks(StarMapView view, Transform layer)
		{
			for (int i = 0; i < StarLinkPairs.GetLength(0); i++)
			{
				string a = StarLinkPairs[i, 0];
				string b = StarLinkPairs[i, 1];
				StarMapCircleView circleA = FindStarCircle(view, a);
				StarMapCircleView circleB = FindStarCircle(view, b);
				if (circleA == null || circleB == null)
				{
					continue;
				}

				Vector2 start = circleA.Position;
				Vector2 end = circleB.Position;
				Vector2 mid = (start + end) * 0.5f;
				Vector2 self = StarPos(StarSelfPosX, StarSelfPosY);

				Vector2 dir = end - start;
				float length = dir.magnitude;
				if (length < 1f)
				{
					continue;
				}

				// 法线翻向"远离中心"的一侧：星图中心永远留空给「自我」。
				var normal = new Vector2(-dir.y, dir.x) / length;
				if (Vector2.Dot(mid - self, normal) < 0f)
				{
					normal = -normal;
				}

				float bend = (34f + length / StarDesignScale * 0.04f) * StarDesignScale;
				Vector2 control = mid + normal * bend;

				var link = new StarMapLinkView { EmotionA = a, EmotionB = b };
				var container = new GameObject("Link_" + a + "_" + b,
					typeof(RectTransform), typeof(CanvasGroup));
				container.transform.SetParent(layer, false);
				StretchFullScreen(container.GetComponent<RectTransform>());
				link.Group = container.GetComponent<CanvasGroup>();
				link.Group.alpha = 0f;
				link.Group.blocksRaycasts = false;

				Color colorA = circleA.EmotionColor;
				Color colorB = circleB.EmotionColor;
				Color lineColor = Color.Lerp(colorA, colorB, 0.5f);

				Vector2 previous = start;
				for (int s = 1; s <= StarLinkSegments; s++)
				{
					float t = (float)s / StarLinkSegments;
					Vector2 point = QuadraticBezier(start, control, end, t);
					Vector2 segmentVector = point - previous;
					float segmentLength = segmentVector.magnitude;
					if (segmentLength < 0.01f)
					{
						previous = point;
						continue;
					}

					var segGo = new GameObject("Seg" + s, typeof(RectTransform), typeof(Image));
					segGo.transform.SetParent(container.transform, false);
					RectTransform segRect = segGo.GetComponent<RectTransform>();
					segRect.anchorMin = segRect.anchorMax = new Vector2(0.5f, 0.5f);
					segRect.pivot = new Vector2(0f, 0.5f);
					segRect.anchoredPosition = previous;
					segRect.sizeDelta = new Vector2(segmentLength, StarLinkWidth);
					segRect.localRotation = Quaternion.Euler(0f, 0f,
						Mathf.Atan2(segmentVector.y, segmentVector.x) * Mathf.Rad2Deg);

					Image segImage = segGo.GetComponent<Image>();
					segImage.sprite = GetWhiteSprite();
					segImage.color = lineColor;
					segImage.raycastTarget = false;

					link.Segments.Add(segRect);
					link.SegmentLengths.Add(segmentLength);
					link.TotalLength += segmentLength;
					previous = point;
				}

				view.Links.Add(link);
			}
		}

		/// <summary>
		/// 终幕用的四束光。和连线一样是"预先摆好、播放时只改长度"：
		/// 束身 pivot 在起点、朝中心，长度 0 → 全长就是光打到中心；头部是一颗小光斑跟着走。
		/// </summary>
		private void BuildStarBeams(StarMapView view, Transform layer)
		{
			Vector2 center = StarPos(StarSelfPosX, StarSelfPosY);
			int index = 0;
			for (int i = 0; i < view.Circles.Count; i++)
			{
				StarMapCircleView circle = view.Circles[i];
				if (circle.IsSelf)
				{
					continue;
				}

				Vector2 delta = center - circle.Position;
				float length = delta.magnitude;
				if (length < 1f)
				{
					continue;
				}

				float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
				Color beamColor = BoostDiscColor(circle.EmotionColor, 1.7f, 1f);

				var beamGo = new GameObject("Beam_" + circle.EmotionId,
					typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
				beamGo.transform.SetParent(layer, false);
				RectTransform beamRect = beamGo.GetComponent<RectTransform>();
				beamRect.anchorMin = beamRect.anchorMax = new Vector2(0.5f, 0.5f);
				beamRect.pivot = new Vector2(0f, 0.5f);
				beamRect.anchoredPosition = circle.Position;
				beamRect.sizeDelta = new Vector2(0f, StarBeamWidth);
				beamRect.localRotation = Quaternion.Euler(0f, 0f, angle);

				Image beam = beamGo.GetComponent<Image>();
				beam.sprite = GetWhiteSprite();
				beam.color = beamColor;
				beam.raycastTarget = false;

				CanvasGroup beamGroup = beamGo.GetComponent<CanvasGroup>();
				beamGroup.alpha = 0f;
				beamGroup.blocksRaycasts = false;

				var headGo = new GameObject("Head", typeof(RectTransform), typeof(Image),
					typeof(CanvasGroup));
				headGo.transform.SetParent(layer, false);
				RectTransform headRect = headGo.GetComponent<RectTransform>();
				CenterAt(headRect, circle.Position,
					new Vector2(StarBeamHeadSize, StarBeamHeadSize));

				Image head = headGo.GetComponent<Image>();
				head.sprite = GetEmergeHaloSprite();
				head.color = new Color(beamColor.r, beamColor.g, beamColor.b, 0.92f);
				head.raycastTarget = false;

				CanvasGroup headGroup = headGo.GetComponent<CanvasGroup>();
				headGroup.alpha = 0f;
				headGroup.blocksRaycasts = false;

				view.Beams.Add(new StarMapBeamView
				{
					EmotionId = circle.EmotionId,
					BeamRect = beamRect,
					Beam = beam,
					BeamGroup = beamGroup,
					HeadRect = headRect,
					HeadGroup = headGroup,
					Length = length,
					Delay = index * StarBeamStagger,
				});
				index++;
			}
		}

		private static StarMapCircleView FindStarCircle(StarMapView view, string emotionId)
		{
			for (int i = 0; i < view.Circles.Count; i++)
			{
				if (view.Circles[i].EmotionId == emotionId)
				{
					return view.Circles[i];
				}
			}

			return null;
		}

		private static Sprite s_starWhiteSprite;

		private static Sprite GetWhiteSprite()
		{
			if (s_starWhiteSprite != null)
			{
				return s_starWhiteSprite;
			}

			var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			texture.SetPixel(0, 0, Color.white);
			texture.Apply();
			s_starWhiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
				new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
			s_starWhiteSprite.name = "MemoryStarMapWhite";
			return s_starWhiteSprite;
		}

		// ============================================================
		// 点击
		// ============================================================
		private void OnClickStarCircle(StarMapCircleView circle)
		{
			if (!m_starMapPlaying || m_starMapFinished || circle == null || circle.Lit)
			{
				return;
			}

			// 终幕一旦开始，星图不再接受操作。
			if (m_starMapFinaleAt >= 0f)
			{
				return;
			}

			if (circle.IsSelf)
			{
				if (!m_starMapSelfUnlocked)
				{
					// 还有情绪没亮：只回一次环闪，不给文案。
					circle.RejectAt = m_starMapTime;
					return;
				}

				circle.Lit = true;
				circle.LitAt = m_starMapTime;
				m_starMapFinaleAt = m_starMapTime;

				// 结算放在点下去这一刻：演出被打断也不丢情绪。预览不写档。
				if (!m_starMapPreview && NarrativeSaveData.Get.GrantEmotion(StarMapSelfEmotionId))
				{
					DLogger.Log("MemoryStarMap: 获得情绪 {0}", StarMapSelfEmotionId);
				}

				return;
			}

			circle.Lit = true;
			circle.LitAt = m_starMapTime;
			m_starMapLitCount++;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.StoryEmotionEmerge);

			int starCount = m_starMapView == null ? 0 : m_starMapView.Circles.Count - 1;
			if (!m_starMapSelfUnlocked && m_starMapLitCount >= starCount)
			{
				m_starMapSelfUnlocked = true;
			}
		}

		// ============================================================
		// 逐帧求值（整条时间线只有一个循环，不做串联 await）
		// ============================================================
		private void ApplyMemoryStarMapFrame(StarMapView view, float t, float finale)
		{
			if (view == null)
			{
				return;
			}

			// 开场：字距由张到收，再整体散开淡去
			float introIn = Mathf.Clamp01((t - StarIntroStart) / StarIntroFadeIn);
			float introOut = Mathf.Clamp01((t - StarIntroFadeOutAt) / StarIntroFadeOut);
			float introAlpha = Mathf.Max(0f, introIn * (1f - introOut)) * 0.9f;
			SetGroup(view.IntroGroup, introAlpha);
			if (view.IntroText != null)
			{
				view.IntroText.characterSpacing = Mathf.Lerp(28f, 6f, introIn) + introOut * 46f;
			}

			// 星图整体呼吸：四颗全亮后轻轻起伏，表示"它还活着"
			float breath = 1f;
			if (m_starMapSelfUnlocked && m_starMapFinaleAt < 0f)
			{
				breath = 0.92f + 0.08f * Mathf.Sin(t * 1.25f);
			}

			float appear = Mathf.Clamp01((t - StarCircleAppearAt) / StarCircleAppearSeconds);
			float appearE = EaseOutCubic(appear);

			for (int i = 0; i < view.Circles.Count; i++)
			{
				StarMapCircleView circle = view.Circles[i];
				bool isSelf = circle.IsSelf;
				float lit = circle.Lit ? Mathf.Clamp01((t - circle.LitAt) / StarColorSeconds) : 0f;
				float litE = EaseOutCubic(lit);

				float alpha = appearE * breath;
				if (finale >= 0f && !isSelf)
				{
					alpha *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(finale / 0.8f));
				}

				SetGroup(circle.Group, alpha);
				circle.Group.blocksRaycasts = appear > 0.05f && m_starMapFinaleAt < 0f;

				// 圆面：灰 → 情绪色；顺手做一下点击回弹
				Color discColor = Color.Lerp(StarLockedDiscColor, circle.DiscColor, litE);
				circle.Disc.color = discColor;
				float pop = 1f;
				if (circle.Lit && lit < 0.62f)
				{
					pop = 1f + 0.065f * Mathf.Sin(lit / 0.62f * Mathf.PI);
				}

				circle.Disc.rectTransform.localScale = Vector3.one * pop;

				// 光晕
				float glow = litE;
				if (isSelf && m_starMapSelfUnlocked && !circle.Lit)
				{
					float pulse = 0.55f + 0.45f * Mathf.Sin(t * 3f);
					glow = 0.24f * pulse;
				}

				SetGroup(circle.GlowGroup, glow * (isSelf ? 0.55f : 0.75f));

				// 名字：灰态用浅色，点亮后用深色（圆面变浅了）
				if (circle.Label != null)
				{
					string label = circle.EmotionName;
					if (isSelf && !m_starMapSelfUnlocked)
					{
						label = "？";
					}

					if (circle.Label.text != label)
					{
						circle.Label.text = label;
					}

					Color textColor = circle.Lit
						? Color.Lerp(StarLightTextColor, StarDarkTextColor, litE)
						: StarLightTextColor;
					if (isSelf && m_starMapSelfUnlocked && !circle.Lit)
					{
						textColor = new Color(0.933f, 0.878f, 0.831f, 0.78f);
					}

					circle.Label.color = textColor;
				}

				// 未解锁时点中心：一次短促的环闪
				if (isSelf && circle.RejectAt >= 0f)
				{
					float reject = (t - circle.RejectAt) / 0.55f;
					if (reject >= 1f)
					{
						circle.RejectAt = -1f;
					}
					else
					{
						circle.Disc.rectTransform.localScale =
							Vector3.one * (1f + 0.06f * Mathf.Sin(reject * Mathf.PI));
					}
				}

				// 记忆图：从圆心飞出去，按张数依次错开
				for (int m = 0; m < circle.Memories.Count; m++)
				{
					StarMapMemoryView memory = circle.Memories[m];
					if (!circle.Lit)
					{
						SetGroup(memory.Group, 0f);
						continue;
					}

					float local = (t - circle.LitAt - StarMemoryDelay - m * StarMemoryStagger) /
						StarMemoryInSeconds;
					float localE = EaseOutCubic(Mathf.Clamp01(local));
					if (local <= 0f)
					{
						SetGroup(memory.Group, 0f);
						continue;
					}

					float distance = StarMemoryDistance * (0.42f + 0.58f * localE);
					memory.Rect.anchoredPosition = circle.Position + new Vector2(
						Mathf.Cos(memory.Angle) * distance,
						Mathf.Sin(memory.Angle) * distance);
					float scale = 0.62f + 0.38f * localE;
					memory.Rect.localScale = new Vector3(scale, scale, 1f);

					float memoryAlpha = Mathf.Clamp01(local * 1.8f) * 0.96f;
					if (finale >= 0f)
					{
						memoryAlpha *= Mathf.Lerp(1f, 0.2f, Mathf.Clamp01(finale / 0.8f));
					}

					SetGroup(memory.Group, memoryAlpha * alpha);
				}
			}

			// 连线：两端都亮才生长
			for (int i = 0; i < view.Links.Count; i++)
			{
				StarMapLinkView link = view.Links[i];
				StarMapCircleView a = FindStarCircle(view, link.EmotionA);
				StarMapCircleView b = FindStarCircle(view, link.EmotionB);
				if (a == null || b == null || !a.Lit || !b.Lit)
				{
					SetGroup(link.Group, 0f);
					continue;
				}

				float startAt = Mathf.Max(a.LitAt, b.LitAt);
				float grow = Mathf.Clamp01((t - startAt) / StarLinkGrowSeconds);
				float linkAlpha = 0.88f;
				if (finale >= 0f)
				{
					linkAlpha *= Mathf.Lerp(1f, 0.16f, Mathf.Clamp01(finale / 1.0f));
				}

				SetGroup(link.Group, linkAlpha * breath * appearE);

				// 按进度截断：前面整段全显，当前段按比例缩短，后面藏掉
				float revealed = grow * link.TotalLength;
				float consumed = 0f;
				for (int s = 0; s < link.Segments.Count; s++)
				{
					float segLength = link.SegmentLengths[s];
					RectTransform seg = link.Segments[s];
					float visible = Mathf.Clamp(revealed - consumed, 0f, segLength);
					bool show = visible > 0.01f;
					if (seg.gameObject.activeSelf != show)
					{
						seg.gameObject.SetActive(show);
					}

					if (show)
					{
						seg.sizeDelta = new Vector2(visible, StarLinkWidth);
					}

					consumed += segLength;
				}
			}

			// 终幕
			ApplyStarMapFinaleFrame(view, t, finale);
		}

		private void ApplyStarMapFinaleFrame(StarMapView view, float t, float finale)
		{
			if (finale < 0f)
			{
				SetGroup(view.FinaleGlowGroup, 0f);
				SetGroup(view.WhiteGroup, 0f);
				SetGroup(view.SelfCharGroup, 0f);
				SetGroup(view.SubGroup, 0f);
				ApplyStarMapBeams(view, -1f);
				return;
			}

			StarMapCircleView self = FindStarCircle(view, StarMapSelfEmotionId);
			Vector2 center = self == null ? Vector2.zero : self.Position;

			// 四束光先飞（0 ~ 0.76s），光球在最后一束抵达时胀开。
			ApplyStarMapBeams(view, finale);

			// 光球：从中心那一点胀开
			float glowT = Mathf.Clamp01((finale - StarFinaleGlowAt) / StarFinaleGlowSeconds);
			float glowScale = Mathf.Lerp(0.06f, 1f, EaseInOutQuad(glowT));
			view.FinaleGlow.rectTransform.anchoredPosition = center;
			view.FinaleGlow.rectTransform.sizeDelta =
				new Vector2(StarGlowSize, StarGlowSize) * glowScale;
			SetGroup(view.FinaleGlowGroup, glowT <= 0f ? 0f : Mathf.Lerp(0.95f, 0f, glowT) * 0.9f);

			// 白幕
			float whiteT = Mathf.Clamp01((finale - StarFinaleWhiteAt) / StarFinaleWhiteSeconds);
			SetGroup(view.WhiteGroup, whiteT <= 0f ? 0f : Mathf.Clamp01(whiteT * 1.05f));

			// 「我」
			float charT = Mathf.Clamp01((finale - StarFinaleCharAt) / StarFinaleCharSeconds);
			SetGroup(view.SelfCharGroup, EaseOutCubic(charT));
			if (view.SelfCharRect != null)
			{
				float scale = 1.08f - 0.08f * EaseOutCubic(charT);
				view.SelfCharRect.localScale = new Vector3(scale, scale, 1f);
			}

			// 副句
			float subT = Mathf.Clamp01((finale - StarFinaleSubAt) / StarFinaleSubSeconds);
			SetGroup(view.SubGroup, EaseOutCubic(subT));
		}

		/// <summary>终幕光流：束身长度随进度长到中心，光斑加速飞过去，抵达即交给中心光球。</summary>
		private void ApplyStarMapBeams(StarMapView view, float finale)
		{
			Vector2 center = StarPos(StarSelfPosX, StarSelfPosY);
			for (int i = 0; i < view.Beams.Count; i++)
			{
				StarMapBeamView beam = view.Beams[i];
				StarMapCircleView circle = FindStarCircle(view, beam.EmotionId);
				if (finale < 0f || circle == null || !circle.Lit)
				{
					SetGroup(beam.BeamGroup, 0f);
					SetGroup(beam.HeadGroup, 0f);
					continue;
				}

				float bt = Mathf.Clamp01((finale - beam.Delay) / StarBeamSeconds);
				if (bt >= 1f)
				{
					SetGroup(beam.BeamGroup, 0f);
					SetGroup(beam.HeadGroup, 0f);
					continue;
				}

				// 光斑越靠近中心越快（被吸进去的感觉）。
				float headD = EaseInCubic(bt);
				beam.BeamRect.sizeDelta = new Vector2(beam.Length * headD, StarBeamWidth);
				SetGroup(beam.BeamGroup,
					0.82f * Mathf.Clamp01(bt * 6f) * (1f - Mathf.Clamp01((bt - 0.8f) / 0.2f)));

				beam.HeadRect.anchoredPosition =
					Vector2.Lerp(circle.Position, center, headD);
				float headScale = Mathf.Lerp(1f, 0.5f, bt);
				beam.HeadRect.localScale = new Vector3(headScale, headScale, 1f);
				SetGroup(beam.HeadGroup,
					Mathf.Clamp01(bt * 8f) * (1f - Mathf.Clamp01((bt - 0.85f) / 0.15f)));
			}
		}

		private bool IsStarMapAlive(int version)
		{
			return !IsDestroyed && version == m_starMapVersion && m_starMapView != null;
		}

		private void DestroyMemoryStarMap()
		{
			m_starMapVersion++;
			m_starMapPlaying = false;
			m_starMapFinished = false;
			m_starMapFinaleAt = -1f;
			m_starMapPreview = false;
			if (m_starMapView != null && m_starMapView.Root != null)
			{
				UnityEngine.Object.Destroy(m_starMapView.Root);
			}

			m_starMapView = null;
		}
	}
}
