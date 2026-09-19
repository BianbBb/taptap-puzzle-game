# Spine 3.8 运行时

此嵌入式 Unity Package 用于 Spine 3.8.xx（包括 3.8.75）导出资源。
Package Manager 中的 `3.8.0` 是官方运行时包版本，不是 Spine 编辑器补丁版本。

- 官方仓库：https://github.com/EsotericSoftware/spine-runtimes
- 分支：`3.8`
- 固定提交：`8b4844bd4b193ba9e54487ed397a777993cbad56`
- Unity 集成来自 `spine-unity/Assets/Spine`。
- C# 核心来自同一提交的 `spine-csharp/src`，按官方目录布局合并到 `Runtime/spine-csharp`。
- 保留官方 GUID、着色器、材质和编辑器导入工具；未安装示例和 2D Renderer 示例。
- `URP/Shaders` 集成了官方 3.8 URP Shader，并将官方 `Assets/Spine/...` include 路径适配为 `Packages/com.esotericsoftware.spine.spine-unity/...`。
- 项目使用 Unity 2022.3 的 URP 14；Spine 3.8 的旧 2D Renderer Shader 与 URP 14 接口不兼容，因此不纳入本项目。

本地适配：在 package.json 及运行时、编辑器 asmdef 中声明 Unity 2022 的 UGUI 依赖。
3.8 的 C# 核心和 Unity 集成属于同一个 `spine-unity` 程序集，因此项目的
`SPINE_UNITY`、`SPINE_CSHARP` 均由本包的 `[3.8,3.9)` 版本条件启用。

许可证见 `LICENSE`。升级时应保持核心与 Unity 集成来自同一提交，并核对资源导出版本。
