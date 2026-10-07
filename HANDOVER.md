# 21Days 交接文档 — 未完成事项

> **给谁看**：接手本项目的开发者。
> **前置阅读**：`CLAUDE.md`（硬规则与目录约定）→ `ai-docs/project-guide.md`（共用约定）→ `docs/architecture.md`（框架层与各服务契约）。
> **现状口径**：当前接线以源码与场景为准，剩余验收见本文；测试数字只对应所列日期与范围。
> **系统层**：加载黑幕相位守卫、回放等待揭幕与不进队列的存档角落提示已实现。2026-10-07 历史记录：EditMode 1115/1115、Session 回放 2/2、Exploration 8/8；视觉验收仍待确认。
> **S 组**：Identity / Stealth 已挂 Boot，遭遇接了身份禁攻、遮挡与处决；World 已挂 Boot 并有两张灰盒；TurnBased 仍为纯规则加白盒回放，正式战斗入口 / UI 未接。
> **S 组历史验证（2026-10-07）**：EditMode 1836/1836；identity / stealth / turnbased 回放 PASS，world 回放 FAIL（13 个失败检查点）。此记录不能替代之后的回归。
> **依据**：[S 组规划](docs/planning/S组落地总规划.md)、[状态字典](ai-docs/docs/story-facts.md)、[World PRP](PRP/world-scenes/prp.md)、[处决 PRP](PRP/stealth-execution/prp.md)；报告为 `Logs/verify/` 生成物。

## 0. 现状一句话

主流程已有通过记录：标题页 → 进入探索场景 → 潜行 / 战斗 / 对话 / 演出 / 任务 / 存档。Narrative 依赖的 DialogueService 遗漏片段已补交；补交后的定向回归与人工回放验收已有记录，但尚未在独立干净检出上完成最终提交验证。本轮验证不代表全仓所有测试已重新跑过。

**本轮验证范围**：Performance EditMode 150 项、演出 Showcase 6 项通过，失焦键盘自测与输入设置恢复检查通过；演出回放报告为 `Logs/verify/performance/20260929-042650/report.md`，失焦自测报告为 `Logs/verify/selftest/20260929-025916/report.md`（报告为生成物，不入库）。角色隐藏已覆盖直接播放、对白插入及场景触发；演出期间世界 Tick / 玩家位置不变、Timeline 继续推进，结束后世界恢复。时停已验证，无需进一步修改。用户已完成一次视觉验收，暂未发现问题。

**后续验证与当前交付边界**：

- Core/Dialogue/Mirror/Narrative/Quest/Session 联合 EditMode 647/647、Dialogue/Mirror/Narrative PlayMode 16/16，均零失败零跳过；这是该轮联合回归结果。Narrative 最新记录另有补交后 EditMode 22/22、三条回放通过及用户人工确认，详见 §1.2 和 `PRP/narrative-dialogue/tasks.md`。
- 气泡补修已提交并完成 review-change：井边妇人旧副本接回共用预制体，组件增加视口约束与临时缩小/恢复。Dialogue EditMode 125/125，零失败零跳过；两条气泡回放任务成功，报告 `Logs/verify/dialogue/20260929-075616/report.md` 为 PASS，覆盖村民侧面、妇人侧面与正面。最终 MCP result=null，不补推 passed/skipped 细分；该报告检查点失败 0、运行时异常 0。
- 气泡姓名、正文与边框的截图核对已有记录；用户视觉确认仍待完成。
- 提交拆分依据见 `PRP/commit-batches-2026-09-29.md`。它是历史清单：C2 气泡及顶部四文件补交均已入库，不再列为待办；其它批次以仓库记录核对。

**框架可运行与正式内容完成是两回事。** 当前已有 `Game.Core`、运行时模块、主流程、编辑器工具、回放与埋点体系，外加照镜 demo（`PRP/mirror-core`）；剩余验收和内容工作见下文。

**没做的分两层，量级差很远：**

