using System;
using System.Collections.Generic;
using UnityEngine;

namespace DGame
{
    public partial class DebuggerDriver
    {
        private class GameObjectPoolInformationWindow : ScrollableDebuggerWindowBase
        {
            private const float REFRESH_INTERVAL = 0.5f;
            private const int MAX_OBJECT_ROWS = 200;

            private readonly List<GameObjectPoolDebugInfo> m_poolInfos = new List<GameObjectPoolDebugInfo>();
            private readonly Dictionary<string, bool> m_poolFoldouts = new Dictionary<string, bool>();
            private IGameObjectPoolModule m_gameObjectPool;
            private string m_searchText = string.Empty;
            private string m_errorMessage;
            private float m_refreshTimer;

            public override void Initialize(params object[] args)
            {
                m_gameObjectPool = ModuleSystem.GetModule<IGameObjectPoolModule>();
                if (m_gameObjectPool == null)
                {
                    m_errorMessage = "GameObjectPoolModule is invalid.";
                    return;
                }

                RefreshDebugInfos();
            }

            public override void OnEnter()
            {
                base.OnEnter();
                RefreshDebugInfos();
            }

            public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
            {
                if (m_gameObjectPool == null)
                {
                    return;
                }

                m_refreshTimer += realElapseSeconds;
                if (m_refreshTimer < REFRESH_INTERVAL)
                {
                    return;
                }

                m_refreshTimer = 0f;
                RefreshDebugInfos();
            }

            protected override void OnDrawScrollableWindow()
            {
                if (m_gameObjectPool == null)
                {
                    DrawSectionTitle("GameObject Pool");
                    BeginPanel();
                    DrawErrorMessage(m_errorMessage ?? "GameObjectPoolModule is invalid.");
                    EndPanel();
                    return;
                }

                if (!string.IsNullOrEmpty(m_errorMessage))
                {
                    DrawSectionTitle("GameObject Pool");
                    BeginPanel();
                    DrawErrorMessage(m_errorMessage);
                    EndPanel();
                    return;
                }

                DrawSummary();
                DrawControls();
                DrawPoolInfos();
            }

            private void DrawSummary()
            {
                int totalObjects = 0;
                int spawnedObjects = 0;
                int idleObjects = 0;
                int markedPools = 0;

                for (int i = 0; i < m_poolInfos.Count; i++)
                {
                    GameObjectPoolDebugInfo info = m_poolInfos[i];
                    totalObjects += info.Count;
                    spawnedObjects += info.SpawnedCount;
                    idleObjects += info.NoSpawnCount;
                    if (info.MarkedForDestroy)
                    {
                        markedPools++;
                    }
                }

                DrawSectionTitle("GameObject Pool Overview");
                BeginPanel();
                ResetRowIndex();
                DrawItemColored("Pools", m_poolInfos.Count.ToString(), m_poolInfos.Count > 0 ? DebuggerStyles.PrimaryColor : DebuggerStyles.TextColor);
                DrawItemColored("Total Objects", totalObjects.ToString(), totalObjects > 0 ? DebuggerStyles.PrimaryColor : DebuggerStyles.TextColor);
                DrawItemColored("Spawned Objects", spawnedObjects.ToString(), spawnedObjects > 0 ? DebuggerStyles.SuccessColor : DebuggerStyles.TextColor);
                DrawItemColored("Idle Objects", idleObjects.ToString(), idleObjects > 0 ? DebuggerStyles.WarningColor : DebuggerStyles.TextColor);
                DrawItemColored("Marked Pools", markedPools.ToString(), markedPools > 0 ? DebuggerStyles.ErrorColor : DebuggerStyles.TextColor);
                EndPanel();
            }

