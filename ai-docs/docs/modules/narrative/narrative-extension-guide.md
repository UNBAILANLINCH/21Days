---
type: extension-guide
module: narrative
layer: runtime
maturity: seed
---

# Narrative 扩展指南

> 要给剧情规则加条件类型、加阶段类型、加遭遇规则，或要把 Narrative 真正接进 Unity 时看这份。
> 架构见 [`narrative-module-guide.md`](narrative-module-guide.md)，对外签名见 [`narrative-external-api.md`](narrative-external-api.md)。
> 本模块还没有 Installer/场景/Showcase——大部分「扩展」目前都是改纯 C#，不是改场景资产。

## 扩展点一览

| 要加什么 | 扩展点 | 改代码吗 |
| --- | --- | --- |
| 新的条件事实类型 | `EncounterContext.Fact` 枚举 + `Read` 分支 | 是 |
| 新的阶段类型 | `NarrativeContent.StageKind` 枚举 + `NarrativeRules`/未来 `NarrativeController` 里的分派分支 | 是 |
| 新的遭遇规则 | `EncounterRules.Rule` 实例（内容层，未来由 Luban 表产出） | 否（有内容管线后） |
| 真实条件事实来源接 Dialogue | 实现 `IDialogueConditionSource`，替换 `DialogueInstaller` 里的占位注册 | 是（Dialogue 目录） |
| 把 Narrative 接进 Unity | 新建 `NarrativeController` + Installer（模块目前没有） | 是，且是当前最大的缺口 |

## 新增一种条件事实类型

1. `EncounterContext.cs`：在 `Fact` 枚举加一项；构造函数按需加对应的只读属性；`Read(Fact, string)` 的 `switch` 里加一个 `case`。
2. 若事实需要参数化（像 `StoryFlag` 用 `key`），在你的 `case` 里做和 `StoryFlag` 一样的空值校验（抛 `ArgumentException`），
   不要静默返回 `false`——内容错误要在求值时立刻暴露，不能被当成「条件不满足」吞掉。
3. Dialogue 侧如果要用这个新事实：`DialogueCatalog.cs:242` 附近的 `ConditionFact` 映射要**按名字**加同一项
   （见 `dialogue-module-guide.md`「内容表」一节），两边不同步会在翻译时抛异常或漏判。
4. 补 `NarrativeRulesTests.cs` 或新测试覆盖这个事实的求值分支；不要只加枚举不加断言。

**不要**为了「以后可能要参数化」而把 `Fact` 换成字符串或反射查找——PRP 明确禁止通用条件表达式语言，
未接入的事实类型必须在内容检查阶段报错，不能默认 `true`（见 `prp.md` 第 4.1 节）。

## 新增一种阶段类型（`StageKind`）

1. `NarrativeContent.cs`：在 `StageKind` 枚举加一项；如果这种阶段有自己的结构约束（像 `Condition` 必须有
   `True`/`False` 出口、`WaitAction` 多部分行为必须有 `Success` 出口），在构造函数的校验循环里补对应分支
   （`NarrativeContent.cs:43`–`51` 是现有两个例子）。
2. `NarrativeRules.ResolveAutomatic`（`NarrativeRules.cs:64`）目前只自动处理 `Condition` 和 `End`；
   其余 `StageKind`（`Dialogue`、`WaitAction`、`Battle`，以及你新加的）都停在原地等待外部调用 `Apply` 推进——
   **不要把新阶段类型也塞进自动推进循环**，除非它确实是「无需等待任何外部输入」的纯判断阶段。
3. 阶段分派（「进了 `Dialogue` 阶段该去调 Dialogue 服务」这类）目前没有实现，属于 `NarrativeController` 的职责——
   新阶段类型的分派逻辑等这个类真正建立后再加，不要现在就在 `NarrativeRules` 里塞玩法调用（它必须保持纯规则）。
4. 补内容构造测试：非法结构应该在 `new NarrativeContent(...)` 时就抛异常，仿照 `NarrativeRulesTests.Condition_WhenAutomaticCycleExists_RejectsContent`。

## 新增一条遭遇规则

内容层的事，目前没有 Luban 表（T7 未做），只能在代码里手工构造 `EncounterRules.Rule` 传入构造函数：

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
- 等 T7（Luban 内容管线）落地后，这里应该改成表驱动 + 导入适配层校验（重复 ID、失效跳转、条件类型未接入、同优先级冲突），不要在那之前手工在代码里堆一大批 `Rule`。

## 接入条件源（替换 `DefaultDialogueConditionSource`）

这是 Dialogue ↔ Narrative 唯一现存的接口点，步骤与 `dialogue-extension-guide.md`「接入条件源」一节相同，此处从 Narrative 侧重述：

1. 在 Narrative 侧（或专门的接线类——不能放 `Game.Narrative` 目录并引用 `Game.Dialogue`，依赖方向不允许）实现
   `Game.Dialogue.IDialogueConditionSource.Snapshot(string targetId)`，从真实的玩家/目标/剧情状态拼出 `EncounterContext`。
2. 实现必须廉价、无副作用、不抛异常——它会被 Dialogue 每 0.25 秒定时调用一次，外加每次选项提交时再调一次。
3. 在 `DialogueInstaller.Install`（`DialogueInstaller.cs:51`）把 `DefaultDialogueConditionSource` 的注册换成新实现，
   然后 `git rm` 删掉 `DefaultDialogueConditionSource.cs` 及其 `.meta`。
4. 这一步通常需要 `NarrativeController` 已经能提供「当前遭遇的目标状态」——如果 `NarrativeController` 还没做（T6），
   先做一个只读真实玩家/剧情标记状态、暂不联动阶段机的最小实现也可以，不必等全部接线完成。

## 把 Narrative 真正接进 Unity（`NarrativeController` + Installer）

这是当前最大的缺口（module-guide「未接线清单」T6），不是「扩展」而是「从零接线」，按 `follow-up-integration.md`
第 4.2 节的阶段分派表起步：`Condition` → 调 `ResolveAutomatic`；`Dialogue` → 找 `PayloadId` 对应内容启动对白；
`WaitAction` → 按需发起请求后等待结果；`Battle` → 生成 `EncounterId` 调战斗模块；`End` → 写结果或续接父阶段。
进入等待阶段时**先写入阶段身份和 `ActionRequestId`，再订阅事件，最后发起请求**，避免同步完成的结果被漏掉。
落地前请先读 module-guide「接线顺序」一节，确认哪些 follow-up-integration.md 的原文已经过时。

## 不该从哪扩

- 不要把玩法判断（背包、任务、好感度……）塞进 `NarrativeRules`——它只管阶段迁移与结果校验，实际效果由所属玩法模块的公开入口应用。
- 不要新起第二套条件/规则解释框架；条件永远走 `NarrativeCondition.Matches`，遭遇仲裁永远走 `EncounterRules`。
- 不要在 `Game.Core` 里加剧情名词；`Game.Narrative` 已经是最合适的落点。
- 不要为了图省事让 `NarrativeContent` 支持运行时热改内容结构——它的校验设计前提是「构造即定型」，运行时改字段会绕过全部结构校验。
