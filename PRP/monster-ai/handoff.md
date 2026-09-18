# Player / Monster 模块交接

一句话：玩法逻辑、输入映射、回放状态、测试与文档都已落地；剩下的只有 Unity 编辑器里的资产与场景接线。规则层已有自动化用例，输入与画面只能人工确认。

## 代码在哪

| 层 | 位置 |
| --- | --- |
| 玩家规则 | `Assets/_Project/Scripts/Runtime/Player/PlayerRules.cs`（快照 `PlayerSnapshot.cs`、配置 `PlayerConfig.cs`、注册 `PlayerInstaller.cs`） |
| 怪物规则 | `Assets/_Project/Scripts/Runtime/Monster/MonsterRules.cs`（状态机、感知、巡逻、战斗） |
| 每 tick 顺序 | `.../Monster/EncounterStep.cs`——同一个步内先玩家后怪物，顺序写死在这里 |
| 输入 | `Core/Simulation/InputCommand.cs`（按钮位）、`Core/Input/LiveInputSource.cs`（采样）、`Data/Input/GameInput.inputactions`（动作与绑定） |
| 回放 | `Core/Replay/ReplayFormat.cs`（v2）；状态按 PlayerModel → MonsterRules → EncounterStep 顺序落档，注册点在 `MonsterInstaller.cs` |
| 测试 | `Scripts/Tests/EditMode/Player/PlayerRulesTests.cs`、`Scripts/Tests/EditMode/Monster/MonsterRulesTests.cs`、`Scripts/Tests/EditMode/Replay/ReplayFormatTests.cs` |
| 画面验证 | `Scripts/Tests/Showcase/Player/PlayerShowcase.cs`、`Scripts/Tests/Showcase/Monster/MonsterShowcase.cs`；代码搭场景，不需要场景资产 |
| 文档 | `ai-docs/docs/modules/player/`、`ai-docs/docs/modules/monster/`；需求、方案、任务见本目录其余文件 |

## 还没做完（只能在 Unity 编辑器里做）

按 [editor-setup.md](editor-setup.md) 逐步执行，三件事：

1. 建配置资产 `Data/Player/PlayerConfig.asset`、`Data/Monster/MonsterConfig.asset`。
2. `Boot.unity` 的 `GameBootstrap`：**移除** `SampleInstaller`，添加 `PlayerInstaller` 与 `MonsterInstaller` 并拖入配置。
3. 新建 `Scenes/MonsterEncounter.unity`（`Encounter` + `PlayerSpawn` + 巡逻点），加入 Addressables 并设地址 `MonsterEncounter`。

`.unity`、`.meta`、Addressables YAML 不能手改，所以这几步没有代码替代方案。

## 怎么跑验证

- EditMode：Test Runner → EditMode 页签，程序集 `Game.Tests.EditMode`，三个类 `PlayerRulesTests`、`MonsterRulesTests`、`ReplayFormatTests`。纯逻辑，**不需要接线**。
- Showcase：Test Runner → PlayMode 页签，程序集 `Game.Tests.Showcase`，按 Category `Showcase` 筛，或直接跑 `PlayerShowcase` / `MonsterShowcase`。两者都覆写了 `ScenePath = null`、`LoadBootScene = false`，画面在代码里搭、也不加载 Boot，所以**接线之前就能跑**，可以先用它确认表现层。
- 产物：截图与报告写在 `Logs/verify/`（已 gitignore），控制台按 `[VERIFY]` 前缀过滤。检查点失败只记警告不打断，最后在 TearDown 汇总成一次 `Assert.Fail`——别只看红字，要读报告。
- 节奏：菜单写入 EditorPrefs `Game.Verify.HoldScale`，批处理下自动归零；失败步骤多停 2.5 秒。
- 只能在编辑器里跑：Showcase 靠 `EditorSceneManager.LoadSceneInPlayMode` 加载不在 Build Settings 里的场景。

## 验收对照（PRD 六条 → 证据）

| PRD 验收 | 自动化证据 | 还得人工看 |
| --- | --- | --- |
| 1 巡逻 7–10 秒后停 2 秒；警戒后不再站立 | `MonsterRulesTests.Patrol_PausesAfterSevenToTenSeconds_ForTwoSeconds` | — |
| 2 红区直接敌对；橙区 4 秒升满警戒；玩家看得见警戒值 | `Perception_PrioritizesRed_AndRespectsDisguiseSneakAndRearProximity`、`Alert_FillsInFourSeconds_DecaysInSix_AfterHostileLossInTwo`；Showcase 步骤「警戒」 | 警戒条是否真画出来（`EncounterSceneView`） |
| 3 满警戒 6 秒匀加速归零；失目标 2 秒后退回警戒 | `Alert_FillsInFourSeconds_DecaysInSix_AfterHostileLossInTwo` | — |
| 4 潜行免背后近距；伪装免橙区；都不免红区 | `Perception_PrioritizesRed_AndRespectsDisguiseSneakAndRearProximity` | — |
| 5 互攻；受击与死亡有反馈；死者停止行动 | `Combat_HitsAndDies_ThenCannotAct`、`PlayerRulesTests.FixedIntent_ControlsMovementActionsDamageAndDeath`；Showcase 步骤「命中」「死亡」 | 命中与死亡的画面反馈 |
| 6 键鼠/手柄/触屏同一组动作；回放恢复 | `PlayerRulesTests.ReplaySnapshot_RestoresActionEdgesAndCooldown`、`MonsterRulesTests.ReplaySnapshot_RestoresPatrolPointsTimersAndRandomState`、`ReplayFormatTests` | 三端实际触发：`EncounterTouchControls` 走 Gamepad 路径，需真机或编辑器确认 |

规则层有自动化，输入与画面没有——这两块只能人工判。
## 坑与风险

- **回放 v2 是破坏性升级**：`CurrentFormatVersion` 与 `MinimumReadableFormatVersion` 已从 1 抬到 2，此前录制的 v1 文件会被明确拒收。这是既定决定，不是 bug；要兼容就得另写 v1 读取分支。
- **快照字节布局等于注册顺序**：改动 `MonsterInstaller` 里 `replay.Register` 的先后，等于改归档格式，必须同时再升一次版本。
- `SampleInstaller` 要从 `GameBootstrap` 上移除，**不能只取消勾选**：`GameLifetimeScope` 会读取同物体上的所有 `GameplayInstaller`。
- 攻击冷却、玩家速度等是原型暂定值，试玩后校准配置资产，别改代码。
- 本会话没有可用的 Unity 环境，EditMode 与 Showcase 均未实际运行；以上状态来自代码与 diff 阅读，未经验证的部分已在此标明。

## 首版数值与键位

扇区全角 75°，红/橙半径 2/6，近距察觉 1.5，攻击距离 0.8；巡逻 2 单位/秒，警戒/敌对倍率 1.1/1.25；双方生命 3、单次伤害 1。

键鼠：移动 WASD/方向键、Shift 潜行、G 伪装、J 攻击；手柄：左摇杆、左肩键、北面按钮、西面按钮；Android 用 `EncounterTouchControls` 的虚拟摇杆与三个按钮。
