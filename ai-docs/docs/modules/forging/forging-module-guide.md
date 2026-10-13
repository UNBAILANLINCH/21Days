---
type: module-guide
module: forging
layer: runtime
maturity: seed
---

# 打造独立玩法

官证改拨料，匠人按固定四点位打造钥匙和首饰，提交规定首饰后保留钥匙。

2026-10-13按用户纠正迁到同一3D主场景，玩法仍独立，不连接十二阶段主线。建筑Cube、现成小人和按店显隐的实际入口见[五店接线指南](../shopplaytest/shopplaytest-module-guide.md)。

## 责任与数据流

借官证 → 拨料从2增加到8 → 借匠人 → 开钥匙工件 → 点位1/2/3/4 → 开首饰工件 → 同顺序完成 → 提交首饰 → 显示成果 → 重试。

类型：ForgingConfig / ForgingRules / ForgingIntent / ForgingController。规则与意图在Game.Runtime中；规则纯C#，UI通过共用状态接线，Core不引用玩法。Controller记录意图结果，Reset只重置本玩法。

## 配置

配置资产位于Assets/_Project/Data/ShopPlaytest/。

Forging.asset：初始额度2，最大8；钥匙耗4，首饰耗2，提交首饰1；pointOrder默认0/1/2/3。成本与规定数量之和不能超过最大额度，使用long校验以防整数溢出。

## 接线与生命周期

Boot上的ShopPlaytestInstaller工厂创建Controller并注入模块遥测scope；ShopPlaytestState复用原Taming/Identity归属，把实际职业转成只读意图。配置在运行时只读。人物、操作物和建筑已在现有SampleScene预摆，进入活动区按店显示，操作不会生成房间或角色。

## 约束

复用ItemCraftRules只做材料可用判定。本地金属/钥匙/首饰编号1/2/3只在规则内部；不是正式物品表ID。未完成工件只预留材料，取消释放预留；已完成材料不退。

## 验证入口

- EditMode：Assets/_Project/Scripts/Tests/EditMode/Forging/。
- Showcase：Assets/_Project/Scripts/Tests/Showcase/Forging/ForgingShowcase.cs。
- 回放已改为正常Boot与现有3D SampleScene，真实走到店前、切人和操作；不再使用独立试玩入口。
- 回放存档由Showcase框架隔离，真实测试数字和报告在[实施记录](../../../../PRP/independent-shop-mechanics/implementation-report.md)。自动检查不替代开发者视觉确认。

## 埋点

模块scope为forging；intent_resolved记录操作与接受/拒绝，reset记录重试。每帧移动和表现不埋点。加载、借身份与长耗时由ShopPlaytest以及已有Identity/Rhythm/Battle负责。

## 主场景生命周期增量

同店显隐不会清进度；未完成工件预留、下一点位和完成产物由五店接线保存/恢复。
新游戏和切槽先换会话代数，旧现场卸载不得覆盖新分区。当前仍为独立玩法成果，不自动接十二阶段事实或正式物品发放。