            private void DrawControls()
            {
                DrawSectionTitle("Controls");
                BeginPanel();
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Search", DebuggerStyles.LabelStyle, GUILayout.Width(80f));
                    m_searchText = GUILayout.TextField(m_searchText ?? string.Empty, GUI.skin.textField, GUILayout.MinWidth(160f));

                    if (GUILayout.Button("Clear", DebuggerStyles.ButtonStyle, GUILayout.Width(72f), GUILayout.Height(DebuggerStyles.SmallButtonHeight)))
                    {
                        m_searchText = string.Empty;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("Destroy Non Persistent", DebuggerStyles.ButtonStyle, GUILayout.Height(DebuggerStyles.SmallButtonHeight)))
                    {
                        m_gameObjectPool.DestroyAllPool(false);
                        ForceRefreshDebugInfos();
                    }

                    if (GUILayout.Button("Destroy All", DebuggerStyles.ButtonStyle, GUILayout.Height(DebuggerStyles.SmallButtonHeight)))
                    {
                        m_gameObjectPool.DestroyAllPool(true);
                        ForceRefreshDebugInfos();
                    }
                }
                GUILayout.EndHorizontal();
                EndPanel();
            }

            private void DrawPoolInfos()
            {
                DrawSectionTitle("Pool Details");

                if (m_poolInfos.Count <= 0)
                {
                    BeginPanel();
                    DrawInfoMessage("No GameObject pools created yet...");
                    EndPanel();
                    return;
                }

                int visibleIndex = 0;
                for (int i = 0; i < m_poolInfos.Count; i++)
                {
                    GameObjectPoolDebugInfo info = m_poolInfos[i];
                    if (!MatchSearch(info))
                    {
                        continue;
                    }

                    DrawPoolInfo(info, visibleIndex++);
                }

                if (visibleIndex <= 0)
                {
                    BeginPanel();
                    DrawInfoMessage("No GameObject pools match the search text...");
                    EndPanel();
                }
            }

            private void DrawPoolInfo(GameObjectPoolDebugInfo info, int index)
            {
                string key = string.IsNullOrEmpty(info.Location) ? "<empty>" : info.Location;
                if (!m_poolFoldouts.ContainsKey(key))
                {
                    m_poolFoldouts.Add(key, true);
                }

                DrawSectionTitle(Utility.StringUtil.Format("Pool #{0}: {1}", index + 1, key));
                BeginPanel();
                {
                    GUILayout.BeginHorizontal();
                    {
                        string foldoutText = m_poolFoldouts[key] ? "Hide Objects" : "Show Objects";
                        if (GUILayout.Button(foldoutText, DebuggerStyles.ButtonStyle, GUILayout.Width(120f), GUILayout.Height(DebuggerStyles.SmallButtonHeight)))
                        {
                            m_poolFoldouts[key] = !m_poolFoldouts[key];
                        }

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("Destroy", DebuggerStyles.ButtonStyle, GUILayout.Width(92f), GUILayout.Height(DebuggerStyles.SmallButtonHeight)))
                        {
                            m_gameObjectPool.DestroyPool(info.Location);
                            ForceRefreshDebugInfos();
                        }
                    }
                    GUILayout.EndHorizontal();

                    ResetRowIndex();
                    DrawItem("Location", key, "GameObjectPool location");
                    DrawUsage(info);
                    DrawItem("Idle Objects", info.NoSpawnCount.ToString(), "Objects waiting in pool");
                    DrawItem("Max Capacity", GetCapacityText(info.MaxCapacity), "Maximum pool capacity");
                    DrawItem("Auto Destroy Time", Utility.StringUtil.Format("{0:F1} s", info.AutoDestroyTime), "Auto destroy threshold");
                    DrawItem("Idle Time", Utility.StringUtil.Format("{0:F1} s", info.IdleTime), "Current idle duration");

                    GUILayout.Space(4);
                    DrawState("Dont Destroy", info.DontDestroy);
                    DrawState("Marked For Destroy", info.MarkedForDestroy);
                    DrawState("Is Destroyed", info.IsDestroyed);
                    DrawState("Can Auto Destroy", info.CanAutoDestroy);
                }
                EndPanel();

                if (m_poolFoldouts[key])
                {
                    DrawObjectInfos(info);
                }
            }

