# .understand-anything — DGame 代码知识图谱

> 新人指引：这个目录是什么、能帮你做什么、怎么用。

## 一、这个目录是什么

`.understand-anything` 是 **Understand-Anything** 代码理解工具为 DGame 生成的**知识图谱**产物目录。

> 工具官方仓库：<https://github.com/Egonex-AI/Understand-Anything>

它扫描仓库源码，把整个项目抽象成一张「节点 + 关系」的图：文件、类、函数是节点，调用 / 继承 / 依赖 / 包含是边。图谱按逻辑分层归类，并附带一条为新人设计的「导览路线（Tour）」。

**一句话：这是 DGame 代码库的地图，专门帮新人（和 AI）快速搞懂"代码长什么样、模块怎么连"。**

当前快照信息（见 `meta.json`）：

| 项 | 值 |
|----|----|
| 分析时间 | 2026-07-05 |
| 分析文件数 | 448 |
| 对应 commit | `1269acbc` |
| 输出语言 | 中文（见 `config.json`） |
| 自动更新 | 关闭（`autoUpdate: false`） |

图谱规模：**1817 个节点**、**1963 条关系**，划分为 **14 个逻辑分层**。

---

## 二、目录里都有什么

```
.understand-anything/
├── README.md              ← 本文档
├── config.json            输出配置（语言、是否自动更新）
├── meta.json              元信息（分析时间、commit、文件数）
├── .understandignore      扫描忽略规则（语法同 .gitignore）
├── knowledge-graph.json   ★ 核心：完整知识图谱（节点/边/分层/导览）
├── fingerprints.json      文件指纹，用于增量更新时判断哪些文件变了
└── intermediate/
    └── scan-result.json   扫描中间产物（文件清单、语言、行数、分类）
```

**最重要的是 `knowledge-graph.json`**，其余都是支撑文件。它顶层包含：

- `project` — 项目名、语言、框架栈、整体描述
- `nodes` — 所有节点（document / file / config / class / function / resource）
- `edges` — 所有关系（contains / depends_on / calls / inherits / implements / imports / configures ...）
- `layers` — 逻辑分层（见下）
- `tour` — 12 步新人导览路线

---

## 三、DGame 的分层地图（图谱怎么切分代码）

图谱把 448 个源码文件归入 14 层。看懂这张表，就大致看懂了 DGame 的骨架（`files` 为该层文件数）：

| 分层 | 说明 | files |
|------|------|-------|
| 框架运行时核心层 | `DGame/Runtime/Core`：DGameLog、MemoryPool、GameEvent、ModuleSystem、Utility 等地基能力 | 53 |
| 框架运行时模块层 | `DGame/Runtime/Module`：AnimModule / AudioModule / ResourceModule / FsmModule / ProcedureModule / InputModule 等 | 118 |
| 编辑器工具层 | `DGame/Editor`：Spine 工具、图集工具、HybridCLR、ReleaseTools、LubanTools 等编辑器扩展 | 40 |
| 热更玩法 UI 模块层 | `HotFix/GameLogic/Module/UIModule`：UIWindow / UIWidget / UIBase、自动绑定、循环列表等 UI 框架 | 89 |
| 热更玩法模块层 | `HotFix/GameLogic/Module`：RedDotModule、FrameAnimModule、LocalizationModule、TextModule 等 | 39 |
| 热更玩法数据层 | `HotFix/GameLogic/DataCenter`：DataCenterModule、GameClient、存档、PlayerData | 10 |
| 热更玩法 UI 层 | `HotFix/GameLogic/UI`：MainWindow、GMPanel 等具体 UI 实现 | 11 |
| 热更入口层 | `HotFix/GameLogic`：GameStart、GameModule、asmdef 装配间 | 5 |
| 热更通用层 | 通用工具、ConfigMgr、IEvent 接口、GameBattle 战斗程序集 | 11 |
| 配置协议层 | `HotFix/GameProto`：ConfigSystem、Luban 生成的配置类 | 39 |
| Luban 配置层 | `GameConfig`：luban.conf、Defines、转表脚本等配置源头 | 5 |
| 构建工具层 | `Tools/BuildTools`：AB / 客户端打包脚本 | 18 |
| 文档层 | AGENTS.md、CLAUDE.md、README.md 等协作文档 | 3 |
| 工程杂项层 | ProjectSettings、Packages、裁剪脚本等 | 7 |

---

## 四、新人导览路线（Tour）

图谱内置一条 **12 步** 的学习路径，建议按顺序理解 DGame。这也是新人上手 DGame 最快的路线：

1. **项目概览** — 认识 DGame（TEngine 二次封装 + HybridCLR / YooAsset / UniTask / Luban）
2. **开发规范与强制工作流** — 先读 `CLAUDE.md` / `AGENTS.md`，理解分层落位红线
3. **热更入口与启动装配** — `GameStart.cs` → `GameModule.cs`，玩法从这里进
4. **框架运行时核心能力** — 日志 / 内存池 / 事件 / ModuleSystem 地基
5. **框架运行时模块** — 运行时模块由 ModuleSystem 统一管生命周期
6. **资源管理：YooAsset 封装** — `ResourceModule` 是加载资源的唯一入口
7. **热更玩法 UI 系统** — 窗口 / 部件 / 自动绑定 / 循环列表
8. **热更玩法数据层** — 玩家数据、存档读写
9. **热更玩法功能模块** — 红点 / 帧动画 / 本地化 / 输入
10. **配置系统：Luban 配置协议** — Excel → Luban → cs → ConfigSystem 消费
11. **编辑器扩展工具** — 一键建入口、打 DLL、出 AB
12. **构建与出包流程** — CI / 命令行批处理出包

