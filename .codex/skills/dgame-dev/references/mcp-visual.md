# MCP 材质与视觉操作

> **适用场景**：通过 CoplayDev unity-mcp 管理材质、Shader、纹理、动画、摄像机、图形设置、粒子/VFX、生成或导入模型与图片 | **关联文档**：[mcp-tools.md](mcp-tools.md)（通用 MCP）、[resource-api.md](resource-api.md)（运行时资源加载）、[naming-rules.md](naming-rules.md)（资源与 UI 命名）

## 已核实工具名

| 工具 | 用途 |
|------|------|
| `manage_material` | 创建材质、设置 Shader 属性、赋给 Renderer |
| `manage_shader` | 创建、读取、更新、删除 Shader 文件 |
| `manage_texture` | 创建纹理/Sprite、应用图案/渐变/噪声、设置导入参数 |
| `manage_animation` | Animator、AnimatorController、AnimationClip 操作 |
| `manage_camera` | 摄像机操作 |
| `manage_graphics` | 图形设置相关操作 |
| `manage_vfx` | ParticleSystem、VFX Graph、LineRenderer、TrailRenderer 操作 |
| `generate_image` | 生成图片资产，长任务需轮询 |
| `generate_model` | 生成模型资产，长任务需轮询 |
| `import_model` | 从远端/生成结果导入模型，长任务需轮询 |
| `import_model_file` | 导入本地模型文件 |

这些名称来自 `GameUnity/Library/PackageCache/com.coplaydev.unity-mcp@11836003a5` 的 `[McpForUnityTool("...")]` 声明。

## DGame 资源目录

| 资源类型 | 推荐目录 |
|----------|----------|
| 材质 | `Assets/BundleAssets/Materials/` |
| Shader | `Assets/BundleAssets/Shaders/` |
| 特效 Prefab / 粒子 | `Assets/BundleAssets/Effects/` |
| UI Prefab | `Assets/BundleAssets/UI/` |
| UI 图集/图标源图 | `Assets/BundleAssets/UIRaw/` |
| 音频 | `Assets/BundleAssets/Audios/` |
| 角色/模型 Prefab | `Assets/BundleAssets/Actor/` 或 `Assets/BundleAssets/Prefabs/` |
| 场景 | `Assets/BundleAssets/Scenes/` |

视觉资产进入运行时前必须确认 YooAsset 收集规则和最终资源地址。不要使用 `Assets/AssetRaw/`。

## 材质与 Shader

### manage_material

| action | 说明 | 关键参数 |
|--------|------|----------|
| `create` | 创建材质 | `materialPath`, `shader`, `color`, `property`, `properties` |
| `set_material_color` | 设置颜色属性 | `materialPath`, `color`, `property` |
| `set_material_shader_property` | 设置 Shader 属性 | `materialPath`, `property`, `value` |
| `assign_material_to_renderer` | 赋给 Renderer | `target`, `materialPath`, `slot` |
| `set_renderer_color` | 快捷设置 Renderer 颜色 | `target`, `color`, `mode`, `slot` |
| `get_material_info` | 查看材质信息 | `materialPath` |

示例：

```json
{
  "tool": "manage_material",
  "params": {
    "action": "create",
    "materialPath": "Assets/BundleAssets/Materials/HeroPreviewMat.mat",
    "shader": "Universal Render Pipeline/Lit",
    "color": { "r": 0.25, "g": 0.55, "b": 0.9, "a": 1.0 },
    "property": "_BaseColor"
  }
}
```

颜色属性按 Shader 实际属性选择。不确定时先 `get_material_info` 或 `unity_reflect`，不要猜。

### 常见 Shader 属性

| Shader / 管线 | 主颜色 | 主贴图 | 备注 |
|---------------|--------|--------|------|
| URP Lit / URP Unlit | `_BaseColor` | `_BaseMap` | DGame 视觉资产优先按当前渲染管线确认 |
| Built-in Standard | `_Color` | `_MainTex` | 标准管线旧材质常见 |
| UI/Default 类 Shader | `_Color` | `_MainTex` | UI 材质或字体材质需先查实际属性 |
| 自定义 Shader | 以 `get_material_info` 返回为准 | 以 `get_material_info` 返回为准 | 不要凭名称推断 |

