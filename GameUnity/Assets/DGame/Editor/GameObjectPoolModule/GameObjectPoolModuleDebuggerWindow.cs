using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DGame
{
    internal sealed class GameObjectPoolModuleDebuggerWindow : EditorWindow
    {
        private const double AUTO_REFRESH_INTERVAL = 0.5d;
        private const int MAX_OBJECT_ROWS = 200;

        private readonly List<GameObjectPoolDebugInfo> m_poolInfos = new List<GameObjectPoolDebugInfo>();
        private readonly Dictionary<string, bool> m_poolFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> m_objectFoldouts = new Dictionary<string, bool>();

        private Vector2 m_scrollPosition;
        private bool m_autoRefresh = true;
        private string m_searchText = string.Empty;
        private double m_nextRefreshTime;
        private string m_errorMessage;
        private int m_visiblePoolRowIndex;

        [MenuItem("DGame Tools/Debugger/GameObject Pool")]
        internal static void Open()
        {
            var window = GetWindow<GameObjectPoolModuleDebuggerWindow>();
            window.titleContent = new GUIContent("GameObject Pool");
            window.minSize = new Vector2(780f, 440f);
            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            Refresh();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (!m_autoRefresh || EditorApplication.timeSinceStartup < m_nextRefreshTime)
            {
                return;
            }

            Refresh();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("GameObjectPoolModule 只在 Play Mode 中可查看。", MessageType.Info);
                return;
            }

            if (!string.IsNullOrEmpty(m_errorMessage))
            {
                EditorGUILayout.HelpBox(m_errorMessage, MessageType.Warning);
                return;
            }

            DrawSummary();
            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            DrawPoolInfos();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                Refresh();
            }

            m_autoRefresh = GUILayout.Toggle(m_autoRefresh, "自动刷新", EditorStyles.toolbarButton,
                GUILayout.Width(80f));
            GUILayout.Space(8f);
            GUILayout.Label("搜索", GUILayout.Width(32f));
            m_searchText = GUILayout.TextField(m_searchText, GUILayout.MinWidth(200f));
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!Application.isPlaying || GetModule() == null))
            {
                if (GUILayout.Button("销毁非常驻池", EditorStyles.toolbarButton, GUILayout.Width(96f))
                    && EditorUtility.DisplayDialog("销毁非常驻对象池", "确认销毁所有非 DontDestroy 对象池？", "确认", "取消"))
                {
                    GetModule()?.DestroyAllPool(false);
                    Refresh();
                }

                if (GUILayout.Button("销毁全部", EditorStyles.toolbarButton, GUILayout.Width(72f))
                    && EditorUtility.DisplayDialog("销毁全部对象池", "确认销毁全部 GameObjectPoolModule 对象池？", "确认", "取消"))
                {
                    GetModule()?.DestroyAllPool(true);
                    Refresh();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            var totalObjects = 0;
            var spawnedObjects = 0;
            var idleObjects = 0;
            var markedPools = 0;
            for (var i = 0; i < m_poolInfos.Count; i++)
            {
                totalObjects += m_poolInfos[i].Count;
                spawnedObjects += m_poolInfos[i].SpawnedCount;
                idleObjects += m_poolInfos[i].NoSpawnCount;
                if (m_poolInfos[i].MarkedForDestroy)
                {
                    markedPools++;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("运行时状态", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Pools: {m_poolInfos.Count}", GUILayout.Width(110f));
            EditorGUILayout.LabelField($"Total: {totalObjects}", GUILayout.Width(110f));
            EditorGUILayout.LabelField($"Spawned: {spawnedObjects}", GUILayout.Width(120f));
            EditorGUILayout.LabelField($"Idle: {idleObjects}", GUILayout.Width(100f));
            EditorGUILayout.LabelField($"Marked: {markedPools}", GUILayout.Width(110f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawPoolInfos()
        {
            if (m_poolInfos.Count <= 0)
            {
                EditorGUILayout.HelpBox("暂无 GameObjectPool。", MessageType.Info);
                return;
            }

            m_visiblePoolRowIndex = 0;
            for (var i = 0; i < m_poolInfos.Count; i++)
            {
                var info = m_poolInfos[i];
                if (!MatchSearch(info))
                {
                    continue;
                }

                DrawPoolInfo(info, m_visiblePoolRowIndex++);
            }
        }

        private void DrawPoolInfo(GameObjectPoolDebugInfo info, int rowIndex)
        {
            var key = string.IsNullOrEmpty(info.Location) ? "<empty>" : info.Location;
            if (!m_poolFoldouts.ContainsKey(key))
            {
                m_poolFoldouts.Add(key, true);
            }

            var poolRect = EditorGUILayout.BeginVertical("box");
            DrawZebraStriping(poolRect, rowIndex);
            EditorGUILayout.BeginHorizontal();
            m_poolFoldouts[key] = EditorGUILayout.Foldout(m_poolFoldouts[key], key, true);
            GUILayout.FlexibleSpace();
            DrawUsageBar(info);
            if (GUILayout.Button("销毁", GUILayout.Width(56f))
                && EditorUtility.DisplayDialog("销毁对象池", $"确认销毁对象池：{info.Location}？", "确认", "取消"))
            {
                GetModule()?.DestroyPool(info.Location);
                Refresh();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"总数: {info.Count}", GUILayout.Width(90f));
            EditorGUILayout.LabelField($"使用中: {info.SpawnedCount}", GUILayout.Width(110f));
            EditorGUILayout.LabelField($"空闲: {info.NoSpawnCount}", GUILayout.Width(90f));
            EditorGUILayout.LabelField($"最大: {GetCapacityText(info.MaxCapacity)}", GUILayout.Width(120f));
            EditorGUILayout.LabelField($"自动销毁: {info.AutoDestroyTime:F1}s", GUILayout.Width(140f));
            EditorGUILayout.LabelField($"空闲: {info.IdleTime:F1}s", GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawStateLabel("常驻", info.DontDestroy);
            DrawStateLabel("标记销毁", info.MarkedForDestroy);
            DrawStateLabel("已销毁", info.IsDestroyed);
            DrawStateLabel("可自动销毁", info.CanAutoDestroy);
            EditorGUILayout.EndHorizontal();

            if (m_poolFoldouts[key])
            {
                DrawObjectInfos(key, info);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawUsageBar(GameObjectPoolDebugInfo info)
        {
            var rect = GUILayoutUtility.GetRect(220f, 18f, GUILayout.Width(220f));
            var usage = info.Count > 0 ? Mathf.Clamp01((float)info.SpawnedCount / info.Count) : 0f;
            EditorGUI.ProgressBar(rect, usage, $"使用中 {info.SpawnedCount}/{info.Count}");
        }

        private static void DrawStateLabel(string label, bool active)
        {
            var oldColor = GUI.color;
            GUI.color = active ? Color.green : Color.gray;
            EditorGUILayout.LabelField($"{label}: {(active ? "是" : "否")}", GUILayout.Width(120f));
            GUI.color = oldColor;
        }

        private void DrawObjectInfos(string poolKey, GameObjectPoolDebugInfo info)
        {
            var key = $"{poolKey}/objects";
            if (!m_objectFoldouts.ContainsKey(key))
            {
                m_objectFoldouts.Add(key, false);
            }

            var isOpen = m_objectFoldouts[key];
            UnityEditorUtil.LayoutFoldoutBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    EditorGUI.indentLevel++;
                    var count = Mathf.Min(info.Objects.Count, MAX_OBJECT_ROWS);
                    for (var i = 0; i < count; i++)
                    {
                        DrawObjectInfo(info.Objects[i], i);
                    }

                    if (info.Objects.Count > MAX_OBJECT_ROWS)
                    {
                        EditorGUILayout.HelpBox($"仅显示前 {MAX_OBJECT_ROWS} 个对象，当前共有 {info.Objects.Count} 个。",
                            MessageType.Info);
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }, $"对象详情 ({info.Objects.Count})", ref isOpen, true);
            m_objectFoldouts[key] = isOpen;
        }

        private static void DrawObjectInfo(GameObjectPoolObjectDebugInfo info, int rowIndex)
        {
            var rowRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawZebraStriping(rowRect, rowIndex);
            {
                EditorGUILayout.ObjectField("对象", info.GameObject, typeof(GameObject), true);
                EditorGUILayout.BeginHorizontal();
                {
                    var oldColor = GUI.color;
                    GUI.color = info.Spawned ? Color.green : Color.yellow;
                    GUILayout.Label("●", GUILayout.Width(18f));
                    GUI.color = oldColor;
                    EditorGUILayout.LabelField("状态", GUILayout.Width(40f));
                    EditorGUILayout.LabelField(info.Spawned ? "使用中" : "空闲", EditorStyles.miniLabel,
                        GUILayout.Width(80f));
                    EditorGUILayout.LabelField($"激活: {(info.ActiveSelf ? "是" : "否")}", EditorStyles.miniLabel,
                        GUILayout.Width(80f));
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.ObjectField("父节点", info.Parent, typeof(Transform), true);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawZebraStriping(Rect rowRect, int rowIndex)
        {
            if (rowIndex % 2 == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }

            float alpha = EditorGUIUtility.isProSkin ? 0.033f : 0.05f;
            Color color = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, alpha)
                : new Color(0f, 0f, 0f, alpha);
            EditorGUI.DrawRect(rowRect, color);
        }

        private void Refresh()
        {
            m_nextRefreshTime = EditorApplication.timeSinceStartup + AUTO_REFRESH_INTERVAL;
            m_poolInfos.Clear();
            m_errorMessage = null;
            if (!Application.isPlaying)
            {
                return;
            }

            var module = GetModule();
            if (module == null)
            {
                m_errorMessage = "未找到 IGameObjectPoolModule。";
                return;
            }

            try
            {
                module.GetDebugInfos(m_poolInfos);
            }
            catch (Exception e)
            {
                m_errorMessage = e.Message;
            }
        }

        private bool MatchSearch(GameObjectPoolDebugInfo info)
        {
            return string.IsNullOrEmpty(m_searchText)
                   || !string.IsNullOrEmpty(info.Location)
                   && info.Location.IndexOf(m_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IGameObjectPoolModule GetModule()
        {
            return ModuleSystem.GetModule<IGameObjectPoolModule>();
        }

        private static string GetCapacityText(int capacity)
        {
            return capacity == int.MaxValue ? "未限制" : capacity.ToString();
        }
    }
}
