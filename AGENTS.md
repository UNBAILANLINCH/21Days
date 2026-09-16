# 21Days — Codex 入口

开始项目任务前，读取并遵守 [共用项目约定](ai-docs/project-guide.md)。
按其中的路由读取当前任务相关规则和模块文档；不要一次加载全部文档。

## Codex 执行约定

- 九项项目技能已在 `.agents/skills/` 注册，入口引用 `.claude/skills/` 的同一份正文与脚本。可用 `$unity-mcp`、`$unity-test`、`$build`、`$new-feature`、`$review-change`、`$generate-doc`、`$project-lint`、`$unity-code-review`、`$evolution` 显式调用。
- `build`、`new-feature`、`review-change`、`unity-test` 保留原技能的显式调用策略；其余技能允许按任务描述选择。技能注册不等于 hooks 注册。

- 如果 `python` 不在 PATH，使用已安装的 uv 查找解释器：PowerShell 中 `$projectPython = uv python find`，再用 `& $projectPython <脚本> <参数>` 执行相同检查。

- 项目 Unity MCP 配置在 [.codex/config.toml](.codex/config.toml)。只有本会话工具可用且实际读取编辑器成功，才算连接完成。
- 共用细则保留在 `.claude/rules/` 原路径；Codex hooks 按该目录规则提示相关文档，必读清单复用 `.claude/hooks/required_reads.json`。
- `.claude/rules/model-routing.md` 的 Fable / opus / sonnet 派单策略只适用于 Claude。Codex 使用本会话可用模型及其执行规则。
- Codex hooks 已注册在 `.codex/hooks.json`；首次使用或定义变更后须在 `/hooks` 审查信任。具体行为与验证方式见 [.codex/hooks/README.md](.codex/hooks/README.md)。未信任或运行报错时，明确报告未启用，主动执行原有检查作为补充。
- 代码编辑使用 `apply_patch`，不要改用 shell 写文件绕过检查。必读文档用独立命令 `Get-Content -Raw -Encoding UTF8 -LiteralPath '文档相对路径'` 完整读取；输出截断时增加输出预算并重读，合并命令或部分读取不记入自动账本。
- 生成物写入会被拒绝。Codex 当前不支持前置 hook 的 ask 返回值，因此原本需确认的提交、推送、敏感设置修改会被阻止，提示用户手动执行；不通过关闭 hooks 绕过。
- 已信任 hooks 会在补丁完成后自动运行 C# lint；检查失败、工具路径未覆盖或 hooks 未信任时，手动运行 `python .claude/skills/project-lint/lint.py <文件>` 并报告实际结果。
- 交付前检查 diff、新资产配套 `.meta`、临时代码及实际测试结果；配置或文档改动后运行 `python .claude/skills/evolution/gc_scan.py`。缺少解释器或 Unity 连接时明确报告未完成的验证。
- 用户以自然语言要求测试、构建、审查或模块文档同步时，按共用约定中的对应流程文件执行。文件中的 `/命令` 表示流程引用，不代表 Codex 已注册该斜杠命令。
- 流程里的 `$ARGUMENTS` 使用用户实际任务替换；`Read/Edit/Write/Bash` 对应当前可用的读取、编辑和命令工具。客户端专属派单和私有记忆路径不移植；自动机制以 Codex 适配器的实际覆盖范围为准。
- 跨客户端需要保留的项目知识写入 `ai-docs/` 或 `PRP/`；不要把另一客户端的私有记忆当作已加载上下文。
