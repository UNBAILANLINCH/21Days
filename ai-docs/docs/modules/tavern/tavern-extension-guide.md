---
type: extension-guide
module: tavern
layer: runtime
maturity: seed
---

# 酒肆扩展

划拳强弱通过 `TavernConfig` 的基础强度、偏好概率、调整曲线和最大变化步长修改。曲线横轴是玩家非平局平滑表现，纵轴是强度增量；不能把曲线标成已保证的胜率。新增对手须同步控制器目标白名单、偏好、存档校验与场景入口。保持先固定对手、后确认玩家的顺序，策略接口不能接本轮玩家答案。

改学习算法须覆盖固定习惯、习惯改变、平局、最大步长、样本上限与深拷贝；改 UI 跑 `TavernShowcase.Guess_LearningSaveBattleAndLeaveReset`。正式 BOSS 通过局部遭遇续接主线，不能重新 `StartAsync` 覆盖主线。重入纪元取持久 Narrative 激活序号，不用进程内计数。

改上限、阈值与倍率用Tavern.asset。规则输入不读Time、不改Player/Monster/Boss SO。正式酒桌交互、被灌酒演出和过关奖励应由正式场景接此规则，不在Battle里再复制一套场外醉酒。

改规则后跑本模块EditMode；改场景、界面或回店流程后跑本模块Showcase。场景和预制体用Unity编辑器保存，不手写YAML/meta。不得运行时修改SO。

主场景人物各自持有醉酒，移动倍率与感知倍率分别接原驱动；增加客人时用稳定ID并核对作者初始点位与近距资格。场外值已进当前槽ShopProgressSaveData，离区按约定清理。挑战使用shop_boss_battle复用Battle；仅shop_boss目标读取现场sample_boss醉酒，卸载撤销，不写原示例击败标记。正式酒肆剧情、奖励和被灌演出仍待内容定案。

