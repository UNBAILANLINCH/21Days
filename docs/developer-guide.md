# 开发者手册

面向在本工程写代码的开发者，回答「怎么操作」。设计决策与各服务的契约见 [`architecture.md`](architecture.md)，本文不重复讲为什么，只讲怎么做。

## 读这份手册的顺序

**不要从头读到尾。** 按你现在要做的事挑：

| 你是谁 / 要做什么 | 读哪几章 |
| --- | --- |
| 第一天，机器还是空的 | 1 环境准备 → 2 拉取工程后第一步（跑 `/onboard`，它会把这两章串起来） |
| 第一次写代码，不知道文件该放哪 | 3 目录与程序集 → 4 提交规范 |
| 想搞清楚游戏是怎么跑起来的 | 5 启动流程 → 6 服务速查（只看你要用的那一节） |
| **要写第一个玩法模块** | 7 新建玩法模块（以 `Sample` 模块为范例，从头走一遍） |
| 要改表 / 存档 / 输入 / UI / 音频 | 8～12，按主题挑一章 |
| 要跑测试、出包 | 13 测试 → 14 打包与 CI |
| 要接 2.5D 场景美术（画质分档 / 角色纸片 / 贴地） | 6.13 |
| 要接对话（代码拉起 / 场景放 NPC）或给角色换拼接小人 | 6.14 · 6.15 |
| 卡住了、报了看不懂的错 | 15 常见问题（先在这儿搜一遍，八成有） |

三份文档的分工：**本文**讲怎么做，[`architecture.md`](architecture.md) 讲为什么这么设计、各服务的契约长什么样，
[`../ai-docs/pitfalls.md`](../ai-docs/pitfalls.md) 记踩过的坑。不确定该读哪份就看
[`../ai-docs/docs/catalog.md`](../ai-docs/docs/catalog.md)。

## 1. 环境准备

新开发者拿到一台干净机器，按下表顺序装完、逐项验证即可。装完看下面的「一键安装」与「装完之后」两节；细节按需展开对应小节（`/onboard` 与 `check_env.py` 的失败提示会指到具体小节）。

| 顺序 | 工具 | 版本要求 | 为什么需要 | 安装方式 | 验证命令 |
| --- | --- | --- | --- | --- | --- |
| 1.1 | Unity Hub | 最新稳定版 | 管理编辑器版本、下载模块 | 官网下载，或 `winget install Unity.UnityHub` | 能正常打开 Hub 窗口 |
| 1.2 | Unity 编辑器 | 2022.3.62f2（精确版本，见 `ProjectSettings/ProjectVersion.txt`） | 工程用这个版本开发；换版本打开可能触发不可逆的资源升级 | Hub → Installs → Install Editor → Archive 里找该版本；或用工程 changeset 拼深链在浏览器打开：`unityhub://2022.3.62f2/7670c08855a9`；必须勾 **Windows Build Support (IL2CPP)** 与 **Android Build Support**（含 Android SDK & NDK Tools、OpenJDK） | Hub 的 Installs 列表里能看到该版本；或 `check_env.py` 第一项 `[通过]` |
| 1.3 | Git | 任意近期版本 | 版本控制；本仓库不用 LFS | `winget install Git.Git` | `git --version` |
| 1.4 | Python | 3.10+ | 钩子、lint 脚本、`/onboard` 自检脚本都是 Python 写的 | `winget install Python.Python.3.12` | `python --version` |
| 1.5 | uv | 任意近期版本 | MCP for Unity 服务端靠 `uvx` 拉起（见 `.mcp.json`） | `winget install astral-sh.uv`，或官方一行安装脚本 | `uv --version`、`uvx --version` |
| 1.6 | .NET SDK | 8.0+ | Luban 配置表生成用（第 8 章），改表就要 | `winget install Microsoft.DotNet.SDK.8` | `dotnet --list-sdks` |
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

8.0 及以上，`winget install Microsoft.DotNet.SDK.8`。Luban 配置表生成工具需要（第 8 章「配置表怎么改」）。只读代码不改表的话可以先跳过，但跑 `scripts/gen-tables.ps1` 之前必须装。

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
3. 打开 `Assets/_Project/Scenes/Boot.unity`（入口场景），按 Play 应该看到标题界面，点「开始」能进示例玩法场景再退回来。跑不起来先看第 15 章。
4. **Input System 后端不用手动切**：工程里 `ProjectSettings/ProjectSettings.asset` 的 `activeInputHandler` 已经是 `2`（Both），装完 Input System 不会弹「切换输入后端需要重启编辑器」的对话框，新旧两套输入 API 都能用（新 API 走 Action Map 给玩法用，旧 API 留给 `IngameDebugConsole` 这类第三方调试台）。

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
- **能引用什么**：asmdef 里按名字引用第三方程序集（UniTask、VContainer、MessagePipe、MessagePipe.VContainer、Unity.Addressables、Unity.ResourceManager、Unity.InputSystem、Unity.TextMeshPro、LitMotion、LitMotion.Extensions、Luban.Runtime），不要用 GUID 引用，也不要在代码里反射拿私有 API。要用 Luban 生成的配置类（`cfg.Item` 这些）就得引 `Luban.Runtime`——它们继承 `Luban.BeanBase`，少了这条引用会报「类型定义在未引用的程序集里」。
- **加能力前的顺序**：先看能不能复用已有脚本/组件/SO 换个参数解决，再看能不能扩展进已有文件，最后才新建文件——新建要在文件头写明前两步为什么不行。

## 4. 提交规范与审查

- 提交信息格式按 [`commit-convention.md`](commit-convention.md)：`type(scope): 一句话`，不带任何 AI 署名。
- 改动前后跑一遍项目 lint：保存 `.cs` 时钩子会自动跑，手动跑用 `python .claude/skills/project-lint/lint.py <file.cs>`。
- 新建/移动/删除 `.asmdef`、`.cs`、场景、预制体等资产时，让 Unity 编辑器刷新生成对应的 `.meta`，`.meta` 要和资产一起提交，不要留孤儿 `.meta`，也不要手改 `.meta` 内容。
- 提交前用 `/review-change` 列改动清单，等明确同意再 `git commit`；不 `push` 除非明说。

## 5. 启动流程

入口场景是 `Assets/_Project/Scenes/Boot.unity`（Build Settings 第 0 位）。场景里只有两个物体：`Main Camera` 和 `GameBootstrap`——后者挂着 `GameLifetimeScope`（根作用域）、`GameBootstrap`（唯一 MonoBehaviour 入口），以及**各玩法模块的 `GameplayInstaller` 子类**（示例模块的是 `SampleInstaller`）。

```
Boot.unity 加载
 → GameBootstrap.Awake：DontDestroyOnLoad + 缓存 LifetimeScope + 建 CancellationTokenSource
   （LifetimeScope 在它自己的 Awake 里建容器，所以启动流程写在 Start，不写在 Awake）
 → GameLifetimeScope.Configure：注册全部框架服务，最后把同一物体上的
   每个 GameplayInstaller.Install(builder) 调一遍（玩法的状态 / 规则类 / 入口点在这一步进根作用域）
 → GameBootstrap.Start → BootAsync
     ① 编辑器/开发包下实例化 IngameDebugConsole 预制体（Inspector 上留空就跳过并 Warn）
     ② IGameFlow.GoToAsync<BootState>()
     ③ 解析 IReadOnlyList<IGameService>，按**容器注册顺序串行** await 每个 InitializeAsync
     ④ 发布 BootCompletedEvent
     ⑤ IGameFlow.GoToAsync<TitleState>()
```

要点：

- **注册顺序就是初始化顺序**。要调整顺序，改 `GameLifetimeScope.Configure` 里的注册先后，不要在别处加调用。顺序按 `architecture.md` 5.1：Platform → Log → Assets → Config → Save → Input → Audio → UI。
- **加一个新框架服务** = 实现 `IGameService` + 在 `GameLifetimeScope` 里 `.As<I你的接口, IGameService>()`，别的地方一行不用改。
- **加一个玩法模块** = 写一个 `GameplayInstaller` 子类，把组件挂到 `GameBootstrap` 物体上。`Game.Core` 不认识任何玩法，所以玩法只能这样把自己接上来（第 7 章有完整流程）。**玩法状态必须注册进根作用域**，不能放玩法场景的子作用域——`GameFlow` 从根 `IObjectResolver` 解析状态类型，而且切进去之前那个场景还没加载。
- 标题界面的「开始」按钮**不在框架里决定去哪**：`TitleView` 抛 `OnStartClicked` 事件 → `TitleState` 发布 `TitleStartClickedEvent` → 玩法侧的入口点订阅它并 `GoToAsync<自己的状态>()`。没有玩法接进来时点了只留一条日志，不是错误。
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

**状态要带一个场景就继承 `SceneGameState`**，别自己在 `EnterAsync` 里加载：

```csharp
public sealed class BattleState : SceneGameState
{
    private readonly IUIService ui;
    public BattleState(IAssetService assets, IUIService ui) : base(assets) => this.ui = ui;

    protected override string SceneKey => "Battle";                     // Addressables 地址
    protected override async UniTask OnSceneReadyAsync(CancellationToken ct)   // 场景加载完
        => await ui.OpenAsync<BattleHudView>(ct: ct);
    protected override UniTask OnSceneUnloadingAsync(CancellationToken ct) { ... }  // 卸载前清理
}
```

基类的 `EnterAsync` / `ExitAsync` 是 `sealed` 的：进入时 `LoadSceneMode.Additive` 加载并持有 `SceneHandle`，退出时无条件卸载（子类清理抛异常也照卸，否则再进一次会叠出两份场景）。Boot 场景全程常驻，所以永远是 Additive，不用 Single。
**禁止**：在 `GameState` 里写每帧逻辑（用 `ITickable`）；在 `EnterAsync` 里同步阻塞等待；忘了把玩法状态注册进作用域（`GoToAsync` 会解析失败）；自己在状态里 `LoadSceneAsync` 又不存句柄。

