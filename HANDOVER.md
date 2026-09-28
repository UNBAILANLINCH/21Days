# 21Days 交接文档 — 未完成事项

> **给谁看**：接手本项目的开发者。
> **前置阅读**：`CLAUDE.md`（硬规则与目录约定）→ `ai-docs/project-guide.md`（共用约定）→ `docs/architecture.md`（框架层与各服务契约）。
> **基线**：`main` @ `4da29a3`（§1.1 的 A、B 随它推送）；§1.1-C 的改动在工作区待审。本文行号按「该基线 + C 的工作区改动」核对过；改动后请顺手更新本文。

## 0. 现状一句话

主流程已跑通并**全部合入 main**：标题页 → 进入探索场景 → 潜行 / 战斗 / 对话 / 演出 / 任务 / 存档，模块三件套与回放（Showcase）齐备，EditMode 与回放测试均为绿。

**没有「写一半的代码」——但要说清楚做完的到底是什么。** 做完的是**框架与系统**：`Game.Core` + 16 个运行时模块、主流程全链路、三套编辑器工具、回放与埋点体系，外加一个照镜 demo（`PRP/mirror-core`）。

**没做的分两层，量级差很远：**

- **§1 / §2 是已有模块的收尾**（7 件活 + 10 项待拍板）。数量少、都能立刻动手。
- **§3 是内容层，几乎没开始**：`docs/roadmap.md` A–H 共 51 条，其中 20 条标着「待做 / 待定义」；`docs/design/features/` 16 份策划稿只落地了 3 份（01 通灵视 / 02 照镜辨形 / 03 镜之耐久与镜碎），**其余 12 份一行代码都没有**。这一片才是大头，且卡在策划拍板。

一句话：**框架能跑，玩法的肉还在纸上。**

---

## 1. 可以直接开工

### 1.1 演出旧模式下架 + 对白插播传锚点 【已完成，C 待审】

A（下架示例 greeting）、B（对白插播传 NPC 锚点、插播时藏场景角色）已随 `4da29a3` 推送。
C（2026-09-28 用户定：叠加模式连同 Live2D 适配层整个删掉，演出只保留世界舞台 + 序列帧小人）改动在工作区，待审后按路径提交，提交后删掉本节：

- 删 `PerformanceStageMode` 枚举与舞台 `mode` 字段，世界舞台成为唯一行为；删相机栈叠加及其退路（埋点 `camera_stack_unavailable`）、摆放忽略告警（`placement_ignored`）。
- 黑边整条删：策略 / 配置 / 舞台开关 / 面板参数 / `PerformanceView` 字段与补间 / 预制体 `LetterboxTop`、`LetterboxBottom` 两个节点；进场黑场 `Fade` 保留。
- 模板工厂「新建演出」改建世界舞台壳（透视 Base 舞台相机 + 空 `Actors` 站位根 + 字幕 / 动作 / 音效三轨），舞台相机与村口示例 builder 共用 `CreateWorldStageCamera`；校验器删掉只对叠加有意义的检查，相机检查改为无条件执行。
- 删 `Runtime/Live2D/` 整个程序集、`Editor/Performance/Live2DDefineSync.cs`、演出编辑器「检查 Live2D 符号」；立绘占位演员 `SpritePerformanceActor` 一并删除（`PerformanceActor` 抽象与表情轨保留，但工程里暂无具体实现，模板不建表情轨）。
- 验收（2026-09-29）：编译零错误零警告；EditMode 全量 1030 条通过；`PerformanceShowcase`（4 条）+ `ScenePerformanceShowcase`（2 条）回放 PASS；
  `DialogueShowcase` 7 条中 6 条 PASS，`Bubble_ShowsAboveHead_WithoutPausing` 失败，原因与本单无关，见 §1.4 最后一行。

### 1.2 回放框架的输入失焦问题

**现象**：编辑器窗口失去焦点时，10 个键盘回放用例失败。
**做法**：在 Showcase 的公共 SetUp 里临时改 InputSettings 的 `backgroundBehavior` / `editorInputBehaviorInPlayMode`，TearDown 还原。
**现状**：`Assets/_Project/Scripts/Tests/Showcase/` 下搜不到这两个字段（已核）。

