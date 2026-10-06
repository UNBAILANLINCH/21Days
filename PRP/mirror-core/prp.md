# PRP：镜核心机制 demo（mirror-core）

**状态：冻结（2026-10-06）。** 策划已换为《聚光灯》（`docs/design/spotlight/`），本 PRP 依据的旧版 `docs/design/features/` [01][02][03][13] 已降为旧版。代码与测试保留（Boot 仍挂 MirrorInstaller，玩家血量归零时的镜碎页仍生效），不再推进、不做视觉验收；聚光灯的「暴露 → 死亡」流程（`docs/design/features-spotlight/02_身份暴露与怀疑.md`）落地时再替换或删除。可能复用：阶段九「随身镜识破」（`docs/design/features-spotlight/07_关卡专属机制.md`）。

> 状态：2026-09-28 第一版，**待用户对第 2 节架构点头后再出 tasks.md**（PRP 第三道门）。
> 需求见同目录 `prd.md`；设计源 `docs/design/features/` [01] [02] [03] [13]。

## 1. 上下文快照（2026-09-28）

**确定性内核**：`LiveInputSource` → `InputCommand` → `EncounterStep`（先 Player 后 Monster，单玩家单怪）→ `PlayerRules` / `MonsterRules`。
`PlayerConfig.maxHealth = 3`、`MonsterConfig.attackDamage = 1`，所以「被击中三次」恰好等于 `Health` 从 3 到 0，`EncounterStep.PendingResult = Defeat`。
战败结果目前**没有消费方**（只有 Session 用它挡自动保存），玩家变黑后原地不动。

**交互类功能不进内核的先例**：Loot 的开箱、Dialogue 的对话都在内核之外，由 `ITickable` 轮询 `Gameplay/*` 动作的 `WasPressedThisFrame`，
并在对话进行、世界暂停、沉浸模式下让位（`SupplyCrateFocus` 是样板）。回放格式因此不用升版。

**可复用**：
- Loot：`LootService.Items`（键 = tbitem id），`tbitem` 已有 1005「破旧信笺」（类别线索）。
- Session：`SessionStartedEvent` 里各分区同步重载（样板 `QuestService.ReloadFromSave`）；`GameSession.RequestSave(reason)` 合并式自动保存。
- 流程：探索重置已用 `IGameFlow.GoToAsync<MonsterEncounterState>()` 重进场景（玩家回出生点、Health 回满、内存进度保留）。
- UI：`IUIService.OpenAsync<T>()`、`UILayer`、`IHudVisibility`、`IWorldPauseService.Acquire`。
- 配置表：`IConfigService.Tables`，Luban 定义样板 `Tables/Defines/quest.xml`（一条一个 JSON 文件）。
- 小人：`FramePuppetGenerator` 从序列帧生成预制体；方舟占位闸门自动覆盖 `Art/Sprites/Characters/Ark/`。
- 输入：键盘 R、V 与手柄左右扳机未被 Gameplay 图占用（执行前再核一次）。

**本次必须规避的 pitfalls**（`ai-docs/pitfalls.md`）：
- Luban JSON 字段不能缺省；生成物内容相同时不重写 `.bytes`。
- 给共享 MonoBehaviour 加序列化字段会静默改掉别的场景：本 PRP **不改** `EncounterSceneView` / `SupplyCrate` 等共享组件的字段。
- MCP 搭 UI 预制体会在活动场景留散件；预制体舞台改动可能不落盘，agent 报「已改」要看 YAML 复核；MCP 改场景当场保存。
- `execute_menu_item` 执行两遍、`execute_code` 可能重复执行：场景接线脚本写成幂等。
- 世界空间头顶标记跨模块复用同一张图会撞脸：通灵视影子用专属图。
- 走 Boot 的回放会写真实存档槽：Showcase 按 `module-verify.md` 新口径用框架的临时槽。
- 回放中途别人保存 `.cs` 会吞掉测试协程：并行会话稳定点前不写 `.cs`。
- 共用工作区：按路径提交，不整份暂存。
- 测试自己装依赖会掩盖接线缺口：Showcase 走 Boot 真实流程，不在测试里手动注册 Mirror 服务。

