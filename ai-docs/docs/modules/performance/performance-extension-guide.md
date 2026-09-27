---
type: extension-guide
module: performance
layer: runtime
maturity: stable
---

# Performance 扩展指南

## 新建一段演出（给动画师，不用改代码）

1. 打开菜单 `21Days/演出/演出编辑器`，右上角输入 id（小写字母 / 数字 / 下划线，如 `perf_village_intro`），点「创建」。
   `PerformanceTemplateFactory` 会一步建齐：时间轴（字幕 / 表情 / 动作 / 音效四条轨）+ 预制体（`PerformanceStage` +
   `PlayableDirector` + 舞台相机 + 占位演员）+ 全树设 `Performance` 层 + 登记 Addressables 地址，并自动打开时间轴。
2. 在 Timeline 窗口里拖时间线：字幕轨加 `SubtitleClip`（说话者 + 正文）、表情轨加 `ExpressionClip`（表情名，须与演员的
   `ExpressionNames` 一致，大小写敏感）、动作轨拖演员 `Animator` 的 `AnimationClip`、需要停顿处在 Markers 区加 `HoldMarker`。
3. 点 Inspector 或编辑器窗口的「校验」，看 `PerformanceValidator` 列出的问题（红色 Error 必须清零，黄色 Warning 视情况）。
4. Play 模式下从 Boot 进游戏，回到演出编辑器窗口点「试播」验证效果；也可以直接用 `PerformanceTrigger` 挂进验证场景走完整流程。

**世界模式（演员站在 3D 场景里）的差异**：模板工厂只建叠加模式，世界模式照 `Editor/Performance/Samples/SceneTalkSampleBuilder.cs`
的做法建——`PerformanceStage.mode = World`、舞台相机透视 Base 且不打 MainCamera 标签、演员是 `PuppetVisual`（`CameraBillboard`）下嵌套的
小人预制体、`cast` 填说话者 → 头像；场景里给 `PerformanceTrigger` 接 `anchor`，勾 `hideActorVisual`，同形象的 NPC / 怪与标记拖进
`hiddenDuringPlay`。构图按 `/verify-module Performance` 的截图调相机局部位姿与站位。

## 接入一个 Live2D 模型

1. 人工把 Live2D Cubism SDK for Unity 导入到 `Assets/Live2D/`（授权协议要人点，不进仓库）；`Live2DDefineSync` 会自动检测到
   `Live2D.Cubism.asmdef` 并加上编译符号 `LIVE2D_CUBISM`，`Game.Live2D` 程序集随之参与编译。想立刻确认就跑菜单
   `21Days/演出/检查 Live2D 符号`。
2. 模型放 `Assets/_Project/Art/Live2D/<角色>/`，按 Cubism 导入流程生成 `.model3.json` 等资产。
3. 演出预制体的演员子物体用 `Live2DPerformanceActor`（挂在 Cubism 模型根，与 `CubismExpressionController` /
   `CubismRenderController` 同物体）替换 `SpritePerformanceActor`；表情轨道绑定不用改，`ExpressionTrack` 只认
   `PerformanceActor` 基类。**该类未本地编译验证**，导入 SDK 后若报错先按 `Live2DPerformanceActor.cs` 文件头的
   API 清单核对（官方源码 `github.com/Live2D/CubismUnityComponents`）。
4. 动作走 Cubism 导入器生成的 `AnimationClip`：时间轴的 Animation 轨直接绑模型的 `Animator`，不经适配层；混合要平滑
   再给模型挂 `CubismFadeController`（可选）。
5. 想撤 Live2D：删掉 SDK 目录，`Live2DDefineSync` 下次域重载会自动移除 `LIVE2D_CUBISM` 符号，`Game.Live2D` 整个程序集
   随之停止编译，不留编译错误。

## 加一个新的挂载点

现有三种：场景触发区（`PerformanceTrigger`）、场景加载即播（`PerformanceTrigger.Mode = OnSceneStart`）、
对白节点前插播（`Node.PerformanceId`）。加第四种（比如「击败某个 Boss 后自动播」）：

