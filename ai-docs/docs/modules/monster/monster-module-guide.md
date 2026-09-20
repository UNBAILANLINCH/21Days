---
type: module-guide
module: monster
layer: runtime
maturity: stable
---

# Monster 模块指南

## 职责与边界

Monster 在遭遇场景中沿巡逻点移动，感知 Player，累积或消退警戒值，追击、攻击、受伤和死亡。
首版是开放地形原型：没有障碍物视线遮挡、寻路、背后处决或正式美术。
需求来源、暂定参数、已确认取舍见 `PRP/monster-ai/`。

| 层 | 类型 | 职责 |
| --- | --- | --- |
| 配置 | `MonsterConfig` | 可调原型半径、速度、时间、战斗数值 |
| 状态名 | `MonsterMode` | 巡逻走、巡逻停、警戒、敌对、死亡 |
| 固定输入 | `MonsterIntent` | 本 tick 玩家快照及固定步长 |
| 运行数据 | `MonsterModel` | 位置、朝向、生命、警戒值与计时器 |
| 规则 | `MonsterRules` | 纯 C# 巡逻、感知、转态、战斗与快照 |
| 同 tick 调度 | `EncounterStep` | 先 Player 后 Monster，处理双方命中 |
| 场景状态 | `MonsterEncounterState` | 加载和退出遭遇场景 |
| 场景表现 | `EncounterSceneView` | 巡逻点引用、占位图与状态界面 |
| 独立场景入口 | `StandaloneEncounterController` | 直接播放原型场景时读取输入并推进同一套遭遇规则 |
| 触屏输入 | `EncounterTouchControls` | 运行时虚拟摇杆与按钮 |
| 根注册 | `MonsterInstaller` | 玩法逻辑步骤和回放状态接线 |
| 标题入口 | `MonsterTitleRouter` | 标题“开始”事件切到遭遇 |

Monster 与 Player 均在 `Game.Runtime` 程序集。Monster 依赖 Player 的公开快照与伤害意图。
Game.Core 不引用玩法模块；规则类不读取场景组件、不用 `Time.deltaTime` 或全局随机数。

## 状态规则

| 状态 | 进入原因 | 行为 | 离开原因 |
| --- | --- | --- | --- |
| `PatrolWalk` | 开局、停步结束、警戒清零 | 顺序巡逻并计时 | 7–10 秒后停步；感知玩家 |
| `PatrolPause` | 巡逻计时达到随机阈值 | 原地停 2 秒 | 计时结束；感知玩家 |
| `Alert` | 橙区或背后近距察觉、敌对丢失目标 | 累积/消退警戒，发现目标时接近 | 警戒满值或清零；红区出现 |
| `Hostile` | 红区、警戒满值、受击 | 追击最后已知位置并尝试攻击 | 丢失目标 2 秒；死亡 |
| `Dead` | 生命为零 | 不再更新移动或攻击 | 下次遭遇 `Reset` |

警戒与敌对是怪物内部状态，不新增 `GameFlow` 状态；GameFlow 仅管理标题和遭遇场景。
受击和攻击是规则的输入与动作结果，没有为它们再增设常驻状态。

## 感知与警戒

- 前方扇区总角度 75°，红区半径 2，橙区半径 6，比例 1:3。
- 红区先判定；伪装或潜行都不能阻止红区立即敌对。
- 伪装阻止橙区警戒，但不阻止红区与背后近距判定。
- 背后 1.5 单位内可近距察觉；潜行阻止该近距判定。
- 橙区连续暴露 4 秒令警戒升满；脱离后按平方时间衰减，满值 6 秒清零。
- 敌对丢失目标 2 秒后转为满值警戒，再按上述规则消退。
- 感知是位置和朝向的数学判定，没有 Physics 查询或遮挡物判断。
- 扇区本身不在游戏画面显示，状态颜色与警戒条可见。

`MonsterRules.Sense` 在规则类内部完成上述优先级；表现层不能额外判定一次。
攻击仅在敌对、能感知到活目标、进入攻击距离且冷却结束时触发。
玩家攻击命中 Monster 时，`EncounterStep` 限制距离和前半平面，再把伤害意图交给 Monster。

## 巡逻和数值

