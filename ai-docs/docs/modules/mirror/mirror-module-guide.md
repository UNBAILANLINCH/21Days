---
type: module-guide
module: mirror
layer: runtime
maturity: seed
---

# Mirror 模块指南

**模块状态：冻结（2026-10-06）。** 策划已换为《聚光灯》（`docs/design/spotlight/`），本模块依据的旧版 `docs/design/features/` [01][02][03][13] 已降为旧版。代码与测试保留（Boot 仍挂 MirrorInstaller，玩家血量归零时的镜碎页仍生效），不再推进、不做视觉验收；聚光灯的「暴露 → 死亡」流程（`docs/design/features-spotlight/02_身份暴露与怀疑.md`）落地时再替换或删除。可能复用：阶段九「随身镜识破」（`docs/design/features-spotlight/07_关卡专属机制.md`）。

> 改 `Assets/_Project/Scripts/Runtime/Mirror/` 之前读这份。对外怎么调看
> [`mirror-external-api.md`](mirror-external-api.md)，要加东西看 [`mirror-extension-guide.md`](mirror-extension-guide.md)。
> 设计定稿见 [`PRP/mirror-core/prp.md`](../../../../PRP/mirror-core/prp.md) 第 2 节（含波 1 / 波 2 两段「定稿」）；与源码不符时以源码为准。

## 职责边界

**做**：照镜辨形（人 / 物 / 妖的模糊轮廓 / 真形 / 照不到）、自照（恒空白）、通灵视（雨 / 夜 / 昏暗任一区域内提示附近有妖，不给名字）、
镜裂（按玩家受击换算裂痕，缩减作用距离与可见范围）、镜碎（三道击中裂痕后开镜碎页并重开本场）、辨认记录进存档分区。

**不做**（PRD 非目标，`PRP/mirror-core/prd.md` 「范围 / 不做」）：照镜消耗耐久；「照镜背 / 照水面」一类需要可照场景物件的失败用法（远处一律按「照不到」体现）；
镜中世界、收押、归还、画皮、账、追逐躲藏；妖在世界里切换真形外观（真形只在镜中显示）；天气 / 昼夜系统（环境条件是区域上的静态标记）；触屏控件。

## 运行时类分工