### 6.8 Assets — `IAssetService`

```csharp
public sealed class Foo { public Foo(IAssetService assets) { ... } }
using (AssetHandle<Sprite> h = await assets.LoadAsync<Sprite>("Icon_Sword", ct)) { image.sprite = h.Asset; }
IReadOnlyList<AssetHandle<TextAsset>> all = await assets.LoadAllAsync<TextAsset>("config", ct);  // 按标签批量
GameObject go = await assets.InstantiateAsync("Enemy_Slime", parent, ct);
assets.ReleaseInstance(go);                                   // 配对，不要 Destroy
SceneHandle scene = await assets.LoadSceneAsync("Level01", LoadSceneMode.Additive, ct);
scene.Dispose();                                              // 即卸载
```

底下是 Addressables。**只有 Load 与 Release 两类动词**：句柄 `Dispose` 即释放且幂等，生命周期跟着持有者走；服务销毁时会把没还的强行释放并 `Log.Warn` 报数量，看到这条 Warn 就是有人漏了。
**禁止**：直接调 `Addressables` / `Resources.Load`；把 `Instantiate` 出来的对象 `Destroy`（要 `ReleaseInstance`）；只存 `handle.Asset` 不存句柄（释放后 `Asset` 变 null）；指望有 `Exists` / `Check` / `Download`——**故意不提供**，业务拿它当加载判定会在真机上静默失败。

### 6.9 Config — `IConfigService`

```csharp
public sealed class Foo { public Foo(IConfigService config) { ... } }
cfg.Item item = config.Tables.TbItem.Get(1001);      // 主键不存在会抛
cfg.Item maybe = config.Tables.TbItem.GetOrDefault(1001);
foreach (cfg.Item it in config.Tables.TbItem.DataList) { ... }
```

数据源是 `Tables/` 下的 Excel，生成物在 `Core/Config/Generated/`（代码）与 `Data/Config/`（`.bytes`）。启动时 `ConfigService` 按 Addressables 标签 `config` 把全部表读进内存，所以加新表不用改框架代码。改表流程见第 8 章。
**禁止**：手改 `Generated/` 与 `Data/Config/`（钩子会拒）；自己 `new cfg.Tables(...)`；往配置对象上挂运行期状态（字段都是 `readonly`，要状态就复制到自己的类里）；在注册顺序排在 `ConfigService` 之前的服务的 `InitializeAsync` 里读表（会抛「还没初始化完」）。

### 6.10 Save — `ISaveService` / `ISaveData`

```csharp
public sealed class Foo { public Foo(ISaveService saves) { ... } }
SettingsSaveData s = saves.Get<SettingsSaveData>();   // 首次访问自动创建，之后恒是同一个实例
s.MasterVolume = 0.5f;                                // 直接改，不用「标记为脏」
bool ok = await saves.SaveAsync(slot: 0, ct);         // 先写 .tmp 再原子替换
bool loaded = await saves.LoadAsync(0, ct);           // 槽位不存在 / 文件损坏都返回 false，不抛
if (saves.Exists(0)) { saves.Delete(0); }
```

JSON 文件在 `IPlatformService.SaveRoot` 下，一个槽位一个 `slot<N>.json`；每个分区各自带版本号，读回来时版本低于代码就调一次 `Migrate(旧版本)`。加分区、写迁移见第 9 章。
**禁止**：自己拼 `Application.persistentDataPath`；在分区里放 `UnityEngine.Object` 引用（分区是纯 DTO，要存资源就存它的 Addressables key）；靠 `try/catch` 接读档异常（读档失败返回 `false`，不抛）；把大块运行期缓存塞进分区（存档要能人读能 diff）。

### 6.11 UI — `IUIService` / `UIView`

```csharp
public sealed class Foo { public Foo(IUIService ui) { ... } }
TitleView view = await ui.OpenAsync<TitleView>(arg: null, ct);   // 预制体地址 = 类名
TitleView opened = ui.Get<TitleView>();                          // 没开返回 null
await ui.CloseAsync(view, ct);
await ui.CloseTopAsync(ct);                                      // 返回键：先关弹窗再关面板
```

`UIService` 启动时在代码里搭出 `UIRoot`（DontDestroyOnLoad）：四层 Canvas（Hud/Panel/Popup/Top，`sortingOrder` 0/100/200/300）各带 `CanvasScaler` + `GraphicRaycaster`，其下一个挂 `SafeAreaFitter` 的 `SafeArea` 节点当内容根；再加一个 `EventSystem` + `InputSystemUIInputModule`，动作集绑到 `IInputService.Actions.asset` 并启用 `UI` map。参考分辨率、匹配系数、过渡时长全从 `UIConfig` 来。新建面板的完整步骤见第 11 章。
**禁止**：自己 `Instantiate` 面板预制体或自己找 Canvas；把面板做成场景里的常驻物体；在玩法场景里再放一个 `EventSystem`（Unity 只认第一个启用的，UIService 会把别的关掉并 Warn）；预制体地址和类名不一致（`OpenAsync` 会报「地址上没有那个组件」）。

### 6.12 Audio — `IAudioService`

```csharp
public sealed class Foo { public Foo(IAudioService audio) { ... } }
audio.PlaySfx(clip, 0.8f);                      // 手上已有 clip
await audio.PlaySfxAsync("Sfx_Click");          // 按地址加载、播完自动释放
await audio.PlayBgmAsync("Bgm_Title", 1.5f);    // 旧曲淡出 → 新曲淡入；0 直接切，负数用配置默认值
audio.StopBgm(0.5f);
audio.MasterVolume = 0.5f;                      // 立刻生效并写回 SettingsSaveData，但不落盘
```

三路音量都是 0～1 线性值，`Master` 乘在另外两路之上；setter 写的就是存档分区，**落盘由设置界面负责**（见第 12 章）。`AudioRoot` 上 1 个 loop 的 BGM 源 + `AudioConfig.SfxVoices` 个 SFX 声部（默认 8，轮转复用）。
**禁止**：自己建 `AudioSource` 或用 `AudioSource.PlayClipAtPoint`；在音量 setter 之后立刻 `SaveAsync`（拖滑块会每帧写文件）；直接改 `SettingsSaveData` 的音量字段而不走服务（改了不会生效）。

### 6.13 2.5D 场景美术接线（画质分档 / 角色纸片 / 贴地）

探索场景是 3D 灰盒 + 2D 角色纸片的 2.5D 表现（`c5d01ae` 落地），接线要点集中在这一节。

**画质分档**：两份 URP 资产各管一档——`Assets/Settings/UniversalRP.asset`（高档：Renderer List = `[Renderer2D, UniversalRenderer]`，软阴影、MSAA 2x、HDR、`UniversalRenderer.asset` 带 SSAO）与 `UniversalRP_Mobile.asset`（手游档：`[Renderer2D, UniversalRenderer_Mobile]`，硬阴影、无 MSAA/HDR/SSAO）。`ProjectSettings/QualitySettings.asset` 六档 Very Low/Low/Medium → 手游档，High/Very High/Ultra → 高档；平台默认 Android=Low、Standalone=High。**改渲染参数去对应的 URP 资产改；新增或调整分档时两份资产要一起改**，改完跑一遍守护测试
`Assets/_Project/Scripts/Tests/EditMode/Rendering/RenderPipelineTiersTests.cs`。`Renderer2D` 仍是索引 0，Boot/UI 场景不受影响。

**角色纸片接法**：贴图 Pivot 设为 Bottom、PPU 100 → 材质用 `Art/Materials/Character/M_SpriteDepthClip.mat`（着色器 `Art/Shaders/SpriteDepthClip.shader`，写深度、受雾不受光）→ 根节点摆在脚底 → 脚下挂 `BlobShadow` / `SelectRing`、头顶挂 World Space Canvas 的 `NameTag`（配 `CameraBillboard`）。完整步骤见
[`isometricexploration-extension-guide.md`](../ai-docs/docs/modules/isometricexploration/isometricexploration-extension-guide.md)，这里只给入口。

**可站立的环境物体放 `Ground` 层**：`EncounterSceneView` 靠 `groundMask` 向下射线贴地（纯规则在 `Assets/_Project/Scripts/Runtime/Monster/EncounterProjection.cs`），只认 `Ground` 层（`ProjectSettings/TagManager.asset` 第 8 槽），没挂这个层的物体贴不上地。

**Sprite 导入默认预设已经是高清手绘**（Bilinear / 压缩 / 生成 mipmap / PPU 100），不是像素风，见 `Assets/_Project/Art/Sprites/README.md`。

### 6.14 对话系统 — `DialogueService` / `DialogueInteractable`

模块 `Game.Dialogue`（`Assets/_Project/Scripts/Runtime/Dialogue/`）。这里只给接入入口，内部结构、接线全表与禁止事项见
[`dialogue-module-guide.md`](../ai-docs/docs/modules/dialogue/dialogue-module-guide.md)，签名与异常见
[`dialogue-external-api.md`](../ai-docs/docs/modules/dialogue/dialogue-external-api.md)，加内容 / 换触发见
[`dialogue-extension-guide.md`](../ai-docs/docs/modules/dialogue/dialogue-extension-guide.md)。

**从代码拉起**：构造注入 `DialogueService`，`var result = await dialogue.PlayAsync(id, ct);`。它负责暂停世界（`IWorldPauseService`）、
关 Gameplay 输入图、跑完整段对白、恢复现场，返回 `DialogueResult`（`Outcome` 出口码、`Skipped`）。进行中重复调用抛
`InvalidOperationException`（先看 `IsRunning`），表里没有该编号抛 `ArgumentException`。要旁听用它的 C# 事件
`OnStarted / OnChoiceSelected / OnEnded`（不是 MessagePipe，自己退订）。