1. 在对应模块里注入 `IPerformanceService`，判断触发条件后直接 `await performance.PlayAsync(id, ct)`——不需要新建
   `Performance` 侧的类型，`PlayAsync` 是通用入口。
2. 想要「只播一次」的语义就自己查 `performance.HasPlayed(id)` 再决定要不要播；`PerformanceTriggerRules.ShouldFire`
   是纯函数，可以直接复用而不用照抄一份判定逻辑。
3. 不要在 `Game.Performance` 里加新模块的名词（依赖方向永远是「调用方 → Performance」，不能反过来）。

## 加一种新的时间轴轨道（比如「相机运镜」「音效强度」）

1. 参照 `Runtime/Performance/Timeline/ExpressionTrack.cs` 的四件套：`XxxTrack : TrackAsset`（`[TrackClipType]`，
   需要绑定对象就加 `[TrackBindingType]`）、`XxxClip : PlayableAsset, ITimelineClipAsset`、
   `XxxBehaviour : PlayableBehaviour`（数据载体）、`XxxMixerBehaviour : PlayableBehaviour`（混合逻辑，
   只在权重最大片段变化时调用真正的效果，不要每帧调）。
2. 需要通知舞台暂停之类的「跨片段」行为，参照 `HoldMarker`：`Marker, INotification, INotificationOptionProvider`，
   `PerformanceStage.OnNotify` 按类型识别；不要用 `SignalEmitter`（需要额外接 `SignalReceiver`，动画师每段都要手接一遍）。
3. 需要在 `PerformanceValidator` 里加对应校验（同名方法 `CheckXxxTrack`），并在 `PerformanceTemplateFactory.CreateTimeline`
   决定是否要默认建这条轨（不是每种轨道都要进模板）。
4. 校验与工厂改动配 `Tests/EditMode/Editor/Performance/` 的对应测试。

## 换 HUD 美术 / 调参数

- 面板字段名对照见 `performance-module-guide.md` 的「接线要求」表；改布局不用改代码，`PerformanceView` 只认物体名。
- 数值全在 `PerformanceConfig`（长按跳过秒数、黑边高度、黑场时长、提示文案、模板工厂的默认策略开关）；不要在
  `PerformanceStage` / `PerformanceService` 里写死数字，新增参数照抄现有字段的 `[Tooltip]` + `[Min]` 写法。
- 单段演出想跟全局默认不一样：改该演出预制体上 `PerformanceStage` 的四个开关（`skippable`/`pauseWorld`/`hideHud`/`letterbox`），
  不要改 `PerformanceConfig`（那是全局默认值）。

## 依赖方向约束

新扩展只能 `Game.Performance → Game.Core`；`Game.Performance.Timeline` 只能被 `Game.Performance` 与 `Game.Editor.Performance`
引用。不要引用 `Game.Dialogue` / `Game.IsometricExploration` 等玩法模块（方向反了，是它们调 `IPerformanceService`）；
不要在 `Game.Runtime` 里直接出现 `Live2D.Cubism.*` 类型（只能在可选程序集 `Game.Live2D` 里，且不能被 `Game.Performance`
引用）；编辑器扩展不进 `Game.Runtime`，运行时类型不引用 `UnityEditor`。

## 验证

规则改动（`PerformanceRules`/`PerformancePolicy`/`PerformanceSaveData`/`PerformanceTriggerRules`）配 EditMode 测试；
编辑器工具（模板工厂、校验器）配 `Tests/EditMode/Editor/Performance/` 测试，用临时目录并在 `TearDown` 删干净；
玩家可见行为（黑边、字幕、停顿、跳过、触发、对白插播）走 `Tests/Showcase/Performance/PerformanceShowcase.cs`。
场景与预制体资产只通过 Unity 编辑器、Unity MCP 或本模块的编辑器工具修改，不手改 `.prefab`/`.playable` 的 YAML。