| 类 | 是什么 | 谁持有 / 谁调 |
| --- | --- | --- |
| `MirrorConfig` | SO：距离、裂痕缩减、扇形、冷却、结果停留、通灵视、可见范围、各结果文案（`Assets/_Project/Scripts/Runtime/Mirror/MirrorConfig.cs:12-122`） | `Data/Mirror/MirrorConfig.asset` |
| `MirrorSubjectKind` | 对象种类枚举：Human/Object/Yao（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSubjectKind.cs:8-18`） | 场景标记 / 规则共用 |
| `MirrorSubject` | 场景标记：kind、yaoId、displayName、portrait、hintAnchor、followsMonster（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSubject.cs:18-41`） | 场景物体；由 `MirrorSceneBinder` 登记 |
| `SpiritSightZone` | 场景标记：`BoxCollider` 范围 + rain/night/dim（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightZone.cs:17-26`） | 场景物体；由 `MirrorSceneBinder` 登记 |
| `MirrorRules` | **纯 C#**：扇形选最近候选、按种类与线索判结果、自照恒空白、线索是否齐、写辨认记录（`Assets/_Project/Scripts/Runtime/Mirror/MirrorRules.cs:24-141`） | `MirrorService` 调；EditMode 穷举 |
| `MirrorCrackRules` | **纯 C#**：击中裂痕 = maxHealth − Health、作用距离 / 可见范围缩减、镜碎判定（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackRules.cs:11-52`） | `MirrorService` / `MirrorCrackPresenter` 调 |
| `MirrorCrackTracker` | **纯 C#**：逐帧跟踪裂痕变化与镜碎防重入（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackTracker.cs:9-63`） | `MirrorCrackPresenter` 持有 |
| `SpiritSightRules` | **纯 C#**：环境条件是否生效、是否可见、是否落在区域内（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightRules.cs:12-34`） | `SpiritSightPresenter` / `MirrorSceneBinder` 调 |
| `MirrorSaveData` | 存档分区：`Identified` / `GlimpsedBlurry` / `StoryCracks` / `SelfLooks`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSaveData.cs:10-30`） | `ISaveService.Get<MirrorSaveData>()` 产出 |
| `MirrorService` | **对外门面**：照镜 / 自照 / 剧情裂痕，写分区、发事件、埋点（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:34-226`） | 根作用域单例 + `IGameService` |
| `YaoCatalog` | **只读查询**（2026-10-07 加）：妖物表按 `yaoId` 取整行，按列反查 `clan` / `sealable` / `mask` / `tier` / `killable` / `defeat_method` / `drop_items`；**不做玩法判定**，表没就绪一律按「查不到」返回（`Assets/_Project/Scripts/Runtime/Mirror/YaoCatalog.cs`） | 根作用域单例（`AsSelf`），`MirrorInstaller` 注册；收押 / 画皮 / 账簿 / 调查面板与怪物分层按类型注入 |
| `MirrorSceneBinder` | 入口点：登记场景标记与区域、投影坐标、给候选（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSceneBinder.cs:24-191`） | 根作用域入口点（`AsSelf`） |
| `MirrorInputPresenter` | 入口点：读照镜 / 自照按键，让位判断，组结果，开 / 关结果画面并管理图片所有权（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:28`） | 根作用域入口点（`AsSelf`） |
| `MirrorCrackPresenter` | 入口点：驱动镜图标 / 视野遮罩，镜碎时结束遭遇并重开本场（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackPresenter.cs:36-267`） | 根作用域入口点（`AsSelf`） |
| `SpiritSightPresenter` | 入口点：判定通灵视是否生效，给半径内的妖挂影子提示；渲染前对齐当前主相机（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:24`） | 根作用域入口点（`AsSelf`） |
| `MirrorResultView` / `MirrorHudView` / `MirrorVisionView` / `MirrorShatterView` | 四个 UI 面板，见下方「UI 层级决策」 | `IUIService.OpenAsync<T>()` |
| `MirrorResultInfo` | 结果画面内容（标题 / 名字 / 说明 / 刻痕 / 图），按种类组文案的纯函数（`Assets/_Project/Scripts/Runtime/Mirror/MirrorResultInfo.cs:14-95`） | `MirrorInputPresenter` 组装 |
| `MirrorCandidate` / `MirrorQuery` / `MirrorResult` / `MirrorResultKind` | 纯值类型：候选、判定输入、判定结果、结果种类 | `MirrorRules` 的输入 / 输出 |
| `MirrorCastEvent` / `MirrorCrackedEvent` / `MirrorShatteredEvent` | 一文件一个 `readonly struct` 事件 | `MirrorInstaller.InstallEvents` 注册 broker |
| `MirrorInstaller` | `GameplayInstaller`：注册以上全部（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInstaller.cs:36-131`） | `Boot.unity` 的 `GameBootstrap` 物体 |

## 依赖方向

`Game.Mirror → Game.Player`（`PlayerModel` / `PlayerConfig` 只读）、`→ Game.Monster`（`MonsterModel` 位置只读、`EncounterStep` 判活动、
`EncounterSceneView.ToLogicPosition` 投影）、`→ Game.Loot`（`LootService.Items` 只读判线索）、`→ Game.Session`（`MirrorInstaller` 解析
`GameSession` 组保存委托）、`→ Game.Dialogue`（`DialogueService.IsRunning` 判让位）、`→ Game.Core`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInstaller.cs:5-6`：
「Mirror → Session 与 Mirror → Loot / Monster / Player / Dialogue 同为 `Game.Runtime` 程序集内的单向引用；Session 引用 Monster / Loot / Quest /
Dialogue，但没有任何模块引用 Mirror，不成环」）。反向禁止：目前没有模块引用 `Game.Mirror`；后续收押模块只应读 Mirror 的对外接口。

## `killable` 与 `defeat_method` 两列的分工（2026-10-07 加 defeat_method）

