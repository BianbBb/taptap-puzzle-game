#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DGame.PSD2UGUI
{
    public enum PSD2UGUITextComponentType
    {
        Text,
#if TextMeshPro
        TextMeshPro,
#endif
#if UNITEXT
        UniText,
#endif
    }

    /// <summary>
    /// 字体名称 → 字体资源路径 的映射条目
    /// </summary>
    [Serializable]
    public class FontPathEntry
    {
        public string fontName;
        public string assetPath;
    }

    /// <summary>
    /// PSD2UGUI 设置。允许在 Inspector 中配置使用的组件类型、字体路径、UI 分辨率等。
    /// 通过类名(可包含命名空间)在运行时反射查找具体组件类型。
    /// </summary>
    public class PSD2UGUISettings : ScriptableObject
    {
        private const string AssetPath = "Assets/Editor/PSD2UGUI/PSD2UGUISettings.asset";

        [Header("UI 分辨率")]
        public Vector2 resolution = new Vector2(750, 1334);

        [Header("文本组件")]
        [Tooltip("PSD 文本图层生成的组件类型")]
        public PSD2UGUITextComponentType textComponentType = PSD2UGUITextComponentType.Text;

        [Tooltip("文本组件类名, 必须派生自 UnityEngine.UI.Text")]
        public string textComponentTypeName = "GameLogic.UIText";

#if TextMeshPro
        [Tooltip("TextMeshPro 组件类名, 必须派生自 TMPro.TMP_Text")]
        public string textMeshProComponentTypeName = "TMPro.TextMeshProUGUI";
#endif
#if UNITEXT
        [Tooltip("UniText 组件类名, 必须派生自 LightSide.UniTextBase")]
        public string uniTextComponentTypeName = "LightSide.UniText";
#endif

        [Header("组件类名(支持带命名空间, 例如 GameLogic.UIText; 留空则使用 Unity 自带的组件)")]
        [Tooltip("图片组件类名, 必须派生自 UnityEngine.UI.Image")]
        public string imageComponentTypeName = "GameLogic.UIImage";

        [Tooltip("按钮组件类名, 必须派生自 UnityEngine.UI.Selectable 或 Button")]
        public string buttonComponentTypeName = "GameLogic.UIButton";

        [Tooltip("RawImage 组件类名, 留空则使用 UnityEngine.UI.RawImage")]
        public string rawImageComponentTypeName = "";

        [Tooltip("阴影组件类名, 留空则使用 UnityEngine.UI.Shadow")]
        public string shadowComponentTypeName = "";

        [Tooltip("描边组件类名, 留空则使用 UnityEngine.UI.Outline。若组件是 Text 内置扩展可填该 Text 自身类名(由代码反射 SetOutLineColor)")]
        public string outlineComponentTypeName = "";

        [Tooltip("渐变组件类名, 留空则忽略渐变设置。可填一个独立 GradientColor 组件类名, 或写 Text 自身类名(由代码反射 SetGradientColor)")]
        public string gradientComponentTypeName = "";

        [Header("图片资源查找路径")]
        public string[] spriteSearchFolders = new[] { "Assets/BundleAssets/UIRaw" };
        public string[] textureSearchFolders = new[] { "Assets/BundleAssets/UIRaw/Raw" };

        [Header("字体设置")]
        public string defaultFontPath = "Assets/BundleAssets/Fonts/SmileySans.ttf";
        public List<FontPathEntry> fontPaths = new List<FontPathEntry>();

#if TextMeshPro
        [Header("TextMeshPro 字体设置")]
        public string defaultTmpFontAssetPath = "Assets/Plugins/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        public List<FontPathEntry> tmpFontAssetPaths = new List<FontPathEntry>();
#endif
#if UNITEXT
        [Header("UniText 字体设置")]
        public string defaultUniTextFontAssetPath = "";
        public List<FontPathEntry> uniTextFontAssetPaths = new List<FontPathEntry>();
#endif

        private Dictionary<string, string> m_fontPathCache;
#if TextMeshPro
        private Dictionary<string, string> m_tmpFontPathCache;
#endif
#if UNITEXT
        private Dictionary<string, string> m_uniTextFontPathCache;
#endif

        public string GetFontPath(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                return defaultFontPath;
            }
            if (m_fontPathCache == null)
            {
                m_fontPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in fontPaths)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.fontName))
                    {
                        m_fontPathCache[entry.fontName] = entry.assetPath;
                    }
                }
            }
            return m_fontPathCache.TryGetValue(fontName, out string p) ? p : defaultFontPath;
        }

#if TextMeshPro
        public string GetTmpFontAssetPath(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                return defaultTmpFontAssetPath;
            }
            if (m_tmpFontPathCache == null)
            {
                m_tmpFontPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in tmpFontAssetPaths)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.fontName))
                    {
                        m_tmpFontPathCache[entry.fontName] = entry.assetPath;
                    }
                }
            }
            return m_tmpFontPathCache.TryGetValue(fontName, out string p) ? p : defaultTmpFontAssetPath;
        }
#endif

#if UNITEXT
        public string GetUniTextFontAssetPath(string fontName)
        {
            if (string.IsNullOrEmpty(fontName))
            {
                return defaultUniTextFontAssetPath;
            }
            if (m_uniTextFontPathCache == null)
            {
                m_uniTextFontPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in uniTextFontAssetPaths)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.fontName))
                    {
                        m_uniTextFontPathCache[entry.fontName] = entry.assetPath;
                    }
                }
            }
            return m_uniTextFontPathCache.TryGetValue(fontName, out string p) ? p : defaultUniTextFontAssetPath;
        }
#endif

        public void ClearCache()
        {
            m_fontPathCache = null;
#if TextMeshPro
            m_tmpFontPathCache = null;
#endif
#if UNITEXT
            m_uniTextFontPathCache = null;
#endif
        }

        private static PSD2UGUISettings s_instance;

        public static PSD2UGUISettings Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = AssetDatabase.LoadAssetAtPath<PSD2UGUISettings>(AssetPath);
                if (s_instance == null)
                {
                    s_instance = CreateInstance<PSD2UGUISettings>();
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AssetPath));
                    AssetDatabase.CreateAsset(s_instance, AssetPath);
                    AssetDatabase.SaveAssets();
                }
                return s_instance;
            }
        }

        [MenuItem("DGame Tools/UISettings/Open PSD2UGUI Settings")]
        public static void OpenSettings()
        {
            Selection.activeObject = Instance;
            EditorGUIUtility.PingObject(Instance);
        }
    }
}
#endif
