---
name: improve-codebase-architecture
description: 发现 DGame 代码库中的架构加深机会，依据 dgame-dev skill 的分层规范、AGENTS.md 编码红线与 docs/adr/ 决策。当用户想改善架构、寻找重构机会、合并紧耦合模块、把散落逻辑收口、生成架构审查 HTML 报告，或让代码库更易测试、更易被 AI 导航时使用。
---

# Improve Codebase Architecture

暴露 DGame 架构摩擦并提出**加深机会**，把浅模块变为深模块。DGame 是热更手游工程，没有普遍的单元测试文化，所以加深的主要收益是：**分层局部性**（变更、bug、知识集中在一处）、**AI 可导航性**（少跳转即可理解一个概念）、以及 DGame 特有的**收口**（资源、配置、事件、模块访问、纯逻辑）。可测试性作为收益仅在逻辑能下沉到 `GameBattle` 纯逻辑域时才成立。

所有提案、报告和回答使用中文。

## 术语表

在每个建议中精确使用这些术语。一致的语言是关键，不要滑向 "component"、"service"、"API" 或 "boundary"。完整定义见 [LANGUAGE.md](LANGUAGE.md)。

- **Module（模块）**：任何具有接口和实现的东西（函数、类、程序集、模块门面项）。
- **Interface（接口）**：调用者使用模块所需知道的一切：类型、不变量、错误模式、顺序、配置。不仅仅是类型签名。
- **Implementation（实现）**：内部的代码。
- **Depth（深度）**：接口处的杠杆：少量接口背后的大量行为。**Deep（深）** = 高杠杆。**Shallow（浅）** = 接口几乎与实现一样复杂。
- **Seam（接缝）**：接口所在的位置；一个可以在不原地编辑的情况下改变行为的地方。用这个词，不是 "boundary"。
- **Adapter（适配器）**：在接缝处满足接口的具体实现。
- **Leverage（杠杆）**：调用者从深度中获得的收益。
- **Locality（局部性）**：维护者从深度中获得的收益：变更、bug、知识集中在一个地方。

关键原则（完整列表见 [LANGUAGE.md](LANGUAGE.md)）：

- **删除测试**（思想实验，非真删代码）：想象删除该模块。如果复杂性消失了，它就是传递模块。如果复杂性重新出现在 N 个调用者身上，它就在发挥价值。
- **接口就是收口面。** 在 DGame，一个好接缝是资源/配置/事件/模块访问被收口的那一点。
- **一个适配器 = 假设的接缝。两个适配器 = 真正的接缝。**

本技能依据 DGame 的分层规范与编码红线，不重新争论它们。`dgame-dev` skill 的 references 为好接缝命名并界定落位；`AGENTS.md`（或当前会话提供的 AGENTS instructions）的核心原则是不可违背的红线；ADR 记录已定的决策。

## 流程

### 1. 探索

先加载 DGame 的分层约束与红线，这是本技能与项目对齐的关键，不可跳过：

- 触发 `dgame-dev` skill 获取相关主题：
  - 分层落位：`references/project-map.md`
  - 四层单向依赖、程序集边界、启动链路、`GameModule` 门面、`ConfigMgr` 封装：`references/client-architecture-codex.md` / `references/client-modules-codex.md`
  - 按涉及主题继续读取 UI、事件、资源、红点、Luban 等 reference
- 阅读 `AGENTS.md` 的核心原则（编码红线）：分层落位、优先复用 TEngine 二次封装、`GameModule` 访问、异步优先（UniTask）、资源必须成对释放、事件解耦（`GameEventDriver` / `EEventGroup`）。违背这些红线的浅性，是最高优先的加深目标。
- 阅读你将涉及区域中的任何 ADR（`docs/adr/`，若存在）。

然后遍历代码库，记录你感受到摩擦的地方。优先使用 `rg` / `rg --files` 做本地探索。只有当用户明确要求或允许并行子代理/多 agent 工作时，才使用 Codex 子代理工具；这种情况下优先用 `explorer` 子代理提出具体、窄范围的问题，不要把当前阻塞路径交给子代理。

DGame 中特别值得留意的浅模块形态：

- **`GameModule` 门面**里只做一次静态转发、几乎无逻辑的入口。它是杠杆点，也是浅性容易堆积的地方。
- **`ConfigMgr` 封装**里只透传 `TbXXX` / `ConfigSystem.Instance.Tables` 的方法；或反过来，本该收口到 `ConfigMgr` 的配置访问散落在多个 UI/模块里（配置**跨接缝泄漏**）。
- **UI 层**：`UIWindow` / `UIWidget` / 子页面只做透传的薄壳；或 UI 直接读配置表、直接加载资源，把本该在下层的知识拉进窗口脚本。
- **资源加载/卸载散落**：加载与释放不成对、分散在多处，没有一个模块持有资源生命周期（无**局部性**）。
- **事件封装单调用点**：`IEvent` 里只有一个发送者一个接收者的封装，删除它复杂性并不集中。
- **可下沉的纯逻辑**：混在 `GameLogic`（依赖 Unity/UI/资源）里的纯计算，本可下沉到 `GameBattle` 成为进程内可测深模块，却因和表现层缠绕而无法测试。
- **红点树**：节点定义、聚合与业务判断散落，未收口到 `RedDotModule` 现有入口。

