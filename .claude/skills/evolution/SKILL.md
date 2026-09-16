---
name: evolution
description: harness 进化层——/learn 把纠错与新约定沉淀成可复用资产，/gc 扫描 harness 自身的健康度。让这套 harness 能被持续改进，而不是搭完就腐化。
---

# evolution（进化层）

> harness 不是搭完就不动的。这一层管它自己的改进与体检。

## `/learn` — 经验沉淀

把本次会话里**被纠正的错误 / 用户的反馈 / 新形成的约定**落成资产。
关键是落到**正确的那一层**，落错层等于没落。

### 沉淀路由（从上往下试，能落上层就别落下层）

| 优先级 | 落到哪 | 什么样的经验 | 为什么在这一层 |
| --- | --- | --- | --- |
| 1 | `.claude/skills/project-lint/rules.json` | 能写成正则、机械可判的违规 | **程序化的拦截不依赖人记得**。写进文档还得指望下次有人读且读进去了；写成规则，下次一犯就当场 exit 2 |
| 2 | `ai-docs/pitfalls.md` | 要理解语义才能判、正则做不了的坑 | 现象 → 根因 → 正确做法 → 关联模块，四段式。给推理留线索，不是给机器判的 |
| 3 | `.claude/rules/<类型>.md` | 稳定成型的规范条款（命名、目录、依赖方向） | 钩子按文件类型自动注入，编辑对应文件时无需 Read 就在眼前 |
| 4 | `ai-docs/docs/modules/<模块>/` | 只对某个玩法模块成立的约定 | 跟着模块走，别污染全局 |
| 5 | 全局记忆 `~/.claude/projects/<本项目>/memory/` | 跨会话的用户偏好、项目动态（不是代码规则） | 不属于仓库内容，不该进 git |

**判断尺子**：这条经验下次能不能被机器当场拦下？能 → 第 1 层。
不能但属于「编辑这类文件前必须知道」→ 第 2/3 层。只对一个模块成立 → 第 4 层。
跟代码无关、是人的偏好 → 第 5 层。

### 落到第 1 层的写法

编辑 `rules.json` 加一条规则即可，字段说明见 `.claude/skills/project-lint/SKILL.md`。
加完**当场用违规样例跑一遍** CLI 验证能抓到，用干净样例验证不误报 —— 没验过的规则等于没加。

### 落到第 2 层的写法

`ai-docs/pitfalls.md` 一条一段：

```markdown
## <一句话标题>
- **现象**：具体表现（报错原文 / 行为异常），别写「有问题」
- **根因**：为什么会这样
- **正确做法**：怎么改，给代码片段
- **关联**：涉及的模块 / 规则文件 / 首次踩到的日期
```

## `/gc` — 健康度扫描

```bash
python .claude/skills/evolution/gc_scan.py
```

查六样：

1. `CLAUDE.md`、`README.md`、`.claude/`、`ai-docs/`、`docs/` 下 markdown 的相对链接目标是否存在
2. `generate-doc/modules.json` 里登记且 `status` 不是 `todo` 的文档目录是否存在
3. `.claude/settings.json` 里钩子引用的 `.py` / `.js` 脚本是否存在
4. 跑一遍钩子自测 `.claude/hooks/tests/run.py`，全绿才算过
5. 跑一遍工程静态不变量扫描 `invariants.py`，无违规才算过
6. `.claude/hooks/required_reads.json` 里的必读文件是否存在 —— **只提示不算失败**

前五样有失效就 exit 1 并逐条列出；全通过 exit 0。只读扫描，不改任何文件
（第 4 样起子进程跑测试，测试自己会收拾掉写出的缓存）。

第 4、5 样是两类「悄悄不生效」故障的**执行载体**：钩子坏掉不报错（判据写反、
提示不再注入、闸被绕开），跨文件不变量破了也不报错（引用断链、面板地址找不到、
Editor 代码混进包体）。两者从外面都看不出来，没有载体的检查跟没有检查一样。

第 6 样单独降级，是因为必读清单常常先于文档写好（先定「编辑这个模块前必须读它的 guide」，
文档随后补）。把「还没写」报成失败，只会逼人把清单删掉。

### 第 5 样：工程静态不变量（`invariants.py`）

