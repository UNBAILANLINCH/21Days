# 五店主场景实施记录

日期：2026-10-13。采用用户已批准的同图独立玩法方案，按new-feature与ponytail执行。正式地图和五店3D资产到位后替换外观与点位，不重新开发玩法。

## 已落地的入口

正常Boot → 标题开始/继续 → MonsterEncounterState → IsometricEncounter（Assets/Scenes/SampleScene.unity）。
FiveShops下预摆五个店铺预制体，建筑为Cube，演员使用项目已有Chibi素材与TMP名字。没有创建新场景；建筑一直显示，当前店演员/操作物按组显示，当前控制人物是出店显示例外。

进入店前范围后，靠近操作点按既有交互键，走近对应演员借身份；借来的小人由原Taming/EncounterStep移动，相机与交互锚点跟随。各店无跨店主线前置。

| 地点 | 可操作内容 | 当前内容边界 |
| --- | --- | --- |
| 戏班 | 学徒验身 → 都知换巡逻路线 → 乐正打开现有四轨下落谱；达标/低分/退出返回原街面 | 固定谱与60%目标分；光点表现后置 |
| 衣肆 | 三顾客证词调查与回看，借正确取衣人领取一次，错误/重复拒绝 | 现有配置唯一解为顾客乙，成果为本玩法标记 |
| 金银行 | 官证额度2→8；金银匠打造钥匙/首饰、四点位、取消释放预留、提交首饰保留钥匙 | 本地材料与成果，不冒充正式Loot物品ID |
| 客栈 | 现场启动既有Dialogue，结束恢复原输入/世界 | 1001明确作为占位对白；正式线索待文策替换 |
| 酒肆 | 自饮、BOSS与客人甲乙分别灌酒，真实移动/潜行/感知倍率，既有Narrative/Battle开战与返回，离区清场外值 | 普通客人20/BOSS100为已确认配置上限，不改战内醉酒档位 |

开发版本另有“重试本店（调试）”，只重置当前店；保留其它店成果、当前控制及位置。酒肆重试不会回退剧情标记和已结算掉落，也会更新当前槽本店进度，不是正式回滚入口。

## 存档与生命周期修复

ShopProgressSaveData保存衣肆调查/领取、未完工打造及成果、戏班前置/结果、按ID场外醉酒和酒肆结案。每次现取当前分区，会话代数阻止旧现场Unbind覆盖新局/目标槽。

GameSession整段新游戏/继续持切换守卫，旧场景离场SaveNow不再抓进已切换分区；自动保存等新现场稳定，取消/失败释放守卫。写盘在途拒绝切槽。原遭遇快照兼容仅缺新增五店角色的旧档，未知/重复ID或原巡逻者缺失仍拒绝。

重绑视图尚未就绪时不推进店铺Tick。异步收场失败仍解绑订阅、醉酒来源、移动倍率、控制锚点和本模块借用身份。

酒肆三个观察NPC配置ObservationOnly：保留原感知/追近与醉酒倍率，双方不结算探索伤害；正式挑战仍调用既有回合制。原巡逻者默认行为保留。
酒肆使用独立占位内容shop_boss_battle，继续复用1004对白/sample_boss战斗配置；胜利不写原world.sampleboss.defeated，避免原示例BOSS跟着退场。配置源JSON经原生成脚本产出narrative_tbnarrativestory.bytes；生成C#没有内容变化。
醉酒来源还核对实际叙事目标与所属会话，同一sample_boss配置的原目标仍取50，本店合法0/100值独立。

## 现场修正

首轮实际回放暴露操作点与顾客过近、切槽绑定期间空引用。操作点与演员分开后统一焦点恢复；加就绪检查后切槽无异常。

原灰盒村口触发区会冻结世界，巡逻线附近长时间操作还会触发镜碎。占位操作点移到北侧安全街面z=7.6，酒肆演员避开原掩体与楼梯。保留原障碍、剧情触发和怪物规则；真实走位回放验证这些作者点位，不能用瞬移代替。

## 验证状态（持续更新）

| 检查 | 实际结果 | 证据 |
| --- | --- | --- |
| 相关EditMode规则/保存/观测者/开战醉酒/真实内容表 | 497/497通过，0失败/跳过 | MCP job 61419b37f91741209ba2f2fbeebab11a |
| 新槽隔离、从旧现场跨槽继续、读档后交互 | 通过，无异常 | Logs/verify/shopplaytest/20261013-164242/report.md |
| 局部重试保留其它店与控制 | 通过，无异常 | 同上 |
| 客栈对话、按店显隐、受控人物出店 | 通过，无异常；柜台缩小并移旁侧后复验通过 | Logs/verify/shopplaytest/20261013-172258/report.md；MCP job de163eb6cc9e4dd9ac4fa02afba079c8 |
| 衣肆回看/错答/唯一领取 | 1/1通过，无异常 | Logs/verify/clothing/20261013-164242/report.md |
| 打造额度/取消/钥匙/提交 | 1/1通过，无异常 | Logs/verify/forging/20261013-164242/report.md |
| 戏班完整四轨演奏、低分及主动退出 | 2/2通过，无异常 | Logs/verify/theater/20261013-164242/report.md |
| 酒肆分角色灌酒、移动、战斗100醉酒继承、胜利返回、原标记隔离、离区清零 | 1/1通过，无异常；截图确认本场胜利 | Logs/verify/tavern/20261013-170517/report.md |
| 原BOSS两次开战、醉酒50与战败后可重试 | 1/1通过，无异常 | MCP job 440e2dcaa60549c4ac6331ef3435562a；Logs/verify/battle/latest.md |
| 五店静态lint与资产meta | 通过 | 所有五店Runtime/Editor脚本lint，目录meta配套检查 |
| 模块级代码审查 | PASS，无剩余BLOCK | 独立只读复核；不替代Unity实际回放 |
| 资产体检 | 四项全过；scanned=True | 实际调用工程AssetAuditWindow扫描并读取完成结果；未手改资产序列化 |
| 最终编译/运行控制台与范围内lint/diff | 无错误；lint/diff通过 | 负对照测试日志与最终运行异常分开收集 |
| 全局gc | 剩余2项共享Dynamic字体缓存告警 | NotoSansSC约26872KB、SourceHanSerifCN-Bold约8228KB；没有清其它会话字体内容 |
| 测试副作用 | 无ProjectSettings改动、无InitTestScene残留 | 实际检查工作区 |
| 人工视觉/正式美术替换/性能测量 | 尚未验收 | 启用Renderer计数不能等同CPU/GPU/内存收益 |

