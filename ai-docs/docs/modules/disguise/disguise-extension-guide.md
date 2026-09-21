---
type: extension-guide
module: disguise
layer: runtime
maturity: stable
---

# Disguise 扩展指南

新增敌人攻击类型时复用攻击许可，并增加已敌对情况下的禁攻用例。
若未来要求伪装隐藏感知或攻击自动破除伪装，先明确规则，再修改感知/Player 意图；不能从当前禁攻隐含推导。
若新增消耗或持续时间，配置放 SO、运行时计时放模型，同步回放版本与快照测试。
驯服与本模块无依赖；后续前置条件应由交互层组合。
