---
name: generate-doc
description: 从源码生成 / 同步玩法模块的文档三件套（module-guide、external-api、extension-guide），解决「代码改了文档没跟上」。配套 detect.py 在编辑运行时代码后提醒同步。
---

# generate-doc

## 解决什么

手写的模块文档一定会过时，而过时的文档比没有文档更糟 —— Agent 会按它做决定。
这个 skill 让文档**从源码生成、随源码同步**，并且只写「编辑这个模块之前必须知道的」，
不做实现细节的复述（那是代码的事，代码永远比文档新）。

`CLAUDE.md` 的知识检索路由里写着：编辑某玩法模块前必读它的 module-guide（钩子强制）。
这条规矩成立的前提，是 guide 确实是新的。

## 文档三件套

一个模块一个目录 `ai-docs/docs/modules/<模块小写>/`，三份各司其职，**不许互相重复**：

| 文件 | 给谁看 | 写什么 | 不写什么 |
| --- | --- | --- | --- |
| `<模块>-module-guide.md` | 要改这个模块内部的人 | 模块职责边界、运行时类的分工、数据流向、依赖方向、场景/预制体上的接线关系、已知约束 | 逐方法说明、实现细节 |
| `<模块>-external-api.md` | 别的模块要调它的人 | 对外公开的接口 / 事件 / ScriptableObject 资产，调用时机与前置条件 | 内部私有实现 |
| `<模块>-extension-guide.md` | 要在它基础上加东西的人 | 扩展点在哪、新增一种「XX」的标准步骤、不该从哪扩 | 重复 guide 的架构描述 |

新模块起步时先只写 guide（`status: seed`），另外两份等模块真有对外接口、真有人要扩了再补。
**没内容的文档不要建空壳** —— 空壳会被 `/gc` 当成有效目标，反而掩盖问题。

## frontmatter

三份文档都带：

```markdown
---
type: module-guide | external-api | extension-guide
module: <模块小写>
layer: runtime | editor | data
maturity: seed | stable | deprecated
---
```

`layer` 说明这份文档描述的代码在哪一层（对应 `project-root.md` 的 asmdef 依赖方向）；
`maturity` 让读的人知道能不能信：`seed` 表示骨架、可能不全，`stable` 表示跟得上代码，
`deprecated` 表示模块已废弃但文档暂留。

## 三种模式

| 入口 | 做什么 | 什么时候用 |
| --- | --- | --- |
| `/generate-doc <模块>` | 从源码通读一遍，生成完整三件套 | 模块第一次成型时；或文档烂到不如重写 |
| `/generate-doc sync <模块>` | 读源码与现有文档的差异，**增量改**已有文档 | 日常。改完代码、`detect.py` 提醒时 |
| `/generate-doc check <模块>` | 只报告文档与代码对不上的地方，**一个字不改** | 提交前自查；不确定要不要 sync 时先 check |

三种模式都**先读 `modules.json` 拿源码目录**，没登记的模块先登记再说。

`sync` 的纪律：**增量改，不整份重写**。重写会把人手工补进去的上下文（为什么这么设计、
踩过什么坑）冲掉，而那些恰恰是源码里读不出来的部分。

## 篇幅约束

- `module-guide` 150–400 行。超了说明在复述实现，砍掉。
- `external-api` / `extension-guide` 各 100 行以内。
- 一律中文；代码标识符英文；引用代码用 `path:line`。
- 表格优先于长段落 —— 读的人多半是在找某一条，不是在通读。

## 模块注册表

`modules.json`，键是模块小写名，值是 `src` / `docs` / `status`：

| status | 含义 | `/gc` 行为 |
| --- | --- | --- |
| `todo` | 登记了，文档还没开始写 | 不查文档目录 |
| `seed` | 只有 guide 骨架 | 查目录必须存在 |
| `ready` | 三件套齐备且跟得上代码 | 查目录必须存在 |

格式与示例见文件里的 `_说明` 与 `_示例` 字段。工程目前是空骨架，`modules` 为空，
第一个玩法模块落地时（`/new-feature` 第 1 步定范围之后）在这里登记。

## 配套钩子 detect.py

PostToolUse 形态：编辑 `Assets/_Project/Scripts/Runtime/<Module>/*.cs` 之后，
若该模块的 guide 已存在，提醒跑 `/generate-doc sync <模块>`。只提醒，从不阻断。

**默认没在 `settings.json` 里注册** —— 现在一个模块都没有，挂上只有噪音。
第一个模块的 guide 写出来之后，把这段加进 `.claude/settings.json` 的
`PostToolUse` 里 `matcher` 为 `Edit|Write|MultiEdit` 的那组 `hooks` 数组：

```json
{
  "type": "command",
  "command": "python \"$CLAUDE_PROJECT_DIR/.claude/skills/generate-doc/detect.py\"",
  "timeout": 10,
  "statusMessage": "detect 提醒同步模块文档"
}
```

加完用一次真实编辑验证它确实弹提醒；不弹就查 `modules.json` 里的 `src` 路径对不对。

手动跑：

```bash
echo '{"tool_input":{"file_path":"Assets/_Project/Scripts/Runtime/Player/PlayerMovement.cs"}}' | python .claude/skills/generate-doc/detect.py
```
