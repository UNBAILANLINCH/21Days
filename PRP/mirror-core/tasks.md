# tasks：镜核心机制 demo（mirror-core）

规则：每项完成打勾并记一句证据；执行中发现 prp.md 有误先改 prp.md 再继续。派单档位按 `.claude/rules/model-routing.md`。
设计已获用户批准（2026-09-28）。
**前置**：21days-cf 发来稳定点 commit hash 之前，不写 `.cs`、不往 `Assets/` 放文件、不占编辑器。开工前 `git status` + `ListAgents` 复核。

## 波 0（sonnet）——占位美术落地

- [x] T0 把 scratchpad `ark-yao/out/{nian,demon,slime}` 的 png 与 `meta.json` 拷进 `Art/Sprites/Characters/Ark/<名>/`，`_trueforms/yao_true_{demon,slime}.png` 拷进 `Art/Sprites/Characters/Ark/TrueForms/`；Ark README 追加三行 + TrueForms 一段；刷新资产；菜单 `21Days/角色/从序列帧生成小人…` 只对 `nian` 生成 `Chibi_nian`（目标高度 1.6）；真形两图设 Sprite（UI 用，关 mipmap），登记 Addressables（地址 = 文件名，不含扩展名）。
  覆盖：PRD「涉及模块」占位美术。
  - 证据：nian / demon / slime 293 个文件入 `Ark/`，真形两图入 `Ark/TrueForms/`（Sprite、关 mipmap），新建 Addressables 组 `Mirror`（地址 `yao_true_demon` / `yao_true_slime`，UI.asset 未动）；`Chibi_nian.prefab` 由 `FramePuppetGenerator.Generate` 生成（PPU 120、24 fps）；README 追加三行 + TrueForms 段。

## 波 1（opus）——数据、输入与规则层

- [x] T1 `Tables/Defines/yao.xml` + `Tables/Data/yao/1.json`、`2.json`（字段按 prp 2.2，JSON 不缺省字段）→ 跑 `gen-tables.ps1` 生成 `yao_tbyao.bytes` 与 C# 代码；核对 `IConfigService.Tables.TbYao` 可用。`Tests/EditMode/Mirror/YaoTableTests.cs`：两行、字段齐、`clue_items` 解析正确。
  覆盖：V12（数据侧）。
- [x] T2 `Data/Input/GameInput.inputactions` 的 Gameplay 图追加 `Mirror`（R / leftTrigger）、`MirrorSelf`（V / rightTrigger），先 grep 确认两扳机未被占用；只追加。
- [x] T3 `Runtime/Mirror/`：`MirrorConfig`、`MirrorSubjectKind`、`MirrorSubject`、`SpiritSightZone`、`MirrorRules`、`MirrorCrackRules`、`SpiritSightRules`、`MirrorSaveData`、`MirrorService`、`MirrorSceneBinder`、三个事件结构（prp 2.3）。
  `Tests/EditMode/Mirror/`：`MirrorRulesTests`（扇形内外、距离边界、最近者优先、人 / 物 / 妖线索齐 / 妖线索缺、无目标、自照恒空白、`followsMonster` 取模型位置）、`MirrorCrackRulesTests`（0..3、夹取、剧情裂痕只缩范围不致碎）、`SpiritSightRulesTests`（任一条件、只对妖、半径边界）、`MirrorServiceTests`（写已照见 / 见过轮廓、首次照见才请求保存、分区不缓存：替换分区后读到新值）。
  覆盖：V1 V2 V3 V4 V5 V7 V8 V9（规则侧）。
- [x] T4 lint 逐文件退出码 0、编译零错误、EditMode 全绿。
  - 证据：代码先在仓库外暂存区写成，离线 csc 三程序集 0 错误、纯规则 52 条通过；拷入后 `refresh_unity` 编译 0 错误，EditMode 925/925。Luban 生成物在暂存区实跑 `Tools/Luban` 得到（签名 `cfg.yao.Yao` / `TbYao`），与跑 `gen-tables.ps1` 等价。偏离见 prp「波 1 定稿」。

## 波 2（opus）——呈现层、预制体与安装器

- [x] T5 `Runtime/Mirror/`：`MirrorInputPresenter`、`MirrorCrackPresenter`、`SpiritSightPresenter`、`MirrorResultView`、`MirrorHudView`（+ 视野遮罩，层级按 prp 2.3 取定并回写 prp）、`MirrorShatterView`、`MirrorInstaller`；埋点按 prp 2.6。
  覆盖：V6 V8 V10 V11（代码侧）。
  - 证据：呈现器 3 个、View 4 个、Installer、`MirrorResultInfo` / `MirrorCrackTracker` 先在暂存区写成（离线编译 0 错误），拷入后编译 0 error；偏离见 prp「波 2 定稿」。
