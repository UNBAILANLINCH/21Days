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
| 明日方舟风格 Q 版小人的**待机 / 走路**表现：序列帧 Sprite + Animator（编辑器工具生成） | 真骨骼形变（无 2D Animation / Spine / Live2D 包） |
| 从角色根的**位移**反推走 / 停与播放速率，写 Animator 参数 | 攻击 / 受击 / 死亡等其他动作 |
| 朝向：读外部纸片的 `flipX`，或按位移在相机右方向的投影 | 读输入、改角色位置、参与任何玩法判定 |
| 整体染色（`SetTint`，以预制体基底色相乘） | 换装系统、状态色（状态色仍染 `SelectRing`） |

一句话：**小人只是「看位移演动画」的皮**。谁推动了角色根、为什么推，它一概不知。

## 类分工

| 类型 | 文件 | 职责 |
| --- | --- | --- |
| `ChibiPuppet`（MonoBehaviour，预制体根，`DisallowMultipleComponent`） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppet.cs:10` | 表现门面：持有 `Animator` 与 `SpriteRenderer[] parts`（序列帧小人只有一个）；唯一写 Animator 参数与根 `localScale.x` 的地方 |
| `ChibiPuppetMotion`（MonoBehaviour，同根，`RequireComponent(ChibiPuppet)`） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppetMotion.cs:12` | 驱动层：`LateUpdate` 读 `trackedRoot` 位移，攒满采样窗口后调规则，结果写给 `ChibiPuppet` |
| `ChibiPuppetMotionRules`（纯 C# 静态类，不依赖 UnityEngine） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppetMotionRules.cs:7` | 判定规则：速度与滞回、朝向死区、走路播放速率夹取；EditMode 穷举 |
| `ChibiPuppetConfig`（ScriptableObject，运行时只读） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppetConfig.cs:8` | 阈值与换算参数；资产 `Assets/_Project/Data/CharacterPuppet/ChibiPuppetConfig.asset` |

分层理由（各文件头注释有完整说明）：

- `ChibiPuppet` 与 `ChibiPuppetMotion` 分开：表现门面不该依赖驱动来源。将来改成输入 / AI 直接驱动，只换 `ChibiPuppetMotion`。
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
   │  攒满 config.SampleWindow（0.1 s）才往下
   ▼
ChibiPuppetMotionRules.Evaluate(|Δ|, window, start, stop, wasMoving) → moving, speed
ChibiPuppetMotionRules.WalkPlaybackRate(speed, perUnit, min, max) → rate（待机时固定 1）
   ▼
ChibiPuppet.SetMoving(moving, rate) → Animator Bool "Moving"、Float "Speed"
   ▼
ResolveFacing → 变了才 ChibiPuppet.SetFacing(left) → 根 localScale.x = ±原幅值
```

关键位置：

| 环节 | 位置 |
| --- | --- |
| 执行次序 `DefaultExecutionOrder(100)` | `ChibiPuppetMotion.cs:10` |
| `trackedRoot` 自动解析（父链上第一个名字不是 `Visual` 的节点，找不到用自身） | `ChibiPuppetMotion.cs:129` |
| 启用时重置采样并强制待机 | `ChibiPuppetMotion.cs:46` |
| 时停分支 | `ChibiPuppetMotion.cs:60` |
| 采样窗口判定 | `ChibiPuppetMotion.cs:81` |
| 朝向解析 | `ChibiPuppetMotion.cs:108` |
| 写 Animator 参数 | `ChibiPuppet.cs:57` |
| 翻面（保留实例整体缩放） | `ChibiPuppet.cs:48` |

### 规则细节

- **滞回**：静止时速度 ≥ `moveStartSpeed`（0.15）才起步；走动中速度 ≤ `moveStopSpeed`（0.05）才停（`ChibiPuppetMotionRules.cs:13`）。两阈值之间保持原状态，防止慢速时来回抖。
- **dt ≤ 0 视为静止**，速度记 0。
- **播放速率**：`clamp(speed × 0.53, 0.8, 2.8)`（`ChibiPuppetMotionRules.cs:41`），走快了腿摆得快，但不会快到抽搐或慢到滑步。
- **朝向死区**：沿右方向的分量绝对值 ≤ 死区时保持上次朝向（`ChibiPuppetMotionRules.cs:30`），纯纵向移动不乱翻。

### 朝向来源两种