- **§1 / §2 是已有模块的收尾与待拍板事项**，具体范围见下面的清单。
- **§3 是内容层，仍有大量待定**：2026-10-06 起策划以《聚光灯》为准（`docs/design/spotlight/`，玩法拆分见 `docs/design/features-spotlight/`），照镜 demo（`PRP/mirror-core`）随旧版策划冻结。`docs/roadmap.md` 的 C1–C3/B3/W2 状态已同步，旧“20 条完全没开始”不再作为当前统计。这一片仍需策划与美术输入。
- **S 组的剩余工作**：区分接线、白盒验收与正式内容。Identity / Stealth / World 的 Installer 和相关 Showcase 已存在，不重新派发“从零接线 / 新建回放”。World 回放尚有失败，新开局 / 读档进入世界场景仍有缺口；TurnBased 正式入口与 UI 未接。处决还受怪物表的 `defeat_method` 内容限制，不能把绕背回放当实际 F 键处决验收。
- **正式内容**：真实章节、怪物数值和场景 / UI 美术仍需外部输入；S6 / S8 / B4 的待定事项按策划总览处理。两份补交的怪物交互与回合制原件已入库，歧义见 `docs/design/features-spotlight/待策划拍板问题.md`。

---

## 1. 可以直接开工

### 1.1 最终提交的独立验证

- 原“遗漏提交”已关闭：DialogueService 的稳定 targetId 重载、Completed 传递及三份契约文档均已入库，不再要求补交。
- 剩余工作是在隔离的干净检出上记录最终提交号，完成编译和相关联合回归；不通过清理或回滚共享工作区来制造干净状态。
- 补交后 22/22 与三条 Narrative 回放、此前 647/647 与 16/16 均不能代替最终提交的独立检出验证。尚无该项验证已执行的证据。
- 完成后回填 `PRP/narrative-dialogue/tasks.md` 的最终提交独立验证项，并关闭本节；不重复实现已有接线。

### 1.2 Narrative 接线的剩余验收

C1–C3/B3 的运行接线、真实条件源、最小表/校验器、任务完成映射剧情标记与 Session 稳定状态保存已实现并有回归证据，不要再创建第二套控制器或存档系统。

- 人工视觉确认已通过：最新实施记录载明用户观看慢速回放后反馈“好像是没问题的”。对应 `Logs/verify/narrative/20260929-094939/report.md`（实时刷新）与 `Logs/verify/narrative/20260929-095052/report.md`（取消/重试/跳过、保存/继续），记录均为检查点失败 0、运行时异常 0；验收仅限这三条覆盖行为。
- 回放关键画面的 `Wait(3f)` 阅读停顿已入库，x2 时约停 6 秒。上述运行证据来自 Narrative 实施记录，仅覆盖该轮验证范围。
- 未覆盖：C5/G5 对抗结果、全目标生命周期中断、全场景恢复事务、对白逐节点/逐字恢复及真实章节内容；详见 PRP 的“未纳入本批的验收欠账”。
- 调试探针曾在未传生命周期令牌、对白仍运行时直接停止 Play，出现 ObjectDisposedException；正式回放正常收尾为零异常，不据此宣称任意根作用域突然销毁均已覆盖。
- `DefaultDialogueConditionSource` 保留作未装 NarrativeInstaller 的兼容来源；不要按旧交接要求直接删除。

### 1.3 仍待处理的小修与偶发问题

