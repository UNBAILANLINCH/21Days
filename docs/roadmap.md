# 参考对标与补足路线图

> **状态：2026-09-26 基础快照；2026-09-29 同步 Narrative C1–C3/B3 与 W2；2026-10-05 按源码/资产/提交纠正 Loot、Inventory、Taming、UI 采用和音频旧状态，未重跑 Unity；2026-10-06 策划换为《聚光灯》：G 组冻结、新增 S 组，§0 / §1.4 / §2.2 / §2.4 / §3 B1·C5·E6·G·S / §5 W1·W2·W4·W5 / §6 / §7 已按聚光灯改。** 读者：全体开发者、策划、美术。未逐项复核的旧现状列仍作历史基线，不能作为重新派工依据；当前欠账与音游/Laila 进行中任务见 `HANDOVER.md`；本次冲突合并未重跑 Unity。
> 每收一波更新第 5 节的进度列；第 3 节差距矩阵某行做完就把状态改成「完成」，不删行。
> 要看「框架为什么这么设计」去 [`architecture.md`](architecture.md)，要看「怎么操作」去三份角色手册，
> 要看「当时刻意没做什么」去 [`history/`](history/)。这份只回答一件事：**对着参考，我们还差什么，按什么顺序补。**

参考示例由用户 2026-09-25 指定，是《明日方舟》SideStory「直到大地变成一颗酸橙」的两个 B 站视频：