聚光灯怪物分层总表的表头是「**可否击杀 / 怎么杀**」（`docs/design/features-spotlight/06_怪物分层.md:130`），
原来的 `killable` 一个 bool 只答得了前半句。真源自己就写了反例：R9（`06_怪物分层.md:121`）把「不可击杀」（籍中吏，`:185`）、
「常态不可击杀」（查勘使，`:161`）、「无法被常规击杀」（户绝民，`:155`）、「持有篮子时不可击杀」（拾骨人，`:159`）都算成「不能常规杀」，
可 `03_潜行与暗杀.md:43` 又写明查勘使「**可通过地形隐匿等方式击杀，难度较高**」——「常态不可击杀」不等于「不可杀」，
一个 bool 表达不了。所以 `tier` / `killable` / `drop_items` 之外补了 `defeat_method` 一列，两列分工写死在 `Tables/Defines/yao.xml` 的列注释里：

- **`killable`**：答「能不能按常规一路杀掉」。R9 的四种说法都填 `false`。
- **`defeat_method`**：答「为什么不能常规杀、以及有没有替代途径」。取值只从真源归纳，五个：
  `可击杀（方式没写）`（表 3.2–3.5 里写「可击杀（方式没写）」的那些，`06:132` / `:154` / `:156` / `:169` / `:180` / `:183` / `:184`）／
  `暗杀`（`03:36` 市令、`03:39` 殁吏、`03:46` 执事）／`特殊条件`（`03:43` 查勘使地形隐匿、`06:159` 拾骨人「此后」、`03:47` 蜃师地图解谜、
  `06:173` 食教者第二阶段、`06:160` 老吏、`06:187` 钱塘君）／`需收服`（`03:38` 户绝民湿皮收服）／`不可杀`（`06:185` 籍中吏）。
- 两条一起读才分得清例外：查勘使是 `killable=false` + `特殊条件`，籍中吏是 `killable=false` + `不可杀`。
- `YaoCatalog.ValidateDefeatMethod` 照 `ValidateTier` 的样子校验白名单，取值不在名单里报 `ArgumentException`（消息带 id、原值与 `yao.xml`）。
  文档里的排版记号不能照抄进数据——`06:126` 的「A·下 / B·下」是层级列的排版记号，`06:159` 那种原文整句也不是取值。

**列白名单校验只做一次**（B13，2026-10-07 收口）：tier 与 defeat_method 在**首次读表那一次**对全表跑一遍
（`YaoCatalog.EnsureTableRead`），之后每次查询直接读缓存，不再回头重扫全表——本表是只读的生成物，运行期没有旁路改写，
重复校验换不来新信息，只会让每个查询方都背上一次全表扫描。`Invalidate()` 语义不变（测试换过表数据后调它重读），
它同时清缓存与「已校验」标记，下一次访问重新校验。回归用例：`Catalog_SecondReadAfterValidation_DoesNotRevalidate`。

## 裂痕 = maxHealth − Health 的取舍

击中裂痕**不另起状态**，直接由 `HitCracks(maxHealth, health) = Clamp(maxHealth − health, 0, 3)` 推出（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackRules.cs:16-18`），
理由见文件头注释：`PlayerConfig.MaxHealth = 3`、`MonsterConfig.attackDamage = 1` 时恰好一击一裂，不用给确定性内核加新计数器（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackRules.cs:5`）。
代价：遭遇开始前 `Health` 为 0 会被算成三道裂痕（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:30-31`），所以 `HitCracks` / `EffectiveRange` / `VisionRadius` 只在
`EncounterStep.IsActive` 时才有意义——`MirrorCrackPresenter.Tick` 把 `step.IsActive` 传给 `MirrorCrackTracker.Update` 做门控，
不活动时不判镜碎（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackPresenter.cs:104-105`）。剧情裂痕（`MirrorSaveData.StoryCracks`）只缩作用距离与可见范围，不进 `IsShattered`
（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackRules.cs:43-44`）。

## 镜碎调 `EncounterStep.End()` 的原因

`MirrorCrackPresenter.Shatter()` 在发布 `MirrorShatteredEvent` 之前先调 `step.End()`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackPresenter.cs:152`）。原因写在类文档
（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackPresenter.cs:30-34`）：demo 里从未调用过 `StartBattle`，`Defeat` 结果永远不会产生，Session 的战败闸门挡不住；
`EncounterStep.End()` 后 `SessionStateAdapter.IsGameplayState` 变 false，离开遭遇的即时保存与期间的合并式自动保存都会跳过，
存档里不会留下 `Health = 0` 的现场；重开本场调 `MonsterEncounterState.Begin` 会让 `PlayerRules.Reset` 把 `Health` 回满，裂痕随之清零。

## UI 层级决策

- **镜碎页放 Popup 层并禁 Esc 关闭**：`MirrorShatterView.Layer => UILayer.Popup`、`CloseOnCancel => false`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorShatterView.cs:38,40`）。
  原因见类文档（`Assets/_Project/Scripts/Runtime/Mirror/MirrorShatterView.cs:19-21`）：Top 层不进面板栈，Esc 会被判为「无面板可关」而打开暂停菜单；Popup 且 `CloseOnCancel = false`
  时取消键交给 `MirrorCrackPresenter.PollShatterKeys` 当「任意键」用（`Assets/_Project/Scripts/Runtime/Mirror/MirrorCrackPresenter.cs:158-169`）。
