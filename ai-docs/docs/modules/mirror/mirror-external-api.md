---
type: external-api
module: mirror
layer: runtime
maturity: seed
---

# Mirror 外部接口

**模块状态：冻结（2026-10-06）。** 策划已换为《聚光灯》（`docs/design/spotlight/`），本模块依据的旧版 `docs/design/features/` [01][02][03][13] 已降为旧版。代码与测试保留（Boot 仍挂 MirrorInstaller，玩家血量归零时的镜碎页仍生效），不再推进、不做视觉验收；聚光灯的「暴露 → 死亡」流程（`docs/design/features-spotlight/02_身份暴露与怀疑.md`）落地时再替换或删除。可能复用：阶段九「随身镜识破」（`docs/design/features-spotlight/07_关卡专属机制.md`）。

> 别的模块要查辨认状态、裂痕 / 作用距离、驱动照镜时查这份。内部结构见 [`mirror-module-guide.md`](mirror-module-guide.md)。

## `Game.Mirror.MirrorService`（根作用域单例 + `IGameService`，构造注入即可）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Cast` | `MirrorResult Cast()` | 照镜：取场景登记的候选判定，写辨认记录，首次照见真形请求保存，发布 `MirrorCastEvent`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:121-125`） |
| `CastAt` | `MirrorResult CastAt(IReadOnlyList<MirrorCandidate> candidates)` | 用给定候选照镜（不经场景登记），供测试 / 调试；事件里的 `Subject` 为 `null`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:128`） |
| `LookSelf` | `MirrorResult LookSelf()` | 自照：结果恒为空白，`SelfLooks` 计数 +1，发布事件（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:131-141`） |
| `AddStoryCrack` | `void AddStoryCrack()` | 剧情裂痕 +1：只缩作用距离 / 可见范围，不计入三裂、不致镜碎（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:144-150`） |
| `HitCracks` | `int HitCracks { get; }` | 击中裂痕（0..3），由 `PlayerModel.Health` 推出；**只在 `EncounterStep.IsActive` 时读才有意义**（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:72-73`） |
| `StoryCracks` | `int StoryCracks { get; }` | 剧情裂痕数（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:75-76`） |
| `EffectiveRange` | `float EffectiveRange { get; }` | 当前照镜作用距离，已计入两种裂痕（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:78-79`） |
| `VisionRadius` | `float VisionRadius { get; }` | 当前屏幕可见范围（画布比例，1 = 不遮），已计入两种裂痕（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:81-82`） |
| `LastSubject` | `MirrorSubject LastSubject { get; }` | 最近一次照镜命中的场景标记；照不到 / 自照 / `CastAt` 时为 `null`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:84-85`） |
| `LastResult` | `MirrorResult LastResult { get; }` | 最近一次照镜 / 自照的判定结果；还没照过时为 `MirrorResult.Nothing`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:87-91`） |
| `IsIdentified` | `bool IsIdentified(int yaoId)` | 该妖是否已照见真形（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:104-105`） |
| `HasGlimpsed` | `bool HasGlimpsed(int yaoId)` | 该妖是否见过模糊轮廓（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:107-108`） |
| `TryGetYao` | `bool TryGetYao(int yaoId, out cfg.yao.Yao yao)` | 查妖物表一行；表未就绪或查不到返回 `false`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:110-115`） |

`Cast` / `LookSelf` 是照镜 / 自照的**唯一入口**：不要绕过它们直接拼 `MirrorRules.Resolve`，那样不会写存档、不会请求保存、
不会发布事件，结果画面与镜图标都不会更新。

## `Game.Mirror.YaoCatalog`（根作用域单例，构造注入即可；2026-10-07 加）

