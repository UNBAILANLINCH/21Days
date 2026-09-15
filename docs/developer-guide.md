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

入口场景是 `Assets/_Project/Scenes/Boot.unity`（Build Settings 第 0 位）。场景里只有两个物体：`Main Camera` 和 `GameBootstrap`——后者同时挂着 `GameLifetimeScope`（根作用域）与 `GameBootstrap`（唯一 MonoBehaviour 入口）。

```
Boot.unity 加载
 → GameBootstrap.Awake：DontDestroyOnLoad + 缓存 LifetimeScope + 建 CancellationTokenSource
   （LifetimeScope 在它自己的 Awake 里建容器，所以启动流程写在 Start，不写在 Awake）
 → GameBootstrap.Start → BootAsync
     ① 编辑器/开发包下实例化 IngameDebugConsole 预制体（Inspector 上留空就跳过并 Warn）
     ② IGameFlow.GoToAsync<BootState>()
     ③ 解析 IReadOnlyList<IGameService>，按**容器注册顺序串行** await 每个 InitializeAsync
     ④ 发布 BootCompletedEvent
     ⑤ IGameFlow.GoToAsync<TitleState>()
```

要点：

- **注册顺序就是初始化顺序**。要调整顺序，改 `GameLifetimeScope.Configure` 里的注册先后，不要在别处加调用。顺序按 `architecture.md` 5.1：Platform → Log → Config → Assets → Save → Input → Audio → UI。
- **加一个新框架服务** = 实现 `IGameService` + 在 `GameLifetimeScope` 里 `.As<I你的接口, IGameService>()`，别的地方一行不用改。
- 任何一步抛异常都会被 `BootAsync` 捕获、`Log.Error` 后**停止**启动，不会带着半初始化的状态往下跑。退出播放模式引起的 `OperationCanceledException` 不算错误。
- 玩法场景走 Additive 加载，Boot 场景全程常驻。

## 6. 服务速查

拿服务的方式只有两种：**构造注入**（推荐，写进自己的构造函数参数）和 `IObjectResolver.Resolve<T>()`（只在 MonoBehaviour 这类容器管不到的地方用）。下面各节的「禁止」都是踩过或必踩的坑。

### 6.1 Logging — `Log`

```csharp
Log.Debug("只在编辑器与开发包里存在");   // 正式包里连参数求值都被剔除
Log.Info("常规信息"); Log.Warn("要留意"); Log.Error("出错了", this);
```

静态门面，不进容器，不用注入。输出统一带 `[Game] ` 前缀，第二个参数传 `UnityEngine.Object` 后点日志能在 Hierarchy 里定位到对象。
**禁止**：直接用 `UnityEngine.Debug.Log`（前缀不统一、剔除不掉）；在每帧路径上打日志（lint 会拦）；用 `Log.Info` 打调试信息（发布包里会留着）。

### 6.2 Events — MessagePipe

```csharp
public sealed class Foo { public Foo(ISubscriber<BootCompletedEvent> sub) { ... } }
sub.Subscribe(e => ...).AddTo(bag);      // bag 是 DisposableBag.CreateBuilder()
publisher.Publish(new BootCompletedEvent(n));
```

事件类型写成 `readonly struct`，命名 `XxxEvent`；全局事件在 `GameLifetimeScope` 用 `RegisterMessageBroker<T>(options)` 注册，模块事件在模块子作用域注册。完整约定见 `Assets/_Project/Scripts/Core/Events/EventConventions.cs` 文件头。
**禁止**：裸订阅（句柄不 `AddTo` 就退订不掉）；用 C# `static event` 做跨模块通信；在事件回调里同步再发同一个事件。

### 6.3 Timing — `IClock` / `ITimerService`

```csharp
public sealed class Foo { public Foo(IClock clock, ITimerService timers) { ... } }
TimerHandle h = timers.Delay(1.5f, () => ...);          // 到点触发一次
TimerHandle r = timers.Interval(1f, () => ..., true);   // 每秒一次，true = 不受 timeScale 影响
h.Dispose();                                            // 取消
```

`IClock` 是**唯一**时间来源（`UtcNow` / `GameTime` / `UnscaledTime` / `DeltaTime` / `UnscaledDeltaTime`）。`TimerService` 只读 `IClock`，所以拿假时钟就能写确定性测试（见 `TimerServiceTests`）。作用域 Dispose 时所有定时器自动取消。
**禁止**：直接读 `Time.time` / `DateTime.UtcNow`；用 `Interval(0)` 冒充每帧（每帧请实现 VContainer 的 `ITickable`）；把句柄丢掉不管。

### 6.4 Pooling — `GameObjectPool` / `IPoolable`

```csharp
var pool = new GameObjectPool(prefab, parentTransform);
pool.Prewarm(20);
GameObject go = pool.Get();   // 已 SetActive(true) 并回调过 IPoolable.OnGet
pool.Release(go);             // 先回调 OnRelease 再 SetActive(false)
```

