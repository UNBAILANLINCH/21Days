# 21Days — Unity 2D 工程（Claude Code harness）

多人协作项目，当前阶段由框架负责人先搭框架层，玩法未定。Unity 2022.3.62f2 LTS，2D URP 模板。
本文件只放最核心的禁令、路由和入口；细则在 `.claude/rules/`（按文件类型自动注入）与 `ai-docs/`（按需读）。

## 硬规则（违反即返工）

1. **生成物不手改**：`Library/ Temp/ Logs/ obj/ UserSettings/`、`*.meta`、`*.csproj/*.sln`、`packages-lock.json`。钩子会拒绝，被拒就换做法，不绕。
2. **不带本地标识**：工程内任何文件不出现本机用户名、绝对路径、邮箱。公司名保持 `DefaultCompany`。
3. **改 `ProjectSettings/`、`Packages/manifest.json` 先说明为什么**，钩子会弹确认。
4. **提交前必审**：改动攒在工作区，收敛后 `/review-change` 列清单，用户逐次明确授权才 `git commit`；不 push 除非明说。提交信息按 `docs/commit-convention.md`，不带任何 AI 署名（钩子会拒）。
5. **护栏挡住时不拆护栏**：lint / 钩子拦下来先看理由；确属误报，在那一行写 `// lint-ok: <理由>` 放行，理由必须写。

## 工作纪律（对 AI，违反即返工）

1. **能力面内的事不外推**：修法已定且工具够得着，就直接做完，不要列 ABCD 选项让用户选、不要用「有风险 / 需人工确认 / 属于后续排期」把自己能做能验的事包装成推给用户。问用户只在三种情况：不可逆操作、穷尽探索仍无解、真正的主观偏好。
2. **「做不了」必须实测过再说**：判断某个 API / 工具 / 路径不存在或不可用，先跑一次实测（MCP `read_console`、`execute_code`、跑一条命令）。读源码猜出来的是猜测，跑过的才是证据。没实测就下结论等同于编造。
3. **subagent 自报通过不算通过**：派单回来说「测试全过」「编译零错误」时，主窗口自己复跑一次 `run_tests` / `read_console` 再采信。同理，凡声称某文件已生成 / 已更新，自己 `stat` 或读一眼。自报结果和自报证据是同一等级的东西，都要独立复核。
4. **新增自动化必须锚定载体**：往 harness 里加会自己跑起来的东西（钩子、定时扫描、生成物刷新），文件头要写明执行载体、5 秒可证伪的状态锚点、退场条件，缺一不上线。细则见 [`.claude/rules/harness-authoring.md`](.claude/rules/harness-authoring.md)。

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

框架层结构与 asmdef 已建立（设计见 docs/architecture.md）；玩法模块目录随 `/new-feature` 落地时再建。加能力的顺序：**先复用 → 再扩展已有文件 → 最后才新建**，新建要在文件头写明前两步为何不行。

## 派单模型路由（主窗口纪律，细则 `.claude/rules/model-routing.md`）

- 主窗口（Fable）只做方案设计、任务拆解、波次验收；工程一律派 subagent。
- 工程任务（跨文件实现、改接口、根因不明的调试）→ `opus`；机械活（单点执行、批量修改、检索摘要）→ `sonnet`。
- **每次派单显式传 `model`**，subagent 永不派成 `fable`。

## 知识检索路由（只读当前任务那一份）

| 场景 | 去哪 |
| --- | --- |
| 编辑 `.cs` / 场景预制体 / 测试 | `.claude/rules/` 对应规则（钩子自动提示） |
| 编辑某玩法模块 | `ai-docs/docs/modules/<模块>/<模块>-module-guide.md`（有则编辑前必读，钩子强制） |
| 怕重踩坑 | `ai-docs/pitfalls.md` |
| 操作编辑器 / 跑测试 / 看控制台 | `.claude/skills/unity-mcp/SKILL.md`（MCP 已接，`/mcp` 看状态） |
| 框架层设计与各服务契约 | `docs/architecture.md` |
| 给其他开发者的操作手册 | `docs/developer-guide.md` |
| 不确定从哪找 | `ai-docs/docs/catalog.md` |

跨会话记忆（用户偏好、项目动态）在全局 `~/.claude/projects/<本项目>/memory/`，按需读写。

## 工作流入口

| 命令 | 用途 |
| --- | --- |
| `/onboard` | 新开发者首次上手：环境自检、人工步骤确认、MCP 连通验证、该读什么 |
| `/dev <任务>` | 统一入口：简单直接做 / 中等先出方案 / 复杂走 PRP |
| `/new-feature <模块名>` | 新玩法模块：定范围 → 设计要点 → 实现 → 接线 → 验证 → 待审 |
| `/unity-test [EditMode\|PlayMode] [过滤]` | 跑测试并汇报失败用例 |
| `/verify-module <模块> [--manual]` | 在编辑器里跑模块回放场景，出报告，视觉验收交开发者 |
| `/build [Windows\|Android] [版本]` | 本机出包（编辑器须关闭），失败摘日志前几条错误 |
| `/review-change` | 列改动清单待审，授权后提交 |
| `/refine-prd` → `/generate-prp` → `/validate-prp` → `/execute-prp` | PRP 四阶段（复杂功能） |
| `/generate-doc` · `/learn` · `/gc` | 模块文档同步 · 经验沉淀 · harness 健康度扫描 |

## 验证与工具

- 编译错误：Unity MCP `read_console`；编辑器没开就让用户看控制台。
- 项目 lint：保存 `.cs` 时钩子自动跑；手动 `python .claude/skills/project-lint/lint.py <file.cs>`。
- 健康度：`python .claude/skills/evolution/gc_scan.py`。
- 模块回放验证：`/verify-module`，规范见 [`docs/module-dev-spec.md`](docs/module-dev-spec.md)。
- 打包：本机 `scripts/build.ps1`（编辑器须关闭），CI 见 [`docs/ci-setup.md`](docs/ci-setup.md)。
- **Bash 不写 `cd … &&`**：工作目录已在工程根，路径写绝对路径或相对工程根。带 `cd` 后权限检查算不出相对路径落在哪，会因 `.env` 的 Read deny 规则弹确认，自动模式也拦不住。
- **中文优先**：回复、代码注释、文档、钩子与 lint 的提示信息一律中文；代码标识符（类名、变量名）仍用英文。引用代码用 `path:line`。
