# 21Days 交接文档 — 未完成事项

> **给谁看**：接手本项目的开发者。
> **前置阅读**：`CLAUDE.md`（硬规则与目录约定）→ `ai-docs/project-guide.md`（共用约定）→ `docs/architecture.md`（框架层与各服务契约）。
> **接手备注（2026-09-30）**：上一会话把「连点补全下沉 Core、演出字幕接连点、可对话 NPC 挡人、laila 上界分析、`run_tests` 回放范围守卫」写进工作区后额度耗尽中断，没来得及提交。接管会话复核后按主题分 8 条提交并推送：编译零错误，EditMode 全量 **1088 条通过**，`python .claude/hooks/tests/run.py` **69 条全过**。它中断时留了两个尾巴，都已处理——编辑器里一个 32 分钟未收的孤儿测试标志（`TestRunStatus.IsRunning`，把 `refresh_unity` 一直挡成 `tests_running`；已补坑册与 MCP 排查表）与「记录面板『关闭』压住『跳过』」（关闭上移 30px，并补了布局守卫测试）。
> **最近核对**：2026-10-05，只读核对源码、资产和提交记录，未重跑 Unity。音游实现与文档分别已在 `15c94f6`、`2b4664d` 本地保存；核对时 `main` 比 `origin/main` 多 15 条提交，不再沿用 2026-09-30 的同步状态。
> **工作区边界**：当前混有 Laila、音游及资产/设置的并行改动，不是干净工作区。两个 TMP 动态字体会在 Play 或真实 View 测试时积累字形；不得为清检查擅自清理共享脏资产。保留原暂存和未提交内容，不整仓暂存、回滚或清理，以实时 `git status` 为准；字体处理见 `ai-docs/pitfalls.md`「TMP Dynamic 字体资产」。

> **进行中，不重复派工**：音游正在处理分级校准可信度、失败候选复用试听与固定曲 Combat；Laila 正在处理 105＋3 怒候选训练与回归。开始任务不代表已实现或验收。Boot 接线/正式音游 UI prefab 迁移暂缓，一曲一谱、不做多难度，两新曲听感暂不调；本机打包由开发者执行。

> **接手备注（2026-10-07，系统层 / roadmap E 组）**：接着上一会话未提交的 E4 改动往下做，工作区里这批尚未提交（等授权）。做完的事——E4 加载黑幕：`LoadingCurtain` 补相位守卫（首次开面板期间被揭幕不再淡入、视图被销毁后仍能重盖、揭幕出错必须回 `IsCovered=false`）、暂停菜单的「黑幕在盖」并进可测的 `ShouldOpen`、回放加 `WaitCurtainRevealed`（场景重载后不再截到半透明黑幕），新增 `LoadingCurtainTests` 5 条；E1 尾巴：「已保存」从通知队列改走不进队列的右下角小字（`INotificationService.ShowCornerHint` + `UIConfig.CornerHintSeconds` + `NotificationView` 的 `Corner/CornerLabel` 子节点，深底小牌避开编辑器右下角相机调试工具条），新增 `NotificationServiceTests` 7 条，Session 回放补检查点与截图「保存后·右下角小字」。
> 证据（全部本次实跑）：编译零错误、控制台 error / warning 0；EditMode 全量 **1115 / 1115**；Session 回放 **2 / 2** PASS（`Logs/verify/session/20261007-022334/report.md`）；Exploration 回放 **8 / 8** PASS（`Logs/verify/exploration/20261007-022419/report.md`）；`gc_scan` 只剩既有 4 条（LailaFace 命名空间 3 条 + 字体资产动态字形，见 §1.3 与 §2）。`docs/roadmap.md` 的 §0 第 6 条、E1 / E4 / W3 已同步。

