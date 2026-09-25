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
| 明日方舟风格 Q 版「拼接小人」的**待机 / 走路**表现：分件 Sprite + Animator | 真骨骼形变（无 2D Animation / Spine / Live2D 包） |
| 从角色根的**位移**反推走 / 停与播放速率，写 Animator 参数 | 攻击 / 受击 / 死亡等其他动作 |
| 朝向：读外部纸片的 `flipX`，或按位移在相机右方向的投影 | 读输入、改角色位置、参与任何玩法判定 |
| 整体染色（`SetTint`，以预制体基底色相乘） | 换装系统、状态色（状态色仍染 `SelectRing`） |

一句话：**小人只是「看位移演动画」的皮**。谁推动了角色根、为什么推，它一概不知。

## 类分工

| 类型 | 文件 | 职责 |
| --- | --- | --- |
| `ChibiPuppet`（MonoBehaviour，预制体根，`DisallowMultipleComponent`） | `Assets/_Project/Scripts/Runtime/CharacterPuppet/ChibiPuppet.cs:10` | 表现门面：持有 `Animator` 与分件 `SpriteRenderer[] parts`；唯一写 Animator 参数与根 `localScale.x` 的地方 |
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

## 预制体层级

`Assets/_Project/Prefabs/Characters/ChibiPuppet_Player.prefab`、`ChibiPuppet_Patrol.prefab`：两个独立预制体，结构相同，只差分件染色。

```text
ChibiPuppet_*（Animator[chr_chibi_puppet, UnscaledTime] + ChibiPuppet + ChibiPuppetMotion；脚底为原点，整体高 ≈ 1.6）
└─ Body（localScale 1.32；动画只动 localPosition.y）
   ├─ LegL / LegR（(∓0.07, 0.34, 0)）
   └─ Torso（(0, 0.30, -0.002)）
      ├─ Head（(0, 0.37, -0.004)）
      │  └─ Hair（(0, 0.20, -0.002)）
      └─ ArmL / ArmR（(∓0.19, 0.34, -0.002)；ArmR 用 flipX 镜像共用臂图）
```

| 分件 | 图 | pivot（sprite alignment） | sortingOrder |
| --- | --- | --- | --- |
| LegL / LegR | `Puppet_Leg.png` | 顶中（=髋） | 0 |
| Torso | `Puppet_Torso.png` | 底中（=腰） | 1 |
| ArmL / ArmR | `Puppet_Arm.png` | 顶中（=肩） | 2 |
| Head | `Puppet_Head.png` | 底中（=颈） | 3 |
| Hair | `Puppet_Hair.png` | 底中（随头） | 4 |

- 分件图在 `Assets/_Project/Art/Sprites/Characters/Puppet/`：白底 + 深色描边，PPU 100，靠 `SpriteRenderer.color` 染色。
- 材质 `Art/Materials/Character/M_SpriteDepthClip`（与原纸片一致，可被灰盒遮挡），sortingLayer `Default`。
- 子节点逐级 0.002 的局部 z 前移：深度写入的材质下，仅靠 sortingOrder 不够保证同平面前后次序，z 偏移兜底。
- 分件图须**左右对称**（五官居中）：翻面只改小人根 `localScale.x`，不对称会穿帮。
- `parts` 数组在预制体里挂全 7 个 `SpriteRenderer`，`SetTint` 只作用于数组里的分件。

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
- **播放速率**：`clamp(speed × 0.8, 0.8, 1.6)`（`ChibiPuppetMotionRules.cs:41`），走快了腿摆得快，但不会快到抽搐或慢到滑步。
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
代价是起步 / 停步最多滞后约一个窗口，叠加 0.12 s 过渡，肉眼不可察。
窗口长度在 `ChibiPuppetConfig.sampleWindow`（`ChibiPuppetConfig.cs:16`），调小会重新引入闪烁。

## 时间口径

| 对象 | 用什么时间 | 理由 |
| --- | --- | --- |
| Animator | **unscaled**（预制体 `m_UpdateMode: 2` = `UnscaledTime`） | 对话时停（`Time.timeScale = 0`）期间待机呼吸继续播放，不定格，画面不「死」 |
| `ChibiPuppetMotion` 采样 | **scaled** `Time.deltaTime`（`ChibiPuppetMotion.cs:58`，带 `// lint-ok`） | 纯表现层，只反推动画状态，不参与逻辑推进与重放；用 scaled 恰好能识别「时停」 |

**时停强制待机**（`ChibiPuppetMotion.cs:60`）：`Time.deltaTime ≤ 0` 时写 `SetMoving(false, 1)`，
并清空采样累计、`lastPosition` 更新为当前位置。原因有二：

1. Animator 走 unscaled 时间，不写 `Moving=false` 的话会带着 Walk 继续播放，看起来像原地走；
2. 不清空累计，恢复后会把时停期间（若有传送等）的位移一次性计入速度，误判起步。

恢复后按原逻辑重新采样，最多约一个窗口 + 过渡回到走路。

