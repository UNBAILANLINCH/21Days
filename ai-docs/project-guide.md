# 21Days — 共用项目约定

Claude Code 与 Codex 共用此文件，入口分别为 `CLAUDE.md` 与 `AGENTS.md`。
Unity 版本以 `ProjectSettings/ProjectVersion.txt` 为准。

## 硬规则（违反即返工）

1. **生成物不手改**：`Library/ Temp/ Logs/ obj/ UserSettings/`、`*.meta`、`*.csproj/*.sln`、`packages-lock.json`。Claude 钩子会拒绝，被拒就换做法，不绕。
2. **不带本地标识**：工程内任何文件不出现本机用户名、绝对路径、邮箱。公司名保持 `DefaultCompany`。
3. **改 `ProjectSettings/`、`Packages/manifest.json` 先说明为什么**，Claude 钩子会弹确认。
4. **提交前必审**：改动攒在工作区，收敛后 按审查流程列清单，用户逐次明确授权才 `git commit`；不 push 除非明说。提交信息按 `docs/commit-convention.md`，不带任何 AI 署名（Claude 钩子会拒）。
5. **护栏挡住时不拆护栏**：lint / 钩子拦下来先看理由；确属误报，在那一行写 `// lint-ok: <理由>` 放行，理由必须写。

## 目录约定（细则见 `.claude/rules/project-root.md`）

自己的内容全放 `Assets/_Project/`；模板留下的 `Assets/Scenes/`、`Assets/Settings/` 原位不动；第三方包走 Package Manager 或 `Assets/Plugins/`。

```
Assets/_Project/
  Scripts/Core/                框架层(零玩法)  asmdef Game.Core
  Scripts/Runtime/<Module>/   游戏逻辑        asmdef Game.Runtime
  Scripts/Editor/             编辑器工具      asmdef Game.Editor
  Scripts/Tests/EditMode/     纯逻辑测试      asmdef Game.Tests.EditMode
  Scripts/Tests/PlayMode/     运行时测试      asmdef Game.Tests.PlayMode
  Scripts/Tests/Showcase/<Module>/   回放验证场景   asmdef Game.Tests.Showcase
  Prefabs/ Scenes/(Verify/ 放验证场景) Art/(Sprites Animations Materials) Audio/ Data/(ScriptableObject)
```

框架层结构与 asmdef 已建立（设计见 docs/architecture.md）；玩法模块目录随新玩法模块流程落地时再建。加能力的顺序：**先复用 → 再扩展已有文件 → 最后才新建**，新建要在文件头写明前两步为何不行。

## 按需读取

下面的细则和流程保留原路径，两端读取同一份正文。hooks 自动运行、模型派单和私有记忆等客户端行为以各自入口为准。

| 场景 | 文件 |
| --- | --- |
| 目录、依赖、新增文件 | [.claude/rules/project-root.md](../.claude/rules/project-root.md) |
| 框架层设计与各服务契约 | [docs/architecture.md](../docs/architecture.md) |
| 开发者操作手册 | [docs/developer-guide.md](../docs/developer-guide.md) |
| 首次上手与环境自检 | [.claude/skills/onboard/SKILL.md](../.claude/skills/onboard/SKILL.md) |
| 模块开发流程与完成标准 | [docs/module-dev-spec.md](../docs/module-dev-spec.md) |
| 模块回放验证 | [.claude/skills/verify-module/SKILL.md](../.claude/skills/verify-module/SKILL.md)、[.claude/rules/module-verify.md](../.claude/rules/module-verify.md) |
| 修改 C# | [.claude/rules/csharp-code.md](../.claude/rules/csharp-code.md) |
| 场景、预制体、SO、材质、asmdef | [.claude/rules/unity-assets.md](../.claude/rules/unity-assets.md) |
| Unity 测试规范 | [.claude/rules/unity-tests.md](../.claude/rules/unity-tests.md) |
| 编辑器操作 | [.claude/skills/unity-mcp/SKILL.md](../.claude/skills/unity-mcp/SKILL.md) |
| 新玩法模块 | [.claude/skills/new-feature/SKILL.md](../.claude/skills/new-feature/SKILL.md) |
| 执行测试 | [.claude/skills/unity-test/SKILL.md](../.claude/skills/unity-test/SKILL.md) |
| 本机构建 | [.claude/skills/build/SKILL.md](../.claude/skills/build/SKILL.md) |
| 审查改动 | [.claude/skills/review-change/SKILL.md](../.claude/skills/review-change/SKILL.md) |
| 模块级只读审查 | [.claude/skills/unity-code-review/SKILL.md](../.claude/skills/unity-code-review/SKILL.md) |
| 项目 lint 与规则维护 | [.claude/skills/project-lint/SKILL.md](../.claude/skills/project-lint/SKILL.md) |
| 经验沉淀与健康检查 | [.claude/skills/evolution/SKILL.md](../.claude/skills/evolution/SKILL.md) |
| 模块文档生成 / 同步 | [.claude/skills/generate-doc/SKILL.md](../.claude/skills/generate-doc/SKILL.md) |
| 复杂功能 | [PRP/README.md](../PRP/README.md) |
| 知识检索 | [docs/catalog.md](docs/catalog.md)、[pitfalls.md](pitfalls.md) |

修改模块前必读已存在的 `ai-docs/docs/modules/<模块>/<模块>-module-guide.md`；跨模块调用读 external-api，扩展模块读 extension-guide。

## 检查与协作

- C# 改完运行 `python .claude/skills/project-lint/lint.py <文件.cs>`。Unity 可用时刷新编译、读取控制台并执行相关测试；lint 不代替编译和测试。
- 配置或文档改动后运行 `python .claude/skills/evolution/gc_scan.py`。
- 场景与预制体通过 Unity MCP 修改，不手改 YAML。没有连接时明确报告；不擅自关闭 Unity。
- 中文回复、注释和文档；英文代码标识符。
- 跨客户端知识保存在 `ai-docs/`、复杂功能决策保存在 `PRP/`。
- 两端可交替使用同一分支；避免同时改同一文件或同时写入 Unity 编辑器，保留另一方的未提交改动。
