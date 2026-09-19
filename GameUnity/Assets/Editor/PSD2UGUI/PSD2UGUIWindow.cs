#if UNITY_EDITOR
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.LayerResources;
using Aspose.PSD.ImageLoadOptions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Font = UnityEngine.Font;

#if TextMeshPro
using TMPro;
#endif
#if UNITEXT
using LightSide;
#endif

namespace DGame.PSD2UGUI
{
    /// <summary>
    /// PSD 转 UGUI 窗口
    /// </summary>
    public class PSD2UGUIWindow : EditorWindow
    {
        private string m_psdPath;
        private bool m_updateMode;
        private bool m_notImage;
        private bool m_notPos;
        private bool m_notText;
        private bool m_onlySize;
        private bool m_onlyCenter;

        private PSD2UGUIAttribute m_attr;
        private UnityEditor.Editor m_settingsEditor;
        private Vector2 m_scroll;
        private int m_tabIndex;
        private static readonly string[] s_tabs = { "PSD转UI", "设置" };
        private const string TextMeshProDefine = "TextMeshPro";
        private static readonly string[] s_textComponentTabs =
        {
            "Unity Text",
#if TextMeshPro
            "TextMeshPro",
#endif
#if UNITEXT
            "UniText",
#endif
        };
        private static readonly Color s_headerColor = new Color(0.4f, 0.6f, 0.8f);

        [MenuItem("DGame Tools/UI/PSD转UI &h")]
        public static void Open()
        {
            var window = GetWindow<PSD2UGUIWindow>(false, "PSD转UI");
            window.minSize = new Vector2(520, 460);
            window.Show();
        }

        private void OnEnable()
        {
            var settings = PSD2UGUISettings.Instance;
            m_settingsEditor = UnityEditor.Editor.CreateEditor(settings);
        }

        private void OnDisable()
        {
            if (m_settingsEditor != null)
            {
                DestroyImmediate(m_settingsEditor);
                m_settingsEditor = null;
            }
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawTabButtons();
            m_scroll = EditorGUILayout.BeginScrollView(m_scroll);
            if (m_tabIndex == 0)
            {
                DrawPSDToUI();
            }
            else
            {
                DrawSettings();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            var rect = EditorGUILayout.GetControlRect(false, 50);
            var headerRect = new Rect(rect.x + 5, rect.y + 5, rect.width - 10, 40);
            EditorGUI.DrawRect(headerRect, s_headerColor);
            GUI.Label(headerRect, "PSD 转 UGUI", new GUIStyle
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            });
            EditorGUILayout.Space(5);
        }