**场景里放 NPC**：根上碰撞体（3D `BoxCollider` / 2D `Collider2D`）+ `DialogueInteractable`（填对话树编号、显示名、交互半径）+
`DialogueInteractableMarker`，子物体放头顶标记 `MarkerIdle` / `MarkerFocus` 与名字 `NameLabel`（3D 场景再挂 `CameraBillboard`）。
玩家根挂一个 `DialogueInteractionActor`，焦点系统据此选最近的 NPC，确认键或右下角「对话」按钮触发。
要支持鼠标 / 触屏点 NPC，场景相机挂 `PhysicsRaycaster`（3D 碰撞体）或 `Physics2DRaycaster`（`Collider2D`），缺了点击静默无效。
现成样子照 `Assets/Scenes/SampleScene.unity` 的 `Npc_Elder` 抄。

**无对话树的 NPC**：编号填 `0`、Inspector 的 `bubbleLines` 填几句台词，再放一个 `Prefabs/World/DialogueSpeechBubble.prefab` 实例作子物体
并拖进标记的 `speechBubble`。交互时头顶气泡按序循环说一句，不暂停世界、不开对话面板。

**两个容易误判的点**：
- 对话服务、焦点系统、EventSystem 都随 Boot 启动。**从 Boot → 标题「开始」进入才有对话**；直接 Play 玩法场景只会记 Warn。
- 运行时 `Instantiate` 出来的 NPC 不会被自动扫描，要自己调 `interactable.Bind(service)`，且不参与焦点。

验证：`/unity-test EditMode Dialogue`；看回放 `/verify-module Dialogue`（验证场景 `Assets/_Project/Scenes/Verify/Dialogue.unity`，编辑器须打开）。
对话内容怎么配见 [`designer-guide.md` 第 11 章](designer-guide.md)。

### 6.15 拼接小人 — `ChibiPuppet`

模块 `Game.CharacterPuppet`（`Assets/_Project/Scripts/Runtime/CharacterPuppet/`），分件 Sprite + Animator 做 Q 版角色的待机 / 走路表现，
**只管表现**：不读输入、不改位置，按角色根的位移自己判走 / 停与朝向。细节见
[`characterpuppet-module-guide.md`](../ai-docs/docs/modules/characterpuppet/characterpuppet-module-guide.md)。

给一个角色换上小人：
1. 把 `Assets/_Project/Prefabs/Characters/ChibiPuppet_Player.prefab`（或 `ChibiPuppet_Patrol.prefab`）实例化到角色的 `Visual` 下，
   localPosition `(0, 0, -0.01)`（略靠前，避免与原纸片同面）。`trackedRoot` 留空即可，自动取父链上第一个不叫 `Visual` 的节点。
2. 原来的纸片 `SpriteRenderer` **不要删**，只取消 `enabled`：`EncounterSceneView` 仍往它上面写 sprite 和 `flipX`，
   它就是朝向的载体。把它拖进小人 `ChibiPuppetMotion` 的 `facingSource`。
3. 没有纸片的场景（如验证场景）`facingSource` 留空，朝向按位移在 `Camera.main` 右方向上的投影判。

手感参数在 `Assets/_Project/Data/CharacterPuppet/ChibiPuppetConfig.asset`（起步 / 停步阈值、采样窗口、走路播放速率）。
Animator 走 unscaled 时间，对话时停期间待机呼吸照播。验证：`/verify-module CharacterPuppet`（`Assets/_Project/Scenes/Verify/CharacterPuppet.unity`）。

## 7. 新建玩法模块

工程里有一个**端到端的样板模块 `Sample`**：`Assets/_Project/Scripts/Runtime/Sample/`，
文档在 [`../ai-docs/docs/modules/sample/`](../ai-docs/docs/modules/sample/sample-module-guide.md)。
它用最少的代码把框架每一层串了一遍（配置表 → 纯 C# 规则 → 意图 → 状态 → 面板 → 场景 → 注册 → 测试），
**新模块照它的形状抄**。下面十步就是它的建法。命令入口 `/new-feature <模块名>` 会把这十步串起来走一遍。

### 7.1 建骨架

菜单 **`21Days/工程/创建模块骨架…`**，输入 PascalCase 的模块名（`Player`、`Inventory`）。它会建：

```
Assets/_Project/Scripts/Runtime/<模块>/<模块>Rules.cs   命名空间 Game.<模块>，带文件头注释的占位规则类
Assets/_Project/Scripts/Tests/EditMode/<模块>/          EditMode 测试
Assets/_Project/Data/<模块>/                            ScriptableObject 配置资产
```

三处任何一处已存在就整单拒绝。**模块不单独建 asmdef**，`Game.Runtime` 已经有了。

### 7.2 规则类（最重要的一步）

玩法规则写成**纯 C# 类**：不继承 MonoBehaviour、不碰 `UnityEngine.Time`、不读单例，
依赖全部构造注入，输入输出都是普通值。这样它能被 EditMode 测试钉住，将来联网也能整体搬到服务端
（[`architecture.md`](architecture.md) 第 7 节）。MonoBehaviour 只做表现与输入转发。

```csharp
public sealed class SampleRules                                   // Runtime/Sample/SampleRules.cs
{
    private readonly IConfigService config;
    public SampleRules(IConfigService config) { this.config = config ?? throw new ArgumentNullException(nameof(config)); }

    public int GetDiscountedPrice(int itemId, float discount)      // 纯函数：同样输入恒得同样输出
    {
        ValidateDiscount(discount);
        int price = GetItem(itemId).Price;                          // id 不存在时抛带 id 和表名的异常
        decimal discounted = price * (1m - (decimal)discount);      // 钱用 decimal 算，理由见 15.10
        return (int)decimal.Round(discounted, 0, MidpointRounding.AwayFromZero);
    }
}
```

非法输入抛**带上出问题那个值**的异常（`ArgumentOutOfRangeException(nameof(itemId), itemId, "…")`），
别让报错停在「给定关键字不在字典中」。

### 7.3 意图对象

要改状态就定义一个 `readonly struct` 意图，由 UI / 输入产生、由规则类消费，不在界面里直接改字段：

```csharp
public readonly struct BuyItemIntent { public BuyItemIntent(int itemId, int count) {...} public int ItemId { get; } public int Count { get; } }
```

**合法性由消费方判，不写在构造函数里**——`default(T)` 绕得过构造函数，写在那里只会给人「构造出来就一定合法」的错觉。

### 7.4 配置

数值进 ScriptableObject，代码里不写死魔法数字：

```csharp
[CreateAssetMenu(menuName = "21Days/Sample/Sample Config", fileName = "SampleConfig")]
public sealed class SampleConfig : ScriptableObject
{
    [SerializeField] private int itemId = 1002;       // 一律 [SerializeField] private + 只读属性
    public int ItemId => itemId;
}
```

资产建在 `Assets/_Project/Data/<模块>/`，**运行时只读**（改 SO 字段会写回资产文件）。

### 7.5 状态

要带场景就继承 `SceneGameState`（基类负责 Additive 加载 / 卸载，`EnterAsync` / `ExitAsync` 是 sealed 的），
纯 UI 的继承 `GameState`：

```csharp
public sealed class SampleState : SceneGameState
{
    public SampleState(IAssetService assets, IUIService ui, IGameFlow flow, SampleRules rules, SampleConfig config) : base(assets) {...}
    protected override string SceneKey => "SampleScene_Game";                    // Addressables 地址
    protected override async UniTask OnSceneReadyAsync(CancellationToken ct)     // 场景就绪：算数据、开面板、订阅
    {
        view = await ui.OpenAsync<SampleView>(rules.…, ct);
        view.OnBackClicked += HandleBackClicked;
    }
    protected override async UniTask OnSceneUnloadingAsync(CancellationToken ct) // 卸载前：退订、关面板（要幂等）
    { if (view != null) { view.OnBackClicked -= HandleBackClicked; await ui.CloseAsync(view, ct); view = null; } }
}
```

场景的 Addressables 地址**别和类名或预制体地址撞**（`Sample` 用 `SampleScene_Game` 就是为了避开 `SampleView`）。

### 7.6 面板

继承 `UIView`，做法见第 11 章。玩法面板放 `Assets/_Project/Scripts/Runtime/<模块>/`，
预制体放 `Assets/_Project/Prefabs/UI/`，**Addressables 地址等于面板类名**，加进 `UI` 组。

**面板里不要注入服务**：它由 Addressables 实例化，不经容器，构造注入拿不到东西。
要显示什么由 `OnOpenAsync` 的 `arg` 传进来，点击往外抛 `event Action`，由状态接住——
`SampleView.OnBackClicked` 就是这个形状。

### 7.7 场景

新建玩法场景放 `Assets/_Project/Scenes/<模块>.unity`，加进 Addressables 的 `Scenes` 组，地址填进状态的 `SceneKey`。

- **不要加进 Build Settings**：Addressables 加载的场景不需要，加了反而会被打两份。
- **场景里不要放 `EventSystem`**：Unity 只认第一个启用的，`UIService` 会把别的关掉并 Warn。
- 场景里的相机**不要带 `AudioListener`**：Boot 场景那个常驻相机上已经有一个，两个会一直报警告。
- **3D 场景（2.5D 表现）相机要选对 Renderer**：`UniversalAdditionalCameraData` 的 Renderer 下拉选**索引 1**（`UniversalRenderer` / `UniversalRenderer_Mobile`），索引 0 是 2D 场景公用的 `Renderer2D`；画质分档细节见 6.13。

### 7.8 注册（把模块接到框架上）

写一个 `GameplayInstaller` 子类，把模块的类型注册进**根作用域**，再把这个组件挂到
`Boot.unity` 的 `GameBootstrap` 物体上、配置资产拖到它的字段里：

