---
type: module-guide
module: tavern
layer: runtime
maturity: seed
---

# 酒肆独立玩法

场外饮酒、灌酒、真实移动和感知效果、战斗初始醉酒及离区清理。

2026-10-13按用户纠正迁到同一3D主场景，玩法仍独立，不连接十二阶段主线。2026-10-14建筑Cube已替换为用户提供的新地图占位；现成小人、店前活动区和按店显隐的实际入口见[五店接线指南](../shopplaytest/shopplaytest-module-guide.md)。回放离区目标按新活动区安排，避免旧固定步长停在区内。

## 责任与数据流

借酒客 → 自己喝酒看减速/禁潜行 → 给BOSS和客人灌酒看范围变化 → 挑战BOSS → 现有开战对白 → 回合制战斗 → 回酒肆 → 离区清零或重试。

类型：TavernConfig / TavernDrunkRules / TavernIntent / TavernController。规则与意图在Game.Runtime中；规则纯C#，UI通过共用状态接线，Core不引用玩法。Controller记录意图结果，Reset只重置本玩法。

## 配置

配置资产位于Assets/_Project/Data/ShopPlaytest/。

Tavern.asset：每杯10；玩家/BOSS上限100，普通客人20；玩家效果阈值10，减速0.5，最低感知倍率0.5。20/100仅为用户批准的临时上限解释，不代表原文最终含义。

## 接线与生命周期

Boot上的ShopPlaytestInstaller工厂创建Controller并注入模块遥测scope；ShopPlaytestState复用原Taming/Identity归属，把实际职业转成只读意图。配置在运行时只读。人物、操作物和建筑已在现有SampleScene预摆，进入活动区按店显示，操作不会生成房间或角色。

## 约束

场外倍率接既有遭遇驱动，灌酒按现场人物ID分开。NPC速度与感知倍率独立；观测NPC保留感知/追近但双方不结算探索伤害。挑战用独立占位shop_boss_battle复用sample_boss配置和原BattleArena，不写原BOSS击败标记。仅本店叙事目标的开战醉酒来自现场规则，退出解绑；0覆盖占位50，原目标仍取50。各店进度已加入正常槽位分区，实际保存仍走Session闸门。

## 验证入口

- EditMode：Assets/_Project/Scripts/Tests/EditMode/Tavern/。
- Showcase：Assets/_Project/Scripts/Tests/Showcase/Tavern/TavernShowcase.cs。
- 回放已改为正常Boot与现有3D SampleScene，真实走到店前、切人和操作；不再使用独立试玩入口。
- 回放存档由Showcase框架隔离，真实测试数字和报告在[实施记录](../../../../PRP/independent-shop-mechanics/implementation-report.md)。自动检查不替代开发者视觉确认。

## 埋点

模块scope为tavern；intent_resolved记录操作与接受/拒绝，reset记录重试。每帧移动和表现不埋点。加载、借身份与长耗时由ShopPlaytest以及已有Identity/Rhythm/Battle负责。

## 主场景生命周期增量

同店显隐不会清进度；玩家/BOSS/各客人场外醉酒及挑战结果由五店接线保存/恢复。
新游戏和切槽先换会话代数，旧现场卸载不得覆盖新分区。当前仍为独立玩法成果，不自动接十二阶段事实或正式物品发放。

