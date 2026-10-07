# 模块总览

> 给谁看：策划、美术看本目录下各模块的说明；程序看右侧「程序文档」列的三件套。
> 口径：**成熟度** 可用 = 规则与画面都能玩、原型 = 只有规则或只能在验证场景里玩、样板 = 给程序照抄的示例；**接入游戏** 已挂 Boot = 从标题「开始」进游戏就在跑、未挂 = 只在验证场景或纯规则、场景级 = 不经过 Boot，直接挂在场景物体上。

## 模块清单

| 模块 | 一句话 | 成熟度 | 接入游戏 | 策划 / 美术说明 | 程序文档 | 主要可调项 |
| --- | --- | --- | --- | --- | --- | --- |
| IsometricExploration | 2.5D 探索舞台：3D 灰盒 + 正对镜头的纸片角色 + 斜俯视跟随镜头，潜行、战斗、对话都在这张场景上演 | 原型 | 场景级（遭遇已接） | [isometricexploration.md](isometricexploration.md) | [isometricexploration-module-guide.md](../../ai-docs/docs/modules/isometricexploration/isometricexploration-module-guide.md) | SO `Data/IsometricExploration/IsometricExplorationConfig.asset`、美术目录 |
| Player | 玩家角色的规则：走、跑、潜行、伪装、攻击、受伤、死亡 | 可用 | 已挂 Boot | [player.md](player.md) | [player-module-guide.md](../../ai-docs/docs/modules/player/player-module-guide.md) | SO `Data/Player/PlayerConfig.asset` |
| Monster | 巡逻敌人：视野扇形感知、警戒、敌对追击、攻击；同时管理遭遇场景的进出 | 可用 | 已挂 Boot | [monster.md](monster.md) | [monster-module-guide.md](../../ai-docs/docs/modules/monster/monster-module-guide.md) | SO `Data/Monster/MonsterConfig.asset`，巡逻点在场景里摆 |
| Disguise | 伪装期间敌人一律不攻击玩家，只有开 / 关一条规则 | 可用 | 未挂（规则随 Monster 生效） | [disguise.md](disguise.md) | [disguise-module-guide.md](../../ai-docs/docs/modules/disguise/disguise-module-guide.md) | 无 |
| Taming | 按键驯服敌人并在玩家与敌人之间切换操控和镜头 | 原型 | 已接 Boot 遭遇（多目标控制） | [taming.md](taming.md) | [taming-module-guide.md](../../ai-docs/docs/modules/taming/taming-module-guide.md) | 无（借用 Player / Monster 的 SO） |
| CharacterPuppet | 序列帧 Q 版小人，按位移演待机 / 走路并左右翻面，不参与玩法 | 可用（美术占位） | 场景级 | [characterpuppet.md](characterpuppet.md) | [characterpuppet-module-guide.md](../../ai-docs/docs/modules/characterpuppet/characterpuppet-module-guide.md) | SO `Data/CharacterPuppet/ChibiPuppetConfig.asset`、预制体 `Prefabs/Characters/Chibi_<名字>.prefab`、帧目录 `Art/Sprites/Characters/<名字>/` |
| Dialogue | 走近 NPC 拉起对话：世界时停、立绘、条件选项、自动 / 倍速 / 跳过 / 历史；路人只冒头顶闲话气泡 | 可用 | 已挂 Boot | [dialogue.md](dialogue.md) | [dialogue-module-guide.md](../../ai-docs/docs/modules/dialogue/dialogue-module-guide.md) | 表 `Tables/Data/dialogue/`、`dialogue_character.json`，SO `Data/Dialogue/DialogueConfig.asset` |
| Performance | 剧情节点插一段短演出：小人站在场景里对话（世界舞台）、字幕、停顿确认、长按跳过，播完回到探索或对白，默认只播一次 | 可用（示例演出为世界舞台 `perf_sample_scene_talk`） | 已挂 Boot | [performance.md](performance.md) | [performance-module-guide.md](../../ai-docs/docs/modules/performance/performance-module-guide.md) | SO `Data/Performance/PerformanceConfig.asset`，演出预制体 `Prefabs/Performance/*.prefab` + 时间轴 `Data/Performance/Timelines/`，演出编辑器菜单 `21Days/演出/演出编辑器` |
| Quest | 主线 / 支线按前置自动接取、目标按顺序推进；任务栏、面板、追踪、头顶标记与画面边缘箭头 | 可用 | 已挂 Boot | [quest.md](quest.md) | [quest-module-guide.md](../../ai-docs/docs/modules/quest/quest-module-guide.md) | 表 `Tables/Data/quest/`（编辑器 `21Days/策划/任务编辑器`），SO `Data/Quest/QuestConfig.asset` |
| Narrative | 事件驱动的剧情阶段迁移、对白条件与遭遇仲裁 | 原型 | 已挂 Boot | [narrative.md](narrative.md) | [narrative-module-guide.md](../../ai-docs/docs/modules/narrative/narrative-module-guide.md) | 无 |
| Sample | 端到端跑通框架每一层的样板模块，给程序照抄用 | 样板 | 未挂 | 无（策划不用管） | [sample-module-guide.md](../../ai-docs/docs/modules/sample/sample-module-guide.md) | SO `Data/Sample/SampleConfig.asset` |

