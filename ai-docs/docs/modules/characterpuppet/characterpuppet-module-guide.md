---
type: module-guide
module: characterpuppet
layer: runtime
maturity: stable
---

# CharacterPuppet 模块指南

> 改 `Assets/_Project/Scripts/Runtime/CharacterPuppet/` 之前读这份。设计见
> [`PRP/character-puppet/prp.md`](../../../../PRP/character-puppet/prp.md)；与源码不符时以源码为准。
> 别的模块要用小人 → [`characterpuppet-external-api.md`](characterpuppet-external-api.md)；
> 换美术 / 加动作 / 给新角色接小人 → [`characterpuppet-extension-guide.md`](characterpuppet-extension-guide.md)。

## 职责边界

| 做 | 不做 |
| --- | --- |
| 明日方舟风格 Q 版小人的**待机 / 走路 / 奔跑**表现：序列帧 Sprite + Animator（编辑器工具生成） | 真骨骼形变（无 2D Animation / Spine / Live2D 包） |
| 从角色根的**位移**反推停 / 走 / 跑与播放速率，写 Animator 参数 | 攻击 / 受击 / 死亡等其他动作 |
| 朝向：读外部纸片的 `flipX`，或按位移在相机右方向的投影 | 读输入、改角色位置、参与任何玩法判定 |
| 整体染色（`SetTint`，以预制体基底色相乘） | 换装系统、状态色（状态色仍染 `SelectRing`） |

一句话：**小人只是「看位移演动画」的皮**。谁推动了角色根、为什么推，它一概不知。

## 类分工

| 类型 | 文件 | 职责 |
| --- | --- | --- |
| `ChibiPuppet`（MonoBehaviour，预制体根，`DisallowMultipleComponent`） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppet.cs:11` | 表现门面：持有 `Animator` 与 `SpriteRenderer[] parts`（序列帧小人只有一个）；唯一写 Animator 参数与根 `localScale.x` 的地方；另存本预制体剪辑的标定数据 `walkClipSpeed` / `runClipSpeed` / `hasRunClip`（生成工具写入） |
| `ChibiPuppetMotion`（MonoBehaviour，同根，`RequireComponent(ChibiPuppet)`） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppetMotion.cs:13` | 驱动层：`LateUpdate` 读 `trackedRoot` 位移，攒满采样窗口后调规则判停 / 走 / 跑与速率，结果写给 `ChibiPuppet` |
| `ChibiPuppetMotionRules`（纯 C# 静态类，不依赖 UnityEngine） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppetMotionRules.cs:7` | 判定规则：速度与走停滞回、跑走滞回、按剪辑地速标定的播放速率、朝向死区；EditMode 穷举 |
| `ChibiPuppetConfig`（ScriptableObject，运行时只读） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppetConfig.cs:9` | 所有小人共用的阈值与夹取范围；资产 `Assets/_Project/Data/CharacterPuppet/ChibiPuppetConfig.asset` |

分层理由（各文件头注释有完整说明）：

- `ChibiPuppet` 与 `ChibiPuppetMotion` 分开：表现门面不该依赖驱动来源。将来改成输入 / AI 直接驱动，只换 `ChibiPuppetMotion`。
- 剪辑地速放 `ChibiPuppet` 而不放 config：它描述的是「这套帧是按多快画的」，随角色美术走，每个预制体不同；config 是全体共用的手感阈值。
- 规则抽成纯 C#：`EncounterProjection.ResolveFlipX`（`Assets/_Project/Scripts/Runtime/Monster/EncounterProjection.cs:14`）只管纸片翻转阈值，没有滞回与播放速率；塞进 Monster 会让两边职责混杂。

## 依赖方向

```text
Game.CharacterPuppet（Runtime/CharacterPuppet/）
  └─► UnityEngine（Animator、SpriteRenderer、Camera.main）
