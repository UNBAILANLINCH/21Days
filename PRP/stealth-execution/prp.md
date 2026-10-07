# PRP: 背后处决交互（S3 潜行与暗杀的收尾件）

> 状态：草案（2026-10-07 建立），待 Q3 接线波释放 `EncounterStep.cs` 与独占 `GameInput.inputactions` 的窗口后执行。
> 上游真源：[`06_怪物状态与交互设计文档.md`](../docs/design/spotlight/06_怪物状态与交互设计文档.md)（**2026-10-07 入库的策划原件**）、[`03_潜行与暗杀.md`](../docs/design/features-spotlight/03_潜行与暗杀.md)、[`06_怪物分层.md`](../docs/design/features-spotlight/06_怪物分层.md)
> 相关：[`S组落地总规划.md`](../docs/planning/S组落地总规划.md) §1.5（② 类工作）、[`stealth-module-guide.md`](../ai-docs/docs/modules/stealth/stealth-module-guide.md)、[`story-facts.md`](../ai-docs/docs/story-facts.md) §4.2

---

## 1·上下文快照

### 1.1 已经就位（不要重造）

| 能力 | 位置 | 说明 |
| --- | --- | --- |
| **判定内核** | `Runtime/Stealth/AssassinationRules.cs` | `Evaluate(...)` → `AssassinationVerdict.Allowed`；拆开的零件也都在：`IsBehind`（`:194`）、`IsInRange`（`:215`）、`IsTargetUnaware`（`:219`）。`AssassinationInput` 收 `TargetAlive` / `TargetAware` / `AttackerSneaking` 等快照量，**不认识 `MonsterModel`** |
| **判定门的出口** | `Runtime/Stealth/StealthDecisionGate.cs` | 逐帧产出 `AssassinationAllowed`（能不能下刀）与 `AlreadyAssassinated`（是否已杀过）；**两者刻意分开**（一个持久事实、一个瞬时能力） |
| **物种门槛的数据** | `Tables/Defines/yao.xml` 的 `defeat_method` 列 + `YaoCatalog.DefeatMethodOf(yaoId)`（`:159`）/ `IsKillable`（`:146`） | 白名单五值含「**暗杀**」，逐条附真源行号；`killable`（能不能常规杀）与 `defeat_method`（怎么杀）的分工已写死在列注释里 |
| **场景里的交互形状可抄** | `Runtime/Dialogue/DialogueInteractionActor.cs` | 独立交互 actor 的既有写法（读 Action、选目标、有焦点概念） |
| **表现钩子** | `Runtime/Monster/EncounterSceneView.cs` | 怪物的视图与朝向都已接线；`MonsterRules` 的 `Facing` 可读 |

### 1.2 缺口

1. **`Gameplay` 输入图里没有 `Execute` 动作**（现有：Move/Confirm/Cancel/Pause/Sneak/Disguise/Tame/Attack/Run/Immersive/Interact/Journal/Mirror/MirrorSelf）。策划原件写的是**背后按 `F`**。
2. **判定结果没有任何消费方**：`AssassinationAllowed` 算出来了，但没人读它、没人响应按键、没人执行。
3. **`stealth.assassinated` 从未被写过**（接线波**刻意**不写——它拿「能不能下刀」去填「已经杀过」会把内容条件弄假成真；正确写法归本 PRP）。
4. **「哪些怪能处决」没有任何代码在读** `defeat_method`。

---

## 2·架构决策

### 2.1 「能不能处决」是两个条件的合取

```
可处决  =  ① 位置与察觉条件成立（AssassinationRules.Evaluate 的 Allowed）
        ∧  ② 这个物种允许被处决（YaoCatalog.DefeatMethodOf(yaoId) == "暗杀"）
```

**② 的依据**：`06_怪物状态与交互设计文档.md:69` 原文「**对于部分怪物。**玩家可以在怪物背后按F处决」——「部分」就是这条门槛；
而 `yao.xml` 的 `defeat_method` 白名单里「**暗杀**」那一项的注释原文写着「**只能绕背处决，走不了常规击杀**」。