            private void DrawUsage(GameObjectPoolDebugInfo info)
            {
                float usagePercent = info.Count > 0 ? Mathf.Clamp01((float)info.SpawnedCount / info.Count) : 0f;
                Color32 usageColor = usagePercent > 0.8f ? DebuggerStyles.WarningColor :
                    usagePercent > 0.5f ? DebuggerStyles.PrimaryColor : DebuggerStyles.SuccessColor;
                string usageText = Utility.StringUtil.Format("{0} / {1} ({2:P0})", info.SpawnedCount, info.Count, usagePercent);
                DrawItemColored("Usage", usageText, usageColor);
            }

            private void DrawState(string title, bool active)
            {
                DrawItemColored(title, active.ToString(), active ? DebuggerStyles.SuccessColor : DebuggerStyles.TextSecondaryColor);
            }

            private void DrawObjectInfos(GameObjectPoolDebugInfo info)
            {
                BeginPanel();
                {
                    if (info.Objects.Count <= 0)
                    {
                        DrawInfoMessage("No objects in this pool...");
                    }
                    else
                    {
                        DrawTableHeader(
                            ("Name", 0),
                            ("State", 70f),
                            ("Active", 60f),
                            ("Parent", 120f),
                            ("Pool Key", 120f)
                        );

                        int count = Mathf.Min(info.Objects.Count, MAX_OBJECT_ROWS);
                        for (int i = 0; i < count; i++)
                        {
                            DrawObjectInfo(info.Objects[i], i % 2 == 1);
                        }

                        if (info.Objects.Count > MAX_OBJECT_ROWS)
                        {
                            DrawInfoMessage(Utility.StringUtil.Format("Only first {0} objects are shown. Total: {1}.", MAX_OBJECT_ROWS, info.Objects.Count));
                        }
                    }
                }
                EndPanel();
            }

            private void DrawObjectInfo(GameObjectPoolObjectDebugInfo info, bool isAlt)
            {
                string objectName = string.IsNullOrEmpty(info.Name) ? "(unnamed)" : info.Name;
                string stateText = info.Spawned ? "Spawned" : "Idle";
                string activeText = info.ActiveSelf ? "True" : "False";
                string parentName = info.Parent == null ? "<none>" : info.Parent.name;
                string poolKey = string.IsNullOrEmpty(info.PoolKey) ? "<empty>" : info.PoolKey;

                if (info.Spawned)
                {
                    GUILayout.BeginHorizontal(DebuggerStyles.PrimaryBoxStyle);
                    {
                        GUILayout.Label(DebuggerStyles.ColorText(objectName, DebuggerStyles.PrimaryColor), DebuggerStyles.RichLabelStyle);
                        GUILayout.Label(DebuggerStyles.ColorText(stateText, DebuggerStyles.PrimaryColor), DebuggerStyles.RichLabelStyle, GUILayout.Width(70f));
                        GUILayout.Label(DebuggerStyles.ColorText(activeText, DebuggerStyles.PrimaryColor), DebuggerStyles.RichLabelStyle, GUILayout.Width(60f));
                        GUILayout.Label(DebuggerStyles.ColorText(parentName, DebuggerStyles.PrimaryColor), DebuggerStyles.RichLabelStyle, GUILayout.Width(120f));
                        GUILayout.Label(DebuggerStyles.ColorText(poolKey, DebuggerStyles.PrimaryColor), DebuggerStyles.RichLabelStyle, GUILayout.Width(120f));
                    }
                    GUILayout.EndHorizontal();
                    return;
                }

                DrawTableRow(isAlt,
                    (objectName, 0),
                    (stateText, 70f),
                    (activeText, 60f),
                    (parentName, 120f),
                    (poolKey, 120f)
                );
            }

            private void ForceRefreshDebugInfos()
            {
                m_refreshTimer = 0f;
                RefreshDebugInfos();
            }

            private void RefreshDebugInfos()
            {
                if (m_gameObjectPool == null)
                {
                    return;
                }

                m_poolInfos.Clear();
                m_errorMessage = null;

                try
                {
                    m_gameObjectPool.GetDebugInfos(m_poolInfos);
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

            private static string GetCapacityText(int capacity)
            {
                return capacity == int.MaxValue ? "Unlimited" : capacity.ToString();
            }
        }
    }
}