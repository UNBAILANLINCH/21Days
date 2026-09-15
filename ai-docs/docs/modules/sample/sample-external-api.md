---
type: external-api
module: Sample
layer: runtime
maturity: stable
---

# Sample 外部接口

> 别的模块要用 Sample 的东西时查这份。**这也是 external-api 的写法样例**：只写公开签名、
> 调用约束和禁止事项，不写内部实现——实现读代码更准、更不会过期。

## 公开类型

### `Game.Sample.SampleRules`（纯 C# 类，根作用域单例）

```csharp
public SampleRules(IConfigService config)                        // 容器注入，不要自己 new
public cfg.Item GetItem(int itemId)                              // id 不存在抛 ArgumentOutOfRangeException
public int GetDiscountedPrice(int itemId, float discount)        // discount 是减免比例，0=原价 1=免费
public int GetOrderTotal(BuyItemIntent intent, float discount)   // = 折后单价 × intent.Count
```

- 三个方法都是**纯函数**：同样的输入恒得同样的输出，不改任何状态，线程无关。
- 结果四舍五入到整数（逢半进位）。中间计算走 `decimal`，别改成 `float` / `double`。
- 三类非法输入都抛 `ArgumentOutOfRangeException`，报错里带上出问题的值：
  id 不在表里、`discount` 不在 0～1（含 `NaN`）、`intent.Count` 不是正数。

### `Game.Sample.BuyItemIntent`（`readonly struct`）

```csharp
public BuyItemIntent(int itemId, int count)
public int ItemId { get; }
public int Count { get; }
```

`default(BuyItemIntent)` 绕得过构造函数（`Count` 为 0），所以**合法性由消费方判**，构造出来不代表合法。

### `Game.Sample.SampleConfig`（ScriptableObject）

```csharp
public int ItemId { get; }      public int Count { get; }      public float Discount { get; }
```

资产在 `Assets/_Project/Data/Sample/SampleConfig.asset`，由 `SampleInstaller` 注册进容器。只读。

### `Game.Sample.SampleState`（`SceneGameState`）

没有公开成员。外部只通过框架切进来：

```csharp
await flow.GoToAsync<SampleState>(ct);
```

### `Game.Sample.SampleView`（`UIView`）

```csharp
public event Action OnBackClicked;      // 「返回标题」被点了
public override UILayer Layer => UILayer.Panel;
```

不要自己 `Instantiate` 它，也不要自己 `OpenAsync<SampleView>`——面板的生命周期归 `SampleState` 管。

## 调用约束

| 想做的事 | 怎么做 | 前提 |
| --- | --- | --- |
| 算个折后价 | 构造注入 `SampleRules`，调 `GetDiscountedPrice` | 容器已建好；`ConfigService` 已初始化（启动流程保证） |
| 切进示例玩法 | `IGameFlow.GoToAsync<SampleState>()` | `SampleInstaller` 已挂在 Boot 场景的 `GameBootstrap` 上 |
| 知道玩家点了标题的「开始」 | 订阅 `Game.Core.Events.TitleStartClickedEvent` | 订阅句柄必须托管（见 `EventConventions.cs` 第 5 条） |

时序：`SampleRules` 只能在**服务初始化完成之后**调用（它读配置表）。写在某个 `IGameService.InitializeAsync`
里、且注册顺序排在 `ConfigService` 之前的话，会撞上「配置表还没初始化完」的异常。

## 禁止事项

- **不要直接读 `TbItem` 绕过 `SampleRules`**：价格口径（减免比例、`decimal` 中间值、逢半进位）只有一份，
  绕过去算出来的价格迟早和界面上显示的对不上。
- **不要在运行时改 `SampleConfig` 的字段**：ScriptableObject 的改动会写回资产文件，
  在编辑器里留下莫名其妙的 diff。需要运行期状态就复制到自己的普通类里。
- **不要 `GetComponent<SampleView>()` 去摸面板**：面板实例归 `IUIService` 所有，
  要拿就 `ui.Get<SampleView>()`（没开返回 null），拿到也不要自己 `Destroy`。
- **不要在 `Game.Core` 里引用本命名空间的任何类型**：asmdef 依赖方向是 `Game.Runtime → Game.Core`，
  反过来会成环，整个工程编译不过。
