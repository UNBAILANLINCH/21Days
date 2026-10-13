---
type: module-guide
module: shopplaytest
layer: runtime
maturity: seed
---

# 五店主场景接线

空间口径沿用[执行文档](../../../../PRP/independent-shop-mechanics/main-scene-execution-plan.md)的同图五店和现成2.5D小人，无店内场景。2026-10-14 将 SampleScene 灰盒街面替换为 `Art/fbx/总场景.glb` 占位地图；各店可分别体验，不加五店主线前置。

原“四房间独立试玩”方案已废弃。旧资产为避免破坏本地内容暂保留，三个场景地址加Legacy前缀，正常入口不使用它们。

## 入口和作者位置

正常Boot → 标题开始/继续 → MonsterEncounterState → IsometricEncounter（当前SampleScene）。
五店在SampleScene的FiveShops下预摆；正式地图替换时重绑入口、点位和角色引用，不重写规则。

| 店铺 | 角色 | 操作 |
| --- | --- | --- |
| 戏班 | 学徒、都知、乐正、巡逻学徒 | 验身、换作者路线、现有四轨下落演奏 |
| 衣肆 | 顾客甲/乙/丙、裁衣匠 | 调查可回看的证词、选择取衣人、只领取一次 |
| 金银行 | 官证、金银匠 | 拨料、四点位打造、取消、提交首饰并保留钥匙 |
| 客栈 | 掌柜 | 现有Dialogue入口；1001为明确的占位对白，正式线索待替换 |
| 酒肆 | 酒客、BOSS、普通客人甲/乙 | 饮酒、近距离分别灌酒、现有战斗、离区清零 |

截图五处建筑的临时对应（用户已确认按此占位）：右侧大建筑＝戏班（`尖叶.1868`），左侧小楼＝衣肆（`立方体.310`），左上小楼＝金银行（`桌子.008`），左下大楼＝客栈（`立方体.339`），中央楼＝酒肆（`立方体.261`）。

场景根 `Environment_FiveShopsPlaceholder` 保留模型实例，缩放 0.025、Y 轴朝向 180°；人物、出生点、交互点、路线和碰撞体共同绕地图根旋转，人物和店名保持原相机朝向，相机随新出生点平移。回放路线使用模型局部坐标，随地图朝向同步。建筑和挡墙用 Obstacle 层 MeshCollider。装饰地面有裂缝，原 Ground 网格碰撞停用，街面改用 `WalkSurface_*` 不可见 Ground 平面。客栈、戏班在下层，经西侧楼梯连接；`LowerStairs_TopJoin/WalkSurface/BottomJoin` 不可见坡面补齐台阶与两端接缝。上层沿用 `UpperGrass_WalkSurface` 与 `UpperStairs_WalkSurface/TopJoin` 连通；平面和坡面是占位碰撞近似，正式场景碰撞待美术交付。原 `Environment_Graybox`、五店 Cube 建筑和方块柜台停用；五店人物、活动区、待机点、巡逻点和操作点改为新地图点位，原稳定 ID 与玩法引用保留。原场景其它交互点也迁到新街面／上层平台，出生点避开演出触发区；原敌对巡逻移至上层平台，避开五店必经街道。

导入模型的白色、黑色和粉色材质缺失仍保留，未完成正式贴图；模型中的远离地图孤立节点 `gudaijishi_232` 在实例中停用。原模型资产不改写。

## 怎么操作

1. 从Boot点开始，沿街面走到店前活动区。建筑一直显示，当前店人物和局部操作物按组显示。
2. 走到店前InteractionPoint，统一交互键E/F打开局部操作面板。
3. 走近对应小人，用借身份按钮或统一交互借用；输入排到既有RequestControl，在逻辑tick生效。
4. 当前小人实际移动，相机与交互锚点跟随它。本体不移动到小人的位置。
5. 在该店操作点附近使用对应按钮。错误职业、距离不足或忙时拒绝。
6. 离店关闭局部面板；当前控制的小人仍显示和移动，其它店人物隐藏。可用回本体按钮归还控制。

局部面板不提供选店传送，不关闭原Quest/Exploration HUD。UI为可替换占位，素材和音效按已有事件后接。