        private void DrawTabButtons()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            for (int i = 0; i < s_tabs.Length; i++)
            {
                bool isSelected = m_tabIndex == i;
                if (GUILayout.Toggle(isSelected, s_tabs[i], EditorStyles.toolbarButton, GUILayout.Width(120)) && !isSelected)
                {
                    m_tabIndex = i;
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("定位设置", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                Selection.activeObject = PSD2UGUISettings.Instance;
                EditorGUIUtility.PingObject(PSD2UGUISettings.Instance);
            }
            EditorGUILayout.EndHorizontal();
        }

        #region 设置区
        private void DrawSettings()
        {
            EditorGUI.BeginChangeCheck();
            if (m_settingsEditor != null)
            {
                m_settingsEditor.OnInspectorGUI();
            }
            if (EditorGUI.EndChangeCheck())
            {
                ComponentTypeResolver.ClearCache();
                PSD2UGUISettings.Instance.ClearCache();
            }
            EditorGUILayout.Space(8);
            if (GUILayout.Button("打开设置文件 (Ping)"))
            {
                Selection.activeObject = PSD2UGUISettings.Instance;
                EditorGUIUtility.PingObject(PSD2UGUISettings.Instance);
            }
        }
        #endregion

        #region PSD 主流程
        private void DrawPSDToUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("PSD 源文件", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("文件", GUILayout.Width(50));
            Rect pathFieldRect = EditorGUILayout.GetControlRect(GUILayout.MinWidth(80));
            m_psdPath = EditorGUI.TextField(pathFieldRect, m_psdPath);
            if (GUILayout.Button("选择", GUILayout.Width(60)))
            {
                string p = EditorUtility.OpenFilePanel("选择PSD文件", "Assets", "psd");
                if (!string.IsNullOrEmpty(p)) m_psdPath = p;
            }
            EditorGUILayout.EndHorizontal();

            // 拖拽支持
            if (((Event.current.type == EventType.DragUpdated) || Event.current.type == EventType.DragExited) &&
                pathFieldRect.Contains(Event.current.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
                if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
                {
                    string path = DragAndDrop.paths[0];
                    if (!string.IsNullOrEmpty(path) && !path.Equals(m_psdPath) &&
                        path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
                    {
                        m_psdPath = path;
                    }
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            DrawTextComponentSwitch();

            if (!File.Exists(m_psdPath))
            {
                EditorGUILayout.HelpBox("拖入或选择一个 .psd 文件", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("执行方式", EditorStyles.boldLabel);
            m_updateMode = EditorGUILayout.ToggleLeft("更新已有 UI", m_updateMode);
            if (m_updateMode)
            {
                EditorGUILayout.BeginHorizontal();
                m_notImage = EditorGUILayout.ToggleLeft("不更新图片", m_notImage);
                m_notPos = EditorGUILayout.ToggleLeft("不更新位置大小", m_notPos);
                m_notText = EditorGUILayout.ToggleLeft("不更新文本内容", m_notText);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(10);
                if (GUILayout.Button("一键更新", GUILayout.Height(30)))
                {
                    UpdateUIByPSD();
                }
            }
            else
            {
                m_notImage = m_notPos = m_notText = false;
                EditorGUILayout.Space(10);
                if (GUILayout.Button("一键生成", GUILayout.Height(30)))
                {
                    ParsePSD();
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTextComponentSwitch()
        {
            var settings = PSD2UGUISettings.Instance;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("文本组件", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            int currentType = (int)settings.textComponentType;
            if (currentType < 0 || currentType >= s_textComponentTabs.Length)
            {
                currentType = 0;
            }
            int newType = GUILayout.Toolbar(currentType, s_textComponentTabs, GUILayout.Height(24));
#if !TextMeshPro
            EditorGUILayout.HelpBox($"当前未定义 TextMeshPro 编译符号，生成时将使用 {GetDisplayComponentName(settings.textComponentTypeName, typeof(Text))}。如果需要支持 TextMeshPro，点击下方添加按钮。", MessageType.Warning);
            DrawAddTextMeshProDefineButton();
#endif
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Change PSD2UGUI Text Component");
                settings.textComponentType = (PSD2UGUITextComponentType)newType;
                EditorUtility.SetDirty(settings);
                ComponentTypeResolver.ClearCache();
                settings.ClearCache();
            }

            string tip = GetTextComponentTip(settings);
            EditorGUILayout.HelpBox(tip, MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private static string GetTextComponentTip(PSD2UGUISettings settings)
        {
#if TextMeshPro
            if (settings.textComponentType == PSD2UGUITextComponentType.TextMeshPro)
            {
                return $"使用 {GetDisplayComponentName(settings.textMeshProComponentTypeName, typeof(TextMeshProUGUI))}，字体走 TMP_FontAsset 映射。";
            }
#endif
#if UNITEXT
            if (settings.textComponentType == PSD2UGUITextComponentType.UniText)
            {
                return $"使用 {GetDisplayComponentName(settings.uniTextComponentTypeName, typeof(UniText))}，字体走 UniTextFont 映射。";
            }
#endif
            return $"使用 {GetDisplayComponentName(settings.textComponentTypeName, typeof(Text))}，字体走 Unity Font 映射。";
        }

        private static string GetDisplayComponentName(string typeName, Type fallback)
        {
            return string.IsNullOrEmpty(typeName) ? fallback.FullName : typeName;
        }

#if !TextMeshPro
        private static void DrawAddTextMeshProDefineButton()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("自动添加 TextMeshPro 宏定义", GUILayout.Width(190)))
            {
                AddTextMeshProDefine();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("添加后 Unity 会触发脚本重编译，完成后可切换到 TextMeshPro 生成。", MessageType.None);
        }

        private static void AddTextMeshProDefine()
        {
            if (!IsTextMeshProAvailable())
            {
                EditorUtility.DisplayDialog(
                    "无法添加宏定义",
                    "当前项目未检测到 TextMeshPro 程序集。请先通过 Package Manager 安装 com.unity.textmeshpro。",
                    "确定");
                return;
            }

            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown)
            {
                EditorUtility.DisplayDialog("无法添加宏定义", "当前 BuildTargetGroup 无效，请先在 Build Settings 中选择目标平台。", "确定");
                return;
            }

            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            if (HasDefine(defines, TextMeshProDefine))
            {
                EditorUtility.DisplayDialog("宏定义已存在", $"当前平台已包含 {TextMeshProDefine}。如果界面仍提示未定义，请等待 Unity 重编译完成。", "确定");
                return;
            }

            string newDefines = string.IsNullOrEmpty(defines) ? TextMeshProDefine : $"{defines};{TextMeshProDefine}";
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, newDefines);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PSD2UGUI] 已为 {group} 添加 Scripting Define Symbol: {TextMeshProDefine}");
        }

        private static bool IsTextMeshProAvailable()
        {
            return Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") != null ||
                   Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro") != null;
        }

        private static bool HasDefine(string defines, string symbol)
        {
            if (string.IsNullOrEmpty(defines))
            {
                return false;
            }

            string[] splitDefines = defines.Split(';');
            for (int i = 0; i < splitDefines.Length; i++)
            {
                if (string.Equals(splitDefines[i].Trim(), symbol, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
#endif

        private void ParsePSD()
        {
            var psdOpts = new PsdLoadOptions { LoadEffectsResource = true, ReadOnlyMode = false };
            Stack<GameObject> stack = new Stack<GameObject>();
            stack.Push(GetOrCreateCanvasGameObject());

            using (FileStream fs = new FileStream(m_psdPath, FileMode.Open))
            {
                try
                {
                    using (var psd = Aspose.PSD.Image.Load(fs, psdOpts) as PsdImage)
                    {
                        for (int i = 0; i < psd.Layers.Length; i++)
                        {
                            var layer = psd.Layers[i];
                            var curLayerType = layer.GetLayerType();
                            string name = layer.DisplayName;
                            foreach (var item in layer.Resources)
                            {
                                if (item is LuniResource luniRes) name = luniRes.Name;
                            }
                            if (curLayerType == PsdLayerType.SectionDividerLayer)
                            {
                                GameObject parent = CreateUI(stack.Peek(), name);
                                stack.Push(parent);
                            }
                            else if (curLayerType == PsdLayerType.LayerGroup)
                            {
                                GameObject group = stack.Count > 1 ? stack.Pop() : CreateUI(stack.Peek(), name);
                                if (!layer.IsVisible || !layer.IsVisibleInGroup || (layer as LayerGroup).Layers.Length == 0)
                                    DestroyImmediate(group);
                                else
                                    group.name = name;
                            }
                            else
                            {
                                if (layer.IsVisible && layer.IsVisibleInGroup)
                                {
                                    m_attr = new PSD2UGUIAttribute();
                                    m_attr.Parser(layer);
                                    CreateUIByAttribute(stack.Peek(), name);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void UpdateUIByPSD()
        {
            var psdOpts = new PsdLoadOptions { LoadEffectsResource = true, ReadOnlyMode = false };
            using (FileStream fs = new FileStream(m_psdPath, FileMode.Open))
            {
                try
                {
                    using (var psd = Aspose.PSD.Image.Load(fs, psdOpts) as PsdImage)
                    {
                        for (int i = 0; i < psd.Layers.Length; i++)
                        {
                            var layer = psd.Layers[i];
                            string name = layer.DisplayName;
                            foreach (var item in layer.Resources)
                            {
                                if (item is LuniResource luniRes) name = luniRes.Name;
                            }
                            m_attr = new PSD2UGUIAttribute();
                            m_attr.Parser(layer);
                            GameObject go = GameObject.Find(GetUIName(name));
                            if (go && go.transform.parent != null && go.transform.parent.name == FindGroupByLayer(psd.Layers, layer))
                            {
                                UpdateUIByAttribute(go);
                            }
                        }
                    }
                    SceneView.RepaintAll();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private string FindGroupByLayer(Layer[] layers, Layer findLayer)
        {
            foreach (var layer in layers)
            {
                if (layer.GetLayerType() == PsdLayerType.LayerGroup)
                {
                    foreach (var item in (layer as LayerGroup).Layers)
                    {
                        if (item == findLayer)
                        {
                            foreach (var res in layer.Resources)
                            {
                                if (res is LuniResource luniRes) return luniRes.Name;
                            }
                            return layer.DisplayName;
                        }
                    }
                }
            }
            return "";
        }
        #endregion

        #region UI 创建/更新
        private static Canvas FindCanvas()
        {
#if UNITY_6000_1_OR_NEWER
            return FindFirstObjectByType<Canvas>();
#else
            return FindObjectOfType<Canvas>();
#endif
        }

        private GameObject GetOrCreateCanvasGameObject()
        {
            Canvas canvas = FindCanvas();
            if (canvas != null && canvas.gameObject.activeInHierarchy) return canvas.gameObject;
            var go = new GameObject("Canvas") { layer = LayerMask.NameToLayer("UI") };
            canvas = go.AddComponent<Canvas>();
            go.AddComponent<GraphicRaycaster>();
            var rect = GetOrAddComponent<RectTransform>(go);
            rect.sizeDelta = PSD2UGUISettings.Instance.resolution;
            return go;
        }

        private GameObject CreateUI(GameObject parent, string name)
        {
            var go = new GameObject(name) { layer = LayerMask.NameToLayer("UI") };
            go.AddComponent<RectTransform>();
            string uniqueName = GameObjectUtility.GetUniqueNameForSibling(parent.transform, go.name);
            go.name = uniqueName;
            GameObjectUtility.SetParentAndAlign(go, parent);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeGameObject = go;
            return go;
        }

        private T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private Component GetOrAddComponentByType(GameObject go, Type type)
        {
            if (type == null) return null;
            var c = go.GetComponent(type);
            return c != null ? c : go.AddComponent(type);
        }

        private void SetPosAndSize(GameObject go)
        {
            if (m_notPos) return;
            Vector2 pos = m_attr.pos;
            if (!m_attr.isLocalPos)
            {
                var canvasTra = FindCanvas().transform;
                var worldPos = canvasTra.TransformPoint(m_attr.pos);
                var parentRect = go.transform.parent.GetComponent<RectTransform>();
                pos = parentRect.InverseTransformPoint(worldPos);
            }
            if (go.TryGetComponent<RectTransform>(out var rect))
            {
                rect.sizeDelta = m_attr.size;
                rect.anchoredPosition = m_onlyCenter ? Vector2.zero : pos;
            }
        }

        private void SetTextData(GameObject go)
        {
            SetPosAndSize(go);
            if (m_onlySize || m_attr.uiType != UIType.Text) return;

            var settings = PSD2UGUISettings.Instance;
#if TextMeshPro
            if (settings.textComponentType == PSD2UGUITextComponentType.TextMeshPro)
            {
                SetTextMeshProData(go, settings);
                return;
            }
#endif
#if UNITEXT
            if (settings.textComponentType == PSD2UGUITextComponentType.UniText)
            {
                SetUniTextData(go, settings);
                return;
            }
#endif

            SetUnityTextData(go, settings);
        }

        private void SetUnityTextData(GameObject go, PSD2UGUISettings settings)
        {
#if TextMeshPro
            RemoveComponents<TMP_Text>(go);
#endif
#if UNITEXT
            RemoveComponents<UniTextBase>(go);
#endif
            var textType = ComponentTypeResolver.Resolve(settings.textComponentTypeName, typeof(Text));
            var text = GetOrAddComponentByType(go, textType) as Text;
            if (text == null)
            {
                Debug.LogError($"[PSD2UGUI] 文本组件类型 {settings.textComponentTypeName} 不是 Text 派生类");
                return;
            }
            string fontPath = settings.GetFontPath(m_attr.fontName);
            if (!string.IsNullOrEmpty(fontPath))
            {
                text.font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            }
            text.fontStyle = m_attr.fontStyle;
            text.fontSize = m_attr.fontSize;
            text.color = m_attr.color;
            text.raycastTarget = false;

            if (!m_notText)
            {
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                switch (m_attr.alignType)
                {
                    case FontAlignType.Left: text.alignment = TextAnchor.MiddleLeft; break;
                    case FontAlignType.Right: text.alignment = TextAnchor.MiddleRight; break;
                    default: text.alignment = TextAnchor.MiddleCenter; break;
                }
                text.text = m_attr.textStr;
            }

            // 渐变
            ApplyGradient(go, text);

            // 描边
            if (m_attr.outline)
            {
                ApplyOutline(go, text, m_attr.outlineColor, m_attr.outlineSize);
            }
            // 阴影
            if (m_attr.shadow)
            {
                ApplyShadow(go, m_attr.shadowColor, m_attr.shadowDis);
            }
        }

#if TextMeshPro
        private void SetTextMeshProData(GameObject go, PSD2UGUISettings settings)
        {
            RemoveComponents<Text>(go);
#if UNITEXT
            RemoveComponents<UniTextBase>(go);
#endif

            var tmpType = ComponentTypeResolver.Resolve(settings.textMeshProComponentTypeName, typeof(TextMeshProUGUI));
            var text = GetOrAddComponentByType(go, tmpType) as TMP_Text;
            if (text == null)
            {
                Debug.LogError($"[PSD2UGUI] TextMeshPro 组件类型 {settings.textMeshProComponentTypeName} 不是 TMP_Text 派生类");
                return;
            }

            string fontAssetPath = settings.GetTmpFontAssetPath(m_attr.fontName);
            if (!string.IsNullOrEmpty(fontAssetPath))
            {
                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontAssetPath);
                if (fontAsset != null)
                {
                    text.font = fontAsset;
                }
            }

            text.fontStyle = ConvertToTmpFontStyle(m_attr.fontStyle);
            text.fontSize = m_attr.fontSize;
            text.color = m_attr.color;
            text.raycastTarget = false;

            if (!m_notText)
            {
                text.overflowMode = TextOverflowModes.Overflow;
#if UNITY_6000_1_OR_NEWER
                text.textWrappingMode = TextWrappingModes.NoWrap;
#else
                text.enableWordWrapping = false;
#endif
                text.alignment = ConvertToTmpAlignment(m_attr.alignType);
                text.text = m_attr.textStr;
            }

            ApplyTextMeshProGradient(text);

            if (m_attr.outline)
            {
                ApplyTextMeshProOutline(text, m_attr.outlineColor, m_attr.outlineSize);
            }
            if (m_attr.shadow)
            {
                ApplyTextMeshProShadow(text, m_attr.shadowColor, m_attr.shadowDis);
            }
        }

        private static FontStyles ConvertToTmpFontStyle(FontStyle fontStyle)
        {
            switch (fontStyle)
            {
                case FontStyle.Bold:
                    return FontStyles.Bold;
                case FontStyle.Italic:
                    return FontStyles.Italic;
                case FontStyle.BoldAndItalic:
                    return FontStyles.Bold | FontStyles.Italic;
                default:
                    return FontStyles.Normal;
            }
        }

        private static TextAlignmentOptions ConvertToTmpAlignment(FontAlignType alignType)
        {
            switch (alignType)
            {
                case FontAlignType.Left:
                    return TextAlignmentOptions.MidlineLeft;
                case FontAlignType.Right:
                    return TextAlignmentOptions.MidlineRight;
                default:
                    return TextAlignmentOptions.Midline;
            }
        }

        private void ApplyTextMeshProGradient(TMP_Text text)
        {
            if (text == null || m_attr.gradient == null || m_attr.gradient.Count == 0)
            {
                return;
            }

            Color a = m_attr.gradient[0];
            Color b = m_attr.gradient[Mathf.Min(1, m_attr.gradient.Count - 1)];
            ResolveGradientDirection(a, b, m_attr.gradientAngle, out var top, out var bottom, out var left, out var right);
            text.enableVertexGradient = true;
            bool horizontal = m_attr.gradientAngle == 0 || Mathf.Abs(m_attr.gradientAngle) == 180;
            Color topLeft = horizontal ? left : top;
            Color topRight = horizontal ? right : top;
            Color bottomLeft = horizontal ? left : bottom;
            Color bottomRight = horizontal ? right : bottom;
            text.colorGradient = new VertexGradient(topLeft, topRight, bottomLeft, bottomRight);
        }

        private void ApplyTextMeshProOutline(TMP_Text text, Color color, int size)
        {
            if (text == null)
            {
                return;
            }

            text.outlineColor = color;
            text.outlineWidth = Mathf.Clamp(size / Mathf.Max(1f, m_attr.fontSize), 0f, 1f);

            var material = text.fontMaterial;
            if (material == null)
            {
                return;
            }

            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            SetMaterialColor(material, "_OutlineColor", color);
            SetMaterialFloat(material, "_OutlineWidth", text.outlineWidth);
            text.fontMaterial = material;
        }

        private void ApplyTextMeshProShadow(TMP_Text text, Color color, Vector2 dis)
        {
            if (text == null)
            {
                return;
            }

            var material = text.fontMaterial;
            if (material == null)
            {
                return;
            }

            float scale = Mathf.Max(1f, m_attr.fontSize);
            material.EnableKeyword("UNDERLAY_ON");
            SetMaterialColor(material, "_UnderlayColor", color);
            SetMaterialFloat(material, "_UnderlayOffsetX", Mathf.Clamp(dis.x / scale, -1f, 1f));
            SetMaterialFloat(material, "_UnderlayOffsetY", Mathf.Clamp(dis.y / scale, -1f, 1f));
            SetMaterialFloat(material, "_UnderlaySoftness", 0f);
            SetMaterialFloat(material, "_UnderlayDilate", 0f);
            text.fontMaterial = material;
        }

        private static void SetMaterialColor(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetMaterialFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }
#endif

#if UNITEXT
        private void SetUniTextData(GameObject go, PSD2UGUISettings settings)
        {
            RemoveComponents<Text>(go);
#if TextMeshPro
            RemoveComponents<TMP_Text>(go);
#endif

            var uniTextType = ComponentTypeResolver.Resolve(settings.uniTextComponentTypeName, typeof(UniText));
            var text = GetOrAddComponentByType(go, uniTextType) as UniTextBase;
            if (text == null)
            {
                Debug.LogError($"[PSD2UGUI] UniText 组件类型 {settings.uniTextComponentTypeName} 不是 UniTextBase 派生类");
                return;
            }

            string fontAssetPath = settings.GetUniTextFontAssetPath(m_attr.fontName);
            if (!string.IsNullOrEmpty(fontAssetPath))
            {
                var fontAsset = AssetDatabase.LoadAssetAtPath<UniTextFont>(fontAssetPath);
                if (fontAsset != null)
                {
                    text.Font = fontAsset;
                }
            }

            text.FontSize = m_attr.fontSize;
            text.color = m_attr.color;
            text.raycastTarget = false;
            ApplyUniTextFontStyle(text);

            if (!m_notText)
            {
                text.WordWrap = false;
                text.HorizontalAlignment = ConvertToUniTextHorizontalAlignment(m_attr.alignType);
                text.VerticalAlignment = VerticalAlignment.Middle;
                text.Text = m_attr.textStr;
            }

            ApplyUniTextGradient(text);
            ApplyUniTextOutline(text, m_attr.outline, m_attr.outlineColor, m_attr.outlineSize);
            ApplyUniTextShadow(text, m_attr.shadow, m_attr.shadowColor, m_attr.shadowDis);
        }

        private static HorizontalAlignment ConvertToUniTextHorizontalAlignment(FontAlignType alignType)
        {
            switch (alignType)
            {
                case FontAlignType.Left:
                    return HorizontalAlignment.Left;
                case FontAlignType.Right:
                    return HorizontalAlignment.Right;
                default:
                    return HorizontalAlignment.Center;
            }
        }

        private void ApplyUniTextFontStyle(UniTextBase text)
        {
            text.ClearWholeText<BoldModifier>();
            text.ClearWholeText<ItalicModifier>();

            switch (m_attr.fontStyle)
            {
                case FontStyle.Bold:
                    text.SetWholeText<BoldModifier>();
                    break;
                case FontStyle.Italic:
                    text.SetWholeText<ItalicModifier>();
                    break;
                case FontStyle.BoldAndItalic:
                    text.SetWholeText<BoldModifier>();
                    text.SetWholeText<ItalicModifier>();
                    break;
            }
        }

        private void ApplyUniTextGradient(UniTextBase text)
        {
            text.ClearWholeText<GradientModifier>();
            if (m_attr.gradient == null || m_attr.gradient.Count == 0)
            {
                return;
            }

            Color a = m_attr.gradient[0];
            Color b = m_attr.gradient[Mathf.Min(1, m_attr.gradient.Count - 1)];
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(a, 0f),
                    new GradientColorKey(b, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(a.a, 0f),
                    new GradientAlphaKey(b.a, 1f),
                });

            var provider = new InlineGradientProvider();
            const string GradientName = "psd2ugui";
            provider.Add(new UniTextGradients.NamedGradient { name = GradientName, gradient = gradient });
            var modifier = new GradientModifier { Provider = provider };
            text.SetWholeText(typeof(GradientModifier), $"{GradientName},linear,{m_attr.gradientAngle}", () => modifier);
        }

        private void ApplyUniTextOutline(UniTextBase text, bool enabled, Color color, int size)
        {
            text.ClearWholeText<OutlineModifier>();
            if (!enabled)
            {
                return;
            }

            var modifier = new OutlineModifier { FixedPixelSize = true };
            text.SetWholeText(typeof(OutlineModifier), $"{ToUniTextColor(color)},{Mathf.Max(0, size)}", () => modifier);
        }

        private void ApplyUniTextShadow(UniTextBase text, bool enabled, Color color, Vector2 dis)
        {
            text.ClearWholeText<ShadowModifier>();
            if (!enabled)
            {
                return;
            }

            var modifier = new ShadowModifier { FixedPixelSize = true };
            text.SetWholeText(typeof(ShadowModifier), $"0,{ToUniTextColor(color)},{dis.x},{dis.y},0", () => modifier);
        }

        private static string ToUniTextColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGBA(color)}";
        }
#endif

        private void RemoveComponents<T>(GameObject go) where T : Component
        {
            var components = go.GetComponents<T>();
            foreach (var component in components)
            {
                DestroyImmediate(component);
            }
        }

        private void SetImageData(GameObject go)
        {
            SetPosAndSize(go);
            if (m_onlySize || m_attr.uiType != UIType.Image) return;

            var settings = PSD2UGUISettings.Instance;

            if (m_attr.isRawImage)
            {
                var rawType = ComponentTypeResolver.Resolve(settings.rawImageComponentTypeName, typeof(RawImage));
                var rawImage = GetOrAddComponentByType(go, rawType) as RawImage;
                if (rawImage == null)
                {
                    Debug.LogError($"[PSD2UGUI] RawImage 类型 {settings.rawImageComponentTypeName} 不是 RawImage 派生类");
                    return;
                }
                rawImage.color = m_attr.color;
                rawImage.raycastTarget = false;
                if (!string.IsNullOrEmpty(m_attr.imgPath) && !m_notImage)
                {
                    rawImage.texture = AssetDatabase.LoadAssetAtPath<Texture>(m_attr.imgPath);
                }
            }
            else
            {
                var imgType = ComponentTypeResolver.Resolve(settings.imageComponentTypeName, typeof(Image));
                var image = GetOrAddComponentByType(go, imgType) as Image;
                if (image == null)
                {
                    Debug.LogError($"[PSD2UGUI] Image 类型 {settings.imageComponentTypeName} 不是 Image 派生类");
                    return;
                }
                image.color = m_attr.color;
                image.raycastTarget = false;
                if (!string.IsNullOrEmpty(m_attr.imgPath) && !m_notImage)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(m_attr.imgPath);
                    if (sprite != null && sprite.border != Vector4.zero)
                    {
                        image.type = Image.Type.Sliced;
                    }
                    image.sprite = sprite;
                }
                if (m_attr.isButton)
                {
                    image.raycastTarget = true;
                    var btnType = ComponentTypeResolver.Resolve(settings.buttonComponentTypeName, typeof(Button));
                    var btn = GetOrAddComponentByType(go, btnType) as Selectable;
                    if (btn != null) btn.targetGraphic = image;
                }
                ApplyGradient(go, image);
            }
        }

        /// <summary>
        /// 应用渐变。若 gradientComponentTypeName 留空则跳过。
        /// 优先尝试在 graphic 上调用 SetGradientColor(Color32 top, Color32 bottom, ...) 方法。
        /// 否则尝试添加配置的独立 GradientColor 组件并赋值 colorTop/colorBottom/colorLeft/colorRight 字段。
        /// </summary>
        private void ApplyGradient(GameObject go, Graphic graphic)
        {
            if (m_attr.gradient == null || m_attr.gradient.Count == 0) return;
            Color a = m_attr.gradient[0];
            Color b = m_attr.gradient[Mathf.Min(1, m_attr.gradient.Count - 1)];

            Color top, bottom, left, right;
            ResolveGradientDirection(a, b, m_attr.gradientAngle, out top, out bottom, out left, out right);

            // 先尝试 graphic 自身的 SetGradientColor 方法
            if (graphic != null)
            {
                var m = graphic.GetType().GetMethod("SetGradientColor",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(Color32), typeof(Color32), typeof(Color32), typeof(Color32), typeof(float), typeof(float), typeof(bool) },
                    null);
                if (m != null)
                {
                    m.Invoke(graphic, new object[] { (Color32)top, (Color32)bottom, (Color32)left, (Color32)right, 0f, 0f, false });
                    return;
                }
            }

            // 否则尝试独立组件
            var settings = PSD2UGUISettings.Instance;
            if (string.IsNullOrEmpty(settings.gradientComponentTypeName)) return;
            var gradType = ComponentTypeResolver.Resolve(settings.gradientComponentTypeName, null);
            if (gradType == null) return;
            var comp = GetOrAddComponentByType(go, gradType);
            SetField(comp, "colorTop", top);
            SetField(comp, "colorBottom", bottom);
            SetField(comp, "colorLeft", left);
            SetField(comp, "colorRight", right);
        }

        private void ResolveGradientDirection(Color a, Color b, int angle, out Color top, out Color bottom, out Color left, out Color right)
        {
            top = bottom = left = right = Color.white;
            if (angle == 0)
            {
                left = a; right = b;
            }
            else if (Mathf.Abs(angle) == 180)
            {
                right = a; left = b;
            }
            else if (angle == 90)
            {
                bottom = a; top = b;
            }
            else if (angle == -90)
            {
                top = a; bottom = b;
            }
            else
            {
                top = a; bottom = b;
            }
        }

        /// <summary>
        /// 应用描边。优先 SetOutLineColor(Color32, int) 方法，否则添加配置的组件并尝试通用字段。
        /// </summary>
        private void ApplyOutline(GameObject go, Graphic graphic, Color color, int size)
        {
            // 先尝试 graphic 自身方法 SetOutLineColor(Color32, int)
            if (graphic != null)
            {
                var m = graphic.GetType().GetMethod("SetOutLineColor",
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(Color32), typeof(int) }, null);
                if (m != null)
                {
                    m.Invoke(graphic, new object[] { (Color32)color, size });
                    // 启用 outline
                    var setUse = graphic.GetType().GetMethod("SetUseOutLine",
                        BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
                    if (setUse != null) setUse.Invoke(graphic, new object[] { true });
                    return;
                }
            }

            var settings = PSD2UGUISettings.Instance;
            var outlineType = ComponentTypeResolver.Resolve(settings.outlineComponentTypeName, typeof(Outline));
            var comp = GetOrAddComponentByType(go, outlineType);
            if (comp is Shadow s)
            {
                s.effectColor = color;
                s.effectDistance = new Vector2(size, -size);
            }
            else if (comp != null)
            {
                SetField(comp, "effectColor", color);
                SetField(comp, "m_OutlineWidth", size);
                SetField(comp, "OutlineWidth", size);
            }
        }

        private void ApplyShadow(GameObject go, Color color, Vector2 dis)
        {
            var settings = PSD2UGUISettings.Instance;
            var shadowType = ComponentTypeResolver.Resolve(settings.shadowComponentTypeName, typeof(Shadow));
            var comp = GetOrAddComponentByType(go, shadowType);
            if (comp is Shadow s)
            {
                s.effectColor = color;
                s.effectDistance = dis;
            }
            else if (comp != null)
            {
                SetField(comp, "effectColor", color);
                SetField(comp, "effectDistance", dis);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            if (target == null) return;
            var t = target.GetType();
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null && (value == null || f.FieldType.IsInstanceOfType(value) || CanAssign(f.FieldType, value)))
            {
                try { f.SetValue(target, ConvertValue(f.FieldType, value)); return; }
                catch { }
            }
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite)
            {
                try { p.SetValue(target, ConvertValue(p.PropertyType, value)); }
                catch { }
            }
        }

        private static bool CanAssign(Type fieldType, object value)
        {
            if (value == null) return !fieldType.IsValueType;
            if (fieldType.IsInstanceOfType(value)) return true;
            if (fieldType == typeof(int) && (value is float || value is double)) return true;
            return false;
        }

        private static object ConvertValue(Type t, object v)
        {
            if (v == null) return null;
            if (t.IsInstanceOfType(v)) return v;
            if (t == typeof(int)) return Convert.ToInt32(v);
            if (t == typeof(float)) return Convert.ToSingle(v);
            return v;
        }

        private GameObject CreateUIByAttribute(GameObject parent, string name = null)
        {
            GameObject go;
            if (m_attr.uiType == UIType.Text)
            {
                go = CreateUI(parent, GetUIName(name));
                SetTextData(go);
            }
            else if (m_attr.uiType == UIType.Image)
            {
                go = CreateUI(parent, GetUIName(name));
                SetImageData(go);
            }
            else
            {
                go = CreateUI(parent, GetUIName(name));
                SetPosAndSize(go);
            }
            go.SetActive(!m_attr.isHide);
            return go;
        }

        private GameObject UpdateUIByAttribute(GameObject go)
        {
            if (m_attr.uiType == UIType.Text) SetTextData(go);
            else if (m_attr.uiType == UIType.Image) SetImageData(go);
            else SetPosAndSize(go);
            return go;
        }

        private string GetUIName(string name)
        {
            if (m_attr.uiType == UIType.Text)
            {
                return $"{(name == null ? "" : name + "_")}Text";
            }
            if (m_attr.uiType == UIType.Image)
            {
                return $"{(name == null ? "" : name + "_")}{(m_attr.isButton ? "Button" : "Image")}";
            }
            return $"{(name == null ? "" : name + "_")}Group";
        }
        #endregion
    }
}
#endif
