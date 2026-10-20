using System.IO;
using DGame;
using GameLogic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace GameEditor
{
	/// <summary>
	/// 按《记忆卡片详情.jpg》重建图鉴详情弹层，只操作 CollectionWindow.prefab 内部：
	/// 白纸大卡 + 齿孔邮票 + 金色圆章/牛皮纸关闭钮 + 标题点阵 + 分隔线 + 米黄正文框。
	/// 布局数值全部落在 prefab 节点上，后续在 Inspector 里直接微调。
	/// </summary>
	public static class CollectionDetailPrefabBuilder
	{
		private const string PrefabPath = "Assets/BundleAssets/UI/CollectionWindow.prefab";
		private const string GenCodePath = "Assets/Scripts/HotFix/GameLogic/UI/Gen";
		private const string ClassName = "CollectionWindow";

		private const string RawDir = "Assets/BundleAssets/UIRaw/Raw";
		private const string CardPngPath = RawDir + "/collection_detail_card.png";
		private const string DotPngPath = RawDir + "/collection_detail_dot.png";

		private const string ArtDir = "Assets/BundleAssets/美术素材/UI/图鉴界面导出";
		private const string StampBasePng = ArtDir + "/collection_image_bg.png";
		private const string StampSlotPng = ArtDir + "/collection_image_slot.png";
		private const string StampBorderPng = ArtDir + "/collection_image_border.png";
		private const string SealPng = ArtDir + "/collection_popup_dec.png";
		private const string ClosePng = ArtDir + "/collection_popup_close_btn.png";

		private const string FontPath = "Assets/BundleAssets/Fonts/SmileySans_Static.asset";

		// ---- 卡片本体（1920x1080 画布内，DetailCard 局部坐标以卡中心为原点，y 向上）----
		// 尺寸与摆放全部来自《记忆卡片详情.jpg》反旋转后的实测像素（1:1 设计分辨率）。
		private const float CardWidth = 1097f;
		private const float CardHeight = 1180f;
		private const float CardPosX = 136f;      // 卡片中心相对 m_goDetail 中心
		private const float CardPosY = -154f;
		private const float CardRotationZ = -4.894f;

		// 局部坐标换算：u 从卡片左边、v 从卡片上边量起（和效果图一致）
		private static float U(float u) => u - CardWidth * 0.5f;
		private static float V(float v) => CardHeight * 0.5f - v;

		// ---- 邮票（穿孔底 → 白纸槽 → 配图 → 虚线框，四者同心）----
		private const float StampWidth = 614f;
		private const float StampHeight = 363f;
		private const float StampU = 543f;
		private const float StampV = 245.5f;
		private const float StampPanelWidth = 555f;
		private const float StampPanelHeight = 309f;

		// ---- 圆章 / 关闭钮 ----
		private const float SealWidth = 168f;
		private const float SealHeight = 106f;
		private const float SealU = 819f;
		private const float SealV = 80.7f;
		private const float SealRotationZ = -6f;
		private const float CloseWidth = 210f;
		private const float CloseHeight = 137f;
		private const float CloseU = 1079.5f;
		private const float CloseV = 127f;
		private const float CloseRotationZ = -3f;

		// ---- 标题 / 点阵 / 分隔线 / 正文 ----
		private const float TitleU = 551.5f;
		private const float TitleV = 507f;
		private const float TitleWidth = 620f;
		private const float TitleHeight = 90f;
		private const float TitleFontSize = 58f;
		private const float DotSize = 17f;
		private const float DotV = 516f;
		private static readonly float[] DotLeftU = { 72f, 113.5f, 155f, 197f };
		private static readonly float[] DotRightU = { 906f, 947.5f, 989f, 1031f };
		private const float DividerU = 551f;
		private const float DividerV = 554f;
		private const float DividerWidth = 982f;
		private const float DividerHeight = 13f;
		private const float BodyPlateU = 552.5f;
		private const float BodyPlateV = 846f;
		private const float BodyPlateWidth = 1003f;
		private const float BodyPlateHeight = 518f;
		private const float BodyTextLeftU = 180f;
		private const float BodyTextTopV = 640f;
		private const float BodyTextWidth = 730f;
		private const float BodyTextHeight = 440f;
		private const float BodyFontSize = 35f;
		private const float BodyLineSpacing = 20f;
		private const float BodyEdgeU = 976f;
		private const float BodyEdgeV = 765f;
		private const float BodyEdgeWidth = 16f;
		private const float BodyEdgeHeight = 330f;

		// ---- 颜色 ----
		private static readonly Color CardColor = new Color32(253, 252, 250, 255);
		private static readonly Color TitleColor = new Color32(78, 57, 54, 255);
		private static readonly Color BodyColor = new Color32(91, 74, 58, 255);
		private static readonly Color DividerColor = new Color32(220, 192, 138, 230);
		private static readonly Color BodyPlateColor = new Color32(249, 239, 212, 255);
		private static readonly Color DotColor = new Color32(232, 199, 122, 255);

		private static Sprite s_card;
		private static Sprite s_dot;
		private static Sprite s_stampBase;
		private static Sprite s_stampSlot;
		private static Sprite s_stampBorder;
		private static Sprite s_seal;
		private static Sprite s_close;
		private static TMP_FontAsset s_font;

		[MenuItem("DGame Tools/图鉴/重建记忆详情布局")]
		public static void Rebuild()
		{
			LastReport = string.Empty;
			if (!File.Exists(PrefabPath))
			{
				Debug.LogError($"[CollectionDetail] 找不到 {PrefabPath}");
				return;
			}

			EnsureSprites();
			LoadSprites();

			GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
			if (root == null)
			{
				Debug.LogError("[CollectionDetail] LoadPrefabContents 失败");
				return;
			}

			try
			{
				Transform detail = FindOrCreateDetailRoot(root.transform);
				if (detail == null)
				{
					return;
				}

				var bind = root.GetComponent<UIBindComponent>();
				int before = GetBindCount(bind);

				ClearChildrenExcept(detail, "m_imgDetailMask");
				BuildDetail(detail);
				Rebind(root);

				int after = GetBindCount(bind);

				Transform builtCard = detail.Find("DetailCard");
				RectTransform builtRect = builtCard as RectTransform;
				string builtInfo = builtRect == null
					? "built=null"
					: $"built={builtRect.sizeDelta.x}x{builtRect.sizeDelta.y} kids={builtCard.childCount}";

				GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
				long size = new FileInfo(PrefabPath).Length;
				TryRegenerateScript(root);

				LastReport = $"bind before={before} after={after} {builtInfo} saved={(saved != null)} bytes={size} " + VerifySaved();
				Debug.Log($"[CollectionDetail] 详情布局已重建（{LastReport}）。若 _Gen.g.cs 未更新，选中 CollectionWindow 根节点点「重新绑定组件」+「自动生成脚本」。");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		public static string LastReport = string.Empty;

		/// <summary>保存后重新从磁盘读一遍 prefab，确认布局真的落盘（避免“看起来成功其实没写”）。</summary>
		private static string VerifySaved()
		{
			AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
			GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
			if (asset == null)
			{
				return "verify=load-failed";
			}

			Transform card = asset.transform.Find("m_goDetail/DetailCard");
			if (card == null)
			{
				return "verify=DetailCard-missing";
			}

			RectTransform rect = card as RectTransform;
			int dots = 0;
			for (int i = 0; i < card.childCount; i++)
			{
				if (card.GetChild(i).name == "Dots")
				{
					dots = card.GetChild(i).childCount;
				}
			}

			return $"verify=DetailCard {rect.sizeDelta.x}x{rect.sizeDelta.y} @({rect.anchoredPosition.x},{rect.anchoredPosition.y}) dots={dots}";
		}

		private static int GetBindCount(UIBindComponent bind)
		{
			if (bind == null)
			{
				return -1;
			}

			var field = typeof(UIBindComponent).GetField("m_components",
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var list = field?.GetValue(bind) as System.Collections.ICollection;
			return list?.Count ?? -1;
		}

		#region 资源

		private static void EnsureSprites()
		{
			Directory.CreateDirectory(RawDir);
			WriteRoundedCard(CardPngPath, 256, 48f, new Color32(253, 252, 250, 255), new Color32(232, 226, 214, 255));
			WriteDot(DotPngPath, 24);
			AssetDatabase.Refresh();

			SetSpriteImporter(CardPngPath, 48f);
			SetSpriteImporter(DotPngPath, 0f);
			AssetDatabase.Refresh();
		}

		private static void LoadSprites()
		{
			s_card = AssetDatabase.LoadAssetAtPath<Sprite>(CardPngPath);
			s_dot = AssetDatabase.LoadAssetAtPath<Sprite>(DotPngPath);
			s_stampBase = AssetDatabase.LoadAssetAtPath<Sprite>(StampBasePng);
			s_stampSlot = AssetDatabase.LoadAssetAtPath<Sprite>(StampSlotPng);
			s_stampBorder = AssetDatabase.LoadAssetAtPath<Sprite>(StampBorderPng);
			s_seal = AssetDatabase.LoadAssetAtPath<Sprite>(SealPng);
			s_close = AssetDatabase.LoadAssetAtPath<Sprite>(ClosePng);
			s_font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

			if (s_card == null) Debug.LogError($"[CollectionDetail] 缺 sprite {CardPngPath}");
			if (s_stampBase == null) Debug.LogError($"[CollectionDetail] 缺 sprite {StampBasePng}");
			if (s_stampSlot == null) Debug.LogError($"[CollectionDetail] 缺 sprite {StampSlotPng}");
			if (s_stampBorder == null) Debug.LogError($"[CollectionDetail] 缺 sprite {StampBorderPng}");
			if (s_seal == null) Debug.LogError($"[CollectionDetail] 缺 sprite {SealPng}");
			if (s_close == null) Debug.LogError($"[CollectionDetail] 缺 sprite {ClosePng}");
		}

		private static void SetSpriteImporter(string path, float border)
		{
			TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
			if (importer == null) return;

			importer.textureType = TextureImporterType.Sprite;
			importer.spriteImportMode = SpriteImportMode.Single;
			importer.spritePixelsPerUnit = 100f;
			importer.mipmapEnabled = false;
			importer.alphaIsTransparency = true;
			importer.filterMode = FilterMode.Bilinear;
			importer.textureCompression = TextureImporterCompression.Uncompressed;
			if (border > 0f)
			{
				importer.spriteBorder = new Vector4(border, border, border, border);
			}

			importer.SaveAndReimport();
		}

		private static void WriteRoundedCard(string path, int size, float radius, Color32 fill, Color32 line)
		{
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			var pixels = new Color32[size * size];
			Vector2 half = new Vector2(size * 0.5f - 1f, size * 0.5f - 1f);

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
					float sd = RoundRectSdf(p, half, radius);
					float alpha = Mathf.Clamp01(0.5f - sd);
					if (alpha <= 0f)
					{
						pixels[y * size + x] = new Color32(0, 0, 0, 0);
						continue;
					}

					Color32 c = sd > -1.5f ? line : fill;
					c.a = (byte)Mathf.RoundToInt(alpha * 255f);
					pixels[y * size + x] = c;
				}
			}

			tex.SetPixels32(pixels);
			tex.Apply();
			File.WriteAllBytes(path, tex.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(tex);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
		}

		private static void WriteDot(string path, int size)
		{
			var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			var pixels = new Color32[size * size];
			Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
			float radius = size * 0.5f - 1f;

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float d = (new Vector2(x + 0.5f, y + 0.5f) - center).magnitude - radius;
					float alpha = Mathf.Clamp01(0.5f - d);
					pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
				}
			}

			tex.SetPixels32(pixels);
			tex.Apply();
			File.WriteAllBytes(path, tex.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(tex);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
		}

		private static float RoundRectSdf(Vector2 p, Vector2 half, float radius)
		{
			Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(radius, radius);
			Vector2 outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
			return Mathf.Min(Mathf.Max(q.x, q.y), 0f) + outside.magnitude - radius;
		}

		#endregion

		#region 结构

		private static Transform FindOrCreateDetailRoot(Transform root)
		{
			Transform detail = root.Find("m_goDetail");
			if (detail != null)
			{
				return detail;
			}

			Debug.LogError("[CollectionDetail] 根节点下找不到 m_goDetail，请确认 prefab 结构后被中断。");
			return null;
		}

		private static void ClearChildrenExcept(Transform parent, params string[] keep)
		{
			for (int i = parent.childCount - 1; i >= 0; i--)
			{
				Transform child = parent.GetChild(i);
				bool skip = false;
				for (int k = 0; k < keep.Length; k++)
				{
					if (child.name == keep[k])
					{
						skip = true;
						break;
					}
				}

				if (!skip)
				{
					UnityEngine.Object.DestroyImmediate(child.gameObject);
				}
			}
		}

		private static void BuildDetail(Transform detail)
		{
			GameObject card = NewNode("DetailCard", detail, CardWidth, CardHeight, CardPosX, CardPosY);
			card.transform.localRotation = Quaternion.Euler(0f, 0f, CardRotationZ);

			// 1. 白纸卡（九宫格，任意拉伸圆角不变形）
			Image cardBg = NewImage("m_imgDetailBg", card.transform, CardWidth, CardHeight,
				0f, 0f, s_card, true);
			cardBg.color = CardColor;

			// 2. 米黄正文框（先铺，后面标题/分隔线压在上面）
			Image plate = NewImage("BodyPlate", card.transform, BodyPlateWidth, BodyPlateHeight,
				U(BodyPlateU), V(BodyPlateV), s_card, true);
			plate.color = BodyPlateColor;

			// 3. 右侧白纸条（贴在正文框右内侧）
			Image edge = NewImage("BodyEdge", card.transform, BodyEdgeWidth, BodyEdgeHeight,
				U(BodyEdgeU), V(BodyEdgeV), s_card, true);
			edge.color = new Color(1f, 1f, 1f, 0.92f);

			// 4. 正文
			TextMeshProUGUI body = NewText("m_tmpDetailBody", card.transform, string.Empty,
				BodyTextWidth, BodyTextHeight,
				U(BodyTextLeftU + BodyTextWidth * 0.5f), V(BodyTextTopV + BodyTextHeight * 0.5f),
				BodyFontSize, BodyColor, TextAlignmentOptions.TopLeft);
			body.lineSpacing = BodyLineSpacing;

			// 5. 分隔线（纯色细条，不用九宫格图，避免高度小于 border 时退化）
			Image divider = NewImage("Divider", card.transform, DividerWidth, DividerHeight,
				U(DividerU), V(DividerV), null, false);
			divider.color = DividerColor;

			// 6. 标题
			NewText("m_tmpDetailTitle", card.transform, "记忆名称", TitleWidth, TitleHeight,
				U(TitleU), V(TitleV), TitleFontSize, TitleColor, TextAlignmentOptions.Center);

			// 7. 点阵（标题两侧各 4 颗）
			BuildDots(card.transform);

			// 8. 邮票：齿孔底 → 白槽 → 配图 → 虚线框（虚线压在最上面，四者同心）
			GameObject stamp = NewNode("StampGroup", card.transform, StampWidth, StampHeight,
				U(StampU), V(StampV));
			NewImage("m_imgStampFrame", stamp.transform, StampWidth, StampHeight, 0f, 0f, s_stampBase, false);
			NewImage("m_imgDetailPhoto", stamp.transform, StampPanelWidth, StampPanelHeight, 0f, 0f, s_stampSlot, false);
			Image photo = NewImage("m_imgDetailContent", stamp.transform, StampPanelWidth, StampPanelHeight,
				0f, 0f, null, false);
			photo.color = new Color(1f, 1f, 1f, 0f);
			photo.preserveAspect = true;
			NewImage("m_imgDetailBorder", stamp.transform, StampPanelWidth, StampPanelHeight,
				0f, 0f, s_stampBorder, false);

			// 9. 金色圆章（骑在邮票右上角）
			Image seal = NewImage("m_imgDetailDec", card.transform, SealWidth, SealHeight,
				U(SealU), V(SealV), s_seal, false);
			seal.rectTransform.localRotation = Quaternion.Euler(0f, 0f, SealRotationZ);

			// 10. 关闭钮（骑在卡片右上角，允许探出卡外）
			Image closeImage = NewImage("m_btnDetailClose", card.transform, CloseWidth, CloseHeight,
				U(CloseU), V(CloseV), s_close, false);
			closeImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, CloseRotationZ);
			Button closeButton = closeImage.gameObject.AddComponent<Button>();
			closeButton.targetGraphic = closeImage;
			closeButton.transition = Selectable.Transition.None;
		}

		private static void BuildDots(Transform parent)
		{
			GameObject dots = NewNode("Dots", parent, CardWidth, 24f, 0f, V(DotV));
			for (int i = 0; i < DotLeftU.Length; i++)
			{
				Image dot = NewImage("DotL" + i, dots.transform, DotSize, DotSize,
					U(DotLeftU[i]), 0f, s_dot, false);
				dot.color = DotColor;
			}

			for (int i = 0; i < DotRightU.Length; i++)
			{
				Image dot = NewImage("DotR" + i, dots.transform, DotSize, DotSize,
					U(DotRightU[i]), 0f, s_dot, false);
				dot.color = DotColor;
			}
		}

		private static GameObject NewNode(string name, Transform parent, float width, float height,
			float x, float y)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.layer = parent.gameObject.layer;
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(width, height);
			rect.anchoredPosition = new Vector2(x, y);
			return go;
		}

		private static Image NewImage(string name, Transform parent, float width, float height,
			float x, float y, Sprite sprite, bool sliced)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(Image));
			go.layer = parent.gameObject.layer;
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(width, height);
			rect.anchoredPosition = new Vector2(x, y);

			Image image = go.GetComponent<Image>();
			image.sprite = sprite;
			image.type = sliced && sprite != null ? Image.Type.Sliced : Image.Type.Simple;
			image.color = Color.white;
			image.raycastTarget = false;
			return image;
		}

		private static TextMeshProUGUI NewText(string name, Transform parent, string content,
			float width, float height, float x, float y, float fontSize, Color color,
			TextAlignmentOptions alignment)
		{
			var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
			go.layer = parent.gameObject.layer;
			go.transform.SetParent(parent, false);
			RectTransform rect = go.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(width, height);
			rect.anchoredPosition = new Vector2(x, y);

			TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
			if (s_font != null)
			{
				text.font = s_font;
			}

			text.text = content;
			text.fontSize = fontSize;
			text.color = color;
			text.alignment = alignment;
			text.enableWordWrapping = true;
			text.overflowMode = TextOverflowModes.Overflow;
			text.raycastTarget = false;
			return text;
		}

		#endregion

		#region 绑定

		private static void Rebind(GameObject root)
		{
			var bind = root.GetComponent<UIBindComponent>();
			if (bind == null)
			{
				Debug.LogError("[CollectionDetail] 根节点没有 UIBindComponent");
				return;
			}

			// ErgodicUIComponent 依赖 UIScriptGenerator 的私有静态前缀表 VARIABLE_NAME_REGEX，
			// 该表只在 CheckVariableNames() 里初始化（Inspector 流程会先调它）。
			// 这里直接调 ErgodicUIComponent 会 NRE，所以先用反射补上这一步。
			EnsureVariableNameRegex();

			bind.Clear();
			UIScriptGenerator.ErgodicUIComponent(root.transform, root.transform, bind);
		}

		private static void EnsureVariableNameRegex()
		{
			var method = typeof(UIScriptGenerator).GetMethod("CheckVariableNames",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			if (method == null)
			{
				Debug.LogWarning("[CollectionDetail] 找不到 UIScriptGenerator.CheckVariableNames，绑定可能失败");
				return;
			}

			method.Invoke(null, null);
		}

		private static void TryRegenerateScript(GameObject root)
		{
			// 目标文件若被设成只读，生成器会写不进去（甚至把文件写丢），先手动解除。
			string genFile = Path.Combine(GenCodePath, ClassName + "_Gen.g.cs");
			if (File.Exists(genFile))
			{
				FileAttributes attributes = File.GetAttributes(genFile);
				if ((attributes & FileAttributes.ReadOnly) != 0)
				{
					File.SetAttributes(genFile, attributes & ~FileAttributes.ReadOnly);
				}
			}

			Transform previous = Selection.activeTransform;
			try
			{
				Selection.activeTransform = root.transform;
				bool ok = UIScriptGenerator.GenerateCSharpScript(true, false, true,
					GenCodePath, ClassName, "UIWindow");
				if (!ok)
				{
					Debug.LogWarning("[CollectionDetail] 自动生成 _Gen.g.cs 失败，请在 Inspector 里手动点「自动生成脚本」。");
				}
			}
			catch (System.Exception e)
			{
				Debug.LogWarning($"[CollectionDetail] 自动生成 _Gen.g.cs 异常：{e.Message}，请手动点「自动生成脚本」。");
			}
			finally
			{
				Selection.activeTransform = previous;
			}
		}

		#endregion
	}
}
