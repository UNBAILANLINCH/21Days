# 曲库、纪录与外部演奏增量

本轮按用户授权在独立 RhythmDemo 增量实施，代码仍在共享工作区，未提交、push、构建或上传。正式三谱、音频、Boot 与 prefab 不修改；一曲一谱，无多难度或新音符类型。

## 实现

- SongData 保留单 Chart，稳定身份分 songId/chartId/revision/rulesetId/scoringVersion；不可变 RunResult 包含 run/context/mode、Completed/Aborted/TechnicalError 和真实单局统计。
- Progress v2 保留 legacy BestScores/ClearedCharts，新增完整同局最佳/最近纪录、永久歌曲开放和完成 runId 去重。未知评分版本的旧分独立展示，不混入当前可比最高分，不造判定摘要；改谱后已开放歌曲不撤回。
- Archive 在 JsonSaveService 读取前检查信封/版本/完整纪录；旧档原字节 SHA256 命名备份只创建一次。坏档/未来档拒绝，并在 State 预检前清空旧内存进度，拒绝路径不会由 Exit 写空档覆盖。
- View 运行时构造 UGUI ScrollRect/条目/详情，保留选中和滚动位置；准备中可取消返回曲库，陈旧异步任务不清新请求的准备锁。
- ConfigureExternal/ClearExternal 与 ExternalSession 接受调用方权限/成功策略；浏览只驯服，所有外部场景演奏开局及消费要求当前控制和有效上下文。只有 Completed Combat 调成功/失败 hook；中断与技术错误独立通知；个人纪录只接受 Completed FreePlay。
- 自然完成先清理音频并更新纪录，再发布结果；匹配消费先封口，再读权限/策略/回调。去重仅消费者实例生命周期内，外部回调须按公开契约排队进入下一帧，跨存档世界事务后置。

## 实际验证

首个冻结快照编译后 Rhythm EditMode **171/171**，失败/跳过0，job `612bc1c2d5174e82a40f4123389acd67`。包括新纪录14例、外部契约15例和原始档案保护11例；手动 C# lint exit0，限定 diff --check 无问题。前次170例使用尚未刷新到的旧测试DLL，legacy混合断言2失败；已更新断言并统一编译，保留失败证据，不当作最终结果。之后只读审查补修校准入口/中断 UI 的同代次重入检查，须补最新快照运行结果。

最新同代次修复快照已统一刷新编译，DLL修改时间晚于所有本轮C#源文件；EditMode再次 **171/171**，失败/跳过0，job `c902a6b201e4408a9c45052af5c9b994`。只读模块复审 PASS，无剩余BLOCK。

最终八项Showcase完成 **7通过、1失败**，job `1dcb370b9cb24b1f87cc5d59621f0954`。报告 `Logs/verify/rhythm/20261005-014637/report.md` 整体 **FAIL**：五项新增曲库回放、旧Hold生命周期和跨轨组合通过；确认式校准 `Calibration_ReasonConfirmPreviewSupplementAndRestore_Work` 的 Suggested 预设收到 Drift。检查点失败1、非预期运行时异常0，分类失败原因尚未定位，未放宽门槛，不冒称fixture已证实有错。新回放实际覆盖20曲滚动/选择保持、锁定提示、完整纪录/失焦排除、v1备份字节/幂等/未来档保留、外部权限/一次消费/模式分流、慢保存取消/旧任务、保存失败恢复、校准取消和中断回调重开。20曲与legacy历史1700独立显示截图已实际核对。

响应父窗口释放Unity给Laila的协调请求，本节点停止继续占用编辑器。最后读取为干净LailaRecognitionPlaytest、idle/非Play/无测试、HoldScale=1。Console保留唯一明确注入的 `rhythm/progress_save_failed` / `IOException: library-fixture-progress-write`；并非零条Console错误。校准分类失败仍待后续取得Unity所有权定位/定向重跑，不宣称整批全部验收。

新增4项曲库 Showcase 和3项原校准/输入生命周期回归首次请求被拒：job `48d671b69fd240579e3a83fb09c2d667`，实际执行0项，提示编辑器正处于或进入Play。未擅自停止该运行；随后只读发现已回idle，待重新编译与运行。不凭测试源码声明通过。

真实目录已实际读到 `Application.persistentDataPath/saves`，`profile-rhythm-progress.json` **不存在**；本轮无旧真实进度文件可备份，未创建空假档。旧档备份与失败恢复测试均用框架临时隔离 SaveRootOverride，不写该真实目录。交付记录使用逻辑目录名称，避免提交本机标识。

三正式谱及 EditorSettings/ProjectSettings/TimeManager SHA256 与开工基线一致。新脚本 meta 由 Unity 刷新生成，未手写。项目 hooks 信任未确认，本批主动运行原 C# 检查。

## 后置与限制

真实乐师权限 adapter、Boot/正式 prefab、伤害值/失败惩罚、跨世界存档幂等未实施。当前暂停令牌冻结世界 tick，未来非正面战斗是否实时仍需设计决定。两进阶曲保留测试正曲，不继续精修听感。真人校准、物理输出、OS设备切换与Player验收不由本批自动测试替代。

独立审查与真实驯服 API 证据分别见 `PRP/rhythm-library-increment-review-20261005/`、`PRP/rhythm-musician-contract-20261005/`；审查所报 legacy 可比性 P2 已修并有最终回归。

健康扫描exit1，仅共享既有3处Gameplay命名空间及动态字体缓存，共4项；本轮回放后字体为37063KB，未清理。模块文档无新增失效链接。
