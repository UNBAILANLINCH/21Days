---
type: external-api
module: tavern
layer: runtime
maturity: seed
---

# 酒肆公开接口

`TavernDrunkRules.Apply(in TavernIntent, out string)`；DrinkPlayer、DrinkBoss、DrinkCustomer需酒客身份，Leave不需身份。主场景普通客人按意图中的稳定ID分别累计，用`CustomerDrunkFor(id)`查询，不能用兼容单客人的CustomerDrunk代表所有人。`Reset()`清全部场外值。

身份和操作许可由调用方提供，不从全局单例猜测。目标近距、可见、存活检查在主场景接线处执行。配置与正常Boot入口见[module guide](tavern-module-guide.md)。

`SpeedMultiplier/CanSneak`供当前控制人物；`BossSpeedMultiplier/CustomerSpeed(id)`供NPC移动；`BossAwarenessMultiplier/CustomerAwareness(id)`只供感知，不能混作速度。挑战使用独立占位内容shop_boss_battle，复用sample_boss配置与既有Battle；仅当前有效会话的shop_boss目标读取实际醉酒值（包含0），其它目标返回null使用各自配置；卸载撤销来源。酒肆结案不写原示例BOSS击败标记。

`CaptureCustomers()`返回字典副本；`Restore(player,boss,customers)`先验证范围与ID，再恢复。主场景在当前槽保存场外值；离酒肆由当前受控人物位置判断，清全部场外值，不改战内规则。

正式接入须先确定物品/事实键及生命周期；不要把试玩本地成果当作已写入主线进度。

