#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// 红点树可视化编辑器窗口
    /// </summary>
    public class RedDotTreeEditorWindow : EditorWindow
    {
        private const string OVERVIEW_PANEL_KEY = "RedDotTreeEditor.OverviewPanelOpen";
        private const string TREE_PANEL_KEY = "RedDotTreeEditor.TreePanelOpen";
        private const string DETAIL_PANEL_KEY = "RedDotTreeEditor.DetailPanelOpen";
        private const string GENERATE_PANEL_KEY = "RedDotTreeEditor.GeneratePanelOpen";
        private const float MIN_LEFT_PANEL_WIDTH = 360f;
        private const float NODE_ROW_HEIGHT = 38f;
        private const float NODE_INDENT_WIDTH = 18f;
        private const float NODE_ACTION_AREA_WIDTH = 78f;

        private RedDotTreeConfig m_config;
        private Vector2 m_treeScrollPosition;
        private Vector2 m_detailScrollPosition;
        private RedDotNodeConfig m_selectedNode;
        private RedDotNodeConfig m_draggedNode;
        private List<RedDotNodeConfig> m_draggedNodeList;
        private bool m_isDragging;
        private RedDotNodeConfig m_dropTargetNode;
        private DropPosition m_dropPosition;
        private string m_searchText = string.Empty;

        private bool m_showOverview = true;
        private bool m_showTree = true;
        private bool m_showDetail = true;
        private bool m_showGenerate = true;
        private int m_visibleNodeRowIndex;

        private enum DropPosition
        {
            None,
            Above,
            Below,
            AsChild
        }

        private GUIStyle m_titleStyle;
        private GUIStyle m_subtitleStyle;
        private GUIStyle m_nodeStyle;
        private GUIStyle m_selectedNodeStyle;
        private GUIStyle m_pathLabelStyle;
        private GUIStyle m_statLabelStyle;

        private readonly Color m_selectedColor = new Color(0.3f, 0.6f, 0.9f, 0.22f);
        private readonly Color m_dragTargetColor = new Color(0.9f, 0.6f, 0.3f, 0.35f);
        private readonly Color m_duplicateNameColor = new Color(0.9f, 0.3f, 0.3f, 0.24f);

        private readonly HashSet<string> m_duplicatePaths = new HashSet<string>();

        [MenuItem("DGame Tools/RedDot/Tree Editor")]
        public static void ShowWindow()
        {
            var window = GetWindow<RedDotTreeEditorWindow>();
            window.titleContent = new GUIContent("红点树编辑器");
            window.minSize = new Vector2(760f, 460f);
            window.Show();
        }

        /// <summary>
        /// 双击 RedDotTreeConfig 资源时打开编辑器窗口
        /// </summary>
        [UnityEditor.Callbacks.OnOpenAsset(1)]
#if UNITY_6000_1_OR_NEWER
        public static bool OnOpenAsset(EntityId entityId, int line)
        {
            return OnOpenAsset(EditorUtility.EntityIdToObject(entityId) as RedDotTreeConfig);
        }
#else
        public static bool OnOpenAsset(int instanceID, int line)
        {
            return OnOpenAsset(EditorUtility.InstanceIDToObject(instanceID) as RedDotTreeConfig);
        }
#endif

        private static bool OnOpenAsset(RedDotTreeConfig asset)
        {
            if (asset == null)
            {
                return false;
            }

            var window = GetWindow<RedDotTreeEditorWindow>();
            window.titleContent = new GUIContent("红点树编辑器");
            window.minSize = new Vector2(760f, 460f);
            window.SetConfig(asset);
            window.Show();
            return true;
        }

        /// <summary>
        /// 设置要编辑的配置
        /// </summary>
        public void SetConfig(RedDotTreeConfig config)
        {
            m_config = config;
            if (m_config != null)
            {
                m_config.RefreshPaths();
            }

            Repaint();
        }

        private void OnEnable()
        {
            LoadPanelStates();
            LoadConfig();
        }

        private void OnDisable()
        {
            SavePanelStates();
            if (m_config != null)
            {
                RedDotTreeCodeGenerator.GenerateCode(m_config, false);
            }
        }

        private void LoadConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:RedDotTreeConfig");
            if (guids.Length <= 0)
            {
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            m_config = AssetDatabase.LoadAssetAtPath<RedDotTreeConfig>(path);
            m_config?.RefreshPaths();
        }

        private void InitStyles()
        {
            m_titleStyle ??= new GUIStyle(EditorStyles.largeLabel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            m_subtitleStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };

            m_nodeStyle ??= new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(4, 4, 0, 0),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };

            m_selectedNodeStyle ??= new GUIStyle(m_nodeStyle)
            {
                fontStyle = FontStyle.Bold
            };

            m_pathLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Italic,
                normal = { textColor = Color.gray },
                clipping = TextClipping.Clip
            };

            m_statLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
        }

        private void OnGUI()
        {
            InitStyles();
            DrawHeader();
            DrawToolbar();

            if (m_config == null)
            {
                DrawNoConfigMessage();
                return;
            }

            m_config.RefreshPaths();
            RefreshDuplicatePaths();
            DrawOverviewPanel();

            bool useVerticalLayout = position.width < 780f;
            if (useVerticalLayout)
            {
                DrawTreePanel();
                DrawSidePanels();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Max(MIN_LEFT_PANEL_WIDTH, position.width * 0.56f)));
                    DrawTreePanel();
                    EditorGUILayout.EndVertical();

                    GUILayout.Space(4f);

                    EditorGUILayout.BeginVertical();
                    DrawSidePanels();
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndHorizontal();
            }

            HandleDragAndDrop();

            if (GUI.changed)
            {
                SavePanelStates();
            }
        }

        private void DrawHeader()
        {
            GUILayout.Space(5f);
            var icon = EditorGUIUtility.IconContent("d_Favorite Icon").image;
            EditorGUILayout.LabelField(new GUIContent(" 红点树编辑器", icon, "Red Dot Tree Editor"), m_titleStyle);
            EditorGUILayout.LabelField("维护 RedDotPathDefine 的源配置，生成后供 RedDotModule 注册使用", m_subtitleStyle);
            EditorGUILayout.Space(6f);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                EditorGUILayout.LabelField("配置", GUILayout.Width(34f));

                EditorGUI.BeginChangeCheck();
                m_config = (RedDotTreeConfig)EditorGUILayout.ObjectField(m_config, typeof(RedDotTreeConfig), false,
                    GUILayout.MinWidth(220f));
                if (EditorGUI.EndChangeCheck())
                {
                    m_config?.RefreshPaths();
                    m_selectedNode = null;
                }

                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                {
                    CreateNewConfig();
                }

                using (new EditorGUI.DisabledScope(m_config == null))
                {
                    if (GUILayout.Button("定位", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                    {
                        Selection.activeObject = m_config;
                        EditorGUIUtility.PingObject(m_config);
                    }

                    if (GUILayout.Button("展开全部", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    {
                        SetAllFoldout(true);
                    }

                    if (GUILayout.Button("折叠全部", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    {
                        SetAllFoldout(false);
                    }
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(m_config == null))
                {
                    GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
                    if (GUILayout.Button("生成代码", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                    {
                        GenerateCode();
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawNoConfigMessage()
        {
            EditorGUILayout.Space(20f);
            EditorGUILayout.HelpBox("请选择或创建 RedDotTreeConfig 资源。", MessageType.Info);
            EditorGUILayout.Space(8f);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("创建红点树配置", GUILayout.Width(160f), GUILayout.Height(28f)))
            {
                CreateNewConfig();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void CreateNewConfig()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "创建红点树配置",
                "RedDotTreeConfig",
                "asset",
                "选择 RedDotTreeConfig 保存位置");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            m_config = ScriptableObject.CreateInstance<RedDotTreeConfig>();
            AssetDatabase.CreateAsset(m_config, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = m_config;
        }

        private void DrawOverviewPanel()
        {
            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    int nodeCount = GetTotalNodeCount();
                    int leafCount = GetLeafNodeCount();
                    int maxDepth = GetMaxDepth();

                    EditorGUILayout.BeginHorizontal();
                    DrawStatItem("根节点", m_config.RootNodes.Count.ToString());
                    DrawStatItem("总节点", nodeCount.ToString());
                    DrawStatItem("叶子节点", leafCount.ToString());
                    DrawStatItem("最大深度", maxDepth.ToString());
                    DrawStatItem("重复路径", m_duplicatePaths.Count.ToString());
                    EditorGUILayout.EndHorizontal();

                    if (m_duplicatePaths.Count > 0)
                    {
                        EditorGUILayout.HelpBox($"存在重复节点路径：{string.Join("，", m_duplicatePaths)}", MessageType.Warning);
                    }
                }
                EditorGUILayout.EndVertical();
            }, "配置概览", ref m_showOverview, true);

            EditorGUILayout.Space(5f);
        }

        private void DrawTreePanel()
        {
            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    DrawTreeHeader();

                    if (m_duplicatePaths.Count > 0)
                    {
                        EditorGUILayout.HelpBox("重复路径会导致生成的 RedDotPathDefine 冲突，请先调整节点名称。", MessageType.Warning);
                    }

                    if (m_config.RootNodes.Count <= 0)
                    {
                        EditorGUILayout.HelpBox("当前配置还没有根节点。", MessageType.Info);
                    }

                    m_treeScrollPosition = EditorGUILayout.BeginScrollView(m_treeScrollPosition);
                    {
                        m_visibleNodeRowIndex = 0;
                        for (int i = 0; i < m_config.RootNodes.Count; i++)
                        {
                            DrawNode(m_config.RootNodes[i], 0, m_config.RootNodes, i);
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
                EditorGUILayout.EndVertical();
            }, "红点树", ref m_showTree, true);
        }

        private void DrawTreeHeader()
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("搜索", GUILayout.Width(34f));
                m_searchText = EditorGUILayout.TextField(m_searchText, GUILayout.MinWidth(140f));

                if (!string.IsNullOrEmpty(m_searchText) && GUILayout.Button("清空", GUILayout.Width(52f)))
                {
                    m_searchText = string.Empty;
                    GUI.FocusControl(null);
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("+ 根节点", GUILayout.Width(82f), GUILayout.Height(22f)))
                {
                    AddRootNode();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        private void DrawSidePanels()
        {
            m_detailScrollPosition = EditorGUILayout.BeginScrollView(m_detailScrollPosition);
            DrawNodeInspector();
            EditorGUILayout.Space(5f);
            DrawGeneratePanel();
            EditorGUILayout.EndScrollView();
        }

        private void DrawNodeInspector()
        {
            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    if (m_selectedNode == null)
                    {
                        EditorGUILayout.HelpBox("选择左侧节点后，可在这里编辑名称、类型和聚合策略。", MessageType.Info);
                    }
                    else
                    {
                        bool isDuplicatePath = m_duplicatePaths.Contains(m_selectedNode.generatedPath);
                        if (isDuplicatePath)
                        {
                            EditorGUILayout.HelpBox("当前节点生成路径重复，请修改节点名称或层级。", MessageType.Warning);
                        }

                        EditorGUI.BeginChangeCheck();
                        DrawNodePropertyFields();
                        if (EditorGUI.EndChangeCheck())
                        {
                            m_config.RefreshPaths();
                            MarkDirty();
                        }

                        DrawSelectedNodeStats();
                        DrawQuickActions();
                    }
                }
                EditorGUILayout.EndVertical();
            }, "节点详情", ref m_showDetail, true);
        }

        private void DrawNodePropertyFields()
        {
            EditorGUILayout.LabelField("节点名称", EditorStyles.boldLabel);
            m_selectedNode.name = EditorGUILayout.TextField(m_selectedNode.name);

            if (string.IsNullOrWhiteSpace(m_selectedNode.name))
            {
                EditorGUILayout.HelpBox("节点名称为空时会生成 Unknown，建议填写明确的英文名称。", MessageType.Warning);
            }

            EditorGUILayout.Space(5f);

            EditorGUILayout.LabelField("生成路径", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(m_selectedNode.generatedPath);
            }

            EditorGUILayout.Space(5f);

            EditorGUILayout.LabelField("描述", EditorStyles.boldLabel);
            var textAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            m_selectedNode.description = EditorGUILayout.TextArea(m_selectedNode.description, textAreaStyle,
                GUILayout.MinHeight(44f));

            EditorGUILayout.Space(5f);

            m_selectedNode.type = (RedDotType)EditorGUILayout.EnumPopup(new GUIContent("红点类型", "RedDotNode 的显示语义"),
                m_selectedNode.type);
            m_selectedNode.strategy = (RedDotAggregateStrategy)EditorGUILayout.EnumPopup(
                new GUIContent("聚合策略", "父节点聚合子节点值的方式"), m_selectedNode.strategy);
        }

        private void DrawSelectedNodeStats()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                DrawInfoRow("生成 ID", m_selectedNode.generatedId <= 0 ? "生成代码时分配" : m_selectedNode.generatedId.ToString());
                DrawInfoRow("子节点", m_selectedNode.children.Count.ToString());
                DrawInfoRow("叶子节点", m_selectedNode.children.Count <= 0 ? "是" : "否");
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawQuickActions()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("快捷操作", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("添加子节点", GUILayout.Height(24f)))
                {
                    AddChildNode(m_selectedNode);
                }

                if (GUILayout.Button("复制节点", GUILayout.Height(24f)))
                {
                    DuplicateNode(m_selectedNode);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("复制生成路径", GUILayout.Height(24f)))
            {
                EditorGUIUtility.systemCopyBuffer = m_selectedNode.generatedPath;
                Debug.Log($"[RedDot] 已复制路径: {m_selectedNode.generatedPath}");
            }
        }

        private void DrawGeneratePanel()
        {
            UnityEditorUtil.LayoutFrameBox(() =>
            {
                EditorGUILayout.BeginVertical("HelpBox");
                {
                    DrawInfoRow("命名空间", m_config.Namespace);
                    DrawInfoRow("类名", m_config.ClassName);
                    DrawInfoRow("输出路径", $"Assets/{m_config.OutputPath}");

                    EditorGUILayout.Space(5f);

                    MessageType messageType = m_duplicatePaths.Count > 0 ? MessageType.Warning : MessageType.Info;
                    string status = m_duplicatePaths.Count > 0
                        ? "存在重复路径，建议修复后再生成代码。"
                        : "配置检查通过，可生成 RedDotPathDefine。";
                    EditorGUILayout.HelpBox(status, messageType);

                    using (new EditorGUI.DisabledScope(m_config == null))
                    {
                        if (GUILayout.Button("生成 RedDotPathDefine", GUILayout.Height(26f)))
                        {
                            GenerateCode();
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }, "校验与生成", ref m_showGenerate, true);
        }

        /// <summary>
        /// 检测并刷新重复路径列表
        /// </summary>
        private void RefreshDuplicatePaths()
        {
            m_duplicatePaths.Clear();
            var pathCount = new Dictionary<string, int>();

            foreach (var root in m_config.RootNodes)
            {
                CollectNodePaths(root, pathCount);
            }

            foreach (var kvp in pathCount)
            {
                if (kvp.Value > 1)
                {
                    m_duplicatePaths.Add(kvp.Key);
                }
            }
        }

        private void CollectNodePaths(RedDotNodeConfig node, Dictionary<string, int> pathCount)
        {
            if (node == null)
            {
                return;
            }

            string path = node.generatedPath;
            if (!string.IsNullOrEmpty(path))
            {
                if (pathCount.ContainsKey(path))
                {
                    pathCount[path]++;
                }
                else
                {
                    pathCount[path] = 1;
                }
            }

            foreach (var child in node.children)
            {
                CollectNodePaths(child, pathCount);
            }
        }

        private void DrawNode(RedDotNodeConfig node, int depth, List<RedDotNodeConfig> parentList, int index)
        {
            if (node == null || !ShouldDrawNode(node))
            {
                return;
            }

            if (m_isDragging && m_dropTargetNode == node && m_dropPosition == DropPosition.Above)
            {
                DrawDropLine();
            }

            Rect rowRect = GUILayoutUtility.GetRect(0f, NODE_ROW_HEIGHT, GUILayout.ExpandWidth(true));
            bool isDuplicatePath = m_duplicatePaths.Contains(node.generatedPath);
            int rowIndex = m_visibleNodeRowIndex++;
            DrawNodeBackground(rowRect, node, isDuplicatePath, rowIndex);
            DrawNodeRow(rowRect, node, depth, parentList, index, isDuplicatePath);

            if (m_isDragging && m_dropTargetNode == node && m_dropPosition == DropPosition.Below)
            {
                DrawDropLine();
            }

            if ((node.foldout || !string.IsNullOrEmpty(m_searchText)) && node.children.Count > 0)
            {
                for (int i = 0; i < node.children.Count; i++)
                {
                    DrawNode(node.children[i], depth + 1, node.children, i);
                }
            }
        }

        private void DrawNodeBackground(Rect rowRect, RedDotNodeConfig node, bool isDuplicatePath, int rowIndex)
        {
            DrawZebraStriping(rowRect, rowIndex);

            if (m_isDragging && m_dropTargetNode == node && m_dropPosition == DropPosition.AsChild)
            {
                EditorGUI.DrawRect(rowRect, m_dragTargetColor);
            }
            else if (isDuplicatePath)
            {
                EditorGUI.DrawRect(rowRect, m_duplicateNameColor);
            }
            else if (m_selectedNode == node)
            {
                EditorGUI.DrawRect(rowRect, m_selectedColor);
            }
        }

        private void DrawNodeRow(Rect rowRect, RedDotNodeConfig node, int depth, List<RedDotNodeConfig> parentList,
            int index, bool isDuplicatePath)
        {
            float actionX = rowRect.xMax - NODE_ACTION_AREA_WIDTH;
            float contentX = rowRect.x + depth * NODE_INDENT_WIDTH + 4f;

            Rect foldoutRect = new Rect(contentX, rowRect.y + 10f, 16f, 18f);
            DrawNodeFoldout(node, foldoutRect);
            contentX += 18f;

            Rect iconRect = new Rect(contentX, rowRect.y + 10f, 18f, 18f);
            DrawNodeIcon(iconRect, isDuplicatePath);
            contentX += 22f;

            float contentWidth = Mathf.Max(24f, actionX - contentX - 6f);
            Rect contentRect = new Rect(contentX, rowRect.y + 3f, contentWidth, rowRect.height - 6f);
            DrawNodeMainContent(contentRect, node, isDuplicatePath);

            DrawNodeActions(rowRect, actionX, node, parentList, index);
        }

        private void DrawNodeFoldout(RedDotNodeConfig node, Rect foldoutRect)
        {
            if (node.children.Count > 0)
            {
                node.foldout = EditorGUI.Foldout(foldoutRect, node.foldout, string.Empty, true);
            }
        }

        private static void DrawZebraStriping(Rect rowRect, int rowIndex)
        {
            if (rowIndex % 2 == 0)
            {
                return;
            }

            float alpha = EditorGUIUtility.isProSkin ? 0.033f : 0.05f;
            Color color = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, alpha)
                : new Color(0f, 0f, 0f, alpha);
            EditorGUI.DrawRect(rowRect, color);
        }

        private static void DrawNodeIcon(Rect iconRect, bool isDuplicatePath)
        {
            GUIContent icon = isDuplicatePath
                ? EditorGUIUtility.IconContent("console.warnicon.sml")
                : EditorGUIUtility.IconContent("d_Favorite Icon");
            GUI.Label(iconRect, icon);
        }

        private void DrawNodeMainContent(Rect contentRect, RedDotNodeConfig node, bool isDuplicatePath)
        {
            Rect titleRect = new Rect(contentRect.x, contentRect.y, contentRect.width, 18f);
            Rect pathRect = new Rect(contentRect.x, contentRect.y + 18f, contentRect.width, 15f);

            string name = string.IsNullOrEmpty(node.name) ? "<未命名节点>" : node.name;
            string title = string.IsNullOrEmpty(node.description) ? name : $"{name}  {node.description}";
            string tooltip = string.IsNullOrEmpty(node.description)
                ? node.generatedPath
                : $"{node.description}\n{node.generatedPath}";
            var style = m_selectedNode == node ? m_selectedNodeStyle : m_nodeStyle;
            if (GUI.Button(titleRect, new GUIContent(title, tooltip), style))
            {
                m_selectedNode = node;
            }

            string path = string.IsNullOrEmpty(node.generatedPath) ? "<未生成路径>" : node.generatedPath;
            if (isDuplicatePath)
            {
                path = $"重复路径: {path}";
            }

            GUI.Label(pathRect, new GUIContent(path, path), m_pathLabelStyle);
        }

        private void DrawNodeActions(Rect rowRect, float actionX, RedDotNodeConfig node,
            List<RedDotNodeConfig> parentList, int index)
        {
            Rect addRect = new Rect(actionX, rowRect.y + 9f, 24f, 20f);
            Rect deleteRect = new Rect(actionX + 26f, rowRect.y + 9f, 24f, 20f);
            Rect dragRect = new Rect(actionX + 54f, rowRect.y + 9f, 20f, 20f);

            if (GUI.Button(addRect, new GUIContent("+", "添加子节点")))
            {
                AddChildNode(node);
            }

            if (GUI.Button(deleteRect, new GUIContent("×", "删除节点")))
            {
                DeleteNode(node, parentList, index);
            }

            GUI.Label(dragRect, new GUIContent("≡", "拖拽排序或移动层级"), EditorStyles.centeredGreyMiniLabel);
            HandleNodeDrag(node, parentList, index, dragRect, rowRect);
        }

        private void DrawDropLine()
        {
            Rect lineRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(2f));
            EditorGUI.DrawRect(lineRect, m_dragTargetColor);
            EditorGUILayout.EndHorizontal();
        }

        private void DeleteNode(RedDotNodeConfig node, List<RedDotNodeConfig> parentList, int index)
        {
            if (!EditorUtility.DisplayDialog("删除红点节点",
                    $"确认删除 “{node.name}” 以及它的所有子节点？", "确认", "取消"))
            {
                return;
            }

            parentList.RemoveAt(index);
            if (m_selectedNode == node)
            {
                m_selectedNode = null;
            }

            m_config.RefreshPaths();
            MarkDirty();
        }

        private void HandleNodeDrag(RedDotNodeConfig node, List<RedDotNodeConfig> parentList, int index, Rect dragRect,
            Rect rowRect)
        {
            Event e = Event.current;

            if (e.type == EventType.MouseDown && dragRect.Contains(e.mousePosition))
            {
                m_isDragging = true;
                m_draggedNode = node;
                m_draggedNodeList = parentList;
                m_dropTargetNode = null;
                m_dropPosition = DropPosition.None;
                e.Use();
            }

            if (m_isDragging && m_draggedNode != node && rowRect.Contains(e.mousePosition))
            {
                if (IsDescendantOf(node, m_draggedNode))
                {
                    m_dropTargetNode = null;
                    m_dropPosition = DropPosition.None;
                }
                else
                {
                    m_dropTargetNode = node;

                    float relativeY = (e.mousePosition.y - rowRect.y) / rowRect.height;

                    if (relativeY < 0.25f)
                    {
                        m_dropPosition = DropPosition.Above;
                    }
                    else if (relativeY > 0.75f)
                    {
                        m_dropPosition = DropPosition.Below;
                    }
                    else
                    {
                        m_dropPosition = DropPosition.AsChild;
                    }

                    Repaint();
                }
            }
        }

        /// <summary>
        /// 检查 node 是否是 potentialAncestor 的后代
        /// </summary>
        private bool IsDescendantOf(RedDotNodeConfig node, RedDotNodeConfig potentialAncestor)
        {
            foreach (var child in potentialAncestor.children)
            {
                if (child == node || IsDescendantOf(node, child))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleDragAndDrop()
        {
            Event e = Event.current;

            if (!m_isDragging)
            {
                return;
            }

            if (e.type == EventType.Repaint)
            {
                EditorGUIUtility.AddCursorRect(new Rect(0, 0, position.width, position.height), MouseCursor.MoveArrow);
            }

            if (e.type == EventType.MouseUp)
            {
                if (m_dropTargetNode != null && m_dropPosition != DropPosition.None)
                {
                    PerformNodeMove();
                }

                ResetDragState();
                Repaint();
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ResetDragState();
                e.Use();
                Repaint();
            }
        }

        private void ResetDragState()
        {
            m_isDragging = false;
            m_draggedNode = null;
            m_draggedNodeList = null;
            m_dropTargetNode = null;
            m_dropPosition = DropPosition.None;
        }

        private void PerformNodeMove()
        {
            if (m_draggedNode == null || m_dropTargetNode == null)
            {
                return;
            }

            m_draggedNodeList.Remove(m_draggedNode);

            List<RedDotNodeConfig> targetList = FindParentList(m_dropTargetNode);
            if (targetList == null)
            {
                return;
            }

            int targetIndex = targetList.IndexOf(m_dropTargetNode);

            switch (m_dropPosition)
            {
                case DropPosition.Above:
                    targetList.Insert(targetIndex, m_draggedNode);
                    break;

                case DropPosition.Below:
                    targetList.Insert(targetIndex + 1, m_draggedNode);
                    break;

                case DropPosition.AsChild:
                    m_dropTargetNode.children.Add(m_draggedNode);
                    m_dropTargetNode.foldout = true;
                    break;
            }

            m_config.RefreshPaths();
            MarkDirty();
        }

        private List<RedDotNodeConfig> FindParentList(RedDotNodeConfig node)
        {
            if (m_config.RootNodes.Contains(node))
            {
                return m_config.RootNodes;
            }

            foreach (var root in m_config.RootNodes)
            {
                var result = FindParentListRecursive(root, node);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private List<RedDotNodeConfig> FindParentListRecursive(RedDotNodeConfig parent, RedDotNodeConfig target)
        {
            if (parent.children.Contains(target))
            {
                return parent.children;
            }

            foreach (var child in parent.children)
            {
                var result = FindParentListRecursive(child, target);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void AddRootNode()
        {
            var newNode = new RedDotNodeConfig("NewRoot");
            m_config.RootNodes.Add(newNode);
            m_config.RefreshPaths();
            m_selectedNode = newNode;
            MarkDirty();
        }

        private void AddChildNode(RedDotNodeConfig parent)
        {
            var newNode = new RedDotNodeConfig("NewNode");
            parent.children.Add(newNode);
            parent.foldout = true;
            m_config.RefreshPaths();
            m_selectedNode = newNode;
            MarkDirty();
        }

        private void DuplicateNode(RedDotNodeConfig node)
        {
            var copy = new RedDotNodeConfig(node.name + "_Copy")
            {
                description = node.description,
                type = node.type,
                strategy = node.strategy
            };

            foreach (var root in m_config.RootNodes)
            {
                if (TryAddAfterNode(root, node, copy))
                {
                    m_config.RefreshPaths();
                    m_selectedNode = copy;
                    MarkDirty();
                    return;
                }
            }

            int index = m_config.RootNodes.IndexOf(node);
            if (index >= 0)
            {
                m_config.RootNodes.Insert(index + 1, copy);
                m_config.RefreshPaths();
                m_selectedNode = copy;
                MarkDirty();
            }
        }

        private bool TryAddAfterNode(RedDotNodeConfig current, RedDotNodeConfig target, RedDotNodeConfig newNode)
        {
            int index = current.children.IndexOf(target);
            if (index >= 0)
            {
                current.children.Insert(index + 1, newNode);
                return true;
            }

            foreach (var child in current.children)
            {
                if (TryAddAfterNode(child, target, newNode))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetAllFoldout(bool foldout)
        {
            foreach (var root in m_config.RootNodes)
            {
                SetNodeFoldout(root, foldout);
            }
            Repaint();
        }

        private void SetNodeFoldout(RedDotNodeConfig node, bool foldout)
        {
            node.foldout = foldout;
            foreach (var child in node.children)
            {
                SetNodeFoldout(child, foldout);
            }
        }

        private bool ShouldDrawNode(RedDotNodeConfig node)
        {
            if (string.IsNullOrEmpty(m_searchText))
            {
                return true;
            }

            return NodeMatchesSearch(node);
        }

        private bool NodeMatchesSearch(RedDotNodeConfig node)
        {
            if (ContainsSearchText(node.name)
                || ContainsSearchText(node.description)
                || ContainsSearchText(node.generatedPath))
            {
                return true;
            }

            foreach (var child in node.children)
            {
                if (NodeMatchesSearch(child))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ContainsSearchText(string value)
        {
            return !string.IsNullOrEmpty(value)
                   && value.IndexOf(m_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int GetTotalNodeCount()
        {
            int count = 0;
            foreach (var root in m_config.RootNodes)
            {
                count += GetNodeCount(root);
            }

            return count;
        }

        private int GetNodeCount(RedDotNodeConfig node)
        {
            int count = 1;
            foreach (var child in node.children)
            {
                count += GetNodeCount(child);
            }

            return count;
        }

        private int GetLeafNodeCount()
        {
            int count = 0;
            foreach (var root in m_config.RootNodes)
            {
                count += GetLeafNodeCount(root);
            }

            return count;
        }

        private int GetLeafNodeCount(RedDotNodeConfig node)
        {
            if (node.children.Count <= 0)
            {
                return 1;
            }

            int count = 0;
            foreach (var child in node.children)
            {
                count += GetLeafNodeCount(child);
            }

            return count;
        }

        private int GetMaxDepth()
        {
            int maxDepth = 0;
            foreach (var root in m_config.RootNodes)
            {
                maxDepth = Mathf.Max(maxDepth, GetNodeDepth(root, 1));
            }

            return maxDepth;
        }

        private int GetNodeDepth(RedDotNodeConfig node, int depth)
        {
            int maxDepth = depth;
            foreach (var child in node.children)
            {
                maxDepth = Mathf.Max(maxDepth, GetNodeDepth(child, depth + 1));
            }

            return maxDepth;
        }

        private void DrawStatItem(string label, string value)
        {
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(70f));
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawInfoRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(72f));
            EditorGUILayout.LabelField(value, m_statLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        private void MarkDirty()
        {
            if (m_config != null)
            {
                EditorUtility.SetDirty(m_config);
            }
        }

        private void LoadPanelStates()
        {
            m_showOverview = EditorPrefs.GetBool(OVERVIEW_PANEL_KEY, m_showOverview);
            m_showTree = EditorPrefs.GetBool(TREE_PANEL_KEY, m_showTree);
            m_showDetail = EditorPrefs.GetBool(DETAIL_PANEL_KEY, m_showDetail);
            m_showGenerate = EditorPrefs.GetBool(GENERATE_PANEL_KEY, m_showGenerate);
        }

        private void SavePanelStates()
        {
            EditorPrefs.SetBool(OVERVIEW_PANEL_KEY, m_showOverview);
            EditorPrefs.SetBool(TREE_PANEL_KEY, m_showTree);
            EditorPrefs.SetBool(DETAIL_PANEL_KEY, m_showDetail);
            EditorPrefs.SetBool(GENERATE_PANEL_KEY, m_showGenerate);
        }

        #region Code Generation

        private void GenerateCode()
        {
            RedDotTreeCodeGenerator.GenerateCode(m_config, true);
        }

        #endregion
    }
}

#endif
