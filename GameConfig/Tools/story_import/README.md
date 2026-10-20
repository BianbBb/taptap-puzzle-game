# 剧情配表导入与可达性校验

把策划版《剧情配表 V1.0》（Session / Turn / Option / Memory / Emotion / Item / Action 多 Sheet 的独立结构）
转成 DGame 现有的 8 张 Luban 叙事表，并在导入前后检查"支线是否都能真的走得到"。

## 两个脚本

### 1. convert_story_v1.py —— 转成程序可读的表

```bash
# 预览：只生成到 GameConfig/Datas/_import_v1_preview/，不动生产表
python GameConfig/Tools/story_import/convert_story_v1.py \
    --src ~/Desktop/副本剧情配表_V1.0.xlsx

# 正式写入：只覆盖剧情驱动的 4 张表（Session / 剧情节点 / 回复选项 / 情绪记忆规则）
python GameConfig/Tools/story_import/convert_story_v1.py \
    --src ~/Desktop/副本剧情配表_V1.0.xlsx --apply

# 连手工维护表一起覆盖（记忆 / 情绪 / 物品 / 部件 / 动作）—— 慎用
python GameConfig/Tools/story_import/convert_story_v1.py \
    --src ~/Desktop/副本剧情配表_V1.0.xlsx --apply --with-static
```

**默认保护手工维护的表。** 记忆表、情绪表、物品表目前由策划在 `Datas/` 里直接维护
（记忆表已扩到 M01–M31 并加了 `imageId`），转换器默认不覆盖它们，
只生成预览供比对；需要覆盖必须显式加 `--with-static`。

`--day-mode`：`date-offset`（默认，按 SessionDate 相对天数换算 dayIndex）或 `sequential`（按顺序 1..N）。

产出：9 张表 + `import_report.md`（含数据规模、缺口清单、警告、**记忆 ID 语义冲突检查**）。

生成的表和 `GameConfig/Datas/` 现有表的 `##var` / `##type` 逐字段一致，
所以不需要改 `__tables__.xlsx`，转表脚本可直接读取。

### 2. check_story_graph.py —— 支线可达性校验

```bash
# 校验策划源表（改完表先自查，不必先导入）
python GameConfig/Tools/story_import/check_story_graph.py \
    --src ~/Desktop/副本剧情配表_V1.0.xlsx --out report.md

# 校验导入后的 Luban 表
python GameConfig/Tools/story_import/check_story_graph.py \
    --datas GameConfig/Datas --out report.md
```

退出码 0 = 通过，1 = 有 ❌ 错误，可直接接进 CI / 转表前钩子。

## 判定规则（与运行时保持一致）

可达性的边按 `NarrativeDomainService.ResolveNextNode` 的真实优先级算：

1. `reply.nextNodeId`（选项指定后继）
2. `extraNextNodeIds`（额外后继候选，按节点 priority 排序）
3. `branches`（`ANY,minAff,maxAff,nextNode` 好感门槛）
4. `defaultNextNodeId`（默认后继）

`isSessionEnd=true` 的节点是 Session 终点，不再展开。
Session 之间只能靠 `nextSessionId` 单链推进（`NarrativeStoryScheduler`）。

## 三层检查

| 层 | 查什么 | 能抓到的问题 |
|---|---|---|
| L1 结构可达 | 纯图遍历 | 走不到的 Turn、孤儿 Session、悬空引用、断头节点、跨 Session 归属歧义 |
| L2 条件可达 | 叠加 `requiredMemory` 锁定 + 记忆获得闭包（含情绪记忆规则） | 永远锁死的选项、**记忆来源缺口清单**、规则阈值够不到、记忆自锁死、新档首节点全是锁定选项 |
| L3 支线覆盖 | 只沿"实际能选"的选项走 | 结构上能到、但玩家被门槛挡住永远看不到的 Turn |

**只看 L1 会漏问题。** 当前 V1.0 源表 L1 全绿（262/262 可达），
但 L2 有 14 个选项因记忆无法获得而成死选项，L3 有 11 个 Turn 因此永远走不到。

## 已知缺口（导入前必须人工决策）

| 缺口 | 影响 | 处理建议 |
|---|---|---|
| `Option.RewardMemory` 源表没有这一列 | 记忆只能靠情绪记忆规则激活；规则条数不足时记忆链断裂 | 补列，或在规则表里补齐每条记忆的来源 |
| `ItemPart` 无 hotspot 坐标 | 部件热点统一填 0.5/0.5（画面正中） | **本版不处理**，后续在 prefab 里重标 |
| `Emotion.Description` / `Memory.ImageID` | 项目表无对应字段，丢弃 | 当前无影响（配图均为 placeholder） |
| 情绪 `E06 怀念` / `E07 自我` 源表无颜色 | 已填默认 `#E6E6E6` | 需美术补 `displayColor` 与 UI 图标 |