巡逻点按场景中 `EncounterSceneView.patrolPoints` 的数组顺序读取；至少一个非空点。
单点路径会在该点附近维持巡逻计时，多点路径依序循环。
随机停步区间使用 `logic.monster.patrol` 专用确定性随机流，当前抽整数秒 7、8、9、10。
停步时长 2 秒；巡逻速度 2 单位/秒，警戒与敌对速度倍率 1.1、1.25。
Monster 默认生命 3、每次命中伤害 1、攻击距离 0.8、攻击冷却 1 秒。
工作簿未规定攻击冷却；它是待试玩校准的原型值。
全部数值集中在 `MonsterConfig`，不要在视图或场景脚本中复制一份。

## 回放状态

| 数据 | 所属 | 快照 |
| --- | --- | --- |
| 位置、朝向、最后已知目标 | `MonsterModel` | 是 |
| 状态、生命、巡逻点索引 | `MonsterModel` | 是 |
| 警戒值与升降、失目标计时 | `MonsterModel` | 是 |
| 攻击冷却、停步剩余与下次停步时间 | `MonsterModel` | 是 |
| 巡逻随机流状态 | `MonsterRules` | 是 |
| 当前巡逻点数组 | `MonsterRules` | 是 |
| 遭遇是否激活 | `EncounterStep` | 是 |
| 静态调参 | `MonsterConfig` | 否 |

`MonsterInstaller` 注册回放状态的顺序为 `PlayerModel` → `MonsterRules` → `EncounterStep`。
它同时把 `EncounterStep` 加入 `SimulationRunner`。顺序影响快照字节布局，不可随意变更。
本次新注册已将回放文件格式版本提升为 2，最低可读版本也为 2。
恢复快照后巡逻点和随机流随状态一起恢复；配置资产仍由场景外的模块注册负责。

## 场景与生命周期

Boot `GameBootstrap` 已挂 `PlayerInstaller` 和 `MonsterInstaller`，并已移除 `SampleInstaller` 的入口接线。
等距原型场景应以地址 `IsometricEncounter` 加入 Addressables `Scenes` 组。
场景中的 `EncounterSceneView` 显式引用玩家出生点、巡逻点、`player`、`enerme` 及其纸片；
逻辑 XY 由该视图投影到场景 XZ。缺少显式接线时状态会报错并返回标题，不再运行时按对象名补建。
直接播放该场景时，`StandaloneEncounterController` 使用场景内 `PlayerInput` 推进同一个 `EncounterStep`；
若检测到 Boot 的 `GameBootstrap`，该控制器立即停用，避免与正式 `SimulationRunner` 重复推进。
`MonsterEncounterState` 在场景就绪后 `Begin`，绑定视图；离场时 `End`、解绑并销毁触屏控件。
触屏优先平台创建虚拟摇杆、潜行、伪装、攻击按钮，映射到同一 Gameplay 动作。
占位表现以玩家蓝/青/绿和怪物灰/橙/红/黑区分状态，并显示生命与警戒条。
标题入口由 `MonsterTitleRouter` 订阅 `TitleStartClickedEvent`；同一事件不应同时留给 Sample 路由。

## 已知集成状态

脚本、输入映射、配置资产、Boot、遭遇场景和 Addressables 均已接线。
2026-09-20 验证结果：Unity 编译无错误，相关工程 EditMode 全量 181/181 通过，
Monster Showcase 的 5 个检查点通过且运行时异常为 0，资产体检四项全过；视觉表现仍需开发者确认。

## 修改时检查

- 改感知优先级：保留红区先于伪装与潜行，并在 EditMode 用例加反例。
- 改状态：同步 `MonsterMode`、模型快照、视图反馈和回放格式版本。
- 改随机巡逻：继续使用命名逻辑流，把影响未来抽样的状态放进快照。
- 改路径：维持场景按序配置，`Reset` 必须收到非空巡逻点。
- 改攻击：仅让规则返回攻击动作，由遭遇步骤给玩家施加伤害。
- 改触屏：先改 Gameplay 输入绑定，触屏控件只模拟同一游戏手柄路径。
- 完成场景接线后：跑 Monster Showcase、资产体检、lint、文档检查并让开发者看画面。
