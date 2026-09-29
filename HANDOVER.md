# 21Days 交接文档 — 未完成事项

> **给谁看**：接手本项目的开发者。
> **前置阅读**：`CLAUDE.md`（硬规则与目录约定）→ `ai-docs/project-guide.md`（共用约定）→ `docs/architecture.md`（框架层与各服务契约）。
> **最近核对**：2026-09-29，当前分支 `main`。Mirror 修复、Core UI 取消回滚、Narrative 接线主体、Replay 五分钟实录、气泡两轮修复、Showcase 文档与手动 MCP 探针均已有提交；提交记录不等于所有相关片段已入库，尤其注意 §1.1。未涉及条目需接手时重新定位，推送状态未核对。
> **工作区边界**：仍有 DialogueService 及 Dialogue/Quest 契约文档、Codex hooks 三文件、`laila` 场景、URP、字体、Laila 美术资产及模块文档、构建设置改动，以及未跟踪的提交清单。气泡源码、测试、SampleScene 和气泡两份指南目前已无 diff。不要整仓暂存、回滚或清理，以实时 `git status` 为准。

## 0. 现状一句话

主流程在完整工作区已跑通：标题页 → 进入探索场景 → 潜行 / 战斗 / 对话 / 演出 / 任务 / 存档。主要实现已进入 main，但 Narrative 依赖的 DialogueService 片段仍未提交，不能把工作区通过写成当前 HEAD 独立通过。本轮验证是定向回归，不代表全仓所有测试已重新跑过。

**本轮验证范围**：Performance EditMode 150 项、演出 Showcase 6 项通过，失焦键盘自测与输入设置恢复检查通过；演出回放报告为 `Logs/verify/performance/20260929-042650/report.md`，失焦自测报告为 `Logs/verify/selftest/20260929-025916/report.md`（本地生成物，不入库）。角色隐藏已覆盖直接播放、对白插入及场景触发；演出期间世界 Tick / 玩家位置不变、Timeline 继续推进，结束后世界恢复。时停已验证，无需进一步修改。用户已完成一次视觉验收，暂未发现问题。

**后续验证与当前交付边界**：

- Core/Dialogue/Mirror/Narrative/Quest/Session 联合 EditMode 647/647、Dialogue/Mirror/Narrative PlayMode 16/16，均零失败零跳过；这是拆分提交前的完整工作区结果。Narrative 人工视觉确认仍待用户，证据见 `PRP/narrative-dialogue/tasks.md`。
- 气泡补修已提交并完成 review-change：井边妇人旧副本接回共用预制体，组件增加视口约束与临时缩小/恢复。Dialogue EditMode 125/125，零失败零跳过；两条气泡回放任务成功，报告 `Logs/verify/dialogue/20260929-075616/report.md` 为 PASS，覆盖村民侧面、妇人侧面与正面。最终 MCP result=null，不补推 passed/skipped 细分；该报告检查点失败 0、运行时异常 0。
- 气泡正面图为同目录 `03-井边妇人·正面接近气泡.png`，已核对姓名、正文和边框完整；用户视觉确认尚未收到。最后一次读取 Unity 为 Boot、无未保存场景、控制台 error=0。
- 提交拆分依据见 `PRP/commit-batches-2026-09-29.md`。它是历史清单：C2 气泡现已提交，不能照旧“待提交”状态重复暂存；其它批次也须与 Git 实时对账。

**框架可运行与正式内容完成是两回事。** 当前已有 `Game.Core`、运行时模块、主流程、编辑器工具、回放与埋点体系，外加照镜 demo（`PRP/mirror-core`）；剩余接线提交、验收和内容工作见下文。

**没做的分两层，量级差很远：**

- **§1 / §2 是已有模块的收尾与待拍板事项**，具体范围见下面的清单。
- **§3 是内容层，仍有大量待定**：镜子相关已有 demo；画皮、收押、无血条对抗、三结局与真实章节内容尚未验收。`docs/roadmap.md` 的 Narrative/B3 状态尚未同步，旧“20 条完全没开始”不能再作为当前统计。这一片仍需策划与美术输入。