**适用规则**：`project-root.md`（目录、依赖方向、加能力顺序）、`csharp-code.md`、`unity-assets.md`、`unity-tests.md`、`module-verify.md`（Showcase 新口径：`ScenePath => null` + `EnterWorldFromTitle()` + `Input.Press / WalkTo`）、`model-routing.md`。

## 2. 架构决策（契约，执行时不得偏离；要改先改这里）

### 2.1 为什么新建模块 `Game.Mirror`

- 复用：没有任何模块处理「对场景对象做身份判定」；Taming 是切换操控，Disguise 是禁攻开关，职责都对不上。
- 扩展：塞进 Player 会让玩家模块依赖 Loot / UI / 配置表；塞进 Monster 则照人、自照无处安放。
- 新建：`Assets/_Project/Scripts/Runtime/Mirror/`，命名空间 `Game.Mirror`，属 `Game.Runtime` 程序集。
  通灵视不是镜的能力，但它和照镜共用「场景里谁是妖」的登记与同一套提示呈现，本波放同一模块；若后续变重再拆。

**依赖方向**：Mirror → Player（快照只读）、Monster（`MonsterEncounterState` 类型、场景视图只读）、Loot（`Items` 只读）、Session（事件与 `RequestSave`）、Dialogue（让位判断，同 `SupplyCrateFocus`）、Core。
反向禁止：没有模块引用 Mirror；后续收押模块读 Mirror 的对外接口。

### 2.2 数据：妖物表（Luban）

`Tables/Defines/yao.xml`，模块 `yao`，一只妖一个 `Tables/Data/yao/<id>.json`：

| 字段 | 类型 | 用途 |
| --- | --- | --- |
| `id` | int | 主键，场景标记组件引用它 |
| `disguise_name` | string | 化形时的称呼（照镜前、影子提示都不显示它以外的信息） |
| `true_name` | string | 照见真形后显示 |
| `true_desc` | string | 真形描述 |
| `true_image` | string | 真形图的 Addressables 地址（Sprite） |
| `flaw` | string | 破绽描述（本波只进表，供策划填、给后续调查面板） |
| `obsession` | string | 执念（同上） |
| `clan` | string | 族属（账用，本波只进表） |
| `clue_items` | list,int | 照见真形所需线索，tbitem id；空 = 无要求 |
| `sealable` | bool | 能否收押（后续波次读） |
| `mask` | bool | 能否制面具（后续波次读） |

demo 数据两行，名字全部是中性占位，不写章节人物：
- 1「巡夜人」：SampleScene 巡逻怪，`clue_items` 空（一照就见）。
- 2「井边妇人」：混在村民里、不攻击，`clue_items = [1005]`。

「人」不进妖物表：场景标记组件自己带显示名与形象。

### 2.3 运行时类

