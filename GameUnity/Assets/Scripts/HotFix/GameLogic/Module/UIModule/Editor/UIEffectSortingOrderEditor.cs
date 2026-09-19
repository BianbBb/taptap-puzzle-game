#if UNITY_EDITOR

using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GameLogic
{
    [CustomEditor(typeof(UIEffectSortingOrder))]
    [CanEditMultipleObjects]
    public class UIEffectSortingOrderEditor : Editor
    {
        private const string BasicPanelKey = "UIEffectSortingOrder.BasicPanelOpen";
        private const string StatePanelKey = "UIEffectSortingOrder.StatePanelOpen";
        private const string PreviewPanelKey = "UIEffectSortingOrder.PreviewPanelOpen";

        private static readonly FieldInfo ChildBaseOrderField = typeof(UIEffectSortingOrder).GetField(
            "m_childBaseOrder", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly string[] ApplyTargetNames =
        {
            "自动",
            "SortingGroup",
            "Renderer",
            "全部"
        };

        private SerializedProperty m_applyTarget;
        private SerializedProperty m_orderOffset;
        private SerializedProperty m_followCanvasOrder;
        private SerializedProperty m_keepChildOrder;

        private bool m_basicPanelOpen = true;
        private bool m_statePanelOpen = true;
        private bool m_previewPanelOpen = true;
        private Vector2 m_previewScrollPosition;

        private void OnEnable()
        {
            m_basicPanelOpen = EditorPrefs.GetBool(BasicPanelKey, m_basicPanelOpen);
            m_statePanelOpen = EditorPrefs.GetBool(StatePanelKey, m_statePanelOpen);
            m_previewPanelOpen = EditorPrefs.GetBool(PreviewPanelKey, m_previewPanelOpen);

            m_applyTarget = serializedObject.FindProperty("m_applyTarget");
            m_orderOffset = serializedObject.FindProperty("m_orderOffset");
            m_followCanvasOrder = serializedObject.FindProperty("m_followCanvasOrder");
            m_keepChildOrder = serializedObject.FindProperty("m_keepChildOrder");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawInspectorHeader();
            DrawBasicSettings();

            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();

            DrawStateInfo();
            DrawPreview();

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                EditorPrefs.SetBool(BasicPanelKey, m_basicPanelOpen);
                EditorPrefs.SetBool(StatePanelKey, m_statePanelOpen);
                EditorPrefs.SetBool(PreviewPanelKey, m_previewPanelOpen);
            }
        }

        private void DrawInspectorHeader()
        {
            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            var icon = EditorGUIUtility.IconContent("ParticleSystem Icon").image;
            EditorGUILayout.LabelField(new GUIContent(" UI特效排序", icon), titleStyle);

            var descStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.LabelField("根据父级 Canvas 排序同步特效渲染层级", descStyle);
            EditorGUILayout.Space(6);
        }

        private void DrawBasicSettings()
        {
            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");

                DrawApplyTargetToolbar();
                EditorGUILayout.PropertyField(m_orderOffset, new GUIContent("排序偏移", "最终排序 = Canvas Sorting Order + 排序偏移"));
                EditorGUILayout.PropertyField(m_followCanvasOrder, new GUIContent("跟随Canvas排序", "启用后会读取最近父级 Canvas 的 Sorting Order"));
                EditorGUILayout.PropertyField(m_keepChildOrder, new GUIContent("保留子节点排序", "启用后会记录子 Renderer/SortingGroup 当前排序，并叠加到目标排序上"));

                EditorGUILayout.Space(3);
                DrawModeHelp();

                EditorGUILayout.EndVertical();
            }, "基础设置", ref m_basicPanelOpen, true);

            EditorGUILayout.Space(5);
        }

        private void DrawApplyTargetToolbar()
        {
            if (m_applyTarget.hasMultipleDifferentValues)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("应用目标", "选择排序值应用到哪些渲染组件"), GUILayout.Width(80));
                EditorGUILayout.PropertyField(m_applyTarget, GUIContent.none);
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (EditorGUIUtility.currentViewWidth < 330f)
            {
                EditorGUILayout.LabelField(new GUIContent("应用目标", "选择排序值应用到哪些渲染组件"));
                m_applyTarget.enumValueIndex = DrawWrappedApplyTargetToolbar(m_applyTarget.enumValueIndex);
                return;
            }

            if (EditorGUIUtility.currentViewWidth < 430f)
            {
                EditorGUILayout.LabelField(new GUIContent("应用目标", "选择排序值应用到哪些渲染组件"));
                m_applyTarget.enumValueIndex = GUILayout.Toolbar(m_applyTarget.enumValueIndex, ApplyTargetNames);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("应用目标", "选择排序值应用到哪些渲染组件"), GUILayout.Width(80));
            m_applyTarget.enumValueIndex = GUILayout.Toolbar(m_applyTarget.enumValueIndex, ApplyTargetNames);
            EditorGUILayout.EndHorizontal();
        }

        private int DrawWrappedApplyTargetToolbar(int selectedIndex)
        {
            EditorGUILayout.BeginHorizontal();
            selectedIndex = DrawApplyTargetToggle(selectedIndex, 0);
            selectedIndex = DrawApplyTargetToggle(selectedIndex, 1);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            selectedIndex = DrawApplyTargetToggle(selectedIndex, 2);
            selectedIndex = DrawApplyTargetToggle(selectedIndex, 3);
            EditorGUILayout.EndHorizontal();

            return selectedIndex;
        }

        private int DrawApplyTargetToggle(int selectedIndex, int index)
        {
            bool selected = GUILayout.Toggle(selectedIndex == index, ApplyTargetNames[index], EditorStyles.miniButton);
            return selected ? index : selectedIndex;
        }

        private void DrawModeHelp()
        {
            if (m_applyTarget.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("多选对象的应用目标不一致。", MessageType.Info);
                return;
            }

            var applyTarget = (UIEffectSortingOrder.ApplyTarget)m_applyTarget.enumValueIndex;

            switch (applyTarget)
            {
                case UIEffectSortingOrder.ApplyTarget.Auto:
                    EditorGUILayout.HelpBox("自动模式：存在 SortingGroup 时只处理 SortingGroup；不存在时处理 Renderer。", MessageType.Info);
                    break;

                case UIEffectSortingOrder.ApplyTarget.Both:
                    EditorGUILayout.HelpBox("全部模式：同时写入 SortingGroup 和 Renderer，适合混合结构特效。", MessageType.Info);
                    break;

                case UIEffectSortingOrder.ApplyTarget.SortingGroup:
                    EditorGUILayout.HelpBox("只处理 SortingGroup，推荐用于规范 UI 特效预制体。", MessageType.None);
                    break;

                case UIEffectSortingOrder.ApplyTarget.Renderer:
                    EditorGUILayout.HelpBox("只处理 Renderer，适合未使用 SortingGroup 的简单粒子或 Sprite 特效。", MessageType.None);
                    break;
            }
        }

        private void DrawStateInfo()
        {
            UnityEditorUtil.LayoutFrameBox(() =>
            {
                if (targets.Length != 1)
                {
                    EditorGUILayout.HelpBox("多选时不显示单个对象的运行状态。", MessageType.Info);
                    return;
                }

                var effectSortingOrder = target as UIEffectSortingOrder;

                if (effectSortingOrder == null)
                {
                    return;
                }

                EditorGUILayout.BeginVertical("HelpBox");

                var canvas = effectSortingOrder.GetComponentInParent<Canvas>();
                int canvasOrder = canvas != null ? canvas.sortingOrder : 0;
                int targetOrder = m_followCanvasOrder.boolValue ? canvasOrder + m_orderOffset.intValue : m_orderOffset.intValue;
                int sortingGroupCount = effectSortingOrder.GetComponentsInChildren<SortingGroup>(true).Length;
                int rendererCount = effectSortingOrder.GetComponentsInChildren<Renderer>(true).Length;

                EditorGUILayout.LabelField("父级 Canvas", canvas != null ? canvas.name : "未找到", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Canvas 排序", canvas != null ? canvasOrder.ToString() : "-", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("目标排序", targetOrder.ToString(), EditorStyles.miniLabel);
                EditorGUILayout.LabelField("SortingGroup 数量", sortingGroupCount.ToString(), EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Renderer 数量", rendererCount.ToString(), EditorStyles.miniLabel);

                if (m_followCanvasOrder.boolValue && canvas == null)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox("启用了跟随 Canvas 排序，但当前节点没有父级 Canvas。", MessageType.Warning);
                }

                EditorGUILayout.EndVertical();
            }, "当前状态", ref m_statePanelOpen, true);

            EditorGUILayout.Space(5);
        }

        private void DrawPreview()
        {
            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");

                var entries = BuildPreviewEntries();

                if (entries.Count <= 0)
                {
                    EditorGUILayout.HelpBox("当前配置不会修改任何 SortingGroup 或 Renderer。", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    return;
                }

                DrawPreviewHeader(entries.Count);

                float viewHeight = Mathf.Min(220f, 24f + entries.Count * 24f);
                m_previewScrollPosition = EditorGUILayout.BeginScrollView(m_previewScrollPosition, GUILayout.Height(viewHeight));

                for (int i = 0; i < entries.Count; i++)
                {
                    DrawPreviewItem(entries[i]);
                }

                EditorGUILayout.EndScrollView();

                EditorGUILayout.EndVertical();
            }, "影响预览", ref m_previewPanelOpen, true);
        }

        private void DrawPreviewHeader(int count)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"将修改 {count} 个组件", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("当前 -> 修改后", EditorStyles.miniBoldLabel, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewItem(PreviewEntry entry)
        {
            EditorGUILayout.BeginHorizontal();

            var content = new GUIContent(entry.Path, "点击定位该物体");
            if (GUILayout.Button(content, EditorStyles.miniButtonLeft, GUILayout.MinWidth(120)))
            {
                Selection.activeGameObject = entry.GameObject;
                EditorGUIUtility.PingObject(entry.GameObject);
            }

            EditorGUILayout.LabelField(entry.ComponentName, EditorStyles.miniLabel, GUILayout.Width(88));
            EditorGUILayout.LabelField($"{entry.CurrentOrder} -> {entry.TargetOrder}", EditorStyles.miniLabel, GUILayout.Width(90));

            if (entry.UseChildBaseOrder)
            {
                var label = entry.UseCachedBaseOrder ? $"基础:{entry.BaseOrder}" : $"基础:{entry.BaseOrder}*";
                EditorGUILayout.LabelField(new GUIContent(label, "* 表示下次刷新时会按当前排序记录基础排序"),
                    EditorStyles.miniLabel, GUILayout.Width(68));
            }

            EditorGUILayout.EndHorizontal();
        }

        private List<PreviewEntry> BuildPreviewEntries()
        {
            var entries = new List<PreviewEntry>();

            for (int i = 0; i < targets.Length; i++)
            {
                var effectSortingOrder = targets[i] as UIEffectSortingOrder;

                if (effectSortingOrder == null)
                {
                    continue;
                }

                entries.AddRange(BuildPreviewEntries(effectSortingOrder));
            }

            return entries;
        }

        private static List<PreviewEntry> BuildPreviewEntries(UIEffectSortingOrder effectSortingOrder)
        {
            var entries = new List<PreviewEntry>();
            var settings = GetSettings(effectSortingOrder);
            var canvas = effectSortingOrder.GetComponentInParent<Canvas>();

            if (settings.followCanvasOrder && canvas == null)
            {
                return entries;
            }

            int canvasOrder = settings.followCanvasOrder ? canvas.sortingOrder : 0;
            int targetOrder = canvasOrder + settings.orderOffset;
            var sortingGroups = effectSortingOrder.GetComponentsInChildren<SortingGroup>(true);
            var renderers = effectSortingOrder.GetComponentsInChildren<Renderer>(true);
            var cachedBaseOrder = GetCachedBaseOrder(effectSortingOrder);

            switch (settings.applyTarget)
            {
                case UIEffectSortingOrder.ApplyTarget.SortingGroup:
                    AddSortingGroupEntries(entries, effectSortingOrder.transform, sortingGroups, targetOrder, settings.keepChildOrder, cachedBaseOrder);
                    break;

                case UIEffectSortingOrder.ApplyTarget.Renderer:
                    AddRendererEntries(entries, effectSortingOrder.transform, renderers, targetOrder, settings.keepChildOrder, cachedBaseOrder);
                    break;

                case UIEffectSortingOrder.ApplyTarget.Both:
                    AddSortingGroupEntries(entries, effectSortingOrder.transform, sortingGroups, targetOrder, settings.keepChildOrder, cachedBaseOrder);
                    AddRendererEntries(entries, effectSortingOrder.transform, renderers, targetOrder, settings.keepChildOrder, cachedBaseOrder);
                    break;

                case UIEffectSortingOrder.ApplyTarget.Auto:
                default:
                    if (sortingGroups.Length > 0)
                    {
                        AddSortingGroupEntries(entries, effectSortingOrder.transform, sortingGroups, targetOrder, settings.keepChildOrder, cachedBaseOrder);
                    }
                    else
                    {
                        AddRendererEntries(entries, effectSortingOrder.transform, renderers, targetOrder, settings.keepChildOrder, cachedBaseOrder);
                    }
                    break;
            }

            return entries;
        }

        private static void AddSortingGroupEntries(List<PreviewEntry> entries, Transform root, SortingGroup[] sortingGroups,
            int targetOrder, bool keepChildOrder, IReadOnlyDictionary<Object, int> cachedBaseOrder)
        {
            for (int i = 0; i < sortingGroups.Length; i++)
            {
                var sortingGroup = sortingGroups[i];
                int baseOrder = GetBaseOrder(sortingGroup, sortingGroup.sortingOrder, keepChildOrder, cachedBaseOrder, out var useCachedBaseOrder);
                entries.Add(new PreviewEntry(sortingGroup.gameObject, GetTransformPath(sortingGroup.transform, root),
                    "SortingGroup", sortingGroup.sortingOrder, targetOrder + baseOrder, baseOrder, keepChildOrder, useCachedBaseOrder));
            }
        }

        private static void AddRendererEntries(List<PreviewEntry> entries, Transform root, Renderer[] renderers,
            int targetOrder, bool keepChildOrder, IReadOnlyDictionary<Object, int> cachedBaseOrder)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                int baseOrder = GetBaseOrder(renderer, renderer.sortingOrder, keepChildOrder, cachedBaseOrder, out var useCachedBaseOrder);
                entries.Add(new PreviewEntry(renderer.gameObject, GetTransformPath(renderer.transform, root),
                    renderer.GetType().Name, renderer.sortingOrder, targetOrder + baseOrder, baseOrder, keepChildOrder, useCachedBaseOrder));
            }
        }

        private static int GetBaseOrder(Object element, int currentOrder, bool keepChildOrder,
            IReadOnlyDictionary<Object, int> cachedBaseOrder, out bool useCachedBaseOrder)
        {
            useCachedBaseOrder = false;

            if (!keepChildOrder)
            {
                return 0;
            }

            if (cachedBaseOrder != null && cachedBaseOrder.TryGetValue(element, out var baseOrder))
            {
                useCachedBaseOrder = true;
                return baseOrder;
            }

            return currentOrder;
        }

        private static IReadOnlyDictionary<Object, int> GetCachedBaseOrder(UIEffectSortingOrder effectSortingOrder)
        {
            return ChildBaseOrderField?.GetValue(effectSortingOrder) as IReadOnlyDictionary<Object, int>;
        }

        private static SortingSettings GetSettings(UIEffectSortingOrder effectSortingOrder)
        {
            using (var serializedTarget = new SerializedObject(effectSortingOrder))
            {
                return new SortingSettings
                {
                    applyTarget = (UIEffectSortingOrder.ApplyTarget)serializedTarget.FindProperty("m_applyTarget").enumValueIndex,
                    orderOffset = serializedTarget.FindProperty("m_orderOffset").intValue,
                    followCanvasOrder = serializedTarget.FindProperty("m_followCanvasOrder").boolValue,
                    keepChildOrder = serializedTarget.FindProperty("m_keepChildOrder").boolValue
                };
            }
        }

        private static string GetTransformPath(Transform transform, Transform root)
        {
            var names = new List<string>();
            var current = transform;

            while (current != null)
            {
                names.Add(current.name);

                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private struct SortingSettings
        {
            public UIEffectSortingOrder.ApplyTarget applyTarget;
            public int orderOffset;
            public bool followCanvasOrder;
            public bool keepChildOrder;
        }

        private readonly struct PreviewEntry
        {
            public PreviewEntry(GameObject gameObject, string path, string componentName, int currentOrder,
                int targetOrder, int baseOrder, bool useChildBaseOrder, bool useCachedBaseOrder)
            {
                GameObject = gameObject;
                Path = path;
                ComponentName = componentName;
                CurrentOrder = currentOrder;
                TargetOrder = targetOrder;
                BaseOrder = baseOrder;
                UseChildBaseOrder = useChildBaseOrder;
                UseCachedBaseOrder = useCachedBaseOrder;
            }

            public GameObject GameObject { get; }
            public string Path { get; }
            public string ComponentName { get; }
            public int CurrentOrder { get; }
            public int TargetOrder { get; }
            public int BaseOrder { get; }
            public bool UseChildBaseOrder { get; }
            public bool UseCachedBaseOrder { get; }
        }
    }
}

#endif