**这条设计是自洽的**：`defeat_method = 暗杀` 的怪通常 `killable = false`（`MonsterRules.ApplyDamage` 对它们**拒伤**），
所以**绕背处决是它们唯一的击杀途径**——不是「多一种打法」，而是「唯一一种」。

**⚠️ 不要**把 `killable` 当门槛：`killable=true` 且 `defeat_method=可击杀（方式没写）` 的怪（当前表里两行都是）**不该**能被一键处决，
否则绕背变成万能解；而 `killable=false` 也可能是「特殊条件 / 需收服 / 不可杀」，那些**不该**走处决。

### 2.2 输入

新增 `Gameplay/Execute` 动作，绑定 **`F`**（键鼠）与手柄一个键（选谁由实现定，写进模块指南）。

- **要动 `Assets/_Project/Data/Input/GameInput.inputactions`**——这是**共享点**，本 PRP 必须独占一波（与 `Boot.unity` 的改动不要放同一波做两件事）。
- 改完 Unity 会重新生成 `GameInput.cs`，**那是生成物、不许手改**。
- 落 `InputCommand` 位掩码的问题：**本 PRP 不进确定性内核**（见 §2.4），所以**暂时不需要新按钮位**；若将来要让回放能驱动处决，再补 `InputCommand` 并在同一次改动里同步位断言测试。

### 2.3 代码落点

新建 `Runtime/Stealth/ExecutionInteractor.cs`（或同类名；**在 `Runtime/Stealth/` 而不是 Monster**——判定与门槛都是潜行的职责，Monster 不该反向依赖 `YaoCatalog` 的语义）：

- 一个 MonoBehaviour，读 `Gameplay/Execute`，做「找最近的可处决目标 → 请求判定 → 成立则执行」。
- **执行的三件事**（按顺序，缺一不可）：
  1. **写 `stealth.assassinated` 事实**（这次是**真的命中了**，与「能不能下刀」区分开——见 §2.5）；
  2. **让目标死亡**：走 `MonsterRules` 的既有死亡路径；因为 `killable=false` 的怪会**拒常规伤害**（`MonsterRules.cs:385`：`if (model.Health <= 0 || !IsKillable) return false;`），所以这里需要一条**显式的处决入口**（`MonsterRules` 加一个方法，或让本组件置 `Health=0` 并触发既有死亡流程）——**二选一，实现时定**。
     > ⚠️ **写作本 PRP 时的一处更正（留痕，别照抄错版）**：初稿曾写「`ApplyDamage` 的注释已写明『这三条路不该复用本方法』」——**逐行核对后该注释并不存在**，那是某一波**交付报告**里的说法、代码里没落地。就地把分工说明补进代码是本波的一部分：真实依据在 **`MonsterKind.cs:102-104`**（`DefeatMethod` 的文档写明「与 `IsKillable` 一起用才完整：查勘使是 `false` + 特殊条件，籍中吏是 `false` + 不可杀」）。**不补的话，下一个人会看不出「`ApplyDamage` 拒伤」与「处决能杀」为何并存，然后去『修』掉它。**
  3. **回写战斗结果**：若这场遭遇以处决收束，按 [`battle-to-narrative/prp.md`](battle-to-narrative/prp.md) 产出一个 `BattleResult`（`Victory`），**不要自创结果键**。
- **动画**：`06:63` 写「进入被处决的动画」。本波**只留钩子**（一个可注入的表现回调），美术未定（`roadmap.md` D6 等 Spine）。

### 2.4 为什么不进确定性内核

处决是**玩家实时输入**触发的交互，而 `EncounterStep` 的 tick 是确定性的。本波把它做成**输入 → 立即结算**，
与 `DialogueInteractionActor`（读 `Interact` 拉起对白）同一层。

**代价要说清**：这样处决**不可回放**（回放跑的是确定性 tick，喂不进实时按键）。若将来要求回放能驱动处决，
需要补 `InputCommand` 位并在 tick 里结算——那时**必须**同步位断言测试与（若改快照）回放格式升版。

### 2.5 与 `stealth.assassinated` 的关系（本 PRP 最容易被做错的一处）

