---
type: external-api
module: mirror
layer: runtime
maturity: seed
---

# Mirror 外部接口

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
| `SpiritSightPresenter.IsActive` | `bool` | 通灵视当前是否生效（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:53`） |
| `SpiritSightPresenter.ShownCount` | `int` | 当前显示中的影子提示数（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:56`） |
| `SpiritSightPresenter.ActiveZone` | `SpiritSightZone` | 当前生效的区域；未生效为 `null`（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:59`） |

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
