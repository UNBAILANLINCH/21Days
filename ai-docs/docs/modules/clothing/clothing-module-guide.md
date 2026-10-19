---
type: module-guide
module: clothing
layer: runtime
maturity: seed
---

# 衣肆独立玩法

三顾客各有身份与订衣情况；借对应身份向裁衣匠交谈，得到被认作该顾客时的回应，正确身份只领一次。

2026-10-13按用户纠正迁到同一3D主场景，玩法仍独立，不连接十二阶段主线。建筑Cube、现成小人和按店显隐的实际入口见[五店接线指南](../shopplaytest/shopplaytest-module-guide.md)。

## 责任与数据流

与顾客交谈直接听到其自身的订衣情况。借用身份是独立按钮，无需先交谈。借甲/乙/丙 → 向裁衣匠交谈，裁衣匠按当前身份回应 → 完成该对白记录调查 → 乙选择「那就劳烦您拿给我。」领衣。甲、丙和本体没有取衣选项；领取后再谈显示已领取。默认订衣人为乙；甲初次到店，丙陪同。

类型：ClothingConfig / ClothingRules / ClothingIntent / ClothingController。规则与意图在Game.Runtime中；规则纯C#，UI通过共用状态接线，Core不引用玩法。Controller记录意图结果，Reset只重置本玩法。

## 配置

配置资产位于Assets/_Project/Data/ShopPlaytest/。

Clothing.asset：names、clues（顾客自己的话）、collectorIndex、visitorReply、tailorReplies（按顾客索引的裁衣匠回应）、receivedReply、handoverReply；旧tailorClue保留兼容。回复数组必须与姓名数量一致。三顾客映射与场景角色是本期固定内容，不可只增加数组长度就宣称支持更多顾客。

## 接线与生命周期

Boot上的ShopPlaytestInstaller工厂创建Controller并注入模块遥测scope；ShopPlaytestState复用原Taming/Identity归属，把实际职业转成只读意图。配置在运行时只读。人物、操作物和建筑已在现有SampleScene预摆，进入活动区按店显示，操作不会生成房间或角色。

## 约束

2026-10-17按用户确认统一为黑色长衫。主场景组合接线在领取后通过LootService发放1802黑色长衫×1，并记录防重复奖励；它是酒肆乔装三件套之一，通行不消耗。ClothingController仍只拥有本店资格和领取结果，不直接写背包。

## 验证入口

- EditMode：Assets/_Project/Scripts/Tests/EditMode/Clothing/。
- Showcase：Assets/_Project/Scripts/Tests/Showcase/Clothing/ClothingShowcase.cs。
- 回放已改为正常Boot与现有3D SampleScene，真实走到店前、切人和操作；不再使用独立试玩入口。
- 回放存档由Showcase框架隔离，真实测试数字和报告在[实施记录](../../../../PRP/independent-shop-mechanics/implementation-report.md)。自动检查不替代开发者视觉确认。

## 埋点

模块scope为clothing；intent_resolved记录操作与接受/拒绝，reset记录重试。每帧移动和表现不埋点。加载、借身份与长耗时由ShopPlaytest以及已有Identity/Rhythm/Battle负责。

## 主场景生命周期增量

同店显隐不会清进度；已调查证词和一次领取由五店接线保存/恢复。
新游戏和切槽先换会话代数，旧现场卸载不得覆盖新分区。正式物品发放由五店组合模块处理，不自动接十二阶段事实。

