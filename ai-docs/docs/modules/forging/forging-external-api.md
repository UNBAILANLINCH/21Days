---
type: external-api
module: forging
layer: runtime
maturity: seed
---

# 打造公开接口

`ForgingRules.Apply(in ForgingIntent, out string)`；action为SetQuota、StartProduct、Strike、Cancel、Submit。SetQuota的value是补料后的当前库存，只能增加且不超上限；StartProduct的value是A/B/C索引0/1/2，Strike的value是点位0至3。只读Quota（当前库存）、Available、Reserved、Spent（累计消耗）、ProductCost、CurrentProduct（无工件=-1）、Progress、NextPoint、Submitted、CanSubmit；`Count(index)`和`Required(index)`读产物/目标数量，非法索引抛ArgumentOutOfRangeException。`ForgingController.Apply(action,value,role)`将officer/smith身份转成意图。

身份和操作许可由调用方提供，不从全局单例猜测。拒绝时不得发放成果。配置与正常Boot主场景入口见[module guide](forging-module-guide.md)。

`Capture()`返回ForgingProgressData；`Restore(saved)`先验证额度、已用材料、产物和未完工预留，再恢复。恢复未完工工件后可继续点位或取消；拒绝损坏快照时不部分修改现有状态。

`ForgingView`通过UIService打开，参数为`(ForgingController, Func<ForgingAction,int,string>)`：视图发点击意图，委托由现场持有者复核许可并返回提示，视图自身不取得身份或写存档。`WaitAsync(CancellationToken)`在返回/外部关闭时结束，取消令牌取消时抛取消；关闭和暂停/输入恢复归调用方负责。

复用ItemCraftRules只做材料可用判定。本地金属/A/B/C编号1/2/3/4只在规则内部，不是正式物品表ID。未完成工件只预留材料，取消释放预留；已完成材料不退。库存上限不限制累计消耗，完整三项目标成本允许超过上限并通过官证补料完成。

正式接入须先确定物品/事实键及生命周期；不要把试玩本地成果当作已写入主线进度。

