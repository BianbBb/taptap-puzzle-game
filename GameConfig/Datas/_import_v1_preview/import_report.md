
## 剧情配表 V1.0 导入报告

- 源文件：`/Users/yuhangbian/Desktop/副本剧情配表_V1.0(1).xlsx`
- 输出目录：`/Users/yuhangbian/taptap-puzzle-game/GameConfig/Datas/_import_v1_preview`
- 模式：仅预览（未改动生产表）

## 数据规模

| 表 | 条数 |
|---|---|
| Session | 9 |
| Turn | 262 |
| Option | 29 |
| Memory | 19 |
| Emotion | 6 |
| Item | 6 |
| ItemPart | 19 |
| Action | 13 |

## 情绪配置表

已读取生产「情绪配置表」6 条，将以它为名称/颜色的基准。


## 记忆配置表

- ℹ️ **[MEMORY_ART_INHERITED]** 按内容复用生产表已有记忆图 [('M01', 'mem_camera_dead'), ('M02', 'mem_camera_lens'), ('M03', 'mem_glasses_frame'), ('M04', 'mem_glasses_temple'), ('M05', 'mem_glasses_lens'), ('M08', 'mem_clock_bird')]；其余记忆 imageId 为空（源表就是 placeholder），运行时只显示记忆名不留白块
- ⚠️ **[MEMORY_ART_MISSING]** 13 条记忆没有配图：['M06', 'M07', 'M09', 'M10', 'M11', 'M12', 'M13', 'M14', 'M15', 'M16', 'M17', 'M18', 'M19']。生产表还有没被复用的旧图可参考：mem_doll_repair / mem_doll_words / mem_doll_token（对应 娃娃记忆 系列，需策划确认归属）

## 叙事物品配置表 / 叙事物品部件配置表

- ℹ️ **[ART_INHERITED]** 按同 ID 继承生产表配图：物品 [('GLASS', 'narrative_glasses'), ('CAMERA', 'narrative_camera'), ('CLOCK', 'narrative_clock'), ('DOLL', 'scene_chenguo_cat_broken')]，部件 [('CAMERA_PART_01', 'narrative_camera'), ('CAMERA_PART_02', 'narrative_camera'), ('GLASS_PART_01', 'narrative_glasses'), ('GLASS_PART_02', 'narrative_glasses'), ('GLASS_PART_03', 'narrative_glasses'), ('CLOCK_PART_01', 'narrative_clock')]（源表这一版写的是 placeholder，直接覆盖会冲掉已有美术）
- ⚠️ **[ItemPart.HotspotPreset]** 13 个部件没有真实热点，已按 8 宫格均匀铺开占位（保证每个部件都能点到）：['CAMERA_PART_03', 'CLOCK_PART_02', 'CLOCK_PART_03', 'DOLL_PART_01', 'DOLL_PART_02', 'DOLL_PART_03', 'DOLL_PART_04', 'RADIO_PART_01', 'RADIO_PART_02', 'RADIO_PART_03', 'RADIO_PART_04', 'TAPE_PART_01', 'TAPE_PART_02']。物品图若是 placeholder，运行时会退回「检查部件」按钮列表，热点不生效；美术补图后请重标这些坐标

## 叙事动作配置表

- ℹ️ **[ACTION_GRANT_EMOTION]** GrantEmotion 动作 [('GRANT_EMOTION_SELF', 'E07')]：TargetID 填的是**情绪 ID**（不是物品），运行时由 NarrativeActionService 白名单放行，并走 EmotionEmerged 演出链路

## 回复选项配置表

- ℹ️ **[Option.RewardMemory]** 源表「Option」没有「奖励记忆 / RewardMemory」列，转换后 rewardMemory 全空。这不影响主链路 —— 记忆主要靠「获得物品 -> 检查部件」产出（ItemPart.memoryId），这条路径在转换里是完整的。只有当某条记忆既没有部件承载、又需要作为选项条件时才要补。用 check_story_graph.py 可以查出到底是哪几条

## 叙事Session配置表

- 场景背景：源表 `BackGroundID` 已映射到现有场景占位（7 个映射），美术补齐新图后在 BACKGROUND_MAP + BattleMainUI.TryResolveBackgroundLocations 同步加键
- ❌ **[Session.ConditionGroupID]** 项目 Session 表无执行条件字段，源表该列当前全空，暂不影响

- dayIndex 换算方式 `date-offset`：[1, 7, 11, 12, 16, 17, 23, 28, 31]

## 剧情节点配置表


- 好感条件改写（1 条，已转成项目支持的数值写法）：
    - `T0203`：`需要好感 5点` -> `minAffection=5（节点级门槛，不足走默认后继）`

## 情绪记忆规则配置表

- 导入 0 条规则（情绪阈值 + 前置记忆 -> 激活记忆）
- 前置记忆语义：`ConditionGroupID` 里的记忆必须**全部持有**才可能触发，与情绪阈值是 AND 关系

## 已生成文件

| 文件 | 数据行 |
|---|---|
| `叙事Session配置表.xlsx` | 9 |
| `剧情节点配置表.xlsx` | 262 |
| `回复选项配置表.xlsx` | 29 |
| `记忆配置表.xlsx` | 19 |
| `情绪配置表.xlsx` | 6 |
| `叙事物品配置表.xlsx` | 6 |
| `叙事物品部件配置表.xlsx` | 19 |
| `叙事动作配置表.xlsx` | 13 |
| `情绪记忆规则配置表.xlsx` | 0 |

## 转换缺口汇总（必须人工决策）

- **Session.ConditionGroupID**：项目 Session 表无执行条件字段，源表该列当前全空，暂不影响

## 警告汇总

- [MEMORY_ART_MISSING] 13 条记忆没有配图：['M06', 'M07', 'M09', 'M10', 'M11', 'M12', 'M13', 'M14', 'M15', 'M16', 'M17', 'M18', 'M19']。生产表还有没被复用的旧图可参考：mem_doll_repair / mem_doll_words / mem_doll_token（对应 娃娃记忆 系列，需策划确认归属）
- [ItemPart.HotspotPreset] 13 个部件没有真实热点，已按 8 宫格均匀铺开占位（保证每个部件都能点到）：['CAMERA_PART_03', 'CLOCK_PART_02', 'CLOCK_PART_03', 'DOLL_PART_01', 'DOLL_PART_02', 'DOLL_PART_03', 'DOLL_PART_04', 'RADIO_PART_01', 'RADIO_PART_02', 'RADIO_PART_03', 'RADIO_PART_04', 'TAPE_PART_01', 'TAPE_PART_02']。物品图若是 placeholder，运行时会退回「检查部件」按钮列表，热点不生效；美术补图后请重标这些坐标