已解决的问题，转换器会自动处理，不再列为缺口：

- **NPC 占位 ID**：`NPC_001=老周 / NPC_002=陈先生 / NPC_003=陈果`，在 `convert_story_v1.py` 的 `NPC_ID_MAP` 里维护。
- **Session 背景**：源表 `BackGroundID` 映射到现有 5 套场景（`scene_shop_old` / `scene_shop_new` / `scene_zhou` / `scene_chenguo` / `scene_father`），
  映射表在 `BACKGROUND_MAP`。新图补齐后，在 `BACKGROUND_MAP` 和 `BattleMainUI.TryResolveBackgroundLocations` 同步加键即可，不用改逻辑。
- **好感门槛自然语言**：`需要好感 5点` 这类写法自动转成项目支持的数值表达式，见下节。
- **情绪表不整体覆盖**：源表 `Emotion` 的 `Name` 列放的是英文 key（`understanding` / `nostalgic`），
  中文在 `Description` 列。直接按 `Name` 写入会让 UI 显示英文、并冲掉策划调好的颜色。
  转换器改为**以生产表为基准**：已有 ID 保留原名与原色，只追加源表新增的 ID。
- **对应表编号翻译**：见下节 `CORR_TO_SRC_MEMORY`。

## ⚠️ 导入前必查：记忆 ID 语义冲突

转换器会自动对比「源表 Memory」和「生产表 记忆配置表」，同一 ID 含义不同的会列出来，
并指出哪些 `requiredMemory` 会被指错。当前状态是 **13 个 ID 冲突、10 个选项受影响**（M07–M19）。

这一步不能跳过：`requiredMemory` 只存 ID，一旦两边 ID 语义不一致，剧情会静默引用到错的记忆，
运行时不会报错，只会表现为"玩家莫名解锁了不相关的记忆"。

```bash
python GameConfig/Tools/story_import/convert_story_v1.py --src <xlsx>
sed -n '/记忆 ID 语义冲突/,/结论/p' GameConfig/Datas/_import_v1_preview/import_report.md
```

### 这不是"谁写错了"，是两代剧情并存

| | 生产表 `记忆配置表.xlsx` | V1.0 源表 `Memory` |
|---|---|---|
| 条数 | M01–M31 + M98/M99（33 条） | M01–M19 + M98/M99（21 条） |
| 粒度 | 一条 = 一个剧情认知（「试着修理」「共同的期待」） | 一条 = 一个物品部件观察点（「旧镜架」「蒙灰镜片」）+ 少量对话记忆 |
| 归属 | 没有物品归属列 | 按物品分组（来自对应表） |

生产表是**旧剧情**的记忆体系。V1.0 上线必须整体替换 `记忆配置表.xlsx`，同时替换
「剧情节点配置表」，不能只换一部分。

好消息是 **ID 空间对得开**：源表只用到 M01–M19 + M98/M99，生产表的 M20–M31（旧"记忆清理"线）
与它们不重叠，可以原样保留当旧存档兜底，也可以随旧节点表一起清掉。

## 三份表的编号关系（重要，容易踩坑）

同名不同号，一共三套编号，别混用：

| 表 | 编号方案 | 用途 |
|---|---|---|
| 剧情源表 `Memory` sheet | M01–M19，按**物品名**编号、跨物品跳跃 | `requiredMemory` / `rewardMemory` 的**唯一真源** |
| `物品-记忆-情绪对应表` | M01–M18 + M20，按**物品顺序紧凑**编号 | 提供"记忆 → 归属物品"映射（配置表没有这列） |
| 生产表 `记忆配置表.xlsx` | M01–M31 | 旧剧情体系，将被替换 |

对应表与源表是**同一套 19 条记忆**，内容一一对应（描述相似度全部 1.00），只是编号错位。
转换器里用 `CORR_TO_SRC_MEMORY` 这张 19 行翻译表搭桥：

| 对应表 | → 源表 | 对应表 | → 源表 |
|---|---|---|---|
| M01 无法使用 | M01 | M10 修复尝试 | M11 |
| M02 镜头见证 | M02 | M11 未尽之言 | M12 |
| M20 相机胶卷 | M07 | M12 卡带 | M13 |
| M03 旧镜架 | M03 | M13 无缘重温 | M14 |
| M04 蒙灰镜片 | M05 | M14 遗忘许久 | M15 |
| M05 不同镜腿 | M04 | M15 尘封旧事 | M16 |
| M06 报时鸟鸣 | M08 | M16 旧事重提 | M19 |
| M07 月下钟声 | M09 | M17 初见你 | M18 |
| M08 旧时停摆 | M06 | M18 起死回生（对话记忆） | M17 |
| M09 友情信物 | M10 | | |