```

- 不引用 Monster / IsometricExploration / Dialogue 任何类型；与 `EncounterSceneView` 的协作全靠**场景接线**（`facingSource` 指向纸片）。
- 反过来也没有模块在代码里引用 `Game.CharacterPuppet`，只有 Showcase（`Game.Tests.Showcase`）与 EditMode 测试引用。
- 不订阅事件、不注册 DI 服务、不走 Addressables；预制体以场景实例存在。

## 预制体

`Assets/_Project/Prefabs/Characters/Chibi_<名字>.prefab`（现有 amiya / chen / skadi / texas / exusiai），全部由
`FramePuppetGenerator` 生成，结构与约定见下文「序列帧小人」。早期的分件拼接小人（`ChibiPuppet_Player` / `ChibiPuppet_Patrol`、
`Art/Sprites/Characters/Puppet/`、`chr_chibi_*` 动画）已于 2026-09-28 删除，要追溯看 git 历史与 `PRP/character-puppet/`。

- 材质 `Art/Materials/Character/M_SpriteDepthClip`（与原纸片一致，可被灰盒遮挡），sortingLayer `Default`。
- `parts` 数组挂唯一的 `Sprite` 渲染器，`SetTint` 只作用于数组里的渲染器。

## 驱动数据流

```text
角色根 Transform.position（别人推：探索移动 / 巡逻 AI / Showcase 协程）
   │  ChibiPuppetMotion.LateUpdate（ExecutionOrder 100）
   ▼
pendingDelta += Δpos，pendingTime += Time.deltaTime
   │  攒满 config.SampleWindow（0.05 s）才往下
   ▼
ChibiPuppetMotionRules.Evaluate(|Δ|, window, start, stop, wasMoving) → moving, speed
running = moving && ChibiPuppetMotionRules.ResolveRunning(speed, runStart, runStop, wasRunning, puppet.HasRunClip)
ChibiPuppetMotionRules.PlaybackRate(speed, running ? RunClipSpeed : WalkClipSpeed, rateMin, rateMax) → rate（待机时固定 1）
   ▼
ChibiPuppet.SetMoving(moving, running, rate) → Animator Bool "Moving"、Bool "Running"、Float "Speed"
   ▼
