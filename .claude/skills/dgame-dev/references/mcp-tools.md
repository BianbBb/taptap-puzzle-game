# MCP 工具指南

> **适用场景**：CoplayDev unity-mcp 场景管理、GameObject/Prefab 操作、UI 资源拼装、脚本生成、编辑器自动化、测试与控制台排查 | **关联文档**：[mcp-visual.md](mcp-visual.md)（材质/Shader/VFX/动画）、[naming-rules.md](naming-rules.md)（UI 节点前缀）、[ui-lifecycle.md](ui-lifecycle.md)（UI 生命周期）

## 已核实来源

DGame 在 `GameUnity/Packages/manifest.json` 引入 `com.coplaydev.unity-mcp`，当前锁定包缓存为 `GameUnity/Library/PackageCache/com.coplaydev.unity-mcp@11836003a5`。工具名以源码中的 `[McpForUnityTool("...")]` 为准，不要照抄其它项目文档。

常用工具名：

| 工具 | 用途 |
|------|------|
| `batch_execute` | 批量执行多个 MCP 命令 |
| `manage_gameobject` | 创建、修改、删除、复制、移动 GameObject |
| `find_gameobjects` | 按名称、路径、Tag、Layer 查找对象 |
| `manage_components` | 添加、移除、设置组件属性 |
| `manage_prefabs` | 创建 Prefab、查看/修改 Prefab 内容、Prefab Stage |
| `manage_scene` | 当前场景、层级、保存、加载、截图、Build Settings |
| `manage_asset` | 搜索、创建、移动、重命名、复制、删除资源 |
| `manage_script` | 创建、删除、验证脚本；`apply_text_edits` 是它的 action |
| `manage_scriptable_object` | 读写或创建 ScriptableObject 资产 |
| `manage_editor` | Play/Pause/Stop、Tag/Layer 管理 |
| `execute_menu_item` | 执行 Unity 菜单项 |
| `read_console` | 读取 Console 日志 |
| `refresh_unity` | 刷新 AssetDatabase |
| `run_tests` / `get_test_job` | 启动并轮询 Unity Test Runner |
| `unity_reflect` | 查询 Unity 类型/成员文档 |
| `execute_code` | 执行编辑器 C# 代码，谨慎用于一次性探查 |

同包还包含 `manage_build`、`manage_packages`、`manage_physics`、`manage_camera`、`manage_graphics`、`manage_profiler`、`manage_probuilder`，仅在任务明确涉及时使用。

## 使用前检查

1. 确认 Unity Editor 已打开 DGame 的 `GameUnity` 项目。
2. 确认 MCP 客户端已连接到 CoplayDev unity-mcp。
3. 操作前优先 `read_console` 查看是否已有编译错误；有编译错误时先处理，否则 Prefab/脚本工具可能返回误导结果。
4. 批量创建对象、组件、资源时优先 `batch_execute`，但单批保持可回滚、可定位。

## DGame 路径

| 类型 | 路径 |
|------|------|
| UI 窗口/Widget 业务脚本 | `Assets/Scripts/HotFix/GameLogic/UI/<模块名>/` |
| UIModule 与 UI 基础模块 | `Assets/Scripts/HotFix/GameLogic/Module/UIModule/` |
| 业务模块 | `Assets/Scripts/HotFix/GameLogic/Module/<ModuleName>/` |
| 事件接口 | `Assets/Scripts/HotFix/GameLogic/IEvent/` |
| Luban 生成代码 | `Assets/Scripts/HotFix/GameProto/`，自动生成，勿手改 |
| 热更资源根目录 | `Assets/BundleAssets/` |
| UI Prefab | `Assets/BundleAssets/UI/` |
| UI 图集/原始图 | `Assets/BundleAssets/UIRaw/` |
| 场景资源 | `Assets/BundleAssets/Scenes/` |
| 材质/Shader/特效 | `Assets/BundleAssets/Materials/`、`Assets/BundleAssets/Shaders/`、`Assets/BundleAssets/Effects/` |

MCP 工具接收的是 Unity 资产路径，通常以 `Assets/...` 开头；不要写宿主机绝对路径。DGame 资源根目录是 `Assets/BundleAssets/`。

## batch_execute 优先

批量创建层级或设置组件时使用 `batch_execute`：

```json
{
  "tool": "batch_execute",
  "commands": [
    { "tool": "manage_gameobject", "params": { "action": "create", "name": "TempUIRoot" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "TempUIRoot", "componentType": "Canvas" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "TempUIRoot", "componentType": "CanvasScaler" } },
    { "tool": "manage_components", "params": { "action": "add", "target": "TempUIRoot", "componentType": "GraphicRaycaster" } }
  ],
  "failFast": true
}
```

