---
type: extension-guide
module: performance
layer: runtime
maturity: stable
---

# Performance 扩展指南

## 新建一段演出（给动画师，不用改代码）

1. 打开菜单 `21Days/演出/演出编辑器`，右上角输入 id（小写字母 / 数字 / 下划线，如 `perf_village_intro`），点「创建」。
   `PerformanceTemplateFactory` 会一步建齐世界舞台壳：时间轴（字幕 / 动作 / 音效三条轨）+ 预制体（`PerformanceStage` +
   `PlayableDirector` + 透视 URP Base 舞台相机 `StageCamera`（不打 MainCamera 标签，默认构图同村口示例）+ 空的演员站位根 `Actors`，
   演员名单为空）+ 全树设 `Performance` 层 + 登记 Addressables 地址，并自动打开时间轴。
2. 往 `Actors` 下放演员，照 `Editor/Performance/Samples/SceneTalkSampleBuilder.cs` 的做法：`Actor_<名字>/PuppetVisual`
   （`CameraBillboard`，目标相机指舞台相机）下嵌套小人预制体 `Chibi_<名字>`，停用小人上的 `ChibiPuppetMotion`；`cast` 填说话者 → 头像 → 站位
   （`side: Left/Right`，按演员在舞台上的实际站位配，决定头像出现在对白面板左槽还是右槽）。
3. 在 Timeline 窗口里拖时间线：字幕轨加 `SubtitleClip`（说话者 + 正文）、动作轨绑演员的 `Animator` 再拖 `AnimationClip`、
   需要停顿处在 Markers 区加 `HoldMarker`。表情轨及无实现的演员抽象已删除。
4. 点 Inspector 或编辑器窗口的「校验」，看 `PerformanceValidator` 列出的问题（红色 Error 必须清零，黄色 Warning 视情况）。
5. Play 模式下从 Boot 进游戏，回到演出编辑器窗口点「试播」验证效果；也可以直接用 `PerformanceTrigger` 挂进验证场景走完整流程。
   场景里给 `PerformanceTrigger` 接 `anchor`。带 `ChibiPuppet` 的场景角色由服务统一隐藏；角色根以外的标记等额外物体拖进 `hiddenDuringPlay`。
   构图按 `/verify-module Performance` 的截图调相机局部位姿与站位。

## 加一个新的挂载点

现有三种：场景触发区（`PerformanceTrigger`）、场景加载即播（`PerformanceTrigger.Mode = OnSceneStart`）、
对白节点前插播（`Node.PerformanceId`）。加第四种（比如「击败某个 Boss 后自动播」）：

1. 在对应模块里注入 `IPerformanceService`，判断触发条件后直接 `await performance.PlayAsync(id, ct)`——不需要新建
   `Performance` 侧的类型，`PlayAsync` 是通用入口。
2. 想要「只播一次」的语义就自己查 `performance.HasPlayed(id)` 再决定要不要播；`PerformanceTriggerRules.ShouldFire`
   是纯函数，可以直接复用而不用照抄一份判定逻辑。
3. 不要在 `Game.Performance` 里加新模块的名词（依赖方向永远是「调用方 → Performance」，不能反过来）。

## 加一种新的时间轴轨道（比如「相机运镜」「音效强度」）

1. 参照 `Runtime/Performance/Timeline/SubtitleTrack.cs` 的四件套：`XxxTrack : TrackAsset`（`[TrackClipType]`，
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
- 数值全在 `PerformanceConfig`（长按跳过秒数、进场黑场时长、提示文案、默认策略开关 `defaultPauseWorld` / `defaultHideHud`——后两项模板工厂目前没读）；不要在
  `PerformanceStage` / `PerformanceService` 里写死数字，新增参数照抄现有字段的 `[Tooltip]` + `[Min]` 写法。
- 单段演出想跟全局默认不一样：改该演出预制体上 `PerformanceStage` 的三个开关（`skippable`/`pauseWorld`/`hideHud`），
  不要改 `PerformanceConfig`（那是全局默认值）。
- 调字幕节奏：改 `PerformanceConfig` 三个字段——`subtitleCharactersPerSecond`（字/秒，**0 = 整句直出**）、
  `subtitlePunctuationPauseSeconds`（标点后停顿秒数）、`subtitlePunctuationChars`（哪些字符算标点）；这三项是全局默认，没有像三开关那样的单段覆盖。
- 调「自动」继续间隔：改 `PerformanceConfig.autoAdvanceSeconds`（默认 1.5 秒，与对白的自动间隔语义一致；资产里填负数 / NaN / 无穷时按默认值兜底），同样是全局默认，没有单段覆盖。

## 依赖方向约束

服务依赖 `Game.Core`；角色隐藏额外读取叶子表现模块 `Game.CharacterPuppet` 的公开组件与 `TrackedRoot`，不引用角色玩法逻辑。`Game.Performance.Timeline` 只能被 `Game.Performance` 与 `Game.Editor.Performance`
引用。不要引用 `Game.Dialogue` / `Game.IsometricExploration` 等玩法模块（方向反了，是它们调 `IPerformanceService`）；
编辑器扩展不进 `Game.Runtime`，运行时类型不引用 `UnityEditor`（编辑器侧的示例 builder 可以引用小人 / 探索模块的组件来搭舞台）。

## 验证

新增世界舞台时，距离剔除从主相机继承；不要再挂一套组件竞争写 `layerCullDistances`。
近远裁剪面仍按舞台作者配置；调镜头后检查远景是否被主相机的分层距离剔除，并测试结束后的恢复。

规则改动（`PerformanceRules`/`PerformancePolicy`/`PerformanceSaveData`/`PerformanceTriggerRules`）配 EditMode 测试；
编辑器工具（模板工厂、校验器）配 `Tests/EditMode/Editor/Performance/` 测试，用临时目录并在 `TearDown` 删干净；
玩家可见行为（舞台相机接管、字幕、停顿、跳过、触发、对白插播）走 `Tests/Showcase/Performance/PerformanceShowcase.cs`
与 `ScenePerformanceShowcase.cs`。
场景与预制体资产只通过 Unity 编辑器、Unity MCP 或本模块的编辑器工具修改，不手改 `.prefab`/`.playable` 的 YAML。