> **接手备注（2026-10-07，S 组地基波 + 接线波）**：本轮按《聚光灯》S 组把**四个纯规则内核**做完并全部通过独立复核，**工作区尚未提交**（300+ 个文件，等授权）。做完的事——`Game.Identity`（S1/S2 身份状态机、六种露馅、账簿、怀疑度）、`Game.Stealth`（S3/S4 视线遮挡、绕背暗杀、击倒状态机、追逐、召唤编队、固定追逐 + **背后按 F 处决**）、`Game.World`（A4/A6 场景/区域/传送点三表、待处理转场、出生点选择、跨场景状态、相机约束）、`Game.TurnBased`（S7 回合制 BOSS 战内核，48 个文件）；外加步 1 怪物种类数据化、道具八类别与合成/使用规则、`Narrative` 补 `Battle`/`IssueRequest`/`RequiredParts` 三类能力。
> **证据（收尾快照）**：全量 EditMode **1836 / 1836 通过、0 失败**（基线 1552 → **净增 284 条**；`completed == declared_total`、`stale=false`、`orphan=false`，并已字节检索确认新测试类在 DLL 里——**四重反假绿核验**）。各组一手数字见 `docs/roadmap.md` §2.2（本轮已按主窗口实测更新：Monster **103**、Stealth **164**、Identity **55**、World **141**、TurnBased **159**）。
> **② 类验收（「功能在实例场景里真的做出来」）——四份回放报告 3 PASS / 1 FAIL**：`identity` **PASS**（失败 0：身份生效时怪物不出手 / 失效后恢复挨打）、`stealth` **PASS**（失败 0：掩体挡住视线 / **遮挡不挡追击** / 潜行到背后 / 可处决）、`turnbased` **PASS**（失败 0：醉酒四档逐档可见）、`world` **FAIL（检查点失败 13 个，12 条是「场景没加载起来」的下游）**。报告在 `Logs/verify/{identity,stealth,turnbased,world}/`（本地生成物，不入库）。
> **规划与台账**：`docs/planning/S组落地总规划.md`（波次、文件所有权矩阵、**§1.5 三类工作分界**、**§4.1 已派波次实际提出的拍板项 19 条**、验收纪律）、`ai-docs/docs/story-facts.md`（跨模块状态字典：键名规范 + V1–V3 校验 + 写入方唯一 owner）。两份新 PRP：[`PRP/world-scenes`](PRP/world-scenes/prp.md)、[`PRP/stealth-execution`](PRP/stealth-execution/prp.md)。
> **本机复跑工具**：`uv run --offline --with mcp python scripts/unity_mcp_check.py --console|--state|--refresh|--test|--meta`（主窗口无原生 MCP 工具时用它做独立复核）。⚠️ **必须加 `--offline`**：不加时它会回 PyPI 解析包，网络偶发失败会以**退出码 2 + stdout 零字节**结束，看起来像「脚本没输出」而不像「连不上」。
> **⚠️ 四种「看起来成功其实失败」的假象都在本轮踩到过**，验收前务必读 `S组落地总规划.md` 第 6 节：假绿（`run_tests` 返回 `succeeded` + `completed:1`）、孤儿任务（`completed:0` + `Job cleared manually`）、**陈旧程序集**（`refresh_unity` 没触发测试程序集重编，`run_tests` 照样返回旧数字——硬判据是**字节检索 DLL 里有没有你的新类型名**，不是时间戳）、**注册方式错导致生命周期钩子不触发**（`Register` 而非 `RegisterEntryPoint` → `IStartable.Start` 永不调用 → 「东西都在却一个都登记不到，且零报错」）。
> **⚠️ 读 `Editor.log` 的正确判据**（本轮经两轮修正）：**比「最后一次成功重载」与「最后一条 `error CS`」的行号先后**——`$ok > $err` 才算编译通过。单看「最后一次成功重载之后」在**编译一直失败**时会误判（成功行停在很久以前，段里混着已修好的旧错误）。
> **⚠️ `Game.Tests.EditMode` 引用 `Game.Editor`**——所以 `Scripts/Editor/**` 里的任何编译错误会**卡住所有 EditMode 测试**（本轮实测被卡约 20 分钟）。并行多波时该目录的修复优先级应高于其它文件。
> **⚠️ 埋点属性上限 4**：`ITelemetryScope.Track` 最多 `(evt, p0..p3)`，`TelemetryProps.Capacity = 4`。**不要为多塞属性改用 `TrackWarn`**（那会改事件级别）。

## 0. 现状一句话

主流程在完整工作区已跑通：标题页 → 进入探索场景 → 潜行 / 战斗 / 对话 / 演出 / 任务 / 存档。Narrative 依赖的 DialogueService 遗漏片段已补交；补交后的定向回归与人工回放验收已有记录，但尚未在独立干净检出上完成最终提交验证。本轮验证不代表全仓所有测试已重新跑过。

**本轮验证范围**：Performance EditMode 150 项、演出 Showcase 6 项通过，失焦键盘自测与输入设置恢复检查通过；演出回放报告为 `Logs/verify/performance/20260929-042650/report.md`，失焦自测报告为 `Logs/verify/selftest/20260929-025916/report.md`（本地生成物，不入库）。角色隐藏已覆盖直接播放、对白插入及场景触发；演出期间世界 Tick / 玩家位置不变、Timeline 继续推进，结束后世界恢复。时停已验证，无需进一步修改。用户已完成一次视觉验收，暂未发现问题。

**后续验证与当前交付边界**：

- Core/Dialogue/Mirror/Narrative/Quest/Session 联合 EditMode 647/647、Dialogue/Mirror/Narrative PlayMode 16/16，均零失败零跳过；这是拆分提交前的完整工作区结果。Narrative 最新记录另有补交后 EditMode 22/22、三条回放通过及用户人工确认，详见 §1.2 和 `PRP/narrative-dialogue/tasks.md`。
- 气泡补修已提交并完成 review-change：井边妇人旧副本接回共用预制体，组件增加视口约束与临时缩小/恢复。Dialogue EditMode 125/125，零失败零跳过；两条气泡回放任务成功，报告 `Logs/verify/dialogue/20260929-075616/report.md` 为 PASS，覆盖村民侧面、妇人侧面与正面。最终 MCP result=null，不补推 passed/skipped 细分；该报告检查点失败 0、运行时异常 0。
- 气泡正面图为同目录 `03-井边妇人·正面接近气泡.png`，已核对姓名、正文和边框完整；用户视觉确认尚未收到。最后一次读取 Unity 为 Boot、无未保存场景、控制台 error=0。
- 提交拆分依据见 `PRP/commit-batches-2026-09-29.md`。它是历史清单：C2 气泡及顶部四文件补交现均已提交，不能照旧“待提交”状态重复暂存；其它批次也须与 Git 实时对账。

**框架可运行与正式内容完成是两回事。** 当前已有 `Game.Core`、运行时模块、主流程、编辑器工具、回放与埋点体系，外加照镜 demo（`PRP/mirror-core`）；剩余验收和内容工作见下文。

**没做的分两层，量级差很远：**

