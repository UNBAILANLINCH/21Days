# 对话系统 实施记录

按 [`prp.md`](prp.md) 执行。状态只在有源码 / 检查 / 运行证据时更新；未勾选不表示完成。
派单模型按 `.claude/rules/model-routing.md`；波内任务互不依赖，可并行。

| 波 | 任务 | model | 范围 | 验收 |
| --- | --- | --- | --- | --- |
| W1 | T1 规则与策略层 | opus | `DialogueContent`（两槽）、`DialogueRules.Skip / OnChoiceSelected`、`DialoguePlaybackSettings / Policy`、`DialogueResult`、三个事件结构、`DialogueConfig` 字段；EditMode 测试 | A1、A2 |
| W1 | T2 内容表与目录 | opus | `Tables/Defines/dialogue.xml`、JSON 数据、跑生成、`DialogueCatalog`；内容检查测试 | A3 |
| W1 | T3 世界暂停服务 | opus | `Core/Timing/IWorldPauseService + WorldPauseService`、根作用域注册、architecture.md 一行、EditMode 测试 | A4（服务侧） |
| W2 | T4 运行时接线 | opus | `DialogueController`、`DialogueView`、`DialogueService`、条件源、`DialogueInteractable`、`DialogueSceneBinder`、`DialogueInstaller`；编译零错误 | A4、A6 |
| W3 | T5 资产与场景（MCP） | opus | 占位立绘、两个预制体、Config 资产、Addressables、Boot 挂 Installer、`Scenes/Verify/Dialogue.unity` | A5、A6 |
| W3 | T6 Showcase | opus | `Tests/Showcase/Dialogue/DialogueShowcase.cs` 两条回放 | A4、A5 |
| W4 | T7 验证与沉淀 | 主窗口 + sonnet | EditMode 全量、`/verify-module Dialogue`、code-reviewer、`/generate-doc dialogue`、登记、gc、`/review-change` | 全部 |

## 进度

- [x] 现状摸底、PRD、PRP。
- [x] T1 规则与策略层（2026-09-25：两槽、`Skip`、`OnChoiceSelected`、策略类、结果与事件结构；Dialogue 分组 17/17 通过，lint 零违规）。
- [x] T2 内容表与目录（`Tables/Defines/dialogue.xml`、1001/1002 JSON、角色表；`DialogueCatalog`；内容检查 7 条通过；缺表用例已改为对表数量不敏感；module 已改名 `dialogue`，生成物在 `Generated/dialogue/`，数据文件 `dialogue_tbdialogue*.bytes`；定向 13 条通过）。
- [x] T3 世界暂停服务（`Core/Timing/IWorldPauseService + WorldPauseService`，根作用域已注册，architecture.md 5.8 已补；7 条测试通过）。
- [x] T4 运行时接线（`DialogueController/View` 重写，`DialogueService`、条件源、`DialogueInteractable`、`DialogueSceneBinder`、`DialogueInstaller`；主窗口复核：编译零错误，EditMode 230 条、3 条失败均与本轮无关）。
- [x] T5 资产与场景（4 张占位立绘、`DialogueView/DialogueHistoryView` 预制体、`Data/Dialogue/DialogueConfig.asset`、Addressables UI 组 6 条、Boot 挂 `DialogueInstaller`、`Scenes/Verify/Dialogue.unity`；全部由 MCP 创建并读回核对）。
- [x] T6 Showcase（`DialogueShowcase.cs` 三条回放 + 基类 `ResolveService<T>`；主窗口用 MCP 跑 PlayMode：3 条通过、0 检查点失败、0 异常，报告 `Logs/verify/dialogue/latest.md`）。
- [x] T7 验证与沉淀（code-reviewer 首轮 NEEDS-CHANGES 三条 WARN → T4b 修完复核 PASS；`ai-docs/docs/modules/dialogue/` 三件套 + catalog + modules.json 登记；gc 仅报他人未提交的字体资产）。
- [ ] 开发者视觉验收：请自行运行 `/verify-module Dialogue` 看 Game 视图回放并点头（DoD 第 4 条）。
- [ ] `/review-change` 授权后提交（本轮未提交）。

## 环境事实

- Unity 编辑器已打开（`21days@ed015a78`，Boot 场景），MCP 可用。
- Luban 工具已解压到 `Tools/Luban/`（gitignore），`scripts/gen-tables.ps1` 可跑；生成物行尾为 CRLF，git 归一化为 LF。
- 工作区已有他人未提交改动：`Assets/Settings/*`、`ProjectSettings/QualitySettings.asset`、`Art/Fonts/*SDF.asset`；本次不碰。

## 执行中确认的事实

