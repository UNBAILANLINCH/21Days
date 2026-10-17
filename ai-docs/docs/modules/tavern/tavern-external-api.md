---
type: external-api
module: tavern
layer: runtime
maturity: seed
---

# 酒肆公开接口

## 划拳入口

`TavernController.BeginGuess(targetId)` 返回局号，创建并隐藏对手选择；仅接受 `shop_boss/shop_customer_a/shop_customer_b`。`ResolveGuess(in TavernIntent, out Result, out reason)` 验证局号、目标、酒客资格与输入，消费一次，再更新醉意和学习。`CancelGuess()` 撤销未完成局；`Guess.IsPending` 供 Session 稳定边界使用。调用方仍须检查当前槽、现场距离、可见存活和三店资格。

`TavernIntent(roundId, targetId, isGuest, fingers, call)` 是确认输入。`TavernFingerGuessRules.Result` 提供双方出指／喊数、合计和结果。`CanChallenge`、`UnlockedClue(targetId)` 只读查询门槛。UI 的 `OnGuess(int,int)/OnNext` 由接线方订阅；关闭只撤销当前局，不撤销已结算醉意。

`CaptureLearning()` 返回深拷贝，`RestoreLearning(dictionary)` 先验证全部数据再替换；空值清当前槽学习，恢复同时取消未完成局。历史最多24条，编码为 `fingers*6+(call-fingers)`，表现／强度范围0～1000。`Reset()` 清醉意和在途局，保留学习；换槽必须显式恢复新槽学习。

`TavernDrunkRules.Apply(in TavernIntent, out string)`；DrinkPlayer、DrinkBoss、DrinkCustomer需酒客身份，Leave不需身份。主场景普通客人按意图中的稳定ID分别累计，用`CustomerDrunkFor(id)`查询，不能用兼容单客人的CustomerDrunk代表所有人。`Reset()`清全部场外值。

身份和操作许可由调用方提供，不从全局单例猜测。目标近距、可见、存活检查在主场景接线处执行。配置与正常Boot入口见[module guide](tavern-module-guide.md)。

`SpeedMultiplier/CanSneak`供当前控制人物；`BossSpeedMultiplier/CustomerSpeed(id)`供NPC移动；`BossAwarenessMultiplier/CustomerAwareness(id)`只供感知，不能混作速度。正式挑战使用局部shop_boss_battle，复用sample_boss战斗配置与既有Battle；仅当前有效会话的shop_boss目标读取实际醉酒值（包含0），其它目标返回null使用各自配置；卸载撤销来源。酒肆结案写world.shopboss.defeated，不写原示例BOSS击败标记。

`CaptureCustomers()`返回字典副本；`Restore(player,boss,customers)`先验证范围与ID，再恢复。主场景在当前槽保存场外值；离酒肆由当前受控人物位置判断，清全部场外值，不改战内规则。

正式主线等待action_r0197消费局部战斗真实胜利；旧无主线五店档保留独立挑战。调用方不能手改剧情阶段或用调试成果冒充正常通关。