`lint-ok` 的边界：只允许在「从既有位移反推表现」的场合用 scaled `Time.deltaTime`。
一旦小人要**产生**位移或影响逻辑，必须改走逻辑 tick，不能照抄这一行。

## 动画资产

| 资产 | 内容 |
| --- | --- |
| `Assets/_Project/Art/Animations/chr_chibi_idle.anim` | 2.0 s 循环：Body y 呼吸、Torso scaleY、Head z 轻摆、双臂 z 反相轻摆 |
| `Assets/_Project/Art/Animations/chr_chibi_walk.anim` | 0.6 s 循环：双腿 z 反相大摆、双臂与同侧腿反相、Body y 每步一次起伏、Torso 前倾、Head 轻摆 |
| `Assets/_Project/Art/Animations/chr_chibi_puppet.controller` | 参数 `Moving`（bool，默认 false）、`Speed`（float，默认 1）；Idle（默认）⇄ Walk，过渡 0.12 s、无退出时间；Walk 的速度乘数绑 `Speed`，Idle 不绑 |

- 曲线绑定路径是**相对小人根的层级路径**：`Body`、`Body/LegL`、`Body/LegR`、`Body/Torso`、`Body/Torso/Head`、
  `Body/Torso/ArmL`、`Body/Torso/ArmR`。改节点名或层级会让曲线静默失效（不报错，只是不动）。
- 两段剪辑覆盖同一组属性，切状态时不残留上一段的值。
- 动画与控制器用 Unity MCP `execute_code`（`AnimatorController` / `AnimationClip.SetCurve`）生成，不手写 YAML。
- 命名遵循 `Art/Animations/README.md`。

## 与 EncounterSceneView 的关系

`EncounterSceneView`（`Assets/_Project/Scripts/Runtime/Monster/EncounterSceneView.cs:9`）**没改**，小人是在它之外叠上去的：

- SampleScene 里 `player/Visual`、`enerme/Visual` 的纸片 `SpriteRenderer` 设为 `enabled = false`，sprite 保留，
  仍是 `EnsureSprite`（`EncounterSceneView.cs:136`）与 `ApplyFlip` 写 `flipX`（`EncounterSceneView.cs:255`）的载体。
- 小人实例挂在 `Visual` 下（localPosition `(0, 0, -0.01)`，略靠前避免与隐藏纸片同面），随 `CameraBillboard` 朝向相机。
- `ChibiPuppetMotion.facingSource` 指向该隐藏纸片；`trackedRoot` 留空，自动解析到 `player` / `enerme` 根。
- 执行次序 100 排在 `EncounterSceneView`（默认 0）之后，读到的是本帧已投影的位置与已翻好的 `flipX`。
- 状态色仍染 `SelectRing`，小人不参与；`BlobShadow` / `NameTag` 不动。

## 测试与验证

| 类别 | 路径 | 覆盖 |
| --- | --- | --- |
| EditMode | `Assets/_Project/Scripts/Tests/EditMode/CharacterPuppet/ChibiPuppetMotionRulesTests.cs` | 无位移静止、起步阈值、走动中阈值上保持、停步阈值、dt ≤ 0、朝向死区保持与符号、播放速率夹取 |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/CharacterPuppet/CharacterPuppetShowcase.cs` | 待机 → 右走（Walk、`localScale.x > 0`）→ 左走翻面 → 停下回 Idle → 时停期间 Idle 的 normalizedTime 仍增长 |
| 验证场景 | `Assets/_Project/Scenes/Verify/CharacterPuppet.unity` | 正交相机 + 空物体 `Puppet` 下挂 `ChibiPuppet_Player`，无 `facingSource`（走位移投影分支） |

- Showcase 不加载 Boot 场景（`LoadBootScene => false`），由协程逐帧推根节点；时停检查在 `finally` 里恢复 `timeScale = 1`。
- 跑法：`/verify-module CharacterPuppet`；规则改动先跑 `/unity-test EditMode CharacterPuppet`。
- SampleScene 冒烟：玩家出生静止为 Idle；巡逻者巡逻时 Walk、朝向随移动翻转；对话时停时回到待机呼吸。

## 已知约束

- **占位美术**：7 张分件图是程序画的白底描边占位，染色靠 `SpriteRenderer.color`；正式美术替换见 extension-guide。
- **只有待机 / 走路**：没有攻击、受击、交互动作；控制器只有两个状态、两个参数。
- **无 Spine / 骨骼形变**：分件刚体旋转，关节处靠 pivot 与遮挡掩盖接缝。
- 两个预制体是独立资产而非 Prefab Variant，改结构要两个都改。
- 分件必须左右对称；`ArmR` 的镜像靠 `flipX`，与根的 `localScale.x` 翻面叠加后仍正确。
- 朝向在 `facingSource` 模式下完全由纸片决定，`facingDeadZone` 不生效。
- 同一小人只允许一个驱动者写 `Moving` / `Speed`（目前是 `ChibiPuppetMotion`）；再加一处写参数会互相覆盖。