- 既有失败用例（本轮之前就有，本轮未碰相关文件）：`ReplayFormatTests.FormatConstants_AreFrozen`（`ReplayFormat.CurrentFormatVersion` 在 d5a9d12 升到 3，测试仍期望 2）、`JsonSaveServiceTests.LoadAsync_WhenStoredVersionIsOlder_CallsMigrateOnce`（同一提交改了 `JsonSaveService`）。归协作者的 narrative-dialogue 后续处理，本轮不改。
- 编辑器里有他人并行操作的痕迹（`M_SpriteDepthClip.mat` 创建失败、`21Days/SpriteDepthClip` 着色器错误、URP / QualitySettings 改动），与本轮无关；MCP 改场景时逐步存盘、只动本轮文件；对方曾进过 Play 模式，改场景 / 跑测试前先读 `mcpforunity://editor/state` 确认不在 Play。
- `execute_code` 在本编辑器走 codedom（C# 6）编译：脚本别用新语法，`UnityEngine.UI` 类型写全名。
- `GameBootstrap` 无重复实例保护：多条用例各自加载 Boot 的 Showcase 需在 `[UnityTearDown]` 销毁旧 `GameLifetimeScope`（`DialogueShowcase.DestroyBootScope`）。

## 追加（用户要求放进 SampleScene）

- [x] `Assets/Scenes/SampleScene.unity` 新增 `Npc_Elder`（1001）、`Npc_Traveler`（1002），`Main Camera` 加 `PhysicsRaycaster`；Play 冒烟全链路通过（Boot → 开始 → 绑定 → 拉起 → 时停 → 跳过 → 选项 → 结束 → 恢复），0 错误。保存时连带写入了他人未保存的根节点顺序对调（`Environment_Graybox` / `Directional Light`），已告知用户。
- [x] 修 `DialogueController` 收尾对未打开历史面板调 `CloseAsync(null)` 的 `null_view` 警告（两处判空）；Dialogue 组 27/27。

## 二期（prp.md 第 8 节）

| 波 | 任务 | model | 范围 |
| --- | --- | --- | --- |
| W5 | T7b Boot 兜底相机让位（prp 8.0） | opus | `Core/Boot/FallbackCamera.cs`、Boot.unity 挂组件、Play 截图验证 |
| W5 | T8 范围交互、NPC 标记与常驻气泡（prp 8.1、8.5） | opus | `DialogueInteractionActor`、`DialogueInteractionFocus`、`DialogueInteractableMarker`（替代 Hint）、`DialogueInteractHudView` + 预制体、标记占位图、Installer 注册、SampleScene / Verify 场景接线、EditMode 测试 |
| W5 | T9 跳过确认与选项样式 | opus | `DialogueSkipConfirmView` + 预制体、Controller 覆盖态、`Choice.icon` schema / 数据 / 生成 / Catalog / Controller 加载 / View、`DialogueView` 预制体选项区改右侧胶囊、Showcase 更新 |
| W6 | T10 验收 | 主窗口 | 编译、EditMode、Showcase、SampleScene 真实指针冒烟、code-reviewer、文档 sync |

- [x] T7b（`Core/Boot/FallbackCamera.cs` 挂 Boot 相机；进遭遇后 `Camera.main` 为 SampleScene 相机、Boot 相机禁用；截图 `Logs/verify/dialogue/camera-check-gameview.png`；「回标题恢复」路径未实测）。
- [x] 一期补：点击路径实测通畅（真实指针 → PhysicsRaycaster → NPC）；`InRange` 改 3D 距离；`DialogueInteractableHint` 临时提示（将被 T8 的 Marker 取代）。
- [x] T8（`DialogueInteractionActor / Focus / InteractHudView / InteractableMarker / SpeechBubble`，Hint 已删；HUD 与气泡预制体、标记占位图；SampleScene 三个 NPC（含无树村民）与玩家 Actor、Verify 场景同步；EditMode Dialogue 33/33，Showcase 6/6，SampleScene 冒烟通过，截图 `Logs/verify/dialogue/smoke/`）。
- [x] T9（`DialogueSkipConfirmView` + 预制体；Controller 覆盖态；`Choice.icon` 进表 + 图标异步加载；`DialogueView` 选项区改右侧胶囊；Showcase 增「取消跳过继续」；注意：为修动态字体图集坏页对他人未提交的字体资产执行了 Clear Dynamic Data）。
- [x] T10（主窗口全量 EditMode 248 条仅 2 条遗留失败、编译零错误；二期 code-reviewer PASS，其 WARN（HUD 打开竞态）与 INFO（历史面板退订对称）已修；`/generate-doc sync dialogue` 已同步三件套；气泡高度改为随文字自适应；Showcase 6 条主窗口复跑见下）。
- [ ] 开发者视觉验收：从 Boot 进 Play 点「开始」自己看；`/verify-module Dialogue` 点头。
- [ ] `/review-change` 授权后提交。
