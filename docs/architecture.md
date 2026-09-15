# 21Days 框架架构与设计决策

> 本文是框架层的**设计定稿**：为什么这样分层、选了哪些公开方案、各服务的契约长什么样。
> 面向开发者的操作手册见 [`developer-guide.md`](developer-guide.md)；本文只记录决定与理由，不写操作步骤。
> 定稿日期 2026-09-15。改动本文中的任何契约，先在 PR 里说明理由。

## 1. 目标与约束

- Unity 2022.3.62f2 LTS，2D URP，纯 C#，多人协作，Windows 与 Android 两个包体共用内容。
- 玩法未定，框架层先行：框架**不感知任何玩法**，玩法模块只通过框架的公开接口接入。
- 开发管线一律用公开方案（Unity 官方包、活跃开源项目），自写只做胶水层。
- 参考过一个成熟商业客户端的分层，借鉴的是手法（见第 6 节），不复制其代码与目录。

## 2. 选型

| 能力 | 方案 | 版本 | 安装 | 取舍理由 |
| --- | --- | --- | --- | --- |
| 异步 | UniTask | 2.5.11 | UPM git | 全框架统一 async/await，不用协程；零分配 |
| 服务注册与生命周期 | VContainer | 1.19.0 | UPM git | 服务集中注册、构造注入、`ITickable` 自动驱动；避免单例加手写调用清单 |
| 事件 | MessagePipe | 1.8.2 | UPM git | 类型化发布订阅，与 VContainer 集成，订阅句柄可随作用域释放 |
| 资源 | Addressables | 1.22.x | Unity Registry | 官方；当前无热更需求。需要热更时再评估 YooAsset |
| 配置表 | Luban | 5.x | 工具 + luban_unity | Excel 单一数据源，生成 C# 强类型类与二进制数据；工具需 .NET SDK |
| 输入 | Input System | 1.x | Unity Registry | Action Map 抹平键鼠、手柄、触控差异 |
| 缓动 | LitMotion | 2.0.2 | UPM git | MIT，零分配，与 UniTask 配合；DOTween 免费版许可证非开源 |
| 存档序列化 | Newtonsoft JSON | 3.2.x | Unity Registry | 官方封装，存档可读可 diff；性能瓶颈时再上 MemoryPack |
| 对象池 | UnityEngine.Pool | 内置 | 无 | 2021 起自带，够用 |
| 真机调试台 | IngameDebugConsole | 最新 | UPM git | 真机看日志与执行命令 |
| 一体化框架 | 不引入 | | | TEngine / GameFramework / QFramework 自带整套分层与约定，与本工程 asmdef 分层和 harness 规则冲突 |

不做的事（本期）：热更（HybridCLR）、网络、FMOD。玩法定了再议。

## 3. 分层与依赖方向

```
Game.Tests.EditMode / PlayMode ──┐
Game.Editor ─────────────────────┼──► Game.Runtime ──► Game.Core ──► 第三方包
                                 └────────────────────►
```

| asmdef | 目录 | 职责 | 允许引用 |
| --- | --- | --- | --- |
| `Game.Core` | `Assets/_Project/Scripts/Core/` | 框架层。启动、服务、事件、资源、配置、状态流、UI、音频、存档、输入、池、定时器、日志、平台 | 第三方包；**不引用 Runtime / Editor / Tests** |
| `Game.Runtime` | `Assets/_Project/Scripts/Runtime/<Module>/` | 玩法模块，一个模块一个目录，命名空间 `Game.<Module>` | `Game.Core` 与第三方包 |
| `Game.Editor` | `Assets/_Project/Scripts/Editor/` | 编辑器工具、打包、导入规则、生成菜单 | `Game.Core`、`Game.Runtime` |
| `Game.Tests.*` | `Assets/_Project/Scripts/Tests/{EditMode,PlayMode}/` | 测试 | `Game.Core`、`Game.Runtime` |

硬约束：

