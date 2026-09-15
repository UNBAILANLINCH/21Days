---
type: module-guide
module: Sample
layer: runtime
maturity: stable
---

# Sample 模块指南

> **本模块也是模块文档的样例**：三份文档（guide / external-api / extension-guide）照着这份写；
> 代码本身也是玩法模块的样板，新模块从 `Assets/_Project/Scripts/Runtime/Sample/` 照抄形状。

## 职责边界

**做**：用最少的代码把框架的每一层串起来跑通一遍——读配置表 → 纯 C# 规则算价 → 意图对象 →
状态加载场景 → 面板显示 → 返回标题。给后来的人一个「玩法模块长什么样」的实物参照。

**不做**：任何真实玩法。没有战斗、没有存档写入、没有输入绑定（这些接进来的写法见
[`developer-guide.md`](../../../../docs/developer-guide.md) 第 6 章的服务速查）。
真玩法上线后这个模块可以整个删掉，删了框架照样跑（只有 Boot 场景上那个 `SampleInstaller` 组件要一起摘）。

## 内部结构

| 类 | 是什么 | 谁持有它 |
| --- | --- | --- |
| `SampleRules` | **纯 C# 规则类**，按 `TbItem` 算折后单价与订单总价 | 根作用域单例，`SampleState` 注入 |
| `BuyItemIntent` | `readonly struct` 意图对象（买几个几号道具） | 谁要算价谁现场 new，不存 |
| `SampleConfig` | ScriptableObject，展示用的 itemId / count / discount | `SampleInstaller` 在 Inspector 上拖赋并注册进容器 |
| `SampleState` | `SceneGameState` 子类，加载 Sample 场景、开面板、接返回、埋点 | 根作用域单例，`IGameFlow` 解析 |
| `SampleView` | `UIView` 子类，显示一行字 + 返回按钮 | `IUIService` 实例化并持有 |
| `SampleTitleRouter` | 入口点，订阅 `TitleStartClickedEvent` → `GoToAsync<SampleState>()` | 根作用域入口点 |
| `SampleInstaller` | `GameplayInstaller` 子类，把上面这些注册进**根作用域** | Boot 场景的 `GameBootstrap` 物体 |

`SampleState` 的构造函数（`SampleState.cs:56`）是
`SampleState(IAssetService, IUIService, IGameFlow, SampleRules, SampleConfig, ITelemetryService)`——
比早期样板多了末位的 `ITelemetryService` 参数。构造函数里当场换成绑好模块名 `"sample"` 的
`ITelemetryScope` 门面存起来，`telemetry` 传 `null`（比如测试里没接埋点服务）时退化成
`NullTelemetryScope.Instance`，调用方不用自己判空。**照抄这个构造函数的新模块要把这个参数一起抄**，
埋点契约见下面「埋点」一节。

依赖方向：`SampleState` → `SampleRules` → `IConfigService`；`SampleState` 另外依赖 `ITelemetryService`——
这是框架服务，`GameLifetimeScope.RegisterTelemetry` 已经把它注册进根作用域，`SampleInstaller` 不用
额外注册就能被 VContainer 自动解析（`SampleRules` 目前没有拿到任何埋点依赖，构造函数没变）。
面板不注入任何服务（它由 Addressables 实例化，不经容器），只往外抛 `event`，由状态接住。

## 核心数据

- 配置资产：`Assets/_Project/Data/Sample/SampleConfig.asset`（`itemId` 1002、`count` 3、`discount` 0.2）。
- 配置表：`TbItem`，源表 `Tables/Data/item.xlsx`，改完跑 `scripts/gen-tables.ps1`。
- 运行时状态：**没有**。规则类是无状态的，面板的那行文字由状态每次进入时现算现传。

## 生命周期

```
GameLifetimeScope.Configure
  └─ SampleInstaller.Install：注册 SampleConfig / SampleRules / SampleState / SampleTitleRouter
容器建好
  └─ SampleTitleRouter.Start：订阅 TitleStartClickedEvent（句柄进 DisposableBag）
玩家点「开始」
  └─ TitleView.OnStartClicked → TitleState 发 TitleStartClickedEvent → Router → GoToAsync<SampleState>
SampleState.EnterAsync（基类 sealed）
  ├─ Additive 加载 Addressables 地址 SampleScene_Game
  └─ OnSceneReadyAsync：SampleRules 算价
       ├─ 算成功 → 埋 sample/buy_item
       ├─ 算失败（ArgumentOutOfRangeException）→ 埋 sample/buy_item_failed（TrackError），文案降级显示
       └─ BeginSpan("view_ready") 包住 ui.OpenAsync<SampleView>(那行字)，Dispose 时自动埋
          sample/view_ready(ms) → 订阅 OnBackClicked
点「返回标题」
  └─ SampleState.HandleBackClicked → GoToAsync<TitleState>()
SampleState.ExitAsync（基类 sealed）
  ├─ OnSceneUnloadingAsync：退订 → ui.CloseAsync(view) → view = null（幂等）
  └─ 卸载场景
```

`Start` / `Awake` / `OnEnable` 一个都没用到：这个模块里唯一的 MonoBehaviour 是 `SampleInstaller`，
它只实现 `Install`。**订阅与退订严格成对**，位置见上面的流程图。

## 埋点