`manage_material` 的当前实现会在未指定 `property` 时优先尝试 `_BaseColor`，再尝试 `_Color`；写文档或脚本时仍建议显式传入实际属性名。

`shader` 传完整 Shader 名字符串：

| Shader 名 | 用途 |
|-----------|------|
| `Universal Render Pipeline/Lit` | URP 不透明主材质 |
| `Universal Render Pipeline/Unlit` | URP 无光照 |
| `Standard` | Built-in 标准管线（URP 项目下慎用） |
| `Sprites/Default` | 2D 精灵 |
| `UI/Default` | uGUI 默认 UI 材质 |

### manage_shader

| action | 说明 |
|--------|------|
| `create` | 创建 Shader |
| `read` | 读取 Shader |
| `update` | 更新 Shader |
| `delete` | 删除 Shader |

自定义 Shader 放 `Assets/BundleAssets/Shaders/`，并确认目标渲染管线与项目设置一致。

## 纹理与 UI 图片

### manage_texture

| action | 说明 |
|--------|------|
| `create` | 创建纹理 |
| `create_sprite` | 创建 Sprite |
| `modify` | 修改纹理 |
| `delete` | 删除纹理 |
| `apply_pattern` | 应用图案 |
| `apply_gradient` | 应用渐变 |
| `apply_noise` | 应用噪声 |
| `set_import_settings` | 修改导入设置 |

UI 图标/图集源图通常放 `Assets/BundleAssets/UIRaw/`。设置 UI Sprite 时常用：

```json
{
  "tool": "manage_texture",
  "params": {
    "action": "set_import_settings",
    "path": "Assets/BundleAssets/UIRaw/Atlas/IconSword.png",
    "textureType": "Sprite",
    "generateMipMaps": false,
    "maxSize": 512
  }
}
```

## 动画

### manage_animation

`manage_animation` 的 action 需要带前缀：

| 前缀 | 常用 action |
|------|-------------|
| `animator_` | `get_info`、`get_parameter`、`play`、`crossfade`、`set_parameter`、`set_speed`、`set_enabled` |
| `controller_` | `create`、`add_state`、`add_transition`、`add_parameter`、`get_info`、`assign`、`add_layer`、`remove_layer`、`set_layer_weight`、`create_blend_tree_1d`、`create_blend_tree_2d`、`add_blend_tree_child` |
| `clip_` | `create`、`get_info`、`add_curve`、`set_curve`、`set_vector_curve`、`create_preset`、`assign`、`add_event`、`remove_event` |

`manage_animation` 的 action 必须带 `controller_`/`clip_`/`animator_` 前缀，如 `controller_create`、`clip_create`、`animator_set_parameter`。

动画控制器和 Clip 放在对应角色或特效资源目录下，例如 `Assets/BundleAssets/Actor/<Role>/Animations/`。

### 动画 workflow：角色 Idle/Run 控制器

以下示例使用当前 CoplayDev action 前缀和 DGame 资源目录。已有 FBX 动画时可跳过 `clip_create`，直接把导入后的 `.anim` 或模型子资源拆出的 Clip 路径填入 `clipPath`。

