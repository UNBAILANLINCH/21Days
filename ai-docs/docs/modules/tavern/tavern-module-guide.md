---
type: module-guide
module: tavern
layer: runtime
maturity: seed
---

# 酒肆独立玩法

## 行酒令分步界面（2026-10-20）

`TavernFingerGuessView` 现在按“选出指→猜总数→看结果”分步呈现，同一时刻只显示一组六项。重选出指保留当前选择；对手仍在玩家确认前固定，玩法没有改成后手看前手。结果主区只显示一行谁饮酒或平局，双方出指／喊数／合计及真实消息移到可展开详情，新消息有明确入口。`OnGuess`、`OnNext`、离席与原UI Cancel链路保持；界面只处理显示，原ShopPlaytestState继续负责暂停、资格、距离、结算和存档。

复用现有无字浅纸、标题菱结与背包选项／动作底图，动态文字仍TMP；旧白盒生成器检测正式Paper后拒绝覆盖。风格、三版动效、各分辨率预览与本次实际验证状态见[行酒令交付记录](../../ui-tavern-20261020.md)。分步输入专项为`TavernArtShowcase.Guess_StepsInputCompactResultCancelAndReopen`，学习／保存／战斗仍由既有`TavernShowcase.Guess_LearningSaveBattleAndLeaveReset`回归。

## 划拳与动态难度（2026-10-18）

正常入口已由直接灌酒改为划拳：各出 0～5 指，喊数在自己出指数至加 5 的范围，只有一方猜中才分胜负，输者加一杯醉意；同中或都不中为平局。对手在玩家确认前固定选择，单局身份只结算一次。`TavernFingerGuessView` 只抛选择事件；`ShopPlaytestState` 复验会话、距离、可见存活、酒客身份和三店资格，再交 `TavernController` 结算。

`TavernOpponentLearning` 每个对手保存最近 24 手，按近期权重枚举 36 个合法应对。前三手只收集；之后按强度概率使用历史最佳应对，其余保留个人偏好。非平局胜负以 1/8 平滑更新玩家表现；`TavernConfig.difficultyAdjustment` 映射强度修正，每局最多变化 0.035。甲、乙、主人基础强度为 0.2／0.4／0.65；这是策略使用率，不等于胜率。没有读取玩家本轮选择或强制输赢。

学习数据、已获线索随槽保存到 `ShopProgressSaveData` v5；取消未揭晓局不记样本，离区只清醉意、保留学习。主人喝一杯即可挑战，客人到 20 解锁各自消息。正式故事在 `action_r0197` 经局部遭遇进入 BOSS，胜利续回楼梯口；旧档没有正式故事时沿原独立挑战。`world.shopboss.defeated` 与原示例 BOSS 标记分开。

新视图地址 `TavernFingerGuessView` 已登记 UI 组，资产由 Unity 编辑器生成。旧直接饮酒接口仅供既有规则调用，正常玩家选项不再直接加值。本轮实际测试与待验事项统一记在 [实施任务](../../../../PRP/tavern-story-guidance/tasks.md)，下方历史记录不代表新划拳已经视觉验收。

复用原场外醉意、真实移动和感知倍率、战斗初始醉酒及离区清理。现成小人、店前活动区和按店显隐的实际入口见[五店接线指南](../shopplaytest/shopplaytest-module-guide.md)。酒肆与其它四店共用SampleScene，正式主线已接到本轮会面终点。

## 责任与数据流

取得并穿上酒客皮 → 与甲／乙／主人划拳 → 输者饮酒、学习已结算历史 → 醉后消息／主人挑战 → 正式战前对白 → 回合制战斗 → 胜利续回会面或失败返回等待 → 离区清醉意。

类型：TavernConfig、TavernDrunkRules、TavernFingerGuessRules、TavernIntent、TavernController、TavernOpponentLearning、TavernLearningData、TavernFingerGuessView。规则与学习器纯C#，UI通过共用状态接线，Core不引用玩法。Controller记录意图结果，Reset清在途拳局和醉意，学习由当前槽恢复。

## 配置

配置资产位于Assets/_Project/Data/ShopPlaytest/。

Tavern.asset：每杯10；玩家/BOSS上限100，普通客人20；玩家效果阈值10，减速0.5，最低感知倍率0.5。划拳基础强度、个人习惯概率、曲线和最大步长由同一配置提供。

## 接线与生命周期

Boot上的ShopPlaytestInstaller工厂创建Controller并注入模块遥测scope；ShopPlaytestState复用原Taming/Identity归属，把实际职业转成只读意图。配置在运行时只读。人物、操作物和建筑已在现有SampleScene预摆，进入活动区按店显示，操作不会生成房间或角色。

## 约束

场外倍率接既有遭遇驱动，饮酒按现场人物ID分开。NPC速度与感知倍率独立；观测NPC保留感知/追近但双方不结算探索伤害。正式挑战通过局部shop_boss_battle复用sample_boss战斗配置和原BattleArena，写独立酒肆胜利标记。仅本店开战醉酒来自现场规则，退出解绑；0覆盖占位50，原示例目标仍取配置。各店进度加入正常槽位分区，实际保存走Session闸门。

## 验证入口

- EditMode：Assets/_Project/Scripts/Tests/EditMode/Tavern/。
- Showcase：Assets/_Project/Scripts/Tests/Showcase/Tavern/TavernShowcase.cs。
- 回放已改为正常Boot与现有3D SampleScene，真实走到店前、切人和操作；不再使用独立试玩入口。
- 回放存档由Showcase框架隔离，真实测试数字和报告在[实施记录](../../../../PRP/independent-shop-mechanics/implementation-report.md)。自动检查不替代开发者视觉确认。

## 埋点

模块scope为tavern；guess_started、guess_resolved记录开始与结算，guess_rejected记录拒绝，difficulty_updated记录强度、表现和样本数；intent_resolved、reset沿用醉意操作日志。每帧移动和表现不埋点。面板耗时与失败由ShopPlaytest的guess_open／guess_failed记录。

## 主场景生命周期增量

同店显隐不会清进度；玩家/BOSS/各客人场外醉酒及挑战结果由五店接线保存/恢复。
新游戏和切槽先换会话代数，旧现场卸载不得覆盖新分区。正式主线读取三店真实背包成果与酒肆胜利；本轮结束不自动完成阶段二。

