# 五店独立玩法试玩设计

> 2026-10-13用户纠正：五店在现有3D主场景中以2.5D表现实现，无室内，切人在原场景进行。本文的独立房间/新试玩场景方案已被否定，不能继续按其空间接法开发。当前待讨论执行入口是[五店主场景接线执行文档](main-scene-execution-plan.md)；保留本页仅作历史追溯。

状态：用户已于2026年10月12日明确确认“按此设计开工”，进入实现。用户已要求分别实现独立玩法，明确不串五店主线；房间、人物、物品使用 Cube 占位与 TMP 名称，制作预制体并预先摆放。戏班先接现有下落式，光点后置。本设计不代表玩法代码、场景或预制体已经完成。

## 范围

四项独立可操作玩法：戏班身份与巡逻指派及下落式音游、衣肆顾客推理、金银行拨料与打造、酒肆场外醉酒及回合制战斗。各项能单独进入、完成、退出和重试，不要求完成其他店铺，不写十二阶段主线进度。客栈沿用现有对白能力，只保留占位交互示例。

房间、人物、物品和名称标记均预先制作成预制体并放入试玩场景。开始游戏、点击操作或重试时不新建这些对象，只更新已有对象的状态和表现。玩法界面沿框架已有加载机制使用预制体；下落式音符沿用现成绘制，未在本次重新制作表现系统。

## 玩家操作和临时规则

下列未在原文确定的细节仅用于独立试玩，写进可修改配置；不当作正式剧情和最终数值。

| 玩法 | 单独试玩时做什么 | 成功与失败 |
| --- | --- | --- |
| 戏班 | 借学徒身份核验，借都知身份把指定巡逻者改到另一条预设路线；借乐正身份进入现有四轨下落式 | 指派确实改变岗位占用；只有指定乐正身份可开始演奏；按所选现有曲谱通过线判断成功；中断、低分、错误资格能给反馈并重试 |
| 衣肆 | 三个占位顾客各有固定证词；玩家逐个借身份询问顾客/裁衣匠，选择正确取衣身份后领取衣服 | 证词给唯一解；错误身份不发衣服且可重试；正确身份只领取一次；重置后可重新调查 |
| 金银行 | 官证身份查看并修改拨料份额，金银匠身份花额度打造金钥匙与首饰，完成规定提交 | 额度不足或身份不符不能开工；暂以四点位固定顺序点击作为可验操作；错点只拒绝本次，不重复扣料；取消返还未完成工件所占额度；完成时扣料一次、产物一次；提交条件满足才可带出钥匙 |
| 酒肆 | 借顾客身份选择灌酒或被灌酒，观察玩家减速/禁止静步、怪物警戒范围变化；离开酒肆测试清零；按当前BOSS醉酒进入既有战斗 | 场外每次饮酒加10；初始0，玩家/BOSS上限100，小怪上限20作为临时口径；醉酒效果阈值和倍率可配；战内沿既有四档和饮酒规则；战败能重试，战斗退出恢复世界输入和原处 |

衣肆试玩证词暂用“订了蓝色衣服并交过订金”与另外两名未订衣服/陪同顾客形成唯一解。人物姓名、正式证词、钥匙用途和主线奖励不在本次确定。酒肆的20/100原文含义未明确，本次只把它作为可改上限；不把这个暂定解释写回原设计。

试玩借身份采用明确的近距离交互、显示当前身份并允许回本体，不增加无限远接管。消耗皮、完整记忆/库存继承、死亡惩罚与跨阶段保存仍待团队确认；独立试玩不先强加这些规则。重试清理本项试玩数据，不删除或覆盖玩家的正式存档。

## 验收标准

1. 四项玩法都能独立打开与重试，不读取其他店铺是否完成；客栈无新增专属系统。
2. 场景、人物、物品与名称标记均来自预摆的预制体；连续操作和重试不会增加这些对象的数量，名称用 TMP 可读显示。
3. 错误身份或当前操作不允许时有明确反馈，不能通过重复按键绕过；切回本体、关闭界面与退出时控制/输入正确恢复。
4. 戏班实际改变指定巡逻路线，乐正实际进入现有四轨音游；正常结束、未达标、中断与重试均回到可操作试玩状态，不跳回主线标题。
5. 衣肆可根据证词找到唯一取衣人；打造额度和产物不因重复完成/取消而复制；醉酒会实际影响场外操作与BOSS战的初始值，离区清理可检验。
6. 新增纯规则有正负对照测试，各项有可看的 Showcase 回放；编译、lint及相关测试通过后交付截图与报告供用户确认，不将自动检查等同于真人验收。

## 设计决定

