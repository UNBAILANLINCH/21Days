---
type: module-guide
module: taming
layer: runtime
maturity: seed
---

# Taming 模块指南

本阶段是单敌人的独立玩法验证。按 T 直接驯服并切换控制，WASD 移动敌人，相机跟随敌人；再次按 T 返回玩家。
按用户要求暂不接入 SampleScene/Boot，不要求伪装、距离、残血、道具或进度。
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
状态属于场景内 TamingRules，每次重新进入场景重建，不写 SO。

## 试玩入口

`Assets/_Project/Scenes/Verify/Taming.unity` 直接 Play，无需 Boot。
场景中 `Encounter` 显式保存视图、输入资产、双方配置和 SmoothCameraFollow 引用。
视图持有 player/enerme、出生点和巡逻点；本独立验证场景采用 XY 平面。
TamingSceneController 是唯一推进者，不同时挂 StandaloneEncounterController。
屏幕显示当前控制对象、按键提示及生命。

## 验证入口

- EditMode：`Assets/_Project/Scripts/Tests/EditMode/Taming/TamingRulesTests.cs`。
- Showcase：`Assets/_Project/Scripts/Tests/Showcase/Taming/TamingShowcase.cs`。
- 验证场景：`Assets/_Project/Scenes/Verify/Taming.unity`。

验证覆盖切换及长按、双方位移归属、返回后友善、死亡退出、实际镜头移动。
这是独立验证模块，尚未注册正式 GameFlow、触屏或确定性回放；后续接入需补输入命令位和状态快照。
不提供多目标选择、敌人攻击操作、自动跟随或永久存档。

## 本轮测试（2026-09-20）

EditMode 全量 191/191 通过。Taming Showcase 1/1 通过，4 个检查点通过，无运行时异常。
报告：`Logs/verify/taming/20260920-221546/report.md`。
真实 Input System 短按 T 已验证；移动和返回通过同一场景控制器的 Simulate 验证，未自动注入 WASD。
截图中测试叠加层与生命 HUD 重叠，且 Game Gizmos 可见；它们不是正式 UI。视觉与手感仍待开发者确认。
