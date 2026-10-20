using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
	public partial class BattleMainUI
	{
		private const string LayoutPreferencePrefix = "NarrativeHudLayout.";
		private const string LayoutPartsKey = LayoutPreferencePrefix + "Parts";
		// 人物默认站位：左右各偏移多少。原来的 ±850 在 1.4~1.7 倍下会被屏幕切掉一条，
		// 这里收到 ±560（老周 1.4 倍时身体最宽，740px，±560 刚好不出画）。
		private const float DefaultCharacterOffsetX = 560f;
		// 头顶离底边的目标高度（画布 1080）。按「精灵在 600×960 槽位里等高铺满」反推纵向偏移，
		// 保证任何缩放下头都不会顶出画面。
		private const float DefaultCharacterHeadTopY = 1000f;
		private const float CharacterSlotHeight = 960f;
		// 缩放按角色存的键前缀，和 NarrativeLayoutDefaults 里的导出条目名保持一致
		private const string CharacterScalePartPrefix = "CharacterScale.";

		/// <summary>
		/// 每个角色在所有场景共用一套缩放（打包默认）。F2 的放大/缩小写的是同名的本机覆盖键，
		/// 所以调一次对所有幕生效。
		/// </summary>
		private static readonly Dictionary<string, float> CharacterScaleDefaults = new Dictionary<string, float>
		{
			{ "老周", 1.4f },
			{ "陈先生", 1.7f },
			{ "陈果", 1.5f },
			{ "女孩", 1.5f },
		};

		// 当前场景建议的人物图层（0=所有前景都在人物前，1=中间，2=人物在所有前景前）
		private int m_sceneCharacterLayer;

		private readonly HashSet<RectTransform> m_layoutDragTargets = new HashSet<RectTransform>();
		private GameObject m_layoutHint;
		private Button m_btnLayoutEdit;
		private TextMeshProUGUI m_layoutEditButtonLabel;
		private TextMeshProUGUI m_layoutLayerLabel;
		private TextMeshProUGUI m_layoutMirrorLabel;
		private TextMeshProUGUI m_layoutScaleLabel;
		private TextMeshProUGUI m_layoutPositionLabel;
		private bool m_layoutEditMode;
		// F2 当前选中的角色槽（0 左 / 1 右 / 2 中后）。点击人物即切换。
		private int m_layoutEditSlotIndex;
		private bool m_layoutAdvanceWanted;

		private string GetCharacterLayoutPart()
		{
			SceneCharacterSlot slot = GetSelectedCharacterSlot();
			if (slot != null) return GetCharacterSlotLayoutPart(slot);
			string npcId = m_currentStoryNode != null ? m_currentStoryNode.NpcId : null;
			return GetCharacterLayoutPart(npcId, true);
		}

		private SceneCharacterSlot GetSelectedCharacterSlot()
		{
			SceneCharacterSlot slot = GetCharacterSlot(m_layoutEditSlotIndex);
			if (slot != null && slot.Rect != null && slot.Rect.gameObject.activeSelf) return slot;
			return null;
		}

		private string GetCharacterLayoutPart(string npcId, bool npcDialogue)
		{
			string sessionId = NarrativeSaveData.Get.CurrentSessionId;
			return "Character." + sessionId + "." + (npcDialogue ? "NpcDialogue." : "PlayerDialogue.") +
				(string.IsNullOrWhiteSpace(npcId) ? "Unknown" : npcId.Trim());
		}

		private static Vector2 ReadLayoutPosition(string part, Vector2 fallback)
		{
			string key = LayoutPreferencePrefix + part;
			if (PlayerPrefs.HasKey(key + ".X") && PlayerPrefs.HasKey(key + ".Y"))
			{
				TrackLayoutPart(part);
				return new Vector2(PlayerPrefs.GetFloat(key + ".X"), PlayerPrefs.GetFloat(key + ".Y"));
			}
			return NarrativeLayoutDefaults.TryGetPosition(part, out Vector2 position) ? position : fallback;
		}

		private static void TrackLayoutPart(string part)
		{
			string saved = PlayerPrefs.GetString(LayoutPartsKey, string.Empty);
			var parts = new HashSet<string>(saved.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries));
			if (!parts.Add(part)) return;
			PlayerPrefs.SetString(LayoutPartsKey, string.Join("\n", parts));
			PlayerPrefs.Save();
		}

		private static void SaveLayoutPosition(string part, RectTransform rect)
		{
			string key = LayoutPreferencePrefix + part;
			PlayerPrefs.SetFloat(key + ".X", rect.anchoredPosition.x);
			PlayerPrefs.SetFloat(key + ".Y", rect.anchoredPosition.y);
			TrackLayoutPart(part);
			PlayerPrefs.Save();
		}

		/// <summary>从 "Character.S01.NpcDialogue.老周" 里取 npcId（npcId 自身可能带点）。</summary>
		private static string GetLayoutPartNpcId(string part)
		{
			if (string.IsNullOrEmpty(part) || !part.StartsWith("Character.", System.StringComparison.Ordinal))
			{
				return string.Empty;
			}

			string[] pieces = part.Split('.');
			return pieces.Length >= 4 ? string.Join(".", pieces, 3, pieces.Length - 3) : string.Empty;
		}

		private static string CharacterScaleKey(string npcId)
			=> LayoutPreferencePrefix + CharacterScalePartPrefix + npcId;

		/// <summary>
		/// 角色缩放只认一个键：与幕、对话方无关，所以同一个人在哪个场景都是同一个大小。
		/// 优先级：本机 F2 覆盖 → 导出的打包默认 → 代码里的初始值。
		/// </summary>
		private static float ReadCharacterScaleByNpc(string npcId)
		{
			if (string.IsNullOrEmpty(npcId))
			{
				return 1f;
			}

			float fallback;
			if (NarrativeLayoutDefaults.TryGetScale(CharacterScalePartPrefix + npcId, out float exported))
			{
				fallback = exported;
			}
			else
			{
				fallback = CharacterScaleDefaults.TryGetValue(npcId, out float configured) ? configured : 1f;
			}

			return Mathf.Clamp(PlayerPrefs.GetFloat(CharacterScaleKey(npcId), fallback), 0.5f, 2f);
		}

		private float ReadCharacterScale()
		{
			return ReadCharacterScale(GetCharacterLayoutPart());
		}

		private static float ReadCharacterScale(string part)
		{
			return ReadCharacterScaleByNpc(GetLayoutPartNpcId(part));
		}

		private static bool ReadCharacterMirror(string part)
		{
			bool fallback = NarrativeLayoutDefaults.TryGetMirror(part, out bool mirror) && mirror;
			return PlayerPrefs.GetInt(LayoutPreferencePrefix + part + ".Mirror", fallback ? 1 : 0) != 0;
		}

		private static Vector3 ReadCharacterVisualScale(string part)
		{
			float scale = ReadCharacterScale(part);
			return new Vector3(ReadCharacterMirror(part) ? -scale : scale, scale, scale);
		}

		private int ReadCharacterLayer()
		{
			return ReadCharacterLayer(GetCharacterLayoutPart());
		}

		private int ReadCharacterLayer(string part)
		{
			// 本机手动覆盖（F2 的「图层」按钮）优先，否则跟当前场景走。
			string key = LayoutPreferencePrefix + part + ".Layer";
			return PlayerPrefs.HasKey(key)
				? Mathf.Clamp(PlayerPrefs.GetInt(key), 0, 2)
				: Mathf.Clamp(m_sceneCharacterLayer, 0, 2);
		}

		/// <summary>
		/// 角色默认站位：按槽位（= npcSet 顺序）分边，纵向按缩放下沉，让头顶稳定落在画面上部。
		/// 三人同框时第三位站中间并后退半步，避免和左右两人叠成一团。
		/// </summary>
		private Vector2 GetCharacterDefaultPosition(string npcId, int slotIndex, int slotCount)
		{
			float x = slotIndex <= 0 ? -DefaultCharacterOffsetX : DefaultCharacterOffsetX;
			float y = Mathf.Min(0f, DefaultCharacterHeadTopY - CharacterSlotHeight * ReadCharacterScaleByNpc(npcId));
			if (slotCount >= 3 && slotIndex == 2)
			{
				x = 0f;
				y -= 40f;
			}
			return new Vector2(x, y);
		}

		/// <summary>没有槽位信息时的兜底（单人）：沿用「npcSet 第一个站左边」的老规则。</summary>
		private Vector2 GetCharacterDefaultPosition(string npcId)
		{
			int index = string.IsNullOrEmpty(npcId) ? -1 : GetSessionNpcSet().IndexOf(npcId);
			return GetCharacterDefaultPosition(npcId, index <= 0 ? 0 : index, 2);
		}

		private string GetSelectedCharacterLayoutPart()
		{
			SceneCharacterSlot slot = GetSelectedCharacterSlot();
			return slot != null ? GetCharacterSlotLayoutPart(slot) : GetCharacterLayoutPart();
		}

		private RectTransform GetSelectedCharacterRect()
		{
			SceneCharacterSlot slot = GetSelectedCharacterSlot();
			return slot != null ? slot.Rect : null;
		}

		private void EnsureLayoutEditButton()
		{
			if (m_btnLayoutEdit != null) return;
			var go = new GameObject("m_btnLayoutEdit", typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(transform, false);
			m_btnLayoutEdit = go.GetComponent<Button>();
			PlaceCornerButton(m_btnLayoutEdit, new Vector2(0f, 1f),
				ReadLayoutPosition("LayoutEditButton", new Vector2(430f, -28f)), new Vector2(200f, 56f));
			Image image = go.GetComponent<Image>();
			image.color = HudPaperButton;
			image.raycastTarget = true;
			ApplyHudSprite(image, "setting_save_popup_bg", false, gameObject.GetCancellationTokenOnDestroy(), true);
			m_layoutEditButtonLabel = CreateOverlayText(go.transform, "调整人物", 22f, Vector2.zero,
				new Vector2(180f, 48f), TextAlignmentOptions.Center);
			RectTransform labelRect = m_layoutEditButtonLabel.rectTransform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(8f, 4f);
			labelRect.offsetMax = new Vector2(-8f, -4f);
			m_btnLayoutEdit.onClick.AddListener(() =>
			{
				if (m_overlay != null) return;
				if (m_layoutEditMode) FinishLayoutEdit();
				else SetLayoutEditMode(true);
			});
		}

		private void InitializeLayoutEditing()
		{
			RegisterLayoutDragHandle(m_btnLayoutEdit != null ? m_btnLayoutEdit.transform as RectTransform : null, "LayoutEditButton");
			RegisterLayoutDragHandle(m_btnItemCollection != null ? m_btnItemCollection.transform as RectTransform : null, "Items");
			RegisterLayoutDragHandle(m_btnEmoCollection != null ? m_btnEmoCollection.transform as RectTransform : null, "Collection");
			RegisterLayoutDragHandle(m_btnSettings != null ? m_btnSettings.transform as RectTransform : null, "Menu");
			RegisterLayoutDragHandle(m_dialoguePanel != null ? m_dialoguePanel.transform as RectTransform : null, "DialoguePanel");
			RegisterLayoutDragHandle(m_scrollMemoryList != null ? m_scrollMemoryList.transform as RectTransform : null, "MemoryBar");
			RegisterLayoutDragHandle(m_tfReplayMessageNode as RectTransform, "ReplyPanel");
			RegisterLayoutDragHandle(m_tfReplayEmoNode as RectTransform, "EmotionPanel");
			RegisterLayoutDragHandle(m_tmpNpcSubtitle != null ? m_tmpNpcSubtitle.rectTransform : null, "NpcSubtitle");

			var hint = new GameObject("LayoutEditHint", typeof(RectTransform), typeof(Image));
			hint.transform.SetParent(transform, false);
			RectTransform hintRect = hint.GetComponent<RectTransform>();
			hintRect.anchorMin = hintRect.anchorMax = Vector2.zero;
			hintRect.pivot = Vector2.zero;
			hintRect.anchoredPosition = new Vector2(24f, 24f);
			hintRect.sizeDelta = new Vector2(1250f, 230f);
			Image background = hint.GetComponent<Image>();
			background.color = new Color(0.15f, 0.12f, 0.1f, 0.86f);
			background.raycastTarget = false;

			var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
			label.transform.SetParent(hint.transform, false);
			RectTransform labelRect = label.GetComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(16f, 82f);
			labelRect.offsetMax = new Vector2(-16f, -8f);
			TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
			if (m_tmpNpcSubtitle != null) text.font = m_tmpNpcSubtitle.font;
			text.text = "拖动人物与各 UI；点击人物可调整大小、图层、镜像";
			text.fontSize = 22f;
			text.color = Color.white;
			text.alignment = TextAlignmentOptions.Center;
			text.enableWordWrapping = true;
			text.raycastTarget = false;
			m_layoutPositionLabel = text;

			CreateLayoutButton(hintRect, "缩小", 18f, 115f, () => AdjustCharacterScale(-0.1f));
			m_layoutScaleLabel = CreateLayoutReadout(hintRect, "Scale", 140f, 150f);
			CreateLayoutButton(hintRect, "放大", 305f, 115f, () => AdjustCharacterScale(0.1f));
			m_layoutLayerLabel = CreateLayoutButton(hintRect, "图层", 435f, 205f, CycleCharacterLayer);
			m_layoutMirrorLabel = CreateLayoutButton(hintRect, "镜像：关", 650f, 205f, ToggleCharacterMirror);
			CreateLayoutButton(hintRect, "恢复默认", 865f, 160f, ResetLayoutPositions);
			CreateLayoutButton(hintRect, "完成调整", 1030f, 200f, FinishLayoutEdit);

			m_layoutHint = hint;
			hint.SetActive(false);
		}

		private TextMeshProUGUI CreateLayoutReadout(RectTransform parent, string name, float x, float width)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = Vector2.zero;
			rect.pivot = Vector2.zero;
			rect.anchoredPosition = new Vector2(x, 12f);
			rect.sizeDelta = new Vector2(width, 60f);
			TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
			if (m_tmpNpcSubtitle != null) label.font = m_tmpNpcSubtitle.font;
			label.fontSize = 27f;
			label.color = Color.white;
			label.alignment = TextAlignmentOptions.Center;
			label.raycastTarget = false;
			return label;
		}

		private TextMeshProUGUI CreateLayoutButton(RectTransform parent, string title, float x, float width, System.Action action)
		{
			var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = Vector2.zero;
			rect.pivot = Vector2.zero;
			rect.anchoredPosition = new Vector2(x, 12f);
			rect.sizeDelta = new Vector2(width, 60f);
			go.GetComponent<Image>().color = new Color(0.94f, 0.84f, 0.62f, 1f);
			go.GetComponent<Button>().onClick.AddListener(() => action());
			var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
			label.transform.SetParent(go.transform, false);
			RectTransform labelRect = label.GetComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
			TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
			if (m_tmpNpcSubtitle != null) text.font = m_tmpNpcSubtitle.font;
			text.text = title;
			text.fontSize = 27f;
			text.color = new Color(0.25f, 0.18f, 0.12f);
			text.alignment = TextAlignmentOptions.Center;
			text.raycastTarget = false;
			return text;
		}

		private void UpdateCharacterControls()
		{
			string selectedPart = GetSelectedCharacterLayoutPart();
			if (m_layoutScaleLabel != null) m_layoutScaleLabel.text = Mathf.RoundToInt(ReadCharacterScale(selectedPart) * 100f) + "%";
			if (m_layoutLayerLabel != null)
			{
				string[] names = { "图层：前景后", "图层：前景中", "图层：前景前" };
				m_layoutLayerLabel.text = names[ReadCharacterLayer(selectedPart)];
			}
			if (m_layoutMirrorLabel != null)
				m_layoutMirrorLabel.text = ReadCharacterMirror(selectedPart) ? "镜像：开" : "镜像：关";
			if (m_layoutPositionLabel != null)
				m_layoutPositionLabel.text = "拖动人物与各 UI；点击人物选择大小、图层和镜像\n" +
					BuildLayoutPositionSummary();
		}

		private string BuildLayoutPositionSummary()
		{
			return BuildCharacterPositionSummary() + "\n" +
				DescribeUiPosition("物品", m_btnItemCollection != null ? m_btnItemCollection.transform as RectTransform : null) + "  |  " +
				DescribeUiPosition("图鉴", m_btnEmoCollection != null ? m_btnEmoCollection.transform as RectTransform : null) + "  |  " +
				DescribeUiPosition("菜单", m_btnSettings != null ? m_btnSettings.transform as RectTransform : null) + "\n" +
				DescribeUiPosition("对话底板", m_dialoguePanel != null ? m_dialoguePanel.transform as RectTransform : null) + "  |  " +
				DescribeUiPosition("记忆条", m_scrollMemoryList != null ? m_scrollMemoryList.transform as RectTransform : null) + "  |  " +
				DescribeUiPosition("选项", m_tfReplayMessageNode as RectTransform) + "\n" +
				DescribeUiPosition("情绪", m_tfReplayEmoNode as RectTransform) + "  |  " +
				DescribeUiPosition("字幕", m_tmpNpcSubtitle != null ? m_tmpNpcSubtitle.rectTransform : null) + "  |  " +
				DescribeUiPosition("调整人物", m_btnLayoutEdit != null ? m_btnLayoutEdit.transform as RectTransform : null);
		}

		private static string DescribeUiPosition(string name, RectTransform rect)
		{
			if (rect == null) return string.Empty;
			Vector2 position = rect.anchoredPosition;
			return $"{name} X:{position.x:F0} Y:{position.y:F0}";
		}

		private string BuildCharacterPositionSummary()
		{
			string summary = string.Empty;
			for (int i = 0; i < m_characterSlots.Count; i++)
			{
				SceneCharacterSlot slot = m_characterSlots[i];
				if (slot == null || slot.Rect == null || !slot.Rect.gameObject.activeSelf) continue;
				string entry = DescribeCharacterPosition(slot.NpcId, slot.Rect, GetCharacterSlotLayoutPart(slot));
				summary = summary.Length == 0 ? entry : summary + "  |  " + entry;
			}
			return summary;
		}

		private string DescribeCharacterPosition(string npcId, RectTransform rect, string part)
		{
			if (rect == null) return string.Empty;
			Vector2 position = rect.anchoredPosition;
			return $"{npcId} X:{position.x:F0} Y:{position.y:F0} 大小:{ReadCharacterScale(part):F1} 图层:{ReadCharacterLayer(part)} 镜像:{(ReadCharacterMirror(part) ? "开" : "关")}";
		}

		private void FinishLayoutEdit()
		{
			SetLayoutEditMode(false);
			ShowOverlay("位置信息", BuildLayoutPositionSummary(),
				new[] { ("继续", (System.Action)(() => DestroyOverlay())) });
		}

		private void AdjustCharacterScale(float delta)
		{
			RectTransform rect = GetSelectedCharacterRect();
			if (!m_layoutEditMode || rect == null) return;
			string part = GetSelectedCharacterLayoutPart();
			string npcId = GetLayoutPartNpcId(part);
			float scale = Mathf.Clamp(Mathf.Round((ReadCharacterScale(part) + delta) * 10f) / 10f, 0.5f, 2f);
			// 缩放按角色存（不按幕存），调一次所有场景同步变
			PlayerPrefs.SetFloat(CharacterScaleKey(npcId), scale);
			TrackLayoutPart(part);
			PlayerPrefs.Save();
			rect.localScale = ReadCharacterVisualScale(part);
			UpdateCharacterControls();
		}

		private void ToggleCharacterMirror()
		{
			RectTransform rect = GetSelectedCharacterRect();
			if (!m_layoutEditMode || rect == null) return;
			string part = GetSelectedCharacterLayoutPart();
			PlayerPrefs.SetInt(LayoutPreferencePrefix + part + ".Mirror", ReadCharacterMirror(part) ? 0 : 1);
			TrackLayoutPart(part);
			PlayerPrefs.Save();
			rect.localScale = ReadCharacterVisualScale(part);
			UpdateCharacterControls();
		}

		private void CycleCharacterLayer()
		{
			if (!m_layoutEditMode || GetSelectedCharacterRect() == null) return;
			string part = GetSelectedCharacterLayoutPart();
			PlayerPrefs.SetInt(LayoutPreferencePrefix + part + ".Layer", (ReadCharacterLayer(part) + 1) % 3);
			TrackLayoutPart(part);
			PlayerPrefs.Save();
			RefreshSceneBackgroundAsync(m_currentStoryNode).Forget();
			UpdateCharacterControls();
		}

		private const string CharacterSlotPartPrefix = "CharacterSlot";

		/// <summary>角色槽的拖拽句柄：保存键按「槽里现在是谁」算，所以要把槽本身捕获进来。</summary>
		private void RegisterCharacterSlotDragHandle(SceneCharacterSlot slot)
		{
			if (slot == null || slot.Rect == null) return;
			RegisterLayoutDragHandle(slot.Rect, CharacterSlotPartPrefix + slot.Index,
				() => GetCharacterSlotLayoutPart(slot));
		}

		private void RegisterLayoutDragHandle(RectTransform rect, string part, System.Func<string> partProvider = null)
		{
			if (rect == null || !m_layoutDragTargets.Add(rect)) return;
			EventTrigger trigger = rect.GetComponent<EventTrigger>();
			if (trigger == null) trigger = rect.gameObject.AddComponent<EventTrigger>();

			var begin = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
			begin.callback.AddListener(_ => SelectLayoutCharacter(part));
			trigger.triggers.Add(begin);
			var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
			drag.callback.AddListener(data => DragLayoutPart(rect, (PointerEventData)data));
			trigger.triggers.Add(drag);
			var end = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
			end.callback.AddListener(_ =>
			{
				if (m_layoutEditMode && rect != null)
				{
					SaveLayoutPosition(partProvider != null ? partProvider() : part, rect);
					UpdateCharacterControls();
				}
			});
			trigger.triggers.Add(end);
			if (partProvider != null || part == "Character" || part == "OtherCharacter")
			{
				var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
				click.callback.AddListener(_ => SelectLayoutCharacter(part));
				trigger.triggers.Add(click);
			}
		}

		private void SelectLayoutCharacter(string part)
		{
			if (!m_layoutEditMode) return;
			if (!string.IsNullOrEmpty(part) && part.StartsWith(CharacterSlotPartPrefix, System.StringComparison.Ordinal))
			{
				int.TryParse(part.Substring(CharacterSlotPartPrefix.Length), out int index);
				m_layoutEditSlotIndex = Mathf.Clamp(index, 0, MaxSceneCharacterSlots - 1);
			}
			UpdateCharacterControls();
		}

		private void DragLayoutPart(RectTransform rect, PointerEventData pointer)
		{
			if (!m_layoutEditMode || m_overlay != null || rect == null || pointer == null) return;
			RectTransform parent = rect.parent as RectTransform;
			if (parent == null) return;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
				parent, pointer.position, pointer.pressEventCamera, out Vector2 current)) return;
			if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
				parent, pointer.position - pointer.delta, pointer.pressEventCamera, out Vector2 previous)) return;
			rect.anchoredPosition += current - previous;
			UpdateCharacterControls();
		}

		private void SetLayoutEditMode(bool enabled, bool savePositions = true)
		{
			if (m_layoutEditMode == enabled) return;
			if (enabled)
			{
				if (m_overlay != null) DestroyOverlay();
				m_layoutAdvanceWanted = m_btnDialogueAdvance != null && m_btnDialogueAdvance.gameObject.activeSelf;
			}
			m_layoutEditMode = enabled;
			if (m_layoutEditButtonLabel != null)
				m_layoutEditButtonLabel.text = enabled ? "完成调整" : "调整人物";
			if (enabled) m_layoutEditSlotIndex = 0;
			for (int i = 0; i < m_characterSlots.Count; i++)
			{
				SceneCharacterSlot slot = m_characterSlots[i];
				if (slot == null) continue;
				if (slot.Image != null) slot.Image.raycastTarget = enabled;
#if SPINE_UNITY && SPINE_CSHARP
				if (slot.Spine != null && !slot.Spine.IsDestroyed) slot.Spine.SetRaycastTarget(enabled);
#endif
			}
			if (m_dialoguePanel != null && m_dialoguePanel.TryGetComponent(out Image panelImage))
				panelImage.raycastTarget = enabled;
			if (m_tfReplayMessageNode != null && m_tfReplayMessageNode.TryGetComponent(out Image replyImage))
				replyImage.raycastTarget = enabled;
			if (m_tmpNpcSubtitle != null) m_tmpNpcSubtitle.raycastTarget = enabled;
			if (m_layoutHint != null)
			{
				m_layoutHint.SetActive(enabled);
				if (enabled)
				{
					m_layoutHint.transform.SetAsLastSibling();
					UpdateCharacterControls();
				}
			}
			if (enabled)
			{
				if (m_btnDialogueAdvance != null) m_btnDialogueAdvance.gameObject.SetActive(false);
			}
			else
			{
				if (savePositions)
				{
					if (m_btnItemCollection != null) SaveLayoutPosition("Items", m_btnItemCollection.transform as RectTransform);
					if (m_btnEmoCollection != null) SaveLayoutPosition("Collection", m_btnEmoCollection.transform as RectTransform);
					if (m_btnSettings != null) SaveLayoutPosition("Menu", m_btnSettings.transform as RectTransform);
					if (m_dialoguePanel != null) SaveLayoutPosition("DialoguePanel", m_dialoguePanel.transform as RectTransform);
					if (m_scrollMemoryList != null) SaveLayoutPosition("MemoryBar", m_scrollMemoryList.transform as RectTransform);
					if (m_tfReplayMessageNode != null) SaveLayoutPosition("ReplyPanel", m_tfReplayMessageNode as RectTransform);
					if (m_tfReplayEmoNode != null) SaveLayoutPosition("EmotionPanel", m_tfReplayEmoNode as RectTransform);
					if (m_tmpNpcSubtitle != null) SaveLayoutPosition("NpcSubtitle", m_tmpNpcSubtitle.rectTransform);
					if (m_btnLayoutEdit != null) SaveLayoutPosition("LayoutEditButton", m_btnLayoutEdit.transform as RectTransform);
					for (int i = 0; i < m_characterSlots.Count; i++)
					{
						SceneCharacterSlot slot = m_characterSlots[i];
						if (slot == null || slot.Rect == null || !slot.Rect.gameObject.activeSelf) continue;
						SaveLayoutPosition(GetCharacterSlotLayoutPart(slot), slot.Rect);
					}
				}
				SetDialogueAdvanceCatcherActive(m_layoutAdvanceWanted);
			}
		}

		private void ResetLayoutPositions()
		{
			var characterParts = new List<string>();
			for (int i = 0; i < m_characterSlots.Count; i++)
			{
				SceneCharacterSlot slot = m_characterSlots[i];
				if (slot == null) continue;
				characterParts.Add(GetCharacterSlotLayoutPart(slot));
			}

			var allParts = new List<string> { "Items", "Collection", "Menu", "DialoguePanel", "MemoryBar",
				"ReplyPanel", "EmotionPanel", "NpcSubtitle", "LayoutEditButton" };
			allParts.AddRange(characterParts);
			foreach (string part in allParts)
			{
				string key = LayoutPreferencePrefix + part;
				PlayerPrefs.DeleteKey(key + ".X");
				PlayerPrefs.DeleteKey(key + ".Y");
				if (characterParts.Contains(part))
				{
					PlayerPrefs.DeleteKey(key + ".Scale");
					PlayerPrefs.DeleteKey(key + ".Layer");
					PlayerPrefs.DeleteKey(key + ".Mirror");
				}
			}

			for (int i = 0; i < characterParts.Count; i++)
			{
				string npcId = GetLayoutPartNpcId(characterParts[i]);
				if (!string.IsNullOrEmpty(npcId)) PlayerPrefs.DeleteKey(CharacterScaleKey(npcId));
			}
			PlayerPrefs.Save();
			if (m_btnItemCollection != null)
				(m_btnItemCollection.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("Items", new Vector2(757f, -146f));
			if (m_btnEmoCollection != null)
				(m_btnEmoCollection.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("Collection", new Vector2(753f, -362f));
			if (m_btnSettings != null)
				(m_btnSettings.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("Menu", new Vector2(84.5f, -80.5f));
			if (m_dialoguePanel != null)
				(m_dialoguePanel.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("DialoguePanel", new Vector2(287f, -103.5f));
			if (m_scrollMemoryList != null)
				(m_scrollMemoryList.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("MemoryBar", new Vector2(297f, -59f));
			if (m_tfReplayMessageNode != null)
				(m_tfReplayMessageNode as RectTransform).anchoredPosition =
					ReadLayoutPosition("ReplyPanel", new Vector2(-77f, 14f));
			if (m_tfReplayEmoNode != null)
				(m_tfReplayEmoNode as RectTransform).anchoredPosition =
					ReadLayoutPosition("EmotionPanel", new Vector2(-77f, 14f));
			if (m_tmpNpcSubtitle != null)
				m_tmpNpcSubtitle.rectTransform.anchoredPosition =
					ReadLayoutPosition("NpcSubtitle", new Vector2(0f, 56f));
			if (m_btnLayoutEdit != null)
				(m_btnLayoutEdit.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("LayoutEditButton", new Vector2(430f, -28f));
			bool layoutChanged = false;
			for (int i = 0; i < m_characterSlots.Count; i++)
			{
				SceneCharacterSlot slot = m_characterSlots[i];
				if (slot == null || slot.Rect == null || !slot.Rect.gameObject.activeSelf) continue;
				string part = GetCharacterSlotLayoutPart(slot);
				slot.Rect.anchoredPosition = ReadLayoutPosition(part,
					GetCharacterDefaultPosition(slot.NpcId, i, m_characterSlots.Count));
				slot.Rect.localScale = ReadCharacterVisualScale(part);
				layoutChanged = true;
			}
			if (layoutChanged) RefreshSceneBackgroundAsync(m_currentStoryNode).Forget();
			UpdateCharacterControls();
			SetLayoutEditMode(false, false);
		}
	}
}
