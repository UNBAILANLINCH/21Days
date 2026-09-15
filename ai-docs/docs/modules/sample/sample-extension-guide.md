---
type: extension-guide
module: Sample
layer: runtime
maturity: stable
---

# Sample 扩展指南

> 要给 Sample 加东西，或者**照着它建一个新模块**时看这份。
> 这也是 extension-guide 的写法样例：只写扩展点和典型步骤，不复述代码。

## 现成的扩展点

| 扩展点 | 类型 | 怎么用 |
| --- | --- | --- |
| `GameplayInstaller` | Core 的抽象 MonoBehaviour | 玩法往**根作用域**注册类型的唯一缝。一个模块一个子类，挂到 Boot 场景的 `GameBootstrap` 物体上 |
| `TitleStartClickedEvent` | Core 的事件 | 想在标题的「开始」之后做别的事，再写一个入口点订阅它即可，不必动 `SampleTitleRouter` |
| `SampleConfig` | ScriptableObject | 加数值字段（`[SerializeField] private` + 只读属性），不加代码常量 |
| `SceneGameState` | Core 的状态基类 | 新玩法状态继承它，给出 `SceneKey`，写 `OnSceneReadyAsync` / `OnSceneUnloadingAsync` |
| `UIView` | Core 的面板基类 | 新面板继承它，实现 `Layer`，事件往外抛、由状态接住 |
| `ITelemetryService.Scope(模块名)` | Core 的服务方法 | 给埋点。Unity 侧的类（状态、MonoBehaviour）构造函数收 `ITelemetryService`，当场转成 `ITelemetryScope` 存起来——`SampleState` 就是这么做的 |

## 典型扩展：照 Sample 建一个新模块

1. **建骨架**：菜单 `21Days/工程/创建模块骨架…` 输入模块名（PascalCase），
   它会建 `Scripts/Runtime/<模块>/`、`Scripts/Tests/EditMode/<模块>/`、`Data/<模块>/` 与一个占位规则类。
2. **规则类**：把占位类写成真的——纯 C#，依赖构造注入，不碰 `UnityEngine.Time`、不读单例。
   照 `SampleRules` 的形状：公开方法是纯函数，非法输入抛带值的异常。
3. **意图对象**（要改状态才需要）：`readonly struct`，一个文件一个类型，合法性由消费方判。
4. **配置**：`<模块>Config : ScriptableObject` + `[CreateAssetMenu(menuName = "21Days/<模块>/…")]`，
   资产放 `Assets/_Project/Data/<模块>/`。
5. **状态**：继承 `SceneGameState`（要场景）或 `GameState`（纯 UI）。
   场景的 Addressables 地址**别和类名 / 预制体地址撞**（Sample 用 `SampleScene_Game` 就是为了避开）。
   照 `SampleState` 的构造函数抄，最后一个参数收 `ITelemetryService`，当场 `.Scope("<模块名>")`。
6. **面板**：继承 `UIView`，预制体放 `Assets/_Project/Prefabs/UI/`，**地址等于类名**，加进 Addressables 的 `UI` 组。
7. **注册**：写 `<模块>Installer : GameplayInstaller`，照 `SampleInstaller` 注册配置、规则类、状态、入口点；
   把组件挂到 Boot 场景的 `GameBootstrap` 上，配置资产拖到字段里。状态收 `ITelemetryService` 这种情况
   `builder.Register<T>(Lifetime.Singleton)` 照常写、不用工厂式——`ITelemetryService` 已由
   `GameLifetimeScope` 注册进根作用域，会被自动解析；只有规则类要收窄到 `ITelemetryScope` 时才需要工厂式
   （见下面「加一条埋点」）。
8. **测试**：`Tests/EditMode/<模块>/<规则类>Tests.cs`，至少一条覆盖核心规则；
   框架服务写个几行的假实现（`SampleRulesTests.FakeConfigService` 就是范例），别拉起真服务。
9. **文档**：`/generate-doc <模块>` 生成三件套，在 [`catalog.md`](../../catalog.md) 补一行，
   在 `.claude/skills/generate-doc/modules.json` 登记一条。

## 典型扩展：给 Sample 自己加东西

- **加一条定价规则**：加到 `SampleRules`（它的职责就是定价），同时在 `SampleRulesTests` 补测试。
  新规则要读新的表就往 `Tables/Data/` 加表、跑 `scripts/gen-tables.ps1`，不要在代码里写死数值。
- **加一个面板**：新写一个 `UIView` 子类 + 预制体 + Addressables 条目，由 `SampleState` 开关。
  弹窗类的把 `Layer` 写成 `UILayer.Popup`（可叠加，不影响 Panel 层）。
- **加每帧逻辑**：实现 VContainer 的 `ITickable`，在 Installer 里
  `builder.RegisterEntryPoint<T>(Lifetime.Singleton)`。**不要**自己写 `Update`，也不要用 `Interval(0)` 冒充每帧。
- **加模块内部事件**：事件类型放本模块目录，broker 注册在**本模块的 Installer** 里
  （全局事件才注册在 `GameLifetimeScope`）。命名描述已发生的事实，不用祈使句。
- **加一条埋点**：先按 [`docs/telemetry.md`](../../../../docs/telemetry.md) 2.2 的四类尺子判断该不该埋
  （批量扫候选点用 `/instrument-module <模块>`，契约不在这里复述）。两种取法看类型在哪一侧：
  - Unity 侧的类（状态、MonoBehaviour）：构造函数直接收 `ITelemetryService`，当场 `.Scope("sample")`
    转成门面存起来（`SampleState.cs:56` 就是这样，`SampleInstaller` 不用改成工厂式，
    `builder.Register<SampleState>(Lifetime.Singleton)` 照常写，`ITelemetryService` 会被自动解析）。
  - 纯 C# 规则类（比如 `SampleRules`，目前还没有埋点）：构造函数只收 `ITelemetryScope`（小接口，
    测试替身好写），由 Installer 工厂式喂：
    `builder.Register(c => new SampleRules(c.Resolve<IConfigService>(), c.Resolve<ITelemetryService>().Scope("sample")))`。
    **禁止** `builder.Register<ITelemetryScope>(...)` 把它注册成一个独立类型——所有 `GameplayInstaller`
    共用同一个根作用域，两个模块各注册一个 `ITelemetryScope` 会互相覆盖，谁拿到谁的全看注册顺序。

## 依赖方向约束

```
Game.Runtime（本模块） ──► Game.Core ──► 第三方包
```

- 本模块**不许**被 `Game.Core` 引用，也不许引用 `Game.Editor` / `Game.Tests.*`。
- 跨玩法模块只走对方的公开接口 / 事件 / ScriptableObject，不 `GetComponent` 到别人的私有实现
  （要调别的模块先读它的 `-external-api.md`）。
- 平台条件编译（`#if UNITY_ANDROID` 之类）与平台专属 API **只允许**出现在 `Core/Platform/`，
  玩法模块里出现即违规；要平台能力就注入 `IPlatformService`。
