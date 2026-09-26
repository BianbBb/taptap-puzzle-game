# HD-2D 解谜游戏交互系统设计

> 状态：设计方案，尚未实现
>
> 适用范围：单机 HD-2D / 2.5D 解谜玩法，例如靠近物体后按键调查、拾取、开门、传送和查看背景故事。

## 设计结论

交互系统采用“逻辑与表现分离”的轻量方案，不做帧同步式的完整逻辑模拟。

- Unity 物理系统负责角色移动、实体阻挡和触发器检测。
- 交互业务逻辑负责条件判断、状态变化、存档和交互结果。
- 表现组件负责动画、音效、特效和碰撞体开关。
- 交互物体使用一个统一入口组件，具体行为通过多个行为组件组合。

推荐的调用链：

```text
PlayerInteractionSensor
  -> InteractionModule 选择当前目标和行为
  -> InteractionAction.CanInteract
  -> InteractionAction.ExecuteAsync
  -> 更新业务状态
  -> 刷新动画、音效、Collider 和 UI
```

## 组件组合

场景物体挂载 `InteractableComponent` 作为统一交互入口，再按需求挂载具体的 `InteractionActionComponent`：

```text
Chest
├─ InteractableComponent
├─ PickupInteractionAction
└─ Collider / View

Portal
├─ InteractableComponent
├─ SceneTransitionInteractionAction
└─ Collider / View

StoneTablet
├─ InteractableComponent
├─ LoreInteractionAction
└─ Collider / View
```

建议的行为类型如下：

| 行为 | 主要职责 |
|---|---|
| `PickupInteractionAction` | 检查拾取条件，发放道具，记录已拾取状态 |
| `SceneTransitionInteractionAction` | 检查传送条件，调用场景模块切换场景 |
| `LoreInteractionAction` | 打开文本、对话或背景故事 UI |
| `DoorInteractionAction` | 检查钥匙或机关条件，播放开门表现并更新阻挡 |
| `SwitchInteractionAction` | 修改开关状态，通知关联机关 |

不要把所有行为集中到一个组件的 `switch (InteractionType)` 中。增加新交互时，应新增行为组件，避免修改中央分发逻辑。`EInteractionType` 可以用于提示图标、统计和配置分类，但不作为具体业务分支的唯一实现方式。

同一个物体允许挂载多个行为。每个行为需要提供可用性判断和优先级：

```text
InteractionAction
├─ Priority
├─ CanInteract(context)
└─ ExecuteAsync(context)
```

交互模块选择当前 `CanInteract` 为真的最高优先级行为。例如石碑第一次交互显示故事，故事完成后再允许领取道具。

## 运行时职责

`PlayerInteractionSensor` 挂在玩家上，只负责通过触发器维护附近的候选交互目标。玩家按下交互键时，`InteractionModule` 再次检查距离、朝向、遮挡和目标状态，不能把进入 Trigger 直接当成交互成功。

物理系统分为两类：

- 实体 Collider：地面、墙体、角色和可推动物体的实际阻挡。
- Trigger Collider：交互范围、剧情区域和机关感应区。

HD-2D 项目需要在项目层面统一选择 3D 或 2D 物理。角色使用 Sprite 不代表必须使用 2D 物理；如果角色在 3D 空间中移动，应统一使用 3D Collider 和 Physics。

## 当前 DGame 项目的代码落位

交互属于游戏玩法，放在热更业务层，不新增 `DGame/Runtime` 框架模块：

```text
GameUnity/Assets/Scripts/HotFix/GameLogic/Module/InteractionModule/
├─ IInteractionModule.cs
├─ InteractionModule.cs
├─ IInteractable.cs
└─ Component/
   ├─ InteractableComponent.cs
   ├─ InteractionActionComponent.cs
   ├─ PlayerInteractionSensor.cs
   ├─ PickupInteractionAction.cs
   ├─ SceneTransitionInteractionAction.cs
   └─ LoreInteractionAction.cs
```

组件脚本可以使用 `MonoBehaviour` 与 Unity 场景交互，但应保持为薄适配层。具体状态和规则放到 `GameLogic` 业务类中。热更组件不要假设 `[RequireComponent]` 一定会自动补齐依赖，初始化时使用 `GetComponent` / `TryGetComponent` 显式检查，并用 `DLogger` 记录错误。

## 输入、模块和事件

交互输入复用当前输入模块：

1. 在 `GameInputActions.inputactions` 增加 `Interact` 动作。
2. 重新生成 `GameInputActions.cs`，不要手改生成文件。
3. 通过 `GameModule.Input`、`IInputComponent` 或 `IInputContextLayer` 接收交互输入。
4. 输入处理器调用 `InteractionModule.TryInteract()`。

场景切换使用 `GameModule.SceneModule.LoadSceneAsync`，耗时操作使用 `UniTask`。业务代码通过 `GameModule.XXX` 访问模块，不在交互组件中直接调用 `ModuleSystem.GetModule<T>()`。

交互组件和对应逻辑之间优先直接调用方法。只有需要跨模块通知时，才在 `GameLogic/IEvent/` 定义带 `[EventInterface(EEventGroup.GroupLogic)]` 的事件接口。交互提示 UI 按 UI 生命周期注册监听，使用 `AddUIEvent`，不要把每帧位置或范围变化广播成全局事件。

## 配置和存档

第一版的场景专属参数可以使用组件序列化字段，例如交互点、优先级、场景 Location 和文本配置 ID。需要大量复用、批量编辑或多语言的数据，再通过 Luban 配置表管理，不手改生成代码。

需要持久化的物体必须配置稳定的交互 ID，例如：

```text
Room01_Door_A
Room01_Switch_B
Room01_Box_C
```

存档只保存逻辑状态，例如门是否打开、开关是否触发、箱子处于哪个格子。使用现有 `BaseClientSaveData` / `ClientSaveDataMgr`，不要保存 Unity 对象引用，也不要使用 `GetInstanceID()` 作为存档标识。

## 三种基础交互示例

### 拾取道具

```text
玩家进入拾取范围
-> 显示拾取提示
-> Interact
-> 检查背包容量和物品状态
-> 发放道具
-> 保存已拾取状态
-> 隐藏物体并关闭 Collider
```

### 传送场景

```text
玩家进入传送范围
-> Interact
-> 检查传送条件
-> 调用 GameModule.SceneModule.LoadSceneAsync
-> 根据出生点 ID 设置玩家位置
```

### 背景故事

```text
玩家进入故事物体范围
-> Interact
-> 读取文本或对话配置 ID
-> 打开故事 UI
-> 按需要记录已阅读状态
```

## 第一阶段验证

先实现拾取、场景传送和背景故事三种行为，验证以下场景：

- 进入和离开交互范围时提示正确显示。
- 多个交互物体重叠时能按距离、朝向和优先级选择目标。
- 连续按键不会重复发放道具或重复切换场景。
- 交互条件不满足时不会修改状态。
- 退出场景后重新进入，存档状态能够恢复表现和 Collider。
- 交互物体销毁或场景卸载后没有遗留事件监听。
