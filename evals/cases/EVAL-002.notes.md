# EVAL-002 设计说明 — 玩法模块加载资源走 IAssetService

> 给人看的。机器判定在 [`EVAL-002.json`](EVAL-002.json)。
> **不要把这份内容发给被测 agent。**

验的规则：`docs/developer-guide.md` 6.8「**禁止**：直接调 `Addressables` / `Resources.Load`」。

## 陷阱在哪

任务说的是「按资源地址 `BackdropMain` 加载」。「地址」这个词天然指向 Addressables，
一个只看任务不看框架的 AI 会直接写 `Addressables.LoadAssetAsync<Sprite>("BackdropMain")` ——
能跑、能过编译、在编辑器里看不出任何问题。

**为什么这条 eval 值得存在**：这个坑 `project-lint` 抓不到（可以抓，但一行正则会误伤
`Core/Assets/AddressablesAssetService.cs` 这个唯一合法的实现），`invariants.py` 的静态扫描也
抓不到（它查的是依赖方向和资产配对，不查「该用哪个接口」）。它只在**新写代码那一刻**发生，
所以只能靠 harness 把 `IAssetService` 这件事在那一刻送到眼前，也只能靠行为 eval 验。

## 为什么用「新模块 Backdrop」而不是改 Sample

`SampleState` 已经注入了 `IAssetService`。让 AI 去改 Sample，它照抄身边的写法就对了——
那测的是「会不会照抄」，不是「harness 有没有把规范送到」。换成一个空模块，
唯一的出路是自己经 `CLAUDE.md` 路由找到 `docs/developer-guide.md` 或 `architecture.md`。

## 期望行为

1. 经 `CLAUDE.md` 的路由表 / `ai-docs/docs/catalog.md` 找到框架契约。
2. 构造注入 `IAssetService`，`LoadAsync<Sprite>("BackdropMain", ct)` 拿 `AssetHandle<Sprite>`。
3. 句柄**存成字段**（只存 `handle.Asset` 是另一个坑：释放后 `Asset` 变 null），退出时 `Dispose`。
4. 类是纯 C#，不继承 MonoBehaviour（玩法规则类可 EditMode 测试，`architecture.md` 第 7 节）。

## 不通过时查什么

| 现象 | 多半是 | 怎么修 |
| --- | --- | --- |
| 直接用了 `Addressables.` | **规则没写清 / 送不到** | 6.8 那条「禁止」写在 developer-guide 深处，路由表里没有一行指向它。考虑在 `catalog.md` 的入口表补一行，或加一条 lint 规则（`path_contains: /Scripts/Runtime/` + `pattern: Addressables\.`，把 Core 排除掉） |
| 用了 `IAssetService` 但没释放 | **AI 行为问题** | 句柄生命周期是要读上下文才懂的事，正则拦不了；连续两次挂就往 `pitfalls.md` 加一条 |
| 写成了 MonoBehaviour | 边缘，不算本条 case 失败 | 这条 case 不判它；要判就另开一条 |
