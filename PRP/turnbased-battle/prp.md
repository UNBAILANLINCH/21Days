# PRP: 回合制战斗落地（独立战斗场景 + SampleScene BOSS 入口 + 剧情回写）

> 状态：执行中（2026-10-07 建立）。
> 上游：[`07_回合制作战文档.md`](../../docs/design/spotlight/07_回合制作战文档.md)（真源，86 行）、[`09_BOSS战.md`](../../docs/design/features-spotlight/09_BOSS战.md)、[`PRP/battle-to-narrative/prp.md`](../battle-to-narrative/prp.md)（C5 的结果词汇与唯一回写路径）。
> 规则内核：[`turnbased-module-guide.md`](../../ai-docs/docs/modules/turnbased/turnbased-module-guide.md)，末节「接线清单」就是本 PRP 要还的账。
> 不涉及：潜行 / 偷袭 / 处决（S3/S4）、巡逻怪遭遇流程（`Runtime/Monster/`）。

---

## 1·用户已拍板（2026-10-07）

| # | 问题 | 结论 |
| --- | --- | --- |
| U1 | 呈现形式 | **独立战斗场景**：叠加加载一张战斗场景（3D 舞台 + 纸片角色对打，出招有冲上去再退回的位移），打完卸载回原地 |
| U2 | 入口 | **SampleScene 村里加一个 BOSS NPC**，走近按 E 开打 = 07「正面攻击」，玩家先手 |
| U3 | 结果去向 | **接剧情阶段机**：开战前一句对白 → Battle 阶段 → 胜利写「已击败」标记、BOSS 退场；失败回到战前、可重打。顺带打通 C5 |

## 2·主窗口定的技术决策

