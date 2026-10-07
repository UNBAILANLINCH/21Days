# 真人校准诊断修复交付（2026-10-05）

## 已应用
用户停止Play后实际确认RhythmDemo空闲、非Play/编译/测试、场景干净，随后通过apply_patch将PRP候选的最小差异应用。未整体覆盖共享dirty文件，不改三正式谱、Boot/prefab、Laila或asmdef；无Git提交/索引/push/打包。

本轮实际C#范围：RhythmCalibrationEstimator、RhythmCalibrationResult、RhythmState、RhythmView；新增RhythmCalibrationDiagnostic与EditMode的RhythmCalibrationDiagnosticTests；增量更新既有RhythmShowcase。新文件meta由Unity导入生成，已检查存在。before保存四份进入本轮时的dirty基线；Scripts保存最终运行时候选副本，不应拿整份副本覆盖未来dirty修改。

- Drift补测按钮与State入口均拒绝无效操作。时序覆盖有效、非Drift/Unstable、主轮有效数至少minimum-8且未补测，才允许一次8拍。NoInput/覆盖不足也不提供必然无效的补测。没有放宽任何质量门。
- 显示分段中位数最大差及20ms门，保原/完整重测可继续；明确应用才替换存档补偿。
- 每轮最多256条/128KB，本地保留格式专属最近5轮。完成、取消、中断自动写既有rhythm-diagnostics目录，不监听其他键、不采文本/设备身份、不外传。
- 记录每拍eventtime、mapped songtime、目标拍/误差、匹配/重复/窗外/非法/适应忽略、离群标记、各块数量/中位数/范围、跨度、全部targets、配置策略版本、桥接起点/跨度、捕获双时钟与跨度、原输入/视觉补偿、结束和结果原因。
- header.source区别runtime与isolated-test。真实目录基于Application.temporaryCachePath；测试用SaveRootOverride隔离。文件为calibration-v1-*.json，回收不碰RHD/其他文件。
- 保存失败不崩、不写Profile、不自动应用候选，失败说明在校准结果页保留；中断时也可在准备状态说明。不将文件路径或长OS错误覆盖在结果上。

## 真实验证证据
实际Unity编译完成，七文件手动project-lint exit0。第一次刷新短暂出现新类型尚未导入的CS0246，完成Unity导入后关闭；域重载bridge短暂断连经实际日志确认自动恢复，未重启/修改bridge配置。
- 定向EditMode44/44（失败0、跳过0），job efffe1932b0e4461b06fda559df0a80c，覆盖Diagnostic/CalibrationFlow/State。
- UI提示微调后，只补验受影响State/Diagnostic：33/33，失败/跳过0，job201b6d116d154cb2a722d2883b453e63。不累加成77个独立用例。
- 两项真实Showcase2/2，失败/跳过0，job27f56a917b01421595629d28777b7914，总用例耗时109.94秒；报告Logs/verify/rhythm/20261005-110306/report.md PASS、检查点失败0、运行时异常0。
  - 实际目录被文件占用：提示保存失败、正常停校准、候选保留、旧+100/+125档案不变；恢复目录后失焦取消留证据且旧值不变。
  - 完整8+32无输入固定结束；Suggested32实际收到32Press/Release、offset80/MAD45/spread0；允许一次8拍且原档不变；试听/保原/明确应用保存/重入恢复；Drift32实际误差80至150、MAD18.06/spread54.19，按钮及强制事件入口均拒绝补测，原档保留。
  - 完整JSON实际回读校验32条/拍号0..31/eventtime-inputStart=songtime/每拍误差/targets/四块/结果跨度/source隔离标识，不靠只查看日志宣称文件完整。
- 首次截图核对发现01保存失败通知的长路径覆盖结果说明；功能断言已通过的原PASS报告保留。修为结果页内短提示，仅补跑受影响用例；补验结果在收尾段。
- EditMode四条既有Dispose测试输出Destroy/EditMode提示，未冒充运行时异常；Showcase控制台与报告分开核对。
- gc_scan仍报既有3处LailaFace命名空间与动态字体约37MB，共4项；本轮不处理并行范围，不报告全仓gc通过。

## 尚不能得出的结论
收尾实测：页内短提示Showcase补验1/1通过（job3739c01e50ac4165bc71487fea88a76f，16.45秒），`Logs/verify/rhythm/20261005-110957/report.md` PASS，检查点失败/运行时异常0。实际截图确认保存失败说明位于结果页，长路径不再遮挡文字，旧+100ms/视觉+125ms保持。最终编辑器只读检查：RhythmDemo、dirty=false、isPlaying/isCompiling/isPaused=false、timeScale/holdScale均1；控制台错误0，Unity已归还。

此前真人日志仅reason/matched，没有原始采样，新增记录不能追溯历史。Synthetic fixture通过不等于真人校准通过或20ms门已验证普适性。保持阈值与用户个人offset，尚需一次真实失败记录区分门槛敏感性、人的偏差变化与采样映射问题。
适应阶段入口是warmup*beatSeconds，首采样目标为(warmup+1)*beatSeconds，因此最后适应拍晚按可计入一次窗外输入；该事件不会进入matched/kept，本身不能解释Drift。此门未修改。

## 真人最少下一步
如愿继续排查，仅正常跟拍一整轮即可；不需要反复重试或主动补测。结束后告知结果页是否显示本机诊断已记录，保留原值继续。由本机诊断读取最近runtime记录分析，不要求用户改补偿或证明自己的操作。也可跳过校准维持现值。
视觉表现仍由用户在Game视图/截图确认；本次不验物理音频/键盘回环、Player或真人成功率。
