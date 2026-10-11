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
| `SetFacing` | `void SetFacing(bool left)` | 转身：根 `localScale.x` 从当前值**补间**到 ±原幅值（像纸片翻面，中途经过 0），保留实例整体缩放；时长 `config.turnSeconds`（默认 0.12 s，unscaled，填 0 = 瞬间）。补间中途改向从当前缩放接着补；往同一朝向重复调不重启（`ChibiPuppet.cs:120`、`:130`） |
| `SetFacing` | `void SetFacing(bool left, bool instant)` | `instant` 为真时一帧到位——出生、读档、初始化这类不该看到翻面的时刻用（现有：驱动层启用后第一次定朝向、`BattleActor.ResetPose`） |
| `FaceTowards` | `void FaceTowards(Vector3 worldPosition)` | 按目标相对自身在主相机右方向上的投影判左右（与驱动层同一口径与死区 `config.facingDeadZone`），走补间转身，并进入**朝向保持**：小人真正移动之前驱动层不改朝向，一移动即解除。只是表现层转身，逻辑朝向不变。取不到主相机时保持当前朝向（`ChibiPuppet.cs:163`） |
| `PlayInteractPulse` | `void PlayInteractPulse()` | 程序化交互动作（占位）：`Sprite` 子物体 Y 缩放 1 → 0.9 → 1.04 → 1、下压时朝面向一侧前倾 5°，共 0.25 s（unscaled，参数在 config）；连续调用从头重播不叠加（`ChibiPuppet.cs:204`） |
| `SetMoving` | `void SetMoving(bool isMoving, bool isRunning, float playbackRate)` | 写 Animator `Moving`（bool）、`Running`（bool，`isMoving` 为假时强制 false）与 `Speed`（float，Walk / Run 播放速率）；`isMoving` 为真时解除朝向保持（`ChibiPuppet.cs:184`）。旧签名 `SetMoving(bool, float)` 已删 |
| `SetTint` | `void SetTint(Color tint)` | `parts` 里各渲染器颜色 = 预制体基底色 × tint；传 `Color.white` 还原（`ChibiPuppet.cs:218`） |
| `FaceLeft` | 只读属性 | **目标**朝向（真 = 朝左）：`SetFacing` / `FaceTowards` 调用后立刻是新值，不是补间中途的缩放 |
| `IsTurning` / `FacingHeld` / `IsPulsing` | 只读属性 | 转身补间进行中 / 朝向保持中 / 交互动作在播；驱动层读 `FacingHeld`，测试与回放读其余 |
| `Animator` / `IsMoving` / `IsRunning` | 只读属性 | 测试与调试读状态用 |
| `WalkClipSpeed` / `RunClipSpeed` / `HasRunClip` | 只读属性 | 本预制体剪辑的制作地速（单位/秒，默认 3 / 5）与有无独立 run 剪辑；由生成工具写入序列化字段，驱动层据此标定速率 |

序列化字段 `config`（`ChibiPuppetConfig`，与 `ChibiPuppetMotion.config` 同一份资产，生成工具写入）：为空时转身瞬间完成、交互动作不播、`FaceTowards` 死区按 0。

前提：`Awake` 之后才能调（基底色、原始缩放、`Sprite` 子物体的原始缩放与旋转在 `Awake` 里记下）。挂了 `ChibiPuppetMotion` 的小人，
`SetFacing` / `SetMoving` 由它每个采样窗口写一次，外部再调会被覆盖 —— 外部可以调 `SetTint`、`FaceTowards`、`PlayInteractPulse`：
`FaceTowards` 靠朝向保持不被驱动层覆盖，`PlayInteractPulse` 只动 `Sprite` 子物体的 transform（帧动画剪辑只写子物体 `SpriteRenderer.m_Sprite`，根缩放归转身），都不写 Animator 参数，不算第二个驱动者。

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
- 组件禁用再启用会重置采样并强制待机；启用后第一次定朝向立即翻面（出生 / 读档进场不出现当场翻面）。
- 门面 `FacingHeld` 为真时不改朝向；保持由门面在 `SetMoving(isMoving: true)` 时自己解除，驱动层只读。

## `Game.CharacterPuppet.ChibiPuppetMotionRules`（纯 C# 静态）