- `Game.Core` 里不出现任何玩法名词；玩法模块之间不互相引用私有实现，只走对方公开接口、事件或 ScriptableObject。
- 平台条件编译与平台专属 API **只在** `Core/Platform/`，对外暴露 `IPlatformService`。
- 生成物不手改：Luban 生成的代码与数据、`.meta`、`Library/` 等。钩子会拦。

## 4. 目录

```
Assets/_Project/
  Scripts/
    Core/                 Game.Core
      Boot/               GameBootstrap、GameLifetimeScope、IGameService
      Events/             事件类型约定、订阅辅助
      Assets/             IAssetService、AssetHandle
      Config/             IConfigService；Generated/ 为 Luban 生成物
      Flow/               IGameFlow、GameState、内置 BootState / TitleState
      UI/                 IUIService、UIView、UILayer
      Audio/              IAudioService
      Save/               ISaveService、ISaveData
      Input/              IInputService、生成的 GameInput 包装类
      Pooling/            GameObjectPool、IPoolable
      Timing/             ITimerService、TimerHandle
      Logging/            Log 静态门面
      Platform/           IPlatformService 与各平台实现（唯一允许 #if 平台宏的地方）
    Runtime/<Module>/     Game.Runtime
    Editor/               Game.Editor
    Tests/EditMode/  Tests/PlayMode/
  Data/
    Config/               Luban 生成的 .bytes 数据（生成物）
    Input/                GameInput.inputactions
    Audio/                AudioMixer
    <Module>/             各模块 ScriptableObject
  Scenes/
    Boot.unity            唯一常驻场景，挂 GameBootstrap；玩法场景 Additive 加载
  Prefabs/UI/             UIView 预制体，Addressables key 等于类名
Tables/                   Excel 源表与 Luban 配置（不是 Unity 资产，放仓库根）
scripts/gen-tables.ps1    生成配置表
```

## 5. 启动流程与核心契约

契约是各波实现的共同依据。实现时命名可微调，**形状与规则不变**。

### 5.1 启动

```
Boot 场景加载
 → GameBootstrap.Awake：DontDestroyOnLoad，构建 GameLifetimeScope（根作用域）
 → IGameFlow.GoToAsync<BootState>()（启动期 Current 不为空）
 → 按注册顺序串行调用每个 IGameService.InitializeAsync（Platform → Log → Assets → Config → Save → Input → Audio → UI）
 → 发布 BootCompletedEvent → IGameFlow.GoToAsync<TitleState>()
```

平台实现的选择在 `Core/Platform/PlatformServiceFactory` 里做，根作用域只写一行普通注册。

```csharp
namespace Game.Core.Boot
public interface IGameService { UniTask InitializeAsync(CancellationToken ct); }
public sealed class GameBootstrap : MonoBehaviour   // 唯一 MonoBehaviour 入口
public sealed class GameLifetimeScope : LifetimeScope   // 注册全部 Core 服务；玩法模块用子作用域
```

每帧逻辑用 VContainer 的 `ITickable` / `IFixedTickable` / `ILateTickable`，不自定义 Update 分发。

### 5.2 事件

- 用 MessagePipe 的 `IPublisher<T>` / `ISubscriber<T>`；事件类型是 `readonly struct`，命名 `XxxEvent`。
- 订阅返回的 `IDisposable` 必须挂到作用域或 `DisposableBag`，禁止裸订阅。
- 全局事件在根作用域注册；模块内部事件在模块子作用域注册。

### 5.3 资源

```csharp
namespace Game.Core.Assets
public interface IAssetService
{
    UniTask<AssetHandle<T>> LoadAsync<T>(string key, CancellationToken ct = default) where T : UnityEngine.Object;
    UniTask<IReadOnlyList<AssetHandle<T>>> LoadAllAsync<T>(string label, CancellationToken ct = default) where T : UnityEngine.Object;   // 按标签批量加载，配置表用
    UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken ct = default);
    void ReleaseInstance(GameObject instance);
    UniTask<SceneHandle> LoadSceneAsync(string key, LoadSceneMode mode, CancellationToken ct = default);
}
public sealed class AssetHandle<T> : IDisposable { public T Asset { get; } }   // Dispose 即释放
```

