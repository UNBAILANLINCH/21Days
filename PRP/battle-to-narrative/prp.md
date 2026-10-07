# PRP: 战斗结果 → 剧情（battle-to-narrative，roadmap C5）

> 状态：草案（2026-10-07 建立），待 Q0-D（Narrative 补战斗能力）回收后执行。
> 本 PRP 覆盖 roadmap **C5**：把「一场战斗怎么结束」接进剧情阶段机，让聚光灯的**四个战斗关阶段**（阶段一、三、六、九）能真正推进。
> 上游：[`00_功能总览.md`](../docs/design/features-spotlight/00_功能总览.md) §8.2 步 2/步 10、[`11_剧情流程与章节结构.md`](../docs/design/features-spotlight/11_剧情流程与章节结构.md)、[`03_潜行与暗杀.md`](../docs/design/features-spotlight/03_潜行与暗杀.md)、[`09_BOSS战.md`](../docs/design/features-spotlight/09_BOSS战.md)
> 相关：[`S组落地总规划.md`](../docs/planning/S组落地总规划.md)（波次与文件所有权）、[`story-facts.md`](../ai-docs/docs/story-facts.md)（`combat.*` 键）、`PRP/narrative-dialogue/`（C1–C3/B3）

---

## 1·上下文快照

### 1.1 已就位的能力（不要重造）

| 能力 | 位置 | 说明 |
| --- | --- | --- |
| 战斗阶段类型 | `Runtime/Narrative/NarrativeContent.cs` 的 `StageKind.Battle` | 五种阶段类型之一 |
| **战斗结果词汇** | `Runtime/Narrative/BattleOutcome.cs`（Q0-D 新增） | 四个有限常量：`Downed` / `Exposed` / `BossPhaseChanged` / `Victory`；`BattleResult` 结构带 `Variant`（BOSS 形态）与 `ExitKey`（`Kind` 或 `Kind:形态`），有严格 `Parse` 与宽松 `TryParse` |
| 战斗结算入口 | `NarrativeRules.cs:117-132` | `CanCompleteBattle(generation, activationId, targetId)` 校验战斗侧身份四项；`CompleteBattle(...)` 两个重载（字符串键 / `BattleResult`） |
| 内容校验 | `Runtime/Narrative/NarrativeCatalog.cs` | 已拒绝：非战斗阶段声明 `battleResults`、战斗阶段不声明结果、结果无对应出口、结果码重复/未知 |
| 阶段与出口 | `Tables/Defines/narrative.xml` | `Stage` 有 `battleResults`（`list,string`）与 `exits`（`list,Exit{result,next}`）；战斗关样例见 `Tables/Data/narrative/sample_battle.json` |
| 遭遇侧结果 | `Runtime/Monster/EncounterStep.cs` 的 `PendingResult` | **有字段，无消费方**——C5 的缺口正在这里 |
| 演出与转场 | `Runtime/Performance/`、`Core/Flow/ILoadingCurtain` | 战斗前后过场与黑幕已有，本 PRP 只用不扩 |

### 1.2 相关模块约束

- **Narrative 是阶段机，不管胜负规则**：它只回答「这个结果键在不在本阶段声明里、该走哪个出口」。胜负怎么判定属于战斗侧。
- **不新建第二套结果类型**：`EncounterStep.Result` 现有枚举是 `None/Victory/Defeat/Aborted`，`docs/roadmap.md:223` 已记它表达不了击倒/暴露/转阶段。**本 PRP 把它扩成能携带 `BattleResult`，而不是另起一个并行枚举**。
- **`chase.*` / `stealth.*` 的写入方另有其人**（见 `story-facts.md` §4.2/§4.3）。本 PRP 只负责 `combat.*` 与「战斗结果 → 阶段推进」这条链路。

### 1.3 必须规避的 pitfalls

- **回放快照**：若改到 `IReplayState` 已注册状态的序列化字段或注册顺序，**必须升 `ReplayFormat.CurrentFormatVersion`**（`Core/Replay/ReplayFormat.cs` 的判据是字节布局变化）。
- **`Boot.unity` 与 `GameInput.inputactions` 是共享点**：本 PRP 若要动它们，合并进 Q3 接线波，一次只许一个 agent 改。
- **生成物不手改**：改 `narrative.xml` 必须**同一次把 `Tables/Data/narrative/*.json` 补齐**再跑 `gen-tables.ps1`——只改 schema 会让**全表生成失败、卡住所有会话**（2026-10-07 已真实发生）。

