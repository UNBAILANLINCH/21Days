---
type: external-api
module: characterpuppet
layer: runtime
maturity: stable
---

# CharacterPuppet 外部接口

> 别的模块 / 场景要用拼接小人时查这份。内部结构见 [`characterpuppet-module-guide.md`](characterpuppet-module-guide.md)。
> 常规用法不需要写代码：把预制体实例挂到角色 `Visual` 下，小人自己看位移演动画。

## 预制体（场景实例，不走 Addressables）

| 资产路径 | 用途 |
| --- | --- |
| `Assets/_Project/Prefabs/Characters/ChibiPuppet_Player.prefab` | 玩家小人（蓝衣、棕发） |
| `Assets/_Project/Prefabs/Characters/ChibiPuppet_Patrol.prefab` | 巡逻者小人（灰衣、深灰发），结构同上 |
| `Assets/_Project/Data/CharacterPuppet/ChibiPuppetConfig.asset` | 共用驱动参数，两个预制体都引用它 |

## `Game.CharacterPuppet.ChibiPuppet`（预制体根，表现门面）

| 成员 | 签名 | 说明 |
| --- | --- | --- |
| `SetFacing` | `void SetFacing(bool left)` | 根 `localScale.x` 取 ±原幅值，保留实例整体缩放（`ChibiPuppet.cs:48`） |
| `SetMoving` | `void SetMoving(bool isMoving, float playbackRate)` | 写 Animator `Moving`（bool）与 `Speed`（float，Walk 播放速率）（`ChibiPuppet.cs:57`） |
| `SetTint` | `void SetTint(Color tint)` | 各分件颜色 = 预制体基底色 × tint；传 `Color.white` 还原（`ChibiPuppet.cs:70`） |
| `Animator` / `FaceLeft` / `IsMoving` | 只读属性 | 测试与调试读状态用 |

前提：`Awake` 之后才能调（基底色与原始缩放在 `Awake` 里记下）。挂了 `ChibiPuppetMotion` 的小人，
`SetFacing` / `SetMoving` 由它每个采样窗口写一次，外部再调会被覆盖 —— 外部只应调 `SetTint`。

## `Game.CharacterPuppet.ChibiPuppetMotion`（同根驱动组件）

| 序列化字段 | 含义 | 为空时 |
| --- | --- | --- |
| `puppet` | 同根的 `ChibiPuppet` | `Awake` 里 `GetComponent` 取 |
| `config` | `ChibiPuppetConfig` 资产 | **必填**，否则 `LateUpdate` 空引用 |
| `trackedRoot` | 读位移的角色根 | 父链上第一个名字不是 `Visual` 的节点；找不到用自身（`ChibiPuppetMotion.cs:129`） |
| `facingSource` | 朝向来源 `SpriteRenderer`，读其 `flipX`（真 = 朝左） | 按位移在 `Camera.main.transform.right` 上的投影判朝向 |

使用前提：

- 角色根的位置由别人推（移动系统 / AI / 协程），小人不改位置。
- 执行次序 100：推位置、翻纸片的脚本须在默认次序（0）或更早，本帧才读得到。
- `facingSource` 模式下纸片应 `enabled = false` 但保留 sprite，让原翻转逻辑照常写 `flipX`。
- 组件禁用再启用会重置采样并强制待机。

## `Game.CharacterPuppet.ChibiPuppetMotionRules`（纯 C# 静态）

| 方法 | 说明 |
| --- | --- |
| `bool Evaluate(float deltaMagnitude, float dt, float moveStart, float moveStop, bool wasMoving, out float speed)` | 速度 = 位移 / dt；静止时 ≥ `moveStart` 起步，走动中 ≤ `moveStop` 停（滞回）；dt ≤ 0 视为静止（`ChibiPuppetMotionRules.cs:13`） |
| `bool ResolveFacing(float deltaAlongRight, float deadZone, bool previousFaceLeft)` | 死区内保持上次朝向，否则负为朝左（`ChibiPuppetMotionRules.cs:30`） |
| `float WalkPlaybackRate(float speed, float perUnit, float min, float max)` | `clamp(speed × perUnit, min, max)`（`ChibiPuppetMotionRules.cs:41`） |

其他表现（如将来的 NPC 纸片动画）要复用同一套走 / 停判定时直接调这里，不要另写阈值。

## `Game.CharacterPuppet.ChibiPuppetConfig`（SO，运行时只读）

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| `moveStartSpeed` | 0.15 | 静止时速度（单位/秒）达到此值才切走路 |
| `moveStopSpeed` | 0.05 | 走动中速度降到此值及以下才回待机；须小于起步阈值 |
| `sampleWindow` | 0.1 | 采样窗口（秒）；逻辑 tick 慢于渲染帧时防闪回待机，别调到接近 0 |
| `facingDeadZone` | 0.01 | 沿相机右方向速度绝对值 ≤ 此值时保持朝向（仅位移投影模式） |
| `walkCycleSpeedPerUnit` | 0.8 | 走路播放速率 = 速度 × 此值 |
| `walkRateMin` / `walkRateMax` | 0.8 / 1.6 | 播放速率夹取范围 |

## 禁止事项

- **不要在别处写 Animator 参数 `Moving` / `Speed`**，也不要直接 `Animator.Play` 切状态：唯一写入口是 `ChibiPuppet.SetMoving`，驱动者只有 `ChibiPuppetMotion`，多处写会互相覆盖、闪烁。
- **不要用 scaled 时间做小人动画**：Animator 必须保持 `UnscaledTime`，否则对话时停时画面定格。
- 不要在运行时改 `ChibiPuppetConfig` 资产（两个预制体共用，改了全局生效且会写回磁盘）。
- 不要直接改分件 `SpriteRenderer.color` 做染色，走 `SetTint`，否则基底色丢失。
- 不要改预制体内节点名或层级：动画曲线按路径绑定，改名会静默失效。