只有 Load 与 Release 两个动词。不提供 Exists / Check / Download 之类接口：参考工程的文档里这类接口是长期踩坑点。

### 5.4 配置表

```csharp
namespace Game.Core.Config
public interface IConfigService { Tables Tables { get; } }   // Luban 生成的 Tables 根，只读
```

Excel 是唯一数据源，`Tables/` 改完跑 `scripts/gen-tables.ps1`，生成代码进 `Core/Config/Generated/`，数据进 `Data/Config/`。两处都是生成物。

### 5.5 状态流

```csharp
namespace Game.Core.Flow
public abstract class GameState
{
    public virtual UniTask EnterAsync(CancellationToken ct) => UniTask.CompletedTask;
    public virtual UniTask ExitAsync(CancellationToken ct) => UniTask.CompletedTask;
}
public interface IGameFlow
{
    GameState Current { get; }
    UniTask GoToAsync<TState>(CancellationToken ct = default) where TState : GameState;
}
```

切换串行执行：先 Exit 当前再 Enter 目标；切换中再次请求切换则排队。切换完成后发布一次 `GameStateChangedEvent(from, to)`，切换前不发。内置 `BootState`、`TitleState`；玩法状态由 `Game.Runtime` 注册。

### 5.6 UI

```csharp
namespace Game.Core.UI
public enum UILayer { Hud, Panel, Popup, Top }
public abstract class UIView : MonoBehaviour
{
    public abstract UILayer Layer { get; }
    public virtual UniTask OnOpenAsync(object arg, CancellationToken ct) => UniTask.CompletedTask;
    public virtual void OnRefresh() { }
    public virtual UniTask OnCloseAsync(CancellationToken ct) => UniTask.CompletedTask;
}
public interface IUIService
{
    UniTask<T> OpenAsync<T>(object arg = null, CancellationToken ct = default) where T : UIView;
    UniTask CloseAsync(UIView view, CancellationToken ct = default);
    UniTask CloseTopAsync(CancellationToken ct = default);
    T Get<T>() where T : UIView;
}
```

四层 Canvas 各一个根节点，`Canvas Scaler` 按屏幕尺寸缩放并适配安全区。Panel 层单栈：打开全屏 Panel 时隐藏其下的 Panel；Popup 层可叠加。预制体 Addressables key 等于类名。

### 5.7 存档

```csharp
namespace Game.Core.Save
public interface ISaveData { int Version { get; } void Migrate(int fromVersion); }
public interface ISaveService
{
    T Get<T>() where T : class, ISaveData, new();   // 按类型取分区，首次访问创建
    UniTask<bool> SaveAsync(int slot, CancellationToken ct = default);
    UniTask<bool> LoadAsync(int slot, CancellationToken ct = default);
    bool Exists(int slot);
    void Delete(int slot);
}
```

JSON 文件，路径由 `IPlatformService.SaveRoot` 给出；先写临时文件再原子替换；每个分区带版本号，加载时逐版本迁移。

### 5.8 输入、定时器、池、日志、音频、平台

```csharp
public interface IInputService { GameInput Actions { get; } void EnableMap(string map); void DisableMap(string map); }
public interface ITimerService
{
    TimerHandle Delay(float seconds, Action callback, bool unscaled = false);
    TimerHandle Interval(float seconds, Action callback, bool unscaled = false);
}
public readonly struct TimerHandle : IDisposable { bool IsActive { get; } }   // Dispose 即取消
public interface IClock   // 唯一时间来源；Unscaled 两项供 unscaled 定时器用
{
    DateTime UtcNow { get; }
    float GameTime { get; } float DeltaTime { get; }
    float UnscaledTime { get; } float UnscaledDeltaTime { get; }
}
public interface IPoolable { void OnGet(); void OnRelease(); }
public sealed class GameObjectPool { GameObject Get(); void Release(GameObject go); }   // 封装 UnityEngine.Pool
public static class Log { Debug / Info / Warn / Error(string message, UnityEngine.Object context = null); }   // Debug 级别编译期剔除
public interface IAudioService
{
    void PlaySfx(AudioClip clip, float volume = 1f);
    UniTask PlayBgmAsync(string key, float fadeSeconds = 0.5f, CancellationToken ct = default);
    void StopBgm(float fadeSeconds = 0.5f);
    float MasterVolume { get; set; } float BgmVolume { get; set; } float SfxVolume { get; set; }
}
public interface IPlatformService { PlatformKind Kind { get; } string SaveRoot { get; } bool IsTouchPrimary { get; } void Vibrate(VibrationKind kind); }
```

