# 21Days 交接文档 — 未完成事项

> **给谁看**：接手本项目的开发者。
> **前置阅读**：`CLAUDE.md`（硬规则与目录约定）→ `ai-docs/project-guide.md`（共用约定）→ `docs/architecture.md`（框架层与各服务契约）。
> **接手备注（2026-09-30）**：上一会话把「连点补全下沉 Core、演出字幕接连点、可对话 NPC 挡人、laila 上界分析、`run_tests` 回放范围守卫」写进工作区后额度耗尽中断，没来得及提交。接管会话复核后按主题分 8 条提交并推送：编译零错误，EditMode 全量 **1088 条通过**，`python .claude/hooks/tests/run.py` **69 条全过**。它中断时留了两个尾巴，都已处理——编辑器里一个 32 分钟未收的孤儿测试标志（`TestRunStatus.IsRunning`，把 `refresh_unity` 一直挡成 `tests_running`；已补坑册与 MCP 排查表）与「记录面板『关闭』压住『跳过』」（关闭上移 30px，并补了布局守卫测试）。
> **接手备注（2026-10-07，系统层 / roadmap E 组）**：接着上一会话未提交的 E4 改动往下做，工作区里这批尚未提交（等授权）。做完的事——E4 加载黑幕：`LoadingCurtain` 补相位守卫（首次开面板期间被揭幕不再淡入、视图被销毁后仍能重盖、揭幕出错必须回 `IsCovered=false`）、暂停菜单的「黑幕在盖」并进可测的 `ShouldOpen`、回放加 `WaitCurtainRevealed`（场景重载后不再截到半透明黑幕），新增 `LoadingCurtainTests` 5 条；E1 尾巴：「已保存」从通知队列改走不进队列的右下角小字（`INotificationService.ShowCornerHint` + `UIConfig.CornerHintSeconds` + `NotificationView` 的 `Corner/CornerLabel` 子节点，深底小牌避开编辑器右下角相机调试工具条），新增 `NotificationServiceTests` 7 条，Session 回放补检查点与截图「保存后·右下角小字」。
> 证据（全部本次实跑）：编译零错误、控制台 error / warning 0；EditMode 全量 **1115 / 1115**；Session 回放 **2 / 2** PASS（`Logs/verify/session/20261007-022334/report.md`）；Exploration 回放 **8 / 8** PASS（`Logs/verify/exploration/20261007-022419/report.md`）；`gc_scan` 只剩既有 4 条（LailaFace 命名空间 3 条 + 字体资产动态字形，见 §1.3 与 §2）。`docs/roadmap.md` 的 §0 第 6 条、E1 / E4 / W3 已同步。
> **最近核对**：2026-10-07，当前分支 `main`。E8 的出包实测（`SetResolution` / 窗口拖拽）仍未做，要关编辑器才测得出。
> **工作区边界**：这批提交后工作区干净。唯一的常态脏数据是两个 TMP 字体资产（`Art/Fonts/…SDF.asset`、`TextMesh Pro/…/LiberationSans SDF - Fallback.asset`）：每跑一次 Play、或一次带真实 View 的 EditMode 测试，就被烘进几 MB 字形，提交前清回基线（6,404 B / 9,633 B）再提交，别带进提交——见 `ai-docs/pitfalls.md`「TMP Dynamic 字体资产」。不要整仓暂存、回滚或清理，以实时 `git status` 为准。

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
- 未覆盖：C5/G5 对抗结果、全目标生命周期中断、全场景恢复事务、对白逐节点/逐字恢复及真实章节内容；详见 PRP 的“未纳入本批的验收欠账”。
- 调试探针曾在未传生命周期令牌、对白仍运行时直接停止 Play，出现 ObjectDisposedException；正式回放正常收尾为零异常，不据此宣称任意根作用域突然销毁均已覆盖。
- `DefaultDialogueConditionSource` 保留作未装 NarrativeInstaller 的兼容来源；不要按旧交接要求直接删除。

### 1.3 仍待处理的小修与偶发问题

| 位置 | 问题 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs:4`、`FaceDragHandle.cs:4`、`MuralFaceController.cs:3` | 命名空间是 `Game.LailaFace`，与所在目录 `Runtime/Gameplay/` 不符；`invariants.py` 每次都报红，常红会掩盖新引入的问题。挪目录改命名空间，或反过来（`.claude/skills/generate-doc/modules.json` 里 `lailaface.src` 也指向这里，要一起改） |
| `Assets/_Project/Scripts/Editor/Tools/ProjectStructureMenu.cs:153` | 全仓唯一一条真 `// TODO`：依赖应走构造注入，别在这里 new 服务或读静态单例 |

