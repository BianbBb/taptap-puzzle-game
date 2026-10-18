using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DGame;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameLogic
{
	public partial class BattleMainUI
	{
		private int m_settingsSelectedSlot = 1;
		private bool m_settingsLayoutEditMode;
		private TextMeshProUGUI m_settingsPositionReadout;

		private void ShowPsdSettings()
		{
			HideMemoryTooltip();
			DestroyOverlay();
			m_settingsPositionReadout = null;
			EnsureEventSystem();
			var blocker = new GameObject("NarrativeOverlay", typeof(RectTransform), typeof(Image));
			blocker.transform.SetParent(transform, false);
			blocker.transform.SetAsLastSibling();
			RectTransform root = blocker.GetComponent<RectTransform>();
			root.anchorMin = Vector2.zero;
			root.anchorMax = Vector2.one;
			root.offsetMin = Vector2.zero;
			root.offsetMax = Vector2.zero;
			Image blockerImage = blocker.GetComponent<Image>();
			blockerImage.color = new Color(0f, 0f, 0f, 0f);
			blockerImage.raycastTarget = true;
			m_overlay = blocker;

			CreatePsdImage(root, "Panel", "setting_bg", 210f, 187f, 1421f, 815f);
			CreatePsdButton(root, "Back", "setting_back_btn", 358f, 198f, 106f, 108f,
				string.Empty, () => ShowSettingsConfirmation(string.Empty,
					"是否回到主菜单，您未保存的内容将会<color=#E76B50>消失</color>",
					ReturnToMainMenu, true));
			CreatePsdButton(root, "Close", "setting_close_btn", 1433f, 156f, 220f, 137f,
				string.Empty, CloseSettingsOverlay);
			CreatePsdImage(root, "VolumeLabel", "setting_volume_text", 437f, 345f, 233f, 67f);
			CreatePsdImage(root, "SaveLabel", "setting_save_text", 439f, 453f, 233f, 67f);

			RectTransform sliderRect = CreatePsdRect(root, "VolumeSlider", 738f, 350f, 750f, 63f);
			Image sliderHitArea = sliderRect.gameObject.AddComponent<Image>();
			sliderHitArea.color = new Color(1f, 1f, 1f, 0.001f);
			sliderHitArea.raycastTarget = true;
			Slider slider = sliderRect.gameObject.AddComponent<Slider>();
			slider.transition = Selectable.Transition.None;
			CreatePsdImage(sliderRect, "Track", "setting_volume_slider", 0f, 25f, 750f, 13f, false);
			RectTransform handleArea = CreateLocalRect(sliderRect, "HandleArea", Vector2.zero,
				new Vector2(750f, 63f));
			Image handleImage = CreatePsdImage(handleArea, "Handle", "setting_volume_handle",
				349.5f, 6f, 51f, 51f, false);
			handleImage.rectTransform.anchoredPosition = Vector2.zero;
			slider.handleRect = handleImage.rectTransform;
			slider.targetGraphic = handleImage;
			slider.minValue = 0f;
			slider.maxValue = 1f;
			slider.wholeNumbers = false;
			slider.SetValueWithoutNotify(Mathf.Clamp01(PlayerPrefs.GetFloat("NarrativeMasterVolume", 1f)));
			AudioListener.volume = slider.value;
			slider.onValueChanged.AddListener(value =>
			{
				if (m_settingsLayoutEditMode) return;
				AudioListener.volume = value;
				PlayerPrefs.SetFloat("NarrativeMasterVolume", value);
				PlayerPrefs.Save();
			});

			for (int slot = 1; slot <= NarrativeSaveData.ManualSaveSlotCount; slot++)
			{
				int selected = slot;
				float x = 738f + ((slot - 1) % 3) * 262f;
				float y = 465f + ((slot - 1) / 3) * 124f;
				bool hasSave = NarrativeSaveData.Get.HasSaveSlot(slot);
				RectTransform slotRoot = CreatePsdRect(root, "SaveSlot" + slot, x, y, 226f, 94f);
				Image slotHitArea = slotRoot.gameObject.AddComponent<Image>();
				slotHitArea.color = new Color(1f, 1f, 1f, 0.001f);
				slotHitArea.raycastTarget = true;
				if (hasSave || slot == m_settingsSelectedSlot)
					CreatePsdImage(slotRoot, "Background", "setting_save_slot_bg", 0f, 0f, 226f, 94f, false);
				else
					CreatePsdImage(slotRoot, "Border", "setting_save_slot_border", 0f, 0f, 226f, 94f, false);
				if (slot == m_settingsSelectedSlot)
					CreatePsdImage(slotRoot, "Selected", "setting_save_slot_highlight",
						-16f, -18f, 258f, 130f, false);
				if (hasSave || slot == m_settingsSelectedSlot)
					CreatePsdText(slotRoot, "SlotLabel", "存档" + slot, 34f,
						Vector2.zero, new Vector2(226f, 70f)).color = Color.white;
				Button slotButton = slotRoot.gameObject.AddComponent<Button>();
				slotButton.transition = Selectable.Transition.None;
				slotButton.targetGraphic = slotHitArea;
				slotButton.onClick.AddListener(() =>
				{
					if (m_settingsLayoutEditMode)
					{
						ShowSettingsPosition(slotRoot);
						return;
					}
					bool wasSelected = m_settingsSelectedSlot == selected;
					if (!NarrativeSaveData.Get.HasSaveSlot(selected))
					{
						ShowSettingsConfirmation("存档" + selected, "是否保留游戏进度？", () =>
						{
							NarrativeSaveData.Get.SaveToSlot(selected);
							m_settingsSelectedSlot = selected;
							ShowPsdSettings();
						});
					}
					else if (wasSelected)
						ShowSettingsConfirmation("存档" + selected, "该档位已有存档，是否覆盖？",
							() => { NarrativeSaveData.Get.SaveToSlot(selected); ShowPsdSettings(); });
					else
					{
						m_settingsSelectedSlot = selected;
						ShowPsdSettings();
					}
				});
			}

			CreatePsdButton(root, "Load", "setting_save_btn", 811f, 771f, 322f, 114f,
				"读取存档", () =>
				{
					int slot = m_settingsSelectedSlot;
					if (!NarrativeSaveData.Get.HasSaveSlot(slot))
					{
					ShowSettingsConfirmation("提示", "请先选择已有存档。", null);
						return;
					}
					ShowSettingsConfirmation("存档" + slot, "读取这个存档？", () => LoadSaveSlot(slot));
				});
			CreatePsdButton(root, "Delete", "setting_save_btn", 1164f, 770f, 310f, 102f,
				"删除存档", () =>
				{
					int slot = m_settingsSelectedSlot;
					if (!NarrativeSaveData.Get.HasSaveSlot(slot)) return;
					ShowSettingsConfirmation("存档" + slot, "是否删除这个存档？", () =>
					{
						NarrativeSaveData.Get.DeleteSaveSlot(slot);
						m_settingsSelectedSlot = 0;
						ShowPsdSettings();
					});
				});
			if (m_settingsLayoutEditMode)
			{
				m_settingsPositionReadout = CreatePsdText(root, "PositionReadout",
					"设置布局编辑：拖动任一控件查看坐标，按 F2 完成", 24f,
					new Vector2(0f, 478f), new Vector2(1500f, 56f));
				m_settingsPositionReadout.color = Color.white;
			}
		}

		private void ShowSettingsConfirmation(string title, string message, Action confirm,
			bool returnToMenu = false)
		{
			if (m_overlay == null) return;
			Transform old = m_overlay.transform.Find("ConfirmationDim");
			if (old != null) UnityEngine.Object.Destroy(old.gameObject);
			RectTransform parent = m_overlay.transform as RectTransform;
			var dim = new GameObject("ConfirmationDim", typeof(RectTransform), typeof(Image));
			dim.transform.SetParent(parent, false);
			RectTransform dimRect = dim.GetComponent<RectTransform>();
			dimRect.anchorMin = Vector2.zero;
			dimRect.anchorMax = Vector2.one;
			dimRect.offsetMin = Vector2.zero;
			dimRect.offsetMax = Vector2.zero;
			Image dimImage = dim.GetComponent<Image>();
			dimImage.color = new Color(0.08f, 0.06f, 0.04f, 0.56f);
			dimImage.raycastTarget = true;
			CreatePsdImage(dimRect, "Popup", "setting_save_popup_bg", 477f, 368f, 1054f, 378f);
			if (!returnToMenu)
				CreatePsdText(dimRect, "PopupTitle", "[ " + title + " ]", 48f,
					new Vector2(0f, 91f), new Vector2(700f, 70f));
			CreatePsdText(dimRect, "PopupMessage", message, returnToMenu ? 36f : 30f,
				returnToMenu ? new Vector2(0f, -20f) : new Vector2(0f, -60f),
				returnToMenu ? new Vector2(930f, 110f) : new Vector2(820f, 110f));
			if (confirm != null)
				CreatePsdButton(dimRect, "Confirm", "setting_save_btn", 579f, 782f, 310f, 102f,
					returnToMenu ? "是" : "确定", confirm);
			CreatePsdButton(dimRect, "Cancel", "setting_save_btn", 1029f, 779f, 310f, 102f,
				returnToMenu ? "否" : confirm == null ? "确定" : "取消",
				() => UnityEngine.Object.Destroy(dim));
		}

		private RectTransform CreatePsdRect(Transform parent, string name,
			float x, float y, float width, float height)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			Vector2 defaultPosition = new Vector2(x + width * 0.5f - 960f, 540f - y - height * 0.5f);
			bool isSettingsPart = m_overlay != null && parent == m_overlay.transform;
			rect.anchoredPosition = isSettingsPart
				? ReadLayoutPosition("Settings." + name, defaultPosition)
				: defaultPosition;
			rect.sizeDelta = new Vector2(width, height);
			if (isSettingsPart) RegisterSettingsLayoutDrag(rect);
			return rect;
		}

		private void RegisterSettingsLayoutDrag(RectTransform rect)
		{
			EventTrigger trigger = rect.gameObject.AddComponent<EventTrigger>();
			var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
			drag.callback.AddListener(data =>
			{
				if (!m_settingsLayoutEditMode || rect == null) return;
				PointerEventData pointer = (PointerEventData)data;
				RectTransform parent = rect.parent as RectTransform;
				if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pointer.position,
					pointer.pressEventCamera, out Vector2 current)) return;
				if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, pointer.position - pointer.delta,
					pointer.pressEventCamera, out Vector2 previous)) return;
				rect.anchoredPosition += current - previous;
				ShowSettingsPosition(rect);
			});
			trigger.triggers.Add(drag);
			var end = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
			end.callback.AddListener(_ =>
			{
				if (!m_settingsLayoutEditMode || rect == null) return;
				SaveLayoutPosition("Settings." + rect.name, rect);
				ShowSettingsPosition(rect);
			});
			trigger.triggers.Add(end);
		}

		private void ShowSettingsPosition(RectTransform rect)
		{
			if (m_settingsPositionReadout == null || rect == null) return;
			m_settingsPositionReadout.text = rect.name + "  X:" + rect.anchoredPosition.x.ToString("F0") +
				"  Y:" + rect.anchoredPosition.y.ToString("F0") + "  （按 F2 完成）";
		}

		private void ToggleSettingsLayoutEdit()
		{
			m_settingsLayoutEditMode = !m_settingsLayoutEditMode;
			ShowPsdSettings();
		}

		private static RectTransform CreateLocalRect(Transform parent, string name,
			Vector2 position, Vector2 size)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = position;
			rect.sizeDelta = size;
			return rect;
		}

		private Image CreatePsdImage(Transform parent, string name, string sprite,
			float x, float y, float width, float height, bool canvasCoordinates = true)
		{
			RectTransform rect = canvasCoordinates
				? CreatePsdRect(parent, name, x, y, width, height)
				: CreateLocalRect(parent, name,
					new Vector2(x + width * 0.5f - (parent as RectTransform).rect.width * 0.5f,
						(parent as RectTransform).rect.height * 0.5f - y - height * 0.5f),
					new Vector2(width, height));
			Image image = rect.gameObject.AddComponent<Image>();
			image.raycastTarget = m_settingsLayoutEditMode && image.GetComponent<EventTrigger>() != null;
			CancellationToken token = gameObject.GetCancellationTokenOnDestroy();
			image.SetSprite(sprite, false, loaded =>
			{
				if (loaded == null || IsDestroyed) return;
				loaded.type = Image.Type.Simple;
				loaded.color = Color.white;
				loaded.raycastTarget = loaded.GetComponent<Button>() != null ||
					(m_settingsLayoutEditMode && loaded.GetComponent<EventTrigger>() != null);
			}, token);
			return image;
		}

		private TextMeshProUGUI CreatePsdText(Transform parent, string name, string value, float fontSize,
			Vector2 position, Vector2 size)
		{
			RectTransform rect = CreateLocalRect(parent, name, position, size);
			TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
			if (m_tmpNpcSubtitle != null) label.font = m_tmpNpcSubtitle.font;
			label.text = value;
			label.fontSize = fontSize;
			label.color = HudPaperText;
			label.alignment = TextAlignmentOptions.Center;
			label.enableWordWrapping = true;
			label.overflowMode = TextOverflowModes.Overflow;
			label.raycastTarget = false;
			return label;
		}

		private void CreatePsdButton(Transform parent, string name, string sprite,
			float x, float y, float width, float height, string label, Action action)
		{
			Image image = CreatePsdImage(parent, name, sprite, x, y, width, height);
			image.raycastTarget = true;
			Button button = image.gameObject.AddComponent<Button>();
			button.targetGraphic = image;
			button.onClick.AddListener(() =>
			{
				if (m_settingsLayoutEditMode)
				{
					ShowSettingsPosition(image.rectTransform);
					return;
				}
				action?.Invoke();
			});
			if (!string.IsNullOrEmpty(label))
				CreatePsdText(image.transform, "Label", label, 27f, Vector2.zero,
					new Vector2(width - 50f, height - 24f));
		}
	}
}