一个池管一种预制体，自己 `new`、自己 `Dispose`（不进容器）。池化对象在根节点上挂实现 `IPoolable` 的组件来重置状态。
**禁止**：把从池里拿的对象 `Destroy` 掉（下次 `Release` 会炸）；指望 `OnEnable/OnDisable` 代替 `IPoolable`；跨预制体共用一个池。

### 6.5 Input — `IInputService`

```csharp
public sealed class Foo { public Foo(IInputService input) { ... } }
Vector2 move = input.Actions.Gameplay.Move.ReadValue<Vector2>();
input.EnableMap("UI"); input.DisableMap("Gameplay");
```

详见第 10 章。
**禁止**：玩法里读具体按键、读 `Input.touches` / 旧 `Input` 类；自己 `new GameInput()`；手改生成的 `GameInput.cs`。

### 6.6 Platform — `IPlatformService`

```csharp
public sealed class Foo { public Foo(IPlatformService platform) { ... } }
if (platform.IsTouchPrimary) { ... }
platform.Vibrate(VibrationKind.Light);       // 不支持的平台上是空操作
string root = platform.SaveRoot;             // persistentDataPath/saves，启动时已建好
```

实现由 `PlatformServiceFactory.Create()` 按构建目标选（编辑器恒定用 `StandalonePlatformService`）。
**禁止**：在 `Core/Platform/` 以外的任何文件里写 `#if UNITY_ANDROID` 这类平台宏、调 `Handheld` / `Application.platform`（lint 与 code-review 都会拦）。

### 6.7 Flow — `IGameFlow`

```csharp
await flow.GoToAsync<TitleState>(ct);
```

切换串行：先 `ExitAsync` 当前状态，再 `EnterAsync` 目标状态；切换进行中再请求会**排队**按序执行，完成后发布 `GameStateChangedEvent(from, to)`。状态由容器解析，所以状态类可以构造注入服务。
**禁止**：在 `GameState` 里写每帧逻辑（用 `ITickable`）；在 `EnterAsync` 里同步阻塞等待；忘了把玩法状态注册进作用域（`GoToAsync` 会解析失败）。

> `Assets`、`Config`、`Save` 待波 2；`UI`、`Audio` 待波 3。

## 7. 新建玩法模块

1. **建目录**：`Assets/_Project/Scripts/Runtime/<模块名>/`，命名空间 `Game.<模块名>`（asmdef 已有 `Game.Runtime`，模块不单独建 asmdef）。
2. **划分类型**：规则类写成**纯 C# 类**（不继承 MonoBehaviour），MonoBehaviour 只做表现与输入转发；数值进 `Assets/_Project/Data/<模块名>/` 的 ScriptableObject。
3. **挂子作用域**：模块场景里放一个 `<模块名>LifetimeScope : LifetimeScope`，在它的 `Configure` 里注册本模块的服务、状态与事件（`RegisterMessageBroker<T>` 用模块自己的 options）。父作用域自动是 `GameLifetimeScope`，所以能直接注入 `IClock`、`ITimerService`、`IInputService` 等框架服务。
   > 新建以 `LifetimeScope.cs` 结尾的脚本时注意第 15 章那条坑：VContainer 会用空模板覆盖一次文件内容。
4. **订阅事件**：构造注入 `ISubscriber<XxxEvent>`，`Subscribe(...).AddTo(bag)`，在 `Dispose` 里释放 bag。跨模块只订阅对方的公开事件，不 `GetComponent` 到对方的私有实现。
5. **写成可测的形状**：规则类的输入输出都是普通值/DTO，不碰 `UnityEngine.Time`、不碰单例；这样 `Assets/_Project/Scripts/Tests/EditMode/<模块名>/` 里一条 `Assert.That` 就能覆盖核心规则，不需要场景也不需要帧循环。每个模块至少一条 EditMode 测试。
6. **收尾**：`/unity-test EditMode` 跑绿，`/generate-doc <模块名>` 生成文档三件套，`/review-change` 列清单待审。

命令入口：`/new-feature <模块名>` 会把上面 1～6 串起来走一遍。

## 8. 配置表怎么改

待波 2 补充（Luban 接入后）。

## 9. 存档

待波 2 补充（`ISaveService` 落地后）。

## 10. 输入

### 10.1 资产与生成物

- 动作定义在 `Assets/_Project/Data/Input/GameInput.inputactions`，**双击它在 Input Actions 窗口里改**，不要手改 JSON。
- 它的导入器勾了 **Generate C# Class**，参数是：类名 `GameInput`、命名空间 `Game.Core.Input`、输出路径 `Assets/_Project/Scripts/Core/Input/GameInput.cs`。
- `GameInput.cs` 是**生成物**：改了 `.inputactions` 保存，Unity 自动重新生成它。**不要手改这个文件**，改了下次保存资产就没了。

