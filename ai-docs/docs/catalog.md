# 知识总目录（catalog）

> 不确定该读哪份文档时，从这里找入口。遵循**渐进式加载**：只读当前任务相关的那一份，不要一次性全读。

## 三级加载（细则 `.claude/rules/knowledge-routing.md`）

| 层级 | 加载时机 | 内容 |
| --- | --- | --- |
| L1 全局 | 会话启动，常驻 | `CLAUDE.md` —— 硬规则、目录约定、派单路由、入口表 |
| L2 模式匹配 | 编辑匹配的文件时自动注入 | `.claude/rules/*.md`（按 frontmatter 的 glob，由 `knowledge-routing` 钩子提示） |
| L3 模块级 | 编辑某玩法模块**之前** | `ai-docs/docs/modules/<模块>/` 三件套（guide 存在时 `required-reads` 钩子强制先读） |

## 模块文档（三件套）

| 模块 | 状态 | 文档目录 |
| --- | --- | --- |
| Sample | 三件套齐备（**同时是模块文档的样例**） | [`modules/sample/`](modules/sample/sample-module-guide.md) |

Sample 是端到端跑通框架每一层的样板模块（配置表 → 纯 C# 规则 → 意图 → 状态 → 面板 → 场景 → 注册 → 测试）。
新玩法模块照它的形状写，文档照它的三件套写。**模块文档随 `/new-feature` 落地模块、`/generate-doc <模块>` 生成而产生**，产生后在本表补一行。

每个模块三件套（命名与职责见 [`modules/README.md`](modules/README.md)）：

- `<模块>-module-guide.md` —— 职责边界、内部结构、核心数据、生命周期、接线要求（**编辑前必读**）
- `<模块>-external-api.md` —— 公开接口签名、调用约束、禁止事项（**跨模块调用时查**）
- `<模块>-extension-guide.md` —— 扩展点与扩展模式（**要给这个模块加东西时看**）

## 规则（策略层，`.claude/rules/`，共 9 条）

| 规则 | 加载 | 一句话 |
| --- | --- | --- |
| [`project-root.md`](../../.claude/rules/project-root.md) | 常驻 | 目录约定、asmdef 依赖方向（Runtime 不引用 Editor / Tests）、生成物边界、加能力的顺序（复用 → 扩展 → 新建）。 |
| [`knowledge-routing.md`](../../.claude/rules/knowledge-routing.md) | 常驻 | 三级渐进加载与决策树：什么场景读哪一份，怎么把上下文压到最小。 |
| [`model-routing.md`](../../.claude/rules/model-routing.md) | 常驻 | 派单模型路由：主窗口只做设计 / 拆解 / 验收，工程派 `opus`、机械派 `sonnet`，每次显式传 `model`。 |
| [`csharp-code.md`](../../.claude/rules/csharp-code.md) | `**/*.cs` | Unity C# 规范：命名、序列化暴露面（不用 public 字段）、生命周期、每帧路径的性能反模式。 |
| [`unity-assets.md`](../../.claude/rules/unity-assets.md) | 场景 / 预制体 / SO / asmdef / mat 等 | 资产改动走 MCP、预制体优先、SO 配置只读、`.meta` 随资产一起提交。 |
| [`unity-tests.md`](../../.claude/rules/unity-tests.md) | `Assets/_Project/Scripts/Tests/**` | EditMode 优先、测试命名、TearDown 清理、怎么跑（MCP / batchmode 二选一）。 |
| [`harness-authoring.md`](../../.claude/rules/harness-authoring.md) | `.claude/**` | 往 harness 加东西的硬要求：载体锚定、不建伴生清单、资产归哪一层、规则与文档的分界。 |
| [`hook-injection-style.md`](../../.claude/rules/hook-injection-style.md) | `.claude/hooks/**` | 钩子怎么对模型说话：第三人称事实陈述、只在写操作注入、同会话去重、阻断要克制、成功静默失败冗余。 |
| [`module-verify.md`](../../.claude/rules/module-verify.md) | `Scripts/Tests/Showcase/**`、`Scenes/Verify/**` | 模块回放验证（Showcase）：目录命名、作者 API、编写约束、与快测试的分工、模块完成定义 DoD。 |

## 其它入口