ResolveFacing → 变了才 ChibiPuppet.SetFacing(left) → 根 localScale.x = ±原幅值
```

关键位置：

| 环节 | 位置 |
| --- | --- |
| 执行次序 `DefaultExecutionOrder(100)` | `ChibiPuppetMotion.cs:11` |
| `trackedRoot` 自动解析（父链上第一个名字不是 `Visual` 的节点，找不到用自身） | `ChibiPuppetMotion.cs:136` |
| 启用时重置采样并强制待机 | `ChibiPuppetMotion.cs:48` |
| 时停分支 | `ChibiPuppetMotion.cs:63` |
| 采样窗口判定 | `ChibiPuppetMotion.cs:85` |
| 跑走判定与速率标定 | `ChibiPuppetMotion.cs:98` |
| 朝向解析 | `ChibiPuppetMotion.cs:115` |
| 写 Animator 参数 | `ChibiPuppet.cs:73` |
| 翻面（保留实例整体缩放） | `ChibiPuppet.cs:61` |

### 规则细节

- **滞回**：静止时速度 ≥ `moveStartSpeed`（0.15）才起步；走动中速度 ≤ `moveStopSpeed`（0.05）才停（`ChibiPuppetMotionRules.cs:13`）。两阈值之间保持原状态，防止慢速时来回抖。
- **dt ≤ 0 视为静止**，速度记 0。
- **跑走滞回**：走着时速度 ≥ `runStartSpeed`（4.0）才切 Run；跑着时速度 ≤ `runStopSpeed`（3.5）才切回 Walk（`ChibiPuppetMotionRules.cs:30`）。
  两阈值落在玩家走 3 与跑 5 之间（`Data/Player/PlayerConfig.asset`，潜行 1.5）。只在已判定移动时判；`hasRunClip` 为 false 时永不置 Running。
- **播放速率按剪辑标定**：`clamp(speed / clipSpeed, rateMin 0.8, rateMax 1.6)`（`ChibiPuppetMotionRules.cs:59`），
  `clipSpeed` 按当前态取预制体的 `walkClipSpeed`（默认 3）或 `runClipSpeed`（默认 5）。实际速度等于剪辑制作地速时原速播放，脚步与位移对得上。
  上限 1.6 是「不快放」的底线：没有 run 剪辑的小人跑 5 单位/秒时 5 / 3 ≈ 1.67 → 夹到 1.6，比走路快但不抽搐；要更像跑就出 run 帧。
  `clipSpeed ≤ 0`（数据缺失）按原速 1 处理。
- 旧做法（`clamp(speed × 0.53, 0.8, 2.8)`，只有 Walk 态）已废弃：0.53 是给旧拼接小人 0.6 s 走路循环调的，套到方舟 1.13 s 的 Move 循环上跑步变成 2.65 倍快放，僵硬割裂。
- **朝向死区**：沿右方向的分量绝对值 ≤ 死区时保持上次朝向（`ChibiPuppetMotionRules.cs:45`），纯纵向移动不乱翻。

### 朝向来源两种

| 来源 | 条件 | 行为 | 用在哪 |
| --- | --- | --- | --- |
| `facingSource.flipX` | 配了 `facingSource` | 直接跟随该 `SpriteRenderer` 的 `flipX`（真 = 朝左） | SampleScene 玩家 / 巡逻者：跟随被 `EncounterSceneView` 翻转的隐藏纸片 |
| 位移投影 | `facingSource` 为空 | `Dot(Δ, Camera.main.transform.right) / window` 过死区；取不到主相机时保持原朝向 | 验证场景（正交相机，无纸片） |

用速度（除以窗口时长）而不是位移过死区：帧率 / 窗口长短不改变判定（`ChibiPuppetMotion.cs:132`）。
`Camera.main` 只在首次需要时取一次并缓存，不每帧 Find。

## 采样窗口（0.05 s）

早先逻辑 tick（60 Hz）与渲染帧率（120 / 144 Hz）不一致，角色根只在 tick 推进，按渲染帧看位移「有、无」交替，
逐帧判定会闪回待机，所以曾用 0.1 s 窗口攒位移。现在视图已做 **tick 间插值**，角色根逐渲染帧连续移动，
这个理由不再成立；窗口只用来抹平单帧噪声（帧时长抖动、插值末端的小跳），**0.05 s** 起步 / 停步延迟更短、更跟手。
窗口长度在 `ChibiPuppetConfig.sampleWindow`（`ChibiPuppetConfig.cs:21`）。若某个驱动源又回到「只在 tick 推进、无插值」，
窗口须重新拉到至少覆盖两次 tick（≥ 0.035 s，保险 0.1 s），否则会重新闪回待机。

## 时间口径

| 对象 | 用什么时间 | 理由 |
| --- | --- | --- |
| Animator | **unscaled**（预制体 `m_UpdateMode: 2` = `UnscaledTime`） | 对话时停（`Time.timeScale = 0`）期间待机动画继续播放，不定格，画面不「死」 |
| `ChibiPuppetMotion` 采样 | **scaled** `Time.deltaTime`（`ChibiPuppetMotion.cs:61`，带 `// lint-ok`） | 纯表现层，只反推动画状态，不参与逻辑推进与重放；用 scaled 恰好能识别「时停」 |

**时停强制待机**（`ChibiPuppetMotion.cs:63`）：`Time.deltaTime ≤ 0` 时写 `SetMoving(false, false, 1)`，
并清空采样累计、`lastPosition` 更新为当前位置。原因有二：

