---
type: external-api
module: monster
layer: runtime
maturity: stable
---

# Monster 外部接口

| 接口 | 用途 | 前提 |
| --- | --- | --- |
| `MonsterRules.Reset(Vector2[])` | 开始一场遭遇并复制巡逻点 | 数组至少一个点；`MonsterRules.cs:45` |
| `MonsterRules.Step(in MonsterIntent)` | 推进一个固定 tick，返回是否攻击 | 已 Reset；`MonsterRules.cs:75` |
| `MonsterRules.ApplyDamage(in DamageIntent, in PlayerSnapshot)` | 受伤并记录攻击者位置；生命零则死亡 | 正伤害；`MonsterRules.cs:189` |
| `MonsterRules.Model` | 只读引用供视图取状态 | 不能从外部写内部字段；`MonsterRules.cs:42` |
| `EncounterStep.Begin/End` | 场景进入/退出时启停整场逻辑 | 根作用域已注册；`EncounterStep.cs:23` |
| `MonsterEncounterState` | 切换到遭遇 | 先把场景登记为地址 `IsometricEncounter`；`MonsterEncounterState.cs:14` |

外部场景切换使用 `IGameFlow.GoToAsync<MonsterEncounterState>()`。
Monster 读 `PlayerSnapshot`，伤害玩家通过 `PlayerRules.ApplyDamage`；不能写 Player 模型字段。
`MonsterConfig` 见 `MonsterConfig.cs:7`，公开属性只读，资产由 `MonsterInstaller` 注入。
`MonsterMode` 见 `MonsterMode.cs:4`，供表现与测试读取，不供外部直接设置。

## 场景契约

`EncounterSceneView` 见 `EncounterSceneView.cs:9`。
场景应配置 `playerSpawn` 与至少一个 `patrolPoints`；未配置巡逻点时 `PatrolPositions` 抛错。
`ConfigureXZ` 显式接入现有角色和 SpriteRenderer，并把逻辑 XY 坐标映射到场景 XZ。
视图在绑定后只显示模型，不推进规则；`OnBackClicked` 由遭遇状态订阅。
场景 Addressables 地址是 `IsometricEncounter`，与状态类名不同。

## 回放契约

由 Installer 注册 `PlayerModel`、`MonsterRules`、`EncounterStep`；不得在别处重复注册。
更改三者顺序或序列化字段必须升级 `ReplayFormat` 版本并更新恢复测试。