```json
[
  {
    "tool": "manage_animation",
    "params": {
      "action": "controller_create",
      "controllerPath": "Assets/BundleAssets/Actor/Hero/Animations/Hero.controller"
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "clip_create",
      "clipPath": "Assets/BundleAssets/Actor/Hero/Animations/HeroIdle.anim",
      "name": "HeroIdle",
      "length": 1.0,
      "frameRate": 30,
      "loop": true
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "clip_create",
      "clipPath": "Assets/BundleAssets/Actor/Hero/Animations/HeroRun.anim",
      "name": "HeroRun",
      "length": 0.8,
      "frameRate": 30,
      "loop": true
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "controller_add_parameter",
      "controllerPath": "Assets/BundleAssets/Actor/Hero/Animations/Hero.controller",
      "parameterName": "Speed",
      "parameterType": "Float",
      "defaultValue": 0
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "controller_add_state",
      "controllerPath": "Assets/BundleAssets/Actor/Hero/Animations/Hero.controller",
      "stateName": "Idle",
      "clipPath": "Assets/BundleAssets/Actor/Hero/Animations/HeroIdle.anim",
      "isDefault": true
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "controller_add_state",
      "controllerPath": "Assets/BundleAssets/Actor/Hero/Animations/Hero.controller",
      "stateName": "Run",
      "clipPath": "Assets/BundleAssets/Actor/Hero/Animations/HeroRun.anim",
      "isDefault": false
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "controller_add_transition",
      "controllerPath": "Assets/BundleAssets/Actor/Hero/Animations/Hero.controller",
      "fromState": "Idle",
      "toState": "Run",
      "hasExitTime": false,
      "duration": 0.12,
      "conditions": [
        { "parameter": "Speed", "mode": "Greater", "threshold": 0.1 }
      ]
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "controller_add_transition",
      "controllerPath": "Assets/BundleAssets/Actor/Hero/Animations/Hero.controller",
      "fromState": "Run",
      "toState": "Idle",
      "hasExitTime": false,
      "duration": 0.12,
      "conditions": [
        { "parameter": "Speed", "mode": "Less", "threshold": 0.1 }
      ]
    }
  },
  {
    "tool": "manage_animation",
    "params": {
      "action": "controller_assign",
      "controllerPath": "Assets/BundleAssets/Actor/Hero/Animations/Hero.controller",
      "target": "HeroPreview",
      "searchMethod": "by_name"
    }
  }
]
```

验证步骤：

1. 用 `manage_animation` + `controller_get_info` 查看参数、状态和 transition。
2. 确认 `HeroPreview` 上存在 `Animator` 且 controller 已赋值。
3. 确认 `Assets/BundleAssets/Actor/Hero/Animations/` 被 YooAsset 收集，运行时地址按收集规则使用。

## 粒子、VFX、线条

### manage_vfx

`manage_vfx` 的 action 需要带前缀：

| 前缀 | 常用 action |
|------|-------------|
| `particle_` | `create`、`get_info`、`set_main`、`set_emission`、`set_shape`、`set_color_over_lifetime`、`set_size_over_lifetime`、`set_velocity_over_lifetime`、`set_noise`、`set_renderer`、`enable_module`、`play`、`stop`、`pause`、`restart`、`clear`、`add_burst`、`clear_bursts` |
| `vfx_` | `create_asset`、`assign_asset`、`list_templates`、`list_assets`、`get_info`、`set_float`、`set_int`、`set_bool`、`set_vector2`、`set_vector3`、`set_vector4`、`set_color`、`set_gradient`、`set_texture`、`set_mesh`、`set_curve`、`send_event`、`play`、`stop`、`pause`、`reinit`、`set_playback_speed`、`set_seed` |
| `line_` | `get_info`、`set_positions`、`add_position`、`set_position`、`set_width`、`set_color`、`set_material`、`set_properties`、`clear`、`create_line`、`create_circle`、`create_arc`、`create_bezier` |
| `trail_` | TrailRenderer 相关读取和写入操作，先用工具返回或源码确认具体参数 |

示例：

```json
{
  "tool": "manage_vfx",
  "params": {
    "action": "particle_create",
    "target": "HitEffect",
    "autoAssignMaterial": true
  }
}
```

### 粒子 workflow：命中爆点特效

分步创建并配置一个瞬发爆点粒子，字段均为 `manage_vfx` 实际参数：