默认每批最多 25 条命令（MCP Tools 窗口可配置，硬上限 100）；`failFast: true` 遇首个错误即停止。

## 目标定位方式（target）

多数工具用 `target` 定位对象，`searchMethod` 指定匹配方式：

| 定位依据 | 示例 | searchMethod |
|----------|------|--------------|
| 名称 | `"Canvas"` | `by_name`（默认，取首个匹配） |
| 层级路径 | `"UIRoot/Canvas/Panel"` | `by_path` |
| InstanceID | `12345` | `by_id`（最可靠，不受重名影响） |
| Tag | `"Enemy"` | `by_tag` |
| Layer | `"UI"` | `by_layer` |

重名场景先用 `find_gameobjects` 拿到 InstanceID 或完整路径，再执行修改，避免命中错误节点。

## 场景与 GameObject

### manage_scene

| action | 说明 | 关键参数 |
|--------|------|----------|
| `get_active` | 当前场景信息 | — |
| `get_hierarchy` | 场景层级，结果过长时分页 | `parent`、`pageSize`、`cursor`、`maxDepth`；返回 `truncated=true` 时带 `next_cursor` 翻页 |
| `save` | 保存当前场景 | — |
| `load` | 加载场景 | `name`、`path`（先 save 当前场景） |
| `create` | 创建新场景 | `name`、`path` |
| `screenshot` | 截图，默认落 `Assets/Screenshots/` | `fileName`、`superSize`、`outputFolder` |
| `get_build_settings` | 获取 Build Settings 场景列表 | — |

### manage_gameobject

| action | 说明 | 关键参数 |
|--------|------|----------|
| `create` | 创建 GameObject 或 Primitive | `name`、`parent`、`position`、`componentType`、`primitiveType`（Cube/Sphere/Plane/Cylinder/Capsule/Quad） |
| `modify` | 修改名称、父节点、Transform、active 状态 | `target`、`name`、`parent`、`position`、`rotation`、`scale`、`setActive` |
| `delete` | 删除对象 | `target` |
| `duplicate` | 复制对象 | `target`、`name`、`position` |
| `move_relative` | 相对移动 | `target`、`deltaPosition`、`space`（World/Self） |
| `look_at` | 朝向目标 | `target` |

### find_gameobjects

