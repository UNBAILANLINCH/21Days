---
type: extension-guide
module: clothing
layer: runtime
maturity: seed
---

# 衣肆扩展

换顾客对白、裁衣匠分身份回应和答案改Clothing.asset；必须人工确认台词与唯一答案一致。新增顾客同时扩展主场景衣肆预制体角色、身份定义以及ShopPlaytestState的三顾客映射，补身份识别与重复领取测试。同步调整调查存档长度与旧档兼容，不新建房间或集中操作面板。借用不需要先对话；调查记录在裁衣匠对白完成时写入。

改规则后跑本模块EditMode；改场景、界面或回店流程后跑本模块Showcase。场景和预制体用Unity编辑器保存，不手写YAML/meta。不得运行时修改SO。

衣物是本玩法本地成果标记，没有写Loot背包或主线事实键。正式衣服用途、物品ID、跨阶段持有要在正式接入时确定。