| 方法 | 说明 |
| --- | --- |
| `bool Evaluate(float deltaMagnitude, float dt, float moveStart, float moveStop, bool wasMoving, out float speed)` | 速度 = 位移 / dt；静止时 ≥ `moveStart` 起步，走动中 ≤ `moveStop` 停（滞回）；dt ≤ 0 视为静止（`ChibiPuppetMotionRules.cs:23`） |
| `bool ResolveRunning(float speed, float runStartSpeed, float runStopSpeed, bool wasRunning, bool hasRunClip)` | 走着时 ≥ `runStartSpeed` 切跑，跑着时 ≤ `runStopSpeed` 切回走（滞回）；`hasRunClip` 为假恒返回 false（`ChibiPuppetMotionRules.cs:40`） |
| `bool ResolveFacing(float deltaAlongRight, float deadZone, bool previousFaceLeft)` | 死区内保持上次朝向，否则负为朝左（`ChibiPuppetMotionRules.cs:55`） |
| `float PlaybackRate(float speed, float clipSpeed, float min, float max)` | `clamp(speed / clipSpeed, min, max)`；`clipSpeed ≤ 0` 时按 1 夹取（`ChibiPuppetMotionRules.cs:69`）。取代旧的 `WalkPlaybackRate(speed, perUnit, min, max)` |
| `bool ResolveFacingTowards(float targetOffsetAlongRight, float deadZone, bool currentFaceLeft)` | 朝向指定点：（目标 − 自身）在相机右方向的投影，口径同 `ResolveFacing`（`ChibiPuppetMotionRules.cs:84`） |
| `bool KeepFacingHold(bool held, bool moving)` | 朝向保持是否继续：`held && !moving`（`ChibiPuppetMotionRules.cs:93`） |
| `float TurnDuration(float fromScaleX, float toScaleX, float baseMagnitude, float turnSeconds)` | 转身耗时 = turnSeconds × 剩余行程 /（2 × 幅值），中途改向剩多少走多少；无行程 / 非正参数返回 0（`ChibiPuppetMotionRules.cs:102`） |
| `float TurnScaleX(float fromScaleX, float toScaleX, float progress)` | 转身进度处的根缩放，smoothstep（近似纸片转动的余弦投影），进度越界夹取（`ChibiPuppetMotionRules.cs:123`） |
| `void InteractPulse(float progress, float squash, float overshoot, out float scaleY, out float lean01)` | 交互回弹曲线：Y 缩放 1 → 1 − squash（前 30%）→ 1 + overshoot（至 65%）→ 1，前倾程度随下压升降；两端为（1, 0）（`ChibiPuppetMotionRules.cs:133`） |

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
| `turnSeconds` | 0.12 | 转身（整面翻转）补间时长，秒，unscaled；0 = 瞬间翻面 |
| `interactPulseSeconds` | 0.25 | 交互挤压回弹总时长，秒，unscaled；0 = 不播 |
| `interactSquash` / `interactOvershoot` | 0.1 / 0.04 | 回弹下压深度 / 过冲：Y 缩放最低 1 − squash、最高 1 + overshoot |
| `interactLeanDegrees` | 5 | 下压时绕脚底朝面向一侧前倾的角度；0 = 不倾斜 |

旧字段 `walkCycleSpeedPerUnit` / `walkRateMin` / `walkRateMax` 已删除；剪辑地速是每个预制体的 `ChibiPuppet.walkClipSpeed` / `runClipSpeed`，不在 config。

## 禁止事项

- **不要在别处写 Animator 参数 `Moving` / `Running` / `Speed`**，也不要直接 `Animator.Play` 切状态：唯一写入口是 `ChibiPuppet.SetMoving`，驱动者常态只有 `ChibiPuppetMotion`；例外是战斗舞台：`BattleActor` 会先禁用该小人的 `ChibiPuppetMotion` 再自己调 `SetMoving` / `SetFacing`（`Assets/_Project/Scripts/Runtime/Battle/BattleActor.cs:271-278`），同一小人任一时刻仍只有一个驱动者。多处写会互相覆盖、闪烁。
- **不要用 scaled 时间做小人动画**：Animator 必须保持 `UnscaledTime`，否则对话时停时画面定格；转身与交互补间同样走 unscaled（LitMotion `UpdateIgnoreTimeScale`）。
- **不要拿 `FaceTowards` 当逻辑朝向**：它只转表现层，隐藏纸片的 `flipX`、玩家 / 怪物模型的 `Facing` 都不变；要改逻辑朝向找对应模块。
- 不要在别处改 `Sprite` 子物体的 `localScale` / `localRotation`：交互动作以 `Awake` 时的值为基准，结束 / 停用时写回该值。
- 不要在运行时改 `ChibiPuppetConfig` 资产（所有小人预制体共用，改了全局生效且会写回磁盘）。
- 不要直接改 `parts` 里 `SpriteRenderer.color` 做染色，走 `SetTint`，否则基底色丢失。
- 不要改预制体内节点名或层级（序列帧小人的子物体 `Sprite`）：动画曲线按路径绑定，改名会静默失效。
