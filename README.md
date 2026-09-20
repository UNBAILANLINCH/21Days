# 21Days

一个 Unity 2D 多人协作项目，支持 **Claude Code 与 Codex 双入口**，共用项目规则和知识文档。

Claude 从 [CLAUDE.md](CLAUDE.md)、Codex 从 [AGENTS.md](AGENTS.md) 读取 [共用项目约定](ai-docs/project-guide.md)。Codex MCP 配置在 [.codex/config.toml](.codex/config.toml)。
下面的斜杠命令、模型派单和自动 hooks 描述属于 Claude；Codex 使用自然语言触发共用流程并主动执行检查。首次连接与验证见 [AI 接入说明](docs/ai-setup.md)。

**当前状态**：框架层（`Game.Core`）已建成——启动与依赖注入、状态流、UI 分层与面板栈、音频、资源、配置表、存档、输入、定时器、对象池、日志、平台隔离，另有一个端到端的示例模块 `Sample` 供照抄。**玩法未定**，第一个真玩法模块用 `/new-feature` 起。设计定稿见 [`docs/architecture.md`](docs/architecture.md)。

## 你该读哪些文档

按你在项目里干什么找这一行，读完「必读」就能开工，其余按需翻。

| 你是谁 | 必读（按顺序） | 需要时再看 |
| --- | --- | --- |
| **程序** | [`docs/developer-guide.md`](docs/developer-guide.md) 第 1～4 章（环境、上手、目录与程序集、提交规范）→ [`docs/architecture.md`](docs/architecture.md) 第 3 节（分层）→ 本文「快速上手」 | 写玩法模块看开发手册第 7 章 + [`Runtime/Sample/`](Assets/_Project/Scripts/Runtime/Sample/) 样板；各服务怎么用看第 6 章；查日志定位问题看 [`docs/telemetry.md`](docs/telemetry.md)；[`ai-docs/pitfalls.md`](ai-docs/pitfalls.md) 踩过的坑 |
| **策划** | [`docs/designer-guide.md`](docs/designer-guide.md) 全文（改数值、加道具、加表、报错怎么查） | 存档字段与版本迁移看开发手册第 9 章；要加新表前先找程序对一下 |
| **美术** | [`docs/artist-guide.md`](docs/artist-guide.md) 全文（资源放哪、导入规则、做 UI 面板、自查） | 面板与程序的分工看开发手册第 11 章；音频看第 12 章 |
| **用 Claude Code 协作的人** | [`CLAUDE.md`](CLAUDE.md)（会话自动加载）→ [`docs/ai-workflow.md`](docs/ai-workflow.md) | 不知道读哪份时去 [`ai-docs/docs/catalog.md`](ai-docs/docs/catalog.md)；钩子行为看 [`.claude/hooks/README.md`](.claude/hooks/README.md) |
| **第一次拉到这个工程的任何人** | 在 Claude Code 里跑 `/onboard`，它会带着装环境、连编辑器、跑一次测试 | 不用 Claude Code 就照开发手册第 1 章手动装 |

三份角色文档的关系：**开发手册**是全量参考，策划与美术两份是各自视角的操作手册，只讲你真会碰到的部分，需要细节时会指回开发手册的对应章节。[`docs/architecture.md`](docs/architecture.md) 记的是「为什么这么设计」，改框架前读它。

## 环境

| 依赖 | 版本 / 用途 |
| --- | --- |
| Unity | **2022.3.62f2 LTS**，2D URP 模板（版本以 `ProjectSettings/ProjectVersion.txt` 为准） |
| uv | 提供 `uvx`，用来拉起 Unity MCP 服务端（`.mcp.json`） |
| Python 3 | 跑 `.claude/hooks/` 的钩子与 `project-lint` / `gc_scan` |
| Node | 跑 `.claude/hooks/guard.js`（写入 / 命令拦截） |

一次性接入步骤见 [`docs/ai-setup.md`](docs/ai-setup.md)（AI 协作）；打包见 [`docs/ci-setup.md`](docs/ci-setup.md)（本机出包用法；CI 已搁置，含根因）。