1. Animator 走 unscaled 时间，不写 `Moving=false` 的话会带着 Walk / Run 继续播放，看起来像原地走；
2. 不清空累计，恢复后会把时停期间（若有传送等）的位移一次性计入速度，误判起步。

恢复后按原逻辑重新采样，最多约一个窗口 + 过渡回到走路。

`lint-ok` 的边界：只允许在「从既有位移反推表现」的场合用 scaled `Time.deltaTime`。
一旦小人要**产生**位移或影响逻辑，必须改走逻辑 tick，不能照抄这一行。

## 动画资产

每个角色一套：`Assets/_Project/Art/Animations/Characters/<名字>/chr_<名字>_<状态>.anim` 与 `chr_<名字>.controller`，
由生成工具写出，参数与过渡约定见下文「序列帧小人」。

- 剪辑绑定路径是**相对小人根的层级路径** `Sprite`（`SpriteRenderer.m_Sprite`）。改子物体名会让剪辑静默失效（不报错，只是不换帧）。
- 不手改，换帧后重跑工具原地更新（GUID 不变）。
- 命名遵循 `Art/Animations/README.md`。

## 与 EncounterSceneView 的关系

`EncounterSceneView`（`Assets/_Project/Scripts/Runtime/Monster/EncounterSceneView.cs:9`）**没改**，小人是在它之外叠上去的：

- SampleScene 里 `player/Visual`、`enerme/Visual` 的纸片 `SpriteRenderer` 设为 `enabled = false`，sprite 已置空
  （`EnsureSprite`（`EncounterSceneView.cs:162`）运行时补 1×1 占位图，组件停用看不见），仍是 `ApplyFlip` 写 `flipX`（`EncounterSceneView.cs:310`）的载体。
- 小人实例挂在 `Visual` 下（localPosition `(0, 0, -0.01)`，略靠前避免与隐藏纸片同面），随 `CameraBillboard` 朝向相机。
- `ChibiPuppetMotion.facingSource` 指向该隐藏纸片；`trackedRoot` 留空，自动解析到 `player` / `enerme` 根。
- 执行次序 100 排在 `EncounterSceneView`（默认 0）之后，读到的是本帧已投影的位置与已翻好的 `flipX`。
- 状态色仍染 `SelectRing`，小人不参与；`BlobShadow` / `NameTag` 不动。

## 序列帧小人（编辑器工具生成，现行唯一的皮）

整帧换 Sprite 的序列帧动画，由编辑器工具从帧目录生成；运行时组件与早期分件小人共用。

| 类型 | 文件 | 职责 |
| --- | --- | --- |
| `FramePuppetGenerator`（EditorWindow + 静态 `Generate(FramePuppetRequest)`） | `Assets/_Project/Scripts/Editor/CharacterPuppet/FramePuppetGenerator.cs` | 菜单 `21Days/角色/从序列帧生成小人…`；改帧贴图导入设置、写 `.anim` / `.controller` / 预制体 / 图集；可重跑、保 GUID |
| `FramePuppetRules`（静态纯规则） | `Assets/_Project/Scripts/Editor/CharacterPuppet/FramePuppetRules.cs` | `chr_<名字>_<状态>_<NN>.png` 解析、按状态分组与数值排序、缺 idle / walk 报错文案（`run` 是可选已知状态，缺了不报错不告警）、PPU / pivot / fps（缺省 24）/ 走跑剪辑地速（缺省 3 / 5）解析、画布尺寸一致性 |
| `FramePuppetMeta` / `FramePuppetRequest` | 同目录 | 可选 `meta.json` 的只读视图（缺字段用哨兵 -1，含 `animations.walk/run.groundSpeed`）；生成参数（目标高度、fps、默认朝左、建图集） |