- **§1 / §2 是已有模块的收尾与待拍板事项**，具体范围见下面的清单。
- **§3 是内容层，仍有大量待定**：2026-10-06 起策划以《聚光灯》为准（`docs/design/spotlight/`，玩法拆分见 `docs/design/features-spotlight/`），照镜 demo（`PRP/mirror-core`）随旧版策划冻结。`docs/roadmap.md` 的 C1–C3/B3/W2 状态已同步，旧“20 条完全没开始”不再作为当前统计。这一片仍需策划与美术输入。
- **⚠️ 2026-10-07 口径修正（此前这行写「换皮附身、潜行暗杀、身份暴露、追逐、皮与面具、关卡专属机制与真实章节内容都还没做」——已不准确）**：S 组的**机制内核已经做完且测试全绿**（见顶部接手备注），现在缺的是**三件不同的事**，请分开看：
  1. **接线**（进行中）：`IdentityInstaller` / `StealthInstaller` 正在挂上 `Boot.unity`，`EncounterStep` 的遮挡体与身份绑定正在接——**在此之前这些内核在生产里是死代码**。
  2. **实例场景里先做出功能（不需要等美术/策划）**：S3 的处决交互（**背后按 F**）、视线的场景几何、A6 的相机边界体与死区、S7 的战斗场景与招式格/怒气槽 UI——**这些都在 `SampleScene` 上做白盒即可**，项目规范把「回放舞台」定义为实现模板（`module-dev-spec.md`：正式场景接同一功能**只改内容不改接法**）。
     **实测缺口**：`loot` / `inventory` / `identity` / `stealth` / `world`（+ 未登记的 `TurnBased`）**都还没有 Showcase**——EditMode 全绿但按 DoD 第 3 条**不算做完**。A4 的两界流转要的是**两张灰盒场景**，不是正式场景。
  3. **正式内容（等外部输入）**：真实章节与剧本进表、每只怪的数值（等策划）；两界场景美术、立绘、UI 皮肤、角色动画（等美术）；S6 关卡机制的具体内容（等 `00` §8.1 #1）、S8 小游戏（等 §8.1 #8）、B4 调查界面的数据形状（等 §8.1 #1）。
- **2026-10-07 两份策划原件补交**（`design/spotlight/06_怪物状态与交互设计文档.md`、`07_回合制作战文档.md`）把两件事从「等拍板」变成「可做」：**S3 的感知与处决规则**（75° 扇形、警戒 4 秒升满/6 秒降 0、敌对 1.25×、背后按 F 处决、巡逻每 7–10 秒站定 2 秒作刺杀窗口、要潜行才免的近距察觉——**与既有实现基本吻合，数值不用动**）与 **S7 的回合制规格**。新增歧义见 `features-spotlight/待策划拍板问题.md` 的 C86–C92。

一句话：**框架能跑，S 组机制的「骨头」已经立起来并测试全绿；缺的是接线（进行中）、在实例场景里把功能做出来（不需要等外部输入，量最大）、以及正式内容（等策划与美术）。**

---

## 1. 可以直接开工

### 1.1 最终提交的独立验证

- 原“遗漏提交”已关闭：DialogueService 的稳定 targetId 重载、Completed 传递及三份契约文档均已入库，不再要求补交。
- 剩余工作是在隔离的干净检出上记录最终提交号，完成编译和相关联合回归；不通过清理或回滚共享工作区来制造干净状态。
- 补交后 22/22 与三条 Narrative 回放属于工作区验证；此前 647/647、16/16 也不能代替此项。尚无独立检出验证已执行的证据。
- 完成后回填 `PRP/narrative-dialogue/tasks.md` 的最终提交独立验证项，并关闭本节；不重复实现已有接线。

### 1.2 Narrative 接线的剩余验收

C1–C3/B3 的运行接线、真实条件源、最小表/校验器、任务完成映射剧情标记与 Session 稳定状态保存已实现并有回归证据，不要再创建第二套控制器或存档系统。

- 人工视觉确认已通过：最新实施记录载明用户观看慢速回放后反馈“好像是没问题的”。对应 `Logs/verify/narrative/20260929-094939/report.md`（实时刷新）与 `Logs/verify/narrative/20260929-095052/report.md`（取消/重试/跳过、保存/继续），记录均为检查点失败 0、运行时异常 0；验收仅限这三条覆盖行为。
- 回放关键画面的 `Wait(3f)` 阅读停顿已提交，x2 时约停 6 秒；不再列为未提交改动。上述新增运行证据来自 Narrative 实施记录，本次交接更新未重跑 Unity。
- 未覆盖：C5 对抗结果（聚光灯 S2 / S3 / S7；旧 G5 冻结）、全目标生命周期中断、全场景恢复事务、对白逐节点/逐字恢复及真实章节内容；详见 PRP 的“未纳入本批的验收欠账”。
- 调试探针曾在未传生命周期令牌、对白仍运行时直接停止 Play，出现 ObjectDisposedException；正式回放正常收尾为零异常，不据此宣称任意根作用域突然销毁均已覆盖。
- `DefaultDialogueConditionSource` 保留作未装 NarrativeInstaller 的兼容来源；不要按旧交接要求直接删除。

### 1.3 仍待处理的小修与偶发问题

