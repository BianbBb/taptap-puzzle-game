using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using DGame;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
	/// <summary>
	/// 新游戏开场的「留名字」对话框（代码搭，不新增 prefab）。
	///
	/// 三版设计都在 <see cref="NameEntryStyles"/> 里，布局差异只靠一张风格表描述，
	/// 公共流程（随机、校验、确认落档）只有一份。要换版本改 <see cref="s_style"/> 一行即可。
	///
	/// 设计稿见 docs/名字对话框设计稿/（A/B/C 三张 PNG + render_mock.py）。
	/// 关掉（右上角 ×）就退回开始界面，不写档。
	/// </summary>
	public partial class StartMenuWindow
	{
		#region 配色（纸片系，与 BattleMainUI 一致）

		private static readonly Color NameInk = new Color(0.282f, 0.220f, 0.157f, 1f);
		private static readonly Color NameInkFaint = new Color(0.573f, 0.502f, 0.416f, 0.784f);
		private static readonly Color NameError = new Color(0.62f, 0.31f, 0.24f, 1f);
		private static readonly Color NameChipPaper = new Color(0.980f, 0.957f, 0.910f, 1f);
		private static readonly Color NameMask = new Color(0.071f, 0.055f, 0.043f, 0.42f);

		#endregion

		#region 风格表

		private enum NameEntryVariant
		{
			Card,
			Ledger,
			Minimal,
		}

		/// <summary>
		/// 一版布局的全部可变量。坐标都是相对「面板中心」的偏移，+y 向上，
		/// 这样三版只是数字不同，摆放逻辑不用各写一遍。
		/// </summary>
		private sealed class NameEntryStyle
		{
			public string Panel;
			public Vector2 PanelSize;
			public bool PanelStretch;
			public Color PanelTint;

			public float TitleDy;
			public float TitleSize;
			public Color TitleColor;

			public float SubDy;
			public float SubSize;
			public Color SubColor;

			/// <summary>输入框/候选/按钮这一列相对面板中心再左移多少（旧账本右侧有装饰，整体偏左）。</summary>
			public float ContentDx;

			public string FieldBack;
			public string FieldSprite;
			public Vector2 FieldSize;
			public Color FieldTint;
			public float FieldDy;
			public float FieldTextSize;

			/// <summary>输入位下面是否再画一条手写横线（登记卡/极简版靠这条线当「填写位」）。</summary>
			public bool FieldUnderline;
			public float UnderlineWidth;
			public float UnderlineDy;

			public Vector2 DiceSize;
			public float DiceDx;
			public float DiceDy;

			public string ChipBack;
			public string ChipSprite;
			public Vector2 ChipSize;
			public Color ChipTint;
			public bool ChipVertical;
			public float ChipStartDy;
			public float ChipPitch;
			public float ChipTextSize;

			public Vector2 ConfirmSize;
			public float ConfirmDy;
			public string ConfirmLabel;

			public Vector2 CloseSize;
			public Vector2 CloseInset;
		}

		// 坐标约定：dy = 面板半高 − 距离面板顶部的像素数（Unity +y 向上）。
		// 设计稿里量到的都是「距顶部多少」，换算是 dy = PanelSize.y / 2 - offset。
		private static readonly NameEntryStyle[] s_styles =
		{
			// A · 登记卡：白纸 + 顶部丝带当标题条，候选是「还没填的格子」。
			new NameEntryStyle
			{
				Panel = "setting_save_popup_bg",
				PanelSize = new Vector2(880f, 600f),
				PanelStretch = true,
				// 纸片素材本身带纸色，这里必须给白：给纸色会让 tint 再乘一遍，整张纸被压暗
				PanelTint = Color.white,
				TitleDy = 206f, TitleSize = 38f, TitleColor = new Color(0.361f, 0.275f, 0.157f, 1f),
				SubDy = 128f, SubSize = 18f, SubColor = NameError,
				ContentDx = 0f,
				FieldBack = "collection_card_image_slot", FieldSprite = null,
				// 比纸色略深一点点，才看得出「这里能填」，又不至于变成一块白砖
				FieldSize = new Vector2(500f, 84f), FieldTint = new Color(0.925f, 0.898f, 0.855f, 1f),
				FieldDy = 52f, FieldTextSize = 28f,
				FieldUnderline = true, UnderlineWidth = 440f, UnderlineDy = 4f,
				DiceSize = new Vector2(84f, 84f), DiceDx = 286f, DiceDy = 56f,
				ChipBack = "collection_card_image_slot", ChipSprite = "setting_save_slot_border",
				ChipSize = new Vector2(196f, 66f), ChipTint = NameChipPaper,
				ChipVertical = false, ChipStartDy = -58f, ChipPitch = 224f, ChipTextSize = 25f,
				ConfirmSize = new Vector2(310f, 102f), ConfirmDy = -168f, ConfirmLabel = "就叫这个",
				CloseSize = new Vector2(104f, 65f), CloseInset = new Vector2(56f, 34f),
			},

			// B · 旧账本：整幅牛皮纸（右侧铅笔尺子是画上去的，不能九切切到），内容靠左。
			new NameEntryStyle
			{
				Panel = "dialogue_panel",
				PanelSize = new Vector2(850f, 615f),
				PanelStretch = false,
				PanelTint = Color.white,
				TitleDy = 223f, TitleSize = 36f, TitleColor = NameInk,
				SubDy = 181f, SubSize = 18f, SubColor = NameError,
				ContentDx = -77f,
				FieldBack = null, FieldSprite = "dialogue_reply_option_bg",
				FieldSize = new Vector2(500f, 100f), FieldTint = Color.white,
				FieldDy = 99f, FieldTextSize = 27f,
				FieldUnderline = false, UnderlineWidth = 0f, UnderlineDy = 0f,
				DiceSize = new Vector2(70f, 70f), DiceDx = 175f, DiceDy = 215f,
				ChipBack = null, ChipSprite = "dialogue_reply_bubble_01",
				ChipSize = new Vector2(500f, 60f), ChipTint = Color.white,
				ChipVertical = true, ChipStartDy = 15f, ChipPitch = 66f, ChipTextSize = 24f,
				ConfirmSize = new Vector2(310f, 102f), ConfirmDy = -208f, ConfirmLabel = "就这个",
				CloseSize = new Vector2(100f, 62f), CloseInset = new Vector2(168f, 34f),
			},

			// C · 极简白卡：一张干净白纸，只有一条下划线当填写位，留白最多。
			new NameEntryStyle
			{
				Panel = "collection_detail_card",
				PanelSize = new Vector2(800f, 540f),
				PanelStretch = true,
				PanelTint = Color.white,
				TitleDy = 174f, TitleSize = 36f, TitleColor = NameInk,
				SubDy = 130f, SubSize = 17f, SubColor = NameError,
				ContentDx = 0f,
				FieldBack = null, FieldSprite = null,
				FieldSize = new Vector2(420f, 70f), FieldTint = new Color(1f, 1f, 1f, 0f),
				FieldDy = 74f, FieldTextSize = 38f,
				FieldUnderline = true, UnderlineWidth = 400f, UnderlineDy = 38f,
				DiceSize = new Vector2(62f, 62f), DiceDx = 250f, DiceDy = 72f,
				ChipBack = null, ChipSprite = "collection_card_image_slot",
				ChipSize = new Vector2(190f, 56f), ChipTint = new Color(0.969f, 0.945f, 0.898f, 1f),
				ChipVertical = false, ChipStartDy = -60f, ChipPitch = 214f, ChipTextSize = 23f,
				ConfirmSize = new Vector2(310f, 102f), ConfirmDy = -160f, ConfirmLabel = "就叫这个",
				CloseSize = new Vector2(92f, 58f), CloseInset = new Vector2(62f, 26f),
			},
		};

		/// <summary>当前启用的版本。A = 0 登记卡 / B = 1 旧账本 / C = 2 极简白卡。</summary>
		private static NameEntryVariant s_activeVariant = NameEntryVariant.Card;

		private static NameEntryStyle ActiveStyle => s_styles[(int)s_activeVariant];

		#endregion

		private GameObject m_nameEntryRoot;
		private TMP_InputField m_nameInput;
		private TextMeshProUGUI m_nameHintText;
		private readonly List<Button> m_nameCandidateButtons = new List<Button>();
		private readonly List<TextMeshProUGUI> m_nameCandidateLabels = new List<TextMeshProUGUI>();
		private bool m_nameEntryStartNewGame;
		private bool m_nameEntryClosing;

		/// <summary>开场留名字。startNewGame=true 表示点的是「开始」，确认后清档重开。</summary>
		public void ShowNameEntry(bool startNewGame)
		{
			if (m_nameEntryRoot != null)
			{
				return;
			}

			NameEntryStyle style = ActiveStyle;
			m_nameEntryStartNewGame = startNewGame;
			m_nameEntryClosing = false;
			EnsureEventSystemForNameEntry();
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiPanelOpen, DGame.AudioType.UISound);

			var root = new GameObject("NameEntry", typeof(RectTransform), typeof(Image));
			root.transform.SetParent(transform, false);
			root.transform.SetAsLastSibling();
			RectTransform rootRect = root.GetComponent<RectTransform>();
			rootRect.anchorMin = Vector2.zero;
			rootRect.anchorMax = Vector2.one;
			rootRect.offsetMin = Vector2.zero;
			rootRect.offsetMax = Vector2.zero;
			Image mask = root.GetComponent<Image>();
			mask.sprite = WhiteSprite;
			mask.color = NameMask;
			mask.raycastTarget = true;
			m_nameEntryRoot = root;

			var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
			panelObject.transform.SetParent(root.transform, false);
			RectTransform panel = panelObject.GetComponent<RectTransform>();
			panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
			panel.pivot = new Vector2(0.5f, 0.5f);
			panel.sizeDelta = style.PanelSize;
			Image panelImage = panelObject.GetComponent<Image>();
			panelImage.color = style.PanelTint;
			ApplyNameEntrySprite(panelImage, style.Panel, style.PanelTint, style.PanelStretch);

			// 弹入：从下方浮上来，和其它弹窗保持同一种节奏
			panel.anchoredPosition = new Vector2(0f, -80f);
			panel.DOAnchorPos(Vector2.zero, 0.24f).SetUpdate(true).SetLink(panelObject);

			CreateNameEntryTitle(panelObject.transform, style);
			CreateNameEntryField(panelObject.transform, style);
			CreateNameEntryDice(panelObject.transform, style);
			CreateNameEntryCandidates(panelObject.transform, style);
			CreateNameEntryConfirm(panelObject.transform, style);
			CreateNameEntryClose(panelObject.transform, style);

			// 默认把上一个名字带出来，玩家改一个字就行
			string existing = NarrativePlayerName.Normalize(NarrativePlayerName.Current);
			if (!string.IsNullOrEmpty(existing) && m_nameInput != null)
			{
				m_nameInput.SetTextWithoutNotify(existing);
			}

			RollNameEntryCandidates();

			if (m_nameInput != null)
			{
				m_nameInput.ActivateInputField();
			}
		}

		#region 组件

		private void CreateNameEntryTitle(Transform parent, NameEntryStyle style)
		{
			TextMeshProUGUI title = CreateNameEntryText(parent, "先留个名字", style.TitleSize, style.TitleColor,
				new Vector2(style.ContentDx, style.TitleDy), new Vector2(600f, 52f),
				TextAlignmentOptions.Center);
			title.fontStyle = FontStyles.Bold;

			// 副标题位平时留空，只当报错槽用（提交空名时在这里出红字），
			// 空字符串不产生任何绘制，不会占视觉。
			m_nameHintText = CreateNameEntryText(parent, string.Empty, style.SubSize, style.SubColor,
				new Vector2(style.ContentDx, style.SubDy), new Vector2(700f, 30f),
				TextAlignmentOptions.Center);
		}

		private void CreateNameEntryField(Transform parent, NameEntryStyle style)
		{
			Vector2 center = new Vector2(style.ContentDx, style.FieldDy);

			// 有的版本输入框下面垫一块纸（FieldBack），有的靠整张纸当底（FieldSprite 为空）
			if (!string.IsNullOrEmpty(style.FieldBack))
			{
				var backObject = new GameObject("FieldBack", typeof(RectTransform), typeof(Image));
				backObject.transform.SetParent(parent, false);
				RectTransform backRect = backObject.GetComponent<RectTransform>();
				backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0.5f);
				backRect.pivot = new Vector2(0.5f, 0.5f);
				backRect.anchoredPosition = center;
				backRect.sizeDelta = style.FieldSize;
				Image backImage = backObject.GetComponent<Image>();
				backImage.raycastTarget = false;
				backImage.color = style.FieldTint;
				ApplyNameEntrySprite(backImage, style.FieldBack, style.FieldTint, true);
			}

			var fieldObject = new GameObject("NameField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
			fieldObject.transform.SetParent(parent, false);
			RectTransform fieldRect = fieldObject.GetComponent<RectTransform>();
			fieldRect.anchorMin = fieldRect.anchorMax = new Vector2(0.5f, 0.5f);
			fieldRect.pivot = new Vector2(0.5f, 0.5f);
			fieldRect.anchoredPosition = center;
			fieldRect.sizeDelta = style.FieldSize;
			Image fieldImage = fieldObject.GetComponent<Image>();
			fieldImage.color = style.FieldTint;
			// 没贴图时也要能点（raycast 不看 alpha），极简版就靠这块透明热区聚焦
			fieldImage.sprite = WhiteSprite;
			ApplyNameEntrySprite(fieldImage, style.FieldSprite, style.FieldTint, true);

			if (style.FieldUnderline)
			{
				// 手写横线：1x1 白点拉成一条，比切图省事，也不用担心九切变形
				var lineObject = new GameObject("FieldUnderline", typeof(RectTransform), typeof(Image));
				lineObject.transform.SetParent(parent, false);
				RectTransform lineRect = lineObject.GetComponent<RectTransform>();
				lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0.5f);
				lineRect.pivot = new Vector2(0.5f, 0.5f);
				lineRect.anchoredPosition = new Vector2(style.ContentDx, style.UnderlineDy);
				lineRect.sizeDelta = new Vector2(style.UnderlineWidth, 3f);
				Image lineImage = lineObject.GetComponent<Image>();
				lineImage.sprite = WhiteSprite;
				lineImage.color = new Color(0.424f, 0.341f, 0.259f, 0.667f);
				lineImage.raycastTarget = false;
			}

			// TMP_InputField 需要一个独立的 textViewport 子节点，文字挂在它下面
			var areaObject = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
			areaObject.transform.SetParent(fieldObject.transform, false);
			RectTransform area = areaObject.GetComponent<RectTransform>();
			area.anchorMin = Vector2.zero;
			area.anchorMax = Vector2.one;
			area.offsetMin = new Vector2(22f, 8f);
			area.offsetMax = new Vector2(-22f, -8f);

			TextMeshProUGUI placeholder = CreateNameEntryText(areaObject.transform,
				"写个名字", style.FieldTextSize, NameInkFaint, Vector2.zero, Vector2.zero,
				TextAlignmentOptions.Center);
			StretchNameEntryRect(placeholder.rectTransform);
			placeholder.enableWordWrapping = false;

			TextMeshProUGUI value = CreateNameEntryText(areaObject.transform,
				string.Empty, style.FieldTextSize, NameInk, Vector2.zero, Vector2.zero,
				TextAlignmentOptions.Center);
			StretchNameEntryRect(value.rectTransform);
			value.enableWordWrapping = false;
			value.richText = false;

			TMP_InputField input = fieldObject.GetComponent<TMP_InputField>();
			input.targetGraphic = fieldImage;
			input.textViewport = area;
			input.textComponent = value;
			input.placeholder = placeholder;
			input.characterLimit = NarrativePlayerName.MaxLength;
			input.lineType = TMP_InputField.LineType.SingleLine;
			input.caretColor = NameInk;
			input.caretWidth = 2;
			input.selectionColor = new Color(0.93f, 0.82f, 0.62f, 0.6f);
			input.onSubmit.AddListener(_ => ConfirmNameEntry());
			m_nameInput = input;
		}

		private void CreateNameEntryDice(Transform parent, NameEntryStyle style)
		{
			var diceObject = new GameObject("RollButton", typeof(RectTransform), typeof(Image), typeof(Button));
			diceObject.transform.SetParent(parent, false);
			RectTransform diceRect = diceObject.GetComponent<RectTransform>();
			diceRect.anchorMin = diceRect.anchorMax = new Vector2(0.5f, 0.5f);
			diceRect.pivot = new Vector2(0.5f, 0.5f);
			diceRect.anchoredPosition = new Vector2(style.ContentDx + style.DiceDx, style.DiceDy);
			diceRect.sizeDelta = style.DiceSize;
			Image diceImage = diceObject.GetComponent<Image>();
			diceImage.color = Color.white;
			ApplyNameEntrySprite(diceImage, "ui_dice", Color.white, false);

			Button diceButton = diceObject.GetComponent<Button>();
			diceButton.transition = Selectable.Transition.None;
			diceButton.onClick.AddListener(OnClickRollName);
			BindNameEntryHover(diceButton);
		}

		private void CreateNameEntryCandidates(Transform parent, NameEntryStyle style)
		{
			for (int i = 0; i < 3; i++)
			{
				// 横排是「三格并排（中间为 0）」，竖排（旧账本）是「从第一格往下走」
				float offset = style.ChipVertical ? -i * style.ChipPitch : (i - 1) * style.ChipPitch;
				Vector2 position = style.ChipVertical
					? new Vector2(style.ContentDx, style.ChipStartDy + offset)
					: new Vector2(style.ContentDx + offset, style.ChipStartDy);

				var chipObject = new GameObject("Candidate" + i, typeof(RectTransform), typeof(Image), typeof(Button));
				chipObject.transform.SetParent(parent, false);
				RectTransform chipRect = chipObject.GetComponent<RectTransform>();
				chipRect.anchorMin = chipRect.anchorMax = new Vector2(0.5f, 0.5f);
				chipRect.pivot = new Vector2(0.5f, 0.5f);
				chipRect.anchoredPosition = position;
				chipRect.sizeDelta = style.ChipSize;
				Image chipImage = chipObject.GetComponent<Image>();
				chipImage.color = style.ChipTint;
				// 有 ChipBack 时底图当填充、ChipSprite 当描边叠在上面（登记卡的虚线格子就是这套）
				ApplyNameEntrySprite(chipImage, style.ChipBack ?? style.ChipSprite, style.ChipTint, true);

				if (!string.IsNullOrEmpty(style.ChipBack) && !string.IsNullOrEmpty(style.ChipSprite))
				{
					var borderObject = new GameObject("Border", typeof(RectTransform), typeof(Image));
					borderObject.transform.SetParent(chipObject.transform, false);
					RectTransform borderRect = borderObject.GetComponent<RectTransform>();
					StretchNameEntryRect(borderRect);
					Image borderImage = borderObject.GetComponent<Image>();
					borderImage.raycastTarget = false;
					borderImage.color = Color.white;
					ApplyNameEntrySprite(borderImage, style.ChipSprite, Color.white, true);
				}

				TextMeshProUGUI label = CreateNameEntryText(chipObject.transform, string.Empty,
					style.ChipTextSize, NameInk, Vector2.zero,
					new Vector2(style.ChipSize.x - 24f, style.ChipSize.y - 12f), TextAlignmentOptions.Center);
				label.enableWordWrapping = false;

				Button chipButton = chipObject.GetComponent<Button>();
				chipButton.transition = Selectable.Transition.None;
				int index = i;
				chipButton.onClick.AddListener(() => OnClickNameCandidate(index));
				BindNameEntryHover(chipButton);

				m_nameCandidateButtons.Add(chipButton);
				m_nameCandidateLabels.Add(label);
			}
		}

		private void CreateNameEntryConfirm(Transform parent, NameEntryStyle style)
		{
			var confirmObject = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button));
			confirmObject.transform.SetParent(parent, false);
			RectTransform confirmRect = confirmObject.GetComponent<RectTransform>();
			confirmRect.anchorMin = confirmRect.anchorMax = new Vector2(0.5f, 0.5f);
			confirmRect.pivot = new Vector2(0.5f, 0.5f);
			confirmRect.anchoredPosition = new Vector2(style.ContentDx, style.ConfirmDy);
			confirmRect.sizeDelta = style.ConfirmSize;
			Image confirmImage = confirmObject.GetComponent<Image>();
			confirmImage.color = Color.white;
			ApplyNameEntrySprite(confirmImage, "setting_save_btn", Color.white, true);

			TextMeshProUGUI label = CreateNameEntryText(confirmObject.transform, style.ConfirmLabel, 28f,
				NameInk, Vector2.zero, new Vector2(260f, 60f), TextAlignmentOptions.Center);
			label.enableWordWrapping = false;

			Button confirm = confirmObject.GetComponent<Button>();
			confirm.transition = Selectable.Transition.None;
			confirm.onClick.AddListener(ConfirmNameEntry);
			BindNameEntryHover(confirm);
		}

		private void CreateNameEntryClose(Transform parent, NameEntryStyle style)
		{
			var closeObject = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
			closeObject.transform.SetParent(parent, false);
			RectTransform closeRect = closeObject.GetComponent<RectTransform>();
			closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0.5f);
			closeRect.pivot = new Vector2(0.5f, 0.5f);
			closeRect.anchoredPosition = new Vector2(
				style.PanelSize.x / 2f - style.CloseInset.x,
				style.PanelSize.y / 2f - style.CloseInset.y);
			closeRect.sizeDelta = style.CloseSize;
			Image closeImage = closeObject.GetComponent<Image>();
			closeImage.color = Color.white;
			ApplyNameEntrySprite(closeImage, "setting_close_btn", Color.white, false);

			Button close = closeObject.GetComponent<Button>();
			close.transition = Selectable.Transition.None;
			close.onClick.AddListener(CloseNameEntry);
			BindNameEntryHover(close);
		}

		#endregion

		#region 交互

		private void OnClickRollName()
		{
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiClick, DGame.AudioType.UISound);
			string picked = NarrativePlayerName.PickRandom(ReadNameEntryText());
			if (m_nameInput != null)
			{
				m_nameInput.SetTextWithoutNotify(picked);
			}

			RollNameEntryCandidates();
			PunchNameEntry(transform.Find("NameEntry/Panel/RollButton") as RectTransform);
			PunchNameEntry(m_nameInput != null ? m_nameInput.transform as RectTransform : null);
		}

		private void OnClickNameCandidate(int index)
		{
			if (index < 0 || index >= m_nameCandidateLabels.Count)
			{
				return;
			}

			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiClick, DGame.AudioType.UISound);
			string picked = m_nameCandidateLabels[index].text;
			if (m_nameInput != null)
			{
				m_nameInput.SetTextWithoutNotify(picked);
			}

			PunchNameEntry(m_nameCandidateButtons[index].transform as RectTransform);
		}

		private void ConfirmNameEntry()
		{
			if (m_nameEntryClosing)
			{
				return;
			}

			string name = NarrativePlayerName.Normalize(ReadNameEntryText());
			if (string.IsNullOrEmpty(name))
			{
				SoundConfigMgr.Instance.PlaySfx(SfxNames.UiError, DGame.AudioType.UISound);
				if (m_nameHintText != null)
				{
					m_nameHintText.text = "名字不能是空的，随便写一个也行。";
					m_nameHintText.color = NameError;
				}

				PunchNameEntry(m_nameInput != null ? m_nameInput.transform as RectTransform : null);
				return;
			}

			m_nameEntryClosing = true;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiClick, DGame.AudioType.UISound);

			NarrativeSaveData saveData = NarrativeSaveData.Get;
			if (m_nameEntryStartNewGame)
			{
				// StartNewGame 不碰名字字段，但先清档再落名更稳妥（顺序反了会被清掉）
				saveData.StartNewGame();
			}

			saveData.SetPlayerName(name);
			DestroyNameEntry();
			EnterGame();
		}

		private void CloseNameEntry()
		{
			if (m_nameEntryClosing)
			{
				return;
			}

			m_nameEntryClosing = true;
			SoundConfigMgr.Instance.PlaySfx(SfxNames.UiPanelClose, DGame.AudioType.UISound);
			DestroyNameEntry();
		}

		private string ReadNameEntryText()
		{
			return m_nameInput == null ? string.Empty : m_nameInput.text;
		}

		/// <summary>刷新候选，并避开输入框里已有的名字，三张牌不会有一张白点。</summary>
		private void RollNameEntryCandidates()
		{
			string exclude = NarrativePlayerName.Normalize(ReadNameEntryText());
			List<string> picked = NarrativePlayerName.PickCandidates(m_nameCandidateLabels.Count + 1);
			int cursor = 0;
			for (int i = 0; i < m_nameCandidateLabels.Count; i++)
			{
				string candidate = null;
				while (cursor < picked.Count)
				{
					string next = picked[cursor++];
					if (!string.Equals(next, exclude, StringComparison.Ordinal))
					{
						candidate = next;
						break;
					}
				}

				m_nameCandidateLabels[i].text = candidate ?? NarrativePlayerName.PickRandom(exclude);
			}
		}

		private void PunchNameEntry(RectTransform rect)
		{
			if (rect == null)
			{
				return;
			}

			rect.DOKill();
			DOTween.Sequence().Append(rect.DOScale(0.9f, 0.06f))
				.Append(rect.DOScale(1f, 0.12f)).SetUpdate(true).SetLink(rect.gameObject);
		}

		private void DestroyNameEntry()
		{
			if (m_nameEntryRoot == null)
			{
				return;
			}

			UnityEngine.Object.Destroy(m_nameEntryRoot);
			m_nameEntryRoot = null;
			m_nameInput = null;
			m_nameHintText = null;
			m_nameCandidateButtons.Clear();
			m_nameCandidateLabels.Clear();
		}

		#endregion

		#region 构建工具

		private static Sprite s_whiteSprite;

		/// <summary>
		/// 1x1 白点：当纯色块（填写位热区、下划线、遮罩）用。
		/// 纹理 Apply(false, true) 后不可读，必须显式 FullRect，否则 Tight 网格会退化报警。
		/// </summary>
		private static Sprite WhiteSprite
		{
			get
			{
				if (s_whiteSprite == null)
				{
					var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
					{
						name = "NameEntryWhite",
						wrapMode = TextureWrapMode.Clamp,
						filterMode = FilterMode.Bilinear,
					};
					texture.SetPixel(0, 0, Color.white);
					texture.Apply(false, true);
					s_whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
						new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
					s_whiteSprite.name = "NameEntryWhite";
				}

				return s_whiteSprite;
			}
		}

		private static TextMeshProUGUI CreateNameEntryText(Transform parent, string value, float fontSize,
			Color color, Vector2 position, Vector2 size, TextAlignmentOptions alignment)
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
			text.color = color;
			text.alignment = alignment;
			text.enableWordWrapping = true;
			text.overflowMode = TextOverflowModes.Ellipsis;
			text.raycastTarget = false;
			return text;
		}

		private static void StretchNameEntryRect(RectTransform rect)
		{
			if (rect == null)
			{
				return;
			}

			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
		}

		/// <summary>
		/// 贴图异步加载：加载完成前先留 tint（没有贴图的 Image 会画成白色实心块），
		/// 回来时按需切成九切。
		/// 注意回调里不能再写 loaded.color —— 那会把调用方给的纸色 tint 刷成白，
		/// 遮罩、米色格子都会因此变白（这个坑踩过一次）。
		/// </summary>
		private void ApplyNameEntrySprite(Image image, string location, Color tint, bool sliced)
		{
			if (image == null || string.IsNullOrEmpty(location))
			{
				return;
			}

			image.color = tint;
			image.SetSprite(location, false, loaded =>
			{
				if (loaded == null)
				{
					return;
				}

				if (sliced)
				{
					loaded.type = Image.Type.Sliced;
				}
			}, gameObject.GetCancellationTokenOnDestroy());
		}

		/// <summary>鼠标进出时轻微放大/回正，给按钮一点「活的」反馈。</summary>
		private void BindNameEntryHover(Button button)
		{
			if (button == null)
			{
				return;
			}

			RectTransform rect = button.transform as RectTransform;
			if (rect == null || button.GetComponent<EventTrigger>() != null)
			{
				return;
			}

			EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();

			EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
			enter.callback.AddListener(_ =>
			{
				rect.DOKill();
				rect.DOScale(1.04f, 0.12f).SetUpdate(true).SetLink(button.gameObject);
			});
			trigger.triggers.Add(enter);

			EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
			exit.callback.AddListener(_ =>
			{
				rect.DOKill();
				rect.DOScale(1f, 0.12f).SetUpdate(true).SetLink(button.gameObject);
			});
			trigger.triggers.Add(exit);
		}

		private static void EnsureEventSystemForNameEntry()
		{
			if (EventSystem.current != null || UnityEngine.Object.FindObjectOfType<EventSystem>() != null)
			{
				return;
			}

#if ENABLE_INPUT_SYSTEM
			var go = new GameObject("EventSystem", typeof(EventSystem),
				typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
			var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
			UnityEngine.Object.DontDestroyOnLoad(go);
		}

		#endregion
	}
}
