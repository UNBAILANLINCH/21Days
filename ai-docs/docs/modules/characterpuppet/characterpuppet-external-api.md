---
type: external-api
module: characterpuppet
layer: runtime
maturity: stable
---

# CharacterPuppet 外部接口

> 别的模块 / 场景要用小人时查这份。内部结构见 [`characterpuppet-module-guide.md`](characterpuppet-module-guide.md)。
> 常规用法不需要写代码：把预制体实例挂到角色 `Visual` 下，小人自己看位移演动画。

## 预制体（场景实例，不走 Addressables）

| 资产路径 | 用途 |
| --- | --- |
| `Assets/_Project/Prefabs/Characters/Chibi_<名字>.prefab` | 序列帧小人，由 `FramePuppetGenerator` 生成；现有 `amiya`（玩家）、`chen`（巡逻者）、`skadi` / `texas` / `exusiai`（NPC），均为占位 |
| `Assets/_Project/Data/CharacterPuppet/ChibiPuppetConfig.asset` | 共用驱动参数，所有小人预制体都引用它 |

## `Game.CharacterPuppet.ChibiPuppet`（预制体根，表现门面）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `SetFacing` | `void SetFacing(bool left)` | 根 `localScale.x` 取 ±原幅值，保留实例整体缩放（`ChibiPuppet.cs:61`） |
| `SetMoving` | `void SetMoving(bool isMoving, bool isRunning, float playbackRate)` | 写 Animator `Moving`（bool）、`Running`（bool，`isMoving` 为假时强制 false）与 `Speed`（float，Walk / Run 播放速率）（`ChibiPuppet.cs:73`）。旧签名 `SetMoving(bool, float)` 已删 |
| `SetTint` | `void SetTint(Color tint)` | `parts` 里各渲染器颜色 = 预制体基底色 × tint；传 `Color.white` 还原（`ChibiPuppet.cs:88`） |
| `Animator` / `FaceLeft` / `IsMoving` / `IsRunning` | 只读属性 | 测试与调试读状态用 |
| `WalkClipSpeed` / `RunClipSpeed` / `HasRunClip` | 只读属性 | 本预制体剪辑的制作地速（单位/秒，默认 3 / 5）与有无独立 run 剪辑；由生成工具写入序列化字段，驱动层据此标定速率 |

前提：`Awake` 之后才能调（基底色与原始缩放在 `Awake` 里记下）。挂了 `ChibiPuppetMotion` 的小人，
`SetFacing` / `SetMoving` 由它每个采样窗口写一次，外部再调会被覆盖 —— 外部只应调 `SetTint`。

## `Game.CharacterPuppet.ChibiPuppetMotion`（同根驱动组件）

| 序列化字段 | 含义 | 为空时 |
| --- | --- | --- |
| `puppet` | 同根的 `ChibiPuppet` | `Awake` 里 `GetComponent` 取 |
| `config` | `ChibiPuppetConfig` 资产 | **必填**，否则 `LateUpdate` 空引用 |
| `trackedRoot` | 读位移的角色根 | 父链上第一个名字不是 `Visual` 的节点；找不到用自身（`ChibiPuppetMotion.cs:136`） |
| `facingSource` | 朝向来源 `SpriteRenderer`，读其 `flipX`（真 = 朝左） | 按位移在 `Camera.main.transform.right` 上的投影判朝向 |

使用前提：

- 角色根的位置由别人推（移动系统 / AI / 协程），小人不改位置；走 / 跑也只按位移速度判，推动者不用告诉它「现在在跑」。
- 执行次序 100：推位置、翻纸片的脚本须在默认次序（0）或更早，本帧才读得到。
- `facingSource` 模式下纸片应 `enabled = false`（sprite 可为空），让原翻转逻辑照常写 `flipX`。
- 组件禁用再启用会重置采样并强制待机。

## `Game.CharacterPuppet.ChibiPuppetMotionRules`（纯 C# 静态）

| 方法 | 说明 |
| --- | --- |
| `bool Evaluate(float deltaMagnitude, float dt, float moveStart, float moveStop, bool wasMoving, out float speed)` | 速度 = 位移 / dt；静止时 ≥ `moveStart` 起步，走动中 ≤ `moveStop` 停（滞回）；dt ≤ 0 视为静止（`ChibiPuppetMotionRules.cs:13`） |
| `bool ResolveRunning(float speed, float runStartSpeed, float runStopSpeed, bool wasRunning, bool hasRunClip)` | 走着时 ≥ `runStartSpeed` 切跑，跑着时 ≤ `runStopSpeed` 切回走（滞回）；`hasRunClip` 为假恒返回 false（`ChibiPuppetMotionRules.cs:30`） |
| `bool ResolveFacing(float deltaAlongRight, float deadZone, bool previousFaceLeft)` | 死区内保持上次朝向，否则负为朝左（`ChibiPuppetMotionRules.cs:45`） |
| `float PlaybackRate(float speed, float clipSpeed, float min, float max)` | `clamp(speed / clipSpeed, min, max)`；`clipSpeed ≤ 0` 时按 1 夹取（`ChibiPuppetMotionRules.cs:59`）。取代旧的 `WalkPlaybackRate(speed, perUnit, min, max)` |

其他表现（如将来的 NPC 纸片动画）要复用同一套走 / 停 / 跑判定时直接调这里，不要另写阈值。

## `Game.CharacterPuppet.ChibiPuppetConfig`（SO，运行时只读）

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| `moveStartSpeed` | 0.15 | 静止时速度（单位/秒）达到此值才切走路 |
| `moveStopSpeed` | 0.05 | 走动中速度降到此值及以下才回待机；须小于起步阈值 |
| `runStartSpeed` | 4.0 | 走路中速度达到此值切奔跑（仅有 run 剪辑的小人）；落在玩家走 3 与跑 5 之间 |
| `runStopSpeed` | 3.5 | 奔跑中速度降到此值及以下切回走路；须小于切跑阈值 |
| `sampleWindow` | 0.05 | 采样窗口（秒）；视图已做 tick 间插值，只抹平单帧噪声。驱动源若只在 tick 推进、无插值，须 ≥ 两次 tick |
| `facingDeadZone` | 0.01 | 沿相机右方向速度绝对值 ≤ 此值时保持朝向（仅位移投影模式） |
| `rateMin` / `rateMax` | 0.8 / 1.6 | 播放速率 = 速度 / 剪辑地速后的夹取范围；上限防「快放」 |

旧字段 `walkCycleSpeedPerUnit` / `walkRateMin` / `walkRateMax` 已删除；剪辑地速是每个预制体的 `ChibiPuppet.walkClipSpeed` / `runClipSpeed`，不在 config。

## 禁止事项

- **不要在别处写 Animator 参数 `Moving` / `Running` / `Speed`**，也不要直接 `Animator.Play` 切状态：唯一写入口是 `ChibiPuppet.SetMoving`，驱动者只有 `ChibiPuppetMotion`，多处写会互相覆盖、闪烁。
- **不要用 scaled 时间做小人动画**：Animator 必须保持 `UnscaledTime`，否则对话时停时画面定格。
- 不要在运行时改 `ChibiPuppetConfig` 资产（所有小人预制体共用，改了全局生效且会写回磁盘）。
- 不要直接改 `parts` 里 `SpriteRenderer.color` 做染色，走 `SetTint`，否则基底色丢失。
- 不要改预制体内节点名或层级（序列帧小人的子物体 `Sprite`）：动画曲线按路径绑定，改名会静默失效。