- **视野遮罩放 Hud 层最底且沉浸可见**：`MirrorVisionView.Layer => UILayer.Hud`、`VisibleWhenHudHidden => true`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorVisionView.cs:38,40`），
  打开时 `transform.SetAsFirstSibling()` 压到 Hud 层最底（`Assets/_Project/Scripts/Runtime/Mirror/MirrorVisionView.cs:50`）。原因见类文档（`Assets/_Project/Scripts/Runtime/Mirror/MirrorVisionView.cs:18-26`）：
  遮罩是「视野」不是 HUD，沉浸模式下镜图标（`MirrorHudView`，`VisibleWhenHudHidden` 未覆写即为 false）会隐藏，遮罩要留着。
- **结果画面追加文字卡与刻痕铭牌**（集成后美术调整，`MirrorResultView.cs` 本身无新增字段）：`TextPanel` 是标题/名字/说明背后的深色卡片，
  600×300（命名见 `Assets/_Project/Prefabs/UI/Mirror/MirrorResultView.prefab:643`，`m_SizeDelta` 见 `Assets/_Project/Prefabs/UI/Mirror/MirrorResultView.prefab:666`，
  深色 `m_Color` 见 `Assets/_Project/Prefabs/UI/Mirror/MirrorResultView.prefab:689`）；
  `EngravingPlate` 是镜缘刻痕的铭牌，挂在镜框（`Mirror`）组下方，420×92（命名见 `Assets/_Project/Prefabs/UI/Mirror/MirrorResultView.prefab:1041`，
  `m_SizeDelta` 见 `Assets/_Project/Prefabs/UI/Mirror/MirrorResultView.prefab:1064`，`m_Father` 指向镜框组见 `Assets/_Project/Prefabs/UI/Mirror/MirrorResultView.prefab:1059`）。

## 照镜不进确定性内核（同 Loot）

`MirrorInputPresenter` 直接读 `Gameplay/Mirror`、`Gameplay/MirrorSelf` 的按下沿（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:106-107`，
`actions.Gameplay.Mirror` 一行标了 `// lint-ok: 照镜不属于确定性模拟，同 InteractionFocus 读交互动作`，`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:103`）。
理由见文件头（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:7`）：照镜属于交互类功能，同 Loot 的开箱、Dialogue 的对话，都在确定性内核之外，回放格式因此不用升版。

## 结果图片的生命周期

打开期间的图片句柄归 `ShowAsync` 本次调用所有；只有成功接管仍在显示的结果画面后，才转交给呈现器字段，随关闭释放。
取消、打开失败或作用域销毁后的晚返回统一经过 `finally`，先清除本次内容在视图中的图片引用，再释放未转交的句柄
（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:160`、`:204`）。
已显示时 `Dispose` 发起关闭；淡出失败也会补做视图清理后释放句柄，不能在图片仍被视图使用时提前释放
（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:142`、`:296`）。面板实例的销毁与 UI 栈仍归 `IUIService`。
关闭失败记录 `mirror_close_failed`（异常及 `disposed` 状态），用于区分作用域退出与正常使用中的关闭故障。

## 通灵视朝向与相机切换

`SpiritSightPresenter` 订阅 `RenderPipelineManager.beginCameraRendering`，直接使用回调中的相机；只处理启用且激活、
类型为 `Game`、标签为 `MainCamera` 的相机。每次渲染前取其当前旋转，不再实现 `ILateTickable`、不查询 `Camera.main`，也不缓存旧相机
（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:51`、`:113`）。`Dispose` 对称退订（`:128`）。
当前接线依赖项目的 URP / SRP 渲染回调；未提供 Built-in 管线回调。切镜、改标签或卸载相机不需要额外刷新缓存。