| 位置 | 问题 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs:4`、`FaceDragHandle.cs:4`、`MuralFaceController.cs:3` | 命名空间是 `Game.LailaFace`，与所在目录 `Runtime/Gameplay/` 不符；`invariants.py` 每次都报红，常红会掩盖新引入的问题。挪目录改命名空间，或反过来（`.claude/skills/generate-doc/modules.json` 里 `lailaface.src` 也指向这里，要一起改） |

`ProjectStructureMenu.cs:153` 的 TODO 位于 `BuildRulesTemplate` 生成字符串中，是新模块骨架的构造注入提示，不是现有服务缺陷，不再列维修任务。命名空间整理需等 Laila 并行修改冻结后再做。

Mirror 句柄释放/相机朝向、CharacterPuppet 重复辅助方法、对白气泡查找和失焦输入经验文档已处理并提交，不再列待办。
Performance EditMode 曾出现记录面板关闭等待 5 秒超时；单独复跑与随后 150 项全量复跑均通过，原因未定位。再次出现时保留状态与日志，不只增大超时。

### 1.4 Replay：实录已补，性能与窗口验收未完

五分钟六实体 DemoWorld 实录已提交：300.096 秒、18000 tick、691931 B；RecordTick 调用内分配 0 B，最大 0.221900 ms，有一次超过 0.2 ms。
不能把局部采样写成整帧性能验收通过。T18/G3 仍需真实玩法开关录制的逐帧 Profiler 对照、稳态 GC，以及 ReplayWindow 的真实交互验收。
报告 `Logs/verify/replay/20260929-064018/report.md`，细节见 `PRP/replay/tasks.md`。

### 1.5 UI 与探索的剩余验收

| 条目 | 尾巴 |
| --- | --- |
| A2 沉浸模式 | 玩家 / 巡逻者 NameTag 未随沉浸隐藏 |
| E8 分辨率基准与画面适配 | `SetResolution` 与窗口拖拽**未出包实测**；编辑器验证不代替 Player，打包由开发者执行 |
| H7 无鼠标悬停反馈 | NPC 悬停高亮未做 |
| Inventory | 面板已实现并提交，尚无 Inventory Showcase；DoD 的回放/视觉验收仍欠，见模块指南 |
| Session 保存提示 | `GameSession.cs:320` 仍调用通知队列，保存提示可能推迟玩法通知；角落提示尚未实现 |

已关闭两条旧资产待办：`DialogueView.prefab:1220` 已采用 SlideUp（transition=1）；UIButtonFeedback 的脚本 GUID 已出现在 Title、Dialogue、Quest、Inventory、Pause、Settings、SaveSlots 等预制体。D4/D5 不再属于“尚未采用”，本次只核对接线、不新增视觉验收结论。Loot/最小背包与 Inventory 面板已有 `ce99f0d`、`5d37eea` 提交；泛化交互不是当前 SupplyCrateFocus 的能力，不重新实现已有箱子/背包。

### 1.6 Codex MCP 与 hooks 工作流

- 手动诊断 `scripts/unity_mcp_probe.py` 与说明已提交；本会话原生 Unity MCP 初始化重试仍失败，实际测试通过临时协议客户端连接现有编辑器完成。临时客户端可用不等于各聊天原生 MCP 已恢复。
- “所有项目聊天自动注册 MCP、持续保活”尚未完成；不能把已注册项目 hooks 当成该能力已实现。连接成功以本会话实际读取目标编辑器为准，测试/域重载期间出现短暂断连时先等待再重连，不反复重启健康 bridge。
- `.codex/hooks/README.md`、`adapter.py`、`test_adapter.py` 的长文记账兼容修复已在 `538e602` 提交，不再列未提交。该提交不证明每个聊天已信任 hooks，也不证明 MCP 自动注册/保活完成。

### 1.7 laila 捏脸表情识别：阶段门 A 已过，从阶段门 B 接着做

规格是 [`PRP/laila-expression-recognition/spec.md`](PRP/laila-expression-recognition/spec.md)，**先读 §15 的审阅结论**。网络代码在 `ML/expression-recognition/`（独立 Python 项目，README 里有环境与命令）。

- **已定（2026-09-30）**：规格通过阶段门 A，12 维输入契约与按钮触发不变。**验收类别改为五类**：neutral、happy、sad、surprise_fear（惊讶与恐惧合并）、angry；厌恶是集外类，完整七类只作不承诺的争取目标。
  依据是上限分析 [`ML/expression-recognition/analysis/laila_upper_bound/REPORT.md`](ML/expression-recognition/analysis/laila_upper_bound/REPORT.md)：laila 现有 22 个形态做不出恐惧要的「又抬又皱」眉形，也做不出厌恶要的皱鼻，这两类在乐观上限下也只有 0.64 左右。
- **网络侧已做完**（`0f43f1e`）：类别集 `--label-set laila_5class`、按绑定剔除做不出来的合成变体、导出元数据的 ONNX sha256、评估器护栏（绑定不符直接报错、`golden_check` 查开发集和测试集不串组、`--tag` 分开报告）、12 维契约测试。不要在 Unity 侧再实现一遍「12 维 → 规范空间」的换算或 softmax。
- **下一步按顺序**：
  1. **阶段门 B 几何校准**（要用编辑器）：按 spec §5.3 逐轴看形变，写出 `ML/expression-recognition/configs/rigs/laila_rig.yaml`（`laila_v1`）和 `laila-binding-review.md`。先确认三件事：
     - `Brow_*_Up` 是否带内眉上抬；
     - 嘴角 Out 是不是横拉，In 是噘嘴还是抿紧（抿紧的话愤怒有救）；
     - `Mouth_UpperLip_Up.001` 是否带动鼻翼（决定厌恶能不能回来）。
     这个文件建好之前，契约测试里那一条会一直跳过，这是正常的。
  2. **Unity 侧接入**（spec §8–§9）：probs 的类别维是 **5**，不是 7（见 §15.2）。
  3. **采集工具与人工盲标**：要 3 个人独立盲标，这需要外部人手。仍按七类加「不明确」来采和标，评估时自动映射成五类。
  4. **正式训练与导出**：命令见 spec §7.1，额外加 `--label-set laila_5class --drop-infeasible-variants`。
- **别做**：
  - 不要用 `analysis/laila_upper_bound/` 里的两份候选绑定导出部署，它们的系数都没有经过几何校准；
  - 模型训练用了公开集的，一律不得随包发布，元数据里 `commercial_use_allowed` 会是 false。

---

### 1.8 Rhythm 四轨音游：已保存基线与进行中增量

本地保存基线：运行时、曲库资产/场景及完整测试依赖已提交为`15c94f6f1069efee4ff9f2202a42c647e94cd6e4`（2026-10-05 02:23:00 +08:00），文档随后保存于 `2b4664d`（02:26:00 +08:00），无历史重写或推送。下方各轮“未提交/待补验/交还场景”均是当轮历史节点，不代表当前工作区或编辑器状态；验收证据执行时间保持真实。当前分级可信度、失败候选试听与固定曲 Combat 增量仍进行中，不提前记为完成。

后续证据已覆盖曲库慢保存取消/旧任务、保存失败恢复、旧档原字节备份/幂等/未来档保留，以及校准 fixture 与非法时钟定向修复。选曲阶段旧“慢写/保存失败未验收”不再作为现行欠账；真实 OS 重启、设备切换、物理输出与真人校准仍未验收。失败回放继续保留，不把定向补验写成一轮全绿。

提交前最新真人主轮诊断（2026-10-05 03:25:01 UTC）：33输入/32匹配/32有效、MAD24.61ms、估计+151.53ms、原补偿+140.27ms，四块中位136.30/162.87/184.94/147.51ms，跨度48.65ms，结果Drift；数据完整，根因尚未解决。现有代码只允许Stable/Suggested候选，Drift数值与候选试听仍被隐藏；低可信试听只是提议，未实现。Boot迁移与正式UI prefab延期，一曲一谱、无多难度需求，两新曲听感暂不调整。

2026-10-05 真人校准诊断修复已应用：Drift/不可恢复结果禁用8拍补测，State也拒绝强制事件；显示分段跨度与20ms上限，不放宽阈值。本机JSON保留最近5轮，每轮256条/128KB，区分runtime与隔离测试；保存失败在结果页提示，原补偿不变。Unity编译/lint通过，EditMode44/44，提示微调后受影响33/33；Showcase2/2见`Logs/verify/rhythm/20261005-110306/report.md`，最终保存失败/取消补验1/1见`Logs/verify/rhythm/20261005-110957/report.md`，检查点失败/运行时异常均0。Unity已归还RhythmDemo，场景干净、非Play/编译、倍率1。历史真人失败缺原始样本，不能宣称真人已通过；继续排查仅需正常一轮并保留原值。[实施记录](PRP/rhythm-real-calibration-diagnostic-20261005/implementation-report.md)。无提交/打包/Laila修改；gc仍有4项既有问题。

**2026-10-05 两项缺陷已修复并定向验收**：非法时钟先守卫后诊断，明确 Reason 停局并清理；校准 fixture 的松开改为单调预定时间，真实 InputSystem 对照证实旧序列丢6/32拍得到Drift，修复后32/32得到Suggested（80ms、MAD45ms、分块差0），不改质量门。定向EditMode35/35；首轮六项4通过/2失败保留，修复fixture隔离后仅补验两项2/2，`Logs/verify/rhythm/20261005-031538/report.md` PASS、检查点失败0/异常0。六项各有通过证据，不冒充一次全绿或全仓重跑。Unity实际归还干净LailaRecognitionPlaytest、idle/非Play/无编译测试、timeScale/倍率1、Console error0，无临时测试场景。详见[本轮交付](PRP/rhythm-final-regression-fix-20261005/implementation-report.md)。三正式谱、Boot/prefab、Laila与asmdef未编辑，未提交/index/push/build；仍待物理/真人及音乐贴合度验收。

**2026-10-05 历史冻结验收**：最终同代次修复后EditMode171/171；八项定向Showcase7通过/1失败，五项新增曲库回放全部通过。确认式校准Suggested预设收到Drift，当时原因未定位，完整报告 `Logs/verify/rhythm/20261005-014637/report.md` 为FAIL，继续保留；此节点的待定位项已由上方本轮修复关闭。具体接口和文件边界见[实施记录](PRP/rhythm-library-integration-20261005/implementation-report.md)。

**曲库与乐师接口增量（2026-10-05）**：[当前实施与证据](PRP/rhythm-library-integration-20261005/implementation-report.md)。独立 RhythmDemo 已新增 runtime 滚动曲库与当前谱面详情、不可变完整单局结果、v2 个人纪录/legacy 分离/原字节备份和永久歌曲开放；一曲一谱。外部请求分驯服浏览与当前控制演奏资格，Combat 只接注入策略/成功失败 hook，不写个人纪录；未接真实乐师 adapter、Boot、正式 prefab、伤害值或失败惩罚。ClearExternal 供复用状态回普通自由局，消费者实例内去重不等于跨世界存档事务。现有世界暂停语义未改。首个冻结快照 EditMode171/171；之后已补校准/中断回调重入代次保护，最终运行结果以实施记录为准。首次 Showcase 请求因外部窗口进入Play而执行0项被拒，未擅停运行；后续五项曲库回放通过，历史整体7通过/1失败与定向修复证据见上方。三正式谱/项目设置hash不变，真实progress档实际不存在，故无空假备份；备份与IO故障测试仅隔离临时档。本批未提交、构建或上传。

**确认式校准收尾（2026-10-04）**：[实现与接口清单](PRP/rhythm-calibration-review-20261004/implementation-report.md)。固定8+32主轮、分类原因、分块覆盖/漂移检查、明确应用才保存、保留继续、原值/候选参考拍反馈和一次主动8拍补测已实现；试听中断回候选页显示原因，补测MAD上限取当前配置。跨轨组合复用Tap/Hold，仅补验证，无新类型/练习UI，三正式谱及项目设置hash不变。最终运行时代码EditMode131/131；旧生命周期与组合通过。新校准先后遇整数滑块断言及协程发键抖动两项fixture失败，保留历史报告，未放宽质量门；修正后定向1/1（95.47秒）、检查点失败0/异常0，报告`Logs/verify/rhythm/20261004-230245/report.md`。预设事件时间戳经真实InputSystem接线，不等于真人跟拍或物理延迟验证；质量阈值仍待真人验证。本批未提交、构建或上传。

此校准轮收尾实际读取：Unity为干净RhythmDemo三根节点、idle/非Play/无运行测试、倍率1、Console error=[]，无临时测试场景残留。文档gc仍有3处Gameplay命名空间不匹配与共享动态字体缓存，共4处；未改Laila并行代码或清字体。不是沿用上一选曲轮的Laila场景交还状态。

**选曲扩展交接（2026-10-04）**：[实际验收记录](PRP/rhythm-song-progression-20261004/stage-report.md)。正式三曲选曲、60% 达标、虫儿飞解锁两曲与独立最高分/通关档案已接入；两新谱为 164/229 枚自动测试谱，尚未人工音乐校准。Unity 编译成功，EditMode 120/120，原四项 Showcase 通过；首次新增长流程因默认 180 秒超时中止，保留该失败记录，已完成入门锁定/解锁与吉他全曲。文字布局已修，长测试明确 360 秒 Timeout；定向补验 1/1（81.66 秒）实际完成 Attention 229 Perfect、15 Hold、Miss=0，换曲清零、重试中断、实际写盘和重入恢复三曲进度通过，报告 `Logs/verify/rhythm/20261004-205648/report.md`。前置 fixture 明确预置已验证入门/吉他成绩，不冒充完整长流程重跑；截图核对无旧重叠，模块审查 PASS。编辑器已交还干净 LailaRecognitionPlaytest、非 Play、无测试、倍率 1、Console error=[]，项目设置 hash 未变。剩余人工试听/物理延迟、OS 重启与慢写/保存失败注入未验收；本批未提交或构建。

接手入口为 [四轨后续规格](PRP/rhythm-followup/spec.md)，当前实现见 [模块指南](ai-docs/docs/modules/rhythm/rhythm-module-guide.md)。**2026-10-04 已取消伪透视并提交等宽四轨（627f656）；本轮并行推进诊断、试听工具和运行时生命周期，Unity 自动验收已通过，人工验收仍待确认。** `PRP/music/spec.md` 是单键打印机候选玩法，不能覆盖当前四轨规则。

- **P0 实现**：有限容量诊断记录与纯逻辑回放已接入，结束后“保存本轮诊断”显式导出 `.rhd`，不写玩家 Profile；[诊断接口](PRP/rhythm-followup/diagnostics-api.md)。[试听定位工具](PRP/rhythm-audio-review-20261003/locator/README.md) 已有真实 Chrome 测试，项目根运行 `node PRP/rhythm-audio-review-20261003/locator/server.mjs` 后打开 `http://127.0.0.1:8766`；逐音符声部和候选仍须人工审核，未改正式谱。
- **P1 实现/待验收**：新增音频暂停/应用挂起、双时钟失配终止、Dynamic 模式契约、预约终点停音；真实 Unity 验收包含二十次重试与卡顿终点。设备对应校准档案未实现；人工手感、物理回环和 Player 实测仍待做。
- **P2**：评估外部制谱工具，候选为 osu!mania 4K Tap/Hold 子集转 canonical JSON 与严格校验（工具尚未选定）；维护等宽琴道可读性，保留可用古风/琴弦元素。透视不再是必做项，工时优先用于试听改谱、长按手感和校准。
- 保留 DFJK、简单 Hold 头判后按到尾自动成功、三 offset 独立和 0ms 首音符/负时间预滚；不增加尾判难度、不扩为整套商业音游。自动 median/MAD 校准是人的综合跟拍统计，不是纯硬件测量。

