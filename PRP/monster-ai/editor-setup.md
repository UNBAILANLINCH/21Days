# Unity 编辑器接线与验收

当前会话没有可调用的 Unity MCP。以下资产必须由 Unity 编辑器创建和保存，不能手改 `.unity`、`.meta` 或 Addressables YAML。

## 配置资产

1. 在 `Assets/_Project/Data/` 建 `Player/` 和 `Monster/` 文件夹。
2. 用 `Assets/Create/21Days/Player/Player Config` 创建 `PlayerConfig.asset`，放进 `Data/Player/`。
3. 用 `Assets/Create/21Days/Monster/Monster Config` 创建 `MonsterConfig.asset`，放进 `Data/Monster/`。
4. 首轮保持脚本默认值；攻击冷却与原型速度待试玩校准。

## Boot 根作用域

1. 打开 `Assets/_Project/Scenes/Boot.unity`，选中 `GameBootstrap`。
2. **移除** `SampleInstaller` 组件。仅取消勾选不足以停止它注册：`GameLifetimeScope` 会读取同物体的所有 `GameplayInstaller`。
3. 添加 `PlayerInstaller`、`MonsterInstaller` 组件，分别拖入上述配置资产。
4. 保存场景。不要移动原有 `GameLifetimeScope` 和框架配置。

## 遭遇场景

1. 用编辑器新建 2D 场景 `Assets/_Project/Scenes/MonsterEncounter.unity`。
2. 新建根对象 `Encounter`，挂 `EncounterSceneView`。
3. 新建 `PlayerSpawn`，位置建议 `(-4, 0, 0)`，赋给 `playerSpawn`。
4. 新建至少两个巡逻点，例如 `PatrolPoint0=(0,0,0)`、`PatrolPoint1=(4,0,0)`，按顺序赋给 `patrolPoints` 数组。
5. 保持场景开放、无障碍物。占位角色、状态色、生命与警戒条由 `EncounterSceneView` 在运行时创建。
6. 在 Addressables `Scenes` 组添加此场景，地址精确设置为 `MonsterEncounter`，然后保存。

## 验证入口

Player 与 Monster Showcase 在测试代码中搭建验证画面，`ScenePath` 为 null，不要求另建 `Scenes/Verify` 资产。
这与框架支持的代码搭建方式一致，但与最初方案列出的两个验证场景资产不同；需要固定场景供策划打开时，再用编辑器保存对应场景。

1. 打开 `Boot.unity` 进入 Play，标题“开始”应切到遭遇。
2. 键盘 WASD/方向键移动，Shift 潜行，G 伪装，J 攻击；手柄验证左摇杆、左肩键、北面按钮、西面按钮。
3. 在触屏设备确认虚拟摇杆与三个按钮可用，且不会挡住“返回标题”。
4. 运行 EditMode 的 `PlayerRulesTests`、`MonsterRulesTests`、`ReplayFormatTests`。
5. 运行 Showcase 的 `PlayerShowcase` 和 `MonsterShowcase`，检查报告与截图；开发者确认视觉反馈后再判模块完成。
6. 运行 `21Days/工程/资产体检`，确认新资产 `.meta`、Missing Script 与 Addressables 条目。
