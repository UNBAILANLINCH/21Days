# PRD: 对话系统（交互拉起 · 表现 · 世界时停）

> 日期：2026-09-25。前置：[`PRP/narrative-dialogue/prp.md`](../narrative-dialogue/prp.md) 已落地 Dialogue／Narrative 纯规则层与 View 骨架（未接线、未验收）。
> 本 PRD 只覆盖「对话表现闭环」：从可交互对象拉起一段对话，到对话结束把世界交还。遭遇触发、存读档 UI、剧情阶段衔接仍归 narrative-dialogue 后续。

## 问题 / 动机

玩家在探索场景里与 NPC 等可交互对象交互后，需要看到一段带立绘、可选择分支的对话；对话期间场景里的一切动态（怪物巡逻、动画、物理）必须停住，避免对话中世界悄悄变化；对话过程中的选择又要能改变游戏状态（剧情标记、任务、好感等由所属模块处理）。

## 目标（可度量）

- G1 与可交互对象交互后 1 帧内拉起对话面板；面板关闭后世界恢复。
- G2 对话期间 `Time.timeScale == 0` 且逻辑 tick 停止；Gameplay 输入图关闭；对话自身的打字、按钮、淡入淡出不受影响。
- G3 字幕流式输出；快速三连点补全当前句；补全后单点进入下一句。
- G4 右上角三个按钮：自动播放（开关）、倍速（x1 → x2 → x4 → x1 循环）、跳过整段对话（停在选项处等待选择，选完继续跳到结束）。
- G5 选项来自配置；每段对话是一棵带整数编号的对话树；同一编号可重复播放。
- G6 说话者立绘显示在对话框左上角或右上角（由配置指定侧别），非说话者一侧压暗。

## 范围 / 非目标

- 做：内容表（Luban）、内容适配、规则层补齐（两槽立绘、跳过）、表现策略（三连点 / 倍速 / 自动）、对话面板与历史面板预制体、世界时停服务、可交互组件与启动入口、验证场景与 Showcase、EditMode 测试、模块文档。
- 不做：感知触发的遭遇仲裁接线（NarrativeController）、存读档界面与对话中存档、跳过前的二次确认、语音 / 口型 / Live2D、非阻塞旁白（配置字段保留，本期一律阻塞）、已读快进开关（被「跳过」取代）。

## 玩家故事 / 关键场景

- 作为玩家，我点击（或触碰）场景里的 NPC，对话框从底部出现，背景里的巡逻怪物停住不动。
- 作为玩家，我看到字一句句打出来；着急时连点三下，整句立刻显示；再点一下进下一句。
- 作为玩家，我点右上角「倍速」把打字速度调到 x2、x4，再点回 x1；点「自动」后不用再点也会往下播，遇到选项会停下等我选。
- 作为玩家，我点「跳过」直接到本段对话结尾；中途有选项时停在选项处，选完继续跳。
- 作为玩家，我选了「我去找人」，对话结束后游戏知道我选了什么（后续模块据此改状态）。
- 作为开发者，我在场景里放一个带编号的可交互组件、在配置里写一棵对话树，不写代码就能接一段新对话。

## 验收标准

- [ ] A1 规则：`Skip` 从任意 Line 阶段快进到下一个 Choice 或 End，每句进历史并标已读；超过节点上限抛异常（EditMode）。
- [ ] A2 策略：Typing 阶段窗口内第 3 次点击返回「补全」，前两次返回「无」；AwaitAdvance 阶段单点返回「推进」；倍速按 [1,2,4] 循环；自动播放等待时长按倍速缩短（EditMode）。
- [ ] A3 内容：真实生成的 `tbdialogue` 每棵树都能构造成 `DialogueContent`（ID 唯一、跳转存在、选项出口唯一、槽位只有 0/1），角色表每个表情有资源地址（EditMode 内容检查）。
- [ ] A4 时停：对话进行中 `Time.timeScale == 0`、`SimulationRunner.IsPaused == true`、Gameplay 图关闭；结束后三者恢复（Showcase 检查点）。
- [ ] A5 表现：Showcase 回放能看到打字、三连点补全、倍速标签变化、自动推进到选项、条件不满足的选项被隐藏、选择后结束并返回 Outcome、跳过停在选项处（开发者看回放点头）。
- [ ] A6 交互：验证场景中对可交互对象调用公开的交互入口或点击其碰撞体都能拉起对应编号的对话；对话进行中重复交互被忽略。

## 涉及模块 / 依赖

- `Assets/_Project/Scripts/Runtime/Dialogue/`（`Game.Dialogue`，已存在，本期补齐并改表现语义）。
- `Assets/_Project/Scripts/Runtime/Narrative/`（只读复用 `EncounterContext` / `NarrativeCondition` 做选项条件）。
- `Assets/_Project/Scripts/Core/Timing/`：新增通用「世界暂停」服务（timeScale + 逻辑 tick，多持有者引用计数），暂停菜单等后续复用。
- `Tables/`：新增对话与角色表（Luban；树状内容用 JSON 输入，schema 用 XML 定义，见 PRP）。
- 新资产：`Prefabs/UI/DialogueView.prefab`、`DialogueHistoryView.prefab`、`Data/Dialogue/DialogueConfig.asset`、`Art/Sprites/Dialogue/` 占位立绘、`Scenes/Verify/Dialogue.unity`、Boot 场景挂 `DialogueInstaller`。
- 第三方：无新增包。

## 待确认问题（已按默认决定推进，可事后调整）

- Q1 跳过是否需要二次确认弹窗？默认不做（用户未要求）；若要加，只在 View 层加一个确认面板，不动规则。
- Q2 内容表用 JSON 而不是 Excel 作为 Luban 输入？默认 JSON（树状嵌套在 Excel 里难维护）；schema 不变，切回 Excel 只改 `input`。
- Q3 自动 / 倍速状态是否跨对话保留？默认每段对话开始时重置为「关 / x1」（用户明确「初始挡位为一倍速」）。
