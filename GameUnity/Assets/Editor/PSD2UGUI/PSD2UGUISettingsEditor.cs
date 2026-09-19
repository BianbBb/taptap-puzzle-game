#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

#if TextMeshPro
using TMPro;
#endif
#if UNITEXT
using LightSide;
#endif

namespace DGame.PSD2UGUI
{
    /// <summary>
    /// PSD2UGUISettings 的自定义 Inspector：
    /// 文件夹字段改为 DefaultAsset ObjectField（只接受 Project 中的文件夹）；
    /// 字体路径改为 Unity Font / TMP_FontAsset ObjectField。
    /// </summary>
    [CustomEditor(typeof(PSD2UGUISettings))]
    public class PSD2UGUISettingsEditor : UnityEditor.Editor
    {
#if TextMeshPro || UNITEXT
        private int m_textSettingsViewIndex;
        private static readonly string[] s_textSettingsTabs =
        {
            "Unity Text 设置",
#if TextMeshPro
            "TextMeshPro 设置",
#endif
#if UNITEXT
            "UniText 设置",
#endif
        };
#endif
#if TextMeshPro
        private const int TextMeshProSettingsIndex = 1;
#endif
#if UNITEXT
#if TextMeshPro
        private const int UniTextSettingsIndex = 2;
#else
        private const int UniTextSettingsIndex = 1;
#endif
#endif

        private static readonly HashSet<string> FolderArrayProps = new HashSet<string>
        {
            "spriteSearchFolders",
            "textureSearchFolders",
        };