开发版本显示“重试本店（调试）”：仅重置当前店规则和必要NPC/路线，不传送当前人物、不重置其它店。它会修改当前槽的本店进度；酒肆已写的剧情标记和战斗掉落保留，不能当作正式重玩/回滚入口。

## 预摆资产

| 资产 | 用途 |
| --- | --- |
| Prefabs/ShopPlaytest/Main_0..4.prefab | 五店建筑、活动区、人物、操作点和巡逻路线 |
| Prefabs/Characters/Chibi_*.prefab | 已有小人素材，嵌入各店人物预制体 |
| Prefabs/UI/ShopPlaytestView.prefab | 局部操作和结果；Addressables地址仍为ShopPlaytestView |
| Data/ShopPlaytest/Clothing/Forging/Tavern/Theater.asset | 已确认的临时规则配置 |
| Data/Identity/IdentityConfig.asset | 保留原定义，追加五店职业 |
| Scenes/Boot.unity | 注册四店Controller与既有Rhythm，关闭Rhythm自动Demo入口 |

上表资产前缀为Assets/_Project/；主灰盒在Assets/Scenes/SampleScene.unity。
人物、建筑和物品由编辑器预摆，进入店铺不会Instantiate。`Main_0..4.prefab` 保留原模板，新地图特定点位记录在 SampleScene 实例；重跑旧灰盒生成或整理菜单不会维护新地图布局。

## 运行时责任

| 类型 | 责任 |
| --- | --- |
| ShopPlaytestInstaller | 注册组合接线与四Controller；复用同一Identity状态，无新入口/身份副本 |
| ShopPlaytestState | 在现有遭遇绑定/解绑，转换局部交互意图、活动结果、存档进度 |
| ShopPlaytestWorld | 五店活动区/操作点/人物引用，决定活动店和显示 |
| ShopPlaytestActor | 职业、稳定ID、统一交互与缓存Renderer显隐 |
| ShopPlaytestView | 预制按钮抛事件，不注入服务或修改规则 |
| ShopProgressSaveData | 当前槽五店进度，含未完成工件和按ID醉酒 |
| EncounterStep/TamingRules | 原唯一角色移动/控制推进；附带友善分类、倍率和资格 |

组合接线不继承SceneGameState，不创建地图，不作为第二个移动驱动。
MonsterEncounterState可选解析它，实际主地图存在ShopPlaytestWorld才绑定。
跨模块依赖走公开方法和事件，Runtime不引用Editor/Tests。

## 分组显隐的边界

- 活动区和新地图建筑常驻；人物/名字/局部道具只切缓存的Renderer显示。
- 不SetActive整店或普通隐藏人物根节点，避免TamingActor禁用事件撤销控制。
- 进入/离开有边界缓冲；同区保留，重叠时选最近入口并按固定顺序打平。
- 隐藏人物不能参与局部交互/借用；当前控制人物是显示例外。
- 巡逻和其它必要规则仍由既有推进者运行。Hierarchy分组不等同逻辑或内存优化。
- BOSS明确击败时禁用该演员，是玩法退场；与普通显隐不同。

## 控制、身份和碰撞

店铺人物追加到原Taming列表，原巡逻者A/B及顺序保留。新增角色有稳定ID和AddedForShops标记。
普通友善角色用Passive，继续原巡逻节奏但不警戒/攻击；酒肆三个观察NPC用ObservationOnly，保留真实感知/追近与醉酒倍率，不与玩家结算探索伤害。战斗只通过已有回合制挑战触发，原巡逻者不受该标记影响。
角色根上的不可见逻辑纸片仍供原投影/翻转使用，外观来自现成小人。
受控角色复用玩家同一白盒扫掠碰撞；不另加移动循环。
职业跟随真实控制对象写同一IdentityState，时限/冷却由既有固定tick事件推进。
离场只释放当前本模块借用的身份，保留外部身份和共享账簿。

InteractionActor提供可切换Anchor；Registry通知原读方重新取锚点，Dialogue等测距随实际人物。
原玩家对象和纸片不被搬到借用人物位置。