| 生成物 | 位置 / 约定 |
| --- | --- |
| 帧图 | `Art/Sprites/Characters/<名字>/`；Sprite Single，PPU = 画布高 / 目标高度，pivot = meta.pivot（Custom）。只由工具改，`SpriteImportProcessor` 的全局首次导入规则不动 |
| 剪辑 | `Art/Animations/Characters/<名字>/chr_<名字>_<状态>.anim`：绑定路径 `Sprite`、`SpriteRenderer.m_Sprite`，末尾补一个与末帧相同的键（长度 = 帧数 / fps），`loopTime` |
| 控制器 | 同目录 `chr_<名字>.controller`：参数 `Moving`（bool，默认 false）、`Running`（bool，默认 false）、`Speed`（float，默认 1）；状态 Idle / Walk / Run，**过渡全部 0 时长**（整帧换图不能混合）、无退出时间：Idle→Walk（Moving 且 !Running）、Idle→Run（Moving 且 Running）、Walk→Run（Running）、Run→Walk（!Running）、Walk/Run→Idle（!Moving，排在各自第一条）；Walk 与 Run 绑 `Speed`，Idle 不绑。有 `run` 帧时 Run 用 run 剪辑，没有时 Run **复用 walk 剪辑**（控制器形状统一，驱动层不分支）；其它状态孤立加入 |
| 预制体 | `Prefabs/Characters/Chibi_<名字>.prefab`：根 Animator[UnscaledTime, AlwaysAnimate] + ChibiPuppet（`parts` = 唯一的 `Sprite` 渲染器；`walkClipSpeed` / `runClipSpeed` = meta `animations.walk/run.groundSpeed`，缺省 3 / 5；`hasRunClip` = 有无 run 帧）+ ChibiPuppetMotion（config 同上，`trackedRoot` / `facingSource` 空）；子物体 `Sprite`（材质 `M_SpriteDepthClip`，sortingOrder 0） |
| 图集 | 帧目录下 `<名字>.spriteatlasv2`（Sprite Packer = V2 时建；已存在不动；tight、padding 4、mipmap） |

- **可重跑保 GUID**：控制器 / 剪辑 / 预制体按路径载入原地改；控制器里状态按名字复用（Idle / Walk / Run 已有就更新剪辑与速度绑定，缺的才新建），
  各状态旧过渡先全部删掉再按约定重建，所以重跑不会叠出重复状态或重复过渡；参数按名字 upsert。旧控制器（只有 Idle / Walk）重跑一次即补齐 Run 态与 `Running` 参数。
- **帧率**：无 meta 时默认 24 fps（走 / 跑 12 fps 步态发顿）；有 meta 时以 meta 的 `fps` 为准（重跑前看一眼 `Ark/*/meta.json`）。
- **默认朝右**：帧按朝右画，根 `localScale.x > 0` = 朝右。美术画成朝左时勾「默认朝左」，工具给 `Sprite` 子物体开 `flipX`，
  **不**把根缩放取负（`ChibiPuppet.Awake` 以根缩放符号当初始朝向，取负会让朝向语义反掉）。
- **NPC 接法**：纯纸片 NPC 的 `Visual` 在半身高（y 0.8、缩放 0.625），而 `CameraBillboard` 会连俯仰一起转，小人挂它下面脚底会偏。
  SampleScene 的三个 NPC 在根下另建 `PuppetVisual`（原点 + `CameraBillboard`），小人挂其下；纸片停用、`flipX` 当朝向源、`trackedRoot` 指 NPC 根。
- **占位素材**：`Art/Sprites/Characters/Ark/{amiya,chen,skadi,texas,exusiai}` 是明日方舟基建小人（`scripts/ark-spine-frames/` 渲染，版权归鹰角），
  只作开发期占位。SampleScene：玩家 amiya、巡逻 chen、`Npc_Elder` skadi、`Npc_Traveler` texas、`Npc_Villager` exusiai；目标高度 1.6（与早期分件小人实测高度 1.597 一致）。

## 测试与验证