- 玩法只读 `Actions.Gameplay.Move` 这类动作，不读具体按键、不读 `Input.touches`。
- 延时用 `ITimerService`，每帧用 `ITickable`，两者随作用域销毁自动取消；不用 `Interval(0)` 冒充每帧。
- 需要时间的地方一律注入 `IClock`，不直接读 `Time.time` 与 `DateTime.UtcNow`；本地实现直接包装二者。
- 音频三路音量 Master / Bgm / Sfx；SFX 的 AudioSource 池化。AudioMixer **可选**：Unity 没有公开 API 创建 Mixer 资产，`AudioConfig.Mixer` 为空时用音量相乘实现，手工建了 Mixer 后切换到暴露参数。另有 `PlaySfxAsync(key)` 按资源 key 播放。BGM 换曲是「旧曲淡出、新曲淡入」的顺序淡化，`fadeSeconds` 传负数表示用配置默认值。
- 状态流附带 `SceneGameState` 基类：Enter 时 Additive 加载 `SceneKey`，Exit 时卸载，子类只写 `OnSceneReadyAsync`。
- `UIView` 的开关过渡是 `PlayOpenTransitionAsync / PlayCloseTransitionAsync(seconds)`，默认 LitMotion 淡入淡出，时长来自 `UIConfig`。

### 5.9 埋点

```csharp
public interface ITelemetryScope   // 模块名已绑好；玩法层注入的是它，不是 ITelemetryService
{
    void Track(string evt, (string Key, PropValue Value) p0 /* 最多四个属性 */);
    void TrackWarn(string evt, in TelemetryProps props = default);
    void TrackError(string evt, Exception error, in TelemetryProps props = default);
    TelemetrySpan BeginSpan(string name);            // using 包住一段，Dispose 时自动埋 ms
}
public interface ITelemetryService { /* … */ ITelemetryScope Scope(string module); }
```

契约要点（完整规范见 [`telemetry.md`](telemetry.md)，这里不重复）：

- **不另建文件通道**：埋点就是一条格式固定的 Unity 日志（`[Game][T] <级别> <模块>/<事件> | <JSON>`），由 Unity 自己落盘。写入点只有一个，崩溃时最后几条不丢，真机路径不用自己管。
- **日志在哪靠指针文件** `Logs/telemetry-source.txt`：`Editor.log` 的路径是本机全局的，猜默认路径会读到另一个 Unity 工程的日志。
- **框架层零侵入自埋**：`core.boot` / `core.flow` / `core.asset` / `core.ui` / `core.save` / `core.audio` / `core.perf` / `core.log`，玩法接进框架就自动有。`core.log/unity_error` 把**任何** `Debug.LogError` 与未捕获异常转成带序号的埋点，是根因分析的主线索。
- **玩法层只埋四类**：意图入口 / 状态迁移 / 失败分支 / 长耗时；**每帧触发的一律不埋**，要每帧数据用 `core.perf` 采样。
- **零分配**：属性走定长四槽的 `TelemetryProps` + 不装箱的 `PropValue`，不用 `params` / `Dictionary`。属性超过四个说明这条事件混了两件事，拆成两条。
- **规则类照埋不误**：`ITelemetryScope` 及其值类型全是纯 C#（只 `using System`），Unity 依赖只在 sink 与 clock 的实现里。规则类构造注入这个接口，仍然可 EditMode 测试、仍然能搬服务端（第 7 节）。
- 开关在 `Data/Telemetry/TelemetryConfig.asset`（总开关 / 最低级别 / 模块过滤 / 采样间隔 / 限流）；`D` 级在正式包里整句剔除。
- 工具：`/analyze-telemetry` 查日志出诊断报告，`/instrument-module <模块>` 按四类尺子补埋点。