| 键 | 含义 | 谁写 | 何时写 |
| --- | --- | --- | --- |
| `AssassinationAllowed`（**不是键**，是 verdict 字段） | 此刻**能不能**下刀 | `StealthDecisionGate` | 逐帧算 |
| `stealth.assassinated`（**是键**） | **已经**用暗杀解决过目标 | **本 PRP** | **命中那一刻** |

接线波刻意没写这个键（拿前者填后者会让「站在守卫背后」等于「已经杀过他」）。**本波写它，但只在真的执行成功之后写**——
这是它与既有一切的分界线，也是[字典 §4.2](../ai-docs/docs/story-facts.md) 的原文口径（「命中即写，持久」）。

### 2.6 埋点

`stealth_execute_allowed` / `stealth_execute_rejected`（**带拒绝原因**：不在背后 / 超距 / 目标已察觉 / 物种不可处决）、
`stealth_executed`（成功）。**拒绝路径必须有点**——它是「玩家按了没反应」的唯一现场。

---

## 3·验证清单

机器可判（EditMode，组 `Game.Tests.EditMode.Stealth` + `Monster`）：

1. 四类拒绝各一条**负对照**：不在背后 / 超出距离 / 目标已察觉 / **物种 `defeat_method != 暗杀`**。
2. **`killable=true` 且 `defeat_method=可击杀（方式没写）`的怪不能被处决**（防止绕背变万能解）——用当前表里那两行数据直接测。
3. `defeat_method=暗杀` 的怪**可以**被处决，且处决后死亡（**即便 `killable=false`**，因为它是唯一击杀途径）。
4. 处决成功后 `stealth.assassinated` 为真；**执行失败时它必须仍为假**（区分能力与事实）。
5. 处决走的是**显式入口**而不是 `ApplyDamage`（用一条断言钉住：对 `killable=false` 的怪调 `ApplyDamage` 仍返回 false，而处决能杀死它）。
6. `F` 动作的绑定存在且是 `Gameplay` 图（**不要求**在 EditMode 里模拟按键）。

肉眼可验（回放）：在本波的 `Stealth` Showcase 里加一步——**潜行绕到背后按 F → 目标倒地**。
（该 Showcase 由 ② 类第一波建立；本波**扩展**它，不要另建一条。）

---

## 4·风险 / 回滚

| 风险 | 处置 |
| --- | --- |
| 动 `GameInput.inputactions` 是共享点 | **独占一波**，与 `Boot.unity` 的改动分开；改前确认该文件 `git status` 干净 |
| 「处决能杀 `killable=false` 的怪」与「`ApplyDamage` 拒伤」看似矛盾 | 这是**设计**不是 bug（`defeat_method` 的注释原文即「走不了常规击杀」）。但**必须在代码注释与测试里写清**，否则下一个人会「修」掉它 |
| 处决不可回放 | 明确登记为已知取舍（§2.4）；要改就得补 `InputCommand` 位 |
| 动画未定（等 Spine） | 只留钩子，不硬编表现；钩子为空时行为不变 |

---

## 5·需拍板 / 需协调

| # | 事项 | 卡住谁 | 出处 |
| --- | --- | --- | --- |
| 1 | **C88 的另一半**：处决「在什么状态下」能按 F（是否要求潜行、是否限「非敌对」、能否对已警觉目标处决） | 本 PRP 的门槛取值。**注**：C88 问的「哪些怪能处决」**已由 `defeat_method` 列自答**（白名单含「暗杀」） | `features-spotlight/待策划拍板问题.md` §C88；`03_潜行与暗杀.md:236` Q9 |
| 2 | 处决后尸体会不会被别的怪发现 | 影不影响「处决 → 触发追逐」链 | `03:236` Q9 |
| 3 | 处决动画规格 | 表现层 | `06_怪物状态与交互设计文档.md:63`、`roadmap.md` D6 |
| 4 | `GameInput.inputactions` 的改动窗口 + **Taming 是否解除「不接 Boot」** | 输入层与共享点排期 | `S组落地总规划.md` §5、`taming-module-guide.md:11` |