### 1.3 Narrative 接线（roadmap C1–C3）【最大一块】

`PRP/narrative-dialogue/` 的 tasks 只勾了 3/10，且**对白那半边已被 dialogue 模块取代**，只剩 Narrative 半边。

- 未完成：T3–T9 —— 存储测试、战斗恢复、Prefab 接线、NarrativeController、内容管线、Showcase、文档
- 落点：替换 `DefaultDialogueConditionSource`；遭遇触发调 `DialogueService.PlayAsync`
- **前置**：`PRP/narrative-dialogue/prp.md` 自述「设计草案…未实现」，`docs/roadmap.md` §6.2 要求**先修订 PRP 再执行**
- 关联：`DialogueSaveData` 尚未接存档（`ai-docs/docs/modules/dialogue/dialogue-module-guide.md:65`）

### 1.4 六处小修（半小时级）

| 位置 | 问题 |
| --- | --- |
| `Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs:139` | `ShowAsync(...).Forget()` 在罕见时序下漏释放图像句柄 |
| `Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs:114` | `Camera.main` 每帧查一次，未缓存 |
| `Assets/_Project/Scripts/Runtime/Gameplay/FaceBlendShapeController.cs:4`、`FaceDragHandle.cs:4`、`MuralFaceController.cs:3` | 命名空间是 `Game.LailaFace`，与所在目录 `Runtime/Gameplay/` 不符；`invariants.py` 每次都报红，常红会掩盖新引入的问题。挪目录改命名空间，或反过来（`.claude/skills/generate-doc/modules.json` 里 `lailaface.src` 也指向这里，要一起改） |
| `Assets/_Project/Scripts/Tests/Showcase/CharacterPuppet/CharacterPuppetShowcase.cs:273` | 残留私有 `RequireButton`（已迁到基类） |
| `Assets/_Project/Scripts/Editor/Tools/ProjectStructureMenu.cs:153` | 全仓唯一一条真 `// TODO`：依赖应走构造注入，别在这里 new 服务或读静态单例 |
| `Assets/_Project/Scripts/Tests/Showcase/Dialogue/DialogueShowcase.cs:313` | `FindRequired<DialogueSpeechBubble>("SpeechBubble")` 按名字全局找气泡；`eed1a5f`（照镜 demo）给 SampleScene 加了第二个同名物体 `Yao_WellWoman/SpeechBubble`，回放 `Bubble_ShowsAboveHead_WithoutPausing` 两个气泡检查点因此失败（2026-09-29 实测）。改成从 `Npc_Villager` 下取气泡 |

### 1.5 replay 缺两个实测数字

`PRP/replay/tasks.md:159`：G3 的 0.2 ms/帧预算、G1 的体积（现在是外推的 690 KB）都**没有实测**。跑一次真实录制回填即可，PRP 只差这一步。

### 1.6 roadmap 里「已做但留尾巴」的 5 条

| 条目 | 尾巴 |
| --- | --- |
| A2 沉浸模式 | 玩家 / 巡逻者 NameTag 未随沉浸隐藏 |
| D4 面板过渡花样 | 滑入 / 缩放预设已实现，但现有预制体尚未选用非 Fade 预设（roadmap 派单一栏还写着「待看视频」） |
| D5 按钮反馈 | 尚未挂到任何预制体 |
| E8 分辨率基准与画面适配 | `SetResolution` 与窗口拖拽**未出包实测**（要本机打一次包，编辑器里测不出来） |
| H7 无鼠标悬停反馈 | NPC 悬停高亮未做 |

这 5 条的状态以 `docs/roadmap.md` 为准；改完记得同步那张表的状态列。

---

## 2. 等拍板，不要自己决定

| 事项 | 卡在谁 | 说明 |
| --- | --- | --- |
| 字体 SDF 资产 23 MB | 项目负责人 | `Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset` 现为 22,687 KB，动态字形数据已入库。`invariants.py` 仍把它判为违规（先例 `d3feb94` 曾把同一资产从 8.5 MB 清到 6 KB），去留未定 |
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

**这一节是大头**：3.1 已废弃，3.2–3.3 是两条长线，3.4 是「一行代码都还没有」的全部内容。