当前代码保留输入批次封口修复，诊断使用同一队列/规则，不让 DSP 推进输入判定。已有解码对比、诊断重放和浏览器工具不能代替听觉/物理延迟验收。2026-10-04 的 16 页 Word/PDF 研究报告已在云端交付，报告无本地路径；本轮只吸收局部生命周期和可回放能力，未移植整套架构。本轮不提交、构建、push 或上传，不干扰 Laila 并行工作。

本轮真实 Unity 2022.3.62f2 验收：初轮 EditMode **74/74**、Rhythm Showcase **3/3**，失败/跳过均 0；报告 `Logs/verify/rhythm/20261004-104628/report.md`。补齐导出/停止细因后 EditMode **88/88**、定向 Showcase **2/2**，报告 `Logs/verify/rhythm/20261004-110234/report.md`，检查点失败 0、非预期运行时异常 0。真实按钮事件生成四份独立 `.rhd`，均磁盘回读 Codec/Replay 一致，旧文件/哨兵未覆盖；故意占用目录产生一条预期 diagnostic_save_failed，实际通知显示失败，内存保留，恢复目录后按钮重试成功。测试输出在框架隔离目录并于收尾清除，不写玩家目录；UI 没有文件选择/取消对话框。

EndReason 已直接保存 AudioPaused/ApplicationPaused/InputModeChanged/ClockDiscontinuity/InvalidClock/LateInput，原数值和布局保持，新读取器兼容旧文件，旧读取器严格拒绝未知追加枚举。生命周期场景已验证暂停/模式细因；时钟细因有守卫/映射/codec 回归。前轮覆盖 56Tap 全命中/全漏、标题重入、±100ms Hold/早放、校准接受/拒绝；最新定向复验含 350ms 跨终点、二十次重试和暂停/模式。手感、人工声部与物理延迟仍待验收。

