# 实时识别与Inspector异常修复（2026-10-03）

本文件仅记录实时增量阶段；当前执行入口统一为[spec§17](../laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)。本轮10项属于识别模块，后续捏脸生命周期12项与眼角诊断见[检查点末尾](checkpoint.md#生命周期与眼角收尾2026-10-03)，不能互作覆盖或人工语义验收。

本轮只在21Days执行；所有文件仅本地，无上传、Player构建、提交或推送。

## 当前识别行为

输入变化立即清除旧结果，合并等待中的输入，自动推理最多10次/秒；静止不重复。
采样使用独立复用缓冲和缓存委托；CPU输出完成作业后直接读入复用概率数组。
等待、当前结果、不确定/认不出、输入错误、模型错误分别显示；关闭面板释放推理资源。
模型/输入出错清除旧分数；有效输入恢复自动识别，模型重新初始化后恢复。
人工采集窗口仍手动，未产生人工作为金标的新标签。部署模型、阈值和几何未改变。

本轮最终EditMode任务 `7e0ea1090c0d4e76b7b70828cf0082ef`：10/10，失败0。
包含有效、越界、成对冲突各1000次实时采样托管分配为零；不代表Sentis/UI显示字符串本身零分配。
跨帧验收最新事实见 `realtime-verification.json`；截图 `realtime-current.png`、`realtime-unknown.png` 已检查实际面板。
真实FaceDragHandle方法驱动；自动化编辑器失焦会取消模拟指针，因此严格拖拽段仅临时停用反馈失焦取消，测试后恢复，并断言权重确实变化及推理数量非零。产品失焦保护保持启用。尚未代替物理鼠标、Player或人工语义验收。

## 误拒诊断

同一资产、相同1518条原始合成验证数据与标签，锁定12轴与完整17轴各自训练、投影和标定。

| 指标 | 锁定12轴 | 完整17轴 |
| --- | --- | --- |
| 已知样本拒识 | 13/1518，0.856% | 43/1518，2.833% |
| 被拒但argmax正确 / 错误 | 11 / 2 | 33 / 10 |
| 最终判对（拒识计错误） | 1485/1518，97.826% | 1466/1518，96.574% |
| energy阈值 | −1.18084979 | −1.56370342 |
| 温度 | 0.40682429 | 0.48712294 |

所有拒识均触发energy，单独min_confidence拒识为0，降低0.4置信度门槛无助于这批误拒。
17轴被拒的中性15条、高兴11条、悲伤7条、惊讶/恐惧8条、愤怒2条；
被拒样本最高分中位数0.869，energy中位数−1.151，高分不保证energy低。
两版绑定投影缓存复算误差0、无规范通道累加超过1后的裁剪饱和；17轴总体残差中位数更低。
因此已确认直接拒绝条件，但不能用单种子合成结果证明所有差异的因果来源。

开发标定曲线：17轴阈值约−0.932677对应已知误拒1.054%，标定负样本拒识90.35%；
当前阈值对应误拒2.833%、标定负样本拒识95%。替换成12轴绝对阈值时分别1.647%、92.15%。
放宽门槛会放行更多负样本；不同模型/温度的绝对energy也不可当作统一量尺。
部署仍保留−1.56370342和min_confidence=0.4；需独立人工开发集及代表性unknown确定取舍，不能用锁定测试调阈值。
详细每类分布、逐个被拒验证索引、同阈值与标定曲线：
`ML/expression-recognition/artifacts/laila_v2_candidate/rejection-diagnosis.json`。
复算入口 `ML/expression-recognition/analysis/laila_v2_candidate/rejection_diagnosis.py`，不重新训练。

当前索引0–4：中性、高兴、悲伤、惊讶/恐惧、愤怒。surprise/fear合并，disgust集外，拒识不是第六个情绪类。
五类动机引用旧22形态上限分析与历史规格§15.1；17轴尚无人工七类可分性证据，不自动扩类重训。

## 当前堆栈问题

附件报错与Console一致：`SerializedObjectNotCreatableException: Object at index 0 is null`。
完整栈：Editor.CreateSerializedObject → GetSerializedObjectInternal → get_serializedObject → RendererEditorBase.OnEnable → SkinnedMeshRendererEditor.OnEnable。
最新复现位于脚本域重载后；并非Sentis推理调用栈，也不是MCP拒绝信息。

只读诊断发现一个 `UnityEditor.SkinnedMeshRendererEditor`，HideAndDontSave，target=null，未被当前Inspector跟踪；当前Selection=null，Inspector未锁定。
普通ForceRebuild未移除。定向DestroyImmediate只清理该失效、未跟踪编辑器缓存，再重建Inspector，失效Editor计数由1变0。
证据 `inspector-repair.json`（UTC修复时间、对象ID、清理范围）。没有销毁Renderer、改资产、升级依赖、重置配置或清Console。
修复后显式域重载、代码编译域重载、Play进入/退出均按原始Editor.log新增异常数量核对；最终累计数另由交付检查记录。
这修复了当前残留编辑器状态，尚未证明原生Editor在所有后续操作中永不再产生失效缓存。

截图本地物化助手在Windows因 `os.setxattr` 不可用失败；后续Library读取及父会话实际像素确认报错，并以本地完整日志定位，未把该传输助手错误当成Unity根因。

## 最终综合验证

最终10项EditMode/C# lint完成。进度记录改成按阶段/每5秒追加JSONL后，综合复测通过且无进度写入异常：
2秒内463次真实权重变化，20次自动推理；旧结果失效最长5.31ms，连续推理间隔约100–105ms，Editor CPU推理p95约4.26ms（20次拖拽段采样，非Player基准）。
静止不重复、拒识/冲突/模型恢复、20次识别组件启停及20次Panel关闭/重开、临时场景卸载均通过；135输入判定一致，17/17抓点射线首命中自身。
最新 `realtime-verification.json` 为通过结果，替代此前每帧覆盖进度JSON遇Windows IO 1224的中断结果。测试结束已恢复31权重、模型和反馈，退订回调；当前结果/拒识截图已实际查看且按钮显示“重新识别”。
当前原生Inspector异常日志累计仍27条，清理后未新增；未主动清Console，历史Editor.log保留，最终Console读取0条错误。显式域重载后失效Editor目标为0，识别资源已释放，Play=false、scene_dirty=false。`gc_scan`仍报三个既有命名空间/目录不匹配，未修改这些无关类。
