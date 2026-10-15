---
type: module-guide
module: forging
layer: runtime
maturity: seed
---

# 打造独立玩法

官证补充金属库存，匠人按固定四点位打造A/B/C并提交规定数量。物品名称暂用字母占位，不写入正式背包或主线事实。

2026-10-13按用户纠正迁到同一3D主场景，玩法仍独立，不连接十二阶段主线。建筑Cube、现成小人和按店显隐的实际入口见[五店接线指南](../shopplaytest/shopplaytest-module-guide.md)。

## 责任与数据流

用独立「借用身份」按钮借匠人（无需先交谈） → 和打造台对话选“开始打造。” → 选择A/B/C并点“打造” → 按圆盘高亮四点顺序完成 → 金属不足时返回街面，借官证向文书补料 → 借匠人继续打造 → A/B/C达到1/2/3件后提交。本体或其它身份不出现打造入口。提交消耗三个目标工件并记录完成，拒绝重复打造、补料和提交；重试仅保留调试接口。

类型：ForgingConfig / ForgingRules / ForgingIntent / ForgingController / ForgingView。规则与意图在Game.Runtime中；规则纯C#，UI通过共用状态接线，Core不引用玩法。Controller记录意图结果，Reset只重置本玩法。

2026-10-14 按用户线框图及后续确认接入独立圆盘界面：左上显示可用金属/库存上限，旁列当前库存、累计消耗和预留；中间圆盘四点；右侧三个可选择工件及数量；底部打造/消耗、取消工件、提交和返回。初始20/24，每件消耗5金属，A/B/C目标分别1/2/3。累计共需30金属，库存上限仅约束当前持有量；官证可在消耗后再次补至24，累计消耗可超过24。物品名称和美术仍为占位。

## 配置

配置资产位于Assets/_Project/Data/ShopPlaytest/。

Forging.asset：initialQuota=20、maximumQuota=24、productCost=5、requiredProducts=1/2/3，pointOrder=0/1/2/3。每件成本须能放入库存；三项目标总成本允许超过库存上限，但不得超过int范围。构造时复制数组，运行时不修改SO。

界面资产为`Assets/_Project/Prefabs/UI/ForgingView.prefab`，Addressables地址`ForgingView`。文字、背景、圆盘和点位可在预制体中直接替换与调整。`21Days/五店/创建金银行打造界面`仅创建缺失资产，已存在时拒绝覆盖，以保留美术编辑。

## 接线与生命周期

Boot上的ShopPlaytestInstaller工厂创建Controller并注入模块遥测scope；ShopPlaytestState复用原Taming/Identity归属，把实际职业转成只读意图。配置在运行时只读。人物、操作物和建筑已在现有SampleScene预摆，进入活动区按店显示，操作不会生成房间或角色。

ShopPlaytestState持有单次打造活动：打开前检查匠人身份，持有世界暂停令牌并关闭Gameplay动作图；每次点击复核会话、活动店、身份、人物显隐和距离，通过Controller应用意图并同步存档分区。界面走UIService的Panel层，自动遮盖HUD；返回、Esc、取消加载或卸载统一收尾，释放暂停并按原状态恢复输入。关闭界面不等于取消工件，未完成工件及预留材料可继续；只有“取消工件”释放预留。

## 约束

复用ItemCraftRules只做材料可用判定。本地金属/A/B/C编号1/2/3/4只在规则内部，不是正式物品表ID。工件索引为0/1/2，无工件为-1。未完成工件只预留材料，取消释放预留；完成时库存扣5，累计消耗加5，不能超过该工件目标数量。提交前必须三种数量全部达标；提交时消耗目标工件，界面显示“已提交”。

## 验证入口

- EditMode：Assets/_Project/Scripts/Tests/EditMode/Forging/。
- Showcase：Assets/_Project/Scripts/Tests/Showcase/Forging/ForgingShowcase.cs。
- 回放已改为正常Boot与现有3D SampleScene，真实走到店前、切人和操作；不再使用独立试玩入口。
- 回放存档由Showcase框架隔离，真实测试数字和报告在[实施记录](../../../../PRP/independent-shop-mechanics/implementation-report.md)。自动检查不替代开发者视觉确认。
- ABC新规则相关EditMode完成147项、job成功且失败列表为空（MCP result为null，不推算passed/skipped）。金银行两条Showcase在`20261014-224543`批次2/2通过、0失败、0跳过、零运行时异常：补料后累计消耗30、ABC目标、防重、错点/取消、关闭/读档续做、Esc恢复移动和界面打开时切局清理均覆盖。界面美术为占位，开发者视觉确认待完成。

## 埋点

模块scope为forging；intent_resolved记录接受的action/value（StartProduct的value为工件索引），intent_rejected记录拒绝理由和可用库存，reset记录重试。每帧移动和表现不埋点。加载、借身份与长耗时由ShopPlaytest以及已有Identity/Rhythm/Battle负责。

打造界面开关沿用core.ui埋点；shopplaytest的forging_entry_rejected、forging_command_rejected记录身份和现场许可拒绝，forging_view_failed记录异步失败。

## 主场景生命周期增量

同店显隐不会清进度；未完成工件预留、下一点位和完成产物由五店接线保存/恢复。
ShopProgressSaveData升为v2，打造快照Schema=2：保存当前库存、累计消耗、三个产物数量和未完成点位；恢复先完整校验再写状态。v1的钥匙/首饰记录保存在LegacyForging，ABC进度从新配置初态开始，不假设两类旧物品与三类新物品的对应关系；衣肆、戏班和酒肆进度保留。
新游戏和切槽先换会话代数，旧现场卸载不得覆盖新分区。当前仍为独立玩法成果，不自动接十二阶段事实或正式物品发放。