```csharp
public sealed class SampleInstaller : GameplayInstaller                 // Runtime/Sample/SampleInstaller.cs
{
    [SerializeField] private SampleConfig config;
    public override void Install(IContainerBuilder builder)
    {
        builder.RegisterInstance(ResolveConfig());
        builder.Register<SampleRules>(Lifetime.Singleton);
        builder.Register<SampleState>(Lifetime.Singleton);              // GoToAsync<T> 按具体类型解析
        builder.RegisterEntryPoint<SampleTitleRouter>(Lifetime.Singleton);
    }
}
```

- `Install` 在容器**构建期间**调用：里面只能 `Register`，不能 `Resolve`、不能碰别的服务。
  要在启动时做事就注册入口点（实现 VContainer 的 `IStartable`）。
- 每帧逻辑实现 `ITickable`，同样用 `RegisterEntryPoint`；**不要**自己写 `Update`。
- 模块内部事件的 broker 注册在**本模块的 Installer** 里（`builder.RegisterMessagePipe()` +
  `RegisterMessageBroker<T>`），全局事件才注册在 `GameLifetimeScope`。
- **想让标题界面的「开始」进你的模块**：写一个入口点订阅 `TitleStartClickedEvent`，
  在回调里 `flow.GoToAsync<你的状态>().Forget()`——范例是 `SampleTitleRouter`。
  订阅句柄必须进 `DisposableBag`，在 `Dispose` 里释放。

### 7.9 埋点（怎么给自己的模块埋点）

出了事故只有两样东西能救你：能复现的步骤，和当时的日志。埋点就是后者。
**跑 `/instrument-module <模块>`**，它按 [`telemetry.md`](telemetry.md) 第 2.2 节的四类尺子扫出候选点，逐条补。
四类是：**意图入口**（玩家做了什么）、**状态迁移**、**失败分支**（`return false` / `throw` / `catch`）、**长耗时**（跨帧的异步）。
**每帧触发的一律不埋**——高频事件会把日志淹掉，要每帧数据用框架自带的 `core.perf` 采样。

拿门面：模块名用**模块目录名的小写**，一个模块一个。

```csharp
private readonly ITelemetryScope telemetry;

public SampleState(/* … */, ITelemetryService telemetry)
{
    this.telemetry = telemetry.Scope("sample");   // 同名 scope 服务内部缓存，不会每次 new
}

telemetry.Track("buy_item", ("id", intent.ItemId), ("n", intent.Count));      // 事实，snake_case
telemetry.TrackError("buy_rejected", $"数量非法：{intent.Count}");            // 失败比成功值钱
using (telemetry.BeginSpan("scene_ready")) { await LoadAsync(ct); }           // Dispose 时自动带 ms
```

**规则类（纯 C#）也能埋，但只有一种写法不破坏「可 EditMode 测试、将来能搬服务端」**：

```csharp
// 规则类：构造函数收 ITelemetryScope 这个小接口（它只 using System，一行 Unity 都没有）
public SampleRules(IConfigService config, ITelemetryScope telemetry) { … }

// 模块 Installer：工厂式注册，显式把 scope 喂进去
builder.Register(c => new SampleRules(
        c.Resolve<IConfigService>(),
        c.Resolve<ITelemetryService>().Scope("sample")),
    Lifetime.Singleton);

// EditMode 测试：三行造一个真服务，断言埋了哪些事件（两个假件在 Tests/EditMode/Telemetry/）
var telemetry = new TelemetryService(
    TelemetryOptions.Default, new FakeTelemetryClock(), new RecordingTelemetrySink());
var rules = new SampleRules(config, telemetry.Scope("sample"));
```

**禁止**：把 `ITelemetryScope` 注册进根作用域（所有模块共用一个根，两个模块各注册一个会互相覆盖）；
在规则类里写 `Log.Error` / `Debug.Log` 或读 `Time.realtimeSinceStartup`（要计时用 `BeginSpan`，时钟在服务那侧）；
因为埋点改判定结果或吞异常（`TrackError` 之后照样 `throw` / `return false`）；
往属性里塞 `UnityEngine.Object`（值只能是 number / string / bool，最多四个，超了拆成两条事件）。

模块里有 `XxxState.cs` / `XxxIntent.cs` 却一条埋点都没有时，保存 `.cs` 时 project-lint 会**提醒**（不拦你）。
埋完想看日志：`/analyze-telemetry --module <模块小写> --last 1`。

### 7.10 测试与文档

- 测试：`Scripts/Tests/EditMode/<模块>/<规则类>Tests.cs`，至少一条覆盖核心规则。写法见第 13 章。
- 文档：`/generate-doc <模块>` 生成三件套，在 [`../ai-docs/docs/catalog.md`](../ai-docs/docs/catalog.md)
  补一行、在 `.claude/skills/generate-doc/modules.json` 登记一条。
- 接完线跑一次菜单 **`21Days/工程/资产体检`**：缺 `.meta`、丢脚本、贴图过大、UI 预制体漏进 Addressables，
  这四类问题都只在运行时或别人拉代码时才炸，体检能提前抓到。
- 收尾 `/unity-test EditMode` 跑绿 → `/verify-module <模块>` 开发者点头 → `/review-change` 列清单待审。

## 8. 配置表怎么改