| Rhythm | DFJK 四轨三曲单谱、校准、个人纪录与暂停 | 原型 | 独立 RhythmDemo 场景 | — | [rhythm-module-guide.md](../../ai-docs/docs/modules/rhythm/rhythm-module-guide.md) | Rhythm 配置与曲库 |
| LailaFace | 31 形态、17 区捏脸与抓点反馈 | 原型 | laila 与独立研究试玩 | — | [lailaface-module-guide.md](../../ai-docs/docs/modules/lailaface/lailaface-module-guide.md) | 控制区拖拽参数、材质 |
| LailaFaceRecognition | 17 输入轴的五类识别与研究对照 | 原型 | 独立研究试玩；正式关卡未接 | — | [lailafacerecognition-module-guide.md](../../ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md) | 模型 / 元数据配对、展示反馈配置 |
| Loot | 物资箱拾取、奖励与持久背包 | 原型 | 已挂 Boot | — | [loot-module-guide.md](../../ai-docs/docs/modules/loot/loot-module-guide.md) | item 表与物资箱 |
| Inventory | 背包白盒与物品筛选 | 原型 | 已挂 Boot | — | [inventory-module-guide.md](../../ai-docs/docs/modules/inventory/inventory-module-guide.md) | item 表与 UI |
| Session | 新游戏、继续、槽位与稳定保存 | 原型 | 已挂 Boot | — | [session-module-guide.md](../../ai-docs/docs/modules/session/session-module-guide.md) | 槽位 / 自动保存配置 |
| Mirror | 照镜辨形与镜裂 demo | 原型（冻结） | 已有接线；随旧版策划冻结 | — | [mirror-module-guide.md](../../ai-docs/docs/modules/mirror/mirror-module-guide.md) | MirrorConfig |
| Identity | 借用身份、露馅、账簿与怀疑度 | 原型 | 已挂 Boot；遭遇禁攻已接 | — | [identity-module-guide.md](../../ai-docs/docs/modules/identity/identity-module-guide.md) | IdentityConfig |
| Stealth | 遮挡、绕背、击倒、追逐与处决规则 | 原型 | 已挂 Boot；遭遇接线 | — | [stealth-module-guide.md](../../ai-docs/docs/modules/stealth/stealth-module-guide.md) | StealthConfig、怪物种类表 |
| World | 两界灰盒、传送、出生点与相机约束 | 原型 | 已挂 Boot；初始进入 / 读档与回放待收尾 | — | [world-module-guide.md](../../ai-docs/docs/modules/world/world-module-guide.md) | scene / region / portal 表、场景锚点 |
| TurnBased | 回合制 BOSS 战规则与醉酒四档 | 原型 | 白盒回放；正式入口 / UI 未接 | — | [turnbased-module-guide.md](../../ai-docs/docs/modules/turnbased/turnbased-module-guide.md) | TurnBasedConfig |

接入状态按当前源码和 Boot / 场景引用核对；已注册不等于功能已完成验收。验证范围和剩余工作见 [HANDOVER](../../HANDOVER.md)，策划原件见 [设计索引](../design/README.md)。

## 场景与界面状态

| 场景 / 界面 | 性质 | 说明 |
| --- | --- | --- |
| `Assets/_Project/Scenes/Boot.unity` + `TitleView` | **正式（当前唯一）** | 标题 / 登录页：功能正式（开始游戏 / 设置 / 退出游戏 / 版本号），美术占位，替换清单见[美术手册 6.10](../artist-guide.md) |
| `Assets/Scenes/SampleScene.unity`（Addressables 地址 `IsometricEncounter`） | 非正式 | 功能 demo 示例场景 + 模块回放舞台；`IsometricEncounter` 临时指向，正式内容将指向 `Assets/_Project/Scenes/` 下新场景 |
| `Assets/_Project/Scenes/MonsterEncounter.unity` | 非正式 | 早期遭遇原型；`Sample.unity` 已于 2026-09-28 删除 |

## 怎么维护

- 新模块落地时在本表补一行，并照 [player.md](player.md) 的七节骨架写一份策划说明；模块规则或可调项变了要同步改对应文件。
- 程序三件套由 `/generate-doc` 从源码同步；本目录的策划说明目前手工维护，改完代码记得回来对一遍第 3、4 节。
- 改数值、改表的正规流程不在这里写，见 [策划手册](../designer-guide.md)；美术资源规格见 [美术手册](../artist-guide.md)。
- 「还没做的」以 [roadmap](../roadmap.md) 第 3 节差距矩阵为准，各模块文件第 6 节引用的行编号（A1、B2……）都指向那里；旧基线与当前代码冲突时先核对模块指南和 HANDOVER。
