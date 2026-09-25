# PRP: 拼接小人（明日方舟风格 Q 版角色的待机 / 走路表现）

> 日期：2026-09-25。用户诉求：把示例角色换成「像明日方舟角色小人」的简单 Live2D 风格小人，看运动表现，带移动与待机动画，简单即可。
> 决定：不引入 Spine / Live2D / 2D Animation 包，用 Unity 自带的分件 Sprite + Animator 做**拼接小人**；占位美术，以后换件不改动画与驱动。

## 1. 范围

- 做：`Runtime/CharacterPuppet/` 模块（`Game.CharacterPuppet`）；7 张占位分件图；待机 / 走路两套 Animator 动画；小人预制体（玩家 / 巡逻者两个变体）；SampleScene 玩家与巡逻者换成小人；验证场景与 Showcase；EditMode 测试；模块文档。
- 不做：真骨骼形变、攻击 / 受击动画、换装系统、Spine 接入（留接口：驱动层只依赖 `Animator` 参数与朝向）。

## 2. 结构

```text
ChibiPuppet（预制体根；ChibiPuppet + ChibiPuppetMotion + Animator）
└─ Body（上下起伏的节点）
   ├─ LegL / LegR（pivot 在髋，绕 z 摆）
   ├─ Torso（pivot 在底，呼吸时 y 缩放）
   │  ├─ Head（pivot 在颈，轻微点头 / 倾斜）
   │  │  └─ Hair（随头）
   │  ├─ ArmL / ArmR（pivot 在肩，绕 z 摆，与腿反相）
```

| 类型 | 职责 |
| --- | --- |
| `ChibiPuppet`（MonoBehaviour） | 持有各分件 `SpriteRenderer` 与 `Animator`；`SetFacing(bool faceLeft)` 用根 `localScale.x = ±1`；`SetMoving(bool, float speed)` 写 Animator 参数 `Moving`（bool）、`Speed`（float，驱动走路播放速率）；`SetTint(Color)`（可选，整体染色） |
| `ChibiPuppetMotion`（MonoBehaviour，同根） | 每帧 `LateUpdate` 读**父级角色根**（`transform.parent` 链上第一个非 Visual 的根，Inspector 可指定 `trackedRoot`）的世界位置差 → `ChibiPuppetMotionRules.Evaluate` → `SetMoving`；朝向优先读 `facingSource`（`SpriteRenderer.flipX`，即探索场景里被 `EncounterSceneView` 翻转的隐藏纸片），没配则按位移在 `Camera.main.transform.right` 上的投影符号 |
| `ChibiPuppetMotionRules`（纯 C#） | `Evaluate(delta, dt, config, wasMoving) → (speed, moving)`：速度 = 位移 / dt；进入移动阈值 `moveStartSpeed`（0.15）、退出阈值 `moveStopSpeed`（0.05）做滞回；`dt <= 0` 视为静止；`ResolveFacing(deltaAlongRight, deadZone, previousFaceLeft)` |
| `ChibiPuppetConfig`（SO） | `moveStartSpeed`、`moveStopSpeed`、`facingDeadZone`（0.01）、`walkCycleSpeedPerUnit`（走路动画速率 = clamp(speed × 此值, 0.8, 1.6)） |

- 时间：Animator 走 unscaled 时间（`updateMode = UnscaledTime`），对话时停期间保持待机呼吸；驱动层在 `Time.deltaTime`（scaled）≤ 0 时强制待机。
- `EncounterSceneView` 不改：`player/Visual` 的原 `SpriteRenderer` 保留 sprite 但 `enabled = false`，继续做 `EnsureSprite` 与 `flipX` 的载体；小人挂在 `Visual` 下（随 `CameraBillboard` 朝向相机），`facingSource` 指向该隐藏纸片。巡逻者同构。状态色仍染 `SelectRing`，小人不参与。
- 分件材质用 `Art/Materials/Character/M_SpriteDepthClip`（与现有纸片一致，可被灰盒遮挡）；sortingLayer 同现有纸片；order：腿 0、躯干 1、臂 2、头 3、发 4。