## harness 五层

| 层 | 回答什么 | 在本仓库是 |
| --- | --- | --- |
| **能力层** | Agent 能做什么 | `.claude/skills/`：`onboard`（新人上手）、`unity-mcp`（操作编辑器）、`unity-test`、`build`、`new-feature`、`review-change` 等，目录里有什么就是什么 |
| **知识层** | Agent 知道什么 | `CLAUDE.md`（L1）+ `ai-docs/`：`docs/catalog.md` 总目录、`docs/modules/` 模块三件套、`pitfalls.md` 错误记忆 |
| **策略层** | 什么必须 / 禁止做 | `CLAUDE.md` 硬规则 + `.claude/rules/`（按 glob 自动注入）+ `.claude/skills/project-lint/`（C# 语义 lint）+ `.claude/hooks/`（注册明细在 `settings.json`）+ `.claude/agents/code-reviewer.md` |
| **编排层** | 怎么组织执行 | `.claude/commands/`：`/dev` 统一入口 + PRP 四阶段 + `/generate-doc`，产物落 `PRP/<feature>/` |
| **进化层** | harness 自己怎么改进 | `/learn` 沉淀 · `/gc` 体检（`.claude/skills/evolution/gc_scan.py`）· `evals/` 行为回归 · `ai-shared/evolution/` 过程归档 |

数据流：上层调下层，不反向依赖。每层只回答一个关切，可以单独换掉而不动其它层。

## 快速上手

```
/dev <要做的事>              统一入口，按复杂度路由：简单直接做 / 中等 Plan / 复杂走 PRP
/new-feature <模块名>        新玩法模块：定范围 → 设计要点 → 实现 → 接线 → 验证 → 待审
/unity-test [EditMode|PlayMode] [过滤]    跑测试并汇报失败用例
/build [Windows|Android] [版本]           本机出包（Unity 编辑器须关闭），CI 出包见 docs/ci-setup.md
/review-change               列改动清单待审，授权后才提交
/generate-doc [sync|check] <模块>         生成 / 同步模块文档三件套
/learn [教训]                沉淀经验：pitfalls / lint 规则 / 记忆 / 模块文档
/gc                          harness 健康度扫描，报告失效引用
```

复杂功能走 PRP 四阶段：

```
/refine-prd <feature> <需求>  →  /generate-prp <feature>
  →  /validate-prp <feature>  →  /execute-prp <feature>
```

写代码时钩子会自动干活：编辑 `.cs` 前提示适用规则与模块文档，保存后自动跑 `project-lint`，写生成物直接拒绝，`git commit` / `git push` 弹确认。**被护栏挡住先看理由，不拆护栏。**

### 验证命令

```bash
# 钩子与脚本语法全绿
find .claude -name '*.py' -exec python -c "import py_compile,sys;py_compile.compile(sys.argv[1],doraise=True)" {} \;
node --check .claude/hooks/guard.js

# JSON 合法
python -c "import json;[json.load(open(f,encoding='utf-8')) for f in ['.claude/settings.json','.mcp.json','.claude/skills/project-lint/rules.json']]"

# harness 无失效引用
python .claude/skills/evolution/gc_scan.py

# 手动跑项目 lint
python .claude/skills/project-lint/lint.py <某个文件.cs>
```

## 目录结构