| 类 | 职责 / 契约 |
| --- | --- |
| `MirrorConfig`（SO，`Data/Mirror/MirrorConfig.asset`） | `baseRange`(5)、`rangeLossPerCrack`(1.2)、`fanHalfAngle`(35°)、`castCooldown`(0.4 s)、`resultSeconds`(2.5，结果画面停留；确认键提前关)、`sightRadius`(8)、`visionBaseRadius`/`visionLossPerCrack`（屏幕可见范围，画布比例）、`engraving`(string[]，镜缘刻痕占位文案)、各结果标题文案、`shatterText`("镜碎")。运行时只读。 |
| `MirrorSubjectKind` | `Human`、`Object`、`Yao`。 |
| `MirrorSubject : MonoBehaviour` | 场景标记：`kind`、`yaoId`（Yao 时有效）、`displayName` 与 `portrait`（Human / Object 用）、`hintAnchor`（影子提示挂点，空则用自身）。位置取 `transform.position` 投到逻辑 XY（与 `EncounterSceneView` 的 XZ ↔ XY 约定一致）。巡逻怪的逻辑位置以 `MonsterModel.Position` 为准：标记挂在巡逻怪上时勾 `followsMonster`，由绑定器改读模型。 |
| `MirrorRules`（纯 C#） | `Resolve(in MirrorQuery) → MirrorResult`。输入：玩家逻辑位置与朝向、当前作用距离、扇形半角、候选列表（位置、种类、妖 id）、「某妖线索是否齐」判定委托。规则：扇形内、距离 ≤ 作用距离的最近候选；无 → `Nothing`；Human → `Human`；Object → `Object`；Yao 且线索齐 → `TrueForm`，否则 `Blurry`。`ResolveSelf() → Self`。EditMode 穷举。 |
| `MirrorCrackRules`（纯 C#） | `HitCracks(maxHealth, health) = maxHealth − health`（夹 0..3）；`EffectiveRange(config, hitCracks, storyCracks)`；`VisionRadius(config, hitCracks, storyCracks)`；`IsShattered(hitCracks) = hitCracks ≥ 3`。剧情裂痕只进后两者、不进 `IsShattered`。 |
| `SpiritSightRules`（纯 C#） | `IsActive(flags)`：雨 / 夜 / 昏暗任一为真；`Visible(subject, playerPos, radius)`：只有 Yao 且在半径内。 |
| `SpiritSightZone : MonoBehaviour` | 场景区域：`BoxCollider`（isTrigger，仅用 bounds）、`rain` / `night` / `dim` 三个布尔。玩家逻辑位置在任一区域 bounds 的 XZ 投影内且该区域满足条件 → 生效。不用物理回调（玩家不是物理驱动）。 |
| `MirrorSaveData : ISaveData` | Version 1：`List<int> Identified`（已照见真形）、`List<int> GlimpsedBlurry`（见过轮廓）、`int StoryCracks`、`int SelfLooks`。 |
| `MirrorService : IGameService, IDisposable` | 对外门面。`HitCracks`、`StoryCracks`、`EffectiveRange`、`VisionRadius`、`IsIdentified(int yaoId)`、`HasGlimpsed(int yaoId)`；`MirrorResult Cast()`、`MirrorResult LookSelf()`、`void AddStoryCrack()`。`Cast` 写分区、首次照见时 `RequestSave("mirror_identified")`、发 `MirrorCastEvent`。分区每次操作现取 `saves.Get<MirrorSaveData>()`，不缓存实例。订阅 `SessionStartedEvent` 无需重载（不缓存）。 |
| `MirrorSceneBinder : IStartable, IDisposable` | 照 `LootSceneBinder`：`sceneLoaded` 扫描 `MirrorSubject`、`SpiritSightZone`（含未激活）；卸载清空。暴露只读列表。 |
| `MirrorInputPresenter : ITickable, IDisposable` | 读 `Gameplay/Mirror`、`Gameplay/MirrorSelf` 按下沿；让位条件同 `SupplyCrateFocus`（对话中、世界暂停、沉浸、结果画面已开）。冷却内忽略。调 `MirrorService` 后打开结果画面，结果画面打开期间 `IWorldPauseService.Acquire`，关闭时释放。 |
| `MirrorCrackPresenter : ITickable, IDisposable` | 每帧读 `PlayerModel.Snapshot.Health` 与 `StoryCracks`，变化时更新镜图标与可见范围遮罩，击中裂痕增加时发 `MirrorCrackedEvent`、遥测；`IsShattered` 首次为真时打开镜碎页。 |
| `SpiritSightPresenter : ITickable, IDisposable` | 每帧判定是否生效，生效时给半径内的妖挂影子提示（对象池，专属图 `Art/Sprites/Fx/Fx_SpiritShadow.png`，不复用其它模块标记图）；状态变化时遥测，不每帧打日志。沉浸模式下同样显示（这是世界里的「看见」，不是 HUD）。 |
| `MirrorResultView : UIView`（Popup 层） | 镜框、结果图、标题、说明、镜缘刻痕。结果图：Human / Object 用标记组件的 `portrait`；TrueForm 用表里 `true_image`；Blurry 用同一张真形图加模糊 / 压暗材质占位（不给名字）；Self 只显示空镜面。 |
| `MirrorHudView : UIView`（Hud 层） | 右上角镜图标 + 0..3 道裂痕叠图 + 全屏可见范围遮罩（径向暗角，半径随裂痕收）。沉浸模式下镜图标隐藏、遮罩保留（遮罩是视野，不是 HUD）：遮罩放在单独的 `MirrorVisionView`（Background 层或 Hud 层下方且 `VisibleWhenHudHidden`），由执行者按 `UILayer` 实际取值定。 |
| `MirrorShatterView : UIView`（Top 层） | 全黑底、居中「镜碎」二字，别无文字与按钮；任意确认 / 取消 / 点击后关闭，调 `flow.GoToAsync<MonsterEncounterState>()` 重开本场。 |
| `MirrorInstaller : GameplayInstaller` | 注册上面的服务与呈现器、`MirrorConfig`、存档分区、三个事件（`MirrorCastEvent`、`MirrorCrackedEvent`、`MirrorShatteredEvent`）。挂 Boot `GameBootstrap`，排在 `LootInstaller` 之后、`ExplorationInstaller` 之前。 |

