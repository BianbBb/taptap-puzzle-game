using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DGame
{
    [CustomEditor(typeof(GameObjectPoolRoot))]
    internal sealed class GameObjectPoolRootInspector : DGameInspector
    {
        private const string RUNTIME_INFO_PANEL_KEY = "GameObjectPoolRoot.RuntimeInfoPanelOpen";
        private const string POOL_LIST_PANEL_KEY = "GameObjectPoolRoot.PoolListPanelOpen";
        private const string ACTION_PANEL_KEY = "GameObjectPoolRoot.ActionPanelOpen";
        private const double AUTO_REFRESH_INTERVAL = 0.5d;
        private const int MAX_OBJECT_ROWS = 80;

        private readonly List<GameObjectPoolDebugInfo> m_poolInfos = new List<GameObjectPoolDebugInfo>();
        private readonly Dictionary<string, bool> m_poolFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> m_objectFoldouts = new Dictionary<string, bool>();

        private bool m_autoRefresh = true;
        private bool m_showRuntimeInfo = true;
        private bool m_showPoolList = true;
        private bool m_showActions = true;
        private double m_nextRefreshTime;
        private string m_errorMessage;
        private int m_runtimeInfoRowIndex;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            serializedObject.Update();

            DrawInspectorHeader();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("GameObjectPoolModule 只在 Play Mode 中可查看。", MessageType.Info);
                DrawActions();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            if (m_autoRefresh && EditorApplication.timeSinceStartup >= m_nextRefreshTime)
            {
                Refresh();
            }

            if (!string.IsNullOrEmpty(m_errorMessage))
            {
                EditorGUILayout.HelpBox(m_errorMessage, MessageType.Warning);
                return;
            }

            DrawRuntimeInfo();
            DrawPoolInfos();
            DrawActions();

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                SavePanelStates();
            }
        }

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying && m_autoRefresh;
        }

        private void DrawInspectorHeader()
        {
            GUILayout.Space(5);

            var titleStyle = new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            var icon = EditorGUIUtility.IconContent("Prefab Icon").image;
            EditorGUILayout.LabelField(new GUIContent(" GameObject 对象池", icon, "GameObject Pool Module"),
                titleStyle);

            var subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.LabelField("运行时对象池状态与调试操作", subtitleStyle);
            EditorGUILayout.Space(6);
        }

        private void DrawActions()
        {
            UnityEditorUtil.LayoutFoldoutBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    m_autoRefresh = EditorGUILayout.ToggleLeft(
                        new GUIContent("自动刷新", "选中对象池根节点时自动刷新 Inspector 信息"),
                        m_autoRefresh);

                    EditorGUILayout.Space(3);

                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("刷新", GUILayout.Height(24)))
                        {
                            Refresh();
                        }

                        if (GUILayout.Button("打开独立窗口", GUILayout.Height(24)))
                        {
                            GameObjectPoolModuleDebuggerWindow.Open();
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    using (new EditorGUI.DisabledScope(!Application.isPlaying || GetModule() == null))
                    {
                        if (GUILayout.Button("销毁非常驻对象池", GUILayout.Height(24))
                            && EditorUtility.DisplayDialog("销毁非常驻对象池", "确认销毁所有非 DontDestroy 对象池？", "确认", "取消"))
                        {
                            GetModule()?.DestroyAllPool(false);
                            Refresh();
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }, "调试操作", ref m_showActions, true);

            EditorGUILayout.Space(5);
        }

        private void DrawRuntimeInfo()
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

            UnityEditorUtil.LayoutFoldoutBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    m_runtimeInfoRowIndex = 0;
                    DrawSummaryRow("对象池数量", m_poolInfos.Count.ToString(), m_runtimeInfoRowIndex++);
                    DrawSummaryRow("对象总数", totalObjects.ToString(), m_runtimeInfoRowIndex++);
                    DrawSummaryRow("使用中对象", spawnedObjects.ToString(), m_runtimeInfoRowIndex++);
                    DrawSummaryRow("空闲对象", idleObjects.ToString(), m_runtimeInfoRowIndex++);
                    DrawSummaryRow("标记销毁池", markedPools.ToString(), m_runtimeInfoRowIndex++);

                    EditorGUILayout.Space(3);
                    EditorGUILayout.HelpBox("实时监控 GameObjectPoolModule 运行状态", MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }, "运行时信息", ref m_showRuntimeInfo, true);

            EditorGUILayout.Space(5);
        }

        private void DrawPoolInfos()
        {
            UnityEditorUtil.LayoutFoldoutBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    if (m_poolInfos.Count <= 0)
                    {
                        EditorGUILayout.HelpBox("暂无 GameObjectPool。", MessageType.Info);
                        EditorGUILayout.EndVertical();
                        return;
                    }

                    for (var i = 0; i < m_poolInfos.Count; i++)
                    {
                        DrawPoolInfo(m_poolInfos[i], i);
                    }
                }
                EditorGUILayout.EndVertical();
            }, "对象池列表", ref m_showPoolList, true);

            EditorGUILayout.Space(5);
        }

        private void DrawPoolInfo(GameObjectPoolDebugInfo info, int rowIndex)
        {
            var key = string.IsNullOrEmpty(info.Location) ? "<empty>" : info.Location;
            if (!m_poolFoldouts.ContainsKey(key))
            {
                m_poolFoldouts.Add(key, true);
            }

            var isOpen = m_poolFoldouts[key];
            UnityEditorUtil.LayoutFoldoutBox(() =>
            {
                var rowRect = EditorGUILayout.BeginVertical("HelpBox");
                DrawZebraStriping(rowRect, rowIndex);
                {
                    DrawSummaryRow("总数/使用/空闲", $"{info.Count} / {info.SpawnedCount} / {info.NoSpawnCount}", 0);
                    DrawSummaryRow("最大容量", GetCapacityText(info.MaxCapacity), 1);
                    DrawSummaryRow("自动销毁/空闲", $"{info.AutoDestroyTime:F1}s / {info.IdleTime:F1}s", 2);
                    DrawStateRows(info);

                    EditorGUILayout.Space(3);

                    if (GUILayout.Button("销毁对象池", GUILayout.Height(22))
                        && EditorUtility.DisplayDialog("销毁对象池", $"确认销毁对象池：{info.Location}？", "确认", "取消"))
                    {
                        GetModule()?.DestroyPool(info.Location);
                        Refresh();
                    }

                    DrawObjectInfos(key, info);
                }
                EditorGUILayout.EndVertical();
            }, key, ref isOpen, true);
            m_poolFoldouts[key] = isOpen;
            EditorGUILayout.Space(3);
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
                        EditorGUILayout.HelpBox($"Inspector 仅显示前 {MAX_OBJECT_ROWS} 个对象，完整列表请打开独立窗口。",
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
                    EditorGUILayout.LabelField(info.Spawned ? "使用中" : "空闲", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.ObjectField("父节点", info.Parent, typeof(Transform), true);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSummaryRow(string label, string value, int rowIndex)
        {
            var rowRect = EditorGUILayout.BeginHorizontal();
            DrawZebraStriping(rowRect, rowIndex);
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(100));
                EditorGUILayout.SelectableLabel(value, EditorStyles.miniLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
            EditorGUILayout.EndHorizontal();
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

        private void DrawStateRows(GameObjectPoolDebugInfo info)
        {
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            {
                DrawStateField("常驻", info.DontDestroy);
                DrawStateField("标记销毁", info.MarkedForDestroy);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            {
                DrawStateField("已销毁", info.IsDestroyed);
                DrawStateField("可自动销毁", info.CanAutoDestroy);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStateField(string label, bool value)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(120));
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(72));
                EditorGUILayout.LabelField(value ? "是" : "否", EditorStyles.miniLabel, GUILayout.Width(32));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void SavePanelStates()
        {
            EditorPrefs.SetBool(RUNTIME_INFO_PANEL_KEY, m_showRuntimeInfo);
            EditorPrefs.SetBool(POOL_LIST_PANEL_KEY, m_showPoolList);
            EditorPrefs.SetBool(ACTION_PANEL_KEY, m_showActions);
        }

        private void LoadPanelStates()
        {
            m_showRuntimeInfo = EditorPrefs.GetBool(RUNTIME_INFO_PANEL_KEY, m_showRuntimeInfo);
            m_showPoolList = EditorPrefs.GetBool(POOL_LIST_PANEL_KEY, m_showPoolList);
            m_showActions = EditorPrefs.GetBool(ACTION_PANEL_KEY, m_showActions);
        }

        private void Refresh()
        {
            m_nextRefreshTime = EditorApplication.timeSinceStartup + AUTO_REFRESH_INTERVAL;
            m_poolInfos.Clear();
            m_errorMessage = null;

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

        private static string GetCapacityText(int capacity)
        {
            return capacity == int.MaxValue ? "未限制" : capacity.ToString();
        }

        private static IGameObjectPoolModule GetModule()
        {
            return ModuleSystem.GetModule<IGameObjectPoolModule>();
        }

        private void OnEnable()
        {
            LoadPanelStates();
            Refresh();
        }
    }
}