### 1.4 适用规则

`.claude/rules/csharp-code.md`（不暴露 public 字段、每帧路径零分配）、`.claude/rules/unity-tests.md`、`docs/module-dev-spec.md`（DoD 六条）。

---

## 2·架构决策

### 2.1 唯一写入方：战斗侧写结果，剧情侧只读

```
Monster/Player（判定胜负）
        │  EncounterStep 结算出 BattleResult
        ▼
EncounterStep.PendingResult  ← 扩成携带 BattleResult（不再是裸字符串）
        │
        ▼
NarrativeRules.CompleteBattle(generation, activationId, targetId, BattleResult)
        │  身份四项对得上 + 结果在本阶段 battleResults 里
        ▼
NarrativeRules.Apply → Move(exitKey)  → 阶段推进
```

**只允许这一条路**。禁止在战斗侧直接改 `NarrativeSaveData`、禁止在剧情侧替战斗判胜负、禁止第二个模块写 `combat.*`（`story-facts.md` §6.1 的「一个键一个写入方」）。

### 2.2 `EncounterStep.Result` 的扩法：承载而不是替换

现有 `None/Victory/Defeat/Aborted` 是**遭遇流程的收尾状态**（"这场遭遇结束了没有"），而 `BattleResult` 是**玩法语义的结果**（"玩家是被击倒、被看穿还是打赢了"）。两者不是一回事，所以：

- `EncounterStep` 保留现有 `Result` 枚举不动（它管流程收尾）；
- **新增一个可空属性**携带 `BattleResult`（例如 `BattleSettlement`，`null` = 未结算或非战斗收尾）；
- `PendingResult` 的消费方从「裸字符串」改为读这个属性。

**理由**：改 `Result` 枚举会动到 `EncounterStep` 的既有序列化与回放快照（触发升版），而新增可空属性只要不写进快照就不升版。**若必须写进快照，按 1.3 升版并同步测试。**

### 2.3 四种结果各自的流程语义

| 结果 | 剧情侧做什么 | 战斗侧做什么 | 真源 |
| --- | --- | --- | --- |
| `Downed`（被击倒） | 走 `Downed` 出口（通常是「整场重来」或「接追逐」） | 击倒状态机进入倒地；`00` §5 C5 的两套口径由策略注入，本 PRP 不裁 | `03_潜行与暗杀.md:85-86`、`00` §8.1 #4 |
| `Exposed`（被看穿） | 走 `Exposed` 出口；写 `identity.exposed`（**由 Identity 模块写，不是本 PRP**） | 露馅判定命中 | `02_身份暴露与怀疑.md`、`00` §8.1 #3 |
| `BossPhaseChanged:<形态>` | **不推进阶段**：战斗还在同一阶段里继续，只是 BOSS 换了形态 | BOSS 阶段推进 + 写 `combat.phase.<n>` | `09_BOSS战.md`、`00` §8.1 #7 |
| `Victory`（胜利） | 走 `Victory` 出口，推进到下一阶段 | 小怪清空 / BOSS 收押 | `11_剧情流程与章节结构.md` |

**`BossPhaseChanged` 的特殊性**：它是唯一**不换阶段**的结果。实现上要么让它在 `battleResults` 里声明并配一个 `next` 指向**本阶段自己**，要么让 `CompleteBattle` 对它有特判（不 `Move` 只回写形态）。**二选一，在实现时按内容侧最省事的那个定，并写进阶段结构规范**（`ai-docs/docs/stage-structure-spec.md`）。

### 2.4 `Victory` 与既有「Success」出口的衔接

现有 `NarrativeRules` 的通用出口走 `NarrativeIntent.Result`，默认词是 `Success`（`Apply` 在 `:108` 按 `Stage.Exits.ContainsKey(intent.Result)` 查）。战斗关的出口键是 `BattleResult.ExitKey`（如 `Victory`），**两套词必须在内容侧分开**：

- 非战斗阶段的出口继续用 `Success` / 自定义词；
- **战斗阶段的出口必须用 `BattleResult.ExitKey` 的取值**，`battleResults` 与 `exits[].result` 必须一一对上（校验器已强制）。

Q0-D 的失败用例 `CompleteBattle_..._AdvancesByItsOwnExit("Victory","cleared")` 报的是 `Expected: "Victory" But was: "Success"`——正是这条衔接没接上。**实现本 PRP 时先修这个根因，别改测试断言去迁就。**

### 2.5 「镜碎」页的处置（E6 的落地）

