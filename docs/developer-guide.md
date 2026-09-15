# 开发者手册

面向在本工程写代码的开发者，回答「怎么操作」。设计决策与各服务的契约见 [`architecture.md`](architecture.md)，本文不重复讲为什么，只讲怎么做。

## 1. 环境准备

新开发者拿到一台干净机器，按下表顺序装完、逐项验证即可。装完看下面的「一键安装」与「装完之后」两节；细节按需展开对应小节（`/onboard` 与 `check_env.py` 的失败提示会指到具体小节）。

| 顺序 | 工具 | 版本要求 | 为什么需要 | 安装方式 | 验证命令 |
| --- | --- | --- | --- | --- | --- |
| 1.1 | Unity Hub | 最新稳定版 | 管理编辑器版本、下载模块 | 官网下载，或 `winget install Unity.UnityHub` | 能正常打开 Hub 窗口 |
| 1.2 | Unity 编辑器 | 2022.3.62f2（精确版本，见 `ProjectSettings/ProjectVersion.txt`） | 工程用这个版本开发；换版本打开可能触发不可逆的资源升级 | Hub → Installs → Install Editor → Archive 里找该版本；或用工程 changeset 拼深链在浏览器打开：`unityhub://2022.3.62f2/7670c08855a9`；必须勾 **Windows Build Support (IL2CPP)** 与 **Android Build Support**（含 Android SDK & NDK Tools、OpenJDK） | Hub 的 Installs 列表里能看到该版本；或 `check_env.py` 第一项 `[通过]` |
| 1.3 | Git | 任意近期版本 | 版本控制；本仓库不用 LFS | `winget install Git.Git` | `git --version` |
| 1.4 | Python | 3.10+ | 钩子、lint 脚本、`/onboard` 自检脚本都是 Python 写的 | `winget install Python.Python.3.12` | `python --version` |
| 1.5 | uv | 任意近期版本 | MCP for Unity 服务端靠 `uvx` 拉起（见 `.mcp.json`） | `winget install astral-sh.uv`，或官方一行安装脚本 | `uv --version`、`uvx --version` |
| 1.6 | .NET SDK | 8.0+ | Luban 配置表生成用，波 2 起必需 | `winget install Microsoft.DotNet.SDK.8` | `dotnet --list-sdks` |
| 1.7 | Claude Code | 以官方文档为准 | 本工程的 harness（规则/钩子/命令/技能）跑在其中 | 以官方文档为准 | `claude --version` |
| 1.8 | 可选：Rider / VS 2022 | 带 Unity 工作负载 | C# 编辑体验，非必需 | 官网下载安装，或 VS 2022 装 Unity 工作负载 | 能正常打开工程的 `.sln` |

### 1.1 Unity Hub

官网下载安装包，或 `winget install Unity.UnityHub`。没有版本要求，装最新稳定版即可。

### 1.2 Unity 编辑器

必须是 **2022.3.62f2**，精确到这个小版本（以 `ProjectSettings/ProjectVersion.txt` 的 `m_EditorVersion` 为准，不要用别的小版本，跨版本打开工程可能触发不可逆的资源升级）。

两种装法：

- Hub 里 **Installs → Install Editor → Archive**，找到 2022.3.62f2。
- 或者直接用工程里的 changeset 拼 Hub 深链，浏览器打开会自动唤起 Hub 安装对应版本：
  `unityhub://2022.3.62f2/7670c08855a9`

安装时必须勾选 **Windows Build Support (IL2CPP)** 与 **Android Build Support**（含 Android SDK & NDK Tools、OpenJDK）两个模块——本工程 Windows / Android 双端出包都要用到。

验证：Hub 的 Installs 列表里能看到该版本；或跑 `python .claude/skills/onboard/check_env.py`，第一项「工程版本对应的 Unity 编辑器」应为 `[通过]`。

### 1.3 Git

`winget install Git.Git`。装完建议设置：

```
git config --global core.autocrlf false
```