## 3. 占位美术（`Art/Sprites/Characters/Puppet/`，PPU 100，整体高 1.6 世界单位）

7 张 PNG，白底可染色 + 深色描边与五官：`Puppet_Head.png`（56×48，pivot 底中）、`Puppet_Hair.png`（60×34，pivot 底中）、`Puppet_Torso.png`（34×40，pivot 底中）、`Puppet_Arm.png`（12×34，pivot 顶中；左右共用，右臂 x 镜像）、`Puppet_Leg.png`（14×34，pivot 顶中，共用）。玩家变体染色：发 (0.45,0.28,0.16)、肤 (1,0.87,0.77)、衣 (0.2,0.55,1)、裤 (0.2,0.22,0.35)；巡逻者变体：衣 (0.55,0.55,0.55)、发 (0.25,0.25,0.25)。

## 4. 动画（`Art/Animations/`，命名按目录 README）

- `chr_chibi_idle.anim`（2.0 s 循环）：Body y ±0.02 呼吸；Torso scaleY 1 → 1.03 → 1；Head 旋转 z ±2°；双臂 z ±3°（反相）。
- `chr_chibi_walk.anim`（0.6 s 循环）：LegL/LegR z ±28°（反相）；ArmL/ArmR z ±22°（与同侧腿反相）；Body y 两次 0.035 起伏（每步一次）；Torso 前倾 z 4°（面向右时）；Head z ±3°。
- `chr_chibi_puppet.controller`：参数 `Moving`(bool)、`Speed`(float)；状态 Idle（默认）⇄ Walk，过渡 0.12 s、无退出时间；Walk 状态 speed 乘数绑定 `Speed` 参数。
- 用 `execute_code`（`UnityEditor.Animations.AnimatorController`、`AnimationClip.SetCurve`）生成，不手写 YAML。

## 5. 预制体与场景

- `Prefabs/Characters/ChibiPuppet_Player.prefab`、`ChibiPuppet_Patrol.prefab`（同一结构、不同染色；`ChibiPuppetConfig` 资产 `Data/CharacterPuppet/ChibiPuppetConfig.asset`）。
- SampleScene：`player/Visual` 下实例化 `ChibiPuppet_Player`（localPosition 0，z 略靠前 -0.01 避免与隐藏纸片同面），`facingSource` = `player/Visual` 的 SpriteRenderer，该 SpriteRenderer `enabled=false`；`enerme/Visual` 同理用 Patrol 变体。`BlobShadow / SelectRing / NameTag` 不动。
- 验证场景 `Scenes/Verify/CharacterPuppet.unity`（2D 正交）：一个 `Puppet` 根（空物体）下挂 `ChibiPuppet_Player`，无 facingSource。

## 6. 验证

- EditMode `Tests/EditMode/CharacterPuppet/ChibiPuppetMotionRulesTests.cs`：静止 / 起步阈值 / 滞回停止 / dt=0 / 朝向死区与保持。
- Showcase `Tests/Showcase/CharacterPuppet/CharacterPuppetShowcase.cs`（`LoadBootScene=false`）：① 待机 2 s（Check Animator 当前状态为 Idle）→ 截图；② 以 1.5 单位/秒向右移动根 2 s（Check 状态 Walk、`localScale.x > 0`）→ 截图；③ 向左移动（Check `localScale.x < 0`）→ 截图；④ 停下（Check 回到 Idle）。
- SampleScene Play 冒烟：进遭遇后玩家出生静止为 Idle；巡逻者巡逻时 Walk 且朝向随移动翻转；对话时停时动画定格。

## 7. 收尾

`/generate-doc characterpuppet`（seed 即可）、`modules.json` 与 catalog 登记；isometricexploration guide 的角色层级一节补一句「Visual 的纸片隐藏、小人挂其下」。
