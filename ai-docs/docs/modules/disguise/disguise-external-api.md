---
type: external-api
module: disguise
layer: runtime
maturity: stable
---

# Disguise 外部接口

`DisguiseRules.AllowsEnemyAttack(bool isDisguised)` 返回攻击许可，无副作用。
所有敌人规则在实际出手前调用；不得仅在开始追击时检查一次。
输入来自 `PlayerSnapshot.IsDisguised`，不能从 Sprite 颜色推断状态。
该接口不改变警戒或追击、不处理玩家自己发起的普通攻击。
