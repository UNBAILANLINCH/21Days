# EVAL-003 设计说明 — 面板监听的订阅与退订成对

> 给人看的。机器判定在 [`EVAL-003.json`](EVAL-003.json)。
> **不要把这份内容发给被测 agent。**

验的规则：`docs/developer-guide.md` 11.1「监听**只写在 `OnOpenAsync` / `OnCloseAsync`**，
不写 `OnEnable` / `OnDisable`」。

## 陷阱在哪

这条是**规则打架**的地方，也是它值得做成 eval 的原因：

- `.claude/rules/csharp-code.md` #生命周期 说的是「`OnEnable`/`OnDisable` **成对**订阅/退订事件」。
  这条规则常驻注入，一编辑 `.cs` 就在眼前。
- 但 `UIView` 是例外：面板被上层全屏面板盖住时 `UIService` 会 `SetActive(false)`，
  `OnEnable`/`OnDisable` 会重复触发，按常驻规则写反而是错的。
- 例外只写在 `docs/developer-guide.md` 11.1 和 `SampleView.cs` 的类注释里——**两个都不常驻**。

所以照着最显眼的那条规则做，会做错。测的正是：harness 有没有在写面板的那一刻，
把这条例外送到比常驻规则更靠前的位置。

## 期望行为

```csharp
public override UniTask OnOpenAsync(object arg, CancellationToken ct)
{
    againButton.onClick.RemoveListener(HandleAgain);   // 先摘再加，复用打开时不叠监听
    againButton.onClick.AddListener(HandleAgain);
    return UniTask.CompletedTask;
}
public override UniTask OnCloseAsync(CancellationToken ct)
{
    againButton.onClick.RemoveListener(HandleAgain);
    return UniTask.CompletedTask;
}
```

外加：`[SerializeField] private` 拿控件引用（不 `public`）；点击往外抛 `event Action`，
不在面板里注入 `IGameFlow`（面板经 Addressables 实例化，不过容器，注入进来是 null）。

## 不通过时查什么

| 现象 | 多半是 | 怎么修 |
| --- | --- | --- |
| 监听写进了 `OnEnable` / `OnDisable` | **规则没写清**，而且是可预期的 | 例外没有常驻载体。修法按优先级：① `csharp-code.md` #生命周期 那条后面补半句「`UIView` 例外，见 developer-guide 11.1」；② 给 `required_reads.json` 加一条「改 `*View.cs` 前读 developer-guide 11.1」 |
| `AddListener` 有、`RemoveListener` 没有 | **AI 行为问题** | 这是纯粹的疏忽，不是信息缺失。连续两次挂就升成 lint 规则（同文件里 `AddListener` 与 `RemoveListener` 计数不等—— 注意这需要文件级判据，`rules.json` 现有字段做不到，得进 `invariants.py`） |
| 面板里注入了服务 | 规则没写清 | `sample-module-guide.md` #禁止事项 有这条，但它要模块 guide 被读到才生效；查 `required-reads` 钩子在这条路径上有没有触发 |

判定用 `files: "**/*View.cs"` 限定范围：`OnEnable` 在别处（比如敌人注册表）是**正确**写法，
全局 absent 会误伤。glob 收窄是这条 check 能成立的前提。
