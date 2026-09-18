# Player 与 Monster 实现上下文

## 框架接点

- `Game.Runtime` 依赖 `Game.Core`；玩法类在 `Runtime/Player`、`Runtime/Monster`，不要把玩法名词写入 Core。
- 根作用域的 `GameplayInstaller` 注册玩法类型；`GameFlow` 只管理遭遇场景，怪物自身状态机在逻辑规则内。
- `SimulationRunner` 每个固定 tick 先处理玩家，再处理怪物；输入经 `InputCommand`，数值运算经 `GameMath`，随机停步经 `logic.monster.patrol` 流。
- 玩家与怪物的可重放运行数据各实现 `IReplayState`，在确定接线点按 Player、Monster 顺序注册。改变快照布局时同步升级 `ReplayFormat` 版本。

## 数据与规则

Player 对外提供只读位置、朝向、潜行、伪装、存活与生命快照，以及受伤入口。Monster 仅依赖这些公开信息。配置资产只存静态数值；位置、目标、生命、警戒值、计时器与随机流状态属于运行数据，不写入 SO 或存档。

怪物状态：`PatrolWalk`、`PatrolPause`、`Alert`、`Hostile`、`Dead`。攻击与受击是动作，不是常驻状态。警戒增长率为每秒 1/4；满值离开后按 `t²/36` 下降，6 秒归零。红区优先于橙区和背后近距察觉。无视线遮挡，追击直线移动；巡逻路径在场景中按顺序布点。

## 首版默认值

扇区全角 75°，红/橙半径 2/6，近距察觉 1.5，攻击距离 0.8；巡逻速度 2 单位/秒，警戒与敌对倍率 1.1/1.25；双方生命 3，每次伤害 1。其余冷却、玩家速度与攻击距离在各模块配置资产中以暂定值标明，试玩后校准。

## 接线与验证

新增遭遇场景，用可替换占位表现；Player/Monster Showcase 可由代码搭建验证画面。遭遇入口由 Monster 侧订阅标题开始事件，Boot 中移除 SampleInstaller 组件，保留 Sample 样板文件。面板或触屏控件只产生动作意图，不直接改运行数据。Unity 资产由编辑器序列化，生成文件与 `.meta` 不手改。先完成 EditMode 与固定输入回放检查，再跑 Showcase、编译、lint、资产体检和文档同步。编辑器接线见 `editor-setup.md`。