只读查询妖物表（`Tables/Defines/yao.xml`），**不认识存档 / 事件 / 场景**，所以收押、画皮、账簿、调查面板与怪物分层
要按种类查配置时注入它，而不是注入 `MirrorService`。列与读者的对应关系写在 `Tables/Defines/yao.xml` 表头。

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `IsReady` / `Count` / `All` | `bool` / `int` / `IReadOnlyList<cfg.yao.Yao>` | 表是否已就绪；条数；全部行（表顺序）。表没就绪时 `false` / `0` / 空列表 |
| `TryGet` / `Get` | `bool TryGet(int yaoId, out cfg.yao.Yao yao)` / `cfg.yao.Yao Get(int yaoId)` | 按 id 取整行；`TryGet` 查不到返回 `false`，`Get` 查不到抛 `KeyNotFoundException` |
| `TierOf` | `string TierOf(int yaoId)` | 怪物层级原文 `"A"` / `"B"` / `"C"`（`06_怪物分层.md:113`）；查不到 `null`，取值非法抛 `ArgumentException` |
| `IsKillable` | `bool IsKillable(int yaoId)` | 能否常规击杀（`06_怪物分层.md:121`）；查不到为 `false` |
| `DefeatMethodOf` | `string DefeatMethodOf(int yaoId)` | 怎么杀 / 有没有替代途径（`06_怪物分层.md:130` 表 3.2–3.5 的「可否击杀 / 怎么杀」列）；查不到 `null`，取值非法抛 `ArgumentException`。取值只可能是 `可击杀（方式没写）` / `暗杀` / `特殊条件` / `需收服` / `不可杀` |
| `DropItemsOf` | `IReadOnlyList<int> DropItemsOf(int yaoId)` | 掉落（tbitem id）；没定或查不到为空列表 |
| `IsSealable` / `CanMask` | `bool IsSealable(int yaoId)` / `bool CanMask(int yaoId)` | `sealable` / `mask` 两列的只读查询；查不到为 `false` |
| `ClanOf` | `string ClanOf(int yaoId)` | 族属；没有这只妖返回 `null`，有妖但族属留空返回空串 |
| `SealableIds` / `MaskIds` / `KillableIds` | `IReadOnlyList<int>` | 按列反查的 id 列表（表顺序）；面板 / 结算一次取够 |

`TierOf` 只认 `A` / `B` / `C`：`06_怪物分层.md:126` 的「A·下 / B·下」是「原文写在上一层条目下一级」的排版记号，
不是第四个层级，本表不建模。表里出现这种值会抛错，别把排版记号照抄进数据。

`IsKillable` 与 `DefeatMethodOf` 必须一起读：前者答「能不能按常规一路杀掉」，后者答「为什么不能常规杀、以及有没有替代途径」。
只看 `IsKillable` 会把两种相反的情况看成一回事——查勘使是 `false` + `特殊条件`（常态杀不了，但 `03_潜行与暗杀.md:43`
写明「可通过地形隐匿等方式击杀」），籍中吏是 `false` + `不可杀`（`06_怪物分层.md:185`，没有任何途径）。
两列的分工与五个取值的逐条出处写在 `Tables/Defines/yao.xml` 的列注释与
[`mirror-module-guide.md`](mirror-module-guide.md)「`killable` 与 `defeat_method` 两列的分工」一节。

**列白名单校验只做一次**（B13）：tier 与 defeat_method 在首次读表那一次对全表跑一遍，之后每次查询直接读缓存；
`Invalidate()` 语义不变（换过表数据后调它重读），它清缓存与「已校验」标记，下一次访问重新校验。
调用方不要指望「改坏表里某一行的值、不 Invalidate，下一次查询就会报错」。

## `Game.Mirror.MirrorSubject`（场景组件）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Kind` / `YaoId` | `MirrorSubjectKind` / `int` | 对象种类；`YaoId` 只在 `Kind == Yao` 时有意义（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSubject.cs:36-37`） |
| `DisplayName` / `Portrait` | `string` / `Sprite` | 人 / 物照镜时显示的名字与形象；妖的名字与真形图取妖物表（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSubject.cs:38-39`） |
| `FollowsMonster` | `bool` | 挂在巡逻怪上时为真：逻辑位置改读 `MonsterModel.Position`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSubject.cs:40`） |
| `HintAnchor` | `Transform HintAnchor { get; }` | 通灵视影子提示挂点；未拖时返回本物体（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSubject.cs:43`） |
| `WorldPosition` | `Vector3 WorldPosition { get; }` | 场景坐标（未投影）（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSubject.cs:46`） |