`SampleState.OnSceneReadyAsync`（`SampleState.cs:83`）里三处，模块名 `sample`（`SampleState.cs:34`
的 `TelemetryModule` 常量）。分类按 [`docs/telemetry.md`](../../../../docs/telemetry.md) 第 2.2 节的
四类尺子：

| 事件 | 尺子类别 | 触发点 | 属性 |
| --- | --- | --- | --- |
| `sample/buy_item` | 1 意图入口 | `BuyItemIntent` 算完价的成功路径（`SampleState.cs:106`） | `id` `n` `total` |
| `sample/buy_item_failed` | 3 失败分支 | 算价抛 `ArgumentOutOfRangeException`，`catch` 里 `TrackError`（`SampleState.cs:120`） | `id` `n` `discount` |
| `sample/view_ready` | 4 长耗时操作 | `BeginSpan("view_ready")` 包住 `ui.OpenAsync<SampleView>`，`Dispose` 自动带 `ms`（`SampleState.cs:132`） | `ms`（自动） |

三处目前只在 `SampleState` 里；`SampleRules` 是纯 C# 规则类，还没有拿到 `ITelemetryScope`，
将来要给它加埋点得走「工厂式注入 `ITelemetryScope`」那条路，见
[`sample-extension-guide.md`](sample-extension-guide.md) 的「加一条埋点」。

详细契约（日志行格式、四类尺子的判定标准、`ITelemetryScope` 的标准取法、`/analyze-telemetry` 怎么读）
不在这里复述，看 [`docs/telemetry.md`](../../../../docs/telemetry.md) 与
[`.claude/skills/instrument-module/SKILL.md`](../../../../.claude/skills/instrument-module/SKILL.md)。

## 接线要求

编辑器里必须做的两件事（缺一样都不会编译报错，只会运行时不动或报「解析失败」）：

1. `Assets/_Project/Scenes/Boot.unity` 的 `GameBootstrap` 物体上挂 `SampleInstaller` 组件。
2. 把 `Assets/_Project/Data/Sample/SampleConfig.asset` 拖到该组件的 **Config** 字段
   （忘了拖不会崩：会记一条 Error 并用代码建的默认值顶上）。

Addressables 里两条地址必须在（Window → Asset Management → Addressables → Groups）：

| 地址 | 组 | 资产 |
| --- | --- | --- |
| `SampleView` | UI | `Assets/_Project/Prefabs/UI/SampleView.prefab` |
| `SampleScene_Game` | Scenes | `Assets/_Project/Scenes/Sample.unity` |

Sample 场景**不进 Build Settings**——Addressables 加载的场景不需要，加进去反而会被打两份。

## 验证入口

- EditMode 测试：`Assets/_Project/Scripts/Tests/EditMode/Sample/SampleRulesTests.cs`（7 条，跑 `/unity-test EditMode`）。
  只覆盖 `SampleRules`——它的构造函数没变；`SampleState` 走 Play 模式端到端验证，没有独立的 EditMode 测试。
- 端到端：进 Play 模式，Boot → Title → 点「开始」→ 看到折后价 → 点「返回标题」回到 Title。
  遥控编辑器时先执行一次 `Application.runInBackground = true`（[`developer-guide.md`](../../../../docs/developer-guide.md) 15.6）。
- 想确认埋点真的打出来了：跑一次端到端流程后 `/analyze-telemetry --module sample --last 1`。
- **没有 Showcase 回放场景**：本模块是样板不是玩法，没有需要肉眼确认的表现。
  真玩法模块要按 [`module-dev-spec.md`](../../../../docs/module-dev-spec.md) 建 Showcase 并跑 `/verify-module`。

## 禁止事项

- **不要把 `SampleState` 挪进玩法场景的子作用域**：`GameFlow` 从**根** `IObjectResolver` 解析状态类型，
  而且玩家还在标题界面时 Sample 场景根本没加载，子作用域还不存在——`GoToAsync<SampleState>()` 必然解析失败。
  玩法状态一律经 `GameplayInstaller` 注册进根作用域。
- **不要让 `Game.Core` 认识本模块**：`TitleView` 只抛 `event`，`TitleState` 只发事件，
  「开始之后去哪」的决定在 `SampleTitleRouter` 这一侧。反过来接线（在 Core 里写 `GoToAsync<SampleState>()`）
  会让 asmdef 成环。
- **钱不要用 `float` / `double` 算**：`0.15f` 的真值是 `0.150000005960464…`，
  `50 × (1 - 0.15f)` 按 double 算是 42.4999997，四舍五入成 42，和策划口算的 43 对不上。
  `SampleRules` 用 `decimal` 算中间值，有测试钉着。
- **面板里不要注入服务**：`SampleView` 是 Addressables 实例化的 MonoBehaviour，不经容器，
  构造注入拿不到东西；要什么由 `OnOpenAsync` 的 `arg` 传进来。
- **不要往规则类里塞 `ITelemetryScope` 却绕开 Installer 工厂式注入**：`SampleRules` 目前没有埋点依赖；
  真要加，构造函数只收 `ITelemetryScope`（不收整个 `ITelemetryService`），由 `SampleInstaller` 用
  `builder.Register(c => new SampleRules(..., c.Resolve<ITelemetryService>().Scope("sample")))` 喂进去——
  **禁止** `builder.Register<ITelemetryScope>(...)`：所有 `GameplayInstaller` 共用同一个根作用域，
  两个模块各注册一个 `ITelemetryScope` 会互相覆盖，谁拿到谁的全看注册顺序。