> **波 1 定稿（偏离回写，2026-09-28）**：① `MirrorService` 不引用 Session，保存请求走构造参数 `Action<string> requestSave`（安装器写 `reason => resolver.Resolve<GameSession>().RequestSave(reason)`）；构造另需 `PlayerConfig`、`MirrorSceneBinder`，工厂注册。② 追加 API：`CastAt(IReadOnlyList<MirrorCandidate>)`（测试 / 调试）、`TryGetYao(int, out cfg.yao.Yao)`、`LastSubject`；`MirrorCastEvent` 带 `Result` / `Subject` / `Range`。③ 新增值类型 `MirrorResultKind`、`MirrorCandidate`、`MirrorQuery`、`MirrorResult` 各一文件。④ `EncounterSceneView.ToLogicPosition` 由 private 改 public（唯一一处改 Monster；避免 Mirror 另存投影平面开关造成两处约定），绑定器找不到视图时按 XY。⑤ `MirrorSceneBinder` 需 `ITelemetryScope`，追加 `LogicPositionOf` / `CollectCandidates` / `FindActiveZone`。⑥ `MirrorConfig` 追加 `blurryBody` / `nothingBody` / `selfBody`；可见范围默认 1、每裂 0.18（画布比例）。⑦ `AddStoryCrack` 自己埋 `mirror_cracked(source=story)`，CrackPresenter 只埋击中裂痕（`source=hit`）。⑧ **`HitCracks` 只在玩法状态且遭遇已开始时有意义**：遭遇开始前 Health 为 0 会被算成 3，CrackPresenter 必须在 `EncounterStep` 活动时才判定镜碎。⑨ `SpiritSightZone` 范围按 BoxCollider 的 center / size 自算，不读 `Collider.bounds`。⑩ 输入读取行尾照 `SupplyCrateFocus.cs:82` 加 `// lint-ok:`。

> **波 2 定稿（偏离回写，2026-09-28）**：① 镜碎时 `MirrorCrackPresenter` 调 `EncounterStep.End()`：`StartBattle` 在 Runtime 无调用，战败结果从不产生，Session 的战败闸门挡不住；不停遭遇的话离开遭遇的即时保存（`SaveTriggerBridge.cs:108-111`）会把 Health=0 写进存档，读档即镜碎。`End()` 后 `IsGameplayState` 为 false，保存跳过，重进时 `Begin` 重新激活。只调公开方法，内核不改。② 影子提示不挂 `CameraBillboard`（属 IsometricExploration，不在依赖内），由 `SpiritSightPresenter.LateTick` 自对齐相机；池根放妖所在场景，随场景卸载。③ 可见范围改为 0..1 系数：r ≥ 1 隐藏暗角，r < 1 时暗角缩放在 1..2.5 线性插值，始终铺满。④ 追加纯 C# 类型 `MirrorResultInfo`（结果文案）、`MirrorCrackTracker`（裂痕逐帧跟踪 + 防重入）。⑤ 照镜不因「对白焦点在」让位（否则照不了身边的村民）；遭遇不活动时静默忽略。⑥ **镜碎页放 Popup 层并禁 Esc 关闭**（不是 Top）：Top 不进面板栈，Esc 会被判为无面板可关而打开暂停菜单；镜碎期间关 Gameplay 输入图，收尾恢复；`shatterInputDelay` 0.6 s 防连按跳过。视野遮罩放 Hud 层最底且 `VisibleWhenHudHidden`；对白 / 演出整层藏 Hud 时遮罩随之暂隐。⑦ `MirrorConfig` 追加 `spiritShadowSprite`（须拖 `Fx_SpiritShadow`）、`spiritHintLift` 1.2、`spiritHintScale` 1、`spiritHintSortingOrder` 10、`shatterInputDelay` 0.6。⑧ Boot 挂载位置：Inventory 之后、Exploration 之前。⑨ 四个 UI 预制体放 `Prefabs/UI/Mirror/`，进 Addressables **`Mirror` 组**（不进 UI 组，避免与 21days-ad 改 `UI.asset` 撞文件），地址 = 类名。⑩ 埋点追加 `mirror_hud_opened` / `mirror_hud_open_failed` / `mirror_view_failed` / `mirror_image_failed` / `mirror_restart_failed`。