| 视频 | 内容 | 对我们的意义 |
| --- | --- | --- |
| [BV1cQ3m6PE4j](https://www.bilibili.com/video/BV1cQ3m6PE4j/) | 「安洁莉娜的旅行小记」小活动全剧情录屏（32 分钟） | 探索小游戏本体 + 剧情演出的完整形态 |
| [BV1mQ4R6gEfj](https://www.bilibili.com/video/BV1mQ4R6gEfj/) | 同活动的 UI / 交互 / 动效归档（UP 主「UI归档·明日方舟」合集） | 界面结构、面板进出、按钮反馈、对话框动效 |

当前策划以《聚光灯》为准（2026-10-06 用户定；设计索引 [`design/features-spotlight/00_功能总览.md`](design/features-spotlight/00_功能总览.md)，飞书原文转写在 [`design/spotlight/`](design/spotlight/README.md)；旧版 `design/product/`、`story/`、`features/` 保留备查）：
**核心玩法是剥脸换皮 / 附身借身份、潜行暗杀、违反身份规则会暴露、追逐；场景是镜中妖界坊市与人间泾阳两张同构地图。**
这些参考里都没有，单列第 3 节 S 组。参考只用来对标「探索 + 剧情演出 + UI 动效」三层：学《明日方舟》的 2.5D 表现，不学塔防与养成体量（这句原出自旧版设计支柱，聚光灯未提及，沿用）；
活动里的关卡战斗、卡池、商店、材料掉率一律不进清单。
下文 [NN] 指 `design/features-spotlight/` 第 NN 篇，「00 §x」指其总览的第 x 节；sp02、sp03、sp04 等出处简称同总览页首。

---

## 0. 一页结论

**现状三句话。** 框架层完整（启动流、UI 四层栈、存档槽位与候选提交、Addressables、Luban、确定性内核与回放、埋点、世界暂停）；
探索场景表现方向已定并跑通（3D 灰盒 + 拼接小人 + 相机 / 光影 / 渲染分档）；对话与任务两个玩法闭环已接进 Boot 并通过回放验证。
**正式内容仍未完成。** Narrative 已接入 Boot，任务标记、条件选项及稳定状态存读档已跑通；标题、设置、暂停、槽位界面已有实现，加载黑幕实现已随本轮远端引入、仍待视觉验收。验证样例不代表正式章节内容验收；本次合并未重跑 Unity。

**最要紧的十件事**（详见第 3、5 节）：

1. 收尾：对话 / 任务两个模块的开发者视觉验收与提交推送，只有人能做（第 5 节 W0）。
2. Narrative 收尾：C1–C3/B3 最小接线已提交，三条回放已获人工确认；仍需最终提交独立编译与联合回归，证据见 `PRP/narrative-dialogue/tasks.md`。
3. 游戏级存档会话已有保存时机、候选读取、槽位界面与继续游戏；仍欠整体视觉验收和完整场景恢复事务，不能重建已有 E1。
4. 物资箱 / 最小背包与背包面板已提交；泛化可交互对象未做，Inventory Showcase 与视觉验收仍欠（A3、B1）。
5. 走 / 跑切换与沉浸模式（A1、A2）。参考里两个最显眼的 HUD 按钮。
6. 主菜单 / 设置 / 暂停 / 加载过渡四件系统 UI（E2–E5）。框架契约都在，缺的是面向玩家的复用件。
7. 对话演出动效：立绘入场与切换、对话框开合、全屏演出与插图节点（D1–D3）。
8. 多场景流转与传送（A4）。目前只有一张场景，任务点、NPC、存档都还没跨过场景边界。
9. 内容管线跑通到策划手里：剧本进表、校验器（F1、F5）；「三万字」目标出自旧版设计支柱，聚光灯未提及。
10. 聚光灯核心机制（第 3 节 S 组）：玩法定义已拆成 [`design/features-spotlight/`](design/features-spotlight/00_功能总览.md)（待策划确认），按 S 组开 PRP；开 PRP 前先请策划拍板 00 §8.1 的阻塞问题，首要是阶段一用哪套设计（sp02 五个附身场景 / sp03 坊市暗杀场景 / 拼）。这是参考里没有、聚光灯里必须有的部分；旧 G 组随旧版冻结。

---

## 1. 参考拆解

### 1.1 「旅行小记」探索层功能清单

来源：PRTS 与 BWIKI 活动页的官方玩法说明（原文见第 7 节），视频未逐帧拆解，形态细节标「待看视频」。

| # | 参考功能 | 参考里的形态 | 学不学 | 对应我们的模块 |
| --- | --- | --- | --- | --- |
| R1 | 移动 | 点击屏幕呼出虚拟摇杆；PC 用方向键 | 学 | Player / Input / Monster 的触屏控件 |
| R2 | 散步 / 奔跑切换 | 右下角按钮 | 学 | Player + Input + HUD |
| R3 | 沉浸模式 | 左下角按钮，隐藏所有 UI 和交互 | 学 | 新的 HUD 协调件 |
| R4 | 心愿任务便签 | 左上角便签看当前任务 | 学（任务系统已做） | Quest |
| R5 | 带标志的 NPC | 头顶标志表示「这里有故事可推进」 | 学 | Dialogue 标记 + Quest 联动 |
| R6 | 万向标 | 画面四周的方向指示，指向目标 | 学（任务系统已做） | Quest 屏外贴边箭头 |
| R7 | 物资箱 | 场景里散布，打开得报酬 | 学形式，奖励内容按聚光灯改（产出皮、面具、钥匙等物品，见 B1、S5） | 新：可交互对象 + 背包 |
| R8 | 探索任务 | 完成特定探索任务得报酬 | 学 | Quest（目标类型已有 TalkTo / ReachLocation / Counter） |
| R9 | 愿望清单 | 达成游玩进度领进度奖励 | 不学（活动运营件） | 无 |
| R10 | 故事进度重置 | 完成全部故事后左上角重置，报酬不重复 | 学「重开」不学「重复领奖」 | 存档会话 |
| R11 | 三章心愿故事 | 随主线关卡解锁（TO-5、TO-ST-3） | 学「分章解锁」，解锁条件改成剧情标记 | Narrative + Quest |
| R12 | 一张地图 | 「黄昏突进号」移动矿井平台，多个区域 | 学「一图多区域」，我们要多场景 | 场景流转 |
| R13 | 背包商店 | 材料兑换 | 不学 | 无 |

### 1.2 剧情演出层

参考的剧情走 AVG 形态：对白框 + 左右立绘 + 说话者名牌 + 自动 / 倍速 / 跳过 + 选项 + 历史记录 + 全屏 CG / 插图。
我们的对话系统二期已覆盖对白框、两槽立绘、自动 / 倍速 / 跳过（含确认）、右侧胶囊选项、历史面板、NPC 标记、范围交互按钮、常驻气泡。
**没做的**：立绘动效（入场、切换、说话者高亮）、对话框开合动效、全屏演出与卷轴插图节点、非阻塞旁白（字段留了，一律阻塞）、语音（旧版设计支柱明确不做配音；聚光灯未提及）。

### 1.3 UI 动效层（待看视频补细节）

第二个视频没有文字资料，下面是同类活动 UI 归档的常见项，**需要有人对着视频逐项确认形态与时长**，确认后回填本表：

| 项 | 要确认什么 | 我们现状 |
| --- | --- | --- |
| 面板进出 | 方向（滑入 / 缩放 / 淡入）、时长、是否带遮罩 | 只有 0.15 秒 CanvasGroup 淡入淡出（`Core/UI/UIView.cs`） |
| 按钮反馈 | 按压缩放、颜色、音效 | 无 |
| 对话框 | 开合方式、文字出现方式、说话者切换 | 打字机有，开合无 |
| 立绘 | 入场方向、切换表情是否有过渡、说话者高亮 | 直接换图 |
| 便签 / 任务栏 | 展开收起、新任务提示 | 任务栏常驻，无动效 |
| 万向标 | 屏内标记与屏外箭头的形态、距离数字 | 已做世界空间头顶标记 + 屏外贴边箭头（Quest 私有） |
| 沉浸模式 | UI 隐藏的过渡方式 | 无 |
| 走跑按钮 | 图标状态切换 | 无 |
| 场景切换 | 黑场 / 过渡图 / 加载提示 | 无，加载完直接切 |

### 1.4 与设计支柱的对照

本节原基于旧版设计支柱（[`design/product/1_核心概念与设计支柱.md`](design/product/1_核心概念与设计支柱.md)，旧版），2026-10-06 按聚光灯逐条复核（新旧对照见 00 §7）：聚光灯有明确说法的已改写，没说的保留旧版原文并注「聚光灯未提及」。

**参考没有、我们必须有**（旧版出处：支柱 1「辨认」与第 5 节「叙事与玩法的咬合」）：

| 旧版条目 | 聚光灯怎么说 | 复核结论 |
| --- | --- | --- |
| 照镜 / 辨形 | 镜不是核心交互，只在阶段九出现一次「随身镜识破水族」（[02] R31、[07] R51） | 不再是必须有；工程里的 Mirror 照镜 demo 冻结（2.2、2.4） |
| 佩戴面具演绎身份（画皮） | 换皮与附身：附身，或使用 / 持有皮、面具，借别人的身份，继承身份 / 物品 / 记忆（[01]、[05]） | 保留，改为 S1 / S5；Disguise（伪装禁攻）改造为身份状态，Taming（驯服切换控制）改造为附身的载体 |
| 收押妖灵 | 「收押」只是剧情结局（钱塘龙君收押带走）；户绝民「收服」、老吏「制服」也不是收押（00 §4.2） | 不做成玩法，旧 G3 冻结 |
| 归还 / 扣押的选择 | 聚光灯未提及 | 保留旧版原文备查，不进 S 组 |
| 镜面裂痕作为失败机制（「镜碎」页） | 本体挨打是击倒（[03] R3）；违反身份规则后「变回原主、死亡」或触发追逐（[02]，00 §5 C1、C5） | 由 S2 / S3 / S4 取代；镜碎页随 Mirror 冻结（E6） |
| 追逐 / 躲藏 / 弱点识破的轻对抗 | 潜行暗杀（避开视野、绕背处决）与追逐；「弱点」近似 BOSS 战的牙削弱（[03]、[04]、[09]） | 保留并扩展为 S3 / S4 / S7 |
| 三结局硬分支 | 大纲只有一个结局「结案」（[11] Q14、Q16） | 不做三结局，旧 G6 冻结 |

**参考有、我们不要**：旧版原文是「材料 / 货币奖励驱动的探索；支柱 3 说一切服务于信息，物资箱和任务奖励应该给信息而不是货币，B1 之前由策划拍板」。
按聚光灯改写：产出以物品为主，皮、面具、钥匙、一次性道具、文书（[05]），材料能合成；sp04 背包分类为「关键道具 / 材料 / 文书」。所以 B1「信息还是物品」已按聚光灯落到物品，类别扩展归 S5。货币、商店、兑换聚光灯未提及，仍不进清单。

---

## 2. 工程现状盘点

调研方式：源码与资产直接读取，`Boot.unity` 的 Installer 用脚本 guid 反查核实，测试数用 `[Test]` / `[TestCase` 计数。

### 2.1 框架层能力（`Assets/_Project/Scripts/Core/`）

| 子系统 | 有什么 | 对标探索叙事游戏还缺什么 |
| --- | --- | --- |
| UI（`Core/UI/`） | 四层 Hud / Panel / Popup / Top，各一个 Canvas；`OpenAsync` / `CloseAsync` / `CloseTopAsync` / `Get`；`UIView` 三段生命周期 + LitMotion 淡入淡出（两个虚方法可重写）；`UIConfig` 1920×1080、match 0.5、0.15 秒；安全区适配 | 通用确认弹窗、toast / 通知、暂停菜单、设置面板、加载黑场（Top 层至今没有任何 View）、通用屏幕边缘指示器、虚拟摇杆预制体 |
| 存档（`Core/Save/`） | `ISaveService`：`Get<T>` 分区、`SaveAsync/LoadAsync(slot)`、`ReadCandidateAsync` + `Capture` + `Commit` 两段式、`Exists/Delete`、独立档案 `Read/WriteProfileAsync`；原子写；分区版本迁移。现有分区 5 个：Settings、Dialogue、Quest、Encounter、Narrative | 没有任何游戏代码在存档时机上调 `SaveAsync`；没有槽位界面；没有元数据（时间戳、章节、时长） |
| 状态流（`Core/Flow/`） | `GameFlow` 串行切换；`BootState` → `TitleState` → 玩法状态；`SceneGameState` 基类 Additive 加载 / 卸载 | 无场景间传送、无加载过渡、无嵌套状态。标题「开始 / 继续 / 选择存档」现由 `Game.Session` 的 `SessionTitleRouter` 接管，跳 `MonsterEncounterState`（地址 `IsometricEncounter` = SampleScene，临时指向，正式内容落地后指向 `Assets/_Project/Scenes/` 下新场景）；`MonsterTitleRouter` 已删除 |
| 输入（`Core/Input/` + `Data/Input/GameInput.inputactions`） | Gameplay 图：Move / Confirm / Cancel / Pause / Sneak / Disguise / Tame / Attack；UI 图标准动作；`EnableMap/DisableMap` | Run / Interact / Journal / Immersive 动作与 Dialogue 图已加；`Pause` 由暂停菜单订阅；触屏摇杆与三键已随探索 HUD 落地，仅触屏平台显示；`EncounterTouchControls` 已删除 |
| 时间与暂停（`Core/Timing/`） | `IWorldPauseService.Acquire(owner)` 引用计数，同时冻结 `Time.timeScale` 与逻辑 tick；Dialogue、Quest 面板在用 | 没有「玩家主动暂停」的使用者 |
| 音频（`Core/Audio/`） | `PlaySfx / PlaySfxAsync / PlayBgmAsync / StopBgm`、三路音量写回 Settings 分区 | `Assets/_Project/Audio/` 零文件；无环境音层、无导入规则、Addressables 无音频条目 |
| 资源与配置 | Addressables：Scenes 组 `IsometricEncounter` / `MonsterEncounter`，UI 组 9 个面板 + 6 张对话图，Config 组一条；Luban 表 dialogue / dialogue_character / quest / item | `Sample.unity` 已删（2026-09-28，Sample 模块也未挂 Boot，见 2.5） |
| 事件（`Core/Events/`） | MessagePipe，`readonly struct`；核心 3 个，Quest 模块 4 个 | 各模块事件清单只核对了 Quest |
| 平台（`Core/Platform/`） | `Kind / SaveRoot / IsTouchPrimary / Vibrate` | 够用 |
| 确定性内核与回放（`Core/Simulation/`、`Core/Replay/`） | 固定步长、确定性随机、输入命令化、录制回放、漂移检测、编辑器回放窗口，已提交（`8595e7a`） | `PRP/replay/tasks.md` 进度表停在波 E，需补记 |
| 埋点（`Core/Telemetry/`） | 结构化埋点门面 + 多 Sink + Unity 日志桥 | 够用 |

### 2.2 玩法模块（`Assets/_Project/Scripts/Runtime/`）

| 模块 | 成熟度 | 挂 Boot | 场景 | EditMode 测试 | Showcase | 三件套 | 一句话状态 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Dialogue | stable | 是 | SampleScene | 33 | 有 | 有 | 二期完成；遭遇触发、存读档 UI、条件真实来源归 Narrative |
| Quest | seed | 是 | SampleScene | 50 | 有 | 有 | 主线 / 支线 / 追踪 / 指引 / 存档分区完成；奖励、通知、失败、已完成列表不做 |
| Monster | stable | 是 | SampleScene（Boot 真实流程）、MonsterEncounter（遗留原型，无人加载） | 18 | 有 | 有 | 巡逻 / 感知 / 警戒 / 追击 / 攻击；无视线遮挡、寻路、正式美术 |
| Player | stable | 是 | 无场景挂件；回放 SampleScene（Boot 真实流程） | 2 | 有 | 有 | 移动 / 潜行 / 伪装 / 攻击 / 受伤 / 死亡；无背包、装备、成长 |
| CharacterPuppet | stable | 不需要 | SampleScene（Boot 真实流程） | 9 | 有 | 有 | 拼接小人待机 / 走路，看位移演动画；无转身、奔跑、交互、战斗动画，Spine 待定 |
| IsometricExploration | stable | 不需要 | SampleScene（Boot 真实流程） | 19（另有共享用例在 Monster 测试里） | 有 | 有 | 纸片朝向、相机跟随、XY→XZ 投影；「场景表现原型，不是正式探索系统」 |
| Disguise | stable | 不需要 | SampleScene（Boot 真实流程） | 3 | 有 | 有 | 伪装期间敌人禁攻，纯静态规则 |
| Taming | seed | 是 | SampleScene（Boot 真实流程） | 历史计数未重数 | 有 | 有 | 多目标稳定身份、控制命令与存档/回放已接入；收押玩法随旧 G3 冻结，聚光灯附身规则归 S1，不能将已有接线重新列为待做 |
| Narrative | seed | 是 | SampleScene（Boot 真实流程，回放临时目标） | 22（2026-09-29 实跑通过） | 3 条 | 有，已登记 | 已接入 Boot（`Boot.unity` 挂 NarrativeInstaller），内容只有验证样例；C1–C3/B3 已提交；工作区回归及三条回放人工验收通过，最终提交独立验证待补 |
| Mirror | seed | 是 | SampleScene（`Yao_WellWoman`、`SpiritSightZone_Well`） | 140（2026-10-07 计数，未实跑） | 有 | 有 | **冻结（2026-10-06，随旧版策划）**：照镜辨形 / 通灵视 / 镜裂 / 镜碎 demo，代码与测试保留、Boot 仍挂 MirrorInstaller，不推进、不做视觉验收；玩家血量归零弹镜碎页、重开本场，S2「暴露 → 死亡」落地时替换或删除；见 2.4 去留表 |
| Sample | stable | **否** | 无 | 7 | 无（按设计） | 有 | 样板模块；Installer 未挂、场景地址未登记，已过期 |

### 2.3 场景、资产与内容规模

- **场景**：可玩场景只有 `Assets/Scenes/SampleScene.unity`（灰盒环境、玩家与巡逻者拼接小人、3 个 NPC、2 个任务点、6 个停用的室内纸片）。另有 `Boot`、`MonsterEncounter`（`Sample.unity` 与 `Verify/` 已于 2026-09-28 删除，回放统一在 SampleScene 上跑）；它同时是功能实现模板，正式场景接法与之对齐。
- **预制体**：12 个。UI 8 个（Title、Sample、Dialogue×4、Quest×2），世界 2 个（气泡、任务标记），角色 2 个（拼接小人玩家 / 巡逻者）。
- **美术**：拼接小人 5 张分件 + 2 段动画 + 1 个控制器；2 张整体 chibi 纸片；对话占位（2 角色 × 2 表情、气泡框、2 个选项图标、2 个标记）；灰盒材质 5 份；深度裁剪着色器 1 份；中文字体 1 套。**无环境模型、无怪物 / 妖灵、无 UI 皮肤、无镜面特效。**
- **音频**：已有三首音游 MP3 与对应曲谱；环境音层、正式内容 BGM/SFX 和完整音频管线仍待做，不再按“零资产”统计。
- **内容**：对话 2 棵树 12 个节点、2 个角色；任务 3 条；道具表 1 张。旧版目标是三万字剧本、序章 + 三章 + 三结局；聚光灯是序章 + 十二阶段（[11]），字数未提及。

### 2.4 PRP 与文档状态

| PRP | 状态 | 未完成项 |
| --- | --- | --- |
| dialogue-system | 代码与文档已提交推送 | 开发者视觉验收（`/verify-module Dialogue`）；SampleScene 变更随他人提交 |
| quest-system | 已按口头授权分三次提交并推送（22f3cc5 / c363891 / 7d02757，2026-09-26 核实在 origin/main） | 面板底板透底实机现象未定位 |
| narrative-dialogue | PRP 已于2026-09-29修订；C1–C3/B3 最小闭环及回放阅读停顿已提交，三条回放人工验收通过 | 最终提交独立验证；C5（S2 / S3 / S7）、完整目标中断/全场景恢复、逐节点恢复及真实章节未纳入本批，见 tasks.md |
| monster-ai | 代码、接线、文档完成 | tasks.md 无勾选格式；视觉与三端输入待人工确认 |
| replay | 代码已提交 | tasks.md 已于 2026-09-26 补记：T16 / T17 完成，T18 部分（体积为外推值、耗时预算未实测） |
| character-puppet | 只有 prp.md | 无 tasks.md |

**策划换版后的去留**（2026-10-06 用户定：《聚光灯》为当前策划，S 组见第 3 节）：

| 对象 | 去留 | 说明 |
| --- | --- | --- |
| Mirror 模块与 `PRP/mirror-core`（含 yao 表、道具 1005 / 1006、SampleScene 的 `Yao_WellWoman` 与 `SpiritSightZone_Well`、`MirrorConfig` 与 4 个 UI 预制体、`GameInput` 的 Mirror / MirrorSelf 动作） | 冻结 | 代码与测试保留、Boot 仍挂 MirrorInstaller；不推进、不做视觉验收；目前玩家血量归零时弹镜碎页、重开本场，S2「暴露 → 死亡」落地时替换或删除；阶段九「随身镜识破」可能复用 |
| Player / Monster（`PRP/monster-ai`，来源即 2026-09-17《26聚光灯——怪物状态与交互设计文档》） | 保留，按 S3 / S4 扩展 | 缺击倒、背后暗杀、视线遮挡、规则暴露；攻击与生命按 S3 重定 |
| Taming | 保留，改造为附身的载体（S1） | 已接 Boot、多目标稳定身份、控制命令与存档/回放；聚光灯附身规则尚未落地，不重复实现已有控制接线 |
| Disguise | 保留，改造为身份状态（S1 / S5） | 现为伪装期间不被攻击的开关 |
| Inventory / Loot | 保留，按 S5 扩展 | 加皮 / 面具类别、使用与合成、怪物掉落 |
| Dialogue、Quest、Narrative、Performance、Session、IsometricExploration、CharacterPuppet、Replay | 保留（通用） | — |
| laila 捏脸（`PRP/laila-expression-recognition`、`ML/expression-recognition`、Gameplay 模块、`laila.unity`、根目录 `.blend`） | 不动，待用户定 | 不是旧版衍生，聚光灯未提及 |
| `PRP/music`（画面打印机 × 节奏音游 spec） | 不动，待用户定 | 不是旧版衍生；与聚光灯音乐解谜（sp00 已改为圆圈光点）不是同一玩法 |
| `docs/design/product/`、`story/`、`features/` | 旧版保留备查 | — |

### 2.5 已核实的小问题（W0 顺手清）

1. **Sample 模块过期**：`SampleInstaller` 未挂 Boot，`SampleScene_Game` 地址未登记 Addressables。2026-09-26 已按「修」路线在 guide 与 Installer 注释写明现状与试跑步骤，代码保留为样板。
2. **标题路由已归位**：`TitleStartClickedEvent` / `TitleContinueClickedEvent` / `TitleLoadClickedEvent` 现由 `Game.Session` 的 `SessionTitleRouter` 统一接管（`MonsterTitleRouter` 已删除，Sample 未挂）。
3. **Narrative 三件套**：2026-09-26 已生成并登记（`ai-docs/docs/modules/narrative/`，maturity seed）。
4. **协作者遗留两条失败测试**：2026-09-26 已修。回放版本常量随 A1 升到 4 并同步测试；存档迁移丢失的根因是读档经快照克隆后 `Get<T>()` 拿到的不是迁移过的实例，已改为直接换入迁移后的实例并补候选路径用例。顺带根治了 UIService 打开失败留下的未观察 UniTask 异常。
5. **字体资产污染**：`Art/Fonts/Font_NotoSansSC_Regular SDF.asset` 在工作区反复变脏，提交前 Clear Dynamic Data（见 pitfalls）。
6. **SampleScene 命名残留**：巡逻者对象名 `enerme`、一个带前导空格的 `(Instance)` 根节点。
7. **任务面板底板透底**：实机看到、回放没复现，怀疑与测试运行器打开 Enter Play Mode Options 有关，未定位。

---

## 3. 差距矩阵

规模：S ≤ 1 天单点；M 2–4 天多文件；L ≥ 1 周跨模块，走 PRP。派单档位按 [`.claude/rules/model-routing.md`](../.claude/rules/model-routing.md)。
状态列：待做 / 进行中 / 完成；2026-10-06 起另有「冻结」（随旧版策划停止推进）与「玩法定义已拆，待策划确认」（S 组）。

### A. 探索层

| # | 缺口 | 现状 | 要做什么 | 归属 | 依赖 | 规模 | 派单 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A1 | 走 / 跑切换 | 只有普通与潜行两档速度（`PlayerConfig.MoveSpeed / SneakSpeed`） | Gameplay 图加 Sprint 动作（键鼠 / 手柄 / 触屏）；`PlayerConfig.RunSpeed`；`PlayerRules` 三档；`InputCommand` 按钮位；拼接小人 Speed 参数驱动步频；右下角 HUD 切换按钮 | Player + Input + CharacterPuppet + HUD | 无 | M | opus | 完成（2026-09-26，回放 v4）。PC 用左 Ctrl / 手柄左摇杆按下切换；HUD 走跑按钮延后到移动端移植阶段（用户 2026-09-26 定：PC 优先） |
| A2 | 沉浸模式 | 无 | 加「切换沉浸」动作与左下角按钮；Hud 层整体 CanvasGroup 显隐协调件（Core/UI）；世界空间标记 / 气泡 / 任务标记同步隐藏；对话拉起时自动退出 | Core/UI + Dialogue + Quest | 无 | M | opus | 完成（2026-09-26）待视觉验收；玩家 / 巡逻者 NameTag 未随沉浸隐藏 |
| A3 | 泛化可交互对象与物资箱 | 交互焦点、范围检测、HUD 按钮、头顶标记全是 Dialogue 私有（`DialogueInteractionActor / Focus / Marker / InteractHudView`） | 抽通用 `IInteractable` + 交互焦点到独立模块（或 Core），Dialogue 改为一种实现；新增物资箱：交互 → 发奖励事件 → 标记已开 → 存档分区 | 新模块 Interaction + Dialogue 重构 | B1 | L | opus，PRP | 待做 |
| A4 | 多场景流转 | 只有一张场景；`SceneGameState` 只支持整场景加载卸载 | 场景表（Luban）、传送点组件、玩家出生点选择、跨场景任务点与 NPC 状态、加载过渡（E4） | 新模块 World / Core/Flow | E1、E4 | L | opus，PRP | 待做 |
| A5 | 触屏摇杆正式 UI | Monster 模块代码现搭（`EncounterTouchControls`），三个按钮写死潜行 / 伪装 / 攻击 | 通用虚拟摇杆 + 动作按钮预制体（Hud 层），按 `IPlatformService.IsTouchPrimary` 显隐，`TouchVirtualStick` 绑定填实；替换掉代码现搭版 | Core/UI + Input | A1（按钮清单） | M | opus | **延后到移动端移植阶段**（用户 2026-09-26 定：PC 优先，不需要摇杆）。届时摇杆 + 走跑 / 潜行 / 伪装 / 攻击按钮放进 ExplorationHudView（RunSlot 已留），按 `IsTouchPrimary` 显隐；`EncounterTouchControls` 可删 |
| A6 | 相机边界与死区 | 只做位置缓动，无边界、前视、死区 | 场景边界体、跟随死区、进对话时的构图切换 | IsometricExploration | A4 | M | opus | 待做 |

### B. 任务系统留在门口的

| # | 缺口 | 现状 | 要做什么 | 归属 | 依赖 | 规模 | 派单 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| B1 | 奖励与背包 | 物资箱拾取已落地（`Runtime/Loot`，奖励写 `LootSaveData.Items`，`tbitem` 引用），但**没有背包界面**，拾取后看不到自己有什么 | 白盒背包（用户 2026-09-26 要求）：`Inventory` 面板（I / 手柄 RB）列出持有物：名字、数量、品质色，占位图标；读 `LootService.Items`，订阅 `CrateCollectedEvent` 刷新；`tbitem` 补描述 / 类别列；奖励语义按聚光灯：产出以物品为主（皮、面具、钥匙、一次性道具、文书），背包分类按 sp04「关键道具 / 材料 / 文书」；类别扩展归 S5 | 新模块 Inventory | 无 | M | opus | 白盒完成（2026-09-26）待视觉验收：`Runtime/Inventory/`，B / RB 开面板，筛选全部 / 物品 / 线索，`tbitem` 加 desc / category 两列并新增 1005 破旧信笺（线索）、1006 铜镜碎片（关键物）；无 Showcase；主窗口全量复核已补：EditMode 609 / 609、编译零错误（2026-09-26）；快捷键 B / RB |
| B2 | 接取 / 完成通知 | 任务只发 4 个事件，无表现 | Core 通用 toast（Top 层，队列，可被沉浸模式隐藏）；订阅 QuestActivated / QuestCompleted | Core/UI + Quest | 无 | S–M | opus | 完成（2026-09-26）待视觉验收；Top 层不随沉浸隐藏，新开局首条主线不弹 |
| B3 | 任务完成写剧情标记 | Narrative 读取持久任务完成状态，按配置写幂等标记，读档补齐 | 任务完成事件 → 剧情标记；对话选项条件读到任务结果 | Quest + Narrative | C2 | S | opus | 最小闭环已实现并提交；工作区回归与人工验收通过，最终提交独立验证待补 |
| B4 | 进度重置与已完成列表 | 不做 | 依赖存档会话的「新游戏」；已完成列表页 | Quest + E1 | E1 | S | sonnet | 待做 |

### C. 叙事接线（`PRP/narrative-dialogue/` T3–T9）

| # | 缺口 | 现状 | 要做什么 | 归属 | 依赖 | 规模 | 派单 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | NarrativeService + Installer | 已注册 Boot，稳定目标对白与身份校验、取消重试、稳定存读档已接通 | Condition / Dialogue / WaitAction / End 与遭遇仲裁；Battle 留给 C5（S2 / S3 / S7） | Narrative | C4 | L | opus，PRP | 最小闭环已实现并提交；三条回放人工验收通过，最终提交独立验证待补 |
| C2 | 剧情标记条件源 | NarrativeConditionSource 读取真实玩家状态、槽位标记与目标生命周期；未装 Installer 时保留占位来源 | 选项定时刷新与提交时复验；无来源的敌意/感知条件拒绝用于生产内容 | Narrative + Dialogue | C1 | M | opus | 已实现并提交；工作区回归与人工验收通过，最终提交独立验证待补 |
| C3 | 剧情内容进表与校验 | 已有三张 Narrative 表、Luban 输出及 Catalog 校验 | 剧情、遭遇、任务标记映射；引用、环路、冲突与未接入能力检查 | Narrative + Tables | C1 | M–L | opus | 最小内容管线已实现并提交；真实章节与完整内容校验工具不在本批，最终提交独立验证待补 |
| C4 | Narrative 三件套与登记 | 已生成 | `/generate-doc narrative`、`modules.json`、catalog 补行 | 文档 | 无 | S | sonnet | 完成（2026-09-26） |
| C5 | 战斗结果 → 剧情 | `EncounterStep.PendingResult` 有，消费方无 | **先按聚光灯 S2 / S3 / S7 重新审视**：本体打不过任何怪、挨打被击倒（S3），违反身份规则会暴露（S2），BOSS 有血量与多阶段（S7）；Battle 阶段的结果可能要能表达击倒、暴露、BOSS 转阶段 | Narrative + Monster | S2 / S3 / S7 定义 | M | opus | 待做 |

### D. 演出与 UI 动效

| # | 缺口 | 现状 | 要做什么 | 归属 | 依赖 | 规模 | 派单 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| D1 | 立绘动效 | 直接换图 | 入场 / 退场滑动、表情切换交叉淡化、说话者高亮与非说话者压暗 | Dialogue View | 待看视频 | M | opus | 完成（2026-09-26，待视觉验收） |
| D2 | 对话框动效 | 打字机有，开合无 | 开合动效、说话者名牌切换、文字节奏可配 | Dialogue View | 待看视频 | S–M | opus | 完成（2026-09-26，待视觉验收） |
| D3 | 全屏演出与插图 | 无 | `NodeKind` 加全屏 / 插图节点，表字段，全屏 View，卷轴滚动 | Dialogue + Tables | 美术给规格 | L | opus，PRP | 由 `PRP/performance-pipeline/` 覆盖：演出管线 + Timeline 编辑器已实现（2026-09-26，待视觉验收）；2026-09-28 起只保留世界舞台，全屏立绘演出与第三方模型适配层已按用户决定下架；对白节点插播走 `performance` 字段而非新 `NodeKind` |
| D4 | 面板过渡花样 | Fade / SlideUp / SlideDown / Scale 已实现 | 按面板选用预设 | Core/UI | 视觉规格 | S | sonnet | 已实现且已有采用：DialogueView.prefab transition=1（SlideUp）；本次未新增视觉验收 |
| D5 | 按钮反馈 | 通用反馈已实现 | 按压缩放 + 音效钩子组件 | Core/UI | F4 音效 | S | sonnet | 已挂 Title / Dialogue / Quest / Inventory / Pause / Settings / SaveSlots 等预制体；不再列未挂载，音效内容及视觉验收单列 |
| D6 | 角色动画补齐 | 待机 / 走路 | 转身、奔跑、交互动作；战斗表现定 Spine 后再议 | CharacterPuppet | A1、美术 | M | opus | 待做 |

### E. 系统与流程

| # | 缺口 | 现状 | 要做什么 | 归属 | 依赖 | 规模 | 派单 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| E1 | 游戏级存档会话 | 分区与两段式接口齐备，无调用者、无槽位 UI | **用户 2026-09-26 定**：不做节点式存档，**全状态记录、即存即用**；关键节点自动保存（任务状态变化、开箱、对白结束、场景切换、退出）；主界面可选存档槽（继续 / 新游戏 / 选槽）。实现：`GameSessionController` 在稳定边界（对话 Preparing / 剧情迁移 / 战斗 tick 未提交时不存）把全部分区写入当前槽，候选读取 → 校验 → Commit、失败回滚；槽位元数据（时间、章节、时长） | 新模块 Session（Runtime） | 无 | L | opus，PRP | 白盒完成（2026-09-26）待视觉验收，回放见 T6；已知体验问题：「已保存」提示走通知队列 FIFO，会把「获得物资」等玩法通知推后数秒，后续改成不进队列的角落小字 |
| E2 | 主菜单 | `TitleView` 只有「开始」 | 继续 / 新游戏 / **选择存档（槽位列表，显示时间与章节）** / 设置 / 退出；标题路由归 `Game.Session`（`MonsterTitleRouter` 已删除）；设置面板已可直接调 `SettingsController.OpenAsync()` | Core/UI Views 或新模块 | E1、E3 | M | opus | 继续 / 选择存档已加进 `TitleView`，路由归 Session；开始游戏 / 设置 / 退出游戏 + 版本号已做，`TitleView` 转正式（美术占位，替换清单见美术手册 6.10） |
| E3 | 设置面板 | `SettingsSaveData` 有音量 / 语言字段，无面板 | 音量三路、语言占位、按键提示；**PC 显示设置**：分辨率列表（取自显示器）、全屏 / 无边框 / 窗口化、垂直同步、帧率上限（见 E8）；写回并落盘 | Core/UI + Core/Save | E5（入口） | M | opus | 完成（2026-09-26）待视觉验收；设置改为跨槽位独立档案 `settings`（分区版本 2）；标题界面已有「设置」入口（2026-09-26） |
| E4 | 加载过渡 | 加载完直接切 | Top 层黑场 / 进度 View；**不用** `SceneGameState` 前后钩子（黑幕要跨两个状态、跨连续切换），改由 `GameFlow` 统一落 / 揭 | Core/UI + Core/Flow | 无 | S–M | opus | 已做（2026-10-06）：`ILoadingCurtain` + `LoadingCurtain` + `LoadingView`（Top 层常驻、自带排序压过同层、全屏黑底挡点击、盖住超 0.5 秒右下角出脉动圆点、unscaled 时间）；`GameFlow` 涉及场景时 Changing → 落幕 → Exit → Enter → Changed → 队列清空才揭幕，失败 / 取消也揭，黑幕自身出错只 Warn；盖住期间不开暂停菜单；埋 `core.flow/curtain_revealed`；时长进 `UIConfig`。待视觉验收；没有进度条（加载无进度来源） |
| E5 | 暂停菜单 | `Pause` 动作无人订阅 | 订阅 Pause → `IWorldPauseService.Acquire` → Popup（继续 / 设置 / 回主菜单） | Core/UI | E3 | S–M | opus | 完成（2026-09-26）待视觉验收；Esc 优先级：可关面板 → 关；对白 / 标题 → 无事；沉浸 → 退沉浸；否则开暂停菜单；P 只开不关 |
| E6 | 「镜碎」失败页 | 无 | 依赖镜裂机制定义 | G4 | G4 | S | sonnet | 冻结（随 Mirror，2026-10-06）（原：demo 完成（`PRP/mirror-core`），待视觉验收）；由 S2 的暴露 / 死亡流程取代，落地时替换或删除镜碎页 |
| E7 | Sample 模块去留 | 过期（2.5 第 1 条） | 删或修，二选一 | Sample | 无 | S | sonnet | 完成（文档说明现状，代码保留） |
| E8 | 分辨率基准与画面适配 | `UIConfig` 1920×1080、match 0.5；工程默认 1920×1080 独占全屏窗口、窗口不可拖拽 | **用户 2026-09-26 已定**：1080p / 16:9 基准；UI 改按高度匹配（match 1），21:9 两侧多看、16:10 两侧少看，UI 不缩放不裁切；最低 1280×720；窗口化可拖拽 + 无边框全屏；美术按 1080p 出图（PPU 100），4K 靠 SDF 与矢量 UI，需要时再补 2x 贴图。落点：`UIConfig.asset` match、`ProjectSettings` resizableWindow=1（改前按硬规则 3 说明）、artist-guide 7.1 / 7.2 措辞、设置面板分辨率项（并入 E3） | Core/UI + ProjectSettings + 美术规格 | E3 | S–M | opus | 完成（2026-09-26）：`UIConfig` match=1、`resizableWindow=1`、artist-guide 7.1 / 7.2 已改；`SetResolution` 与窗口拖拽未出包实测 |

### F. 内容与美术

| # | 缺口 | 现状 | 要做什么 | 归属 | 依赖 | 规模 | 派单 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F1 | 剧本进表 | 2 棵树 12 节点 | 策划按 `designer-guide.md` 第 4 章流程写 Excel / JSON；角色表扩容；本地化字段预留 | 策划 + Tables | C3 | 持续 | 人 | 待做 |
| F2 | 环境模型替换灰盒 | 灰盒 | Blender 低模 + 手绘贴图，模块化 Prefab，Lightmap；放 `Environment_Graybox` 同级替换（`artist-guide.md` 3.1） | 美术 | 无 | 持续 | 人 | 待做 |
| F3 | 角色 / 立绘 / UI 皮肤 | 全占位 | 立绘按角色表地址换图；对话框、气泡、标记、图标只换 Sprite；拼接小人分件按 3.2 规格 | 美术 | 无 | 持续 | 人 | 待做 |
| F4 | 音频 | 已有三首音游 MP3 与曲谱 | 正式内容 BGM / SFX、完整导入与资源管线、环境音层 | 程序 + 美术 | 无 | M | opus | 音游资产已保存；环境音/SFX及正式内容音频仍待做 |
| F5 | 内容校验器 | 对话有 Catalog 测试 | 表级校验命令：死链、缺资源地址、条件类型未接、任务前置环 | Tables + Editor | C3 | S–M | opus | 待做 |

### G. 自家机制（参考之外，支柱之内）（旧版，2026-10-06 冻结）

G 组对应旧版 [`design/features/`](design/features/)（照镜 / 收押 / 两界之账那一版），随旧版策划冻结，行保留备查、不再推进。聚光灯里只有 G2、G5 有对应（S1 / S5、S3 / S4），见 00 §7。

| # | 机制 | 现有雏形 | 第一步 | 状态 |
| --- | --- | --- | --- | --- |
| G1 | 照镜 / 辨形 | 无 | 策划写玩法定义（输入、反馈、失败、与潜行可见范围的关系） | 冻结（旧版策划）（原：demo 完成（`PRP/mirror-core`），待视觉验收） |
| G2 | 画皮面具 | Disguise（伪装禁攻） | 定义面具的获取、生效时间、冷却、被识破；决定是否在 Disguise 上扩展 | 冻结（旧版策划）（原：待定义；聚光灯对应 S1 / S5） |
| G3 | 收押妖灵 | Taming 已接 Boot、多目标身份、输入命令与存档/回放 | 正式收押定义随旧版冻结；保留已有控制接线，附身扩展归 S1 | 冻结（旧版策划）（原：待玩法定义） |
| G4 | 镜裂失败 | 无 | 定义裂痕计数、可见范围缩减、三次重开关卡；接「镜碎」页 | 冻结（旧版策划）（原：demo 完成（`PRP/mirror-core`），待视觉验收） |
| G5 | 追逐 / 躲藏 / 弱点识破 | Monster 感知 / 警戒 / 追击 | 定义无血条对抗的胜负条件；重审 Player / Monster 的攻击与生命字段 | 冻结（旧版策划）（原：待定义；聚光灯对应 S3 / S4） |
| G6 | 三结局硬分支 | Narrative 阶段机可承载 | 内容层的事，C1–C3 做完后由剧本驱动 | 冻结（旧版策划）（原：待内容） |

### S. 聚光灯核心机制

设计索引 [`design/features-spotlight/00_功能总览.md`](design/features-spotlight/00_功能总览.md)；各项现状以 00 §8.2「和现有工程的关系」为基础，并保留 2026-10-05 已核对的 Taming 与音游工程进度，开 PRP 顺序见第 5 节 W5。

| # | 机制 | 现有雏形 | 第一步 | 状态 |
| --- | --- | --- | --- | --- |
| S1 | 换皮与附身（[[01]](design/features-spotlight/01_换皮与附身.md)） | Taming 已接 Boot，支持多目标稳定身份、控制命令与存档/回放；聚光灯的附身条件与身份继承规则尚未落地；Disguise 只是「伪装期间不被攻击」开关，没有身份概念 | 策划确认 [01] 的阻塞问题（00 §8.1 第 1、2、5、11 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |
| S2 | 身份暴露与怀疑（[[02]](design/features-spotlight/02_身份暴露与怀疑.md)） | Monster 警戒值一层已有；规则层暴露、账簿、怀疑度、揭露都没有。Mirror 照镜 demo 随旧版冻结，目前玩家血量归零弹镜碎页、重开本场，本项「暴露 → 死亡」落地时替换或删除；阶段九「随身镜识破」可能复用它 | 策划确认 [02] 的阻塞问题（00 §8.1 第 3、9、11 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |
| S3 | 潜行与暗杀（[[03]](design/features-spotlight/03_潜行与暗杀.md)） | Player / Monster 本来就按聚光灯 2026-09-17 文档做：巡逻、75° 扇区红橙区、警戒、敌对追击、攻击、生命都在；缺击倒、背后暗杀、视线遮挡（看拍板）。Player 现有普通攻击能打死怪，与「本体打不过」冲突（00 §5 C4） | 策划确认 [03] 的阻塞问题（00 §8.1 第 1、4、5、6 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |
| S4 | 追逐（[[04]](design/features-spotlight/04_追逐.md)） | Monster 有敌对追击，但追击速度 2.5 低于玩家步行 3；没有寻路、召唤、集群巡逻队、脚本化的「固定追逐」 | 策划确认 [04] 的阻塞问题（00 §8.1 第 3、4 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |
| S5 | 皮、面具与道具（[[05]](design/features-spotlight/05_皮面具与道具.md)） | Inventory / Loot 有背包白盒和拾取，道具类别是 Material / Consumable / Clue / Key，没有皮和面具，也没有使用、合成；Loot 只认物资箱，怪物死亡不掉东西 | 策划确认 [05] 的阻塞问题（00 §8.1 第 2、10 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |
| S6 | 关卡专属机制（[[07]](design/features-spotlight/07_关卡专属机制.md)） | 现有模块没有任何一条；幻象要原样复用前三阶段的机制（[07] §8 约束 1），每条机制都要能挪到别的场景 | 策划确认 [07] 的阻塞问题（00 §8.1 第 1、6、9、11、12 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |
| S7 | BOSS 战（[[09]](design/features-spotlight/09_BOSS战.md)） | 现在只有「生命归零倒下」；Monster 说明写战斗形态可能重做（旧版 G5，现归 S3 / S7）、倾向无血条，与 sp03「血量归零时转换阶段」和 sp04 BOSS 血条方向相反 | 策划确认 [09] 的阻塞问题（00 §8.1 第 1、7 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |
| S8 | 小游戏（[[08]](design/features-spotlight/08_小游戏.md)） | 已有独立四轨 Rhythm 实现、三首 MP3 与对应曲谱，当前进度见 `HANDOVER.md` §1.8；这些不代表聚光灯小游戏或音乐解谜已接入，不能按“工程没有音游 / 零音频”重新派工（F4）；阶段一不用 sp02 套时可整项搁置 | 策划确认 [08] 的阻塞问题（00 §8.1 第 1、8 条）后 `/refine-prd` | 玩法定义已拆，待策划确认 |

三项支撑文档不单列 S 行：[[06]](design/features-spotlight/06_怪物分层.md) 怪物分层落成「怪物种类数据化」（Monster 种类表进 Luban，S3 / S4 / S7 都要按种类配数值、可否击杀、掉落；表结构可与拍板并行），[[10]](design/features-spotlight/10_两界与场景结构.md) 两界与场景结构并入 A4 / A6（坊市 / 泾阳两张同构地图）；
[[13]](design/features-spotlight/13_系统界面清单.md) 系统界面清单不单开，随各 S 项把对应界面行带进各自 PRP（加载过渡、地图、章节卡走 E4 与 A 组）。

### H. PC 适配（用户 2026-09-26 定：PC 优先，移动端移植后置；方案已批准）

按触屏思路做、在 PC 上不合适的地方，核实清单见本表；执行顺序 H1–H4 → H5 → H8–H9 → H6–H7、H10–H11。

| # | 缺口 | 现状 | 要做什么 | 归属 | 依赖 | 规模 | 派单 | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| H1 | 对白只能鼠标点击推进 | 推进 / 自动 / 倍速 / 跳过 / 历史 / 选项全靠点击，无键盘与手柄路径 | 新增 `Dialogue` 动作图：Advance（Space / Enter / 手柄 A）、Auto（A）、Speed（S）、Skip（Ctrl）、History（H）、Choice1–4（数字键）；对话中启用该图；选项显示时默认选中第一项、Confirm 选定；跳过确认默认选中「取消」；按钮上标键位 | Dialogue + Input | H0 动作表 | M | opus | 完成（2026-09-26）待视觉验收；键位提示只显示键盘串，数字键只选前 4 项 |
| H2 | 任务面板只有鼠标路径 | 只能点任务栏开、点「返回」关；Esc 无效 | `Gameplay/Journal`（Tab / 手柄 Select）开面板；Esc 走通用关闭（H3） | Quest + Input | H0、H3 | S–M | opus | 完成（2026-09-26）待视觉验收；Tab 只开不关，关闭走 Esc |
| H3 | 面板无默认选中项、Esc 不关面板 | 全工程无 `SetSelectedGameObject`；`CloseTopAsync` 无调用方 | `UIView` 加 `defaultSelected` 字段与 `CloseOnCancel` 虚属性；UIService 打开后设选中、关顶层后恢复下层选中；Core 加 UI/Cancel 路由：有可关面板就 `CloseTopAsync`，否则留给暂停菜单（E5） | Core/UI | 无 | M | opus | 完成（2026-09-26）；`UICancelRouter.OnCancelWithNothingToClose` 留给暂停菜单；实际 `SetSelectedGameObject` 只在 Play 走到，未实跑 |
| H4 | 交互复用确认键，220×220 对话大卡片 | `DialogueInteractionFocus` 读 Confirm；HUD 卡片按拇指热区做 | `Gameplay/Interact`（E / F / 手柄 A）；HUD 改成小提示「E 对话」，键位文字取自绑定显示串，仍可点击 | Dialogue + Input | H0 | S–M | opus | 完成（2026-09-26）待视觉验收；提示放底部居中 48 px；手柄为主输入时不切换键位显示 |
| H5 | 虚拟摇杆默认在 PC 显示 | 另一会话把摇杆 / 触屏三键做进 ExplorationHudView，`showStickOnDesktop = 1` | 默认改 false，摇杆 / 三键 / RunToggle 只在 `IsTouchPrimary` 时显示；预制体保留作移植底子 | IsometricExploration（21days-ac 会话） | 无 | S | 对方 | 已通知 |
| H6 | 按钮按手指热区做 | 对话卡 220×220、跳过确认 260×80、返回 180×64、追踪 320×72、胶囊选项 720×72、对话三键 150×60 | 整体缩一档到 32–48 px 高，重排；分辨率方案定后一起做 | 各模块预制体 | E8 | M | opus | 完成（2026-09-26）：7 个预制体按钮 36–44 px、字号 20–24；Dialogue / Quest 回放 9/9 PASS；ExplorationHudView 归另一会话未改 |
| H7 | 无鼠标悬停反馈 | 无高亮 / 提示 / 指针变化；`UIButtonFeedback` 未挂 | 按钮 hover 高亮态、挂 `UIButtonFeedback`、NPC 悬停高亮（可选） | Core/UI + 各预制体 | H6 | S–M | sonnet | 完成（2026-09-26）：Button ColorTint（Image 白、底色在 Normal、悬停 / 选中淡金）+ `UIButtonFeedback`；NPC 悬停高亮未做 |
| H8 | 无 PC 显示设置 | 无分辨率 / 全屏 / 垂直同步 / 帧率上限；存档分区只有音量与语言 | 并入 E3 + E8 | Core | E5 | M | opus | 完成（2026-09-26），并入 E3 / E8 |
| H9 | 窗口不可拖拽、默认独占全屏 | `resizableWindow 0`、`fullscreenMode 1`；移动端自动旋转字段残留 | 按 E8 改 ProjectSettings（改前说明） | ProjectSettings | E8 | S | opus | 完成（2026-09-26）：只改 `resizableWindow`，全屏模式与默认分辨率不动 |
| H10 | 文档措辞 | architecture.md「两个包体共用内容」未标 PC 优先；developer-guide `TouchVirtualStick` 占位已不存在；roadmap 2.1 输入行提到的 `EncounterTouchControls` 已删 | 逐处更正 | 文档 | 无 | S | sonnet | 完成（2026-09-26） |
| H11 | Esc 与暂停键 | Esc 同时绑 Gameplay/Cancel 与 UI/Cancel；Pause 是 P 键 | 做 E5 时定：Esc 无面板可关时开暂停菜单，P 保留为备用 | Core/UI + Input | E5 | S | opus | 完成（2026-09-26），见 E5 行的优先级表 |

H0（前置，已完成 2026-09-26）：`GameInput.inputactions` 一次性加齐 `Gameplay/Interact`、`Gameplay/Journal` 与 `Dialogue` 图，由一个 sonnet 单独做，避免三个 agent 同改一份 JSON。

---

## 4. 任务系统的边界

任务系统（`PRP/quest-system/`，模块 `Game.Quest`）2026-09-25 完成并提交。这里只记它与其它缺口的接口，细节看 [`ai-docs/docs/modules/quest/`](../ai-docs/docs/modules/quest/quest-module-guide.md)。

**已做**：任务表（Main / Side，目标 TalkTo / ReachLocation / Counter，前置）、激活 / 推进 / 完成连锁、追踪、HUD 任务栏、任务面板（主线置顶）、屏内世界空间头顶标记 + 屏外 HUD 贴边箭头、与对话联动（对话结束推进 TalkTo）、场景到达点、其它模块上报进度的统一入口、存档分区、4 个事件。

**Quest 模块边界**：奖励（B1）、接取 / 完成通知（B2）、剧情标记（B3）由外部订阅方承接；B3 已由 Narrative 实现。失败与限时、放弃、已完成列表（B4）、任务对话内容（归对话表）、小地图、多语言不在本模块当前范围。

**接口约定**：任务进度上报走统一入口（Counter 类目标由其它模块按键上报）；四个事件是「已发生事实」，订阅者自己决定表现；任务面板打开时持有一枚世界暂停令牌。

---

## 5. 补足路线

每波收口标准沿用 [`module-dev-spec.md`](module-dev-spec.md)：编译零错误、EditMode 全绿、Showcase PASS、code-reviewer PASS、三件套同步、`/review-change` 授权后提交。
波内任务尽量互不依赖，可并行派单；跨波依赖见第 3 节「依赖」列。

| 波 | 目标 | 任务 | 只能人做的 | 进度 |
| --- | --- | --- | --- | --- |
| **W0 收尾与修正** | 把已完成的两个模块真正交付，清掉已知小问题 | 复跑协作者两条失败测试并处理（2.5 第 4 条）；C4 Narrative 文档登记；E7 Sample 去留；replay / quest tasks.md 补记；SampleScene 命名残留清理；任务面板透底定位 | `/verify-module Dialogue` 与 `/verify-module Quest` 视觉验收点头；字体资产 Clear Dynamic Data；ToastView 已删（2026-09-28），由 `NotificationView` 承接 | 机器可做项已完成（2026-09-26），余下只能人做 |
| **W1 探索层闭环** | 对着「旅行小记」把探索层补齐：走跑、沉浸、交互、拾取、通知 | A1、A2、A5、B2、D4、D5 各自独立派单；A3 + B1 合为一个 PRP「interaction-inventory」 | B1「奖励是信息还是物品」已按聚光灯定为物品（见 1.4、B1）；有人看第二个视频回填 1.3 表 | 进行中：A1 / A2 / B2 / D4 / D5 已完成待视觉验收；A5 延后到移动端移植；A3 / B1（Runtime/Loot）由 21days-46 会话接手 |
| **W2 叙事与存档** | 对话说了什么能改世界，进度能存能读能继续 | C1–C3/B3 按修订 PRP 已实现；E1 单独按 `PRP/save-session/` 验收 | 聚光灯已有十二阶段大纲（[`design/spotlight/01_剧情大纲.md`](design/spotlight/01_剧情大纲.md)），待阶段一设计拍板（00 §8.1 第 1 条）后由策划进表 | C1–C3/B3 已提交，2026-09-29 Narrative 22/22、三条回放通过且用户观看确认；这些是工作区结果，最终提交独立验证待补。E1 整体视觉验收不因 Narrative 三条用例通过而自动关闭 |
| **W3 系统 UI 与演出** | 有一个像游戏的外壳，对话像参考那样动起来 | E2、E3、E4、E5、B4；D1、D2；D3 单独 PRP | 美术给对话框 / 立绘 / 插图规格 | 标题/设置/暂停及对白动效已有实现，不按“其余待做”重建；E4 加载黑幕实现已随本轮远端引入，仍待视觉验收；B4 范围仍开放。世界舞台演出已有 HANDOVER 所述人工验收，不能外推完整内容验收 |
| **W4 场景与内容** | 从一张灰盒到多场景正式内容；目标场景是妖界坊市 / 人间泾阳两张同构地图（[[10]](design/features-spotlight/10_两界与场景结构.md)） | A4 PRP「world-scenes」；A6；D6；F4；F5 | F1 剧本、F2 环境、F3 角色与 UI 皮肤持续产出 | 待做 |
| **W5 聚光灯核心机制** | 换皮附身 / 身份暴露 / 潜行暗杀 / 追逐 / 皮面具道具 / 关卡机制 / BOSS / 小游戏（S1–S8） | 按 00 §8.2 顺序：步 1 怪物种类数据化（[06]）→ S3 → [10] 两界场景（与 S3 并行，并入 A4 / A6）→ S1 → S5 → S2 → S4 → S6 → S8 → S7；每项 `/refine-prd` 后开 PRP，C5 随 S2 / S3 / S7 落地；旧 G1–G6 冻结，E6 随 Mirror 冻结 | 前置条件：策划拍板 00 §8.1 第 1–6 条（首要是阶段一用哪套设计）；其余条目随对应 S 项拍板 | 玩法定义已拆，待策划确认 |

W1 与 W2 没有硬依赖，人手够可以并行；W3 的 E2 / E5 依赖 W2 的 E1 与 E3，其余可提前。
W5 不必等 W4：Narrative 已接入 Boot，S 组有挂点；[10] 两界场景并入 W4 的 A4 / A6，与 S3 并行。

---

## 6. 风险与未决问题

1. **参考与聚光灯的取舍**。参考是活动小游戏，奖励驱动、一图多区域、商店兑换。聚光灯是借身份潜行的志怪叙事：产出是推进用的物品（皮、面具、钥匙、一次性道具、文书），背包按 sp04 分「关键道具 / 材料 / 文书」；场景是坊市 / 泾阳两张同构地图（[10]）。货币、商店、愿望清单聚光灯未提及，照搬参考的这部分会做出另一款游戏。B1 的奖励语义已按聚光灯定为物品（旧版此条是「支柱说一切服务于信息，B1 之前策划必须拍板」）。
2. **Narrative 的验证范围不能外推**。PRP 已于2026-09-29修订并实现最小闭环；旧规格仅供追溯。三条回放不覆盖 C5（S2 / S3 / S7）、任意根作用域中断、全场景恢复事务或真实章节；最终提交独立验证仍待补，详见 `PRP/narrative-dialogue/tasks.md`。
3. **击倒与 BOSS 血量怎么并存还没定**。聚光灯写本体打不过任何怪，被打会被击倒、只能慢走、不能攻击（[03] R3）；BOSS 又有血量与多阶段（钱塘君「血量归零时转换阶段」、sp04 BOSS 血条，[09] R27）。Player / Monster 现在有生命、伤害、攻击冷却，玩家普通攻击能打死怪、生命归零直接死亡（00 §5 C4、C5）。C5 与 S3 / S7 之前要决定这些字段的去留，否则回放快照格式会反复升版。
4. **存档格式将频繁变动**。W1–W3 会新增 Inventory、Session、Narrative 分区并改动 Quest 分区。每次改都要走分区版本迁移并补测试，见 `developer-guide.md` 第 9 章。
5. **多人共用工作区**。至少两条并行会话在同一工作区改 SampleScene 与字体资产。提交按文件挑、不整份暂存（pitfalls「两个会话共用一个工作区」）。场景改动尽量走预制体，减少 `.unity` 冲突。
6. **移动端移植是后置阶段**（用户 2026-09-26 定：PC 优先）。触屏摇杆 / 按钮、Android 低档性能实测、安全区实机检查都归移植阶段；现在只保证输入走 Action Map、UI 走 Canvas Scaler + 安全区、平台差异只在 `Runtime/Platform/`，不让 PC 阶段的代码把移植路堵死。
7. **美术方案未定**。拼接小人是过渡方案，战斗与交互动画等 Spine 决定；立绘 / UI 全部占位。D1、D3、D6 的规格要等美术。
8. **UI 动效层没有文字资料**。1.3 表是常见项推测，需要有人对着视频逐项确认，否则 W3 的动效工作没有验收标准。
9. **CI 搁置、Android 不能上架**。见 `ci-setup.md` 与 2026-09-15 建设纪要「已知缺口」，不在本路线图范围，但 W4 出包前要回头看。
10. **聚光灯内部两套阶段一设计未定**（00 §6）。sp02 五个附身场景（戏班 / 衣肆 / 金银行 / 客栈 / 酒肆，回合制 BOSS）与 sp03 坊市暗杀场景（妖民 / 缄口侍 / 市令 / 药行，BOSS 乐官、行首、知金银行都料与坊正）地点相同、怪物与机制不同，两版大纲的阶段一也不同。S1、S3、S6、S7、S8 的阶段一内容全部悬着（00 §8.1 第 1 条）。
11. **功能文档待定多**。`design/features-spotlight/` 01–13 合计 195 条待定 / 开放问题、15 条原文矛盾（00 §5），各篇重复提问不少。建议请策划按 00 §5（矛盾 C1–C15）与 §8.1（阻塞 1–12）的编号一处答复、多篇回填；拍板后改飞书再重拉 `design/spotlight/`，不让功能文档变成第二份真源。

---

## 7. 资料来源与相关文档

**参考资料**
- 视频：[BV1cQ3m6PE4j](https://www.bilibili.com/video/BV1cQ3m6PE4j/)（剧情录屏）、[BV1mQ4R6gEfj](https://www.bilibili.com/video/BV1mQ4R6gEfj/)（UI / 交互 / 动效归档）
- 玩法说明：[PRTS 活动页](https://prts.wiki/w/%E7%9B%B4%E5%88%B0%E5%A4%A7%E5%9C%B0%E5%8F%98%E6%88%90%E4%B8%80%E9%A2%97%E9%85%B8%E6%A9%99)、[BWIKI 活动页](https://wiki.biligame.com/arknights/%E7%9B%B4%E5%88%B0%E5%A4%A7%E5%9C%B0%E5%8F%98%E6%88%90%E4%B8%80%E9%A2%97%E9%85%B8%E6%A9%99)、[新浪夏活报道](https://www.sina.cn/news/detail/5327118630125618.html)
- 官方说明原文（PRTS）：「点击屏幕呼出虚拟摇杆，或是在PC端使用移动键/上下左右键移动安洁莉娜」「点击右下角按钮，可以切换移动速度（散步/奔跑）」「点击左下角按钮，可以隐藏所有UI和交互，进入沉浸模式」「在左上角的便签处确认心愿任务，前往带有标志的NPC处推进故事」「记得确认画面四周的万向标」「寻找物资箱并打开，或是完成特定探索任务，也可以获取报酬」「完成所有故事后，可以点击左上角按钮重置故事进度，但已获得的报酬和物资箱无法重复获取」

**工程内相关文档**
- 设计（当前，聚光灯）：[`design/spotlight/README.md`](design/spotlight/README.md)（飞书转写）、[`design/features-spotlight/00_功能总览.md`](design/features-spotlight/00_功能总览.md)（S 组设计索引）
- 设计（旧版）：[`design/product/1_核心概念与设计支柱.md`](design/product/1_核心概念与设计支柱.md)（旧版）、[`design/product/2_世界观圣经.md`](design/product/2_世界观圣经.md)（旧版）
- 框架：[`architecture.md`](architecture.md)、[`developer-guide.md`](developer-guide.md)、[`module-dev-spec.md`](module-dev-spec.md)
- 美术与策划：[`artist-guide.md`](artist-guide.md)、[`designer-guide.md`](designer-guide.md)
- 模块三件套：[`../ai-docs/docs/modules/`](../ai-docs/docs/modules/)
- PRP：[`../PRP/dialogue-system/`](../PRP/dialogue-system/)、[`../PRP/quest-system/`](../PRP/quest-system/)、[`../PRP/narrative-dialogue/`](../PRP/narrative-dialogue/)、[`../PRP/monster-ai/`](../PRP/monster-ai/)、[`../PRP/replay/`](../PRP/replay/)、[`../PRP/character-puppet/`](../PRP/character-puppet/)
- 建设纪要：[`history/2026-09-15-框架与harness建设.md`](history/2026-09-15-框架与harness建设.md)、[`history/2026-09-25-对话系统与拼接小人.md`](history/2026-09-25-对话系统与拼接小人.md)、[`history/2026-09-28-回放舞台统一与harness迭代.md`](history/2026-09-28-回放舞台统一与harness迭代.md)
- 踩坑：[`../ai-docs/pitfalls.md`](../ai-docs/pitfalls.md)