---

## 五、怎么用它

### 方式 1：让 AI 用图谱回答问题（最常用）

图谱是给 AI 消费的结构化数据。直接向 Claude Code 提问即可，例如：

- 「用知识图谱讲讲 DGame 的整体架构」
- 「ResourceModule 被哪些模块依赖？」
- 「解释一下 UIModule 的窗口生命周期」

配套的 Understand-Anything skill 命令（在 Claude Code 中输入 `/`）：

| 命令 | 用途 |
|------|------|
| `/understand-anything:understand-onboard` | 生成新人 onboarding 指引 |
| `/understand-anything:understand-chat` | 基于图谱问答 |
| `/understand-anything:understand-explain` | 深入讲解某个文件 / 函数 / 模块 |
| `/understand-anything:understand-dashboard` | 启动可视化仪表盘看图 |
| `/understand-anything:understand-diff` | 分析 git diff / PR 的影响面 |
| `/understand-anything:understand-domain` | 提取业务域流程图 |

### 方式 2：直接查 JSON

`knowledge-graph.json` 是标准 JSON，可用脚本按 `layers` / `nodes` / `edges` 检索。例如统计某层文件、追踪某个类的调用链。

---

## 六、新人常用命令速查

以下命令都在 Claude Code 里以 `/` 开头输入（skill 命令）。默认作用于当前仓库，也可在命令后加一个目录路径指定其他工程。

### 📊 启动图谱网页（可视化浏览）

```
/understand-anything:understand-dashboard
```

- 启动一个本地 **Vite 网页服务器**，把知识图谱渲染成可交互的架构图，比直接看 JSON 直观得多。
- 首次运行会自动安装依赖并构建；启动后终端会打印一条**带 token 的访问地址**，形如：

  ```
  🔑  Dashboard URL: http://127.0.0.1:5173?token=<TOKEN>
  ```

- **必须带上 `?token=...` 整段地址**在浏览器打开，否则会被 token 校验拦住。端口 5173 被占用时会自动顺延。
- 前提：`.understand-anything/knowledge-graph.json` 已存在（否则先跑 `/understand`）。

### 🔄 增量更新图谱（代码改动后刷新）

```
/understand-anything:understand
```

- 直接运行 `/understand`，工具会用 `fingerprints.json` 对比自上次分析以来**改动过的文件**，**只重新分析变更部分**，速度快、省 token。
- 判定规则：
  - 存在旧图谱 **且有文件改动** → 自动走**增量更新**（只重扫改动文件）。
  - 存在旧图谱 **且 commit 未变** → 会询问你：全量重建 / 跑图谱审查 / 什么都不做。

### 🔁 全量重建图谱（结构大改 / 想从头来）

```
/understand-anything:understand --full
```

- 忽略现有图谱，重扫全部文件。项目结构大幅调整、或觉得图谱明显不准时用它。

### ⚙️ 其他常用参数

| 命令 | 用途 |
|------|------|
| `/understand-anything:understand --language zh` | 指定图谱文本语言（本项目已设为中文 `zh`） |
| `/understand-anything:understand --auto-update` | **开启** commit 后自动更新（写入 `config.json`） |
| `/understand-anything:understand --no-auto-update` | **关闭** 自动更新（当前即为此设置） |
| `/understand-anything:understand --review` | 运行 LLM 图谱审查，做更严格的质量校验 |
| `/understand-anything:understand /path/to/repo` | 分析指定目录而非当前工程 |

---

## 七、什么时候需要更新

图谱是 **某个 commit 时刻的快照**（当前对应 `1269acbc`）。因为 `autoUpdate` 关闭，代码有较大变动后，图谱不会自动跟进。

- 当你发现图谱描述和当前源码明显对不上时，**以源码为准**。
- 需要刷新时，按第六节跑增量更新（`/understand`）或全量重建（`--full`）。

---

## 八、边界与注意事项

- **不是源码，是索引。** 这里没有可运行的代码，改这里的 JSON 不会影响游戏。
- **有扫描范围。** `.understandignore` 排除了 Unity 生成目录、`.meta`、二进制资源、第三方框架（Plugins / YooAsset / HybridCLRGenerate 等）、Luban 工具产物和测试文件——图谱**聚焦 DGame 自身框架代码**，第三方源码不在其中。
- **图谱描述可能滞后。** 结论要落地到具体开发时，仍需用 `rg` 核对真实 API（与 `CLAUDE.md` 的冲突处理原则一致）。
- 处理 DGame 代码请优先使用 `dgame-dev` skill；配置表相关优先 `luban-dev` skill。
