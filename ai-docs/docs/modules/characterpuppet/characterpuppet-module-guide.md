---
type: module-guide
module: characterpuppet
layer: runtime
maturity: seed
---

# CharacterPuppet 模块指南

> 改 `Assets/_Project/Scripts/Runtime/CharacterPuppet/` 之前读这份。设计见
> [`PRP/character-puppet/prp.md`](../../../../PRP/character-puppet/prp.md)；与源码不符时以源码为准。
> 当前为 seed：只有本 guide，external-api / extension-guide 待 `/generate-doc characterpuppet` 补齐。

## 职责边界

**做**：明日方舟风格 Q 版「拼接小人」的待机 / 走路表现——分件 Sprite + Animator，
从角色根的位移反推走 / 停与朝向，写 Animator 参数。
**不做**：真骨骼形变、攻击 / 受击动画、换装系统、Spine 接入；不参与玩法判定、不读输入、不改位置。

## 组成

| 类型 | 职责 |
| --- | --- |
| `ChibiPuppet`（MonoBehaviour，预制体根） | 表现门面：持有 `Animator` 与 7 个分件 `SpriteRenderer`；`SetFacing(bool faceLeft)` 写根 `localScale.x = ±原幅值`；`SetMoving(bool, float playbackRate)` 写 `Moving` / `Speed`；`SetTint(Color)` 以预制体颜色为基底整体相乘 |
| `ChibiPuppetMotion`（MonoBehaviour，同根，`DefaultExecutionOrder(100)`） | `LateUpdate` 读 `trackedRoot` 位移，攒满 `sampleWindow` 后调规则判走 / 停、算播放速率；朝向优先读 `facingSource.flipX`，没配按位移在 `Camera.main.transform.right` 上的投影 |
| `ChibiPuppetMotionRules`（纯 C#） | `Evaluate`（速度 = 位移 / dt，起步 / 停步滞回，dt ≤ 0 视为静止）、`ResolveFacing`（死区内保持）、`WalkPlaybackRate`（clamp） |
| `ChibiPuppetConfig`（SO，`Data/CharacterPuppet/ChibiPuppetConfig.asset`） | `moveStartSpeed 0.15`、`moveStopSpeed 0.05`、`sampleWindow 0.1`、`facingDeadZone 0.01`、`walkCycleSpeedPerUnit 0.8`、`walkRateMin/Max 0.8/1.6` |

## 预制体层级

`Prefabs/Characters/ChibiPuppet_Player.prefab`、`ChibiPuppet_Patrol.prefab`（两个独立预制体，只差染色）：

```text
ChibiPuppet_*（Animator[chr_chibi_puppet] + ChibiPuppet + ChibiPuppetMotion，脚底为原点，整体高 ≈ 1.6）
└─ Body（localScale 1.32，动画只动 localPosition.y）
   ├─ LegL / LegR（(∓0.07, 0.34)，pivot 顶中=髋，order 0）
   └─ Torso（(0, 0.30)，pivot 底中，order 1）
      ├─ Head（(0, 0.37)，pivot 底中=颈，order 3）
      │  └─ Hair（(0, 0.20)，order 4）
      └─ ArmL / ArmR（(∓0.19, 0.34)，pivot 顶中=肩，order 2；ArmR flipX）
```

分件图在 `Art/Sprites/Characters/Puppet/Puppet_{Head,Hair,Torso,Arm,Leg}.png`（白底 + 深色描边，PPU 100，
靠 `SpriteRenderer.color` 染色）；材质 `Art/Materials/Character/M_SpriteDepthClip`（可被灰盒遮挡），sortingLayer `Default`。
子节点间有 0.002 的局部 z 前移，保证同平面深度写入时前后次序稳定。

## 动画

`Art/Animations/chr_chibi_idle.anim`（2.0 s 循环）、`chr_chibi_walk.anim`（0.6 s 循环）、`chr_chibi_puppet.controller`
（参数 `Moving` bool、`Speed` float 默认 1；Idle ⇄ Walk 过渡 0.12 s、无退出时间；Walk 的速度乘数绑 `Speed`）。
曲线路径按上面层级（`Body`、`Body/LegL`、`Body/Torso/ArmL` …），两段剪辑都覆盖同一组属性，切状态不留残值。
Animator 走 unscaled 时间（`updateMode = UnscaledTime`）：对话时停（`timeScale = 0`）时待机呼吸继续播放，不定格；
驱动层 `ChibiPuppetMotion` 在 `Time.deltaTime`（scaled）≤ 0 时强制切回待机（`SetMoving(false, ...)`）并清空采样累计，
恢复后按原逻辑重新采样（过渡 0.12 s，最多约 0.1 s 回到走路）。

## 驱动方式与接线

- 预制体挂在角色的 `Visual` 下；`trackedRoot` 为空时自动取父链上第一个名字不是 `Visual` 的节点（即角色根）。
- 逻辑 tick（60 Hz）可能慢于渲染帧率，单帧位移时有时无，所以按 `sampleWindow` 攒位移再判，避免走路时闪回待机。
- 翻面只改小人根的 `localScale.x`，分件图须左右对称（五官居中），翻面不穿帮。

## 与 EncounterSceneView 的关系

`EncounterSceneView` 不改：SampleScene 里 `player/Visual`、`enerme/Visual` 的纸片 `SpriteRenderer` 设为 `enabled = false`
但 sprite 保留，仍是 `EnsureSprite` 与 `flipX` 的载体；小人实例（localPosition `(0, 0, -0.01)`）挂在 `Visual` 下随 `CameraBillboard`
朝向相机，`ChibiPuppetMotion.facingSource` 指向该隐藏纸片。状态色仍染 `SelectRing`，小人不参与。

## 换件 / 换 Spine

- **换件**：替换 `Puppet_*.png`（保持 pivot 语义：头 / 发 / 躯干底中，臂 / 腿顶中），必要时调预制体分件局部位置；动画与驱动不动。
- **换 Spine**：驱动层只依赖 `Moving` / `Speed` 两个参数与根 `localScale.x` 朝向。新表现实现同样的 `SetMoving` / `SetFacing`
  口子（或让 Spine 端的 Animator / 状态机认这两个参数），`ChibiPuppetMotion` 与 `ChibiPuppetMotionRules` 复用。

## 验证

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/CharacterPuppet/ChibiPuppetMotionRulesTests.cs`。
- Showcase：`Assets/_Project/Scripts/Tests/Showcase/CharacterPuppet/CharacterPuppetShowcase.cs`
  （待机 → 右走 → 左走翻面 → 停下），验证场景 `Assets/_Project/Scenes/Verify/CharacterPuppet.unity`
  （正交相机 + 空物体 `Puppet` 下挂 `ChibiPuppet_Player`，无 facingSource）。
