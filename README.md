## AI 开发与验证

仓库开发规则见 [AGENTS.md](AGENTS.md)。

Unity 日常查询、对象/资源操作、编译和测试使用已加载的原生 Unity CLI MCP 工具，先核对 `editor_status.projectPath`；未暴露工具时使用项目 CLI，并显式绑定本工程绝对路径。直接 CLI 使用 `GameUnity/Tools/unity.exe`，版本以 [CLI 版本约束](.agents/scripts/tool-versions.json) 为准；缺少项目副本时运行 `python .agents/scripts/workflow.py doctor`，下载并验收指定版本，不回退到全局 `PATH`。完整工作流、验证矩阵和报告说明见 [.agents/README.md](.agents/README.md)。
