---
name: onboard
description: 新开发者首次上手引导——环境自检、人工步骤逐项确认、MCP 连通验证、该读什么
disable-model-invocation: true
---

# /onboard

新加入的开发者第一次拉下工程后用。七步走完，每步都有明确的「算过」标准；哪步卡住就停在那步说清楚，不要跳过硬闯，也不要替开发者做只有他能做的事（装软件、点编辑器菜单、跑要管理员权限的安装脚本）。

## 0. 安装必要环境

先问开发者：这是不是一台干净机器（没装过 Unity/Git/Python 这些）。

- **是**：把 `docs/developer-guide.md` 第 1 章的安装顺序清单（表格 + 1.1～1.8 小节）给开发者，推荐先跑一键脚本：
  ```
  powershell -ExecutionPolicy Bypass -File .claude/skills/onboard/install_env.ps1
  ```
  提醒两点：要用**管理员权限**打开的 PowerShell；装完要**重开一个新终端**再往下走（winget 新装的工具不会立刻进当前终端的 PATH）。脚本不装 Unity 编辑器（§1.2）与 Claude Code（§1.7），这两项要开发者手动完成，逐项问「装好了吗」确认。
- **不是**：跳过本步，直接进第 1 步自检；自检里每个 `[失败]` 项，把脚本给的修法和它指向的手册小节转述给开发者，让他处理完重跑自检，直到没有 `[失败]` 为止。

**Claude 不替开发者执行 `install_env.ps1`**——它要管理员权限、会往系统装东西，只能由开发者自己跑；Claude 只给命令、解释输出、确认结果。唯一例外是 `-DryRun`：这个只打印将执行的命令、不真装东西，Claude 可以自己跑来预览会装什么。

## 1. 环境自检

```
python .claude/skills/onboard/check_env.py
```

把输出的每一行（`[通过]`/`[失败]`/`[提示]` + 检查项 + 说明）原样贴给开发者，不要摘抄或改写。

- 有 `[失败]`：**不往下走**。挑出失败项，把脚本自带的修法转述给开发者，让他处理完重跑这条命令，直到没有 `[失败]` 为止。
- 只有 `[提示]`：不阻塞，继续下一步（`[提示]` 项如「编辑器开着/没开」「core.autocrlf=true」留个印象即可）。

## 2. 人工步骤清单（只有人能做，逐条问开发者是否完成）

1. Unity Hub 装 **2022.3.62f2**，勾选 **Windows Build Support** 与 **Android Build Support**（含 SDK/NDK Tools、OpenJDK）两个模块。
2. 用 Hub 打开工程根目录；首次解析 `Packages/manifest.json` 里的包需要**联网且能访问 GitHub**（manifest 里挂着几个 git 包），等编辑器状态栏转圈结束。
3. `Window → MCP for Unity`，把 `Transport` 选成 `Stdio`；**不要点** `Configure All Detected Clients`（它往用户级配置写东西，本工程只认项目级 `.mcp.json`）。
4. 确认桥接是**绿灯**（窗口显示 Session Active）。

四条逐项确认「done」，任何一条卡住就停在那条，不替开发者点编辑器菜单。这几步做完后重跑一次第 1 步的环境自检，`MCP 桥接状态` 那项应变成 `[通过]`。

## 3. MCP 连通验证（Claude 来做）

1. 读资源 `mcpforunity://instances`，应**恰好**看到一个名字以本工程目录名开头的实例。本机同时开着别的 Unity 工程时，实例列表会有多个——只 `set_active_instance` 到本工程那个，不用默认路由。
2. 读资源 `mcpforunity://editor/state`，确认 `data.advice.ready_for_tools` 为真。
3. `read_console(action="get")`，确认没有报错（0 条 error）。

任何一步不过，按 `.claude/skills/unity-mcp/SKILL.md` 的「连接状态怎么看」「故障排查」两节处理，不要自己瞎猜。最常见的坑是 Unity 侧 `Transport` 选成了 `HTTPLocal`——连了但 `instance_count: 0`，改成 `Stdio` 即可（`ai-docs/pitfalls.md` 最后一条有完整现象/根因）。

## 4. 跑一次测试

```
/unity-test EditMode
```

工程当前还没有测试用例的话，`/unity-test` 会报 0 个用例——如实说明「当前无测试用例，跳过」，不是失败。

## 5. 该读什么

- `docs/developer-guide.md` 第 1～4 章（环境、拉取工程后第一步、目录与程序集、提交规范）
- `docs/architecture.md` 第 3 节
- `docs/commit-convention.md`

提醒一句：**提交前必须 `/review-change` 列清单待审**，不能自己直接 `git commit`。

## 6. 收尾

输出一张「上手完成清单」，每步标 通过 / 跳过 / 待办：

```
环境自检     通过
人工步骤     通过
MCP 连通     通过
跑一次测试   跳过（当前无测试用例）
文档阅读     待办
```

有「待办」项，说清楚具体待办的是什么（比如「文档阅读」待办就是「开发者还没确认读完」），不要笼统写「待办」了事。

收尾提示后续开发入口：日常开发用 `/dev <任务>`；要新建玩法模块用 `/new-feature <模块名>`。