先查再改。`searchMethod` 取值见[目标定位方式](#目标定位方式target)；返回 `name`、`instanceID`、`path`、`componentTypes`、`activeSelf`。重名对象优先使用 InstanceID 或完整层级路径。

### manage_components

| action | 说明 | 关键参数 |
|--------|------|----------|
| `add` | 添加组件 | `target`、`componentType`、`properties` |
| `remove` | 移除组件 | `target`、`componentType` |
| `set_property` | 设置组件属性 | `target`、`componentType`、`properties` |

常用组件：`Canvas`、`CanvasScaler`、`GraphicRaycaster`、`RectTransform`、`Button`、`Image`、`RawImage`、`Text`、`ScrollRect`、`GridLayoutGroup`、`HorizontalLayoutGroup`、`VerticalLayoutGroup`、`CanvasGroup`、`TMP_Text`、`Rigidbody`、`Collider`、`Animator`、`ParticleSystem`。

## DGame UI Prefab 操作

DGame 运行时 UI 主要走 `UIWindow` / `UIWidget` / `UIModule` 和 uGUI Prefab。CoplayDev 当前 `manage_ui` 工具面向 UI Toolkit（`.uxml` / `.uss` / `UIDocument`），不要把它当成 uGUI Button/Text 快速创建器。

`manage_ui` 适用于 UI Toolkit 资产：

| action | 说明 |
|--------|------|
| `create` / `read` / `update` / `delete` / `list` | 管理 `.uxml` / `.uss` 文件 |
| `attach_ui_document` / `detach_ui_document` | 给 GameObject 挂载或移除 `UIDocument` |
| `create_panel_settings` / `update_panel_settings` | 管理 `PanelSettings` |
| `get_visual_tree` / `render_ui` | 查看或渲染 UI Toolkit 树 |
| `link_stylesheet` | 关联 USS |
| `modify_visual_element` | 修改 VisualElement 文本、class、样式等 |

uGUI Prefab 推荐流程：

1. 使用 `manage_gameobject` 创建临时根节点。
2. 使用 `manage_components` 添加 `Canvas`、`CanvasScaler`、`GraphicRaycaster` 和子节点所需 uGUI 组件。
3. 节点命名严格遵守 [naming-rules.md](naming-rules.md#ui-节点前缀)，例如 `m_btnClose`、`m_tfContent`、`m_dropDownQuality`。
4. 使用 `manage_prefabs` 的 `create_from_gameobject` 保存到 `Assets/BundleAssets/UI/<Name>.prefab`。
5. 删除场景临时对象。
6. 如需生成绑定脚本，走 DGame 的 UI 脚本生成器或现有生成链路，脚本放 `Assets/Scripts/HotFix/GameLogic/UI/<模块名>/`。

Prefab 根节点应至少具备：

```text
XxxUI.prefab
├── Canvas
├── CanvasScaler
├── GraphicRaycaster
└── m_tfContent / m_btnClose / ...
```

Canvas 适配（`CanvasScaler` 通过 `componentProperties` 或 `manage_components` 设置）：

| 属性 | 推荐值 | 说明 |
|------|--------|------|
| `uiScaleMode` | `ScaleWithScreenSize` | 按屏幕尺寸缩放 |
| `referenceResolution` | `{x:1920, y:1080}` | 设计分辨率，按项目实际为准 |
| `screenMatchMode` | `MatchWidthOrHeight` | 宽高匹配 |
| `matchWidthOrHeight` | `0.5` | 宽高各占一半，兼顾横竖比例 |

子节点锚点规则：全屏背景锚点拉伸到四角（`anchorMin {0,0}` / `anchorMax {1,1}`）；固定尺寸控件（按钮、图标）用单点锚点并靠近所在屏幕区域，避免不同分辨率下偏移。

骨架模板（`manage_gameobject` create 的 `componentsToAdd` 直接带组件）：

```json
{ "tool": "manage_gameobject", "params": {
  "action": "create", "name": "m_btnClose", "parent": "Canvas",
  "componentsToAdd": ["RectTransform", "Image", "Button"] } }
```

`componentsToAdd` 支持字符串数组，或 `{ "typeName": "Image", "properties": { ... } }` 对象形式带初始属性。

已存在 Prefab 使用 `manage_prefabs`：

| action | 说明 |
|--------|------|
| `get_info` | 查看 Prefab 基础信息 |
| `get_hierarchy` | 查看 Prefab 层级 |
| `modify_contents` | Headless 修改 Prefab 内容 |
| `open_prefab_stage` / `save_prefab_stage` / `close_prefab_stage` | 进入 Prefab Stage 工作流 |

## 脚本管理

### manage_script

| action | 说明 |
|--------|------|
| `create` | 创建 C# 脚本 |
| `delete` | 删除 C# 脚本 |
| `get_sha` | 获取文件 SHA256，编辑前使用 |
| `apply_text_edits` | 精确文本编辑，是 `manage_script` 的 action |
| `validate` | 验证脚本语法，`level` 取 `basic` / `standard`（默认） / `comprehensive` / `strict` |

创建 UI 脚本示例路径：

```json
{
  "tool": "manage_script",
  "params": {
    "action": "create",
    "name": "BattleMainUI",
    "path": "Assets/Scripts/HotFix/GameLogic/UI/Battle",
    "namespace": "GameLogic",
    "contents": "using DGame;\nnamespace GameLogic\n{\n    public class BattleMainUI : UIWindow\n    {\n    }\n}\n"
  }
}
```

编辑既有脚本优先流程：

```text
1. manage_script action=get_sha
2. manage_script action=apply_text_edits，带 precondition_sha256
3. manage_script action=validate
4. read_console types=["error"]
```

不要把 `apply_text_edits` 写成独立 MCP 工具名。

`apply_text_edits` 常见错误码：

| 错误码 | 含义 | 处理 |
|--------|------|------|
| `precondition_required` | 缺少 `precondition_sha256` | 先执行 `manage_script action=get_sha`，再带 SHA 重试 |
| `stale_file` | 文件已变化，当前 SHA 过期 | 重新 `get_sha`，基于最新文件重算 edits |
| `overlap` | 多个 edit 范围重叠或顺序冲突 | 拆分 edits，确保范围不重叠且按工具要求提交 |

`apply_text_edits` 可选 `options`：`refresh`（`debounced` 延迟合并编译，避免每次编辑都触发导入）、`validate`（`basic` / `standard` / `comprehensive` / `strict`，编辑后即时校验级别）。多 edit 时行列从 1 开始、区域不得重叠。

## 资源与菜单

### manage_asset

| action | 说明 |
|--------|------|
| `search` | 搜索资源 |
| `get_info` | 查看资源类型、GUID、依赖 |
| `create_folder` | 创建目录 |
| `move` / `rename` / `duplicate` / `delete` | 资产移动、重命名、复制、删除 |
| `import` / `create` / `modify` | 导入或改写资产 |
| `get_components` | 查看 Prefab/资产上的组件信息 |

资源改动后需要确认 YooAsset 收集规则和运行时地址。DGame 资源地址通常来自 `Assets/BundleAssets` 下的热更资源，不要假设文件名一定就是最终 Address，改动前搜索现有加载点和 YooAsset 配置。

### manage_scriptable_object

DGame 当前包只有 `create` 与 `modify` 两个 action（读取字段走 MCP 资源，不是独立 `read` action）：

| action | 说明 | 关键参数 |
|--------|------|----------|
| `create` | 创建 SO 资产实例 | `typeName`、`folderPath`、`assetName`、`overwrite` |
| `modify` | 按属性路径批量改字段 | `target`（SO 资产路径）、`patches` |

`patches` 每项：`propertyPath` + `op`（默认 `set`，数组用 `array_resize`）+ `value`。示例：

```json
{
  "tool": "manage_scriptable_object",
  "params": {
    "action": "modify",
    "target": "Assets/BundleAssets/Config/BattleConfig.asset",
    "patches": [
      { "propertyPath": "maxLevel", "op": "set", "value": 60 }
    ]
  }
}
```

### execute_menu_item

菜单路径必须以当前 Unity 菜单实际存在为准。常见用途：

| 操作 | 处理方式 |
|------|----------|
| UI 脚本生成 | 先确认本仓库 `UIScriptGenerator` 菜单项，再执行 |
| Luban 导表 | 优先使用 `luban-dev` 指引，不凭菜单名猜测 |
| HybridCLR 生成 | 先确认菜单项和当前平台 |
| 保存项目 | 可执行 Unity 标准保存菜单 |

## 调试与验证

| 工具 | 用法 |
|------|------|
| `read_console` | 读取 `Error` / `Warning` / `Log`，支持关键词过滤 |
| `manage_editor` | `play`、`pause`、`stop`；Tag/Layer 管理 |
| `run_tests` | 启动 EditMode/PlayMode 测试 |
| `get_test_job` | 轮询测试任务 |
| `refresh_unity` | 手动刷新资产数据库 |
| `unity_reflect` | 不确定 Unity API 时查询类型和成员 |

### read_console

`types`（日志类型数组，小写）取值：`log`、`warning`、`error`、`exception`、`assert`；`all` 为展开关键字（等价 `log`+`warning`+`error`），默认 `["error","warning"]`。支持 `count`、`filter` 关键词过滤、`format`（plain/detailed/json）。

### 场景运行调试

`manage_editor` 支持进出 Play 模式，配合 `read_console` 可无人值守跑一遍启动流程验证：

1. `read_console` 先查是否有编译错误，有则先修（编译错误下 Play 会失败或行为异常）。
2. `manage_editor` action `play` 进入运行；`pause` 暂停/恢复；`stop` 退出。
3. Play 后再 `read_console types=["error","exception"]` 抓运行期报错（如资源加载失败、空引用）。
4. 验证完 `stop` 退出，避免编辑器停在 Play 态影响后续资产操作。

```json
{ "tool": "manage_editor", "params": { "action": "play" } }
```

### Tag / Layer 管理（manage_editor）

| action | 说明 | 参数 |
|--------|------|------|
| `add_tag` / `remove_tag` | 增删 Tag | `tagName` |
| `add_layer` / `remove_layer` | 增删 Layer，自动占用 8~31 首个空闲 User Layer 槽 | `layerName` |

读取现有 Tag/Layer 列表走 MCP 资源，不是 `list_*` action。

### run_tests / get_test_job

```json
{ "tool": "run_tests", "params": { "mode": "EditMode", "assemblyNames": ["GameLogic.Tests"] } }
```

返回 `job_id` 后台运行，每 3 秒用 `get_test_job` 轮询：

```json
{ "tool": "get_test_job", "params": { "job_id": "<job_id>" } }
```

`status` 流转：`running` → `succeeded` / `failed`。清理卡死任务：`run_tests` params=`{ "clear_stuck": true }`。

推荐编译排查：

```text
1. read_console types=["error"] count=30
2. 定位脚本后用 manage_script get_sha + apply_text_edits 修复
3. validate 或等待 Unity 编译
4. read_console 再次确认
```

## 常见错误

| 错误 | 正确做法 |
|------|----------|
| 把 `manage_ui` 当 uGUI 创建工具 | uGUI 用 `manage_gameobject` + `manage_components` + `manage_prefabs` |
| 使用 `Assets/GameScripts/...` | DGame 使用 `Assets/Scripts/HotFix/GameLogic/...` |
| 使用 `Assets/AssetRaw/...` | DGame 热更资源使用 `Assets/BundleAssets/...` |
| 直接改 `GameProto` 生成代码 | 修改配置源/模板并导表 |
| 不查 SHA 直接覆盖脚本 | 先 `get_sha`，再 `apply_text_edits` |
| 重名对象直接修改 | 先 `find_gameobjects`，使用路径或 InstanceID |