Mirror 句柄释放/相机朝向、CharacterPuppet 重复辅助方法、对白气泡查找和失焦输入经验文档已处理并提交，不再列待办。
Performance EditMode 曾出现记录面板关闭等待 5 秒超时；单独复跑与随后 150 项全量复跑均通过，原因未定位。再次出现时保留状态与日志，不只增大超时。

### 1.4 Replay：实录已补，性能与窗口验收未完

五分钟六实体 DemoWorld 实录已提交：300.096 秒、18000 tick、691931 B；RecordTick 调用内分配 0 B，最大 0.221900 ms，有一次超过 0.2 ms。
不能把局部采样写成整帧性能验收通过。T18/G3 仍需真实玩法开关录制的逐帧 Profiler 对照、稳态 GC，以及 ReplayWindow 的真实交互验收。
报告 `Logs/verify/replay/20260929-064018/report.md`，细节见 `PRP/replay/tasks.md`。

### 1.5 roadmap 里「已做但留尾巴」的 5 条

| 条目 | 尾巴 |
| --- | --- |
| A2 沉浸模式 | 玩家 / 巡逻者 NameTag 未随沉浸隐藏 |
| D4 面板过渡花样 | 滑入 / 缩放预设已实现，但现有预制体尚未选用非 Fade 预设（roadmap 派单一栏还写着「待看视频」） |
| D5 按钮反馈 | 尚未挂到任何预制体 |
| E8 分辨率基准与画面适配 | `SetResolution` 与窗口拖拽**未出包实测**（要本机打一次包，编辑器里测不出来） |
| H7 无鼠标悬停反馈 | NPC 悬停高亮未做 |

这 5 条的状态以 `docs/roadmap.md` 为准；改完记得同步那张表的状态列。

### 1.6 Codex MCP 与 hooks 工作流

- 手动诊断 `scripts/unity_mcp_probe.py` 与说明已提交；本会话原生 Unity MCP 初始化重试仍失败，实际测试通过临时协议客户端连接现有编辑器完成。临时客户端可用不等于各聊天原生 MCP 已恢复。
- “所有项目聊天自动注册 MCP、持续保活”尚未完成；不能把已注册项目 hooks 当成该能力已实现。连接成功以本会话实际读取目标编辑器为准，测试/域重载期间出现短暂断连时先等待再重连，不反复重启健康 bridge。
- `.codex/hooks/README.md`、`adapter.py`、`test_adapter.py` 当前属于其它聊天的未提交压缩输出修复；按该聊天证据单独审查，不混入气泡或 Narrative 提交。

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

## 2. 等拍板，不要自己决定

| 事项 | 卡在谁 | 说明 |
| --- | --- | --- |
| 字体 SDF 资产约 22 MB | 项目负责人 | `Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset` 最近扫描为 22,701 KB，动态字形数据仍触发不变量检查；资产另有未提交改动，处理前与美术工作对齐，不能为让检查变绿直接清理 |
| 两个杂散场景副本 | 项目负责人 | `Assets/Scenes/SampleScene 1.unity`、`SampleScene1 1.unity` 已被上一次提交大改并入库；`Assets/Scenes/` 是模板目录、规则要求原位不动，删还是留未定 |
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
| A 探索层 | A3 泛化交互/物资箱须与 Loot 聊天对账；A4 多场景流转、A6 相机边界与死区 |
| B 任务系统 | B3 已有接线与验证，最终提交独立验证见 §1.1；B4 进度重置与已完成列表 |
| C 叙事接线 | C1–C3 转 §1.1/§1.2 收尾；C5 战斗结果 → 剧情等待 G5 规则 |
| D 演出与 UI 动效 | D6 角色动画补齐，D4/D5 资产采用状态见 §1.5 |
| E 系统与流程 | E4 加载过渡已收尾待视觉验收（2026-10-07，相位守卫与回放等待），E8 出包验证 |
| F 内容与美术 | F1 真实剧本进表、F2 环境替换、F3 美术皮肤、F4 音频；F5 已有 Narrative 最小校验，完整内容校验工具未验收 |
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
*当前状态一律以 `git log` 为准，本文不记录提交号。*