## 音游和战斗

戏班用Rhythm.OpenActivityAsync留在原场景内，不GoTo替换世界状态。
真实驯服/控制、前置和职业都复验；结果回调下一帧排关闭。
固定谱、60%目标分沿现有四轨；低分、取消、技术中断都可继续操作。

酒肆用独立占位内容shop_boss_battle和目标shop_boss进入既有Narrative/BattleFlow/BattleArena；沿用1004对白及sample_boss战斗配置，结案不写原world.sampleboss.defeated标记，完成由本店进度保存。
BattleSetup醉酒来源同时核对sample_boss配置ID、当前叙事目标shop_boss与所属会话；实际值0也覆盖原配置占位50。原sample_boss_npc目标仍使用配置50，不读酒肆值。
灌酒必须检查对应现场人物可用、可见、存活且近距离；客人甲乙互不串值。
NPC移动倍率与感知倍率分别由Tavern规则提供，运行时不改SO。

## 保存和清理

每次Bind重置本地Controller，再读当前槽ShopProgressSaveData；旧档无本分区按作者初态。
衣肆调查/领取、打造额度/工件/成果、戏班前置/结果及酒肆场外值分别保存。
改变后同步内存分区，实际落盘仍由GameSession的正常闸门完成。

SessionStarted事件递增会话代数。现场Persist只在所属代数仍有效时写当前分区，
防止切槽后旧Unbind把旧进度写进新槽；不缓存旧分区读取数据。
GameSession在整个新局/切槽期间拒绝旧现场的即时捕获和自动落盘，切换完成才放开保存；写盘在途拒绝切槽。异常/取消也释放守卫，正常离场保存不受影响。
遭遇旧档允许仅缺标记为新增店铺的角色；未知/重复ID和原巡逻者缺失仍拒绝。

异步关闭失败也执行退订、倍率和锚点解绑、身份释放及step.End。
场景绑定失败先清理已绑定的五店，再按原流程报告错误。
存档、资产和游戏总进度不会因局部显隐清空。

## 验证与限制

- 2026-10-14 朝向校正：地图整体转为 Y=180°，5 个入口与全部人物脚底自检通过；4 条关键回放在 `20261014-020843` 批次 4/4 通过、零运行时异常。未迁移校正前旧存档中的绝对坐标，试玩使用新局。
- 2026-10-14 新地图占位替换：四店现有 EditMode 规则 14/14 通过；客栈对白／切人出店、衣肆与金银行局部重试、戏班完整演奏返回、酒肆饮酒／战斗／离区清零四条关键回放分别通过，零运行时异常。报告位于 `Logs/verify/{shopplaytest,theater,tavern}/`，本次批次为 `20261014-004539`（衣肆／金银行）、`20261014-005229`（客栈／戏班）、`20261014-010201`（酒肆）。两处楼梯的双向胶囊扫掠自检通过；全景位于 `outputs/five-shops/replaced-scene.png`。未做完整旧灰盒用例回归、正式材质与性能验收；项目健康扫描仍报两份已有动态字体缓存过大。
- EditMode在四规则目录及Taming/Interaction/Monster中；主场景回放在四店Showcase和MainSceneShowcase。
- 回放走正常Boot和SampleScene，基类隔离SaveRoot，不覆盖真实槽。
- 当前相关EditMode497/497通过（含Narrative真实表和BattleSetup醉酒来源边界）；主场景3条、衣肆1条、打造1条、戏班2条、酒肆1条自动回放全部通过，原BOSS连续两场开战回归通过。完整结果以[实施记录](../../../../PRP/independent-shop-mechanics/main-implementation-report.md)为准，人工视觉仍待确认。
- 衣服/钥匙仍为本玩法成果，正式物品表ID、主线用途未确定，不冒充Loot交付。
- 客栈1001是占位对白；正式线索、音效/UI/场景美术仍待各工种交付。
- 无完整剥皮/材料消耗附身/怀疑惩罚/十二阶段推进；原设计未确认的后果不自行补定。
- 分组显示减少绘制对象，不能承诺CPU/GPU/内存收益；性能与人工视觉确认单独记录。
