---
type: extension-guide
module: narrative
layer: runtime
maturity: seed
---

# Narrative 扩展指南

> 要给剧情规则加条件类型、加阶段类型、加遭遇规则，或要把 Narrative 真正接进 Unity 时看这份。
> 架构见 [`narrative-module-guide.md`](narrative-module-guide.md)，对外签名见 [`narrative-external-api.md`](narrative-external-api.md)。
> 原七文件保持纯规则；运行入口 NarrativeService 与 NarrativeInstaller 已有源码，接线/实测状态见 tasks.md。

## 扩展点一览

| 要加什么 | 扩展点 | 改代码吗 |
| --- | --- | --- |
| 新的条件事实类型 | `EncounterContext.Fact` 枚举 + `Read` 分支 | 是 |
| 新的阶段类型 | StageKind + 纯规则迁移 + NarrativeService 分派 + Catalog 能力检查 | 是；C5/G5 未定不可擅自实现 |
| 新的遭遇规则 | Tables/Data/narrative_encounters.json | 否，生成后跑 Catalog 校验 |
| 条件事实来源 | NarrativeConditionSource；DialogueInstaller 有注册则采用，否则保留占位 | 是 |
| NPC 入口 | NarrativeTrigger 稳定 ID/kind；运行时 Configure | 通常只需接线 |
| 任务完成标记 | Tables/Data/narrative_quest_flags.json | 否；任务 ID 必须存在 |

## 新增一种条件事实类型

1. `EncounterContext.cs`：在 `Fact` 枚举加一项；构造函数按需加对应的只读属性；`Read(Fact, string)` 的 `switch` 里加一个 `case`。
2. 若事实需要参数化（像 `StoryFlag` 用 `key`），在你的 `case` 里做和 `StoryFlag` 一样的空值校验（抛 `ArgumentException`），
   不要静默返回 `false`——内容错误要在求值时立刻暴露，不能被当成「条件不满足」吞掉。
3. Dialogue 侧如果要用这个新事实：`DialogueCatalog.cs:244` 附近的 `ConditionFact` 映射要**按名字**加同一项
   （见 `dialogue-module-guide.md`「内容表」一节），两边不同步会在翻译时抛异常或漏判。
4. 补 `NarrativeRulesTests.cs` 或新测试覆盖这个事实的求值分支；不要只加枚举不加断言。

**不要**为了「以后可能要参数化」而把 `Fact` 换成字符串或反射查找——PRP 明确禁止通用条件表达式语言，
未接入的事实类型必须在内容检查阶段报错，不能默认 `true`（见 `prp.md` 第 4.1 节）。

## 新增一种阶段类型（`StageKind`）

1. `NarrativeContent.cs`：在 `StageKind` 枚举加一项；如果这种阶段有自己的结构约束（像 `Condition` 必须有
   `True`/`False` 出口、`WaitAction` 多部分行为必须有 `Success` 出口），在构造函数的校验循环里补对应分支
   （`NarrativeContent.cs:43`–`51` 是现有两个例子）。
2. `NarrativeRules.ResolveAutomatic`（`NarrativeRules.cs:69`）目前只自动处理 `Condition` 和 `End`；
   其余 `StageKind`（`Dialogue`、`WaitAction`、`Battle`，以及你新加的）都停在原地等待外部调用 `Apply` 推进——
   **不要把新阶段类型也塞进自动推进循环**，除非它确实是「无需等待任何外部输入」的纯判断阶段。
3. 阶段分派在 NarrativeService；新能力须先补 Catalog 校验、失败/取消与保存边界，再放开该类型。
   不要在 NarrativeRules 里塞玩法调用，它必须保持纯规则。
4. 补内容构造测试：非法结构应该在 `new NarrativeContent(...)` 时就抛异常，仿照 `NarrativeRulesTests.Condition_WhenAutomaticCycleExists_RejectsContent`。

## 新增一条遭遇规则

生产内容在 Tables/Data/narrative_encounters.json，结构见 Tables/Defines/narrative.xml。
先添加剧情 JSON，再添加遭遇行，运行 scripts/gen-tables.ps1 与定向 Catalog 测试。纯规则测试仍可构造：

```csharp
new EncounterRules.Rule
{
    Id = "唯一 ID", TriggerKind = "触发类型（如 seen）", TargetKind = "目标类型（空=不限）",
    Priority = 1, StoryId = "剧情 ID", EntryStageId = "入口阶段 ID",
    Repeat = EncounterRules.RepeatPolicy.Reenter,   // 或 Once / RisingCondition
    Conditions = new[] { new[] { new NarrativeCondition { Fact = ..., Expected = true } } },
};
```

- `RepeatPolicy` 三选一：`Once`（消费一次后永不再触发）、`Reenter`（每次新的 `TriggerId` + `EntryEpoch` 都可再触发）、
  `RisingCondition`（条件从「不满足」变为「满足」的那一刻才算一次，`ConsumeEdge` 会把边标记为已触发）。选错会导致重复弹窗或再也不触发。
- 同一批候选里，同优先级的多条匹配规则会被当成内容错误抛 `InvalidOperationException`——设计规则时用不同 `Priority` 显式排出优先级，不要依赖数组顺序。
- Catalog 拒绝同触发类型、目标范围重叠的同优先级规则，即使条件看似互斥也请显式区分 Priority。

## 接入条件源（替换 `DefaultDialogueConditionSource`）

1. 扩展 NarrativeConditionSource 的真实来源，读取须廉价、无副作用；每 0.25 秒及选项提交时会重新 Snapshot。
2. 目标按跨读档稳定 ID 注册；不要用 instance ID 或缓存候选中的过期事实。
3. 来源已接好并有测试后，才移除 Catalog 对相应事实的拒绝。未知来源不能默认 true。
4. 真实源不可依赖 NarrativeService，否则 Service→DialogueService→条件源→Service 会形成 DI 环。
   DefaultDialogueConditionSource 保留兼容无 NarrativeInstaller 的 Boot 与既有测试，不要求删除。

## 场景与恢复能力接线

Boot 的 GameBootstrap 挂 NarrativeInstaller；场景或运行时 NPC 使用 NarrativeTrigger。
样例用已有对白1002与任务1001/1002，验证对白9001展示条件选项；不新建章节、奖励或战斗规则。
主动交互重试只恢复同目标当前 Dialogue；不要删除 Once 消费记录来实现重试，否则已完成遭遇也会重新触发。
新增外部请求时，先固定阶段身份，再订阅，再发请求；await 后仍用之前的身份提交结果。
扩展对白恢复必须落完整 DialogueSaveData 并通过同一保存闸门，不能仅把阶段 ID 写盘后整段重播。
新状态在完整恢复得到验证前保持 CanSave=false；候选校验也须拒绝该状态。

## 不该从哪扩

- 不要把玩法判断（背包、任务、好感度……）塞进 `NarrativeRules`——它只管阶段迁移与结果校验，实际效果由所属玩法模块的公开入口应用。
- 不要新起第二套条件/规则解释框架；条件永远走 `NarrativeCondition.Matches`，遭遇仲裁永远走 `EncounterRules`。
- 不要在 `Game.Core` 里加剧情名词；`Game.Narrative` 已经是最合适的落点。
- 不要为了图省事让 `NarrativeContent` 支持运行时热改内容结构——它的校验设计前提是「构造即定型」，运行时改字段会绕过全部结构校验。
