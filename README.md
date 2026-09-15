# 21Days

一个 Unity 2D 个人项目，外加一套自用的 **Claude Code harness**——让 AI 在这个工程里「每一刻刚好看到它需要的信息、调得动它需要的工具、拿得到它需要的反馈」。

工程本体目前是 2D URP 空骨架，**玩法未定**；仓库里现成的是 harness。第一个玩法模块落地后再建 `Assets/_Project/` 的完整结构。

## 环境

| 依赖 | 版本 / 用途 |
| --- | --- |
| Unity | **2022.3.62f2 LTS**，2D URP 模板（版本以 `ProjectSettings/ProjectVersion.txt` 为准） |
| uv | 提供 `uvx`，用来拉起 Unity MCP 服务端（`.mcp.json`） |
| Python 3 | 跑 `.claude/hooks/` 的钩子与 `project-lint` / `gc_scan` |
| Node | 跑 `.claude/hooks/guard.js`（写入 / 命令拦截） |

一次性接入步骤见 [`docs/ai-setup.md`](docs/ai-setup.md)。

## harness 五层

| 层 | 回答什么 | 在本仓库是 |
| --- | --- | --- |
| **能力层** | Agent 能做什么 | `.claude/skills/`：`unity-mcp`（操作编辑器）、`unity-test`（跑测试）、`new-feature`、`review-change` |
| **知识层** | Agent 知道什么 | `CLAUDE.md`（L1）+ `ai-docs/`：`docs/catalog.md` 总目录、`docs/modules/` 模块三件套、`pitfalls.md` 错误记忆 |
| **策略层** | 什么必须 / 禁止做 | `CLAUDE.md` 硬规则 + `.claude/rules/`（6 条，glob 自动注入）+ `.claude/skills/project-lint/`（C# 语义 lint）+ `.claude/hooks/`（7 个）+ `.claude/agents/code-reviewer.md` |
| **编排层** | 怎么组织执行 | `.claude/commands/`：`/dev` 统一入口 + PRP 四阶段 + `/generate-doc`，产物落 `PRP/<feature>/` |
| **进化层** | harness 自己怎么改进 | `/learn` 沉淀 · `/gc` 体检（`.claude/skills/evolution/gc_scan.py`）· `evals/` 行为回归 · `ai-shared/evolution/` 过程归档 |

数据流：上层调下层，不反向依赖。每层只回答一个关切，可以单独换掉而不动其它层。

## 快速上手

```
/dev <要做的事>              统一入口，按复杂度路由：简单直接做 / 中等 Plan / 复杂走 PRP
/new-feature <模块名>        新玩法模块：定范围 → 设计要点 → 实现 → 接线 → 验证 → 待审
/unity-test [EditMode|PlayMode] [过滤]    跑测试并汇报失败用例
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
│   ├── rules/                 策略层 6 条规则（frontmatter + glob 自动注入）
│   ├── commands/              编排 + 进化：/dev、PRP 四阶段、/generate-doc、/learn、/gc
│   ├── skills/                unity-mcp · unity-test · new-feature · review-change
│   │                          project-lint · generate-doc · unity-code-review · evolution
│   ├── agents/                code-reviewer 子代理（model: sonnet）
│   └── hooks/                 7 个生命周期钩子（说明见 hooks/README.md）
├── ai-docs/
│   ├── docs/catalog.md        知识总目录：三级加载 + 模块表 + 规则表
│   ├── docs/modules/          模块三件套（当前为空，随 /generate-doc 产生）
│   └── pitfalls.md            持久化错误记忆
├── ai-shared/evolution/       进化产物归档（signals/ 复盘 · weekly/ 小结）
├── evals/                     harness 行为回归用例
├── PRP/                       PRP 工作区（决策留痕入库）
├── docs/ai-setup.md           MCP 一次性接入
├── Assets/
│   ├── _Project/              自己的内容全放这儿（第一个模块落地时建）
│   │   ├── Scripts/Runtime/<Module>/   asmdef Game.Runtime
│   │   ├── Scripts/Editor/             asmdef Game.Editor
│   │   ├── Scripts/Tests/{EditMode,PlayMode}/
│   │   └── Prefabs/ Scenes/ Art/ Audio/ Data/
│   ├── Scenes/ Settings/      模板自带，原位不动
│   └── Plugins/               第三方（优先走 Package Manager）
├── Packages/ ProjectSettings/ Unity 工程配置（改动前先说明原因）
└── Library/ Temp/ Logs/ ...   Unity 生成物，已忽略，不手改
```

## 延伸阅读

- [`docs/ai-setup.md`](docs/ai-setup.md) —— Unity MCP 一次性接入与版本升级
- [`.claude/hooks/README.md`](.claude/hooks/README.md) —— 7 个钩子各自做什么、怎么调试
- [`ai-docs/docs/catalog.md`](ai-docs/docs/catalog.md) —— 不知道该读哪份文档时从这里找
- [`ai-docs/pitfalls.md`](ai-docs/pitfalls.md) —— 踩过的坑
- [`PRP/README.md`](PRP/README.md) · [`evals/README.md`](evals/README.md) —— 复杂功能流程 / 行为回归

> harness 方法论参考了一套通用的五层架构实践，这里按 Unity 工程做了适配：模块 = `Assets/_Project/Scripts/Runtime/<Module>/`，验证 = Unity 编译 + `/unity-test` + `project-lint`，编辑器操作走 MCP。