**3.1 Live2D** —— 已废弃（2026-09-28 用户定）：演出只保留世界舞台 + 序列帧小人，Live2D 适配层与叠加模式代码已随 §1.1-C 删除，不再导入 Cubism SDK。

**3.2 移动端移植** —— 现阶段是 PC 游戏：触屏摇杆、触屏三键、走跑按钮、Android 画质档、安全区实机统一，全部后置到移植阶段。注意玩法代码里**禁止**平台条件编译与平台专属 API，这类只允许出现在 `Scripts/Runtime/Platform/`，输入只读 Input System 的 Action Map。

**3.3 美术替换** —— 探索场景从灰盒换成 3D 环境资产（模块化 Prefab + Lightmap）。纸片角色按**正面平视**画，不要按要求俯视 3/4 出图。

**3.4 内容层：roadmap 51 条里 20 条完全没开始**（明细以 `docs/roadmap.md` 为准）

| 组 | 未开始 | 条目 |
| --- | --- | --- |
| A 探索层 | 3/6 | A3 泛化可交互对象与物资箱、A4 多场景流转、A6 相机边界与死区 |
| B 任务系统 | 2/4 | B3 任务完成写剧情标记、B4 进度重置与已完成列表 |
| C 叙事接线 | 4/5 | C1 NarrativeController + Installer、C2 剧情标记条件源、C3 剧情内容进表与校验、C5 战斗结果 → 剧情（C1–C3 与 §1.3 是同一件事） |
| D 演出与 UI 动效 | 1/6 | D6 角色动画补齐 |
| E 系统与流程 | 1/8 | E4 加载过渡 |
| F 内容与美术 | **5/5** | F1 剧本进表、F2 环境模型替换灰盒、F3 角色 / 立绘 / UI 皮肤、F4 音频、F5 内容校验器 |
| G 自家机制 | 4/6 | G2 画皮面具、G3 收押妖灵、G5 追逐 / 躲藏 / 弱点识破、G6 三结局硬分支 |
| H PC 适配 | **0/11** | 已全部完成 |

**玩法内容清单 `docs/design/features/`：16 份，只有 3 份有 demo**

| 有 demo（`PRP/mirror-core`） | 完全没实现（12 份） |
| --- | --- |
| 01 通灵视、02 照镜辨形、03 镜之耐久与镜碎 | 04 收押、05 归还与扣押、06 画皮傩演、07 两界之账、08 追逐躲藏与弱点识破、09 镜中窥探、10 疑案调查与信息、11 分支与结局、12 断其去处、13 妖的化形与破绽、14 流程与章节结构、15 叙事呈现 |

⚠️ 这 12 份**目前不能直接开工**：全部标着「草稿 · 待策划确认」，`docs/design/features/00_功能总览.md` 里还有 30 条跨文档矛盾 + 128 条待定问题没拍板。拍板后要把 `[待定]` 改成 `[原文]` 并回写产品文档，再逐个走 `/refine-prd`。
建议的精炼顺序：G1(02+01) → G4(03) → G5(08) → G3(04+05+07) → G2(06) → G6(11+12)。

---

## 4. 已知坑（别重踩）

- **生成物不手改**：`Library/` `Temp/` `Logs/` `obj/` `UserSettings/`、`*.meta`、`*.csproj/*.sln`、`packages-lock.json` 都由 Unity 生成。
- **共享工作区**：本项目常有多个会话 / 多人共用同一个工作区与 git 索引。提交一律按路径（`git commit -F <信息文件> -- <路径…>`），不要用不带路径的 `git commit` 或 `-a`；同一文件里混有别人未提交的段落时用临时索引出提交。细节见 `ai-docs/pitfalls.md`。
- **`invariants.py` 目前有两处常红**：字体资产、LailaFace 命名空间（§1.4）。看扫描输出时先排除这两条，别当成新问题，也别习惯性忽略——它会掩盖真问题。
- **移动 / 删除 / 重命名资产**用 `git mv` / `git rm`，必须连 `.meta` 一起。
- **角色纸片不要给 `CameraBillboard` 加 `ExecuteAlways`**：会把场景标脏，二十多个物体旋转进 diff。
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