一句话：**框架能跑，玩法的肉还在纸上。**

---

## 1. 可以直接开工

### 1.1 优先补齐已提交模块的遗漏片段

- `Assets/_Project/Scripts/Runtime/Dialogue/DialogueService.cs` 仍有未提交改动：稳定 targetId 的四参数 `PlayAsync` 重载、向 Controller 传 targetId、资源收尾成功后设置 Completed、结束事件传 completed。
- 已入库的 `NarrativeService` 调用上述四参数重载，当前 HEAD 的 DialogueService 尚无该重载；静态核对确认提交不完整。先按内容审查并补齐，不能回滚这个文件来“清理工作区”。尚未在独立干净检出上运行编译。
- `dialogue-external-api.md`、`quest-module-guide.md`、`quest-external-api.md` 还有相关未提交契约说明，和源码一起对账。原分批清单 E/F 可作归属参考，但不要重复提交已入库文件。
- 补齐后验证最终提交状态的编译与相关回归，再同步 `PRP/narrative-dialogue/tasks.md` 和 `docs/roadmap.md`；tasks 内的未勾选项及早期“未实现”记录不能代替后续证据。

### 1.2 Narrative 接线的剩余验收

C1–C3/B3 的运行接线、真实条件源、最小表/校验器、任务完成映射剧情标记与 Session 稳定状态保存已实现并有回归证据，不要再创建第二套控制器或存档系统。

- 人工视觉确认：`Logs/verify/narrative/20260929-063358/report.md`；选项清晰图 `10-选项去调试遮挡.png`。自动通过不代表用户已确认。
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

---

## 2. 等拍板，不要自己决定

| 事项 | 卡在谁 | 说明 |
| --- | --- | --- |
| 字体 SDF 资产约 22 MB | 项目负责人 | `Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset` 最近扫描为 22,701 KB，动态字形数据仍触发不变量检查；资产另有未提交改动，处理前与美术工作对齐，不能为让检查变绿直接清理 |
| 两个杂散场景副本 | 项目负责人 | `Assets/Scenes/SampleScene 1.unity`、`SampleScene1 1.unity` 已被上一次提交大改并入库；`Assets/Scenes/` 是模板目录、规则要求原位不动，删还是留未定 |
| 任务「!」标记是否给台词气泡让位 | 项目负责人 | 灰色「…」会避让，任务黄「!」不让位，无树 NPC 冒气泡时可能被压住 |
| 任务面板底板透底 | 需实机复现 | 回放里正常、实机透底；怀疑与 `ProjectSettings/EditorSettings.asset` 的 Enter Play Mode Options 被测试运行器打开有关，未定位 |
| Mirror `visionLossPerCrack` | 项目负责人 | 现值 0.18，建议 0.3——1～2 道裂时暗角几乎看不见 |
| 对白与演出的补全手势不一致 | 项目负责人 | 对白打字中要「连点三下」补全，演出「单击」补全，统一还是保持 |
| 记录面板「关闭」压住「跳过」 | 项目负责人 | 两按钮位置重叠，待视觉打磨 |
| `docs/design/features/` 的矛盾与待定 | 策划 | 30 条跨文档矛盾 + 128 条待定问题；拍板后要把 `[待定]` 改成 `[原文]` 并回写产品文档，别让 features 变成第二真源 |
| 四本素材 PDF（共 152 MB） | 项目负责人 | 移出仓库放共享盘，还是走 Git LFS |
| Run 动画帧、探索 3D 环境资产 | 美术 | 序列帧小人暂无 Run 动画（现复用 walk 剪辑）；探索环境仍是灰盒 |

---

## 3. 长线与内容层（等外部输入）

**这一节是长期工作**：3.1 已废弃，3.2–3.3 是两条长线，3.4 记录尚待推进的内容及系统项，不把已有接线重新列为未实现。

