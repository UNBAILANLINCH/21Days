---
type: external-api
module: forging
layer: runtime
maturity: seed
---

# 打造公开接口

`ForgingRules.Apply(in ForgingIntent, out string)`；action为SetQuota、StartKey、StartOrnament、Strike、Cancel、Submit。只读Quota、Available、Spent、Progress、NextPoint、Keys、Ornaments、Submitted。`ForgingController.Apply(action,value,role)` 将officer/smith身份转成意图。

身份和操作许可由调用方提供，不从全局单例猜测。拒绝时不得发放成果。配置与正常Boot主场景入口见[module guide](forging-module-guide.md)。

`Capture()`返回ForgingProgressData；`Restore(saved)`先验证额度、已用材料、产物和未完工预留，再恢复。恢复未完工工件后可继续点位或取消；拒绝损坏快照时不部分修改现有状态。

复用ItemCraftRules只做材料可用判定。本地金属/钥匙/首饰编号1/2/3只在规则内部；不是正式物品表ID。未完成工件只预留材料，取消释放预留；已完成材料不退。

正式接入须先确定物品/事实键及生命周期；不要把试玩本地成果当作已写入主线进度。