| # | 决策 | 理由 |
| --- | --- | --- |
| D1 | **新建模块 `Runtime/Battle/`，命名空间 `Game.Battle`**（走 `Game.Runtime`，不建独立 asmdef）。放战斗流程、战斗场景表现、界面、跨模块适配器、Installer。`Runtime/TurnBased/` 保持纯规则不动依赖 | TurnBased guide「依赖方向」明写不依赖 Monster/Player/Inventory/Narrative；流程层要同时用 Narrative / Loot / Player / Core.UI / Core.Flow，塞进去会破坏内核可单测。复用不成立（无现成战斗流程），扩展不成立（职责不同）。新文件头照 project-root「加能力的顺序」写明 |
| D2 | **剧情驱动开战**：Narrative 在 `DriveAsync` 停到 Battle 阶段时**新增发布** `BattleStageEnteredEvent(generation, activationId, targetId, stageId, payload)`；`Game.Battle` 订阅它开战，打完调 `NarrativeService.CompleteBattleAsync(..., session.ExitKey)` | Narrative 现在停在 Battle 阶段后没人知道（`NarrativeService.cs:202-203`）。剧情侧只发通知、不管胜负，符合 battle-to-narrative §2.1「唯一写入方」 |
| D3 | Battle 阶段的 **`payload` = BOSS 定义 id**。BOSS 定义放 `BossRosterConfig`（ScriptableObject，`Assets/_Project/Data/Battle/`）：id、显示名、最大生命、战斗外醉酒值、舞台外观预制体。**本期不进 Luban 表** | 工作区里另一会话有未提交的表数据（`monster_species/1003.json`、`yao/3.json` 及其 bytes），这时候加新表 schema 要全表重生成，牵连太大。等 C90/C91 拍板后再迁表，在文件头写明 |
| D4 | **战斗场景叠加加载**：`IAssetService.LoadSceneAsync(key, LoadSceneMode.Additive)`，登记进 `AssetGroups/Scenes.asset`（照 c72e3a0）。场景放 `Assets/_Project/Scenes/BattleArena.unity`，舞台根放在**远离世界原点的偏移处**；**不切 ActiveScene**（否则别的模块 Instantiate 会落进战斗场景、跟着被卸载） | 回原地不丢世界状态；一张战斗场景供所有世界场景复用 |
| D5 | **世界暂停走 `IWorldPauseService.Acquire` + 关 Gameplay 图**，照 `InventoryPanelController.cs:88-100` 只恢复进来前的状态。暂停会把 `timeScale` 置 0，所以**战斗场景里的一切表现（Animator、位移、闪现提示、数字飘字）都必须走不受缩放的时间** | 统一暂停服务是唯一写 timeScale 的地方；巡逻怪和玩家移动都在确定性内核里，暂停才能一起停住 |
| D6 | 切换节奏：黑幕落 → 加载战斗场景、战斗相机接管、开 `BattleView` → 黑幕揭；结束时对称。黑幕走 `Core/Flow/ILoadingCurtain` | 与 E4 加载过渡一致 |
| D7 | `BattleView`：`UIView`，Panel 层，**`CloseOnCancel = false`**；Esc / P 在战斗中不得开暂停菜单（核对 `PauseMenuController` 的开启条件）。布局照 07 的布局图：顶部道具格、底部三招式格、右侧怒气槽、招式栏上方玩家状态（带剩余回合数）、BOSS 血条（上方状态字、下方醉酒条）、玩家血条、屏幕中央闪现提示；招式 / 道具 / 状态鼠标悬停弹详情 | 07「画面表现」逐条 |
| D8 | **随机源不用 `logic.*`**：战斗在确定性内核之外推进，消耗 `logic.*` 流会让回放在战斗之后哈希对不上。用一条不进快照哈希的独立流（按 `IRandomService` 的实际约定取名，没有合适前缀就从会话种子派生一条本地流），并在模块 guide 里写明「回放不覆盖回合制战斗」 | TurnBased guide「随机数与回放」一节把这个问题留给接线波，这里给出结论 |
| D9 | **战中不存档**：战斗进行期间压住自动存档；读档时剧情若停在 Battle 阶段，就**重新发布 `BattleStageEnteredEvent`，战斗从头开始** | 战斗会话状态不进快照，避免升 `ReplayFormat` |
| D10 | 玩家血量：开战时读 Player 模块的当前 / 最大生命做 `PlayerBattleSnapshot`，**战后不回写**。道具：背包口适配 `LootService.Items`（`IBattleItemInventory.Owns(string)` 与 Loot 的 int id 在适配器里转换）；道具格只显示背包里有的 Consumable 类；用一次从背包扣 1；效果**占位**为治疗药水（1004）回复 30% 生命，配进 `TurnBasedConfig`，注释标「占位，等 C91」 | 07 没写道具效果，也没写玩家血量；占位要能在 Inspector 里改、能删 |
| D11 | BOSS NPC：场景物体挂 `NarrativeTrigger`（`targetKind = SampleBoss`）+ 可交互焦点；遭遇表加 `sample_boss`（Interact，可重复，条件：没有 `world.sampleboss.defeated`）；**新建**故事 `sample_boss_battle.json`（不改 `sample_battle.json`，测试在用）：`taunt`（对白）→ `fight`（Battle，`payload: sample_boss`，`battleResults: [Victory, Downed]`）→ `Victory` 到 `cleared`（End，`setFlags: world.sampleboss.defeated`）/ `Downed` 到 `retreat`（End，失败）。胜利后 BOSS NPC 读该标记退场（先找现成的「按标记显隐」机制，没有再加） | 照 U2/U3；样例内容只证明机制，真实 BOSS 等 §8.1 #1 / C90 |

## 3·数据流

