---
type: extension-guide
module: forging
layer: runtime
maturity: seed
---

# 打造扩展

改成本、额度、提交数量和点击顺序用Forging.asset。四点位索引只能0至3。更复杂打造先明确输入、成功/失败/取消和材料承诺，再换该规则；不把UI点击直接接为Loot发物。

改规则后跑本模块EditMode；改场景、界面或回店流程后跑本模块Showcase。场景和预制体用Unity编辑器保存，不手写YAML/meta。不得运行时修改SO。

复用ItemCraftRules只做材料可用判定。本地金属/钥匙/首饰编号1/2/3只在规则内部；不是正式物品表ID。未完成工件只预留材料，取消释放预留；已完成材料不退。

