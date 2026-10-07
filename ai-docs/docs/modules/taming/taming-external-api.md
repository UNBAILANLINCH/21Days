---
type: external-api
module: taming
layer: runtime
maturity: seed
---

# Taming 外部接口

| 接口 | 契约 |
| --- | --- |
| `TamingRules(PlayerRules, MonsterRules, ITelemetryScope)` | 两个规则先 Reset；每场景创建一个实例 |
| `Step(in TamingIntent, float)` | 非负步长；同 tick 不再由其他调度器推进这两个角色 |
| `AdvanceTargets(Vector2, float, bool isIdentityInEffect = false)` | 每个巡逻者只推进一次，向所有未驯服目标传递身份禁攻；返回首位巡逻者本 tick 是否出手 |
| `IsTamed` | 首位巡逻者是否驯服；`Reset` 或新游戏清除 |
| `IsControllingEnemy` | 当前移动输入应交给敌人，镜头跟随该对象 |
| `TamingSceneController.Simulate` | 自动化验证驱动与正常输入复用的入口 |
| `ManualSimulation` | 测试设 true 后停止自动 FixedUpdate 推进 |

`TamingIntent.ToggleControl` 按下边缘触发，长按不会连续来回切换。
敌人或玩家死亡时拒绝接管；死亡时自动返回不会复活目标。

## 正式遭遇的多实例入口

`EncounterStep.Taming` 是正式玩法唯一归属状态。`TargetIds` 按场景显式数组排序；存档使用稳定 ID，不使用数组槽号、Hierarchy 名或 InstanceID。

| 接口 | 契约 |
| --- | --- |
| `TamingActor.StableId / DisplayName` | 场景保存的身份；复制后必须赋新 ID，配置拒绝重复或缺失身份 |
| `IsTargetTamed(id) / CanControl(id)` | 查询独立驯服状态与当前可选性；死亡、禁用、销毁目标不能选 |
| `EncounterStep.CurrentControlId` | 当前控制稳定 ID；默认 `player` |
| `EncounterSceneView.CurrentControlObject` | 与该 ID 对应的 Transform；场景卸载后不应继续持有视图 |
| `EncounterSceneView.RequestControl(id, tame = false)` | UI 共用入口；校验后排队，下一逻辑 tick 生效；普通请求不能绕过驯服 |
| `TamingRules.OnControlChanged` | 控制变更时传新 ID；重复选择不重复发；订阅方负责退订 |

```csharp
string id = step.CurrentControlId;
string name = step.Taming.GetDisplayName(id);
Transform actor = view.CurrentControlObject;
bool queued = view.RequestControl("patrol-b");
step.Taming.OnControlChanged += HandleControlChanged;
```

`TryTame` / `TryControl` 是规则层同步 API；运行时界面应使用 `RequestControl`，使输入进入回放命令。
InputCommand 的 Axis1.x：0 无定向请求，1 玩家，2 起为场景 patrolActors 顺序；ButtonTame 驯服按住状态，ButtonTamePressed 为 performed/界面请求锁存的新按下沿，ButtonSelectControl 选择。新按下沿能在没采到松开 tick 的连续短按中再次触发，长按不重复切换。
EncounterSaveData v2 保存全部巡逻者、独立驯服归属和当前 ID；v1 原主怪物快照恢复，其余从作者路线初始化且未驯服。
回放二进制版本为 5，旧 v4 回放拒收，与 JSON 存档兼容无关。
