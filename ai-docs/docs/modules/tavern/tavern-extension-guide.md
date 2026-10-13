---
type: extension-guide
module: tavern
layer: runtime
maturity: seed
---

# 酒肆扩展

改上限、阈值与倍率用Tavern.asset。规则输入不读Time、不改Player/Monster/Boss SO。正式酒桌交互、被灌酒演出和过关奖励应由正式场景接此规则，不在Battle里再复制一套场外醉酒。

改规则后跑本模块EditMode；改场景、界面或回店流程后跑本模块Showcase。场景和预制体用Unity编辑器保存，不手写YAML/meta。不得运行时修改SO。

主场景人物各自持有醉酒，移动倍率与感知倍率分别接原驱动；增加客人时用稳定ID并核对作者初始点位与近距资格。场外值已进当前槽ShopProgressSaveData，离区按约定清理。挑战使用shop_boss_battle复用Battle；仅shop_boss目标读取现场sample_boss醉酒，卸载撤销，不写原示例击败标记。正式酒肆剧情、奖励和被灌演出仍待内容定案。