对任何你怀疑是浅模块的东西应用**删除测试**：删除它会让复杂性集中，还是仅仅移动它？"集中了"就是加深信号。

**分层与红线优先**：候选必须尊重四层单向依赖（HotFix -> Main -> DGame -> 基础设施）与热更程序集边界（`GameLogic -> GameProto/GameBattle`，`GameBattle` 不得依赖 `GameLogic` 且只写纯逻辑）。加深不得引入反向依赖、不得让 Runtime/AOT 编译期引用 HotFix 业务类型、不得绕过 TEngine 二次封装层。凡是与这些冲突的"加深"，不是候选。

### 2. 以 HTML 报告呈现候选

将一个自包含 HTML 文件写入操作系统临时目录，不在仓库留痕。从 `$env:TMPDIR` / `$env:TEMP` / `[System.IO.Path]::GetTempPath()` 解析临时目录，写入 `<tmpdir>/architecture-review-<timestamp>.html`，每次运行得到新文件。Windows 上用 `Start-Process <path>` 为用户打开，并告知绝对路径。

报告用 **Tailwind via CDN** 布局，**Mermaid via CDN** 画图状关系（调用图、依赖、四层依赖流、时序），手工 div/SVG 画更偏编辑性质的视觉（质量图、剖面图）。每个候选一个 **before/after 可视化**。要视觉化。

每个候选渲染为卡片：

- **文件**：涉及哪些文件/类/程序集（用真实路径，如 `Scripts/HotFix/GameLogic/ConfigMgr/...`）
- **问题**：当前架构为什么造成摩擦（点名是哪种浅性：门面转发 / 配置泄漏 / 资源不收口 / 纯逻辑无法下沉）
- **方案**：会改变什么的通俗描述，并给出加深后模块的**落位程序集**
- **收益**：用局部性、杠杆、收口解释；仅当能下沉到 `GameBattle` 时才谈可测试性
- **Before / After 图**：并排，展示浅性和加深
- **建议强度**：`Strong` / `Worth exploring` / `Speculative`，渲染为徽章
- **跨层影响**：若跨 Runtime/Main/HotFix/配置层，明确标出

报告末尾以 **顶部建议** 章节结束：先处理哪个候选、为什么。

**领域用 dgame-dev references 的真实名称，架构用 [LANGUAGE.md](LANGUAGE.md) 的词汇。** 说 "`ConfigMgr` 的配置读取接口"、"`GameModule.ResourceModule` 加载生命周期"、"`RedDotModule` 节点入口"，不要杜撰 "FooBarHandler" 或含糊的"某个 service"。

**ADR 冲突**：只有当摩擦足够真实、值得重新审视某条 ADR 时才提出该候选，并在卡片中用琥珀色标注（例如："与 ADR-0007 矛盾，但值得重新开启，因为……"）。不要罗列 ADR 已禁止的每一个理论重构。

完整 HTML 脚手架、图表模式和样式指导见 [HTML-REPORT.md](HTML-REPORT.md)。

文件写好后，问用户："你想探索其中哪一个？"

### 3. 追问循环

用户选定候选后，进入追问对话。与他们一起走设计树：约束、依赖、加深模块的形状、接缝后面是什么、哪些逻辑能下沉到 `GameBattle` 变得可测。同时核对 DGame 落位：加深后的模块归属哪个程序集、是否仍通过 `GameModule.XXX` 访问、资源加载/卸载是否成对收口、配置是否收口到 `ConfigMgr`、事件是否走 `GameEventDriver`。

随着决策成型，副作用在线发生：

- **用 references 里没有的概念命名加深模块？** 将术语加入 `CONTEXT.md`，与 `grill-with-docs` 相同纪律（见 [CONTEXT-FORMAT.md](../grill-with-docs/CONTEXT-FORMAT.md)），文件不存在则惰性创建。若该概念属于框架级规范，同时提议补进对应的 dgame-dev reference。
- **在对话中锐化了一个模糊术语？** 就地更新 `CONTEXT.md`。
- **用户以一个承重理由拒绝候选？** 提供 ADR："要不要把这个记录为 ADR，这样未来架构审查不会再建议它？" 只在理由确实会被未来探索者需要时才提供，跳过临时理由和显而易见的理由。见 [ADR-FORMAT.md](../grill-with-docs/ADR-FORMAT.md)。
- **想探索加深模块的替代接口？** 见 [INTERFACE-DESIGN.md](INTERFACE-DESIGN.md)。
- **发现 references 描述与实际代码 API 不符？** 搜索实际方法签名验证，优先信任代码中的实际实现，并在回答中标注冲突点；必要时建议更新对应 dgame-dev reference。