```
SampleScene  BOSS NPC（NarrativeTrigger: SampleBoss）── 按 E
   ▼
Narrative 遭遇 sample_boss → 故事 sample_boss_battle
   taunt（对白）→ fight（Battle, payload=sample_boss）
   ▼ DriveAsync 停住，发布 BattleStageEnteredEvent
Game.Battle.BattleFlow
   1 BossRosterConfig[payload] → BOSS 定义
   2 世界暂停令牌 + 关 Gameplay 图 + 压住自动存档
   3 黑幕落 → 叠加加载 BattleArena → 战斗相机接管 → 开 BattleView → 黑幕揭
   4 TurnBasedKernel.TryStartBattle(正面攻击) → BattleSession
   5 玩家点招式 / 道具 → session；BOSS 回合自动推进；BattleEvent → 舞台演出 + 界面刷新
   6 结束：黑幕落 → 关 BattleView、卸载 BattleArena、恢复相机 / 输入 / 暂停
   7 （仍在黑幕下）NarrativeService.CompleteBattleAsync(generation, activationId, targetId, ExitKey)
     → BOSS 在黑幕下退场 → 黑幕揭（回写最多等 1 秒，超时先揭幕；回写被拒或抛异常也照样揭幕）
   ▼
Narrative：Victory → cleared（写 world.sampleboss.defeated，BOSS 退场）
           Downed  → retreat（故事结束，可再按 E 重打）
```

## 4·文件与所有权

| 区域 | 动作 | 说明 |
| --- | --- | --- |
| `Runtime/Battle/**`（新） | 新建 | 流程、配置、适配器、Installer、舞台表现、`BattleView` |
| `Runtime/TurnBased/` | 小改 | 只加道具效果占位（D10）与必要的只读查询；不引入对其他模块的依赖 |
| `Runtime/Narrative/` | 小改 | 新增 `BattleStageEnteredEvent` 及其发布点（含读档恢复时重发，D9） |
| `Runtime/Loot/` | 可能小改 | 若没有「扣 1 件」的公开方法则加一个 |
| `Tables/Data/narrative/sample_boss_battle.json`、`Tables/Data/narrative_encounters.json` | 改 / 新建 | 跑 `gen-tables.ps1` 后**只提交 narrative 相关 bytes** |
| `Assets/_Project/Scenes/BattleArena.unity`、`Prefabs/UI/BattleView.prefab`、`Data/Battle/*.asset` | 新建 | 连同 `.meta` |
| `AssetGroups/Scenes.asset`、`AssetGroups/UI.asset` | 加条目 | UI 地址 = 类名（invariants 第 6 条） |
| `Assets/_Project/Scenes/Boot.unity` | **共享点**，只加 `BattleInstaller`，挂在 GameBootstrap 组件列表末尾 | 工作区里另一会话对它有未提交改动（Monster `kindId`）；不得回退、不得顺手提交它们 |
| `Assets/Scenes/SampleScene.unity` | 加 BOSS NPC | 放在**不挡现有回放行走路线**的位置 |
| `Runtime/Monster/**`、`Runtime/Stealth/**`、`Tables/Data/monster_species/**`、`Tables/Data/yao/**` | **禁止改动** | 另一会话正在改，工作区有它们的未提交改动 |

## 5·波次

| 波 | model | 内容 | 产出验收 |
| --- | --- | --- | --- |
| W1 | opus | D1–D3、D5、D8–D11 的**代码与数据**：`Game.Battle` 流程（不含舞台美术）、Narrative 事件、BossRoster、背包适配、道具占位、Installer、剧情内容 + gen-tables；EditMode 测试 | 编译过；EditMode 指定组全绿；`gc_scan` 无新增问题 |
| W2 | opus | D4、D6、D7 的**场景与表现**：`BattleArena.unity`、战斗相机、纸片角色与出招位移、`BattleView` 预制体与布局、悬停详情、闪现提示；Boot 挂 Installer、SampleScene 挂 BOSS NPC；Showcase 回放 | 回放 PASS；截图交主窗口 |
| W3 | sonnet | 文档：`/generate-doc battle`、TurnBased guide 的「没有调用方」和「Showcase 尚无」两处、roadmap S7 / C5 行、battle-to-narrative PRP 状态 | 带源码行号 |
| 审 | code-reviewer（sonnet） | 模块级审查 | PASS |

## 6·验证（范围写死）

