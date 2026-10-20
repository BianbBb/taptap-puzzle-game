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
		private readonly HashSet<RectTransform> m_layoutDragTargets = new HashSet<RectTransform>();
		private GameObject m_layoutHint;
		private Button m_btnLayoutEdit;
		private TextMeshProUGUI m_layoutEditButtonLabel;
		private TextMeshProUGUI m_layoutLayerLabel;
		private TextMeshProUGUI m_layoutMirrorLabel;
		private TextMeshProUGUI m_layoutScaleLabel;
		private TextMeshProUGUI m_layoutPositionLabel;
		private bool m_layoutEditMode;
		private bool m_layoutEditingOtherCharacter;
		private bool m_layoutAdvanceWanted;

		private string GetCharacterLayoutPart()
		{
			string npcId = m_currentStoryNode != null ? m_currentStoryNode.NpcId : null;
			return GetCharacterLayoutPart(npcId, IsNpcDialogueTurn(m_currentStoryNode));
		}

		private string GetOtherCharacterLayoutPart()
			=> GetCharacterLayoutPart(m_otherCharacterNpcId, true);

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

		private float ReadCharacterScale()
		{
			return ReadCharacterScale(GetCharacterLayoutPart());
		}

		private static float ReadCharacterScale(string part)
		{
			string key = LayoutPreferencePrefix + part + ".Scale";
			float fallback = NarrativeLayoutDefaults.TryGetScale(part, out float scale) ? scale : 1f;
			return Mathf.Clamp(PlayerPrefs.GetFloat(key, fallback), 0.5f, 2f);
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

		private int ReadOtherCharacterLayer()
			=> ReadCharacterLayer(GetOtherCharacterLayoutPart());

		private static int ReadCharacterLayer(string part)
		{
			string key = LayoutPreferencePrefix + part + ".Layer";
			int fallback = NarrativeLayoutDefaults.TryGetLayer(part, out int layer) ? layer : 0;
			return Mathf.Clamp(PlayerPrefs.GetInt(key, fallback), 0, 2);
		}

		private string GetSelectedCharacterLayoutPart()
			=> m_layoutEditingOtherCharacter ? GetOtherCharacterLayoutPart() : GetCharacterLayoutPart();

		private RectTransform GetSelectedCharacterRect()
			=> m_layoutEditingOtherCharacter ? m_rtOtherCharacter : m_rtSceneCharacter;

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
			RegisterLayoutDragHandle(m_btnRestart != null ? m_btnRestart.transform as RectTransform : null, "Restart");

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
				DescribeUiPosition("重开", m_btnRestart != null ? m_btnRestart.transform as RectTransform : null) + "  |  " +
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
			string current = DescribeCharacterPosition(m_currentStoryNode?.NpcId, m_rtSceneCharacter,
				GetCharacterLayoutPart());
			if (m_rtOtherCharacter == null || !m_rtOtherCharacter.gameObject.activeSelf)
				return current;
			return current + "  |  " + DescribeCharacterPosition(m_otherCharacterNpcId, m_rtOtherCharacter,
				GetOtherCharacterLayoutPart());
		}

		private static string DescribeCharacterPosition(string npcId, RectTransform rect, string part)
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
			float scale = Mathf.Clamp(Mathf.Round((ReadCharacterScale(part) + delta) * 10f) / 10f, 0.5f, 2f);
			PlayerPrefs.SetFloat(LayoutPreferencePrefix + part + ".Scale", scale);
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
			RefreshSceneBackground(m_currentStoryNode);
			UpdateCharacterControls();
		}

		private void RegisterLayoutDragHandle(RectTransform rect, string part)
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
					SaveLayoutPosition(part == "Character" ? GetCharacterLayoutPart() :
						part == "OtherCharacter" ? GetOtherCharacterLayoutPart() : part, rect);
					UpdateCharacterControls();
				}
			});
			trigger.triggers.Add(end);
			if (part == "Character" || part == "OtherCharacter")
			{
				var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
				click.callback.AddListener(_ => SelectLayoutCharacter(part));
				trigger.triggers.Add(click);
			}
		}

		private void SelectLayoutCharacter(string part)
		{
			if (!m_layoutEditMode) return;
			m_layoutEditingOtherCharacter = part == "OtherCharacter";
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
			if (enabled) m_layoutEditingOtherCharacter = false;
			if (m_imgSceneCharacter != null) m_imgSceneCharacter.raycastTarget = enabled;
			if (m_imgOtherCharacter != null) m_imgOtherCharacter.raycastTarget = enabled;
#if SPINE_UNITY && SPINE_CSHARP
			if (m_npcSpine != null && !m_npcSpine.IsDestroyed) m_npcSpine.SetRaycastTarget(enabled);
#endif
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
					if (m_btnRestart != null) SaveLayoutPosition("Restart", m_btnRestart.transform as RectTransform);
					if (m_btnLayoutEdit != null) SaveLayoutPosition("LayoutEditButton", m_btnLayoutEdit.transform as RectTransform);
					if (m_rtSceneCharacter != null) SaveLayoutPosition(GetCharacterLayoutPart(), m_rtSceneCharacter);
					if (m_rtOtherCharacter != null && m_rtOtherCharacter.gameObject.activeSelf)
						SaveLayoutPosition(GetOtherCharacterLayoutPart(), m_rtOtherCharacter);
				}
				SetDialogueAdvanceCatcherActive(m_layoutAdvanceWanted);
			}
		}

		private void ResetLayoutPositions()
		{
			foreach (string part in new[] { "Items", "Collection", "Menu", "DialoguePanel", "MemoryBar", "ReplyPanel",
				"EmotionPanel", "NpcSubtitle", "Restart", "LayoutEditButton",
				GetCharacterLayoutPart(), GetOtherCharacterLayoutPart() })
			{
				string key = LayoutPreferencePrefix + part;
				PlayerPrefs.DeleteKey(key + ".X");
				PlayerPrefs.DeleteKey(key + ".Y");
				if (part == GetCharacterLayoutPart() || part == GetOtherCharacterLayoutPart())
				{
					PlayerPrefs.DeleteKey(key + ".Scale");
					PlayerPrefs.DeleteKey(key + ".Layer");
					PlayerPrefs.DeleteKey(key + ".Mirror");
				}
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
					ReadLayoutPosition("ReplyPanel", new Vector2(0f, 14f));
			if (m_tfReplayEmoNode != null)
				(m_tfReplayEmoNode as RectTransform).anchoredPosition =
					ReadLayoutPosition("EmotionPanel", new Vector2(0f, 14f));
			if (m_tmpNpcSubtitle != null)
				m_tmpNpcSubtitle.rectTransform.anchoredPosition =
					ReadLayoutPosition("NpcSubtitle", new Vector2(0f, 56f));
			if (m_btnRestart != null)
				(m_btnRestart.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("Restart", new Vector2(210f, -28f));
			if (m_btnLayoutEdit != null)
				(m_btnLayoutEdit.transform as RectTransform).anchoredPosition =
					ReadLayoutPosition("LayoutEditButton", new Vector2(430f, -28f));
			if (m_rtSceneCharacter != null)
			{
				m_rtSceneCharacter.anchoredPosition = ReadLayoutPosition(GetCharacterLayoutPart(), GetActiveCharacterDefaultPosition());
				m_rtSceneCharacter.localScale = ReadCharacterVisualScale(GetCharacterLayoutPart());
				RefreshSceneBackground(m_currentStoryNode);
			}
			if (m_rtOtherCharacter != null && m_rtOtherCharacter.gameObject.activeSelf)
			{
				List<string> npcSet = GetSessionNpcSet();
				m_rtOtherCharacter.anchoredPosition = ReadLayoutPosition(GetOtherCharacterLayoutPart(),
					new Vector2(npcSet.IndexOf(m_otherCharacterNpcId) == 1 ? 850f : -850f, 0f));
				m_rtOtherCharacter.localScale = ReadCharacterVisualScale(GetOtherCharacterLayoutPart());
			}
			UpdateCharacterControls();
			SetLayoutEditMode(false, false);
		}
	}
}