`roadmap.md:245` E6：镜碎页随 Mirror 冻结，**由本 PRP 的 `Downed`/`Exposed` 流程取代，落地时替换或删除**。

- 玩家血量归零当前弹镜碎页、重开本场（`Runtime/Mirror/`）。
- 本 PRP 落地后：玩家失败走**剧情侧的失败出口**（重来 / 追逐 / 脚本化结局，按 §2.3 的策略注入），镜碎页**删除或降级为不可达**。
- **不删 Mirror 模块本体**（`YaoCatalog` 与妖物表仍被复用，阶段九「随身镜识破水族」可能复用照镜能力），只处理这一条流程。

### 2.6 埋点

模块 `narrative` 与 `monster` 各补一条：`battle_settled`（结果键、阶段 id、是否被接受）、`battle_result_rejected`（身份对不上 / 结果未声明，附原因）。**拒绝路径必须有点**——它是「内容写错」的唯一现场。

---

## 3·验证清单

机器可判（EditMode，组 `Game.Tests.EditMode.Narrative` 与 `Game.Tests.EditMode.Monster`）：

1. `BattleResult` 四种结果各自走对出口；`BossPhaseChanged:<形态>` **不换阶段**且形态可读。
2. 身份四项（generation / activationId / targetId / 阶段类型）任一不匹配 → `CompleteBattle` 返回 `false` **且不推进**（旧回调不得推进阶段）。
3. 结果不在本阶段 `battleResults` 里 → 返回 `false`，埋点有 `battle_result_rejected`。
4. 战斗阶段出口键与 `battleResults` 不一一对应 → **内容校验期就拒绝**（不是运行时才发现）。
5. 非战斗阶段声明 `battleResults` → 拒绝。
6. 失败口径（击倒 / 暴露）**不硬编**：换策略实现后同一份内容行为随之改变（用两个假策略各跑一遍证明）。
7. `EncounterStep` 的结算能携带 `BattleResult`；**未结算时为 `null`**；非战斗收尾不产生战斗结果。
8. 镜碎页不可达（玩家失败不再弹它）。

肉眼可验（回放）：在 SampleScene 的验证遭遇上跑一条战斗关回放，能看到「打赢 → 推进」「被击倒 → 走另一出口」两条路径；报告与截图落 `Logs/verify/narrative/`。

---

## 4·风险 / 回滚

| 风险 | 处置 |
| --- | --- |
| 改回放快照导致反复升版 | 按 §2.2 优先「新增可空属性、不写进快照」；确需写进快照则一次升版并把四种结果都测到 |
| `BossPhaseChanged` 的两种实现方式（自指出口 / 特判）以后要改 | 先按内容侧最省事的做，但**把结论写进 `stage-structure-spec.md`**，避免第二个人猜 |
| 四个战斗关的阶段一设计未定（`00` §8.1 #1） | **不阻塞**：本 PRP 只做「结果 → 推进」的机制，四个关的**内容**等拍板后再进表；先用 `sample_battle.json` 这类样例证明机制 |
| 与 Q0-D 的失败用例重叠 | Q0-D 收尾时会修掉 `Victory`/`Success` 衔接；本 PRP 若在它之前开工，**先确认那条用例已绿**，避免两边同时改 `NarrativeRules.cs` |

---

## 5·需拍板 / 需协调（不阻塞机制实现）

| # | 事项 | 卡住谁 | 出处 |
| --- | --- | --- | --- |
| 1 | 挨打后果口径：击倒（多久、怎么起身）还是扣血死亡；本体能不能攻击 | `Downed` 出口指向哪 | `00` §8.1 #4、§5 C4/C5 |
| 2 | 身份暴露后果：变回原主死亡，还是触发追逐；死后从哪重来 | `Exposed` 出口指向哪 | `00` §8.1 #3、§5 C1 |
| 3 | BOSS 形式（回合制 / 叠音游 / 动作战）与「本体打不过任何怪」怎么并存 | `BossPhaseChanged` 的形态语义 | `00` §8.1 #7、§5 C9/C10 |
| 4 | 阶段一用 sp02 / sp03 / 拼 | 四个战斗关的**内容**（机制不受影响） | `00` §8.1 #1 |
| 5 | **`Boot.unity` / `GameInput.inputactions` 的改动窗口** | 接线（若需要新动作） | 本文 1.3、`S组落地总规划.md` 第 2 节 |
| 6 | **Taming 是否解除「不接 Boot」** | 与 S1 共用绕背判定时的接线方式 | `taming-module-guide.md:11` |
