---
type: module-guide
module: theater
layer: runtime
maturity: seed
---

# 戏班独立玩法

换学徒皮验身、都知皮调开巡逻、乐正皮进入现有四轨下落式，完成或中断后回店。皮须先潜行绕背刺杀取得，再从背包使用；不再接管活着的NPC。

2026-10-13按用户纠正迁到同一3D主场景，玩法仍独立，不连接十二阶段主线。建筑Cube、现成小人和按店显隐的实际入口见[五店接线指南](../shopplaytest/shopplaytest-module-guide.md)。

## 责任与数据流

学徒皮 → 向巡逻学徒验身 → 都知皮 → 向巡逻学徒下令，指派另一条预设巡逻路线 → 乐正皮 → 到戏台操作点打开音游 → 点击开始，D/F/J/K → 达标完成；低分、取消、技术中断返回后可重新演奏。入口允许玩家本体穿皮，由五店许可回调复验持有权与职业。2026-10-19明确能力随皮：来源学徒、都知和乐正死亡不阻断玩法，死者不再提供对话或换皮入口，穿皮统一通过背包。戏台只负责演奏，保留原位置并更名，不再兼任验身。

类型：TheaterConfig / TheaterIntent / TheaterController / TheaterEntryPermission。规则与意图在Game.Runtime中；规则纯C#，UI通过共用状态接线，Core不引用玩法。Controller记录意图结果，Reset只重置本玩法。

## 配置

配置资产位于Assets/_Project/Data/ShopPlaytest/。

Theater.asset引用现有ChongErFei谱面，通过百分比默认60；不修改音频和谱面。使用Combat结果契约判关，不施加战斗伤害，也不计个人曲库通关。

## 接线与生命周期

Boot上的ShopPlaytestInstaller工厂创建Controller并注入模块遥测scope；ShopPlaytestState复用原Taming/Identity归属，把实际职业转成只读意图。配置在运行时只读。人物、操作物和建筑已在现有SampleScene预摆，进入活动区按店显示，操作不会生成房间或角色。

## 约束

仍为现有下落式；光点形式、正式都知/乐正人物美术与十二阶段过关效果未接。2026-10-17按最新文策表，演奏达标后由五店组合模块通过Loot发放1801行头三样×1（旧袍、油彩、小冠合为一个条目），用于酒肆乔装，通行不消耗。身份顺序仍为学徒验身、都知改道、乐正演奏。

## 验证入口

- EditMode：Assets/_Project/Scripts/Tests/EditMode/Theater/。
- Showcase：Assets/_Project/Scripts/Tests/Showcase/Theater/TheaterShowcase.cs。
- 回放已改为正常Boot与现有3D SampleScene，真实走到店前、切人和操作；不再使用独立试玩入口。
- 戏班能力回归真实完成序章入镜后，复用既有叙事存档夹具定位到`action_r0197`；不冒充轮回堂至坊市完整主线通过。实际刺杀、背包穿皮、移动、巡逻学徒交互、音游输入及返回均走现有入口；首条回归先杀死三位来源NPC，再依次使用皮完成玩法。公共回放移动按现有地面与胶囊规划绕行，移动仍使用摇杆；与生产导航无关。
- 回放存档由Showcase框架隔离，真实测试数字和报告在[实施记录](../../../../PRP/independent-shop-mechanics/implementation-report.md)。自动检查不替代开发者视觉确认。

## 埋点

模块scope为theater；intent_resolved记录操作与接受/拒绝，reset记录重试。每帧移动和表现不埋点。加载、借身份与长耗时由ShopPlaytest以及已有Identity/Rhythm/Battle负责。

## 主场景生命周期增量

同店显隐不会清进度；验身、调巡逻和演奏结果由五店接线保存/恢复。
新游戏和切槽先换会话代数，旧现场卸载不得覆盖新分区。正式行头发放由五店组合模块处理，不自动接十二阶段事实。