## Addressables 用 `Mirror` 组

`Assets/AddressableAssetsData/AssetGroups/Mirror.asset` 现有 **6 条**（`m_GroupName: Mirror`，
`Assets/AddressableAssetsData/AssetGroups/Mirror.asset:15`）：两张真形图 `yao_true_demon`（`:26`）/ `yao_true_slime`（`:36`），
加四个 UI 预制体，地址 = 类名——`MirrorResultView`（`:31`）、`MirrorHudView`（`:41`）、`MirrorVisionView`（`:46`）、`MirrorShatterView`（`:21`）。
预制体没有进 `UI` 组（避免和另一会话改 `UI.asset` 撞文件）。四个预制体已落地在 `Assets/_Project/Prefabs/UI/Mirror/`，
`Data/Mirror/MirrorConfig.asset` 已建好（`Assets/_Project/Data/Mirror/MirrorConfig.asset`），Boot 挂载见下节「场景接线要求」。

## 场景接线要求

缺任一项都**不会编译报错**，只会运行时不动或报 Error：

| 项 | 要求 | 缺了会怎样 |
| --- | --- | --- |
| Installer | `Boot.unity` 的 `GameBootstrap` 挂 `MirrorInstaller`，**Config** 拖 `Data/Mirror/MirrorConfig.asset` | 没挂：解析不到 `MirrorService`；没拖：记 Error 并用代码建的默认值顶上（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInstaller.cs:122-130`）。已接线：`Assets/_Project/Scenes/Boot.unity:327` 挂了 `MirrorInstaller`，`:330` 的 `config` 已指向 `Assets/_Project/Data/Mirror/MirrorConfig.asset`（guid 比对一致） |
| Installer 顺序 | 排在 `InventoryInstaller` 之后、`ExplorationInstaller` 之前（`PRP/mirror-core/prp.md` 波 2 定稿 ⑧） | 已按此顺序接线，`GameBootstrap` 组件列表见 `Assets/_Project/Scenes/Boot.unity:132-145`（12 个 MonoBehaviour 组件）：`InventoryInstaller` 排第 9 位（列表项 `Assets/_Project/Scenes/Boot.unity:142`，脚本引用 `Assets/_Project/Scenes/Boot.unity:289`），`MirrorInstaller` 第 10 位（列表项 `Assets/_Project/Scenes/Boot.unity:143`，脚本引用 `Assets/_Project/Scenes/Boot.unity:327`），`ExplorationInstaller` 第 11 位（列表项 `Assets/_Project/Scenes/Boot.unity:144`，脚本引用 `Assets/_Project/Scenes/Boot.unity:263`）；类文档字符串已同步（`Assets/_Project/Scripts/Runtime/Mirror/MirrorInstaller.cs:31`：「排在 `InventoryInstaller` 之后、`ExplorationInstaller` 之前」） |
| 照镜对象 | 场景物体挂 `MirrorSubject`：`Human`/`Object` 填 `displayName` + `portrait`；`Yao` 填 `yaoId`（对应 `Tables/Defines/yao.xml` 主键）；巡逻怪上勾 `followsMonster` | `yaoId` 未填的 `Yao` 标记：`MirrorSceneBinder` 记 Warn，照镜只得到模糊轮廓（`Assets/_Project/Scripts/Runtime/Mirror/MirrorSceneBinder.cs:168-171`）。已接线（`Assets/Scenes/SampleScene.unity`）：`enerme`（Yao,1,followsMonster，`:6422-6430`）、新建 `Yao_WellWoman`（Yao,2，`:3313-3321`；由 `Npc_Villager` 克隆，位置 (8, 4.8884, 8.8)，无 `ExplorationPointOfInterest`）、三村民（Human：「村民」`:6952-6960`、「旅人」`:8829-8837`、「老者」`:10148-10156`）、`Crate_A`（Object「物资箱」，`:8136-8144`） |
| 通灵视区域 | 场景物体挂 `SpiritSightZone` + `BoxCollider`（`isTrigger`），勾 `rain`/`night`/`dim` 任一 | 没有 `BoxCollider`：`TryGetWorldCorners` 返回 false，该区域永远不生效（`Assets/_Project/Scripts/Runtime/Mirror/SpiritSightZone.cs:42-48`）。已接线：`SpiritSightZone_Well`（`Assets/Scenes/SampleScene.unity:4863` 命名，`:4881-4884` `area`/`rain=0`/`night=0`/`dim=1`），罩住新建的 `Yao_WellWoman` 周围 |
| 妖物表 | `Tables/Defines/yao.xml` → `Assets/_Project/Data/Config/yao_tbyao.bytes`（已生成落地）；字段 `id`/`disguise_name`/`true_name`/`true_desc`/`true_image`/`flaw`/`obsession`/`clan`/`clue_items`/`sealable`/`mask`（`Assets/_Project/Scripts/Core/Config/Generated/yao/Yao.cs:43-83`） | 查不到该 id：`MirrorService.FindYao` 记 Warn，按线索不齐处理，只给模糊轮廓（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:196-209`） |
| 输入动作 | Gameplay 图已有 `Mirror`（键盘 R / 手柄左扳机，`Assets/_Project/Data/Input/GameInput.inputactions:126`）与 `MirrorSelf`（键盘 V / 手柄右扳机，`Assets/_Project/Data/Input/GameInput.inputactions:135`），已生成到 `Assets/_Project/Scripts/Core/Input/GameInput.cs`（**生成物，行号随动作增减整体漂移，故不写行号**：搜 `m_Gameplay_Mirror` 的 `FindAction` 与 `@Mirror` 包装属性） | 已接线（当前工程状态） |
| 预制体与配置资产 | 四个 UI 预制体放 `Prefabs/UI/Mirror/`，`Data/Mirror/MirrorConfig.asset`，Addressables 地址 = 类名（见上节） | 缺预制体：`IUIService.OpenAsync<T>()` 找不到地址会抛异常，对应面板打不开。已落地：`Assets/_Project/Prefabs/UI/Mirror/{MirrorResultView,MirrorHudView,MirrorVisionView,MirrorShatterView}.prefab`、`Assets/_Project/Data/Mirror/MirrorConfig.asset` |