| 类别 | 路径 | 覆盖 |
| --- | --- | --- |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/CharacterPuppet/ChibiPuppetMotionRulesTests.cs` | 无位移静止、起步阈值、走动中阈值上保持、停步阈值、dt ≤ 0、朝向死区保持与符号、按剪辑地速的播放速率（原速、夹取、地速非正回退）、跑走滞回、无 run 剪辑永不置 Running |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/CharacterPuppet/FramePuppetRulesTests.cs` | 帧名解析（合法 / 各类非法）、分组与数值排序、重复序号报错、缺号与陌生文件告警、缺态文案、run 可选、PPU、pivot 回退链、fps 优先级（缺省 24）、groundSpeed 解析与缺省 3 / 5、meta 解析失败、画布尺寸不一致、状态名映射 |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/CharacterPuppet/CharacterPuppetShowcase.cs` | 待机 → 右走（Walk、`localScale.x > 0`）→ 左走翻面 → 停下回 Idle → 3 单位/秒走路（Animator `Speed` ≈ 1.0）→ 5 单位/秒奔跑（amiya 无 run 帧：仍 Walk 态，`Speed` ≈ 1.6 且高于走路）→ 停下回 Idle → 时停期间 Idle 的 normalizedTime 仍增长 |
| 验证场景 | `Assets/_Project/Scenes/Verify/CharacterPuppet.unity` | 正交相机 + 空物体 `Puppet` 下挂 `Chibi_amiya`，无 `facingSource`（走位移投影分支） |

- Showcase 不加载 Boot 场景（`LoadBootScene => false`），由协程逐帧推根节点；时停检查在 `finally` 里恢复 `timeScale = 1`。
- 跑法：`/verify-module CharacterPuppet`；规则改动先跑 `/unity-test EditMode CharacterPuppet`。
- SampleScene 冒烟：玩家出生静止为 Idle；巡逻者巡逻时 Walk、朝向随移动翻转；对话时停时回到待机。

## 已知约束

- **占位美术**：五套小人都是明日方舟基建小人渲出的占位（版权归鹰角，正式包体不得包含）；正式美术替换见 extension-guide。
- **只有待机 / 走路 / 奔跑**：没有攻击、受击、交互动作；控制器三个状态、三个参数。五套方舟占位都没有 run 帧——源 Spine 模型本身没有跑步动画，渲不出来，不是筛选；
  跑步是 walk 剪辑提速到 1.6 倍上限（地速 5 ÷ walk 剪辑地速 3 ≈ 1.67，夹到 1.6），效果是走快但没有前倾、摆臂、步幅变化，是占位素材的极限。
  程序侧已就绪：换正式美术时按 `docs/artist-guide.md`「角色序列帧交付规范」交一套 `chr_<名字>_run_NN.png`（同画布、同脚底锚点、24 fps；
  `meta.json` 可选写 `animations.run.groundSpeed`，缺省 5），重跑 `21Days/角色/从序列帧生成小人…` 即可自动换成 run 剪辑，运行时代码不用改。
- **跑走只从位移反推**：不读输入、不读 PlayerModel；速度落在 3.5～4.0 之间时保持原态。推动者的「跑」若慢于 4 单位/秒，小人仍演走路。
- **无 Spine / 骨骼形变**：整帧换图，动作细腻程度取决于帧数。
- 各角色预制体是独立资产而非 Prefab Variant；结构统一由生成工具维护，改结构改工具后重跑。
- 翻面是整张镜像：不对称的挂件镜像后会换边；「默认朝左」素材靠子物体 `Sprite.flipX`，与根 `localScale.x` 翻面叠加后仍正确。
- 朝向在 `facingSource` 模式下完全由纸片决定，`facingDeadZone` 不生效。
- 同一小人只允许一个驱动者写 `Moving` / `Running` / `Speed`（目前是 `ChibiPuppetMotion`）；再加一处写参数会互相覆盖。
