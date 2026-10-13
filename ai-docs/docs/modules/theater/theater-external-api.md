---
type: external-api
module: theater
layer: runtime
maturity: seed
---

# 戏班公开接口

`TheaterController.Apply(in TheaterIntent)`；Verify、Redirect、FinishSuccess、FinishFailure。`CanPerform(role)`要求验身、巡逻已调开、当前乐正（musician）、未完成。`TheaterEntryPermission.Capture(context)`同时读Taming的驯服/控制资格及本次上下文。

身份和操作许可由调用方提供，不从全局单例猜测。配置与正常Boot主场景入口见[module guide](theater-module-guide.md)。`Restore(entered,redirected,completed)`验证有序前置后恢复；接线者同步恢复作者巡逻路线。

仍为现有四轨下落式，以Rhythm活动留在主场景；低分、取消或中断可再演奏。占位角色使用学徒/都知/乐正名称；正式美术、身份道具与主线过关效果未接。

正式接入须先确定物品/事实键及生命周期；不要把试玩本地成果当作已写入主线进度。