```json
[
  {
    "tool": "manage_vfx",
    "params": { "action": "particle_create", "target": "HitEffect", "autoAssignMaterial": true }
  },
  {
    "tool": "manage_vfx",
    "params": {
      "action": "particle_set_main", "target": "HitEffect",
      "duration": 1.0, "looping": false, "startLifetime": 0.5,
      "startSpeed": 6.0, "startSize": 0.3, "maxParticles": 100,
      "simulationSpace": "World"
    }
  },
  {
    "tool": "manage_vfx",
    "params": { "action": "particle_set_emission", "target": "HitEffect", "rateOverTime": 0 }
  },
  {
    "tool": "manage_vfx",
    "params": { "action": "particle_add_burst", "target": "HitEffect", "time": 0.0, "count": 30 }
  },
  {
    "tool": "manage_vfx",
    "params": { "action": "particle_set_shape", "target": "HitEffect", "shapeType": "Sphere", "radius": 0.2 }
  }
]
```

瞬发爆点用 `rateOverTime: 0` + `add_burst` 一次性喷发，`looping: false`；持续拖尾类改 `rateOverTime` 常量并 `looping: true`。命中/飞行特效务必 `simulationSpace: "World"`，默认 `Local` 会让粒子跟随物体移动。

特效资源保存到 `Assets/BundleAssets/Effects/`，运行时加载用 `GameModule.ResourceModule`，生命周期按 [resource-api.md](resource-api.md) 释放。

## 生成与导入资源

| 工具 | 说明 |
|------|------|
| `generate_image` | 生成图片，适合占位图、图标草案、视觉验证素材 |
| `generate_model` | 生成模型，适合原型或占位模型 |
| `import_model` | 导入生成/远端模型 |
| `import_model_file` | 导入本地模型文件 |

生成类工具通常是长任务，按工具返回的任务状态轮询。导入后必须：

1. 放入 `Assets/BundleAssets/...` 的正确分类目录。
2. 检查材质、贴图、Prefab 依赖是否也在可收集目录。
3. 用 `manage_asset get_info` 查看依赖。
4. 确认 YooAsset 收集规则和运行时 Address。

## 批量视觉操作

多个材质、纹理、Prefab 修改应使用 `batch_execute`，并设置 `failFast: true`：

```json
{
  "tool": "batch_execute",
  "commands": [
    {
      "tool": "manage_material",
      "params": {
        "action": "create",
        "materialPath": "Assets/BundleAssets/Materials/EnemyPreviewMat.mat",
        "shader": "Universal Render Pipeline/Lit"
      }
    },
    {
      "tool": "manage_material",
      "params": {
        "action": "set_material_color",
        "materialPath": "Assets/BundleAssets/Materials/EnemyPreviewMat.mat",
        "property": "_BaseColor",
        "color": { "r": 0.8, "g": 0.2, "b": 0.2, "a": 1.0 }
      }
    }
  ],
  "failFast": true
}
```

## 常见错误

| 错误 | 正确做法 |
|------|----------|
| 使用 `Assets/AssetRaw/...` | 使用 `Assets/BundleAssets/...` |
| `manage_animation` 写 `create_controller` | 写 `controller_create` |
| `manage_vfx` 写 `particle_create` 以外的无前缀 action | 使用 `particle_` / `vfx_` / `line_` / `trail_` 前缀 |
| Shader 属性凭经验写 | 先查材质/Shader 信息，再设置 `_BaseColor` / `_Color` 等属性 |
| `manage_material.create` 写 `savePath` / `shaderName` | 写 `materialPath` / `shader` |
| `set_material_color` 写 `colorProperty` 或拆散 `r/g/b/a` | 写 `property` 和 `color: { r,g,b,a }` |
| `particle_set_main` 省略 `simulationSpace` | 命中/飞行特效显式写 `simulationSpace: "World"`，默认 Local 会让粒子跟随物体移动 |
| `controller_add_transition` 用默认 `hasExitTime` | 移动/战斗等即时响应过渡写 `hasExitTime: false`，默认 `true` 需等动画播完才切换 |
| 只创建材质不放入收集目录 | 放入 `BundleAssets` 并确认 YooAsset 收集 |
| 导入模型后不查依赖 | 用 `manage_asset get_info` 检查贴图、材质、Prefab 依赖 |