| 来源 | 条件 | 行为 | 用在哪 |
| --- | --- | --- | --- |
| `facingSource.flipX` | 配了 `facingSource` | 直接跟随该 `SpriteRenderer` 的 `flipX`（真 = 朝左） | SampleScene 玩家 / 巡逻者：跟随被 `EncounterSceneView` 翻转的隐藏纸片 |
| 位移投影 | `facingSource` 为空 | `Dot(Δ, Camera.main.transform.right) / window` 过死区；取不到主相机时保持原朝向 | 验证场景（正交相机，无纸片） |

用速度（除以窗口时长）而不是位移过死区：帧率 / 窗口长短不改变判定（`ChibiPuppetMotion.cs:125`）。
`Camera.main` 只在首次需要时取一次并缓存，不每帧 Find。

## 为什么要 0.1 s 采样窗口

逻辑 tick 固定 60 Hz，渲染帧率可能更高（120 / 144 Hz）。角色根的位置只在逻辑 tick 推进，
于是按渲染帧看，位移是「有、无、有、无」交替的；逐帧判定会在没推进的帧读到速度 0，
触发停步阈值，走路时不停闪回待机。攒够 0.1 s 再判，窗口里必然包含若干次推进，速度稳定。
代价是起步 / 停步最多滞后约一个窗口（序列帧 Idle ⇄ Walk 无过渡），肉眼不可察。
窗口长度在 `ChibiPuppetConfig.sampleWindow`（`ChibiPuppetConfig.cs:16`），调小会重新引入闪烁。

## 时间口径

| 对象 | 用什么时间 | 理由 |
| --- | --- | --- |
| Animator | **unscaled**（预制体 `m_UpdateMode: 2` = `UnscaledTime`） | 对话时停（`Time.timeScale = 0`）期间待机动画继续播放，不定格，画面不「死」 |
| `ChibiPuppetMotion` 采样 | **scaled** `Time.deltaTime`（`ChibiPuppetMotion.cs:58`，带 `// lint-ok`） | 纯表现层，只反推动画状态，不参与逻辑推进与重放；用 scaled 恰好能识别「时停」 |

**时停强制待机**（`ChibiPuppetMotion.cs:60`）：`Time.deltaTime ≤ 0` 时写 `SetMoving(false, 1)`，
并清空采样累计、`lastPosition` 更新为当前位置。原因有二：

1. Animator 走 unscaled 时间，不写 `Moving=false` 的话会带着 Walk 继续播放，看起来像原地走；
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

整帧换 Sprite 的序列帧动画，由编辑器工具从帧目录生成；运行时组件与早期分件小人共用，**运行时代码没改**。

| 类型 | 文件 | 职责 |
| --- | --- | --- |
| `FramePuppetGenerator`（EditorWindow + 静态 `Generate(FramePuppetRequest)`） | `Assets/_Project/Scripts/Editor/CharacterPuppet/FramePuppetGenerator.cs` | 菜单 `21Days/角色/从序列帧生成小人…`；改帧贴图导入设置、写 `.anim` / `.controller` / 预制体 / 图集；可重跑、保 GUID |
| `FramePuppetRules`（静态纯规则） | `Assets/_Project/Scripts/Editor/CharacterPuppet/FramePuppetRules.cs` | `chr_<名字>_<状态>_<NN>.png` 解析、按状态分组与数值排序、缺 idle / walk 报错文案、PPU / pivot / fps 解析、画布尺寸一致性 |
| `FramePuppetMeta` / `FramePuppetRequest` | 同目录 | 可选 `meta.json` 的只读视图（缺字段用哨兵 -1）；生成参数（目标高度、fps、默认朝左、建图集） |

| 生成物 | 位置 / 约定 |
| --- | --- |
| 帧图 | `Art/Sprites/Characters/<名字>/`；Sprite Single，PPU = 画布高 / 目标高度，pivot = meta.pivot（Custom）。只由工具改，`SpriteImportProcessor` 的全局首次导入规则不动 |
| 剪辑 | `Art/Animations/Characters/<名字>/chr_<名字>_<状态>.anim`：绑定路径 `Sprite`、`SpriteRenderer.m_Sprite`，末尾补一个与末帧相同的键（长度 = 帧数 / fps），`loopTime` |
| 控制器 | 同目录 `chr_<名字>.controller`：参数 `Moving`（bool，默认 false）、`Speed`（float，默认 1）；Idle ⇄ Walk **过渡 0**（整帧换图不能混合）、无退出时间；Walk 绑 `Speed`；其它状态孤立加入 |
| 预制体 | `Prefabs/Characters/Chibi_<名字>.prefab`：根 Animator[UnscaledTime, AlwaysAnimate] + ChibiPuppet（`parts` = 唯一的 `Sprite` 渲染器）+ ChibiPuppetMotion（config 同上，`trackedRoot` / `facingSource` 空）；子物体 `Sprite`（材质 `M_SpriteDepthClip`，sortingOrder 0） |
| 图集 | 帧目录下 `<名字>.spriteatlasv2`（Sprite Packer = V2 时建；已存在不动；tight、padding 4、mipmap） |