| 要什么 | 去哪 |
| --- | --- |
| 框架层设计定稿、选型理由、各服务契约 | [`docs/architecture.md`](../../docs/architecture.md) |
| 给其他开发者的操作手册 | [`docs/developer-guide.md`](../../docs/developer-guide.md) |
| 新开发者首次上手（环境自检、MCP 连通、该读什么） | [`.claude/skills/onboard/SKILL.md`](../../.claude/skills/onboard/SKILL.md)（`/onboard`） |
| 策划怎么改数值 / 加道具 / 加配置表 | [`docs/designer-guide.md`](../../docs/designer-guide.md) |
| 美术怎么放资源 / 导入规则 / 做 UI 面板 | [`docs/artist-guide.md`](../../docs/artist-guide.md) |
| 这套 AI 工作流本身：五层怎么交互、日常怎么用、新增东西放哪层 | [`docs/ai-workflow.md`](../../docs/ai-workflow.md) |
| 某阶段做了什么、当时怎么取舍、刻意没做什么 | [`docs/history/`](../../docs/history/) 下按日期的建设纪要 |
| 踩过的坑，别重踩 | [`../pitfalls.md`](../pitfalls.md) |
| 操作编辑器 / 建物体 / 跑测试 / 看控制台 | [`.claude/skills/unity-mcp/SKILL.md`](../../.claude/skills/unity-mcp/SKILL.md)（MCP 已接，`/mcp` 看状态） |
| MCP 一次性接入步骤、钩子总述 | [`docs/ai-setup.md`](../../docs/ai-setup.md) |
| 一个模块从定范围到待审的完整流程、DoD、怎么写 Showcase | [`docs/module-dev-spec.md`](../../docs/module-dev-spec.md) |
| 跑模块回放、看报告与截图、节奏菜单 | [`.claude/skills/verify-module/SKILL.md`](../../.claude/skills/verify-module/SKILL.md)（`/verify-module`，编辑器须打开） |
| 埋点契约：日志行格式、该埋什么 / 不该埋什么、开关与开销 | [`docs/telemetry.md`](../../docs/telemetry.md) |
| 从日志倒推事故根因，出诊断报告 | [`.claude/skills/telemetry/SKILL.md`](../../.claude/skills/telemetry/SKILL.md)（`/analyze-telemetry`） |
| 给一个模块补齐埋点（扫候选点 → 逐条判断 → 待审） | [`.claude/skills/instrument-module/SKILL.md`](../../.claude/skills/instrument-module/SKILL.md)（`/instrument-module <模块>`） |
| 本机出包（Windows / Android）怎么跑、失败怎么读日志 | [`.claude/skills/build/SKILL.md`](../../.claude/skills/build/SKILL.md)（`/build`，编辑器须关闭） |
| CI 为什么搁置（激活 401 根因）、本机出包怎么跑 | [`docs/ci-setup.md`](../../docs/ci-setup.md) |
| 钩子各自做什么、怎么调试 | [`.claude/hooks/README.md`](../../.claude/hooks/README.md) |
| 项目 lint 规则清单 | [`.claude/skills/project-lint/`](../../.claude/skills/project-lint/) |
| 跨会话记忆（用户偏好、项目动态） | 全局 `~/.claude/projects/<本项目>/memory/`（目录名由 Claude Code 按项目路径自动生成） |
| 复杂功能的决策留痕 | [`PRP/README.md`](../../PRP/README.md) |
| harness 行为回归用例 | [`evals/README.md`](../../evals/README.md) |
| 跑行为 eval（改完规则验 AI 行为有没有真变） | [`.claude/skills/run-evals/SKILL.md`](../../.claude/skills/run-evals/SKILL.md)（`/run-evals`） |
| 工程跨文件静态不变量（asmdef 方向 / 平台宏 / 命名空间 / `.meta` / UI 地址） | [`.claude/skills/evolution/invariants.py`](../../.claude/skills/evolution/invariants.py)（`/gc` 第 5 项自动跑） |

## 三类资产的区别（别混）

- **Rules**（`.claude/rules/`）告诉 AI「**不要做什么**」—— 约束。
- **Docs**（`ai-docs/`）告诉 AI「**这是什么**」—— 知识。
- **Skills / Commands**（`.claude/skills/`、`.claude/commands/`）告诉 AI「**怎么做**」—— 流程与模板。

写新内容前先想清楚它是哪一类，放错地方就加载不到。
