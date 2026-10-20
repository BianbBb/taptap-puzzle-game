using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameEditor
{
	/// <summary>
	/// 生成 NPC 字幕面板 prefab，并把它作为嵌套实例挂进 BattleMainUI.prefab。
	/// 字号、颜色、间距都在 prefab 上调整，运行时代码只负责填文本和打字机。
	/// </summary>
	public static class NpcSubtitlePrefabBuilder
	{
		private const string PrefabPath = "Assets/BundleAssets/UI/NpcSubtitlePanel.prefab";
		private const string IconPath = "Assets/BundleAssets/UI/Icons/SubtitleContinue.png";
		private const string HostPrefabPath = "Assets/BundleAssets/UI/BattleMainUI.prefab";
		private const string RootName = "NpcSubtitlePanel";

		private const float PanelWidth = 1180f;
		private const float PanelHeight = 220f;
		private const float PanelBottom = 100f;
		private const float NameInsetLeft = 36f;
		private const float NameOverlap = 12f;
		private const float BodyTopInset = 58f;
		private const float BodyBottomInset = 46f;
		private const float BodySideInset = 140f;

		private static readonly Color NameColor = new Color(0.243f, 0.161f, 0.086f, 1f);
		private static readonly Color BodyColor = new Color(0.290f, 0.239f, 0.188f, 1f);
		private static readonly Color PanelColor = new Color(0.992f, 0.984f, 0.961f, 0.80f);
		private static readonly Color BorderColor = new Color(0.557f, 0.478f, 0.361f, 0.95f);
		private static readonly Color HintColor = new Color(0.353f, 0.263f, 0.153f, 0.55f);
		private static readonly Color HintLabelColor = new Color(0.353f, 0.263f, 0.153f, 0.38f);
		private const float BorderThickness = 1.5f;

		[MenuItem("DGame Tools/剧情/重建 NPC 字幕 Prefab")]
		public static void RebuildAll()
		{
			EnsureIcon();
			BuildPrefab();
			EmbedIntoBattleMainUI();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			Debug.Log($"[NpcSubtitle] 已生成 {PrefabPath} 并挂到 {HostPrefabPath}");
		}

		private static void EnsureIcon()
		{
			if (File.Exists(IconPath))
			{
				return;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(IconPath) ?? "Assets/BundleAssets/UI/Icons");
			const int size = 96;
			Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			Color32[] pixels = new Color32[size * size];
			Vector2[] stem = { new Vector2(60f, 84f), new Vector2(60f, 50f), new Vector2(52f, 36f), new Vector2(36f, 32f) };
			Vector2[] head = { new Vector2(40f, 16f), new Vector2(22f, 32f), new Vector2(40f, 48f) };
			const float halfWidth = 3.6f;
			const float feather = 1.2f;

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
					float d = DistanceToPolyline(p, stem);
					d = Mathf.Min(d, DistanceToPolyline(p, head));
					float alpha = Mathf.Clamp01((halfWidth + feather - d) / feather);
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
				}
			}

			tex.SetPixels32(pixels);
			tex.Apply();
			File.WriteAllBytes(IconPath, tex.EncodeToPNG());
			Object.DestroyImmediate(tex);
			AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);

			TextureImporter importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
			if (importer != null)
			{
				importer.textureType = TextureImporterType.Sprite;
				importer.spriteImportMode = SpriteImportMode.Single;
				importer.spritePixelsPerUnit = 100f;
				importer.filterMode = FilterMode.Bilinear;
				importer.mipmapEnabled = false;
				importer.alphaIsTransparency = true;
				importer.textureCompression = TextureImporterCompression.Uncompressed;
				importer.SaveAndReimport();
			}
		}

		private static float DistanceToPolyline(Vector2 p, Vector2[] points)
		{
			float best = float.MaxValue;
			for (int i = 0; i < points.Length - 1; i++)
			{
				best = Mathf.Min(best, DistanceToSegment(p, points[i], points[i + 1]));
			}

			return best;
		}

		private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float lengthSq = ab.sqrMagnitude;
			if (lengthSq < 0.0001f)
			{
				return Vector2.Distance(p, a);
			}

			float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
			return Vector2.Distance(p, a + (ab * t));
		}

		private static void BuildPrefab()
		{
			GameObject root = new GameObject(RootName, typeof(RectTransform));
			RectTransform rootRect = root.GetComponent<RectTransform>();
			rootRect.anchorMin = new Vector2(0.5f, 0f);
			rootRect.anchorMax = new Vector2(0.5f, 0f);
			rootRect.pivot = new Vector2(0.5f, 0f);
			rootRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
			rootRect.anchoredPosition = new Vector2(0f, PanelBottom);

			GameObject frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
			frame.transform.SetParent(root.transform, false);
			RectTransform frameRect = frame.GetComponent<RectTransform>();
			frameRect.anchorMin = Vector2.zero;
			frameRect.anchorMax = Vector2.one;
			frameRect.offsetMin = Vector2.zero;
			frameRect.offsetMax = Vector2.zero;
			Image frameImage = frame.GetComponent<Image>();
			frameImage.color = PanelColor;
			frameImage.raycastTarget = false;

			CreateBorder(frame.transform, "BorderTop", true, true);
			CreateBorder(frame.transform, "BorderBottom", true, false);
			CreateBorder(frame.transform, "BorderLeft", false, true);
			CreateBorder(frame.transform, "BorderRight", false, false);

			TextMeshProUGUI body = CreateText(frame.transform, "BodyText");
			body.fontSize = 36f;
			body.color = BodyColor;
			body.alignment = TextAlignmentOptions.Center;
			body.lineSpacing = 10f;
			RectTransform bodyRect = body.rectTransform;
			bodyRect.anchorMin = Vector2.zero;
			bodyRect.anchorMax = Vector2.one;
			bodyRect.offsetMin = new Vector2(BodySideInset, BodyBottomInset);
			bodyRect.offsetMax = new Vector2(-BodySideInset, -BodyTopInset);

			RectTransform hint = new GameObject("ContinueHint", typeof(RectTransform)).GetComponent<RectTransform>();
			hint.SetParent(frame.transform, false);
			hint.anchorMin = new Vector2(1f, 0f);
			hint.anchorMax = new Vector2(1f, 0f);
			hint.pivot = new Vector2(1f, 0f);
			hint.anchoredPosition = new Vector2(-24f, 14f);
			hint.sizeDelta = new Vector2(120f, 40f);

			Sprite iconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
			GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			icon.transform.SetParent(hint, false);
			RectTransform iconRect = icon.GetComponent<RectTransform>();
			iconRect.anchorMin = new Vector2(0f, 0.5f);
			iconRect.anchorMax = new Vector2(0f, 0.5f);
			iconRect.pivot = new Vector2(0f, 0.5f);
			iconRect.anchoredPosition = Vector2.zero;
			iconRect.sizeDelta = new Vector2(22f, 22f);
			Image iconImage = icon.GetComponent<Image>();
			iconImage.sprite = iconSprite;
			iconImage.color = HintColor;
			iconImage.raycastTarget = false;

			TextMeshProUGUI hintLabel = CreateText(hint, "Label");
			hintLabel.fontSize = 22f;
			hintLabel.color = HintLabelColor;
			hintLabel.characterSpacing = 2f;
			hintLabel.alignment = TextAlignmentOptions.Left;
			hintLabel.text = "继续";
			RectTransform labelRect = hintLabel.rectTransform;
			labelRect.anchorMin = new Vector2(0f, 0.5f);
			labelRect.anchorMax = new Vector2(0f, 0.5f);
			labelRect.pivot = new Vector2(0f, 0.5f);
			labelRect.anchoredPosition = new Vector2(30f, 0f);
			labelRect.sizeDelta = new Vector2(80f, 32f);

			TextMeshProUGUI speaker = CreateText(root.transform, "SpeakerLabel");
			speaker.fontSize = 40f;
			speaker.fontStyle = FontStyles.Bold | FontStyles.Italic;
			speaker.color = NameColor;
			speaker.characterSpacing = 4f;
			speaker.alignment = TextAlignmentOptions.BottomLeft;
			RectTransform speakerRect = speaker.rectTransform;
			speakerRect.anchorMin = new Vector2(0f, 1f);
			speakerRect.anchorMax = new Vector2(0f, 1f);
			speakerRect.pivot = new Vector2(0f, 0f);
			speakerRect.anchoredPosition = new Vector2(NameInsetLeft, -NameOverlap);
			speakerRect.sizeDelta = new Vector2(700f, 56f);

			PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
			Object.DestroyImmediate(root);
		}

		private static void CreateBorder(Transform parent, string name, bool horizontal, bool isTopOrLeft)
		{
			GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			if (horizontal)
			{
				rect.anchorMin = new Vector2(0f, isTopOrLeft ? 1f : 0f);
				rect.anchorMax = new Vector2(1f, isTopOrLeft ? 1f : 0f);
				rect.pivot = new Vector2(0.5f, isTopOrLeft ? 1f : 0f);
				rect.offsetMin = new Vector2(0f, isTopOrLeft ? -BorderThickness : 0f);
				rect.offsetMax = new Vector2(0f, isTopOrLeft ? 0f : BorderThickness);
			}
			else
			{
				rect.anchorMin = new Vector2(isTopOrLeft ? 0f : 1f, 0f);
				rect.anchorMax = new Vector2(isTopOrLeft ? 0f : 1f, 1f);
				rect.pivot = new Vector2(isTopOrLeft ? 0f : 1f, 0.5f);
				rect.offsetMin = new Vector2(isTopOrLeft ? 0f : -BorderThickness, 0f);
				rect.offsetMax = new Vector2(isTopOrLeft ? BorderThickness : 0f, 0f);
			}

			Image image = go.GetComponent<Image>();
			image.color = BorderColor;
			image.raycastTarget = false;
		}

		private static TextMeshProUGUI CreateText(Transform parent, string name)
		{
			GameObject go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
			text.font = TMP_Settings.defaultFontAsset;
			text.text = string.Empty;
			text.enableWordWrapping = true;
			text.overflowMode = TextOverflowModes.Overflow;
			text.raycastTarget = false;
			return text;
		}

		private static void EmbedIntoBattleMainUI()
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
			if (prefab == null)
			{
				Debug.LogError($"[NpcSubtitle] 找不到 {PrefabPath}");
				return;
			}

			GameObject contents = PrefabUtility.LoadPrefabContents(HostPrefabPath);
			try
			{
				Transform existing = contents.transform.Find(RootName);
				if (existing != null)
				{
					Object.DestroyImmediate(existing.gameObject);
				}

				GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, contents.transform);
				instance.name = RootName;
				RectTransform rect = instance.GetComponent<RectTransform>();
				rect.localScale = Vector3.one;
				rect.localRotation = Quaternion.identity;
				PrefabUtility.SaveAsPrefabAsset(contents, HostPrefabPath);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(contents);
			}
		}
	}
}