```bash
python .claude/skills/evolution/invariants.py    # 也可独立跑；无违规时静默 exit 0
```

查六条**跨文件 / 跨资产**的约束——每一条都是 `project-lint` 的逐行正则天生够不着的：

| 查什么 | 依据 |
| --- | --- |
| asmdef 依赖方向（`Game.Core` 不引用 Runtime/Editor/Tests，`Game.Runtime` 不引用 Editor/Tests） | `project-root.md` #asmdef 依赖方向 |
| 平台宏只在 `Core/Platform/`（`#if UNITY_EDITOR` 放行，那是允许的） | `project-root.md` #平台差异只在框架层 |
| `using UnityEditor` 必须真的落在 `#if UNITY_EDITOR` **块内**（lint 那条只做文件级判断，漏这种） | `project-root.md` #asmdef 依赖方向 |
| 命名空间与目录一致（`Core/<X>/` → `Game.Core.<X>`；`Runtime/<M>/` → `Game.<M>`） | `architecture.md` #4 |
| `.meta` 配对（缺 meta 与孤儿 meta 两头都查） | `pitfalls.md` #.meta 没提交 |
| UI 面板的 Addressables 地址等于类名 | `architecture.md` #5.6 |

分工尺子：**一行之内判得完的归 `rules.json`，必须把整个仓库摊开才能判的归 `invariants.py`**，
两边不重复。误报出现两次就改判据或删掉那条，**不要加白名单**。

## harness 走歪了的信号与处理

| 信号 | 说明它歪在哪 | 怎么处理 |
| --- | --- | --- |
| `/gc` 失效引用开始变多 | 文件挪过位置、改过名，引用方没跟上 | 逐条修；改结构时顺手 `/gc` 一次 |
| lint 频繁误报，开始习惯性写 `// lint-ok:` | 规则收得太宽，或收了本不该机械判的东西 | 收紧 `exclude_patterns`，或直接删掉这条规则 —— 绕的路子形成后，真该拦那次也拦不住 |
| lint 从来不报，但同类坑还在犯 | 规则太窄，或该写规则的经验落到了文档层 | 回头看 `pitfalls.md`，把能程序化的提上去 |
| 同一个坑反复出现在对话里 | 经验压根没沉淀，或沉淀到了读不到的地方 | 按上面的路由重新落一次，并检查钩子有没有把它注入到位 |
| 某份规则 / 文档从没被读过 | 路由没连上，或者它根本没必要存在 | 要么接进 `knowledge-routing` / `required_reads`，要么删 |

处理顺序固定：**先 `/gc` 找失效引用 → 修 → 查最近的 harness 改动是否引入退化 → 必要时 `git revert`**。
harness 自身的改动同样受 `CLAUDE.md` 硬规则 4 管：攒在工作区，`/review-change` 列清单等审。

## 归档目录

| 目录 | 放什么 |
| --- | --- |
| `ai-docs/pitfalls.md` | 现役的坑，编辑前会被提示读 |
| `ai-docs/docs/catalog.md` | 知识层目录索引，新增文档在这里挂上 |
| `PRP/` | 复杂功能的 PRD / PRP / tasks，做完留档 |
| `evals/` | 行为回归用例：真派 agent 做一个自然任务，再机器判产出（`/run-evals`） |

## `/gc` 与 `/run-evals` 的分界

两件事都叫「回归」，但查的东西和载体都不一样，别混：

| | `/gc`（含 `invariants.py`） | `/run-evals` |
| --- | --- | --- |
| 查什么 | **当前仓库**静态自洽：引用、路径、不变量 | **AI 的行为**：同一个任务，改完规则之后做得对不对 |
| 要不要跑 AI | 不要，秒级纯扫描 | 要，每条用例派一个 subagent 真做一遍 |
| 载体 | 用户改完 harness 结构 / `/review-change` 之前主动跑 | 用户改完 `.claude/rules/` 或 `rules.json` 之后主动跑（`/learn` 收尾会问） |

改了规则只跑 `/gc` 是不够的：`/gc` 能证明规则文件**还在、链接没断**，
证明不了规则**被读进去了、并且改变了行为**。后者只有 `/run-evals` 能答。