---

## 2. 等拍板，不要自己决定

| 事项 | 卡在谁 | 说明 |
| --- | --- | --- |
| 字体 SDF 动态数据膨胀 | 项目负责人 | `Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset` 本轮扫描为 37,090 KB，动态字形数据仍触发不变量检查；资产另有未提交改动，处理前与美术工作对齐，不能为让检查变绿直接清理 |
| 杂散场景副本 | 项目负责人 | 当前磁盘只找到 `Assets/Scenes/SampleScene1 1.unity` 及其 meta，旧清单中的 `SampleScene 1.unity` 不存在；不继续按“两份”派工。现存副本删还是留未定，本次不删除 |
| 任务「!」标记是否给台词气泡让位 | 项目负责人 | 灰色「…」会避让，任务黄「!」不让位，无树 NPC 冒气泡时可能被压住 |
| 任务面板底板透底 | 需实机复现 | 回放里正常、实机透底；怀疑与 `ProjectSettings/EditorSettings.asset` 的 Enter Play Mode Options 被测试运行器打开有关，未定位 |
| Mirror `visionLossPerCrack` | 项目负责人 | 现值 0.18，建议 0.3——1～2 道裂时暗角几乎看不见（Mirror 已冻结，暂不调） |
| 聚光灯玩法拆分的矛盾与待定 | 策划 | 见 `docs/design/features-spotlight/00_功能总览.md`；旧版 `docs/design/features/` 的 30 条矛盾 + 128 条待定随旧版冻结，不再追 |
| 四本素材 PDF（共 152 MB） | 项目负责人 | 移出仓库放共享盘，还是走 Git LFS |
| Run 动画帧、探索 3D 环境资产 | 美术 | 序列帧小人暂无 Run 动画（现复用 walk 剪辑）；探索环境仍是灰盒 |

