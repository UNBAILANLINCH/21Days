# Narrative／Dialogue 后续对接指南

> **2026-09-25 更新**：第 3 节（对白接线）已过时——其中三槽立绘、`advance` 按钮、已读快进 Toggle、
> 调用方自行 `rules.Start` + `PresentAsync`、`SetPaused` 等做法均已被 `PRP/dialogue-system/` 的实现取代。
> 对白接线以 [`ai-docs/docs/modules/dialogue/dialogue-external-api.md`](../../ai-docs/docs/modules/dialogue/dialogue-external-api.md) 为准；
> 第 4、6 节（剧情 / 存档）仍可参考。以下正文保持原样。

> 状态：接线指南，基于当前工作区源码整理。
> 适用范围：把已经落地的纯规则、对白表现骨架、战斗快照和存档候选接口接入 Unity 运行时。
> 当前结论：规则层可以独立检查；Unity 场景、Addressables、Luban 内容和完整存读档事务还没有验收。

## 1. 先看当前完成边界

已经存在的运行时代码：

| 范围 | 入口 | 当前能力 |
| --- | --- | --- |
| Dialogue | `DialogueRules` | 台词节点、打字阶段、补全文、选项重新校验、历史、已读和恢复 |
| Dialogue | `DialogueController` | TMP 可见字符、立绘加载取消、动态选项、历史面板和已读快进的表现接线 |
| Narrative | `NarrativeRules` | 阶段迁移、条件节点、WaitAction、行为结果、局部遭遇父阶段续接 |
| Narrative | `EncounterRules` | 条件匹配、Once／Reenter／RisingCondition、优先级和稳定目标仲裁 |
| Save | `ISaveService`、`JsonSaveService`、`SaveSnapshot` | 候选读取、显式提交、独立玩家档案、串行磁盘 IO |
| Monster／Player | `EncounterStep`、`PlayerSaveData`、`MonsterSaveData`、`EncounterSaveData` | 战斗实例身份、生命／AI／随机流快照、待消费终局结果 |
| Simulation | `SimulationRunner` | 多暂停原因、tick 提交事件、禁止递归推进 |

当前没有完成的部分：

- 没有 `NarrativeController`、`GameSessionController` 和槽位界面。
- 没有 Dialogue／Narrative Installer 的根作用域注册。
- 没有 Dialogue、Narrative Prefab、验证场景和 Showcase。
- 没有剧情 Excel／Luban 表及内容检查器。
- 没有 Unity Editor 编译、PlayMode、真机和视觉验收证据。

因此，后续工作应该围绕“接线和验证”展开，不要再复制一套规则类。

## 2. 推荐接线顺序

按下面顺序接，每一步完成后再进入下一步：

1. 建立最小内容对象：一段 Line → Choice → End 对白，以及一个 WaitAction → Battle → End 剧情。
2. 建立角色表情地址索引，把 `CharacterId + ExpressionId` 映射到 Addressables Sprite 地址。
3. 在 Boot 根作用域注册 `DialogueRules`、`DialogueController`、`NarrativeRules`、`EncounterRules`。
4. 创建 `DialogueView` 和 `DialogueHistoryView` 预制体，地址必须分别等于类名。
5. 先接“对白开始、选择、结束”，再接遭遇触发；不要一开始把所有玩法结果接进按钮。
6. 接 `SimulationRunner.SetPaused`：阻塞对白和历史窗口持有自己的 pause owner，关闭时只释放自己的 owner。
7. 接真实战斗：先 `StartBattle`，tick 提交后消费结果，再把结果转换为 `NarrativeIntent`。
8. 接 `GameSessionController` 的保存、候选恢复和失败回滚。
9. 最后接 Excel／Luban、Prefab、Addressables、验证场景和 Showcase。

## 3. 对白接线

### 3.1 启动一段对白

调用方负责准备内容和规则状态，然后启动表现控制器：

```csharp
dialogueRules.Start(content);
dialogueTask = dialogueController.PresentAsync(
    () => encounterContextProvider.CreateCurrent(),
    cancellationToken);
```

`PresentAsync` 只负责 UI 和资源生命周期。它不会替 Narrative 启动阶段，也不会替玩法执行选择后的行为。

### 3.2 View Inspector 引用

`DialogueView` 必须显式拖入：

| 字段 | 要求 |
| --- | --- |
| `speaker` | 一个 TMP 文本 |
| `body` | 一个 TMP 文本，支持富文本 |
| `portraits` | 长度必须为 3，顺序为左／中／右 |
| `advance` | 推进按钮 |
| `history` | 历史按钮 |
| `skip` | 已读快进 Toggle |
| `choiceRoot` | 动态选项父节点 |
| `choiceTemplate` | Button 预制模板，运行时默认隐藏 |

`DialogueHistoryView` 需要一个 TMP 文本和关闭按钮。View 不注入服务，按钮只发 `DialogueIntent` 或事件。

### 3.3 Addressables 规则