- [x] T6 自制占位图：`Art/Sprites/Fx/Fx_SpiritShadow.png`、`Art/Sprites/UI/Mirror/{ui_mirror_frame,ui_mirror_icon,ui_mirror_crack_1..3,ui_mirror_vignette}.png`（脚本生成即可，不用方舟素材）。
  - 证据：9 张自制占位图入 `Art/Sprites/{Fx,UI/Mirror}/`；集成时暗角图重生成（透明核缩小、边缘全黑，1..2.5 倍放大下仍可见，3 道剧情裂痕时屏幕边缘亮度 201→121）。
- [x] T7 预制体 `Prefabs/UI/Mirror{Result,Hud,Shatter}View.prefab`（及遮罩 View）在预制体舞台里搭，Addressables 地址 = 类名；`Data/Mirror/MirrorConfig.asset`；Boot `GameBootstrap` 挂 `MirrorInstaller`（Loot 之后、Exploration 之前），拖 config。YAML 复核引用非空、活动场景无散件，Boot diff 纯增。
  - 证据：`build_mirror_assets` 执行一次；四个预制体在 `Prefabs/UI/Mirror/`，字段非空、TMP 全为 NotoSansSC；`Mirror.asset` 6 条地址；`UI.asset` / `AddressableAssetSettings.asset` md5 未变；Boot diff 纯增 14 行，`MirrorInstaller` 第 10 位；集成中 `MirrorResultView` 追加深色文字卡 `TextPanel` 与刻痕铭牌 `EngravingPlate`（构建脚本已同步）。
- [x] T8 lint、编译、EditMode 全绿；从 Boot 冒烟一次（进场景、按 R 照空地得「照不到」、按 V 自照、控制台 0 error）。
  - 证据：EditMode 1022/1022（970 + 52）；从 Boot 冒烟：`mirror_hud_opened`、`mirror_subjects_bound(6, zones 1)`、R 得 `mirror_cast(human, 3, 5)`、V 得 `mirror_self`，0 error（临时存档目录，真实槽未动）。

## 波 3（sonnet）——SampleScene 布置

- [x] T9 按 prp 2.5 用 MCP 布置并当场保存：`enerme` 挂 `MirrorSubject(Yao,1,followsMonster)`；新增 `Yao_WellWoman`（`PuppetVisual` + `Chibi_nian`，`MirrorSubject(Yao,2)`，无树气泡一句占位破绽）；三个村民 `MirrorSubject(Human)` + 方舟头像；一只箱子 `MirrorSubject(Object)`；另一只箱子 `itemId = 1005`；`SpiritSightZone(dim)` 罩住妇人、出生点在区外。脚本幂等。SampleScene diff 只增不删（除改 itemId 一行）。
  覆盖：V12（场景侧）及回放前置。
  - 证据：`wire_sample_scene` 日志与 README 逐行对上；YAML 文档 568→636，删除 0、新增 68、改动 6（组件列表与 SceneRoots）；`Yao_WellWoman` 无 `ExplorationPointOfInterest`，`PuppetVisual` 下只有 `Chibi_nian`；`Crate_A` 早已是 1005，未改。

## 波 4（opus）——回放与回归

- [x] T10 `Tests/Showcase/Mirror/MirrorShowcase.cs`（新口径：`ScenePath => null` + `EnterWorldFromTitle()` + `Input.Press / WalkTo`，读 `module-verify.md` API 表与 Exploration / Session 样例）：照村民、照妇人得轮廓 → 开箱拿信 → 再照得真形、自照、进出昏暗区、三次被击 → 镜碎页 → 任意键重开（裂痕 0、箱子仍开）、对话中按 R 无效。每步截图。`/verify-module Mirror` PASS。
  覆盖：V1–V11 回放侧。
  - 证据：6 条用例，第 3 轮 PASS（`Logs/verify/mirror/20260928-084017/report.md`，0 失败 0 异常）；修回放两处站位 / 绕行（长凳、路障锥）；回放读只读属性 `LastResult` / `Current` / `ShownCracks` / `ShownRadius` / `ActiveZone` / `IsRestarting`。
- [x] T11 复跑 `/verify-module` Player、Monster、Disguise、Exploration、Session；Player 的「受击 → 死亡」用例（21days-cf 提交说明里标注位置）改为断言镜碎页出现与重开，其余按需调；全部 PASS。EditMode 全量复跑。
  覆盖：V13。
  - 证据：Player 4/4、Monster 4/4、Disguise 2/2、Exploration 8/8、Session 2/2 全 PASS（`Logs/verify/<模块>/20260928-0845xx`）；`PlayerShowcase` 受击→死亡改为断言镜碎页、遭遇停、确认后重开且生命回满。

## 波 5（sonnet + code-reviewer）——文档与审查

- [x] T12 `/generate-doc mirror` 三件套、`modules.json`、catalog 补行；roadmap G1 / G4 / E6 状态回填（「demo 完成，待视觉验收」）；player / monster 三件套补「战败 → 镜碎页」一句。
- [x] T13 `invariants.py` / `gc_scan.py` 零新增报错；code-reviewer 无 BLOCK。
  覆盖：V13。
  - 证据：code-reviewer 0 BLOCK / 2 WARN / 1 INFO；INFO（Installer 注释）已改；两条 WARN（`MirrorInputPresenter.ShowAsync` 罕见时序下 `imageHandle` 未释放、`SpiritSightPresenter.LateTick` 每帧 `Camera.main`）修改被权限分类器拦下，未修，记入 mirror guide 已知限制待开发者决定；`gc_scan` 只报既有字体资产膨胀。
