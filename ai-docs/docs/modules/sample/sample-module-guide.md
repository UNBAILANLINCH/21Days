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
| `SampleState` | `SceneGameState` 子类，加载 Sample 场景、开面板、接返回 | 根作用域单例，`IGameFlow` 解析 |
| `SampleView` | `UIView` 子类，显示一行字 + 返回按钮 | `IUIService` 实例化并持有 |
| `SampleTitleRouter` | 入口点，订阅 `TitleStartClickedEvent` → `GoToAsync<SampleState>()` | 根作用域入口点 |
| `SampleInstaller` | `GameplayInstaller` 子类，把上面这些注册进**根作用域** | Boot 场景的 `GameBootstrap` 物体 |

依赖方向：`SampleState` → `SampleRules` → `IConfigService`。面板不注入任何服务（它由
Addressables 实例化，不经容器），只往外抛 `event`，由状态接住。

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
  └─ OnSceneReadyAsync：SampleRules 算价 → ui.OpenAsync<SampleView>(那行字) → 订阅 OnBackClicked
点「返回标题」
  └─ SampleState.HandleBackClicked → GoToAsync<TitleState>()
SampleState.ExitAsync（基类 sealed）
  ├─ OnSceneUnloadingAsync：退订 → ui.CloseAsync(view) → view = null（幂等）
  └─ 卸载场景
```

`Start` / `Awake` / `OnEnable` 一个都没用到：这个模块里唯一的 MonoBehaviour 是 `SampleInstaller`，
它只实现 `Install`。**订阅与退订严格成对**，位置见上面的流程图。

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
- 端到端：进 Play 模式，Boot → Title → 点「开始」→ 看到折后价 → 点「返回标题」回到 Title。
  遥控编辑器时先执行一次 `Application.runInBackground = true`（[`developer-guide.md`](../../../../docs/developer-guide.md) 15.6）。
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
