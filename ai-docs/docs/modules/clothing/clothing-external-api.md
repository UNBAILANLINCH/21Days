---
type: external-api
module: clothing
layer: runtime
maturity: seed
---

# 衣肆公开接口

`ClothingConfig.CreateRules()` 建规则；`ClothingRules.Apply(in ClothingIntent, out string)` 返回是否接受；`HasInvestigated(index)`、`Received` 只读。`ClothingController.Apply(customer, claim, role)` 返回反馈。顾客role为customer0至customer2。

身份和操作许可由调用方提供，不从全局单例猜测。拒绝时不得发放成果。配置与正常Boot主场景入口见[module guide](clothing-module-guide.md)。

`CaptureInvestigations()`返回调查标记副本；`Restore(bool[] seen, bool received)`先验证长度与领取前置，再恢复。主场景组合接线将它们写入当前槽ShopProgressSaveData。

衣物是本玩法本地成果标记，没有写Loot背包或主线事实键。正式衣服用途、物品ID、跨阶段持有要在正式接入时确定。

正式接入须先确定物品/事实键及生命周期；不要把试玩本地成果当作已写入主线进度。

