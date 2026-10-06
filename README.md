## AI 开发与验证

仓库开发规则见 [AGENTS.md](AGENTS.md)。

Unity 使用前先执行 `python .agents/scripts/workflow.py doctor`，校验 [CLI 版本约束](.agents/scripts/tool-versions.json)。Workflow 优先使用 `GameUnity/Tools/unity.exe`，缺少时从系统 `PATH` 查找 `unity`，不会自动安装或替换工具。完整工作流、验证矩阵和报告说明见 [.agents/README.md](.agents/README.md)。