**注意**：拿源表编号直接去对应表里查会大面积查不到。这不是"对应表缺条目"，
是编号体系不同 —— 之前误报的"对应表缺 M18/M19"就是这个坑。

翻译后重新核对 16 个 `requiredMemory` 引用：**14 条 OK、2 条跨物品**（见下）。

### 2 处跨物品引用

| Option | 所在节点 | 引用记忆 | 判断 |
|---|---|---|---|
| `O060702` | `T0607` 磁带 | `M16` 收录机记忆4「磁带放置处有磁带内置」 | ✅ 合法。台词"磁带记录时间为2017年3月25日……您的记忆里有和这个时间挂钩的事情吗"回指收音机阶段发现磁带，前后呼应成立 |
| `O020602` | `T0204` 座钟 | `M02` 相机记忆2 | ❌ **疑似填表 bug**。`O02060` 整组文本写的是"结论：**无法使用**，建议丢弃"——"无法使用"是相机的结论（相机记忆1），而座钟只是上弦孔生锈、能修；条件又挂 `M02`（相机记忆）而非座钟记忆（`M08/M09/M06`）。看起来是从相机那组复制过来没改 |

`O020602` 的连带风险：座钟章节的两个选项里，只有它带条件。若玩家在相机阶段没拿到 `M02`，
它会被锁死，只能选 `O020601`"建议丢弃"，座钟线的走向就变了。建议策划先修这一组。

## 好感门槛的标准写法

项目不支持自然语言条件，转换器按下面规则改写：

| 源表写法 | 项目写法 | 运行时行为 |
|---|---|---|
| `需要好感 5点` + `ExtraNextTurnIDs=[T0203]` | `branches = ANY,5,101,T0203` | 好感 ≥5 走 T0203，否则走 `defaultNextNodeId` |
| `需要好感 5点`（无额外后继） | `minAffection = 5` | 节点级门槛，好感不足时跳过本节点走默认后继 |
| `好感 5-10` | `branches = ANY,5,10,...` | 区间判定，`maxAffection` 是开区间 |

解析函数是 `parse_affection_condition()`，支持 `需要好感 5点` / `好感≥5` / `好感 5-10` / `affection >= 5`。

## 记忆获得机制：选项奖励 + 情绪记忆规则

记忆有两条来源，运行时都会把新记忆并入存档：

1. **选项奖励** `ReplyOptionConfig.rewardMemory`：选中该回复就直接获得。
2. **情绪记忆规则** `EmotionMemoryRuleConfig`：情绪累计使用次数达到阈值 **且** 前置记忆全部持有时，
   激活奖励记忆。两个条件是 **AND** 关系。

规则表字段：

| 字段 | 含义 |
|---|---|
| `emotionId` | 被检查的情绪 |
| `threshold` | 该情绪被使用/表达的累计次数阈值 |
| `requiredMemoryIds` | 前置记忆，必须**全部持有**才可能触发 |
| `rewardMemoryIds` | 条件满足后激活的记忆 |
| `priority` | 多条规则同时满足时的执行顺序，数值越大越优先 |
| `repeatPolicy` | `OncePerRun` / `OncePerDay` / `Repeatable`（当前实现里 OncePerRun 与 OncePerDay 都只触发一次） |
| `maxTriggerCount` | 整局最多触发次数，0 表示不限 |

运行时入口：`NarrativeDomainService.ActivateMemoriesByEmotionRule()`，
在 `CommitTurn` 里"情绪计数累加之后、ApplyTurnState 落盘之前"执行，
所以同一轮里新增的情绪可以立刻触发规则。

## 完整流程

```bash
# 1. 导入并生成预览 + 报告
python GameConfig/Tools/story_import/convert_story_v1.py --src <xlsx>

# 2. 读 GameConfig/Datas/_import_v1_preview/import_report.md，确认缺口

# 3. 校验预览表
python GameConfig/Tools/story_import/check_story_graph.py \
    --datas GameConfig/Datas/_import_v1_preview

# 4. 全绿后写入生产表
python GameConfig/Tools/story_import/convert_story_v1.py --src <xlsx> --apply

# 5. 转表 + 重新编译
bash GameConfig/GenerateTool_Binary/gen_bin_client_lazyload.sh
```

第 5 步之后，运行时的 `NarrativeConfigValidator` 会再做一次引用完整性检查
（悬空引用、无出口节点、分支区间为空等），两者互补：本工具管"能不能走到"，
运行时校验器管"字段引用对不对"。
