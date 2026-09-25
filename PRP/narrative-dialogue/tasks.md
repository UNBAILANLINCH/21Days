# Narrative／Dialogue 实施记录

用户已明确要求按 prp.md 开发；范围与设计检查点已通过，不重复请求设计批准。
状态只在有源码、检查或运行证据时更新；未勾选项不表示完成。
后续 Unity／Luban／存读档接线按同目录的 `follow-up-integration.md` 执行。

| 顺序 | 实施范围 | 验收 |
| --- | --- | --- |
| T1 | Dialogue 内容模型、规则、意图、历史、已读与恢复；EditMode 测试 | A01、A03–A05、A10、A18 |
| T2 | Narrative 条件、遭遇仲裁、阶段／父阶段恢复、结果意图；EditMode 测试 | A08、A17、A19–A23 |
| T3 | Core/Save 候选读取／提交、独立档案、安全写盘、串行访问；存储测试 | A06、A13、A14 |
| T4 | Player／Monster 快照、战斗开始／恢复、暂停、终局结果；恢复对照测试 | A07–A09、A11、A12 |
| T5 | DialogueController、View／HistoryView、Config、Installer；输入与资源生命周期 | A01–A05、A07、A18、A21 |
| T6 | NarrativeController、真实状态／交互接线、GameSessionController、SaveSlotsView | A08–A14、A17、A19–A22 |
| T7 | Excel／Luban 内容、校验器、验证对白与遭遇内容 | A16、A23 |
| T8 | Boot Installer、UI 预制体、Addressables、验证场景及 Showcase | A02、A07–A09、A15、A17、A20 |
| T9 | lint、编译、测试、模块审查、模块文档三件套、登记与健康检查 | A01–A23 |

## 进度

- [x] 读取已确认 spec、技能与工作区状态。
- [x] T1 对白规则与测试（纯规则检查通过）。
- [x] T2 剧情／遭遇规则与测试（条件、父阶段、仲裁规则检查通过）。
- [ ] T3 存储能力与测试（候选提交、独立档案与串行 IO 已实现，Unity 存储测试待跑）。
- [ ] T4 战斗恢复与暂停（快照、结果关联、tick 提交回调已实现，Unity 集成待跑）。
- [ ] T5 对白运行时与 UI（View、历史面板、资源生命周期已实现，Prefab/Addressables 待接线）。
- [ ] T6 剧情接线与统一存读档。
- [ ] T7 内容管线与校验。
- [ ] T8 Unity 资产接线及 Showcase。
- [ ] T9 检查与模块文档。

> 2026-09-25 说明：T5（对白运行时与 UI）、T7 中的对话表、T8 中的 Dialogue 预制体 / 验证场景 / Showcase
> 已由 [`PRP/dialogue-system/`](../dialogue-system/prp.md) 以不同表现语义完成（两槽立绘、跳过取代已读快进、JSON 内容表），
> 上面的勾选保持原样未改。后续 T6 / T9 与 Narrative 半边接线，以 `PRP/dialogue-system/prp.md` 的
> `IDialogueConditionSource`（条件事实来源，替换 `DefaultDialogueConditionSource`）与 `DialogueService.PlayAsync`（拉起入口）为对接点，
> 现行接口见 `ai-docs/docs/modules/dialogue/dialogue-external-api.md`。

## 资产与环境

验证场景目标为 `Assets/_Project/Scenes/Verify/Dialogue.unity`、`Narrative.unity`；
Showcase 目标为 `Scripts/Tests/Showcase/Dialogue/DialogueShowcase.cs`、`Narrative/NarrativeShowcase.cs`。
源码与资产具体范围沿用 prp.md 第 13.3 节，新增必要 DTO 与意图各自独立文件。
场景／预制体只经 Unity 创建，不手写 YAML 或 meta；没有可用 Unity MCP 时记录未接线项目。
保留任务开始前工作区已有场景、设置、探索文档及美术改动。不提交、不推送。
