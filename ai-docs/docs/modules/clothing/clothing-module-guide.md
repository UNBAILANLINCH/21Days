---
type: module-guide
module: clothing
layer: runtime
maturity: seed
---

# 衣肆独立玩法

三顾客证词形成唯一取衣解；调查需对应顾客身份，已调查证词可回看，正确身份只领一次。

2026-10-13按用户纠正迁到同一3D主场景，玩法仍独立，不连接十二阶段主线。建筑Cube、现成小人和按店显隐的实际入口见[五店接线指南](../shopplaytest/shopplaytest-module-guide.md)。

## 责任与数据流

借顾客甲/乙/丙 → 查看各自证词 → 用乙的身份领衣 → 显示蓝长衫 → 重试。默认正确答案为乙；甲初次到店，丙陪同。

类型：ClothingConfig / ClothingRules / ClothingIntent / ClothingController。规则与意图在Game.Runtime中；规则纯C#，UI通过共用状态接线，Core不引用玩法。Controller记录意图结果，Reset只重置本玩法。

## 配置

配置资产位于Assets/_Project/Data/ShopPlaytest/。

Clothing.asset：names、clues、tailorClue、collectorIndex。三顾客按钮与场景角色是本期固定内容，不可只增加数组长度就宣称支持更多顾客。

## 接线与生命周期

Boot上的ShopPlaytestInstaller工厂创建Controller并注入模块遥测scope；ShopPlaytestState复用原Taming/Identity归属，把实际职业转成只读意图。配置在运行时只读。人物、操作物和建筑已在现有SampleScene预摆，进入活动区按店显示，操作不会生成房间或角色。

## 约束

衣物是本玩法本地成果标记，没有写Loot背包或主线事实键。正式衣服用途、物品ID、跨阶段持有要在正式接入时确定。

## 验证入口

- EditMode：Assets/_Project/Scripts/Tests/EditMode/Clothing/。
- Showcase：Assets/_Project/Scripts/Tests/Showcase/Clothing/ClothingShowcase.cs。
- 回放已改为正常Boot与现有3D SampleScene，真实走到店前、切人和操作；不再使用独立试玩入口。
- 回放存档由Showcase框架隔离，真实测试数字和报告在[实施记录](../../../../PRP/independent-shop-mechanics/implementation-report.md)。自动检查不替代开发者视觉确认。

## 埋点

模块scope为clothing；intent_resolved记录操作与接受/拒绝，reset记录重试。每帧移动和表现不埋点。加载、借身份与长耗时由ShopPlaytest以及已有Identity/Rhythm/Battle负责。

## 主场景生命周期增量

同店显隐不会清进度；已调查证词和一次领取由五店接线保存/恢复。
新游戏和切槽先换会话代数，旧现场卸载不得覆盖新分区。当前仍为独立玩法成果，不自动接十二阶段事实或正式物品发放。