仓库 `.gitattributes` 已统一 LF，不需要 Git 帮你转换换行符（`autocrlf=true` 不会报错，但 `check_env.py` 会提示一句）。

### 1.4 Python

3.10 及以上，`winget install Python.Python.3.12`。项目的 PreToolUse/PostToolUse 钩子、`project-lint`、`/onboard` 自检脚本都是 Python 写的，没有它这些环节全跑不起来。

### 1.5 uv

`winget install astral-sh.uv`，或官方一行安装：

```
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

MCP for Unity 的服务端靠 `uvx` 按需拉起（配置见工程根 `.mcp.json`），没有 `uv`/`uvx` 就连不上 MCP 桥接。

### 1.6 .NET SDK

8.0 及以上，`winget install Microsoft.DotNet.SDK.8`。Luban 配置表生成工具需要，波 2（配置表接入）起必需，波 0/1 可以先跳过。

### 1.7 Claude Code

以官方文档为准安装（不同渠道更新频繁，这里不重复步骤）。命令行验证：

```
claude --version
```

打开本工程后，`/mcp` 里 `UnityMCP` 应显示为 connected；Unity 编辑器侧要在 `Window → MCP for Unity` 把 Transport 选成 **Stdio**（不要点 `Configure All Detected Clients`，本工程只认项目级 `.mcp.json`）。

### 1.8 可选：Rider 或 VS 2022

C# 编辑体验用，非必需——命令行加编辑器内置脚本编辑器也能开发。选 Rider（官网下载）或 VS 2022（装 Unity 工作负载）。验证：能正常打开工程根下的 `.sln`（首次由 Unity 编辑器生成；按硬规则第 1 条，`.sln`/`.csproj` 是生成物，不手改）。

### 1.9 一键安装（推荐）

管理员权限打开 PowerShell，在工程根下跑：

```
powershell -ExecutionPolicy Bypass -File .claude/skills/onboard/install_env.ps1
```

它会装 1.1（Unity Hub）与 1.3～1.6（Git / Python / uv / .NET SDK）；已安装且版本达标的会跳过，不重复装。**不装** 1.2（Unity 编辑器，体积大且要手动勾模块，脚本只打印深链和模块清单）与 1.7（Claude Code，走官方渠道），这两项仍需手动完成。

装完**重开一个终端窗口**再往下走——`winget` 新装的工具不会立刻出现在当前终端的 PATH 里。

### 1.10 装完之后

```
python .claude/skills/onboard/check_env.py
```

全部 `[通过]`（`[提示]` 不阻塞，可以先往下走）再继续看第 2 章。

## 2. 拉取工程后第一步

1. 在 Claude Code 里跑 `/onboard`，按提示逐项完成；环境没装齐时它会先带你按第 1 章把环境装好。
2. 用 Unity Hub 打开工程根目录，等 Package Manager 把 `manifest.json` 里的包（Unity Registry 包 + git 包）解析完，编辑器状态栏转圈结束再动手，中途改代码容易和包解析打架。
3. 波 1 落地 Boot 场景后，第一步会改成「打开 `Assets/_Project/Scenes/Boot.unity`」；当前波（波 0）还没有场景，打开工程能编译通过即可。
4. **Input System 后端不用手动切**：本波已经把 `ProjectSettings/ProjectSettings.asset` 的 `activeInputHandler` 设成 `2`（Both），装完 Input System 不会弹「切换输入后端需要重启编辑器」的对话框，新旧两套输入 API 都能用（新 API 走 Action Map 给玩法用，旧 API 留给 `IngameDebugConsole` 这类第三方调试台）。

## 3. 目录与程序集：我的代码该放哪

依赖方向（细则见 [`../.claude/rules/project-root.md`](../.claude/rules/project-root.md)）：

```
Game.Tests.EditMode / Game.Tests.PlayMode ──┐
Game.Editor ─────────────────────────────────┼──► Game.Runtime ──► Game.Core ──► 第三方包
                                             └──────────────────►