**3.1 Live2D** —— 已废弃（2026-09-28 用户定）：演出只保留世界舞台 + 序列帧小人，Live2D 适配层与叠加模式代码已随 `161c690` 删除，不再导入 Cubism SDK。

**3.2 移动端移植** —— 现阶段是 PC 游戏：触屏摇杆、触屏三键、走跑按钮、Android 画质档、安全区实机统一，全部后置到移植阶段。注意玩法代码里**禁止**平台条件编译与平台专属 API，这类只允许出现在 `Scripts/Runtime/Platform/`，输入只读 Input System 的 Action Map。

**3.3 美术替换** —— 探索场景从灰盒换成 3D 环境资产（模块化 Prefab + Lightmap）。纸片角色按**正面平视**画，不要按要求俯视 3/4 出图。

**3.4 内容与系统剩余方向**（roadmap 的 C1–C3/B3/W2 状态及统计待同步，不沿用旧总数）

| 组 | 待推进或核对 |
| --- | --- |
| A 探索层 | A3 泛化交互/物资箱须与 Loot 聊天对账；A4 多场景流转、A6 相机边界与死区 |
| B 任务系统 | B3 已有接线与验证，先补齐 §1.1；B4 进度重置与已完成列表 |
| C 叙事接线 | C1–C3 转 §1.1/§1.2 收尾；C5 战斗结果 → 剧情等待 G5 规则 |
| D 演出与 UI 动效 | D6 角色动画补齐，D4/D5 资产采用状态见 §1.5 |
| E 系统与流程 | E4 加载过渡，E8 出包验证 |
| F 内容与美术 | F1 真实剧本进表、F2 环境替换、F3 美术皮肤、F4 音频；F5 已有 Narrative 最小校验，完整内容校验工具未验收 |
| G 自家机制 | G2 画皮、G3 收押、G5 追逐/躲藏/弱点识破、G6 三结局 |
| H PC 适配 | 主要路径已实现，NPC 悬停、窗口实测及其它聊天持有项仍需逐项确认，不能写全部验收完成 |

**玩法内容清单 `docs/design/features/`：既有 demo 与待策划内容**

| 有 demo（`PRP/mirror-core`） | 尚无完整内容验收（12 份） |
| --- | --- |
| 01 通灵视、02 照镜辨形、03 镜之耐久与镜碎 | 04 收押、05 归还与扣押、06 画皮傩演、07 两界之账、08 追逐躲藏与弱点识破、09 镜中窥探、10 疑案调查与信息、11 分支与结局、12 断其去处、13 妖的化形与破绽、14 流程与章节结构、15 叙事呈现 |

这 12 份沿用待策划确认状态，不因框架或验证样例落地就视为内容已定；14/15 涉及的通用流程和呈现已有系统支撑，不能说一行代码都没有。
具体矛盾与待定项按 `docs/design/features/00_功能总览.md` 重新核对，本文不沿用未经本轮重数的数量。拍板后把 `[待定]` 改成 `[原文]` 并回写产品文档，再逐个走 `/refine-prd`。
建议的精炼顺序：G1(02+01) → G4(03) → G5(08) → G3(04+05+07) → G2(06) → G6(11+12)。

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
| 看编译错误 | Unity MCP 的 `read_console`，或编辑器 Console |
| 项目 lint（保存 `.cs` 时自动跑） | `python .claude/skills/project-lint/lint.py <file.cs>` |
| 健康度 / 跨文件不变量 | `python .claude/skills/evolution/gc_scan.py` |
| 跑测试 | `/unity-test [EditMode\|PlayMode] [过滤]` |
| 模块回放验证 | `/verify-module <模块>` |
| 本机出包（编辑器须关闭） | `scripts/build.ps1` |

---

*本文是未完成事项的活文档：每关掉一项就删掉对应条目，别让它积累成第二份现状描述。*
*当前状态一律以 `git log` 为准，本文不记录提交号。*