自动回放按同版范围收集：五店相关8条均通过，额外原BOSS1条通过。最后一轮仅调整Default层柜台Cube外观与相对位置，操作点/人物/规则未改，补跑主场景交互与显隐1条通过；没有为这次外观修正再重复整套音游/战斗。所有关键截图已查看，最终位置、遮挡与手感仍请开发者确认。
正式地图若保留既有EncounterSceneView契约，可移植五店预制体并重绑；若改用WorldSceneDriver，当前只有玩家驱动，仍需另补组合接线与多角色控制，不能将两条场景通路宣称为自动互换。

## 待审改动归属

路径相对项目根；新Unity资产与.meta配套。工作区还有小地图、标题字体和其它会话改动，按内容归属，不整仓暂存/提交。本会话未提交或推送。

| 文件路径 | 位置 | 改了什么 |
| --- | --- | --- |
| Assets/_Project/Scripts/Runtime/ShopPlaytest/ | State/World/Actor/View/Installer/SaveData | 同图组合、按组显隐、现场权限、结果/保存、局部重试；停用独立入口 |
| Assets/_Project/Scripts/Runtime/Clothing/、Forging/、Tavern/、Theater/ | 规则/Controller/保存数据 | 各玩法闭环、回看/取消/按ID醉酒与合法快照恢复 |
| Assets/_Project/Scripts/Runtime/Taming/、Monster/ | 角色分类/控制/原驱动/场景绑定 | 友善与新增角色元数据、选择资格、倍率/路线、受控角色碰撞、异步清理；保留远端遮挡与结算改动 |
| Assets/_Project/Scripts/Runtime/Interaction/ | Actor/Registry/Presenter | 实际受控人物测距锚点与通知，不搬动本体 |
| Assets/_Project/Scripts/Runtime/Session/GameSession.cs | NewGame/Continue/Tick/SaveNow | 目标槽分区保护、重入/写盘在途守卫及finally释放 |
| Tables/Data/narrative/shop_boss_battle.json、Assets/_Project/Data/Config/narrative_tbnarrativestory.bytes | 酒肆占位内容 | 复用原战斗/对白节点并隔离结果标记，正式生成数据 |
| Assets/_Project/Scripts/Editor/ShopPlaytest/ShopPlaytestBuilder.cs | 预摆/整理/归档菜单 | 已有3D主街预制体点位与Boot接线，无新地图/运行时生成 |
| Assets/Scenes/SampleScene.unity、Assets/_Project/Scenes/Boot.unity | FiveShops/GameBootstrap | 五店预摆及正常入口注册 |
| Assets/_Project/Prefabs/ShopPlaytest/Main_0..4.prefab、Prefabs/UI/ShopPlaytestView.prefab、Data/ShopPlaytest/、Data/Identity/IdentityConfig.asset | 作者引用与配置 | Cube建筑、现成小人、局部面板与职业，保留原身份定义 |
| Assets/AddressableAssetsData/AssetGroups/Scenes.asset | 三个旧场景地址 | Legacy前缀归档；其它正式场景/UI地址保留 |
| Assets/_Project/Scripts/Tests/EditMode/及Tests/Showcase/ | 四店/显隐/锚点/旧档/Session/主场景 | 规则边界及正常Boot实际输入回放，存档隔离 |
| ai-docs/docs/modules/与PRP/independent-shop-mechanics/ | 模块接口/指南/执行与实施记录 | 同步当前接线、实际验证、旧壳保留和未确认内容；修正受本次行号漂移影响的Player/Mirror引用 |

埋点候选已复核：身份切换由Taming原事件记录；玩法意图由四Controller记录；组合层已有打开/拒绝/目标拒绝/异步耗时/离场/局部重试记录；查询布尔值、Dispose、作者配置throw与每帧守卫不重复埋。Session历史候选不扩大修补范围。
本次空间/控制纠正与保存边界已写进模块指南，未另增重复的全局经验规则。

拟提交信息仅供后续审查，尚无提交授权：

    fix(shopplaytest): 修正主街现场接线与跨槽保存边界
    - 五店使用现有3D地图、预摆小人与按组显示
    - 现场玩法复用原控制、下落音游和回合制
    - 防止旧场景离场覆盖新局或目标槽
    - 补齐正常Boot回放、独立重试和模块说明

旧场景广泛删除被自动审批拒绝：已改用保留文件/Legacy地址归档，正常入口不加载旧独立试玩。没有绕过审批删文件。
