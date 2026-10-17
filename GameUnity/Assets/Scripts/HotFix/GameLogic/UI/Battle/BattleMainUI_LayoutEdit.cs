using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
	public partial class BattleMainUI
	{
		private const string LayoutPreferencePrefix = "NarrativeHudLayout.";
		private readonly HashSet<RectTransform> m_layoutDragTargets = new HashSet<RectTransform>();
		private GameObject m_layoutHint;
		private TextMeshProUGUI m_layoutLayerLabel;
		private TextMeshProUGUI m_layoutScaleLabel;
		private bool m_layoutEditMode;
		private bool m_layoutAdvanceWanted;

		private string GetCharacterLayoutPart()
		{
			string npcId = m_currentStoryNode != null ? m_currentStoryNode.NpcId : null;
			return string.IsNullOrWhiteSpace(npcId) ? "Character" : "Character." + npcId.Trim();
		}

		private static Vector2 ReadLayoutPosition(string part, Vector2 fallback)
		{
			string key = LayoutPreferencePrefix + part;
			if (!PlayerPrefs.HasKey(key + ".X") || !PlayerPrefs.HasKey(key + ".Y"))
			{
				return fallback;
			}
			return new Vector2(PlayerPrefs.GetFloat(key + ".X"), PlayerPrefs.GetFloat(key + ".Y"));
		}

		private static void SaveLayoutPosition(string part, RectTransform rect)
		{
			string key = LayoutPreferencePrefix + part;
			PlayerPrefs.SetFloat(key + ".X", rect.anchoredPosition.x);
			PlayerPrefs.SetFloat(key + ".Y", rect.anchoredPosition.y);
			PlayerPrefs.Save();
		}

		private float ReadCharacterScale()
		{
			return Mathf.Clamp(PlayerPrefs.GetFloat(LayoutPreferencePrefix + GetCharacterLayoutPart() + ".Scale", 1f), 0.5f, 2f);
		}

		private int ReadCharacterLayer()
		{
			return Mathf.Clamp(PlayerPrefs.GetInt(LayoutPreferencePrefix + GetCharacterLayoutPart() + ".Layer", 0), 0, 2);
		}

		private void InitializeLayoutEditing()
		{
			RegisterLayoutDragHandle(m_btnItemCollection != null ? m_btnItemCollection.transform as RectTransform : null, "Items");
			RegisterLayoutDragHandle(m_btnEmoCollection != null ? m_btnEmoCollection.transform as RectTransform : null, "Collection");

			var hint = new GameObject("LayoutEditHint", typeof(RectTransform), typeof(Image));
			hint.transform.SetParent(transform, false);
			RectTransform hintRect = hint.GetComponent<RectTransform>();
			hintRect.anchorMin = hintRect.anchorMax = Vector2.zero;
			hintRect.pivot = Vector2.zero;
			hintRect.anchoredPosition = new Vector2(24f, 24f);
			hintRect.sizeDelta = new Vector2(1250f, 142f);
			Image background = hint.GetComponent<Image>();
			background.color = new Color(0.15f, 0.12f, 0.1f, 0.86f);
			background.raycastTarget = false;

			var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
			label.transform.SetParent(hint.transform, false);
			RectTransform labelRect = label.GetComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(16f, 90f);
			labelRect.offsetMax = new Vector2(-16f, -8f);
			TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
			if (m_tmpNpcSubtitle != null) text.font = m_tmpNpcSubtitle.font;
			text.text = "拖动人物、物品、图鉴；大小和图层按当前人物分别保存";
			text.fontSize = 25f;
			text.color = Color.white;
			text.alignment = TextAlignmentOptions.Center;
			text.enableWordWrapping = false;
			text.raycastTarget = false;

			CreateLayoutButton(hintRect, "缩小", 18f, 115f, () => AdjustCharacterScale(-0.1f));
			m_layoutScaleLabel = CreateLayoutReadout(hintRect, "Scale", 140f, 150f);
			CreateLayoutButton(hintRect, "放大", 305f, 115f, () => AdjustCharacterScale(0.1f));
			m_layoutLayerLabel = CreateLayoutButton(hintRect, "图层", 435f, 255f, CycleCharacterLayer);
			CreateLayoutButton(hintRect, "恢复默认", 705f, 190f, ResetLayoutPositions);
			CreateLayoutButton(hintRect, "完成调整", 910f, 215f, () => SetLayoutEditMode(false));

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
			if (m_layoutScaleLabel != null) m_layoutScaleLabel.text = Mathf.RoundToInt(ReadCharacterScale() * 100f) + "%";
			if (m_layoutLayerLabel != null)
			{
				string[] names = { "图层：前景后", "图层：前景中", "图层：前景前" };
				m_layoutLayerLabel.text = names[ReadCharacterLayer()];
			}
		}

		private void AdjustCharacterScale(float delta)
		{
			if (!m_layoutEditMode || m_rtSceneCharacter == null) return;
			float scale = Mathf.Clamp(Mathf.Round((ReadCharacterScale() + delta) * 10f) / 10f, 0.5f, 2f);
			PlayerPrefs.SetFloat(LayoutPreferencePrefix + GetCharacterLayoutPart() + ".Scale", scale);
			PlayerPrefs.Save();
			m_rtSceneCharacter.localScale = Vector3.one * scale;
			UpdateCharacterControls();
		}

		private void CycleCharacterLayer()
		{
			if (!m_layoutEditMode || m_rtSceneCharacter == null) return;
			PlayerPrefs.SetInt(LayoutPreferencePrefix + GetCharacterLayoutPart() + ".Layer", (ReadCharacterLayer() + 1) % 3);
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
			begin.callback.AddListener(_ => { });
			trigger.triggers.Add(begin);
			var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
			drag.callback.AddListener(data => DragLayoutPart(rect, (PointerEventData)data));
			trigger.triggers.Add(drag);
			var end = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
			end.callback.AddListener(_ =>
			{
				if (m_layoutEditMode && rect != null)
					SaveLayoutPosition(part == "Character" ? GetCharacterLayoutPart() : part, rect);
			});
			trigger.triggers.Add(end);
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
		}

		private void SetLayoutEditMode(bool enabled)
		{
			if (m_layoutEditMode == enabled) return;
			if (enabled)
			{
				if (m_overlay != null) DestroyOverlay();
				m_layoutAdvanceWanted = m_btnDialogueAdvance != null && m_btnDialogueAdvance.gameObject.activeSelf;
			}
			m_layoutEditMode = enabled;
			if (m_imgSceneCharacter != null) m_imgSceneCharacter.raycastTarget = enabled;
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
				if (m_btnItemCollection != null) SaveLayoutPosition("Items", m_btnItemCollection.transform as RectTransform);
				if (m_btnEmoCollection != null) SaveLayoutPosition("Collection", m_btnEmoCollection.transform as RectTransform);
				if (m_rtSceneCharacter != null) SaveLayoutPosition(GetCharacterLayoutPart(), m_rtSceneCharacter);
				SetDialogueAdvanceCatcherActive(m_layoutAdvanceWanted);
			}
		}

		private void ResetLayoutPositions()
		{
			foreach (string part in new[] { "Items", "Collection", GetCharacterLayoutPart() })
			{
				string key = LayoutPreferencePrefix + part;
				PlayerPrefs.DeleteKey(key + ".X");
				PlayerPrefs.DeleteKey(key + ".Y");
				if (part == GetCharacterLayoutPart())
				{
					PlayerPrefs.DeleteKey(key + ".Scale");
					PlayerPrefs.DeleteKey(key + ".Layer");
				}
			}
			PlayerPrefs.Save();
			if (m_btnItemCollection != null)
				(m_btnItemCollection.transform as RectTransform).anchoredPosition = new Vector2(750f, -150f);
			if (m_btnEmoCollection != null)
				(m_btnEmoCollection.transform as RectTransform).anchoredPosition = new Vector2(750f, -360f);
			if (m_rtSceneCharacter != null)
			{
				m_rtSceneCharacter.anchoredPosition = new Vector2(-650f, 0f);
				m_rtSceneCharacter.localScale = Vector3.one;
				RefreshSceneBackground(m_currentStoryNode);
			}
			UpdateCharacterControls();
		}
	}
}