**重开本场的语义**：重进场景 → `EncounterStep.Begin` → `PlayerRules.Reset` 把 Health 回满 → 击中裂痕自然清零；
任务、拾取、辨认记录、剧情裂痕都在内存分区里，保留。镜碎页出现到重进之间不自动保存（战败 tick 已被 Session 闸门挡住，执行时核实）。

**不改内核**：`PlayerRules`、`MonsterRules`、`EncounterStep`、`InputCommand`、回放格式都不动。照镜是否在回放里重现：
不重现（与 Loot 一致）；回放验证用 Showcase 真实按键，不依赖录像。

### 2.4 输入

`GameInput.inputactions` 的 Gameplay 图追加两个 Button 动作：`Mirror`（`<Keyboard>/r`、`<Gamepad>/leftTrigger`）、`MirrorSelf`（`<Keyboard>/v`、`<Gamepad>/rightTrigger`）。
只追加，不改既有绑定；若生成了 C# 包装类，按既有方式重生成。

### 2.5 场景与资产（全部走 MCP，场景改完当场保存）

- 美术占位（已渲好，暂存仓库外 scratchpad `ark-yao/out/`，并行会话稳定点后拷入）：`nian/`（年，`models/2014_nian`，画布 448×192）作「井边妇人」的化形，`FramePuppetGenerator` 生成 `Chibi_nian`；`demon/`（萨卡兹大剑手）与 `slime/`（源石虫）的 idle 首帧 `yao_true_demon.png` / `yao_true_slime.png` 分别作妖 1、妖 2 的真形图。全部放 `Art/Sprites/Characters/Ark/` 下，Ark README 追加三行。已知：nian 待机带悬浮大剑特效，包围盒宽，生成工具按画布高定 PPU，身体会比其他小人矮一截，demo 接受；demon / slime 的序列帧本波不生成小人，只用首帧；影子提示图、镜框、裂痕叠图、暗角图为自制占位，放 `Art/Sprites/Fx/` 与 `Art/Sprites/UI/Mirror/`。
- 真形图进 Addressables（地址 = 文件名，组沿用现有 UI 或新建 `Mirror` 组）。
- `SampleScene.unity`：
  - 巡逻怪 `enerme` 挂 `MirrorSubject(Yao, 1, followsMonster)`。
  - 新增静态 NPC `Yao_WellWoman`：`PuppetVisual` 下挂新化形小人（接法同三个 NPC），`MirrorSubject(Yao, 2)`，头顶常驻台词气泡给一句占位破绽（复用 Dialogue 无树气泡，不新建对话树）。
  - 三个村民 NPC 挂 `MirrorSubject(Human)`，`portrait` 用各自方舟头像。
  - 一处营地道具挂 `MirrorSubject(Object)`（例如物资箱之一），证明「照物正常」。
  - 新增 `SpiritSightZone`（`dim = true`）覆盖「井边妇人」周围一片，出生点不在区内。
  - 物资箱之一的 `itemId` 改为 1005（顺带修掉「#1 ×1」遗留问题中的一只）。
  - **T9 定稿**：`Crate_A` 在 `b469c49` 已是 1005，离妇人最近（约 3 m），直接用作线索箱，场景无需改 itemId；`Yao_WellWoman` 落在 (8.0, 4.8884, 8.8)，由 `Npc_Villager` 克隆（`Instantiate` 自动重定向内部引用），换 `Chibi_nian`、改显示名与气泡台词，**删掉克隆带来的 `ExplorationPointOfInterest`**（妖不进万向标，不做强引导）；`SpiritSightZone_Well` 约 8×8×3 m 罩住妇人，出生点在区外。