```
<项目根>/
├── CLAUDE.md                  L1 常驻上下文：硬规则 + 目录约定 + 路由 + 入口
├── README.md                  本文档
├── .mcp.json                  Unity MCP 服务端（项目级配置）
├── .claude/
│   ├── settings.json          权限白名单 + 钩子注册
│   ├── rules/                 策略层规则（frontmatter + glob 自动注入）
│   ├── commands/              编排 + 进化：/dev、PRP 四阶段、/generate-doc、/learn、/gc
│   ├── skills/                onboard · unity-mcp · unity-test · build · new-feature
│   │                          review-change · verify-module · project-lint
│   │                          generate-doc · unity-code-review · evolution
│   ├── agents/                code-reviewer 子代理（model: sonnet）
│   └── hooks/                 生命周期钩子（说明见 hooks/README.md）
├── ai-docs/
│   ├── docs/catalog.md        知识总目录：三级加载 + 模块表 + 规则表
│   ├── docs/modules/          模块三件套（sample/ 是样例，新模块照它写）
│   └── pitfalls.md            持久化错误记忆
├── ai-shared/evolution/       进化产物归档（signals/ 复盘 · weekly/ 小结）
├── evals/                     harness 行为回归用例
├── PRP/                       PRP 工作区（决策留痕入库）
├── scripts/build.ps1          本机打包入口（调 Editor 的 BuildScript，编辑器须关闭）
├── docs/ai-setup.md           MCP 一次性接入
├── docs/ci-setup.md           本机打包用法；CI 为何搁置（激活 401）与重做前置
├── Tables/                    配置表源头：Excel 源表 + Luban 配置（策划改这儿）
├── scripts/gen-tables.ps1     配置表生成（也可用 Unity 菜单 21Days/配置表/生成）
├── Assets/
│   ├── _Project/              自己的内容全放这儿
│   │   ├── Scripts/Core/               框架层，零玩法  asmdef Game.Core
│   │   ├── Scripts/Runtime/<Module>/   玩法模块        asmdef Game.Runtime
│   │   ├── Scripts/Editor/             编辑器工具      asmdef Game.Editor
│   │   ├── Scripts/Tests/{EditMode,PlayMode,Showcase}/
│   │   ├── Data/                       ScriptableObject 配置 + Config/ 生成的表数据
│   │   └── Prefabs/ Scenes/ Art/ Audio/
│   ├── Scenes/ Settings/      模板自带，原位不动
│   └── Plugins/               第三方（优先走 Package Manager）
├── Packages/ ProjectSettings/ Unity 工程配置（改动前先说明原因）
├── Builds/                    本机打包产物（已忽略，不进仓库）
└── Library/ Temp/ Logs/ ...   Unity 生成物，已忽略，不手改
```

## 延伸阅读

- [`docs/ai-workflow.md`](docs/ai-workflow.md) —— **AI 工作流总纲**：为什么要有这套、五层怎么交互、日常怎么用、会自动发生什么
- [`docs/ai-setup.md`](docs/ai-setup.md) —— Unity MCP 一次性接入与版本升级
- [`docs/ci-setup.md`](docs/ci-setup.md) —— CI 一次性配置（三个 secret）、怎么触发、本机打包与 CI 的关系
- [`docs/architecture.md`](docs/architecture.md) —— 框架层设计定稿：选型、asmdef 分层、目录、各服务契约
- [`docs/developer-guide.md`](docs/developer-guide.md) —— 程序的操作手册（15 章，从装环境到打包）
- [`docs/designer-guide.md`](docs/designer-guide.md) —— 策划：改数值、加道具、加表、报错怎么查
- [`docs/cultural-guide.md`](docs/cultural-guide.md) —— 文策：相关考据参考安置处
- [`docs/artist-guide.md`](docs/artist-guide.md) —— 美术：资源放哪、导入规则、做 UI 面板、交活前自查
- [`.claude/hooks/README.md`](.claude/hooks/README.md) —— 各个钩子做什么、怎么调试
- [`ai-docs/docs/catalog.md`](ai-docs/docs/catalog.md) —— 不知道该读哪份文档时从这里找
- [`docs/history/`](docs/history/) —— 建设纪要：某阶段做了什么、当时怎么取舍、**刻意没做什么**
- [`ai-docs/pitfalls.md`](ai-docs/pitfalls.md) —— 踩过的坑
- [`PRP/README.md`](PRP/README.md) · [`evals/README.md`](evals/README.md) —— 复杂功能流程 / 行为回归

> harness 方法论参考了一套通用的五层架构实践，这里按 Unity 工程做了适配：模块 = `Assets/_Project/Scripts/Runtime/<Module>/`，验证 = Unity 编译 + `/unity-test` + `project-lint`，编辑器操作走 MCP。
