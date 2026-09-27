---
type: external-api
module: isometricexploration
layer: runtime
maturity: stable
---

# IsometricExploration 外部接口

| 接口 | 用途 | 前提 |
| --- | --- | --- |
| `StandaloneEncounterController` | 直接播放场景时推进现有遭遇规则，并缓存 J/G 短按 | 场景没有活动的 `GameBootstrap` |
| `EncounterSceneView.ConfigureXZ(...)` | 显式接入现有角色并执行逻辑 XY → 场景 XZ 映射 | 出生点、巡逻点和角色引用完整 |
| `MonsterEncounterState` | 通过正式流程加载等距遭遇 | Addressables 已登记 `IsometricEncounter` |
| `SmoothCameraFollow.Target / SetTarget(Transform)` | 读取或切换镜头跟随对象 | 初始偏移已由 Start 建立；切换重置缓动速度 |
| `ExplorationHudView.RunSlot` | 右下角预留槽位，供走 / 跑按钮挂入 | 探索 HUD 已打开（`IUIService.Get<ExplorationHudView>()` 非空） |
| `ExplorationHudView.SetImmersive(bool)` / `OnImmersiveToggle` | 按钮文字与透明度 / 按钮点击 | 只由 `ExplorationHudPresenter` 驱动；别的模块要切沉浸调 `IHudVisibility.SetHudHidden` |
| `ExplorationHudView.SetRunLabel` / `SetStickVisible` / `SetTouchButtonsVisible` / `SetRunToggleVisible` / `SetPrompt` / `SetControlsVisible` | 走跑标签、摇杆 / 触屏三键 / 走跑按钮显隐、交互提示、控件区整体显隐 | 只由 `ExplorationControlsPresenter` 驱动；全部可空容错，未接线字段调用即空操作 |
| `ExplorationHudView.OnResetClicked` / `CompassRoot` / `CompassMarkerTemplate` / `CanvasSize` | 重置按钮点击事件 / 万向标父节点 / 万向标模板 / 画布尺寸 | 分别由 `ExplorationControlsPresenter`、`ExplorationCompassPresenter` 驱动；模板与根节点未接线时呈现器记一次 Error 并埋 `compass_template_missing` |
| `ExplorationConfirmView.OpenAsync(string)` + `WaitAsync(ct)` + `SetButtonLabels(...)` | 通用确认弹窗：打开 → 等选择 → 设按钮文案 | Addressables 地址 `ExplorationConfirmView`；`WaitAsync` 被关闭 / 取消 / token 取消都返回 `false`，不抛异常 |
| `ExplorationCompassRules.TryPlace(...)` / `TrimLabel(...)` | 万向标摆位纯函数 / 标签截断纯函数 | 无状态、无 Unity 依赖，供其它需要「屏外指引」的表现层直接复用 |
| `ExplorationControlsPresenter.ShouldShowStick(...)` / `SelectRunLabel(...)` | 触屏控件（摇杆 / 三键 / 走跑按钮）显隐 / 走跑标签选择纯函数；PC 优先，桌面默认隐藏 | 无状态；供测试或另一套 HUD 表现复用同一判定，避免各写一份 |
| `ExplorationPointOfInterest.Label` / `Kind` / `Position` / `IsVisible` | 兴趣点只读信息 | 由 `ExplorationCompassPresenter` 读取；`Crate` 类型的 `IsVisible` 反映同物体 `SupplyCrate.IsOpened` |

## 沉浸模式（跨模块）

沉浸状态的真相在 Core 的 `IHudVisibility`，不在本模块：其他模块读 `IsHudHidden` 或订阅 `HudVisibilityChangedEvent`
隐藏自己的世界空间提示；自己的 Hud 面板默认会被隐藏，沉浸中仍需显示的面板重写 `UIView.VisibleWhenHudHidden => true`。
Addressables：`UI` 组地址 `ExplorationHudView`（预制体 `Assets/_Project/Prefabs/UI/ExplorationHudView.prefab`）、
`ExplorationConfirmView`（预制体 `Assets/_Project/Prefabs/UI/ExplorationConfirmView.prefab`）。

沉浸时 `ExplorationHudView` 本身仍显示（`VisibleWhenHudHidden = true`），但 `ControlsRoot` 与独立的
`RunToggle` 两个 `CanvasGroup` 会被 `ExplorationControlsPresenter` 一起置为不可见 / 不可交互——别的模块
要判断「控件是否可见」不要读这两个 CanvasGroup，统一读 `IHudVisibility.IsHudHidden`。

## 场景契约

正式遭遇要求把 `Assets/Scenes/SampleScene.unity` 以 Addressables 地址 `IsometricEncounter` 登记后加载。
场景必须显式保存 `EncounterSceneView`、玩家出生点、至少一个巡逻点，以及玩家、敌人与纸片引用。
玩家 `PlayerInput` 在直接播放时启用，由 StandaloneEncounterController 读取；Boot 路径停用它。
敌人 PlayerInput 与双方 IsometricPlayerController3D 停用，刚体为无重力运动学。

外部模块不要直接写角色 Transform 参与玩法判断。Player/Monster 模型是位置真相，
`EncounterSceneView` 只把逻辑 XY 映射到场景 XZ。

## 关键表现资产（供跨模块复用）

本次「探索场景 2.5D 表现升级」运行时公开接口没变，以下是新增的表现层资产路径，
供其它模块的角色/场景需要同类效果（遮挡、阴影、后处理）时直接复用，而不是各自新建：

| 资产 | 路径 | 用途 |
| --- | --- | --- |
| 后处理 Volume Profile | `Assets/Settings/ExplorationVolumeProfile.asset` | `GlobalVolume` 引用，Tonemapping/Color Adjustments/Vignette |
| 纸片深度裁剪着色器 | `Assets/_Project/Art/Shaders/SpriteDepthClip.shader` | 让 SpriteRenderer 被 3D 几何体遮挡、参与 SSAO、可投 ShadowCaster |
| 纸片深度裁剪材质 | `Assets/_Project/Art/Materials/Character/M_SpriteDepthClip.mat` | 角色纸片默认材质；新增可被遮挡的纸片角色应复用它，不要自建同类材质 |

这些是表现层资产，不是运行时接口；改动仍要走 Unity 编辑器，不手改 `.mat` / `.shader` 之外的序列化字段。
