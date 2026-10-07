---
type: module-guide
module: taming
layer: runtime
maturity: seed
---

# Taming 模块指南

正式入口已接 Boot → 标题「开始」→ Addressables IsometricEncounter（SampleScene）。原巡逻者与复制实例使用相同美术/组件，独立路线与身份 `patrol-a`、`patrol-b`，显示名为巡逻者甲、巡逻者乙，玩家为 `player`。
按 T 直接驯服最近巡逻者并接管，WASD 移动，相机跟随；再次 T 返回玩家。左下控制区可定向驯服，已驯服后可 A/B 往返选择。未驯服角色的切换按钮禁用。
仍不要求伪装、距离、残血、道具或进度。
死亡对象不能控制；控制期间目标死亡会返回玩家。

| 类型 | 职责 |
| --- | --- |
| `TamingIntent` | 移动轴与切换按钮 |
| `TamingRules` | 驯服归属、按键边缘、控制切换、双方移动路由 |
| `TamingSceneController` | 独立场景初始化、Action 输入与相机接管 |
| `MonsterRules.MoveControlled`（复用扩展） | 受控敌人的纯逻辑移动 |
| `SmoothCameraFollow.SetTarget`（复用扩展） | 保留镜头偏移，更换跟随对象 |

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

验证覆盖切换及长按、双方位移归属、返回后友善、死亡退出、实际镜头移动。
多目标选择通过既有视图控制区实现，输入命令与回放状态已接入。已驯服但未控制的角色原地待命，未驯服者继续各自 AI。
当前不提供敌人攻击操作、自动跟随；受控敌人沿用原怪物移动/地面投影，未新增玩家式障碍碰撞。

## 本轮测试（2026-09-20）

EditMode 全量 191/191 通过。Taming Showcase 1/1 通过，4 个检查点通过，无运行时异常。
报告：`Logs/verify/taming/20260920-221546/report.md`。
真实 Input System 短按 T 已验证；移动和返回通过同一场景控制器的 Simulate 验证，未自动注入 WASD。
截图中测试叠加层与生命 HUD 重叠，且 Game Gizmos 可见；它们不是正式 UI。视觉与手感仍待开发者确认。
