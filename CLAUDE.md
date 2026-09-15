# 21Days — Unity 2D 工程（Claude Code harness）

多人协作项目，当前阶段由框架负责人先搭框架层，玩法未定。Unity 2022.3.62f2 LTS，2D URP 模板。
开始项目任务前，读取并遵守 [共用项目约定](ai-docs/project-guide.md)。
项目硬规则与目录约定只维护在该文件；下面补充 Claude Code 的客户端行为。

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
