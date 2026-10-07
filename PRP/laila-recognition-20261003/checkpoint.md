# 识别工程检查点

本文件是初次接线阶段记录；唯一当前执行入口为[spec§17](../laila-expression-recognition/spec.md#17-当前工程候选接手入口2026-10-04)，实时增量另见[realtime-and-stack.md](realtime-and-stack.md)。下列6项/135输入是初次证据，不当作本轮重新执行。最新收尾在文末。

2026-10-03。状态：实际实验接线完成，未通过人工语义验收。没有提交/推送，没有改模型、形态幅度、Unity版本或无关场景。

## 交付

- `Assets/_Project/Scenes/laila.unity`：局部真实识别对象与右上角结果面板；开始/结束均编辑态、dirty=false。
- `Assets/_Project/Scripts/Runtime/LailaFaceRecognition/`：只读17轴采样、Sentis CPU推理、错误/拒识与结果UI。
- `Assets/_Project/Scripts/Editor/Tools/LailaRecognitionTools.cs`、`LailaRecognitionCaptureWindow.cs`、`LailaRecognitionDeploymentCheck.cs`：局部安装、显式分组的未标注采集、部署配对守卫。
- Runtime/Editor asmdef加实际Unity.Sentis引用；Editor加现有TMP引用。Package Manager加官方Sentis2.1.3，保留用户既有GLTF包改动；packages-lock由Unity生成。
- `Assets/_Project/Data/LailaFaceRecognition/`：候选ONNX/JSON，源模型SHA256 `7e63c6fc1f2b4da857fbe4a3ba00885bf43f39b6483e3b572d65c0ad6b93396f`；资产配套meta均由Unity生成。
- `ML/expression-recognition/configs/rigs/laila_rig_v2_candidate.yaml`、`laila-v2-candidate-review.md`，新增候选绑定测试与对照脚本；正式v2未冻结。
- HANDOVER、DESIGN、catalog、README、spec§17及模块文档/注册同步；REFERENCES和旧上限分析不覆盖。

## 实测

候选训练和同资产锁定12轴对照都只用合成数据、种子42、默认预算及五类/过滤规则。17轴argmax宏F1略高，但合成阈值后的最终判对率96.57%，锁定12轴97.83%，没有证明17轴更准。完整配置、hash、metrics、混淆矩阵保存在已有忽略的artifacts中。

ONNX1000输入自检通过，opset15白名单通过，562.8KB。Unity CPU固定135输入probs最大误差3.58e-7、energy1.67e-6，最终判定135一致。20次识别组件启停释放/重建；20次面板启停后单次Pointer事件点击只触发一次识别；按钮首命中正确，17/17抓点仍首命中自身；缺模型、同对冲突、模型hash错配、近邻组跨集均拒绝。Editor零输入CPU推理20预热+500测量p95为0.4844ms，不含采样/UI，不外推Player。

截图 `result-ui.png`，数值/场景证据 `unity-verification.json`，UI/守卫/性能 `ui-lifecycle.json`。一个采集样本在ML/data/laila-captures-v2/unassigned中，始终unlabeled，不能冒充人工golden。

新增UnityEditMode6项任务aff09086325a49049e25474f38ea87be完成；ML123通过、2可选依赖跳过（mediapipe/cv2）。首次全量因既有pytest临时目录权限失败，改为仓库专用basetemp后通过。七个新增C#手动lint通过，当前无法确认hooks信任。

## 限制与下一步

候选系数/侧别尚待正式复核；51维空间没有独立眉中基，三段Down仍共享同侧browDown，不能声称全部新增信息无损。人工训练/开发/锁定测试没有创建，没有伪造标签；按近邻组采集、独立盲标、开发集修绑定与拒识，再冻结测试。

Windows Player/Android、实际鼠标设备、20次完整Play切换尚未验证。仓库build技能要求关闭编辑器后用scripts/build.ps1，本次保留编辑器，不绕过。初次gc_scan报告3处旧LailaFace命名空间/目录不匹配及本轮生成的Dynamic字体缓存；本轮额外字体缓存已精确还原到开始时字节，最终gc_scan仅剩3处旧命名空间问题。Unity生成场景YAML有尾空格，未手改序列化文件。过程曾读到SerializedObjectNotCreatableException编辑器日志；最终退出Play后错误读取为0，没有主动清空Console。

## 生命周期与眼角收尾（2026-10-03）

已有生命周期补丁核对在工作树，未重复应用。捏脸定向EditMode本轮12/12、失败/跳过0（任务`a5730221344f48b78cd2b9b3b6ed77b8`），覆盖销毁后捕获回调不访问失效对象、销毁时退订；该结果不是识别模块10项或全仓测试。实际只读Unity返回RhythmDemo、playing=false、compiling=false、dirty=false、loaded=1，Face预览live/dead更新委托均0，Console错误0，没有清日志。

已有29点眼睑网格记录继续离线定位：三角面1600相对Basis法向告警不单独等同翻面，但1213/1601在77–100存在严格内部交叉，100交线约0.000501场景单位，不能误报为纯法向指标问题；没有检出受影响40面与双眼/角膜相交。当前截图仍不足以验收任意角度/组合。几何及识别部署未改，眼角局部修复、独立人工盲标与Player仍未完成。

当前17→51→5，59D/6类仅研究；139点+29点均未标注，29点沿用同一开发近邻组，不变成29个独立人工样本。原始证据仅本地忽略目录`ML/expression-recognition/artifacts/laila_v2_candidate/lifecycle-eye-20261003/`；不自动将截图、模型训练权重、日志或临时patch混入工程提交。

收尾保护核对113个文件，`after.json`的changed/unexpected均为空；本轮没有再修改资产，已有生命周期补丁留在工作树。两个C# lint退出0；Gameplay埋点扫描的6个候选均在未改到的历史方法，本批新增的表现清理无该埋未埋点。gc_scan的链接/注册/hooks自测未报告失败，静态不变量仍报3处旧命名空间与已有12,381KB动态字体缓存；字体哈希未变且不纳入本批，不回滚用户内容。hooks信任未确认，使用手动检查补充。
