---
type: external-api
module: shopplaytest
layer: runtime
maturity: seed
---

# 五店主场景公开接口

ShopPlaytestState由正常Boot根注册，绑定到既有MonsterEncounterState；不是GameFlow状态。

| 接口 | 契约 |
| --- | --- |
| BindAsync(world, existingView, ct) | 遭遇配置和角色模型就绪后绑定；缺引用失败，不生成地图 |
| UnbindAsync() | 离场清理；不转标题，不删除资产/存档 |
| BorrowRole(index) | 当前店作者角色顺序，检查真实距离，再排原RequestControl |
| ReturnBody() | 当前活动空闲时排回本体；不清所有驯服归属 |
| OpenLocalPanel() | 近当前店操作点才打开；统一交互调此入口 |
| ExecuteCommand(index) | 当前店预制命令映射，职业/距离/目标复验；拒绝不交成果 |
| Retry() | 仅开发版本、空闲且近操作点；重置当前店，保留其它店、控制及位置 |
| ActiveShop/CurrentRole | -1为街面；职业以当前真实控制和Identity生效为准 |
| World/ControlledPosition | 只读作者引用及当前受控人物逻辑位置，供验证 |
| Clothing/Forging/Tavern/Theater/Taming | 只读规则查询；其它模块不借此绕过意图写进度 |

ShopPlaytestWorld.ShopAt(position)确定活动区；Display(shop, ownerId)显隐；Station/NearStation供现场交互。
ShopPlaytestActor.SetVisible只改缓存Renderer；IsVisible与角色可用性不同，根节点保持激活。
ShopPlaytestView抛OnRole、OnCommand、OnBody、OnRetry；旧选店/回标题控件不作为入口。重试为明确标记的开发调试操作，会更新当前槽的本店进度；不回退剧情标记及已结算战斗掉落。

保存由组合接线写ShopProgressSaveData分区，实际落盘调用方仍经GameSession。
不得手改本分区来给玩家发物品，不得用旧独立场景键或选店传送接线。