配置表用 [Luban](https://github.com/focus-creative-games/luban)。**Excel 是唯一数据源**，代码和二进制数据都是生成物。

### 8.1 目录

| 路径 | 是什么 | 进 git |
| --- | --- | --- |
| `Tables/luban.conf` | 生成配置：分组、schema 文件清单、目标 | 是 |
| `Tables/Defines/builtin.xml` | 内置结构（`vector2/3/4`），原样别动 | 是 |
| `Tables/Data/__enums__.xlsx` | 枚举定义 | 是 |
| `Tables/Data/__beans__.xlsx` | 结构（表的行类型）定义 | 是 |
| `Tables/Data/__tables__.xlsx` | 表清单：表名、行类型、数据文件、主键 | 是 |
| `Tables/Data/<表>.xlsx` | 数据 | 是 |
| `Assets/_Project/Scripts/Core/Config/Generated/` | 生成的 C# | **是**（同事和 CI 不用装工具） |
| `Assets/_Project/Data/Config/*.bytes` | 生成的二进制数据 | **是** |
| `Tools/Luban/` | Luban 工具本体，约 30 MB | 否（已 gitignore，脚本自动下载） |

生成物**不手改**，钩子会直接拒。要改内容去改 Excel 再重新生成。

### 8.2 改一张已有表的数据

1. 改 `Tables/Data/<表>.xlsx`，保存关掉 Excel（占着文件会让生成失败）。
2. 跑生成：编辑器里点菜单 **21Days → 配置表 → 生成**，或命令行
   `powershell -ExecutionPolicy Bypass -File scripts/gen-tables.ps1`。
3. 回 Unity 等刷新完，`/unity-test EditMode` 跑绿。
4. 提交时**把生成物一起带上**（`Generated/` 的 `.cs` + `.meta`、`Data/Config/` 的 `.bytes` + `.meta`）。

### 8.3 加一张新表

1. `Tables/Data/__beans__.xlsx` 加行类型：`full_name` 填结构名（如 `Skill`），
   右边 `*fields` 区逐行写字段 `name` / `type` / `comment`。类型写 `int` `long` `float` `bool` `string`、
   已定义的枚举名、`vector2/3`，列表写 `(list#sep=,),string` 这种（`sep` 是单元格内的分隔符）。
2. 要枚举就在 `Tables/Data/__enums__.xlsx` 加：`full_name` 一行，右边 `*items` 区逐行写 `name` / `alias` / `value`。
3. `Tables/Data/__tables__.xlsx` 加一行：`full_name`=`TbSkill`、`value_type`=`Skill`、
   `read_schema_from_file`=`FALSE`、`input`=`skill.xlsx`（相对 `Tables/Data/`）、`index`=主键字段名、`group`=`c`。
4. 建 `Tables/Data/skill.xlsx`：A1 写 `##`，B 列起是字段名；第二行 A 列也写 `##`，其余填中文注释；第三行起是数据（A 列留空）。
5. 跑生成，新表会自动出现在 `config.Tables.TbSkill`，**框架代码一行不用改**——
   `Data/Config` 整个文件夹作为一个 Addressables 条目打了 `config` 标签，新 `.bytes` 自动被收进去。

> 表头里 `*fields` / `*items` 这种「一对多」的父列必须是**合并单元格**，跨完它下面所有子列。
> 不合并的话 Luban 只认得第一个子列，报「缺失列:'alias'」这类看不懂的错。

### 8.4 环境要求

- **.NET**：Luban 是 net8.0 程序。脚本会设 `DOTNET_ROLL_FORWARD=Major`，本机只有 .NET 9 也能跑。
  万一报 `framework 'Microsoft.NETCore.App' version '8.0.0' was not found`，装个 .NET 8 运行时：
  `winget install Microsoft.DotNet.Runtime.8`。
- **解压**：工具包是 `.7z`。脚本先用 Windows 自带的 `tar.exe` 解，不行再找 `%ProgramFiles%\7-Zip\7z.exe`；
  两条都不通就报错让你装：`winget install 7zip.7zip`。
- **网络**：首次生成要从 GitHub 下 30 MB。下不动就手动下载
  `https://github.com/focus-creative-games/luban/releases/download/v5.1.0/Luban.7z`，解压到 `Tools/Luban/`（`Luban.dll` 要在这一层）。
  换版本改 `scripts/gen-tables.ps1` 顶部的 `$LubanVersion`，再跑一次 `-Force`。

### 8.5 常见报错

| 报错 | 原因与修法 |
| --- | --- |
| `缺失列:'xxx'` | `__beans__` / `__enums__` 的 `*fields` / `*items` 父列没做成合并单元格，见 8.3 末尾 |
| `不存在对应的数据文件` | `__tables__` 的 `input` 写错了，路径相对 `Tables/Data/`，别带 `Data/` 前缀 |
| `xxx 不是合法的类型` | 字段类型拼错，或枚举名和 `__enums__` 里的 `full_name` 对不上 |
| Excel 被占用 / IO 异常 | 表还开在 Excel 里，关掉重跑 |
| 运行时 `配置表 "xxx" 的数据文件没找到` | 代码生成了但 `.bytes` 没进 Addressables 的 Config 组，或者压根没重新生成——重跑 8.2 |
| 运行时 `标签 "config" 下一个资源都没有` | Addressables 里 `Assets/_Project/Data/Config` 这个条目丢了标签。打开 Window → Asset Management → Addressables → Groups，把 Config 组里那个条目的 Label 勾回 `config` |

## 9. 存档

### 9.1 文件在哪、长什么样

`<IPlatformService.SaveRoot>/slot<槽位>.json`；`SaveRoot` 是 `Application.persistentDataPath/saves`
（Windows 在 `%userprofile%\AppData\LocalLow\DefaultCompany\<产品名>\saves`，Android 在应用私有目录）。

```json
{
  "formatVersion": 1,
  "partitions": {
    "Game.Core.Save.SettingsSaveData": { "version": 1, "data": { "MasterVolume": 1.0, "Language": "zh-CN" } }
  }
}
```

键是分区类型的**全名**，所以给分区改命名空间或类名 = 换了一个分区，老数据会被当成「代码里已经没有的分区」跳过（只 Warn，不报错）。真要改名就把旧名当一个待迁移的老分区处理，或者别改。

### 9.2 加一个分区

1. 在 `Core/Save/`（框架级）或自己模块目录下写一个纯 C# 类实现 `ISaveData`：
   ```csharp
   public sealed class PlayerProgressSaveData : ISaveData
   {
       public int Version => 1;
       public int Level { get; set; } = 1;
       public List<string> UnlockedSkills { get; set; } = new List<string>();
       public void Migrate(int fromVersion) { }
   }
   ```
2. 只要可读写属性 + 属性初始化器给默认值，**不用注册**：`saves.Get<PlayerProgressSaveData>()` 第一次访问就会创建。
3. 不放 `UnityEngine.Object` 引用（存不下），要指资源就存它的 Addressables key。

### 9.3 版本迁移怎么写

- **只加字段**：`Version` 不用动。老存档里没有的字段，Newtonsoft 会保留属性初始化器给的默认值。
- **改语义**（改名、换单位、值域变了）：`Version` 加一，在 `Migrate` 里按 `fromVersion` **逐级**往上迁：
  ```csharp
  public int Version => 3;
  public void Migrate(int fromVersion)
  {
      if (fromVersion < 2) { Hp = Hp * 10; }              // v1 的血量是百分比
      if (fromVersion < 3) { Language = Language ?? "zh-CN"; }
  }
  ```
  写成 `if (fromVersion < N)` 的阶梯而不是 `switch (fromVersion)`：玩家可能从很老的版本一步升上来。
- `Migrate` 只在「存档里的版本 < 代码里的版本」时被调用**一次**。存档比代码新（玩家降级了）只 Warn 不迁，读进来的字段对不上的退回默认值。

### 9.4 规矩

- 写盘先落 `.tmp` 再原子替换，所以断电最多留个 `.tmp`，正档不会半截。磁盘 IO 在线程池上，序列化留在主线程（分区对象是玩法在改的）。
- `LoadAsync` 对「没有文件」「JSON 坏了」「信封版本太新」一律记日志返回 `false`，**不抛**——存档坏掉不该把游戏带崩，拿到 `false` 就当新档开。
- 什么时候存由调用方决定（存档点、退出、设置改完）。别每帧存。

## 10. 输入

### 10.1 资产与生成物

- 动作定义在 `Assets/_Project/Data/Input/GameInput.inputactions`，**双击它在 Input Actions 窗口里改**，不要手改 JSON。
- 它的导入器勾了 **Generate C# Class**，参数是：类名 `GameInput`、命名空间 `Game.Core.Input`、输出路径 `Assets/_Project/Scripts/Core/Input/GameInput.cs`。
- `GameInput.cs` 是**生成物**：改了 `.inputactions` 保存，Unity 自动重新生成它。**不要手改这个文件**，改了下次保存资产就没了。

### 10.2 两个 Action Map

| Map | 动作 | 绑定 |
| --- | --- | --- |
| `Gameplay` | `Move`(Vector2)、`Confirm`、`Cancel`、`Pause` | 键鼠（WASD / 方向键 / Enter / Esc / P）、手柄（左摇杆 / 十字键 / A / B / Start）、触屏（primaryTouch tap）。`Move` 上留了一条空路径的 `TouchVirtualStick` 绑定，等虚拟摇杆落地后在 Inspector 里补上 |
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

- `InputService` 在启动初始化时 `new GameInput()` 并启用 `Gameplay` map；`UI` map 默认不开，由 `IUIService` 在初始化时 `EnableMap("UI")` 接上 EventSystem。
- 要临时屏蔽玩法输入（开面板、播过场）：`input.DisableMap("Gameplay")`，结束后再 `EnableMap`。
- **只读动作，不读按键**：玩法代码里出现 `Keyboard.current`、`Input.GetKey`、`Input.touches` 一律算违规——那样手柄和触屏就得各写一遍。要加新的输入方式，去 `.inputactions` 里给同一个动作加 binding。
- 新增一个动作 = 在 `.inputactions` 里加 → 保存（自动重新生成 `GameInput.cs`）→ 玩法里 `input.Actions.Gameplay.<新动作>`。框架代码一行不用改。

## 11. UI 面板

### 11.1 加一个新面板的完整步骤

1. **写脚本**：`Assets/_Project/Scripts/Runtime/<模块>/<名字>View.cs`（面板多到一眼看不过来时再开个 `UI/` 子目录；框架自带的占位面板在 `Core/UI/Views/`，玩法范例是 `Runtime/Sample/SampleView.cs`），继承 `UIView`，实现 `Layer`；按需重写三段生命周期。

   ```csharp
   public sealed class ShopView : UIView
   {
       [SerializeField] private Button closeButton;
       public override UILayer Layer => UILayer.Panel;
       public override bool IsFullScreen => true;                 // 半透明面板改成 false
       public override UniTask OnOpenAsync(object arg, CancellationToken ct)
       {
           closeButton.onClick.RemoveListener(OnClose);           // 先摘再加，复用打开时不会叠监听
           closeButton.onClick.AddListener(OnClose);
           return UniTask.CompletedTask;
       }
       public override UniTask OnCloseAsync(CancellationToken ct)
       {
           closeButton.onClick.RemoveListener(OnClose);
           return UniTask.CompletedTask;
       }
   }
   ```

   监听**只写在 `OnOpenAsync` / `OnCloseAsync`**，不写 `OnEnable` / `OnDisable`：面板被全屏面板盖住时会 `SetActive(false)`，那两个回调会重复触发。

2. **建预制体**：放 `Assets/_Project/Prefabs/UI/<名字>View.prefab`。根节点是**全拉伸的 `RectTransform` + `CanvasGroup` + 面板脚本**，**不要带 `Canvas`**（Canvas 在 `UIRoot` 上，一层一个）。
3. **加进 Addressables**：Window → Asset Management → Addressables → Groups，拖进 `UI` 组，**地址改成类名**（`ShopView`）。地址对不上时 `OpenAsync` 会抛「地址上没有那个组件」。
4. **打开**：`await ui.OpenAsync<ShopView>(arg, ct)`。已经开着就返回同一个实例并再走一次 `OnOpenAsync`，不会开出两份。

### 11.2 层级规则

| 层 | 用途 | 进栈？ |
| --- | --- | --- |
| `Hud` | 血条、摇杆、小地图 | 否，常驻 |
| `Panel` | 标题、背包、设置 | 是，**单栈**：压入全屏 Panel 时它下面的 Panel 全部 `SetActive(false)`，弹出后恢复 |
| `Popup` | 确认框、飘窗 | 是，可叠加，互不隐藏，也不影响 Panel |
| `Top` | 加载遮罩、转圈、调试台 | 否，永远在最上面 |

`CloseTopAsync()` 先看 Popup 再看 Panel；`Hud` / `Top` 不进栈，所以返回键关不掉它们。规则本身写在纯 C# 的 `UIStack` 里，有 `UIStackTests` 钉着——改规则先改测试。

### 11.3 过渡动画

`UIView` 默认用 LitMotion 做 `CanvasGroup.alpha` 的淡入淡出，时长取 `UIConfig.TransitionSeconds`（默认 0.15 秒，设 0 则跳过动画直接显隐），调度器是 `UpdateIgnoreTimeScale`——暂停菜单在 `timeScale = 0` 时也得能淡出来。要换成缩放、滑入就重写 `PlayOpenTransitionAsync(float seconds, CancellationToken ct)` / `PlayCloseTransitionAsync`。同一时刻只跑一个过渡，新的会掐断旧的（旧的 `await` 正常结束，不抛异常）。

### 11.4 安全区

每层 Canvas 下的 `SafeArea` 节点挂着 `SafeAreaFitter`，按 `Screen.safeArea` 设 `anchorMin/anchorMax`，只在 `OnEnable` 与 `OnRectTransformDimensionsChange` 时重算（不轮询 `Update`）。面板生在这个节点下面，所以**不用自己适配刘海**。换算逻辑是纯函数 `SafeAreaMath.ToAnchors`，屏幕尺寸为 0 时退回全屏而不是产生 NaN（NaN 赋给 anchor 会让整个界面消失）。

### 11.5 中文字体

**已解决，拉下来就能用，不需要各自生成。**

工程里放了 `Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular.otf`（Noto Sans SC Regular，8.0 MB，
SIL OFL 1.1，许可证在同目录 `OFL.txt`）与它的 TMP 资产 `Font_NotoSansSC_Regular SDF.asset`。

**接法是 fallback，不是换默认字体**：`TMP Settings` 的 **Fallback Font Assets** 里挂着中文资产，
`Default Font Asset` 仍是 `LiberationSans SDF`。这样英文数字保留 Liberation 的字形，缺字才回落。
选 fallback 而不是改默认字体，是因为 `TitleView` / `SampleView` 的预制体上 **TMP 组件已经显式引用了
`LiberationSans SDF`** —— 组件上指定了字体时，`Default Font Asset` 只是查找链的最后一环，
改它不如改 fallback 直接；而 fallback 对**所有** TMP 组件生效，不管它们各自挂的是哪个字体资产。
TMP 的查找顺序是：组件自己的字体 → 该字体的局部 fallback → **TMP Settings 全局 fallback** →
Default Font Asset（`TMP_Text.cs:6198` 一带）。

**图集模式是 Dynamic，1024×1024**。中文两万多字，Static 会烘出巨大图集且拖慢导入；
Dynamic 按需栅格化，首帧用到几个字就只烘几个。加字重或换字族时这一条最容易做错，
细则在 `Assets/_Project/Art/Fonts/README.md`。

**提交前记得清动态数据**：Dynamic 字体资产在编辑器里进一次 Play 就会把用到的字烘进 `.asset`
（6 KB → 2 MB），在 `git status` 里冒出来。字体资产 Inspector 上点 **Clear Dynamic Data** 再提交。
出包后运行时只在内存里加字，不写回资产。

## 12. 音频

### 12.1 三路音量与存档的关系

`MasterVolume` / `BgmVolume` / `SfxVolume` 三个属性**读写的就是 `SettingsSaveData` 分区**，不是服务自己的字段：

```csharp
audio.MasterVolume = 0.5f;     // ① 夹到 0～1 ② 写进 SettingsSaveData ③ 立刻应用到 AudioSource
```

**服务不负责落盘**——设置界面拖滑块时每帧写一次文件是灾难。正确做法是设置面板关闭时调一次 `saves.SaveAsync(slot, ct)`。反过来，`LoadAsync` 读回存档后要让音量生效，重新赋一次 `audio.MasterVolume = settings.MasterVolume` 即可。

音量怎么落到声音上：`AudioConfig.Mixer` 留空（现状）时用 AudioSource 音量相乘——BGM 源音量 = `Master × Bgm`，SFX 声部音量 = `Master × Sfx`，`PlaySfx` 的 `volume` 参数再乘一次。换算在纯函数 `AudioVolumeMath.Effective` 里，有测试钉着。

### 12.2 BGM 切换

```csharp
await audio.PlayBgmAsync("Bgm_Title");        // 不传就是契约里的默认值 0.5 秒
await audio.PlayBgmAsync("Bgm_Title", -1f);   // 负数 = 用 AudioConfig.DefaultBgmFadeSeconds
await audio.PlayBgmAsync("Bgm_Title", 0f);    // 0 = 直接切，不淡
```

只有一路 BGM 源，所以是**先淡出旧的再淡入新的**（总耗时约 `2 × fade`），不是真正的交叉淡化；要交叉得改成两路源轮流用。已经在放同一首时是空操作。曲子的 `AssetHandle` 由服务持有，换曲与 `StopBgm` 时释放。

### 12.3 SFX 池

`AudioRoot` 上有 `AudioConfig.SfxVoices` 个 `AudioSource`（默认 8），`PlaySfx` 轮转取一个 `PlayOneShot`——用满一圈后最早用过的那个被再次拿走，前一发声音继续混着播完，不会被掐。
`PlaySfxAsync(key)` 按地址加载后播放，用 `UniTask.Delay(clip.length)` 等它播完再释放句柄（而不是缓存最近 N 个句柄：N 取多少都不对，短音效被提前释放、长音效白占内存）。不关心播完时机就 `.Forget()`。

### 12.4 将来接 AudioMixer

Unity 没有公开 API 从代码创建 AudioMixer 资产，所以这一步必须手工做一次：

1. Assets → Create → Audio Mixer，放 `Assets/_Project/Data/Audio/`。
2. 建 `Bgm`、`Sfx` 两个子组，在 Inspector 里把三个 Volume 参数 Expose 出来，改名成 `MasterVolume` / `BgmVolume` / `SfxVolume`（`AudioService` 按这三个名字 `SetFloat`）。
3. 把 Mixer 拖到 `AudioConfig.Mixer` 字段上。
4. 把 BGM 源与 SFX 声部的 `outputAudioMixerGroup` 指到对应组（目前 `AudioService` 不做这一步，接 Mixer 时补上）。

接上之后 `AudioService` 自动改走分贝：各 `AudioSource` 音量恒为 1，音量由 `AudioVolumeMath.ToDecibels`（0 → -80，1 → 0）算出来喂给 Mixer 参数。

## 13. 测试

### 13.1 EditMode 还是 PlayMode

**能抽成纯逻辑的一律写 EditMode**：不进播放模式、不加载场景、不等帧，一次全量跑完只要几秒。
只有必须走 Unity 生命周期的（物理、动画、场景加载、协程时序）才写 PlayMode，用
`[UnityTest]` + `yield return null`。规则见 [`../.claude/rules/unity-tests.md`](../.claude/rules/unity-tests.md)。

| 要测什么 | 写哪种 | 例子 |
| --- | --- | --- |
| 玩法规则、纯算法、数据换算 | EditMode | `SampleRulesTests`、`UIStackTests`、`AudioVolumeMathTests` |
| 服务的可测部分（喂假依赖） | EditMode | `TimerServiceTests`（假 `IClock`）、`ConfigServiceTests`（读磁盘 `.bytes`） |
| 异步且要跨帧的 | EditMode + `[UnityTest]` | `JsonSaveServiceTests`（存档 IO 走线程池，见 15.4） |
| 真要起场景、起物理 | PlayMode | 目前工程里还没有 |
| 要肉眼看表现对不对 | **都不是** | 走 Showcase 回放：[`module-dev-spec.md`](module-dev-spec.md) + `/verify-module` |

一个玩法模块**至少一条 EditMode 测试**覆盖核心规则，这是模块完成的硬条件。

### 13.2 目录与命名

```
Assets/_Project/Scripts/Tests/EditMode/<模块>/<被测类>Tests.cs
Assets/_Project/Scripts/Tests/PlayMode/<模块>/<被测类>Tests.cs
```

- 测试类 `<被测类>Tests`，方法 `<行为>_<条件>_<期望>`
  （`GetDiscountedPrice_WhenDiscountIsOne_ReturnsZero`）。名字要能当报告读。
- `[SetUp]` 建被测对象，`[TearDown]` 销毁 `new GameObject` 出来的东西。
- 断言用 `Assert.That(actual, Is.EqualTo(expected))`，**一条测试一个关注点**；
  断言里带一句中文说明，失败时不用再去翻代码：
  `Assert.That(..., Is.EqualTo(0), "折扣 1 = 免费，不是原价")`。

### 13.3 假服务怎么写

框架服务都是接口，接口都小，**假实现通常就几行**，写在测试文件里当私有嵌套类，不要另开文件、
更不要为了测试去拉起真服务（那就成了集成测试，慢且脆）。

```csharp
// Tests/EditMode/Sample/SampleRulesTests.cs 里的真实写法
private sealed class FakeConfigService : IConfigService
{
    public FakeConfigService(global::cfg.Tables tables) { Tables = tables; }
    public global::cfg.Tables Tables { get; }
}
```

几个现成的路子：

- **配置表**：不经 Addressables，直接从磁盘读 `Assets/_Project/Data/Config/*.bytes` 喂
  `ConfigService.BuildTables(...)`（`SampleRulesTests.ReadAllTableBytes` 就是这么干的）。
  路径从 `Application.dataPath` 推，**不写死本机绝对路径**。
- **时间**：假一个 `IClock`，手动推 `GameTime`，就能测出「3 秒后触发」而不用真等 3 秒。
- **存档**：`ISaveService` 的假实现拿个 `Dictionary` 当槽位即可。
- **资源 / UI**：需要它们才说明被测的东西不够纯——先想想能不能把规则抽出来。

### 13.4 怎么跑

| 场景 | 怎么跑 |
| --- | --- |
| 编辑器开着、MCP 已连 | `/unity-test [EditMode\|PlayMode] [过滤]`，走 MCP 的 `run_tests`（先 `manage_tools activate testing`） |
| 编辑器没开 | 同一个命令，它会走 batchmode |
| 编辑器开着但 MCP 没连 | **停下**，让人在 Test Runner 里跑（Window → General → Test Runner） |

**编辑器开着时 batchmode 必定失败**（工程锁被占），这是设计如此，重试没有意义，
别为了跑通去关别人的编辑器（[`../ai-docs/pitfalls.md`](../ai-docs/pitfalls.md) 有这条）。

测试失败**如实报告，不靠改弱断言凑绿**。当前 EditMode 全量 77 条（含 Addressables 包自带的一条桩测试）。

### 13.5 `Sample` 模块的测试当范例

`Assets/_Project/Scripts/Tests/EditMode/Sample/SampleRulesTests.cs` 七条，覆盖面就是玩法测试该有的样子：

1. 正常路径（0 折扣 → 原价）；
2. 有意思的中间值（0.15 折扣 → 42.5 → 43，**同时钉住「钱用 decimal 算」**，改回 `double` 这条就红）；
3. 边界（折扣 1.0 → 0）；
4. 非法 id → 抛异常，且**断言报错文案里有 id 和表名**；
5. 折扣越界（负数 / 大于 1 / `NaN`）→ 抛异常；
6. 意图对象的正常路径（单价 × 数量）；
7. 意图对象的非法值（`default(T)` 的 `Count` 是 0）→ 抛异常。

写新模块的测试时照这七类对一遍：正常、有意思的中间值、边界、每一类非法输入。

## 14. 打包与 CI

### 14.1 本机出包

```
/build Windows           # 或 /build Android，可带版本号：/build Windows 0.2.0
```

`/build` 底下跑的是 `scripts/build.ps1`，它再调用编辑器的 `Game.Editor.BuildScript`：

| 平台 | 入口 | 默认产物 |
| --- | --- | --- |
| Windows | `BuildScript.BuildWindows`（`StandaloneWindows64`） | `Builds/Windows/21Days.exe` |
| Android | `BuildScript.BuildAndroid`（只出 APK，不出 AAB） | `Builds/Android/21Days.apk` |

**编辑器必须先关掉**：批处理实例拿不到 `Temp/UnityLockfile`，开着编辑器跑必定失败，重试没用。

命令行参数由 `build.ps1` 拼装透传：`-outputPath <相对工程根的路径>`、`-buildVersion <字符串>`
（写进 `PlayerSettings.bundleVersion`）、`-buildNumber <整数>`（Android 的 `bundleVersionCode`，
不给而给了 `-buildVersion` 时改为自增）、`-development`（打开 `BuildOptions.Development`）、
`-releaseBuild`（Android 的可上架配置，对应 `build.ps1` 的 `-Release` 开关，见 14.2）。

### 14.2 两种 Android 配置：默认的快，`-Release` 的能过 64 位

Android 出包有两档，**默认那档是为了快**：

```powershell
powershell -File scripts/build.ps1 -Target Android            # 默认：快
powershell -File scripts/build.ps1 -Target Android -Release   # 可上架的 64 位配置：慢一个量级
```

| | 默认（不加 `-Release`） | 加 `-Release` |
| --- | --- | --- |
| 脚本后端 | Mono（`ProjectSettings` 里的默认值） | IL2CPP |
| CPU 架构 | 沿用 `ProjectSettings`（当前 ARMv7） | **仅 ARM64** |
| `lib/` 下 | `armeabi-v7a/`，含 `libmonobdwgc-2.0.so` | `arm64-v8a/`，含 `libil2cpp.so` |
| 满足 Play 的 64 位要求 | **不满足** | 满足 |
| 出包耗时（2026-09-16 实测，Library 已热） | **75 秒** | **268 秒，约 3.6 倍** |
| APK 体积（同上） | 33.4 MB | 41.6 MB（+25%） |

耗时那一行是**本工程当前**（几乎没有玩法代码）的数字，别当成上限：IL2CPP 的开销随 C# 代码量增长，
代码上来之后十几分钟很常见，而 Mono 那档基本不动。所以这个差距只会越来越大，不会缩小。
体积反过来：ARM64 单架构下 `libil2cpp.so` 比 Mono 的运行时大，但省掉了 32 位那一份，净增 8 MB 左右。

**各自什么时候用**：日常自测、看美术效果、验流程走默认档，别加 `-Release` 白等；
要出上架候选包，或者要在真机上量真实性能（Mono 与 IL2CPP 的运行速度不是一回事），才加。

**为什么 `-Release` 的包能满足 64 位要求**：Google Play 自 2019 年 8 月起要求所有新应用和更新
提供 64 位版本。Unity 的 Mono 后端在 Android 上**只支持 32 位**，想出 `arm64-v8a` 就必须切
IL2CPP —— 所以这两项是绑定的，不能只改架构不换后端。
架构只勾 ARM64、不勾「ARMv7 + ARM64」：双架构会把 IL2CPP 的原生产物（`libil2cpp.so`、
`libunity.so`）按架构各打一份进 APK，体积接近翻倍，而 2026 年的新项目只支持 64 位是主流做法。
真要照顾 32 位老设备，改 `BuildScript.cs` 里 `targetArchitectures` 那一行即可。

> **⚠ 加了 `-Release` ≠ 可以上架。** 它只解决 64 位这一条。还差两样：
> **① 格式**——Google Play 对新应用要求 **AAB**（`.aab`），而 `BuildScript` 目前只出 **APK**；
> **② 签名**——出来的包没配 keystore，是调试签名，商店不收。
> 真要上架时：在 `ConfigureAndroid` 里把 `EditorUserBuildSettings.buildAppBundle` 翻成 `true`
> （建议再加个 `-appBundle` 开关，别写死），并配上签名 keystore（见
> [`ci-setup.md`](ci-setup.md) 的「上架相关的缺口」）。**这两条现在都没做**，别以为有了开关就万事大吉。

**设置是临时改的，不会留在工作区**。`-Release` 改的 `scriptingBackend` / `targetArchitectures`
都存在 `ProjectSettings/ProjectSettings.asset` 里，属于全工程共享的状态。`BuildScript` 的做法是：
进来先把原值和该文件的**原始字节**一起存下，`BuildPlayer` 包在 `try` 里，`finally` 里无条件写回
并 `AssetDatabase.SaveAssets()`，再按原始字节比对、不一致就整体回写；构建失败或抛异常同样走这条路。
所以出完包 `git status` 里**不该**多出 `ProjectSettings/ProjectSettings.asset`。
两个细节值得知道，都不显然：

- 只按 API 恢复语义是不够的。`scriptingBackend` 原本是空字典 `{}`，用 `SetScriptingBackend`
  写回 `Mono2x` 会留下一条显式的 `Android: 0` —— 语义一样，文本多一行，`git diff` 照样脏。
  所以恢复的最后一步是按原始字节整体回写。
- 恢复必须发生在报告结果**之前**。批处理模式下汇报的最后一步是 `EditorApplication.Exit()`，
  那是直接终止进程、不展开调用栈的，把它写进 `try` 块里 `finally` 就永远跑不到。

### 14.3 Addressables 内容已经接进打包

`BuildScript` 在出包前会**自动跑一次 `AddressableAssetSettings.BuildPlayerContent()`**，
所以不需要手动 Build 内容。这意味着：

- 新加的 Addressables 条目（面板预制体、玩法场景、配置表数据）**不用额外操作**就会进包；
- 反过来，**漏加进组的资源在编辑器里照跑、在包里直接找不到**——
  编辑器的 Play Mode Script 是 `Use Asset Database (fastest)`，它不看组，直接从工程里取。
  这是「编辑器好好的、出包就白屏」的头号原因，接完线跑一次 `21Days/工程/资产体检` 能提前抓到。
- 玩法场景走 Addressables 加载，**不进 Build Settings**；Build Settings 里只有 `Boot.unity`。

### 14.4 出错了怎么读

`/build` 失败时会摘日志里的前几条错误。常见的三类：

| 报错 | 多半是 |
| --- | --- |
| 拿不到工程锁 / `Temp/UnityLockfile` | 编辑器还开着 |
| `Android SDK/NDK not found` | 装编辑器时没勾 Android Build Support 的子模块（见 1.2） |
| 运行包体时面板 / 场景加载不出来 | 资源没进 Addressables 组（见 14.3） |

### 14.5 CI（已搁置）

**2026-09-16 起工程里没有 CI**：两条 GitHub Actions 流水线已删除，许可证 secret 已清空，
push 不再触发任何自动化。原因是 game-ci 激活 Unity 许可证时账号登录返回 **401**——
Personal 许可证是机器绑定的，而 CI 每跑一次都是一台新机器，绕这个限制要在线登录账号，
这条链断在 Unity 侧，不是工程配置问题。完整根因、当时验证通过的环节、将来重做的前置条件，
见 [`ci-setup.md`](ci-setup.md)。

**所以出包和跑测试都只有本机一条路**：`/build` 出包（编辑器须关闭），`/unity-test` 跑测试。
没有兜底的第二道门了——本机漏跑的测试不会有人替你拦，提交前自己跑。

## 15. 常见问题

### 15.1 新建的 `*LifetimeScope.cs` 内容被清空成模板（波 1 踩到）

**现象**：写好一个名字以 `LifetimeScope.cs` 结尾的脚本，Unity 刷新一次之后打开发现内容变成了空的 `public class Xxx : LifetimeScope { protected override void Configure(...) { } }`，命名空间也被换成了 asmdef 的 rootNamespace。

**根因**：VContainer 包里有个 `ScriptTemplateProcessor`（`AssetModificationProcessor.OnWillCreateAsset`），Unity 为**新建**的 `*LifetimeScope.cs` 生成 `.meta` 时，它会用自带模板 `File.WriteAllText` 覆盖文件内容。这是它的「新建 LifetimeScope 自动套模板」功能，对在编辑器外写好的文件是误伤。

**正确做法**：只在**首次创建**时发生一次。流程改成「先建文件 → 让 Unity 刷新生成 `.meta` → 再把真正的内容写进去 → 再刷新」。文件存在之后再怎么改都不会被覆盖。想彻底关掉：`Project Settings → VContainer` 里勾 `Disable Script Modifier`（本工程没关，因为只在新建时影响一次）。

### 15.2 MCP `read_console` 有时读不到刚打的日志（波 1 踩到）

**现象**：波 1 实施时 `read_console(action="get")` 一度稳定返回 0 条，连刚用 `execute_code` 打的 `Debug.Log` 也读不到；随后主窗口在测试跑完后实测又能读到。

> **真因见 15.7**（波 3 定位）：是 Console 窗口右上角的等级过滤按钮被关了。先去点亮那三个按钮，再看下面这些替代手段。剩下这一节留着是因为那些手段本身仍然有用。

**替代验证手段**（不要因为读不到控制台就宣称「编译通过」）：

- 编译是否成功：`execute_code` 里读 `UnityEditor.EditorUtility.scriptCompilationFailed`，再用 `System.Type.GetType("命名空间.类名, 程序集名")` 确认新类型真的被编译进了目标程序集。
- 运行时日志：`execute_code` 里临时挂 `Application.logMessageReceived`，触发一次要观察的流程，收集完再摘掉。
- 实在要看历史日志：让用户看编辑器 Console 窗口，或用运行时的 IngameDebugConsole。

### 15.3 Luban 的 `__beans__` / `__enums__` 报「缺失列」（波 2 踩到）

**现象**：`__beans__.xlsx` 照着官方 MiniTemplate 抄的表头，跑生成却报
`bean:'__intern__.__FieldInfo__' 缺失列:'alias'，请检查是否写错或者遗漏`，报错位置指着第一个字段所在的单元格。

**根因**：`*fields`（`__enums__` 里是 `*items`）这种「一对多」的父列，在官方模板里是**合并单元格**，
横跨它下面所有子列（`__beans__` 是 `J1:P1`，`__enums__` 是 `H1:L1`）。不合并的话 Luban 只把第一个子列
认成 `*fields` 的成员，后面的 `alias` / `type` 全部当成了顶层列，于是报「子结构缺列」。
用 openpyxl 之类的脚本重建表头最容易漏掉这一步——肉眼看内容完全一样。

**正确做法**：表头里凡是 `*` 开头的父列都要合并到覆盖全部子列。openpyxl 里是 `ws.merge_cells("J1:P1")`。

### 15.4 EditMode 测试里同步等 UniTask 会死锁（波 2 踩到）

**现象**：`JsonSaveServiceTests` 里用 `task.AsTask().GetAwaiter().GetResult()` 等存档写完，
测试直接挂住不动，Test Runner 转圈到超时。

**根因**：存档的磁盘 IO 走 `UniTask.RunOnThreadPool`，跑完要切回主线程继续。编辑器下 UniTask 的
PlayerLoop 是靠 `EditorApplication.update` 推的（`PlayerLoopHelper.InitOnEditor`），
在主线程上阻塞等，就等于把推它的那只手按住了——续接永远排不上队。

**正确做法**：异步用例写成 `[UnityTest] public IEnumerator Xxx() => UniTask.ToCoroutine(async () => { ... });`。
EditMode 的 `[UnityTest]` 由测试运行器按编辑器更新逐帧推进，主线程不被占住，续接就能回来。

### 15.5 Addressables 的 `config` 标签丢了，配置表加载不出来

**现象**：进 Play 模式启动到 `ConfigService` 时抛
`Addressables 里标签 "config" 下一个资源都没有`。

**根因**：`Assets/_Project/Data/Config` 是作为**一个文件夹条目**加进 Addressables 的 `Config` 组的，
标签打在这个条目上、由子资源继承。有人在 Addressables 窗口里删了条目或取消了标签，就全断了。

**正确做法**：Window → Asset Management → Addressables → Groups，确认 `Config` 组里有一个
address 为 `Config` 的文件夹条目、Labels 勾着 `config`。条目整个没了就把 `Assets/_Project/Data/Config`
文件夹重新拖进 `Config` 组再勾标签。Play Mode Script 保持 **Use Asset Database (fastest)**，
这样改完表不用每次 Build 内容。出包时 `BuildScript` 会自动先跑 `BuildPlayerContent()`。

### 15.6 编辑器失焦时 Play 模式不走帧，所有 `await` 卡住（波 3 踩到）

**现象**：用 MCP 进 Play 模式后，`await ui.CloseTopAsync()` 之类的调用永远不返回，面板的淡出动画停在第一帧；
连查两次 `Time.frameCount` 数值一模一样。代码本身没有报错，看起来像死锁。

**根因**：`Application.runInBackground` 默认 false，编辑器窗口**没有焦点**时 Unity 不推进 PlayerLoop。
不走帧 → LitMotion 不更新、UniTask 的续接排不上队 → 所有 `await` 停在原地。
用 MCP 遥控时编辑器一直是失焦的，所以必现。

**正确做法**：验证前先在 Play 模式里执行一次 `Application.runInBackground = true`（运行时改，退出即失效，
不动 Player Settings）。判断是不是这个坑：隔几秒读两次 `Time.frameCount`，不变就是它。

### 15.7 `read_console` 读不到日志，是控制台的等级开关被关了（波 3 定位）

**现象**：`read_console(types=["all"])` 稳定返回 0 条，但 Console 窗口里明明有日志——
这就是 15.2 记的那个「读不到刚打的日志」，波 3 查到了真因。

**根因**：Console 窗口的三个等级开关（Log / Warning / Error 那三个按钮）是**过滤器**，
关掉之后 `LogEntries` 一条都不返回，MCP 读的就是这个接口。本工程的编辑器里
Log(1&lt;&lt;7) 与 Warning(1&lt;&lt;8) 两位是关的、Error(1&lt;&lt;9) 是开的，所以「只有错误读得到」。

**正确做法**：在 Console 窗口右上角把三个等级按钮都点亮即可。脚本里确认用
`UnityEditor.LogEntries.GetCountsByType(ref err, ref warn, ref log)`——**它不受过滤器影响**，
所以「零错误」这个结论可以靠它，而不必依赖 `read_console` 能不能读出来。

### 15.8 退出 Play 模式时报「资源服务销毁时还有 N 个实例没归还」（波 3 踩到）

**现象**：明明每个面板都是 `IUIService` 开的、也没人手动 Destroy，退出播放模式却总看到
`AddressablesAssetService` 报还有实例/句柄没归还。

**根因**：VContainer 按**注册顺序**释放 `IDisposable`。`IAssetService` 注册在前（启动顺序要求），
`UIService` / `AudioService` 注册在后，于是资源服务先销毁、清空登记，等 UI 与音频再去 `ReleaseInstance`
就已经晚了。这是顺序问题，不是真泄漏——但它会让那条本来用来抓真泄漏的 Warn 变成天天响的噪音。

**正确做法**：持有资源的服务在 `InitializeAsync` 里订阅 `Application.quitting`，在回调里把实例和句柄
先还回去（`UIService.ReleaseAllViews` / `AudioService.ReleaseAllHandles`），`Dispose` 时退订并再调一次（幂等）。
新写「持有 Addressables 句柄的框架服务」时照这个做。

### 15.9 切状态时 `EnterAsync` 抛异常，`Current` 还停在旧状态（波 3 审查后定的语义）

**现象**：`GoToAsync<SomeState>()` 抛异常回来，接着看 `IGameFlow.Current`，它还是切换**之前**那个状态；
再切一次到别的状态时，旧状态的 `ExitAsync` 又被调了一遍。

**根因**：`GameFlow` 现在只在 `EnterAsync` **成功之后**才把 `Current` 指向新状态（之前是 Enter 前就赋值，
Enter 炸了 `Current` 会指着一个从没进去过的状态，下一次切换去 Exit 它，Exit 里那些「Enter 时申请的东西」全是空的）。
代价是失败后会处在「前一状态已 `Exit`、目标状态未 `Enter`」的空档，而 `Current` 仍指向前一状态——
它的语义是「**最近一个成功进入的状态**」，不是「场上活着的状态」。

**正确做法**：

- **状态的 `ExitAsync` 要写成幂等的**：可能被连着调用两次（失败那次 + 下一次切换那次）。摘监听、还句柄这类操作
  本来就该能重复执行；`Exit` 里不要做「计数减一」这种只能跑一次的事。
- 切换失败不用自己收拾状态机：异常会原样回传给 `GoToAsync` 的调用方，队列照常处理后面的请求，
  恢复手段就是再 `GoToAsync` 到一个能进得去的状态。
- 失败那次**不发** `GameStateChangedEvent`，所以订阅者看到的事件序列里永远只有成功的切换。

### 15.10 玩法状态注册在子作用域里，`GoToAsync` 解析失败（波 4 定的做法）

**现象**：按「模块用自己的 `LifetimeScope` 子作用域注册」的直觉写法，把玩法状态注册在玩法场景里的
`<模块>LifetimeScope` 上，然后 `flow.GoToAsync<你的状态>()`，运行时抛 VContainer 的解析失败，
说找不到那个类型的注册。

**根因**：两条叠一起。一是 `GameFlow` 持有的是**根作用域**的 `IObjectResolver`
（构造注入进来的那个），子作用域的注册对父作用域**不可见**——VContainer 的可见性是单向的，
子能看父，父看不到子。二是时序：玩家还在标题界面时，玩法场景根本没加载，那个子作用域连对象都还不存在。

**正确做法**：玩法模块经 `Game.Core.Boot.GameplayInstaller` 把状态、规则类、入口点注册进**根作用域**——
继承它，把组件挂到 `Boot.unity` 的 `GameBootstrap` 物体上，写法见第 7.8 节与 `Runtime/Sample/SampleInstaller.cs`。
`Game.Core` 因此仍然不认识任何玩法类型（asmdef 依赖方向不变），玩法自己把自己接上来。
子作用域不是不能用，但只适合「随场景生灭、且只在场景内部被解析」的东西。

### 15.11 用 `float` 算钱，界面上的价格和策划口算对不上（波 4 踩到）

**现象**：`50 × (1 - 0.15f)` 期望是 42.5、四舍五入 43，实际得到 42。断言写 43 的测试红了，
但公式怎么看都没错。

**根因**：`0.15f` 存不下 0.15，它的真值是 `0.150000005960464…`。按 `double` 算出来是 42.4999997，
四舍五入自然是 42。这类误差在单笔交易上只差 1，但一旦参与累加、比较「够不够买」，就会变成
「明明够却提示钱不够」这种查不出来的 bug。

**正确做法**：**钱一律用 `decimal` 算中间值**，最后一步再转回 `int`：

```csharp
decimal discounted = price * (1m - (decimal)discount);
int rounded = (int)decimal.Round(discounted, 0, MidpointRounding.AwayFromZero);
```

`(decimal)0.15f` 按 7 位有效数字取整，拿到的就是 `0.15`，结果 42.5，逢半进位得 43。
取整用 `MidpointRounding.AwayFromZero`（逢半进位）而不是默认的银行家舍入——玩家看到的价格要和口算一致。
`SampleRulesTests` 里有一条测试专门钉着这个，改回 `double` 它就会红。