## `Game.Mirror.MirrorSceneBinder`（根作用域入口点，`AsSelf`，可构造注入）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `Subjects` / `Zones` | `IReadOnlyList<MirrorSubject>` / `IReadOnlyList<SpiritSightZone>` | 已加载场景里登记的照镜对象 / 通灵视区域（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSceneBinder.cs:40,43`） |
| `ToLogicPosition` | `Vector2 ToLogicPosition(Vector3 worldPosition)` | 场景坐标 → 逻辑 XY，约定同 `EncounterSceneView`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSceneBinder.cs:70-76`） |
| `LogicPositionOf` | `Vector2 LogicPositionOf(MirrorSubject subject)` | 标记的逻辑位置；`FollowsMonster` 时取 `MonsterModel.Position`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSceneBinder.cs:78-84`） |
| `FindActiveZone` | `SpiritSightZone FindActiveZone(Vector2 logicPosition)` | 该逻辑位置落在哪个「条件满足且激活」的区域；都不在返回 `null`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSceneBinder.cs:104-117`） |

`MirrorService.Cast` / `SpiritSightPresenter` 用它取候选与区域；一般不用自己调 `CollectCandidates`，除非要做独立于照镜流程的候选清单。

## `Game.Mirror.MirrorCrackPresenter` / `SpiritSightPresenter`（根作用域入口点，`AsSelf`，回放常用只读成员）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `MirrorCrackPresenter.IsShatterShowing` | `bool` | 镜碎页是否在显示（含重开途中）（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackPresenter.cs:84`） |
| `MirrorCrackPresenter.IsRestarting` | `bool` | 镜碎页已交回、正在重进遭遇（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackPresenter.cs:87`） |
| `SpiritSightPresenter.IsActive` | `bool` | 通灵视当前是否生效（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:55`） |
| `SpiritSightPresenter.ShownCount` | `int` | 当前显示中的影子提示数（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:58`） |
| `SpiritSightPresenter.ActiveZone` | `SpiritSightZone` | 当前生效的区域；未生效为 `null`（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:61`） |

## 事件（MessagePipe，`IPublisher<T>` / `ISubscriber<T>` 注入，一文件一个 `readonly struct`）

| 事件 | 字段 | 何时发布 |
| --- | --- | --- |
| `MirrorCastEvent` | `MirrorResult Result`、`MirrorSubject Subject`、`float Range` | `MirrorService.Cast` / `CastAt` / `LookSelf` 成功后，写分区（与首次照见的保存请求）之后（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCastEvent.cs:9-25`） |
| `MirrorCrackedEvent` | `int HitCracks`、`int StoryCracks` | `MirrorCrackPresenter` 检测到击中裂痕增加时（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackedEvent.cs:6-19`） |
| `MirrorShatteredEvent` | 无载荷 | `MirrorCrackPresenter` 首次判定镜碎时（`Assets/_Project/Scripts/Runtime/Mirror/MirrorShatteredEvent.cs:6-8`） |

订阅按 `EventConventions.cs` 第 5 条：`ISubscriber<T>.Subscribe(...).AddTo(bag)`，句柄进 `DisposableBag` 自行释放。

## 调用时机与前置条件

- 需要 Boot 根作用域已建好（`MirrorInstaller` 已注册）——直接 Play 玩法场景没有 `MirrorService` / `MirrorSceneBinder`。
- `MirrorService` 本身**不检查** `EncounterStep.IsActive`（构造签名里没有 `EncounterStep`，见
  `Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:52-54`）：`Cast` / `CastAt` / `LookSelf` 任何时候调用都会执行判定。
  遭遇是否活动的判断由调用方（`MirrorInputPresenter.Tick`）在调用前做：`if (!step.IsActive) return;`
  （`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:124`），跳过按键但不算「被挡」。跨模块直接调用 `MirrorService`
  时要自己复现这层判断，否则标题页 / 镜碎后一样能照出结果。
- `HitCracks` / `EffectiveRange` / `VisionRadius` 在遭遇未开始（`Health` 尚为初始值之外的场景，例如标题页）读取没有意义，
  见 module-guide「裂痕的取舍」一节。
- `TryGetYao` 依赖 `IConfigService.Tables` 已就绪；未就绪时按查不到处理，不抛异常（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:196-209`）。

## 禁止事项

- **不要绕过 `MirrorService.Cast` / `LookSelf` 直接调 `MirrorRules.Resolve`**：会跳过存档 / 保存请求 / 事件。
- **不要长期持有 `MirrorSaveData`**：本模块不对外暴露该类型，读进度一律走 `MirrorService.IsIdentified` / `HasGlimpsed` / `StoryCracks`。
- **不要在别的模块里给照镜对象重新做一套扫描 / 候选收集**：复用 `MirrorSceneBinder.Subjects` / `Zones` / `CollectCandidates`。
- **不要在 Runtime 里修改 `MirrorConfig` 字段**：ScriptableObject 配置只读，运行时状态见 `MirrorService`。