        /// <summary>
        /// 字段名 → 中文显示名
        /// </summary>
        private static readonly Dictionary<string, string> LabelMap = new Dictionary<string, string>
        {
            { "resolution", "UI 分辨率" },
            { "textComponentType", "文本组件类型" },
            { "textComponentTypeName", "文本组件名" },
#if TextMeshPro
            { "textMeshProComponentTypeName", "TextMeshPro 组件名" },
#endif
#if UNITEXT
            { "uniTextComponentTypeName", "UniText 组件名" },
#endif
            { "imageComponentTypeName", "图片组件名" },
            { "buttonComponentTypeName", "按钮组件名" },
            { "rawImageComponentTypeName", "RawImage 组件名" },
            { "shadowComponentTypeName", "阴影组件名" },
            { "outlineComponentTypeName", "描边组件名" },
            { "gradientComponentTypeName", "渐变组件名" },
            { "spriteSearchFolders", "Sprite 查找路径" },
            { "textureSearchFolders", "Texture 查找路径" },
            { "defaultFontPath", "默认字体" },
            { "fontPaths", "字体映射" },
#if TextMeshPro
            { "defaultTmpFontAssetPath", "默认 TMP 字体" },
            { "tmpFontAssetPaths", "TMP 字体映射" },
#endif
#if UNITEXT
            { "defaultUniTextFontAssetPath", "默认 UniText 字体" },
            { "uniTextFontAssetPaths", "UniText 字体映射" },
#endif
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space(4);
#if TextMeshPro || UNITEXT
            DrawTextSettingsSwitch();
#endif
            var iter = serializedObject.GetIterator();
            iter.NextVisible(true); // m_Script
            while (iter.NextVisible(false))
            {
                if (iter.name == "textComponentType")
                {
                    continue;
                }
                if (!ShouldDrawProperty(iter.name))
                {
                    continue;
                }
                if (FolderArrayProps.Contains(iter.name))
                {
                    DrawFolderArray(iter);
                }
                else if (iter.name == "defaultFontPath")
                {
                    DrawAssetPathField<Font>(iter, GetLabel(iter));
                }
                else if (iter.name == "fontPaths")
                {
                    DrawFontPathList<Font>(iter, "PSD 字体名 → Unity Font");
                }
                else if (iter.name == "defaultTmpFontAssetPath")
                {
#if TextMeshPro
                    DrawAssetPathField<TMP_FontAsset>(iter, GetLabel(iter));
#endif
                }
                else if (iter.name == "tmpFontAssetPaths")
                {
#if TextMeshPro
                    DrawFontPathList<TMP_FontAsset>(iter, "PSD 字体名 → TMP_FontAsset");
#endif
                }
                else if (iter.name == "defaultUniTextFontAssetPath")
                {
#if UNITEXT
                    DrawAssetPathField<UniTextFont>(iter, GetLabel(iter));
#endif
                }
                else if (iter.name == "uniTextFontAssetPaths")
                {
#if UNITEXT
                    DrawFontPathList<UniTextFont>(iter, "PSD 字体名 → UniTextFont");
#endif
                }
                else if (iter.name == "resolution")
                {
                    EditorGUILayout.PropertyField(iter, GetLabel(iter), true);
                }
                else
                {
                    EditorGUILayout.PropertyField(iter, GetLabel(iter), true);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

#if TextMeshPro || UNITEXT
        private void DrawTextSettingsSwitch()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("文本配置显示", EditorStyles.boldLabel);
            m_textSettingsViewIndex = Mathf.Clamp(m_textSettingsViewIndex, 0, s_textSettingsTabs.Length - 1);
            m_textSettingsViewIndex = GUILayout.Toolbar(m_textSettingsViewIndex, s_textSettingsTabs, GUILayout.Height(24));
            EditorGUILayout.HelpBox("这里只切换设置页显示，不影响主界面生成使用的文本组件类型。", MessageType.None);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }
#endif

        private bool ShouldDrawProperty(string propertyName)
        {
#if TextMeshPro || UNITEXT
            bool showTextSettings = m_textSettingsViewIndex == 0;
            if (propertyName == "textComponentTypeName" ||
                propertyName == "defaultFontPath" ||
                propertyName == "fontPaths")
            {
                return showTextSettings;
            }
#endif
#if TextMeshPro
            if (propertyName == "textMeshProComponentTypeName" ||
                propertyName == "defaultTmpFontAssetPath" ||
                propertyName == "tmpFontAssetPaths")
            {
                return m_textSettingsViewIndex == TextMeshProSettingsIndex;
            }
#endif
#if UNITEXT
            if (propertyName == "uniTextComponentTypeName" ||
                propertyName == "defaultUniTextFontAssetPath" ||
                propertyName == "uniTextFontAssetPaths")
            {
                return m_textSettingsViewIndex == UniTextSettingsIndex;
            }
#endif
            return true;
        }

        private static GUIContent GetLabel(SerializedProperty prop)
        {
            string label = LabelMap.TryGetValue(prop.name, out var cn) ? cn : prop.displayName;
            return new GUIContent(label, prop.tooltip);
        }

        private void DrawFolderArray(SerializedProperty prop)
        {
            EditorGUILayout.LabelField(GetLabel(prop), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            int removeIdx = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var el = prop.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginHorizontal();

                DefaultAsset folder = string.IsNullOrEmpty(el.stringValue)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<DefaultAsset>(el.stringValue);

                EditorGUI.BeginChangeCheck();
                var newFolder = (DefaultAsset)EditorGUILayout.ObjectField($"路径 {i}", folder, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (newFolder == null)
                    {
                        el.stringValue = "";
                    }
                    else
                    {
                        string path = AssetDatabase.GetAssetPath(newFolder);
                        if (AssetDatabase.IsValidFolder(path))
                        {
                            el.stringValue = path;
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("无效选择", "请选择一个文件夹", "确定");
                        }
                    }
                }
                if (GUILayout.Button("-", GUILayout.Width(22)))
                {
                    removeIdx = i;
                }
                EditorGUILayout.EndHorizontal();
            }
            if (removeIdx >= 0)
            {
                prop.DeleteArrayElementAtIndex(removeIdx);
            }

            if (GUILayout.Button("+ 添加文件夹"))
            {
                prop.arraySize++;
                prop.GetArrayElementAtIndex(prop.arraySize - 1).stringValue = "";
            }
            EditorGUI.indentLevel--;
        }

        private void DrawAssetPathField<T>(SerializedProperty prop, GUIContent label) where T : Object
        {
            var current = string.IsNullOrEmpty(prop.stringValue)
                ? null
                : AssetDatabase.LoadAssetAtPath<T>(prop.stringValue);
            EditorGUI.BeginChangeCheck();
            var newObj = (T)EditorGUILayout.ObjectField(label, current, typeof(T), false);
            if (EditorGUI.EndChangeCheck())
            {
                prop.stringValue = newObj == null ? "" : AssetDatabase.GetAssetPath(newObj);
            }
        }

        private void DrawFontPathList<T>(SerializedProperty prop, string subtitle) where T : Object
        {
            var label = GetLabel(prop);
            EditorGUILayout.LabelField($"{label.text} ({subtitle})", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            int removeIdx = -1;
            for (int i = 0; i < prop.arraySize; i++)
            {
                var el = prop.GetArrayElementAtIndex(i);
                var nameProp = el.FindPropertyRelative("fontName");
                var pathProp = el.FindPropertyRelative("assetPath");

                EditorGUILayout.BeginHorizontal();
                nameProp.stringValue = EditorGUILayout.TextField(nameProp.stringValue, GUILayout.MinWidth(80));

                var current = string.IsNullOrEmpty(pathProp.stringValue)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<T>(pathProp.stringValue);
                EditorGUI.BeginChangeCheck();
                var newFont = (T)EditorGUILayout.ObjectField(current, typeof(T), false);
                if (EditorGUI.EndChangeCheck())
                {
                    pathProp.stringValue = newFont == null ? "" : AssetDatabase.GetAssetPath(newFont);
                }
                if (GUILayout.Button("-", GUILayout.Width(22))) removeIdx = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeIdx >= 0) prop.DeleteArrayElementAtIndex(removeIdx);

            if (GUILayout.Button("+ 添加字体映射"))
            {
                prop.arraySize++;
                var el = prop.GetArrayElementAtIndex(prop.arraySize - 1);
                el.FindPropertyRelative("fontName").stringValue = "";
                el.FindPropertyRelative("assetPath").stringValue = "";
            }
            EditorGUI.indentLevel--;
        }
    }
}
#endif