## 验证入口

`Assets/_Project/Scripts/Tests/Showcase/Mirror/MirrorShowcase.cs`（`ScenePath => null` + `EnterDemoWorld`，走 Boot 真实流程 + 虚拟输入）
6 条用例：`LookAtVillager_ShowsHuman`、`WellWoman_BlurryThenTrueForm`、`SelfLook_ShowsBlank`、`DialogueBlocksMirror`、
`ThreeHits_ShatterAndRestart`、`StoryCrack_ShrinksVisionOnly`，回放统一在 `Assets/Scenes/SampleScene.unity` 上跑。
历史批次结论 **PASS**（检查点失败 0 个、运行时异常 0 条），见 `Logs/verify/mirror/20260928-084017/report.md:6`；该记录不覆盖下述后续呈现器修复。
EditMode 覆盖：`MirrorRulesTests`（28 用例）、`MirrorCrackRulesTests`（9）、`MirrorCrackTrackerTests`（9）、`SpiritSightRulesTests`（8）、
`MirrorServiceTests`（12）、`MirrorSceneBinderTests`（5）、`MirrorResultInfoTests`（7）、`MirrorInputPresenterTests`（纯判定及异步生命周期）、
`MirrorCrackPresenterTests`（5 组 TestCase）、`MirrorHudViewTests`、`MirrorVisionViewTests`、`YaoTableTests`（24，2026-10-07 加 `defeat_method` 那一列后从 16 涨到 24），均在
`Assets/_Project/Scripts/Tests/EditMode/Mirror/`。跑 `/unity-test EditMode Mirror`；端到端视觉验收跑 `/verify-module Mirror`（编辑器须打开）。