```

- **写框架能力**（启动、服务、事件、资源、配置、状态流、UI、音频、存档、输入、池、定时器、日志、平台）→ `Assets/_Project/Scripts/Core/`，asmdef `Game.Core`。这里**不允许出现任何玩法名词**，改动前先看 [`architecture.md`](architecture.md) 第 5 节的契约，形状不能变。
- **写玩法**：`Assets/_Project/Scripts/Runtime/<你的模块名>/`，一个模块一个目录，命名空间 `Game.<模块名>`，asmdef `Game.Runtime`。只能引用 `Game.Core` 与第三方包提供的能力，不能反向引用别的玩法模块的私有实现——要用别的模块的东西，走对方的公开接口 / 事件 / ScriptableObject。
- **写编辑器工具**：`Assets/_Project/Scripts/Editor/`，asmdef `Game.Editor`，可以引用 `Game.Core`、`Game.Runtime`，不会进构建包体。
- **写测试**：`Assets/_Project/Scripts/Tests/{EditMode,PlayMode}/`，EditMode 优先（不用起编辑器播放模式，跑得快）。
- **能引用什么**：asmdef 里按名字引用第三方程序集（UniTask、VContainer、MessagePipe、MessagePipe.VContainer、Unity.Addressables、Unity.ResourceManager、Unity.InputSystem、Unity.TextMeshPro、LitMotion、LitMotion.Extensions），不要用 GUID 引用，也不要在代码里反射拿私有 API。
- **加能力前的顺序**：先看能不能复用已有脚本/组件/SO 换个参数解决，再看能不能扩展进已有文件，最后才新建文件——新建要在文件头写明前两步为什么不行。

## 4. 提交规范与审查

- 提交信息格式按 [`commit-convention.md`](commit-convention.md)：`type(scope): 一句话`，不带任何 AI 署名。
- 改动前后跑一遍项目 lint：保存 `.cs` 时钩子会自动跑，手动跑用 `python .claude/skills/project-lint/lint.py <file.cs>`。
- 新建/移动/删除 `.asmdef`、`.cs`、场景、预制体等资产时，让 Unity 编辑器刷新生成对应的 `.meta`，`.meta` 要和资产一起提交，不要留孤儿 `.meta`，也不要手改 `.meta` 内容。
- 提交前用 `/review-change` 列改动清单，等明确同意再 `git commit`；不 `push` 除非明说。

## 5. 启动流程

待波 1 补充（`GameBootstrap` / `GameLifetimeScope` 落地后）。

## 6. 服务速查

待波 1～3 补充。每个服务会给一小节：怎么拿到（构造注入 / `IObjectResolver`）、常用调用、禁止事项。覆盖：`Boot`、`Events`、`Assets`、`Config`、`Flow`、`UI`、`Audio`、`Save`、`Input`、`Pooling`、`Timing`、`Logging`、`Platform`。

## 7. 新建玩法模块

待波 1 补充（`/new-feature` 命令落地流程后）。

## 8. 配置表怎么改

待波 2 补充（Luban 接入后）。

## 9. 存档

待波 2 补充（`ISaveService` 落地后）。

## 10. 输入

待波 1 补充（`IInputService` 与 `GameInput` 落地后）。

## 11. UI 面板

待波 3 补充（`IUIService` / `UIView` 落地后）。

## 12. 音频

待波 3 补充（`IAudioService` 落地后）。

## 13. 测试

测试怎么写、怎么跑见 [`../.claude/rules/unity-tests.md`](../.claude/rules/unity-tests.md) 与 `/unity-test` 命令；具体测试范例待波 4 补充。

## 14. 打包与 CI

本机出包用 `/build`（`scripts/build.ps1`，编辑器须关闭）；CI 一次性配置与打 tag 出包见 [`ci-setup.md`](ci-setup.md)。

## 15. 常见问题

待各波开发过程中累积。