---

## 3. 长线与内容层（等外部输入）

**这一节是长期工作**：3.1 已废弃，3.2–3.3 是两条长线，3.4 记录尚待推进的内容及系统项，不把已有接线重新列为未实现。

**3.1 Live2D** —— 已废弃（2026-09-28 用户定）：演出只保留世界舞台 + 序列帧小人，Live2D 适配层与叠加模式代码已随 `161c690` 删除，不再导入 Cubism SDK。

**3.2 移动端移植** —— 现阶段是 PC 游戏：触屏摇杆、触屏三键、走跑按钮、Android 画质档、安全区实机统一，全部后置到移植阶段。注意玩法代码里**禁止**平台条件编译与平台专属 API，这类只允许出现在 `Scripts/Runtime/Platform/`，输入只读 Input System 的 Action Map。

**3.3 美术替换** —— 探索场景从灰盒换成 3D 环境资产（模块化 Prefab + Lightmap）。纸片角色按**正面平视**画，不要按要求俯视 3/4 出图。

**3.4 内容与系统剩余方向**（roadmap 的 C1–C3/B3/W2 状态已同步，不沿用旧总数）

| 组 | 待推进或核对 |
| --- | --- |
| A 探索层 | A3 物资箱与最小背包已实现，泛化交互仍未做；A4 多场景流转、A6 相机边界与死区 |
| B 任务系统 | B3 已有接线与验证，最终提交独立验证见 §1.1；B4 进度重置与已完成列表 |
| C 叙事接线 | C1–C3 转 §1.1/§1.2 收尾；C5 对抗结果 → 剧情等待聚光灯 S2 / S3 / S7 规则，旧 G5 冻结 |
| D 演出与 UI 动效 | D6 正式角色动作资产仍缺，走跑程序支持已就绪；D4/D5 已有资产采用，剩余验收见 §1.5 |
| E 系统与流程 | E4 加载过渡已收尾待视觉验收（2026-10-07，相位守卫与回放等待），E8 出包验证 |
| F 内容与美术 | F1 真实剧本进表、F2 环境替换、F3 美术皮肤；F4 已有三首音游音乐，环境音/SFX/正式内容音频仍缺；F5 已有 Narrative 最小校验，完整内容校验工具未验收 |
| S 聚光灯核心机制 | S1 换皮与附身、S2 身份暴露与怀疑、S3 潜行与暗杀、S4 追逐、S5 皮面具与道具、S6 关卡专属机制、S7 BOSS 战、S8 小游戏；旧 G 组随旧版冻结 |
| H PC 适配 | 主要路径已实现，NPC 悬停、窗口实测及其它聊天持有项仍需逐项确认，不能写全部验收完成 |

