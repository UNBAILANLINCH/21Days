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
