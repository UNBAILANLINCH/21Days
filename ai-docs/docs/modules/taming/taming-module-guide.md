---
type: module-guide
module: taming
layer: runtime
maturity: seed
---

# Taming 模块指南

## 五店主场景角色分类（2026-10-13）

静态作者配置：Passive维持待机/巡逻但不察觉/伤害；ObservationOnly保留感知与追近，但双方不结算探索伤害，当前只用于酒肆BOSS与两个观测客人；原巡逻默认false，仍按原战斗规则。AddedForShops仅用于旧档允许缺新增角色，不替代伤害分类。
ConfigureActor新增末尾可选观测参数，旧调用点行为不变；这些分类不进动态存档。主场景受控小人沿用EncounterSceneView现有扫掠碰撞，控制、名字与交互锚点继续共用。

正式入口已接 Boot → 标题「开始」→ Addressables IsometricEncounter（SampleScene）。原巡逻者与复制实例使用相同美术/组件，独立路线与身份 `patrol-a`、`patrol-b`，显示名为巡逻者甲、巡逻者乙，玩家为 `player`。
按 T 直接驯服最近巡逻者并接管，WASD 移动，相机跟随；再次 T 返回玩家。左下控制区可定向驯服，已驯服后可 A/B 往返选择。未驯服角色的切换按钮禁用。
仍不要求伪装、距离、残血、道具或进度。
死亡对象不能控制；控制期间目标死亡会返回玩家。

| 类型 | 职责 |
| --- | --- |
| `TamingActor` | 场景里**一只巡逻者的作者数据**（2026-10-14 扩了两个字段，见下）：稳定 id、显示名、纸片与指示环、巡逻点，外加 `kindId` 与 `fullParticipant` |
| `TamingIntent` | 移动轴与切换按钮 |
| `TamingRules` | 驯服归属、按键边缘、控制切换、双方移动路由 |
| `TamingSceneController` | 独立场景初始化、Action 输入与相机接管 |
| `MonsterRules.MoveControlled`（复用扩展） | 受控敌人的纯逻辑移动 |
| `SmoothCameraFollow.SetTarget`（复用扩展） | 保留镜头偏移，更换跟随对象 |

## 一场遭遇多种怪（2026-10-14，PRP/combat-iteration D2）

`TamingActor` 上多了两个序列化字段，**默认值都等于加字段之前的行为**，所以巡逻者乙一个字没改：

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| `kindId` | `0` | `0` = 沿用主怪种类（现状）；非 0 = 这一只按 `monster_species` 表里那一行取数值、可否击杀与掉落 |
| `fullParticipant` | `false` | `false` = 只巡逻 / 感知 / 攻击的附加巡逻者（现状）；`true` = **完整参与者** |

**只有完整参与者**会被背后处决、死亡掉落（掉落闸门按 id 各一次）、产生露馅信号与被露馅惊动、触发被抓口径、参与升级回合制；
**整场遭遇的胜负仍只认主怪**（`ExecutionResolver` 只在目标是主怪时 `SettleBattle(Victory)`）。
`EncounterSceneView` 把 `patrolActors` 的两个数组经 `ConfigureTaming` 传进 `EncounterStep`，`MonsterRules.CreateForActor(stableId, kindIdOverride)` 按覆盖的种类造附加怪。
存档与回放快照按稳定 id 对齐（不再要求数量一致），见 [`monster-module-guide.md`](../monster/monster-module-guide.md)「回放状态」。

SampleScene 现状：主怪（市令，1003）+ 巡逻者乙（两字段默认，**零改动**）+ 巡夜人（`patrol_xunyeren`，`kindId = 1001`、`fullParticipant = true`，东南角独立路线）。

规则依赖 Player/Monster 公开 API 和 Core 遥测接口，不依赖 Disguise。
切换、死亡返回及拒绝分支记录遥测；移动不记录每帧日志。独立场景使用 NullTelemetryScope。
已驯服敌人不再推进敌对 AI，返回玩家后保持驯服并原地待命。
移动速度复用 MonsterConfig.PatrolSpeed，玩家速度复用 PlayerConfig.MoveSpeed。
状态由正式 EncounterStep 持有，存档快照通过既有 Session 分区恢复，不写 SO。

## 试玩入口

正式试玩从 `Assets/_Project/Scenes/Boot.unity` 运行并点「开始」。SampleScene 直接 Play 仍可由 StandaloneEncounterController 驱动同一套规则。
`Encounter/TamingDemo` 是旧单敌演示挂点，保持不激活；正式与直接播放流程都不需要启用它，不要让它与既有调度器同时推进。
`TamingDemo` 已接好视图、输入资产、双方配置和 SmoothCameraFollow 五个引用。
视图持有 player/enerme、出生点和巡逻点；采用 XY 平面，挂在 SampleScene 的 `Encounter/TamingDemo` 子物体上（默认不激活）。
TamingSceneController 是唯一推进者，不同时挂 StandaloneEncounterController。
屏幕显示当前控制对象、按键提示及生命。

## 验证入口

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/Taming/TamingRulesTests.cs`。
- Showcase：`Assets/_Project/Scripts/Tests/Showcase/Taming/TamingShowcase.cs`。
- Showcase 从 Boot 标题实际加载世界；使用框架隔离存档目录。
- **2026-10-14 改了一处断言**（用户授权只改这一行，因为加了巡夜人）：原「巡逻者恰好 2 只」改成
  「至少 2 只，且第 0、1 位的 stableId 仍是 `patrol-a` / `patrol-b`」（`TamingShowcase.cs:52`）。其余内容未动。
  已知欠账（与本次改动无关、归协作者）：按 T 时选中的是离得最近的巡逻者乙而不是主怪——主怪巡逻线 2026-10-07 从 4 米拉到 8 米所致；
  该回放 2026-09-28 之后没人跑过，本轮只报告不修。

验证覆盖切换及长按、双方位移归属、返回后友善、死亡退出、实际镜头移动。
多目标选择通过既有视图控制区实现，输入命令与回放状态已接入。已驯服但未控制的角色原地待命，未驯服者继续各自 AI。
当前不提供敌人攻击操作、自动跟随；主场景受控角色已沿用玩家扫掠碰撞，旧独立挂点不作为五店入口。

## 本轮测试（2026-09-20）

EditMode 全量 191/191 通过。Taming Showcase 1/1 通过，4 个检查点通过，无运行时异常。
报告：`Logs/verify/taming/20260920-221546/report.md`。
真实 Input System 短按 T 已验证；移动和返回通过同一场景控制器的 Simulate 验证，未自动注入 WASD。
截图中测试叠加层与生命 HUD 重叠，且 Game Gizmos 可见；它们不是正式 UI。视觉与手感仍待开发者确认。