**玩法内容清单 `docs/design/features-spotlight/`（聚光灯）**

13 份功能文档：01 换皮与附身、02 身份暴露与怀疑、03 潜行与暗杀、04 追逐、05 皮面具与道具、06 怪物分层、07 关卡专属机制、08 小游戏、09 BOSS 战、10 两界与场景结构、11 剧情流程与章节结构、12 调查与线索、13 系统界面清单；状态均为待策划确认，原文矛盾与待定见 00 总览。
旧版 `docs/design/features/` 01–15 与照镜 demo（`PRP/mirror-core`）已冻结，保留备查。精炼与开 PRP 的顺序见 `docs/roadmap.md` 第 5 节 W5。

---

## 4. 已知坑（别重踩）

- **生成物不手改**：`Library/` `Temp/` `Logs/` `obj/` `UserSettings/`、`*.meta`、`*.csproj/*.sln`、`packages-lock.json` 都由 Unity 生成。
- **共享工作区**：本项目常有多个会话 / 多人共用同一个工作区与 git 索引。提交一律按路径（`git commit -F <信息文件> -- <路径…>`），不要用不带路径的 `git commit` 或 `-a`；同一文件里混有别人未提交的段落时用临时索引出提交。细节见 `ai-docs/pitfalls.md`。
- **`invariants.py` 最近四项常红**：字体资产一项、LailaFace 命名空间三项（§1.3）。这是两类问题，不能将全仓检查写成通过；须区分既有问题与新增问题。
- **气泡不能只测村民**：保留共用预制体连接，并走真实探索测试不同 NPC 与接近方向。只修缩放仍会漏掉正面站位的上沿裁切；视口约束不负责其它 HUD 的遮挡避让。
- **移动 / 删除 / 重命名资产**用 `git mv` / `git rm`，必须连 `.meta` 一起。
- **角色纸片不要给 `CameraBillboard` 加 `ExecuteAlways`**：会把场景标脏，二十多个物体旋转进 diff。
- **演出隐藏的覆盖边界**：`PerformanceService.PlayAsync` 在加载舞台前统一快照场景中的 `ChibiPuppet`，包含未激活角色，排除舞台演员，并在退出时恢复 Renderer / Canvas 原状态。演出期间新生成的角色不在快照内；无 `ChibiPuppetMotion.TrackedRoot` 时退回顶层根，角色放在公共容器下时应配置角色根，避免连带隐藏其他对象。表情轨与 `PerformanceActor` 已删除，不要再配置旧绑定。
- **Codex 长文必读记账**：兼容修复在 `.codex/hooks/`，没有修改 Claude hook。长文从同一次完整读取结果分批交付，缺段、失败与跨调用混入仍不记账；诊断方式与示例见 [.codex/hooks/README.md](.codex/hooks/README.md)。压缩后账本重置是既有设计，需要重新读取，不是这次故障复发。
- 更多踩过的坑见 `ai-docs/pitfalls.md`，动手前值得扫一遍。

---

## 5. 工具速查

| 要做的事 | 怎么做 |
| --- | --- |
| 看编译错误 | Unity MCP 的 `read_console`，或编辑器 Console。**控制台不可靠**（会被别的会话清空、也会读到编译中间态）——真源是 `%LOCALAPPDATA%\Unity\Editor\Editor.log` 里的 `error CS` 与 `## Script Compilation Error for: … <程序集>.dll` |
| 项目 lint（保存 `.cs` 时自动跑） | `python .claude/skills/project-lint/lint.py <file.cs>` |
| 健康度 / 跨文件不变量 | `python .claude/skills/evolution/gc_scan.py` |
| 跑测试 | `/unity-test [EditMode\|PlayMode] [过滤]`；**没有原生 MCP 工具时**用 `uv run --with mcp python scripts/unity_mcp_check.py --test --mode EditMode --group <组>` |
| **复核「测试真的跑了吗」** | `--test` 输出里看 `stale_assembly_suspect` / `orphaned_job` / `verdict` 三个字段；**再加字节检索 DLL**：`$t=[Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes('Library/ScriptAssemblies/Game.Tests.EditMode.dll')); $t.Contains('你的新测试类名')`。三者都过才采信 |
| **查新增文件缺不缺 `.meta`** | `uv run --with mcp python scripts/unity_mcp_check.py --meta`（扫 `git status` 里所有改动/新增的 `Assets/` 资产） |
| 强制 Unity 重编 | `refresh_unity(mode=force, scope=all, compile=request, wait_for_ready=true)`；**第一次常返回 `recovered_from_disconnect: true` 而没落地，重试一次**；仍不行就 touch 一个生产 `.cs` 再刷新 |
| 模块回放验证 | `/verify-module <模块>` |
| 本机出包（编辑器须关闭） | `scripts/build.ps1` |

---

*本文是未完成事项的活文档：每关掉一项就删掉对应条目，别让它积累成第二份现状描述。*
*提交状态以 `git log` 为准；实现还需对照源码，验收以对应运行证据为准，进行中不算完成。历史记录不删除、不改真实执行时间。*