- [ ] T14 `/review-change` 列改动清单，按路径提交，等用户授权。

## 2026-09-29 呈现器修复审查

仅包含下表 9 个文件。基础 demo 已在 `eed1a5f` 提交，本批修复波 5 记录的两项呈现器问题；不重复提交基础资产。
状态：`review-change` 完成，本批未发现阻断提交的问题，可按下表单独提交；未暂存、未提交，等待用户执行提交。

| 文件路径 | 位置（类 / 方法） | 改了什么 |
| --- | --- | --- |
| `Assets/_Project/Scripts/Runtime/Mirror/MirrorInputPresenter.cs` | `ShowAsync` / `ComposeAsync` / `Dispose` / `CloseQuietlyAsync` | 明确异步图片所有权，失败或取消后先清视图引用再释放，补关闭失败埋点 |
| `Assets/_Project/Scripts/Runtime/Mirror/SpiritSightPresenter.cs` | 构造 / `OnBeginCameraRendering` / `Dispose` | SRP 渲染前使用当前主相机，对称订阅与退订 |
| `Assets/_Project/Scripts/Tests/EditMode/Mirror/MirrorInputPresenterTests.cs` | 异步生命周期用例及受控服务 | 覆盖晚返回、取消、打开失败、重复开关、淡入中关闭与关闭失败 |
| `Assets/_Project/Scripts/Tests/EditMode/Mirror/SpiritSightPresenterTests.cs` | 渲染与释放用例 | 覆盖切镜、标签变更、禁用、销毁、非 Game 相机及退订 |
| `Assets/_Project/Scripts/Tests/EditMode/Mirror/SpiritSightPresenterTests.cs.meta` | Unity 生成 GUID | 配套新增测试资产 |
| `ai-docs/docs/modules/mirror/mirror-module-guide.md` | 生命周期 / 相机 / 验证入口 | 同步实现约束、真实验证证据与已有待办状态 |
| `ai-docs/docs/modules/mirror/mirror-external-api.md` | 呈现器只读属性 | 更新源码定位 |
| `ai-docs/docs/modules/mirror/mirror-extension-guide.md` | 新结果图片 / 通灵视条件 | 补异步图片扩展约束，更新源码定位 |
| `PRP/mirror-core/tasks.md` | 本审查记录 | 固定本批提交范围、收尾门和提交说明 |

- 埋点门：实跑 `instrument-module/scan.py Mirror`，补 `mirror_close_failed`（异常、`disposed`）。`ShowAsync` 取消及作用域销毁是正常退出；普通异常已有 `mirror_view_failed`，图片失败已有 `mirror_image_failed`。面板开关耗时由 `core.ui` 记录，渲染回调不加每帧埋点；其余扫描候选均为本次未改的历史代码。
- 沉淀门：图片所有权转移与 URP/SRP 相机边界已在本批模块指南和扩展指南说明；审查阶段本次无新结论，不重复追加 pitfalls。不改 lint / rules，未跑行为 eval。
- 自查：4 个 C# 的 lint、范围内 diff 检查通过；新增测试 `.meta` 存在；无本机标识、`Debug.Break` / `TEMP` / `HACK` 残留；索引为空。
- 验证证据：首轮 Mirror EditMode job `bbf2d4b2a9c6422fa5879e4731f4b25c` 执行 140 项成功；Showcase `20260929-055540` 六项 PASS。追加埋点后，含 Mirror 的回归 job `0f9f708b6a1e43f1b0786e709e146de6` 为 **461/461 通过、0 失败、0 跳过、11.6726942 秒**。主审直接读取准确 `result.summary` 和控制台错误 0 条，并复核首轮报告及关键截图。新增一行错误埋点未改呈现行为，未为此重复整批视觉回放。
- 全仓 GC 仍为 exit 1：3 项既有 `Gameplay` / `Game.LailaFace` 命名空间不匹配、1 项既有动态字体膨胀。无本批失效文档引用或缺 `.meta`；这些文件不在本提交范围。
- 保留限制：相机接线仅覆盖当前 URP/SRP；既有长真形描述溢出、影子偏淡及暗角强度问题未纳入这两项修复。

拟用提交信息（本批同属修复）：

```text
fix(mirror): 修复结果图片释放与通灵视相机朝向

- 结果图片在异步打开、取消和销毁中按所有权释放
- 通灵视渲染前对齐当前主相机并在释放时退订
- 补异步生命周期与相机切换回归、关闭失败埋点
- 同步模块文档、验证证据与提交范围
```

按上表路径单独提交；Narrative、Dialogue、Session、Quest、Replay、Laila 美术与场景、字体、渲染配置、MCP 工作流文件均不混入。