- **EditMode**：`Game.Tests.EditMode.Battle`（新）、`Game.Tests.EditMode.TurnBased`（改了道具效果）、`Game.Tests.EditMode.Narrative`（改了 DriveAsync 发布点）；如果改了 Loot，再加 `Game.Tests.EditMode.Loot`。
- **回放**，逐个模块：
  - `Battle`（新）：功能本身。要求两条：打赢后 BOSS 退场、标记写入；被击倒后走 retreat，BOSS 还在，可以再按 E 重打。被击倒那条用 Showcase 内的配置覆盖把 BOSS 伤害调高，不改正式资产。
  - `Narrative`：DriveAsync 新增了发布点，它的三条回放要确认没受影响。
  - `Exploration`：SampleScene 新放了 NPC，可能挡住它的行走路线。
- 不跑全套、不跑 PerformanceEvidence。

## 7·已知坑

1. **叠加场景会触发各模块的 `SceneManager.sceneLoaded`**（Dialogue / Loot / Mirror / Exploration 罗盘 / World 等 SceneBinder，还有 `Core/Boot/FallbackCamera`）。`LootSceneBinder.BindScene` 只会追加，看起来安全，但要**逐个核实**：有没有哪个会把「当前场景」改指到战斗场景，或者在卸载时清掉对世界场景的绑定。World 的传送 / 出生点逻辑尤其要看。
2. **RenderSettings（雾、环境光）跟着 ActiveScene 走**。不切 ActiveScene，战斗舞台就会吃到世界场景的雾，要用相机远近和灯光调到能看清。相机栈相关的实测结论见 Performance 模块 guide 和 `ai-docs/pitfalls.md`。
3. **`timeScale = 0`**：Animator 要设成 `UnscaledTime`，位移、飘字、闪现提示都要用不受缩放的时间。UI 过渡本来就是 `UpdateIgnoreTimeScale`（`UIView.cs:256`）。
4. **两个同名的 `BattleOutcome`**（`Game.TurnBased` / `Game.Narrative`）：同一个文件里不要同时 `using` 两个命名空间，结果映射只走 `BattleExitKeys` 字符串。
5. **身份三项别混**：`CompleteBattleAsync` 要的 generation / activationId / targetId 用事件里带的值，跟 `EncounterStep` 的 EncounterId / ActivationId 是两套。
6. **中文字形**：新 UI 文案用到的字要在中文 SDF 字体里存在（参考 8776e25「中文动态字体补入回放所需字形」的做法）；新增文案后检查有没有方框。
7. **Luban**：只改 json 不改 schema。跑完 `gen-tables.ps1` 后看 `git status`，**只认 narrative 相关的 bytes**；`monster_species` / `yao` 的 bytes 是另一会话的，不碰也不提交。
8. **MCP 菜单 / 编译**：改完 `.cs` 先等 Unity 刷新编译，用 `read_console` 确认零错误再进场景操作。

## 8·占位与待拍板（不阻塞本期）

| 项 | 占位 | 等 |
| --- | --- | --- |
| 回合制适用范围 | 只给 `sample_boss` 用 | C90 |
| 全部数值（血量、伤害、回合上限、怒气上限）、道具效果、玩家血量是否回写 | `TurnBasedConfig` / `BossRosterConfig` 占位 | C91 |
| 界面细节、与美术需求表 BOSS 血条 / 结算界面怎么合 | 照 07 布局图做白盒 | C92 / Q19 |
| BOSS 是谁、外观 | 「阶段一 BOSS（占位）」，战斗外醉酒值占位 50（02:109 写 BOSS 100，口径未明） | §8.1 #1 |
| 出招动画 | 位移 + 受击闪白 + 震屏 + 伤害飘字；招式 3 加重版 | 美术（D6 角色动画） |

## 9·进度回填