### 10.2 两个 Action Map

| Map | 动作 | 绑定 |
| --- | --- | --- |
| `Gameplay` | `Move`(Vector2)、`Confirm`、`Cancel`、`Pause` | 键鼠（WASD / 方向键 / Enter / Esc / P）、手柄（左摇杆 / 十字键 / A / B / Start）、触屏（primaryTouch tap）。`Move` 上留了一条空路径的 `TouchVirtualStick` 绑定，等波 3 的虚拟摇杆落地后在 Inspector 里补上 |
| `UI` | Input System 默认的 UI 动作（Navigate / Submit / Cancel / Point / Click / ScrollWheel / MiddleClick / RightClick / TrackedDevice*） | 默认键鼠 + 手柄 + 触屏 |

### 10.3 玩法怎么用

```csharp
public sealed class PlayerMovement          // 表现层 MonoBehaviour 或纯 C# 规则类都行
{
    private readonly IInputService input;
    public PlayerMovement(IInputService input) => this.input = input;

    public Vector2 ReadMove() => input.Actions.Gameplay.Move.ReadValue<Vector2>();
}
```

- `InputService` 在启动初始化时 `new GameInput()` 并启用 `Gameplay` map；`UI` map 默认不开，由波 3 的 `IUIService` 按需 `EnableMap("UI")`。
- 要临时屏蔽玩法输入（开面板、播过场）：`input.DisableMap("Gameplay")`，结束后再 `EnableMap`。
- **只读动作，不读按键**：玩法代码里出现 `Keyboard.current`、`Input.GetKey`、`Input.touches` 一律算违规——那样手柄和触屏就得各写一遍。要加新的输入方式，去 `.inputactions` 里给同一个动作加 binding。
- 新增一个动作 = 在 `.inputactions` 里加 → 保存（自动重新生成 `GameInput.cs`）→ 玩法里 `input.Actions.Gameplay.<新动作>`。框架代码一行不用改。

## 11. UI 面板

待波 3 补充（`IUIService` / `UIView` 落地后）。

## 12. 音频

待波 3 补充（`IAudioService` 落地后）。

## 13. 测试

测试怎么写、怎么跑见 [`../.claude/rules/unity-tests.md`](../.claude/rules/unity-tests.md) 与 `/unity-test` 命令；具体测试范例待波 4 补充。

## 14. 打包与 CI

本机出包用 `/build`（`scripts/build.ps1`，编辑器须关闭）；CI 一次性配置与打 tag 出包见 [`ci-setup.md`](ci-setup.md)。

## 15. 常见问题

### 15.1 新建的 `*LifetimeScope.cs` 内容被清空成模板（波 1 踩到）

**现象**：写好一个名字以 `LifetimeScope.cs` 结尾的脚本，Unity 刷新一次之后打开发现内容变成了空的 `public class Xxx : LifetimeScope { protected override void Configure(...) { } }`，命名空间也被换成了 asmdef 的 rootNamespace。

**根因**：VContainer 包里有个 `ScriptTemplateProcessor`（`AssetModificationProcessor.OnWillCreateAsset`），Unity 为**新建**的 `*LifetimeScope.cs` 生成 `.meta` 时，它会用自带模板 `File.WriteAllText` 覆盖文件内容。这是它的「新建 LifetimeScope 自动套模板」功能，对在编辑器外写好的文件是误伤。

**正确做法**：只在**首次创建**时发生一次。流程改成「先建文件 → 让 Unity 刷新生成 `.meta` → 再把真正的内容写进去 → 再刷新」。文件存在之后再怎么改都不会被覆盖。想彻底关掉：`Project Settings → VContainer` 里勾 `Disable Script Modifier`（本工程没关，因为只在新建时影响一次）。

### 15.2 MCP `read_console` 有时读不到刚打的日志（波 1 踩到）

**现象**：波 1 实施时 `read_console(action="get")` 一度稳定返回 0 条，连刚用 `execute_code` 打的 `Debug.Log` 也读不到；随后主窗口在测试跑完后实测又能读到。根因未定位，怀疑与域重载时机或控制台被清空有关。遇到时先 `refresh_unity` 等编辑器空闲再读一次，`types` 传 `["all"]`。

**替代验证手段**（不要因为读不到控制台就宣称「编译通过」）：

- 编译是否成功：`execute_code` 里读 `UnityEditor.EditorUtility.scriptCompilationFailed`，再用 `System.Type.GetType("命名空间.类名, 程序集名")` 确认新类型真的被编译进了目标程序集。
- 运行时日志：`execute_code` 里临时挂 `Application.logMessageReceived`，触发一次要观察的流程，收集完再摘掉。
- 实在要看历史日志：让用户看编辑器 Console 窗口，或用运行时的 IngameDebugConsole。