- `DialogueView` 预制体地址必须是 `DialogueView`。
- `DialogueHistoryView` 预制体地址必须是 `DialogueHistoryView`。
- 立绘 Sprite 的地址由角色表提供，不要在 View 里硬编码。
- 表情缺失时由 `DialogueController` 回退默认表情；默认图也加载失败时隐藏槽位并记录诊断。
- 切换节点前释放旧 `AssetHandle<Sprite>`；异步加载完成后必须再次检查 `Generation` 和 `Visit`。

### 3.4 输入和暂停

`DialogueView` 的提交入口统一经过 `DialogueController.Submit`。提交前检查：

1. 当前对白仍在运行。
2. 当前会话没有被暂停或历史窗口覆盖。
3. 当前 `Generation` 和 `Visit` 仍匹配。
4. 当前节点仍允许该动作。

阻塞对白接入方式：

```csharp
simulation.SetPaused(dialogueController, dialogueController.BlocksWorld);
```

实际项目中应由会话协调器每次状态变化后统一重算暂停权限，避免某个窗口关闭时误解除另一个窗口的暂停。

## 4. Narrative 接线

### 4.1 事件进入

感知／交互模块只报告事实，不直接打开对白：

```text
感知事实
  → EncounterContext
  → EncounterRules.TryActivate(candidates)
  → NarrativeRules.EnterEncounter
  → 根据 Stage.Kind 调 Dialogue、WaitAction 或 Battle
```

`EncounterContext` 当前支持：玩家存活、潜行、伪装、目标存活、目标敌对、目标已发现和剧情标记。
新增事实时先扩展上下文和条件校验，再扩展内容表；不要从 UI 颜色或 Transform 推断条件。

### 4.2 阶段分派

建议 `NarrativeController` 只做分派：

| `StageKind` | 控制器动作 |
| --- | --- |
| `Condition` | 调 `ResolveAutomatic(context)`，继续处理出口 |
| `Dialogue` | 找 `PayloadId` 对应 DialogueContent，启动对白 |
| `WaitAction` | 可选地发起一次请求，然后等待结果或部分完成 |
| `Battle` | 生成 EncounterId，调用 `EncounterStep.StartBattle` |
| `End` | 写入结果；若有父阶段则恢复父阶段，否则结束主剧情 |

进入等待阶段时，先写入阶段身份和 `ActionRequestId`，再建立事件订阅，最后发起请求。这样即使请求同步完成，也不会漏掉结果。

### 4.3 真实行为结果

对白选项只表示玩家意图，不代表行为成功。行为模块完成后创建：

```csharp
new NarrativeIntent(
    narrative.Generation,
    narrative.Current.ActivationId,
    narrative.Current.TargetId,
    narrative.Current.ActionRequestId,
    resultCode,
    optionalPart);
```

调用 `NarrativeRules.Apply` 前必须确认：

- `Generation` 没有因读档或取消而变化。
- `ActivationId`、`TargetId`、`ActionRequestId` 全部匹配。
- `resultCode` 是当前阶段配置的出口。
- `optionalPart` 属于 `RequiredParts`，且成功出口已经配置。

重复结果必须被拒绝；失败和取消不得当作成功。

## 5. 战斗接线

### 5.1 开始与恢复

新战斗：

```csharp
encounterStep.Begin(view.PlayerStart, view.PatrolPositions());
encounterStep.StartBattle(encounterId, narrative.Current.ActivationId);
```

恢复战斗：

```csharp
encounterStep.Restore(savedEncounter);
```

恢复入口禁止调用 `Begin`，否则会重置生命、位置、AI 计时和随机流。

### 5.2 结算边界

`EncounterStep` 在 tick 内只写入 `PendingResult`。接线层订阅 `SimulationRunner.OnTickCommitted`，在整个 tick 完成后消费：

```text
tick 完成
  → 读取 PendingResult
  → ConsumeResult(EncounterId, ActivationId)
  → 将 Victory／Defeat／Aborted 转成 NarrativeIntent
  → NarrativeRules.Apply
  → 开放下一阶段或继续探索
```

结果消费成功前不能发奖励或切剧情。保存发生在结果消费后的稳定边界；若结果尚未消费，快照必须保留待处理结果。

### 5.3 战斗暂停

战斗输入权限和世界模拟暂停是两件事：

- 对白／历史窗口打开时，暂停相关模拟并阻断战斗输入。
- 非阻塞旁白只改变对白表现，不暂停战斗。
- 不使用 `SimulationRunner.Mode.Driven` 冒充剧情暂停。
- 不依赖 `Time.timeScale` 作为唯一暂停机制。

## 6. 存档与读档事务

### 6.1 保存分区

稳定边界至少保存：

- `NarrativeSaveData`：当前阶段、父阶段、ActivationId、请求、部分完成记录和消费标记。
- `DialogueSaveData`：会话、节点、阶段、解析后的文本、历史和立绘槽位。
- `EncounterSaveData`：EncounterId、ActivationId、tick、玩家快照、怪物快照和待处理结果。

跨槽位的已读集合使用 `DialogueReadData` 独立保存，不放入剧情槽位。