2026-10-07 `defeat_method` 那轮的实际执行结果：`run_tests(mode="EditMode", group_names="Game.Tests.EditMode.Mirror")`，
job `3a8706b30b6c4734830c06715e1efba0` 为 `succeeded`，`total 158 / passed 158 / failed 0 / skipped 0`（2.3130155 秒），
控制台 `error CS` 0 条。注意 `include_details` 的结果文本会被 MCP 截断（约 2 万字符），
要点名核对某个测试类得在客户端侧过滤，别指望一次读完。

2026-09-29 两项呈现器修复已完成首轮验证：Mirror EditMode 执行 140 项，job `bbf2d4b2a9c6422fa5879e4731f4b25c` 为 `succeeded`；
Mirror Showcase 6 项 PASS（`Logs/verify/mirror/20260929-055540/report.md`），所属整批 PlayMode job `2f8128b2e6014eeea505c23ec8b739ee` 执行 23 项并成功。
主审窗口已独立查询两份 job、读取回放报告、查看结果画面与通灵视截图，并实际读取控制台错误 0 条。域重载后 MCP job 的 `result` 为 null，不能把估计的 `progress.total=1043` 当成实际执行数。
收尾新增关闭失败埋点后，包含 Mirror 的 EditMode 回归 job `0f9f708b6a1e43f1b0786e709e146de6` 为 **461/461 通过、0 失败、0 跳过**（11.6726942 秒）；
主审窗口直接读取到准确 `result.summary` 并复查控制台错误 0 条。提交范围及最终验证见 [`PRP/mirror-core/tasks.md`](../../../../PRP/mirror-core/tasks.md) 的呈现器修复审查。
`MirrorInputPresenterTests` 新增加载取消 / 晚返回、打开失败、销毁期间关闭、重复开关、淡入中已关闭和关闭失败用例
（`Assets/_Project/Scripts/Tests/EditMode/Mirror/MirrorInputPresenterTests.cs:93`）；新增 `SpiritSightPresenterTests` 验证切镜 / 标签变更、
禁用 / 销毁 / 非 Game 相机过滤及退订（`Assets/_Project/Scripts/Tests/EditMode/Mirror/SpiritSightPresenterTests.cs:50`）。
统一验证过滤器为 `Game.Tests.EditMode.Mirror`，另跑 Mirror Showcase 核验实际渲染朝向和结果画面；测试源码存在不代表执行通过。

## 已知限制 / 未做

已验证 V8「全程界面上没有生命数字或血条」（针对玩家）：遭遇场景调试面板已去掉玩家生命数字，只显示潜行 / 伪装
（`Assets/_Project/Scripts/Runtime/Monster/EncounterSceneView.cs:257-258`）；怪物生命数字仍保留（该面板是开发调试用，不是玩家可见 HUD）。

- 照镜不消耗耐久，只有被妖击中才产生裂痕（PRD 非目标，矛盾 #4 的 demo 默认）。
- 剧情裂痕只提供接口 `MirrorService.AddStoryCrack()`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorService.cs:144-150`），本波不接任何剧情触发。
- 失败用法只有「照不到」（扇形外或超距）；「照镜背 / 照水面」需要可照场景物件，留给后续镜界层。
- 妖不会在世界里切换真形外观；真形图只在结果画面显示。
- 没有天气 / 昼夜系统：`SpiritSightZone` 的 rain/night/dim 是场景上的静态勾选，不随时间变化。
- 不做触屏控件（PC 优先）。
- 真形描述超过约 3 行会溢出 `TextPanel`（固定 600×300，`Assets/_Project/Prefabs/UI/Mirror/MirrorResultView.prefab:666`），需要长描述时要么精简文案要么后续加自适应。
- 影子提示观感偏淡，回放截图可见（`Logs/verify/mirror/20260928-084017/13-通灵视·影子提示.png`）。
- 暗角在 1 道裂痕时偏轻：`visionLossPerCrack = 0.18`（`Assets/_Project/Scripts/Runtime/Mirror/MirrorConfig.cs:51`），单裂痕只收掉 18% 可见范围，回放截图见 `Logs/verify/mirror/20260928-084017/09-第1道裂痕.png`；建议调到 0.3（单裂痕收掉 30%），本轮未改，留给开发者决定。