- **默认朝右**：帧按朝右画，根 `localScale.x > 0` = 朝右。美术画成朝左时勾「默认朝左」，工具给 `Sprite` 子物体开 `flipX`，
  **不**把根缩放取负（`ChibiPuppet.Awake` 以根缩放符号当初始朝向，取负会让朝向语义反掉）。
- **NPC 接法**：纯纸片 NPC 的 `Visual` 在半身高（y 0.8、缩放 0.625），而 `CameraBillboard` 会连俯仰一起转，小人挂它下面脚底会偏。
  SampleScene 的三个 NPC 在根下另建 `PuppetVisual`（原点 + `CameraBillboard`），小人挂其下；纸片停用、`flipX` 当朝向源、`trackedRoot` 指 NPC 根。
- **占位素材**：`Art/Sprites/Characters/Ark/{amiya,chen,skadi,texas,exusiai}` 是明日方舟基建小人（`scripts/ark-spine-frames/` 渲染，版权归鹰角），
  只作开发期占位。SampleScene：玩家 amiya、巡逻 chen、`Npc_Elder` skadi、`Npc_Traveler` texas、`Npc_Villager` exusiai；目标高度 1.6（与早期分件小人实测高度 1.597 一致）。

## 测试与验证

| 类别 | 路径 | 覆盖 |
| --- | --- | --- |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/CharacterPuppet/ChibiPuppetMotionRulesTests.cs` | 无位移静止、起步阈值、走动中阈值上保持、停步阈值、dt ≤ 0、朝向死区保持与符号、播放速率夹取 |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/CharacterPuppet/FramePuppetRulesTests.cs` | 帧名解析（合法 / 各类非法）、分组与数值排序、重复序号报错、缺号与陌生文件告警、缺态文案、PPU、pivot 回退链、fps 优先级、meta 解析失败、画布尺寸不一致 |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/CharacterPuppet/CharacterPuppetShowcase.cs` | 待机 → 右走（Walk、`localScale.x > 0`）→ 左走翻面 → 停下回 Idle → 3 单位/秒走路（Animator `Speed` ≈ 1.59）→ 5 单位/秒奔跑（≈ 2.65 且高于走路）→ 停下回 Idle → 时停期间 Idle 的 normalizedTime 仍增长 |
| 验证场景 | `Assets/_Project/Scenes/Verify/CharacterPuppet.unity` | 正交相机 + 空物体 `Puppet` 下挂 `Chibi_amiya`，无 `facingSource`（走位移投影分支） |

- Showcase 不加载 Boot 场景（`LoadBootScene => false`），由协程逐帧推根节点；时停检查在 `finally` 里恢复 `timeScale = 1`。
- 跑法：`/verify-module CharacterPuppet`；规则改动先跑 `/unity-test EditMode CharacterPuppet`。
- SampleScene 冒烟：玩家出生静止为 Idle；巡逻者巡逻时 Walk、朝向随移动翻转；对话时停时回到待机。

## 已知约束

- **占位美术**：五套小人都是明日方舟基建小人渲出的占位（版权归鹰角，正式包体不得包含）；正式美术替换见 extension-guide。
- **只有待机 / 走路**：没有攻击、受击、交互动作；控制器只有两个状态、两个参数。
- **无 Spine / 骨骼形变**：整帧换图，动作细腻程度取决于帧数。
- 各角色预制体是独立资产而非 Prefab Variant；结构统一由生成工具维护，改结构改工具后重跑。
- 翻面是整张镜像：不对称的挂件镜像后会换边；「默认朝左」素材靠子物体 `Sprite.flipX`，与根 `localScale.x` 翻面叠加后仍正确。
- 朝向在 `facingSource` 模式下完全由纸片决定，`facingDeadZone` 不生效。
- 同一小人只允许一个驱动者写 `Moving` / `Speed`（目前是 `ChibiPuppetMotion`）；再加一处写参数会互相覆盖。