| 日期 | 波 | 结论 |
| --- | --- | --- |
| 2026-10-07 | W1 | 完成，未提交。EditMode：Battle 33/33、TurnBased 165/165、Narrative 94/94、Loot 36/36；gc_scan 无新增问题。新增了对白表 `Tables/Data/dialogue/1004.json`（taunt 用）。gen-tables 后只有 dialogue / narrative 三个 bytes 变了 |
| 2026-10-07 | W1 偏离① | **D9 改了存档边界**：`NarrativeService.IsStable` 原来不把 Battle 阶段算稳定，读档会被拒，D9 走不到。现在改成「战斗在途（`TryBeginBattle` 登记的 RequestIssued）不稳定，没在打时稳定」。战中不存档复用 Session 现有的 NarrativeStable 闸门，Session 没改 |
| 2026-10-07 | W1 偏离② | **主动交互的 Reenter 原来只能触发一次**：`NarrativeTrigger` 的候选 EntryEpoch 恒为 0，消费键撞车。现在改成：Interact 候选用剧情的 `NextActivationId` 当纪元（`NarrativeService.cs:133` 附近）。Once 不受影响。另外加了一条：Battle 阶段空闲时同目标交互会重发开战通知 |
| 2026-10-07 | 主窗口改 D10 | 探索血量只有 3，药水 30% 取整为 0，BOSS 重击一下就秒。所以**战斗血量与探索血量分开**：`BossDefinition` 加「本场玩家生命」（占位 10），BOSS 生命 12，不读 PlayerModel。招式伤害不动（TurnBased 回放依赖） |
| 2026-10-07 | 工作区 | 另一会话把 `ExecutionInteractor` 挂进了 SampleScene，Boot 的 `kindId` 也改了，都未提交。W2a 不碰这两张场景；W2b 改它们时只提交自己的 hunk |
| 2026-10-07 | W2a | 完成，未提交（中途撞会话额度，续跑后完成）。Battle 组 EditMode 69/69；`Logs/verify/battle/w2a/` 有 4 张截图，主窗口看过，布局与 07 一致。<br>新增：<br>· `BattleArena.unity`：根节点 y=−1000，相机默认关闭、不打 MainCamera 标签、无 AudioListener。<br>· `BattleView.prefab`、BOSS 占位预制体。<br>· 编辑器菜单「21Days/战斗/重建战斗白盒」。<br>SceneBinder 审计十项：全部安全。QuestSceneBinder 的前提是战斗相机不能打 MainCamera 标签。<br>相机接管：关世界相机组件、不关物体，所以 AudioListener 照常；收场时在卸载场景之前恢复。<br>暂停菜单：BattleView 不让 Esc 关，所以 Esc 被挡下；P 键所在的 Gameplay 图已关。<br>键盘 1/2/3 出招借用了 Dialogue 图的 Choice1–3。<br>实测：游戏中 ActiveScene 一直是 Boot，所以舞台吃的是 Boot 的 RenderSettings。<br>副作用：`AddressableAssetSettings.asset` 的一行缓存哈希被改，钩子拦了还原，**提交时不带这一处** |
| 2026-10-07 | 主窗口验收 W2a | 要在 W2b 处理：①战斗中每个动作都让警告数上涨（23→26），查来源；②世界场景的视角调试按钮和日志计数器露在战斗画面上，查是不是只在开发版里有 |
| 2026-10-07 | W2b | 完成，未提交（中途撞会话额度，续跑后完成）。<br>EditMode：Battle 69/69、Narrative 105/105。<br>回放都限定单模块、都 PASS：Battle 2/2（`Logs/verify/battle/20261007-194728/`）、Narrative 3/3、Exploration 8/8。<br>Boot 末尾挂了 `BattleInstaller`，GUID 已反查。SampleScene 新增 `Npc_SampleBoss`，位置 (-4.2, 4.89, 8.4)。<br>新增 `NarrativeFlagVisibility`。按 E 的入口靠新加的 `DialogueInteractable` 交互转交（`NarrativeTrigger` 绑定时接管）。原因：现有 Interact 遭遇在场景里从来没接过，焦点、提示、头顶标记只认 DialogueInteractable。<br>回放控制胜负靠 `BattleSetup.OverrideSettings`。<br>警告上涨的原因是截图帧触发 `core.perf/spike`，不是战斗引起的。视角按钮和日志计数器都只在编辑器或开发版里有 |
| 2026-10-07 | 主窗口验收 W2b | 截图看过，整条链通了。收尾修四处：①胜负大字看不清；②BOSS 头顶的条跟着倒地动画滑出屏幕；③剧情回写挪到揭黑幕之前，让 BOSS 在黑幕下退场；④交互提示的动词改成「挑战」（有现成字段才改）。另外补跑 Dialogue 组 EditMode（共用文件 `DialogueInteractable` 改过）。19:44 那批 Monster+Stealth 回放是另一会话跑的，不归本 PRP |
| 2026-10-07 | 收尾修正 | 完成。EditMode：Battle 75/75、Dialogue 119/119；Battle 回放 PASS（`Logs/verify/battle/20261007-201528/`），主窗口看过截图。<br>①胜负大字：横条原本就有，截图发淡是因为截在淡入第一帧；现在等完全淡入再截，结局字也一直留到落幕。<br>②头顶条和飘字锚定在站立时的头顶（`BattleActor.StandingHeadPosition`），冲刺也不跟。<br>③剧情回写挪到幕下，§3 第 6、7 步已改；加了 1 秒兜底和埋点 `write_back_outlived_curtain`。<br>④交互动词写死在 `DialogueInteractHudView.cs:25`（`Verb = "对话"`），按约定没有新加机制，**仍显示「对话」** |
| 2026-10-07 | 代码审查 | PASS，无 BLOCK。<br>WARN 1：`BattleSetup.OverrideSettings` 是 public 的测试口。<br>WARN 2：NarrativeService Dispose 后，BOSS 的交互转交还指着已释放的服务。<br>另有：开战失败时先揭幕、后恢复世界；两处测试缺口（Reenter 下剧情进行中连按 E、门闸未开时新身份顶掉旧请求）。以上已合成一单派出（opus） |
| 2026-10-07 | 审查意见修正 | 完成。EditMode：Battle 85/85、Narrative 108/108；Battle 回放 2/2 PASS（`Logs/verify/battle/20261007-203145/`）。<br>`OverrideSettings` 正式包里调用会抛异常，开打时有替换就记 `battle/settings_overridden`，所以回放每场都会多一条 W 级埋点，属预期。<br>`NarrativeTrigger` 加了 `IsReady` 守卫。<br>开战失败时改成先恢复世界再揭幕：`EnterAsync` 加了 `restoreOnFailure` 参数。<br>两处测试缺口已补 |
| 2026-10-07 | W3 | 完成：battle 三件套；turnbased / narrative / loot / characterpuppet 指南同步；catalog、pitfalls、roadmap 已改；battle-to-narrative 状态已更新 |
| 2026-10-07 | 并发事故 | 另一会话的提交 2303f86 按整文件提交了 Boot 和 SampleScene，把本 PRP 的 `BattleInstaller` 和 `Npc_SampleBoss` 带了进去，但没带对应脚本。20:53 它为拉取远端做了全量 stash，把本 PRP 的全部未提交文件收走；合并远端 29 个提交（3396ca8）后才还原。还原后逐文件核对：未跟踪 151 个逐字节一致，已跟踪文件都在。合并时远端覆盖了 roadmap 的 S7 行和 catalog 的 TurnBased 行，主窗口已按远端的新列格式补回 |
| 2026-10-07 | 合并后复核 | 编译零错误。EditMode：Battle 85、TurnBased 165、Narrative 108、Loot 36、Dialogue 119，共 513/513。Battle 回放第二次 PASS（`20261007-220915`）。第一次失败（`20261007-220658`）卡在另一会话 d6f41ce 新加的开局操作说明面板上：埋点只有 `tutorial_shown`、没有 `tutorial_dismissed`，也没有任何 battle 事件，跟本 PRP 无关 |