| 位置 | 问题 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs:4`、`FaceDragHandle.cs:4`、`MuralFaceController.cs:3` | `Game.LailaFace` 与 `Runtime/Gameplay/` 不符，静态检查持续报告三处；注册表仍按实际目录定位，不把这条记录作为自动迁移指令 |

`ProjectStructureMenu.cs` 的 TODO 位于 `BuildRulesTemplate` 生成字符串中，是新模块骨架提示，不是现有服务缺陷。

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
| E8 分辨率基准与画面适配 | `SetResolution` 与窗口拖拽**未出包实测**（须构建 Player 后验证，编辑器里测不出来） |
| H7 无鼠标悬停反馈 | NPC 悬停高亮未做 |

D4 / D5 的旧「未采用」待办已过期：`DialogueView.prefab` 已采用 SlideUp（`transition: 1`），多个 UI 预制体已挂 UIButtonFeedback。已有接线不等于新一轮视觉验收；Inventory 的回放与视觉验收仍需独立核对。

### 1.6 Codex MCP 与 hooks 工作流

- 手动诊断工具 `scripts/unity_mcp_probe.py` 与说明已入库。协议客户端可用不等于各聊天原生 MCP 均可用；接手时需实际读取目标编辑器确认连接。
- “所有项目聊天自动注册 MCP、持续保活”尚未完成；不能把已注册项目 hooks 当成该能力已实现。连接成功以实际读取目标编辑器为准，测试/域重载期间出现短暂断连时先等待再重连，不反复重启健康 bridge。
- `.codex/hooks/README.md`、`adapter.py`、`test_adapter.py` 的长文记账兼容修复已入库。项目 hooks 是否生效以各会话实测为准；操作限制与处理方式见该 README，不关闭护栏绕过。

### 1.7 Laila 捏脸与表情识别：已接独立研究试玩，继续补组合测试

- **已实现**：31 个形态、17 个交互轴的采样与五类 Sentis 推理；自动更新、错误清理、稳定反馈与展示迟滞；标注工具、匿名采集、反馈保存和有界候选训练。唯一场景 `LailaRecognitionPlaytest.unity` 默认定向修复 59D，保留上一版反馈、r779 与原 51D 对照；重复旧场景及一次性搭建代码已移除。17 轴是模型输入，51D / 59D 是图内特征空间，输出仍是中性、高兴、悲伤、惊恐、愤怒五类。
- **入口与维护**：Unity 菜单 `21Days/Laila/候选试玩（含旧版对照）`；接手先读 [spec §17](PRP/laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)，再读[识别模块指南](ai-docs/docs/modules/lailafacerecognition/lailafacerecognition-module-guide.md)与[试玩 / 标注说明](ML/expression-recognition/analysis/laila_v2_candidate/ANNOTATOR.md)。旧 12 轴 / v1 阶段门留在规格中作历史复现，不再照旧清单重新派工。
- **已有验证记录**：候选的 205 条 Sentis / ONNX 数值对照、21 档实际 UI、四版切换 / 清理和抓点 hover 回归见模块指南；三个新增怒点属于训练拟合，同原型预设属于暴露开发回归。2026-10-07 标注与反馈修复相关的四份 Python 测试文件共 **36/36 通过**；该结果不包含 Unity 或人工语义验收。
- **剩余工作**：冻结组合姿态反例，复核脸型表达、左右响应和跨帧稳定性；独立人工语义验收、物理鼠标与 Player 仍未完成，研究拒识未校准。先补测试缺口，再讨论场景整合；不自动追加训练、扩类或接关卡判定。公开集来源的研究模型不按正式发布模型交付。

### 1.8 Rhythm 四轨音游：曲库、校准、同局暂停已实现

- **已实现**：独立 `RhythmDemo.unity` 的等宽 DFJK 四轨、Tap / Hold、三首音乐与单谱曲库、完整单局结果、v2 个人纪录和解锁；设置草稿明确保存 / 取消，演奏中暂停 / 继续 / 重试 / 退出。Practice、校准、Combat 与自由局纪录分开；不做多难度，不改两首新曲听感。
- **校准与外部能力**：固定主轮、分级可信度、原值 / 候选试听与明确采用；低可信 / Drift 只供比较与用户选择，不自动替换。保存先落盘再改内存，失败保留原值与候选。固定谱 Combat 复核 song / chart / revision / ruleset / scoring 全身份并去重，不写自由局纪录；真实乐师 adapter、伤害与失败惩罚未接。
- **校准返回修复**：校准取消、保留和采用成功返回曲库，关闭旧设置 / 暂停 / 候选覆盖层，准备与结算按钮布局统一。定向 Showcase **1/1 通过**、两个尺寸的渲染与按钮命中记录，以及测试包已构建但完整 Player 往返未验收的边界，见[模块指南末段](ai-docs/docs/modules/rhythm/rhythm-module-guide.md#2026-10-07-校准返回页面修复)。验证仅覆盖该记录中的版本与行为。
- **接手与剩余验收**：[四轨后续规格](PRP/rhythm-followup/spec.md)、[曲库实施](PRP/rhythm-library-integration-20261005/implementation-report.md)、[分级校准 / Combat](PRP/rhythm-final-regression-fix-20261005/confidence-combat-implementation-report.md)、[页面 / 暂停实施](PRP/rhythm-ui-navigation-20261006/implementation-report.md)。Boot 与正式 UI prefab 迁移后置；真人校准有效性、跨设备 / 物理延迟、音乐贴合度和 Player 实测仍欠。现有四轨实现不能直接算作《聚光灯》S8 已接入。

### 1.9 Taming 多目标控制：已有正式接线，保留与 S 组的兼容

多目标驯服已接 Boot → SampleScene：两名巡逻者使用独立路线和稳定 ID，T 驯服 / 返回玩家、UI 定向选择、镜头跟随、死亡 / 不可用时回退；存档 v2 与回放 v5 保存各目标及控制归属。入口与边界见[Taming 模块指南](ai-docs/docs/modules/taming/taming-module-guide.md)。这不代表聚光灯附身条件与身份继承规则已完成。

控制路由已接身份禁攻、怪物种类配置与潜行 / 战斗结算；输入位分配为驯服 7–9、交互 / 背包 10–11，每个巡逻者每 tick 只推进一次。多目标身份禁攻、单 tick 推进与输入位不重叠的回归检查已通过，场景回放验收仍待完成。

**集成验证边界（2026-10-07）**：相关 C# 文件 lint 通过，Unity 脚本重编成功；Taming / Monster / Simulation 定向 EditMode **157/157 通过**，新回归测试已核对进入程序集，含 v1 遭遇旧档兼容。该结果不包含全仓测试或 Showcase 场景回放验收。

### 1.10 中文字体补字

**已有验证（2026-10-07）**：使用 TMP API 在已有 318 个字符上追加 328 个字符，共 646 个。编辑器与保存后的资产均已核对：原字形数据、图集位置和 `.meta` 保持不变；尚未进行界面显示的人工验收。动态图集体积仍属于健康检查已知项，不清空已有字形。

---

## 2. 等拍板，不要自己决定

| 事项 | 卡在谁 | 说明 |
| --- | --- | --- |
| 两个杂散场景副本 | 项目负责人 | `Assets/Scenes/SampleScene 1.unity`、`SampleScene1 1.unity` 已被上一次提交大改并入库；`Assets/Scenes/` 是模板目录、规则要求原位不动，删还是留未定 |
| 任务「!」标记是否给台词气泡让位 | 项目负责人 | 灰色「…」会避让，任务黄「!」不让位，无树 NPC 冒气泡时可能被压住 |
| 任务面板底板透底 | 需实机复现 | 回放里正常、实机透底；怀疑与 `ProjectSettings/EditorSettings.asset` 的 Enter Play Mode Options 被测试运行器打开有关，未定位 |
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
| A 探索层 | A3 物资箱 / 最小背包已有接线，泛化交互仍需核对；A4 / A6 机制与灰盒见顶部 S 组记录，正式内容及最终验收另计 |
| B 任务系统 | B3 已有接线与验证，最终提交独立验证见 §1.1；B4 进度重置与已完成列表 |
| C 叙事接线 | C1–C3 转 §1.1/§1.2 收尾；C5 战斗结果 → 剧情仍需核对 S2 / S3 / S7 契约 |
| D 演出与 UI 动效 | D6 角色动画补齐，D4/D5 资产采用状态见 §1.5 |
| E 系统与流程 | E4 加载过渡已收尾待视觉验收（2026-10-07，相位守卫与回放等待），E8 出包验证 |
| F 内容与美术 | F1 真实剧本进表、F2 环境替换、F3 美术皮肤；F4 已有三首音游音乐，环境音 / SFX / 正式内容音频仍缺；F5 已有 Narrative 最小校验，完整内容校验工具未验收 |
| S 聚光灯核心机制 | S1 换皮与附身、S2 身份暴露与怀疑、S3 潜行与暗杀、S4 追逐、S5 皮面具与道具、S6 关卡专属机制、S7 BOSS 战、S8 小游戏；旧 G 组随旧版冻结 |
| H PC 适配 | 主要路径已实现，NPC 悬停与窗口实测仍需逐项确认，不能写全部验收完成 |

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
- **工具与 hooks 排查**：见 [.codex/hooks/README.md](.codex/hooks/README.md) 与 [坑册](ai-docs/pitfalls.md)，本文不重复会话诊断步骤。
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
| 构建 Player（编辑器须关闭） | `scripts/build.ps1` |

---

*本文是未完成事项的活文档：每关掉一项就删掉对应条目，别让它积累成第二份现状描述。*
*当前状态一律以 `git log` 为准，本文不记录提交号。*