### 6.2 候选读取

读档必须分成候选读取和提交：

```csharp
SaveSnapshot oldSnapshot = saveService.Capture();
SaveSnapshot candidate = await saveService.ReadCandidateAsync(slot, ct);

// Runtime 校验 candidate，恢复场景和模块
// 全部成功后：
saveService.Commit(candidate);
```

校验失败时保留旧内存状态，不覆盖磁盘槽位。恢复过程中要屏蔽输入和迟到回调；失败后通过旧快照重建旧场景。

### 6.3 保存时机

以下区间不能直接保存：

- Dialogue 仍处于 `Preparing`，文本尚未由 TMP 解析完成。
- Narrative 正在迁移阶段或提交行为结果。
- Battle tick 尚未提交，或者终局结果已产生但尚未消费。
- SceneGameState 正在加载、卸载或绑定场景对象。

请求应排到下一个稳定边界；取消和 IO 失败要返回明确错误，不能静默当作保存成功。

## 7. 内容表对接

Luban 表最终至少需要以下逻辑字段：

| 内容 | 必要字段 |
| --- | --- |
| 剧情阶段 | `StoryId`、`StageId`、`Kind`、`PayloadId`、出口结果和后继阶段 |
| 条件 | `FactKind`、`Key`、`Expected`、条件组 ID |
| 遭遇规则 | `RuleId`、触发类型、目标类型、优先级、入口阶段、重复策略 |
| 对白节点 | `ConversationId`、`NodeId`、`Revision`、`Kind`、说话者、文本、后继节点 |
| 选项 | `ChoiceId`、文本、条件组、不可用表现、节点出口或结果码 |
| 立绘 | 槽位、Action、`CharacterId`、`ExpressionId` |
| 角色表情 | 角色名、默认表情、表情 ID、Sprite 地址 |

导入适配层把生成表转换为 `DialogueContent`、`NarrativeContent` 和 `EncounterRules.Rule`。适配层必须在运行前检查：

- 重复 ID、失效跳转和不存在的资源地址。
- 选项同时配置节点出口和行为出口。
- 条件类型未接入。
- 同优先级规则冲突。
- Condition 自动环路、没有出口的等待和不可达节点。

## 8. Unity 资产接线清单

### Boot 根作用域

需要在 `GameBootstrap` 上挂一个新的玩法 Installer，注册：

- `DialogueConfig` 实例。
- `DialogueRules` 和 `DialogueController`。
- Narrative 内容目录和 `NarrativeRules`。
- 遭遇规则目录和 `EncounterRules`。
- 需要时注册 `NarrativeController`、`GameSessionController`。

Installer 只在 `Install` 中注册依赖，不在安装期间 Resolve 或启动剧情。

### Addressables

- UI 预制体加入 `UI` 组，地址等于 View 类名。
- 验证场景加入 `Scenes` 组，场景地址不能与 View 类名冲突。
- 立绘加入角色表引用的资源组。
- 逐项运行资产体检，确认脚本、Prefab 和地址均存在。

当前没有可用 Unity MCP，因此本清单只记录接线要求，没有声称场景资产已完成。

## 9. 验证顺序

### 规则层

当前手动规则检查入口为 `scripts/narrative-rule-check.cs`，最近一次结果：

```text
Pure rule checks: 7 passed, 0 failed.
Unity runtime checks not run.
```

覆盖对白推进／恢复、动态选择、已读快进、剧情父阶段、旧结果隔离、条件环路和遭遇优先级仲裁。

### Unity 接入后必须补跑

1. Unity 控制台零编译错误。
2. Dialogue EditMode 和 Narrative EditMode 全量通过。
3. Storage 测试覆盖候选读取、损坏档、并发保存和高版本拒绝。
4. Monster／Player 恢复对照测试覆盖生命、位置、AI 计时和随机流。
5. Dialogue Showcase：打字、换表情、选择、历史和已读快进。
6. Narrative Showcase：潜行、可交谈、敌对、战斗胜负、局部遭遇和父阶段续接。
7. Save Showcase：打字中、选择前、行为等待和战斗中保存恢复。
8. 最后由开发者查看 Game 视图，确认安全区、触控按钮、选项和立绘布局。

## 10. 完成判定

后续对接只有同时满足以下条件才可以把任务标为完成：

- `PRP/narrative-dialogue/tasks.md` 中 T3–T9 均有对应证据。
- Unity Editor 编译成功，控制台没有新增异常。
- EditMode、PlayMode 和 Showcase 均有报告。
- Prefab、Addressables、Boot Installer 和 Verify 场景都已接线。
- 存档恢复失败时旧场景和旧 Model 可重建，原槽位不被覆盖。
- `ai-docs/docs/modules/dialogue/` 与 `ai-docs/docs/modules/narrative/` 三件套已根据实际源码生成并登记。
- 运行 `gc_scan.py`、模块文档检查和最终 diff 检查。

在这些证据齐全前，提交信息应描述为“基础接入”或“规则基础”，不要描述成“完整剧情系统已完成”。