1. **分别持有试玩状态与结果。** 戏班、衣肆、打造、酒肆各自维护本玩法的操作状态、成功结果和重试，不新建跨五店阶段机。共用的试玩入口只负责选择和退出。
2. **优先连接已有能力。** 身份与控制读取 Identity/Taming 的公开口；戏班复用 Rhythm 判定、曲谱、暂停和结果契约；打造复用 ItemCraftRules 的材料校验；酒肆复用 TurnBased/Battle 的战内规则。新增部分限于顾客推理、打造操作和场外醉酒及对应接线。
3. **根作用域注册。** 依既有 Sample 模板使用 GameplayInstaller 注册，保持 Core不引用玩法。独立试玩通过明确入口进入，现有正式新游戏/继续不被替换；世界保留、音游返回、战斗返回由各试玩入口负责。
4. **规则与界面分离。** 规则层为纯C#，动作经只读意图对象输入。数值、顾客证词、身份权限、打造点位和配方进配置资产，运行时不修改SO；界面只发操作事件。
5. **预先制作占位资产。** 可复用 Cube＋TMP 角色、物件与房间预制体在编辑器制作；玩法有显式对象/点位引用，不在Update里搜索或构造对象。临时界面同样做成预制体，运行时不拼整套界面。
6. **隔离验证与主线。** 在 SampleScene 预摆独立示例作为接法模板，提供各项单独入口与回放；真正使用时可将本项预制体和配置放到正式地点。新增验证使用隔离存档，避免写入玩家曲库、校准或主线进度。

## 预计触及的文件范围

下列为设计文件清单，尚不是任务拆解。实现前仍须按各模块guide、external-api和extension-guide核对具体公开接法；能在既有文件扩展的，不重复造同职责服务。

| 部分 | 新建或扩展范围 |
| --- | --- |
| 共用试玩入口 | `Assets/_Project/Scripts/Runtime/ShopPlaytest/ShopPlaytestInstaller.cs`、`ShopPlaytestState.cs`、`ShopPlaytestView.cs`；必要的明确场景引用组件放同目录 |
| 戏班 | `Assets/_Project/Scripts/Runtime/Theater/TheaterConfig.cs`、`TheaterController.cs`、`TheaterEntryPermission.cs`；按需扩展 `Runtime/Rhythm/RhythmState.cs` 的外部返回与生命周期入口，不重写音符规则 |
| 衣肆 | `Assets/_Project/Scripts/Runtime/Clothing/ClothingConfig.cs`、`ClothingRules.cs`、`ClothingIntent.cs`、`ClothingController.cs` |
| 打造 | `Assets/_Project/Scripts/Runtime/Forging/ForgingConfig.cs`、`ForgingRules.cs`、`ForgingIntent.cs`、`ForgingController.cs` |
| 酒肆 | `Assets/_Project/Scripts/Runtime/Tavern/TavernConfig.cs`、`TavernDrunkRules.cs`、`TavernIntent.cs`、`TavernController.cs`；按需扩展 `Runtime/Battle/BattleSetup.cs` 接收真实场外醉酒快照，不能运行时修改 BOSS配置资产 |
| 规则单测 | `Assets/_Project/Scripts/Tests/EditMode/Theater/TheaterRulesTests.cs`、`Clothing/ClothingRulesTests.cs`、`Forging/ForgingRulesTests.cs`、`Tavern/TavernDrunkRulesTests.cs`；涉及已有契约的测试同步补负对照 |
| Showcase | `Assets/_Project/Scripts/Tests/Showcase/Theater/TheaterShowcase.cs`、`Clothing/ClothingShowcase.cs`、`Forging/ForgingShowcase.cs`、`Tavern/TavernShowcase.cs` |
| 占位资产 | `Assets/_Project/Prefabs/ShopPlaytest/` 的房间、角色、物品预制体；`Assets/_Project/Prefabs/UI/ShopPlaytestView.prefab` 与按需玩法面板；`Assets/_Project/Data/Theater/`、`Clothing/`、`Forging/`、`Tavern/` 配置资产 |
| 场景与生成工具 | `Assets/Scenes/SampleScene.unity` 的独立示例区；`Assets/_Project/Scenes/ShopPlaytest.unity` 的独立试玩场景；`Assets/_Project/Scripts/Editor/ShopPlaytest/ShopPlaytestBuilder.cs` 在编辑器生成与接线，不在运行时生成 |

新资产的`.meta`由Unity生成，场景/预制体由Unity MCP驱动编辑器创建和保存。编译、控制台与运行结果按实际调用报告。

## 当前准备状态

2026年10月12日通过项目已有的临时STDIO MCP桥读取到 `21Days` 的 Unity 2022.3.62f2，当前场景Boot，未播放、未编译、没有测试在跑。此为项目脚本建立的临时连接，未宣称 Codex 原生 Unity MCP 已连接。尚未修改游戏脚本与场景。

独立玩法的原始依据为 `docs/design/spotlight/02_阶段一设计文档.md`、`07_回合制作战文档.md`；现成接口以各模块三件套及源码为准。本次不串五店主线、不接十二阶段、不开发光点样式、不代替未交付正式美术与音乐。