- `Boot.unity`：`GameBootstrap` 挂 `MirrorInstaller`，拖 `MirrorConfig.asset`。
- 三个 UI 预制体 `Prefabs/UI/Mirror{Result,Hud,Shatter}View.prefab`（及可能的 `MirrorVisionView`），Addressables 地址 = 类名；在预制体舞台里搭，不在活动场景里搭。

### 2.6 埋点（遥测事件名）

`mirror_cast`（result、yao_id、distance、range）、`mirror_self`、`mirror_blocked`（reason：dialogue / paused / hud_hidden / cooldown / view_open）、
`mirror_identified`（yao_id，首次）、`mirror_cracked`（hit_cracks、story_cracks、range）、`mirror_shattered`、`mirror_restart`、
`spirit_sight_changed`（active、zone）、`mirror_subjects_bound`（count）。都在状态变化时打，不每帧打。

## 3. 验证清单

- [ ] 控制台零编译错误；新 `.cs` 逐个 lint 退出码 0；`invariants.py` 零新增报错。
- [ ] EditMode：`MirrorRulesTests`（V1 V2 V3 V4 V5）、`MirrorCrackRulesTests`（V8 V9）、`SpiritSightRulesTests`（V7）、`MirrorServiceTests`（分区写入、首次照见触发保存、存档往返 V3）、妖物表加载测试（两行、字段齐）。全量全绿。
- [ ] Showcase `Tests/Showcase/Mirror/MirrorShowcase.cs`（新口径，走 Boot 真实流程 + 虚拟输入）：
  照村民（V1）、照井边妇人得轮廓 → 开箱拿信 → 再照得真形（V2 V3）、自照（V4）、走进昏暗区出现影子 / 走出消失（V7）、
  被巡逻怪击中三次看裂痕与遮罩 → 镜碎页 → 任意键 → 回出生点、裂痕 0、箱子仍开（V8 V10）、对话中按照镜键无效（V11）。截图覆盖 V6。
- [ ] 复跑 `/verify-module` Player、Monster、Disguise、Exploration、Session（V13）；战败现在会弹镜碎页并重进场景，这几份若断言了战败后的停留状态要跟着调。
- [ ] code-reviewer 无 BLOCK；`/generate-doc mirror` 三件套、`modules.json`、catalog 补行；roadmap G1 / G4 / E6 状态回填。

## 4. 风险 / 回滚

- **并行会话**：21days-cf 在稳定点（五份回放全绿并提交）之前不许写 `.cs`、不占编辑器；本 PRP 执行从稳定点之后开始。美术渲染在仓库外先做。
- **战败语义变化**：以前战败玩家原地变黑，现在弹镜碎页并重进场景。依赖旧行为的回放要改；已知一条：21days-cf 重写的 Player 回放「受击掉血 → 死亡」用例按「死亡后场景不切换」断言与收尾（基类 TearDown 先经 `IGameFlow` 回标题），合入时要改它的死亡检查点与收尾，位置见该会话提交说明；`EncounterSaveData` 的 Defeat 校验不受影响。
- **可见范围遮罩与沉浸模式**：遮罩层级取错会被沉浸一起藏掉或盖住对白框，执行时以回放截图核对。
- **扇形判定用逻辑坐标**：巡逻怪用模型位置、静态对象用场景坐标投影，两者约定不一致会让照镜「照不到」；`followsMonster` 分支要有测试。
- **占位素材**：全部在 `Characters/Ark/` 或自制，出包闸门覆盖；真形图若放到 `Ark/` 以外必须是自制图。
- 回滚：Mirror 目录、`yao.xml` 与数据、生成物、输入动作两条、Boot 与 SampleScene 的新增节点，按路径 `git checkout` / 删除；场景节点用 MCP 删回。
</content>
</invoke>
