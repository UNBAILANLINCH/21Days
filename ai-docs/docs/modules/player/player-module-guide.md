---
type: module-guide
module: player
layer: runtime
maturity: stable
---

# Player 模块指南

## 职责与边界

Player 提供本次 Monster 遭遇所需的最小玩家行为：移动、朝向、潜行、伪装、攻击、受伤和死亡。
需求、暂定数值和验收口径见 `PRP/monster-ai/`；背包、装备、成长、正式动画与背后处决没有实现。

| 层 | 类型 | 职责 |
| --- | --- | --- |
| 输入 | `PlayerIntent` | 一个逻辑 tick 的移动向量与三个按钮状态 |
| 配置 | `PlayerConfig` | 不变的原型数值，编辑器可调 |
| 规则 | `PlayerRules` | 固定步长推进、按键边缘、伤害 |
| 运行数据 | `PlayerModel` | 位置、朝向、状态、生命、计时器与回放字段 |
| 跨模块读口 | `PlayerSnapshot` | Monster 感知所需的只读值 |
| 跨模块写口 | `DamageIntent` | 经 `PlayerRules.ApplyDamage` 施加正伤害 |
| 根注册 | `PlayerInstaller` | 把配置、模型、规则注册进 Boot 根作用域 |

这些类型均在 `Game.Runtime` 程序集。依赖方向为 Player → Game.Core；Game.Core 不引用 Player。
规则类为纯 C# 对象，不继承 MonoBehaviour，不读取 Unity 的帧时间或场景单例。

## 数据流

1. `GameInput.inputactions` 的 Gameplay 动作由 `LiveInputSource` 采样。
2. `InputCommand` 保持原有 31 字节布局，只使用按钮位 3、4、5。
3. `EncounterStep` 把轴与按钮位映射为 `PlayerIntent`。
4. 同一固定 tick 内先执行 `PlayerRules.Step`，再由 Monster 读取 `PlayerModel.Snapshot`。
5. 玩家攻击命中后通过 `MonsterRules.ApplyDamage` 写怪物状态。
6. 怪物命中后通过 `PlayerRules.ApplyDamage` 写玩家状态。
7. `EncounterSceneView` 在 `LateUpdate` 读取模型，只更新占位图和界面。

`PlayerRules.Step` 返回本 tick 是否产生攻击动作；命中距离、朝向与目标由 `EncounterStep` 判定。
伪装为按下边缘切换，攻击为按下边缘触发且受冷却限制，潜行为按住生效。
玩家生命归零后不再移动或攻击；死亡视觉由遭遇场景的占位图反馈。

## 运行数据与回放

| 数据 | 所属 | 快照 |
| --- | --- | --- |
| 位置与朝向 | `PlayerModel` | 是 |
| 潜行与伪装 | `PlayerModel` | 是 |
| 当前生命 | `PlayerModel` | 是 |
| 攻击剩余冷却 | `PlayerModel` | 是 |
| 上 tick 伪装与攻击按钮 | `PlayerModel` | 是 |
| 移动速度、伤害等常量 | `PlayerConfig` | 否 |

按钮边缘也进入快照，恢复后长按不会被误判为一次新攻击。
运行数据不写回 ScriptableObject，也不增加 JSON 存档分区。
Player 回放状态由 `MonsterInstaller` 在固定顺序中首先注册，然后才是 Monster 与遭遇步骤。
这次注册顺序变化已把 `ReplayFormat` 版本升至 2；版本 1 的旧回放会被拒读。

## 配置与临时数值

`PlayerConfig` 定义在 `Assets/_Project/Scripts/Runtime/Player/PlayerConfig.cs:7`。
默认移动速度 3、潜行速度 1.5、攻击距离 1、攻击冷却 0.6 秒、生命 3、伤害 1。
它们是原型值，攻击冷却尤其需要试玩校准。
数值只应在配置资产上调整，不应在规则或视图中再写第二份。
配置运行时必须为正，唯攻击冷却允许为零；不合法配置会在规则构造时抛错。

## 接线与生命周期

`PlayerInstaller` 已挂在 Boot 场景 `GameBootstrap` 上，和 `MonsterInstaller` 一起进入根作用域。
配置资产位于 `Assets/_Project/Data/Player/PlayerConfig.asset`，已赋给 Installer 的 `config` 字段。
如果未拖配置，Installer 会记录错误并用内存中的默认 ScriptableObject，供原型调试。
进入遭遇时 `EncounterStep.Begin` 调用 `PlayerRules.Reset`，以场景出生点初始化模型。
退出遭遇时 `EncounterStep.End` 停止逻辑推进；回放仍可读写模型快照。
玩家没有单独的场景状态或子作用域；场景与标题导航由 Monster 遭遇状态管理。

输入映射：键盘 WASD/方向键移动、Shift 潜行、G 伪装、J 攻击；手柄左摇杆、左肩键、北面按钮、西面按钮。
触屏虚拟手柄在平台报告触屏优先时由遭遇状态创建，复用同一 Gameplay 动作。
`GameInput.cs` 为 Unity 输入系统生成物；只改 `.inputactions`，由 Unity 重新生成。

## 已知集成状态

脚本、输入映射、配置资产、Boot Installer、遭遇场景和 Addressables 均已接线。
2026-09-20 验证结果：Unity 编译无错误，相关工程 EditMode 全量 181/181 通过，
Player Showcase 的 3 个检查点通过且运行时异常为 0；视觉表现仍需开发者确认。

## 修改时检查

- 增加一个玩家动作：先改输入资产和 `InputCommand` 的预留位，再改 `EncounterStep` 映射与意图。
- 改按键边缘或冷却：同步 `PlayerModel` 快照字段，并检查回放格式版本。
- 改跨模块数据：让 Monster 读 `PlayerSnapshot`，不要把 `PlayerModel` 可写字段暴露出去。
- 改伤害：保持 `DamageIntent` 为正，死亡统一表现为生命零。
- 改显示：只在视图读状态，不把规则搬到 `LateUpdate` 或 `OnGUI`。
- 改配置：同步 `PlayerConfig` 的值范围校验和 EditMode 测试。
- 完成编辑器接线后：跑 Player Showcase、资产体检、lint 与文档检查。