## 6. 从参考工程借鉴的手法与规避的坑

借鉴：

1. 单一 MonoBehaviour 入口驱动，框架 Tick 与 Unity 生命周期分离。
2. 启动顺序显式串行，资源与配置预载完成后才进入第一个可交互状态。
3. 资源句柄 `IDisposable`，生命周期跟着持有者走。
4. 延时定时器与每帧调度是两个东西，接口分开。
5. UI 面板三段生命周期加分层单栈，全屏面板剔除下层。
6. 配置表 Excel 单一数据源，生成物禁止手改并由钩子拦截。
7. 消息队列每帧掏空再处理、迭代中延迟增删注册表，用于事件与 Tick 派发。

规避：

1. 单例加多处手写调用清单，新增系统要改三个方法。改用 VContainer 集中注册。
2. 资源接口暴露 Check / Download 语义，业务拿它当加载判定导致真机静默失败。
3. 入口脚本堆满平台分支与第三方 SDK 初始化。平台差异只进 `Core/Platform/`。
4. `UnityEngine.Object` 用 `?.` / `??` / `is null` 判空，绕过 Unity 的伪空重载。规则已补进 `csharp-code.md`。
5. 数据类后缀混用。`Config` 是 ScriptableObject 配置，`Settings` 是嵌套可序列化块，`Data` 是纯 DTO，`Info` 是运行时临时对象，不叠加。

## 7. 单机优先，联网只留缝

本项目是**单机游戏**，框架不含网络层、不定义任何服务器接口。但以下几条约束让这套框架将来能复用到联网项目，成本很低，从第一个玩法模块起就遵守：

| 缝 | 现在怎么做 | 将来联网时怎么换 |
| --- | --- | --- |
| 服务全部是接口且经 VContainer 注入 | `ISaveService`、`IConfigService`、`IClock` 等都只暴露接口 | 换成服务器实现重新注册即可，玩法代码不动 |
| 时间只来自 `IClock` | 本地包装 `Time` 与 `DateTime.UtcNow` | 换成服务器校时实现，防改本机时间 |
| 玩法规则写成纯 C# 类 | `Runtime/<Module>/` 里规则类不继承 MonoBehaviour，可 EditMode 测试；MonoBehaviour 只做表现 | 规则类可搬到服务端或做本地预测 |
| 状态改变走命令 | 输入与 UI 产生「意图」对象，由规则类应用到状态，不直接改字段 | 意图对象即网络消息，序列化后发送 |
| 存档是带版本的 DTO | `ISaveData` 分区各自版本化，JSON 序列化 | 同一份 DTO 上传下载或做云存档 |
| 状态流是异步的 | `GameState.EnterAsync` 可等待 | 加一个连接中状态，不改状态机 |

不做的事：不预建 `Core/Network/`，不定义 `INetworkService`，不给玩法留「联机模式」分支。真要做时再按上表换实现。

## 8. 实施波次与完成标准

| 波 | 内容 | 完成标准 |
| --- | --- | --- |
| 0 | 装包、asmdef、目录、规则与文档更新 | 编辑器零编译错误，`developer-guide.md` 骨架就位 |
| 1 | Boot、Log、Events、Timing、Pooling、Input、Platform | Boot 场景能跑到 TitleState 占位；纯逻辑部分有 EditMode 测试 |
| 2 | Assets、Config（Luban 接入并跑通一张示例表）、Save | 示例表能读；存档能存能读能迁移 |
| 3 | Flow、UI、Audio | 一个占位 Title 面板能开能关；BGM 能淡入淡出 |
| 4 | Editor 工具、测试补齐、模块三件套、开发者手册定稿 | 手册按第 4 节目录逐项可操作 |

每波结束：lint 零违规、code-reviewer 审查通过、EditMode 测试通过、改动清单经授权后提交。
