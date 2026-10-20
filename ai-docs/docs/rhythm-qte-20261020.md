# 虫儿飞 QTE 补丁接入记录

## 范围与来源

交付包为 `21Days-QTE-ChongErFei-patch.zip`（258382 bytes）、`21Days-Story-Environment-patch.zip`（134947 bytes），补充边界为 `QTE-Change-Notes.txt`。两包均声明基于 `d2f241dca8088e95eb1261b798793eb59a7cb9c1`。包内历史静态检查不作为本次 Unity 验收证据；未执行附带脚本。

已验证 manifest 的 QTE 42 份 before/after SHA256 与环境 20 份 SHA256 全部一致。QTE 21 个变更文件与接入前工作区的 before 文本一致（统一换行比较），没有需要覆盖的本地 Rhythm 后续差异。采用经过逐项审查的差异块，通过 apply_patch 增量接入；不复制整份 after 文件。

试点只在独立 `RhythmDemo` 显式启用，代码开关默认关闭，不迁移 Boot／ShopPlaytest，不改变其它两曲、原 58 枚谱、旧进度或奖励。表现为当前／下一组收圈、同拍并排与独立长按提示；沿用四个 Lane 动作及时间戳输入、歌曲绝对时钟、输入批次与暂停链路。没有角色周围光圈、Boss 伤害或新战斗流程。

## 规则与原始数据

| 项目 | 依据／值 |
| --- | --- |
| 精确／成功窗 | 交付设计指定 ±100／±250ms；两类成功平等计入通过数 |
| 长按尾部 | 到尾自动完成，提前释放最多 100ms，整枚只结算一次 |
| 通过 | 完整局成功数达到 60%，整数向上取整；58 枚须 35 枚，34 枚失败 |
| 同键交叠 | 选最近未决目标，等距取较早；同刻及长按占用冲突拒绝 |
| 身份／诊断 | `qte-beat-v1`；写 v2，兼容读 v1 严格规则 |
| 保存 | 试点校准 `rhythm-qte-pilot-calibration`；不读写旧曲库成绩 |
| 音频 | `Assets/_Project/Audio/Rhythm/ChongErFei.mp3`，3912192 bytes，SHA256 `F5C98E150D1684B8308DA39EE3384CF72B8F8E77C4F0028FE58438292F2362C4` |
| 配置片段 | 原配置起点 16 秒、长度 40 秒、chartOffsetMs=0、预滚至少 3 秒 |
| 原谱静态核对 | 58 枚（56 Tap、2 Hold），最后终点 39.466 秒；同键一组头部间隔 314ms，宽窗下无长按占用冲突 |
| 既有输入资产 | Lane0～3 绑定键盘 D/F/J/K；本次不新增设备键或触控旁路 |

以上窗口来自用户交付设计，不是物理延迟测量或真人手感结论。BPM 元数据为空，不从文件名推断节拍。Unity 实际读取音频长 163.008 秒、7824384 帧、48kHz 双声道；导入为 PCM／DecompressOnLoad。原 16～56 秒片段在边界内。GetData 解码读取起点附近非静音样本成功，采样峰值约 0.13934；这不证明旋律或对拍正确。

## 环境包兼容性

| 分类 | 实际状态 |
| --- | --- |
| 已合入且文本一致 | GameFlow、IGameFlowTransitionStatus、SpotlightEnvironmentBuilder／Config／Rules、GameFlowTests、环境 Config／Rules 测试；见当前提交 `1834c17` |
| 已合入且有后续增量 | EnvironmentView 增加非缩放粒子时间，PlayMode 测试增加对白时停发射回归；保留 |
| 已合入且有本地角色／镜头差异 | SpotlightStoryFlow 保留王录业／值守呈现、窗边镜头与对应清理；保留 |
| 文档差异 | Narrative 三份文档保留本地接线、回放证据与未接锚点说明 |
| 与 QTE 依赖 | 两包没有源码相互依赖；本次不重复应用环境包，不扩张环境效果 |

书库烛光和水口薄雾缺正式锚点的状态沿用 Narrative 文档，不能称为全部环境效果均已接线。环境历史验证仍按原记录范围解释，本次 QTE 验收不替代其视觉验收。

## 本次验证与修正

用户要求不向其它聊天发消息，已等待人物环境与行酒令聊天结束后才串行修改 C#、导入与回放。实际连接工程与编译版本均为 Unity `2022.3.62f2`。未提交、未推送。

| 验证 | 本次证据与结果 |
| --- | --- |
| 编译／lint | 接入及最后文案调整后编译，控制台 error 0；19 个改动 C# 文件手动 lint 通过 |
| 首轮 EditMode | job `4d2ec7b2f853417e92ec3a2e44ba7bc3` 完成239项，新增圆环网格测试3项因反射同名重载歧义失败；保留此历史，不当作规则通过 |
| 修后 EditMode | 指定 VertexHelper 参数类型，未削弱断言；job `83bd4749a33343a894d29ca72129bdeb`，239/239 PASS，失败0、跳过0 |
| 回放三条 | job `7eae6e67e461424ca55970d1713bef52`，3/3 PASS，失败0、跳过0；[061448报告](../../Logs/verify/rhythm/20261020-061448/report.md)，零失败检查／零运行时异常 |
| 布局补验 | job `27a00b52b3b84763b46f2cbd0dee653f`，单条成功；首次取结果超时，随后读回 succeeded 与 [061729报告](../../Logs/verify/rhythm/20261020-061729/report.md) PASS，未将超时写成测试通过证据 |
| 最终短谱复验 | job `8cbd48518364478eb9cb3e7c5a1f5cd2`，1/1 PASS，失败0、跳过0；[062105报告](../../Logs/verify/rhythm/20261020-062105/report.md)，包括两种尺寸按钮边界、中心射线命中及暂停退出中文文案 |
| 真实入口 | 启用、恢复、再次启用菜单均实际核对标记；保存 RhythmDemo 后真实 Play，直接单曲准备页。点 StartButton 后 qte=true／58枚，歌曲时刻推进；暂停后点 PauseExit，回 TitleState、面板关闭、playing=false、结果 Aborted |

三条回放分别是旧模式 `CrossLaneChordsAndMultipleHolds_RealInput_Work`、QTE短谱 `QtePilot_ChordsHoldRetryAndChineseOutcome_Work`、原谱 `QtePilot_Original58Notes_CompleteAndKeepLegacyProfiles`。短谱使用临时副本和真实 Input System，包含双押、长按、暂停时冻结、暂停中松开后只失败一次、重试全漏按继续至结束、失焦中断、手动试听取消、自动参考拍取消与提示残留清理。原谱实际播放原40秒片段，58枚全部成功、2枚长按完成，诊断重放一致；完整局及退出后旧校准123／45哨兵和旧解锁／成绩哨兵保持，动作图与世界暂停归属恢复。所有自动回放沿框架 SaveRootOverride 隔离存档。

额外修正仅有：新增网格测试反射重载选择；复用已有页面渲染工具检查两种尺寸；独立试点暂停退出文字从“返回曲库”修为“返回标题”。未修改原谱、音频、输入绑定或其它曲目的数值。

## 当前入口与截图

`Assets/_Project/Scenes/RhythmDemo.unity` 已通过编辑器保存 `qtePilot=1`。代码字段默认false，Boot／ShopPlaytest未开启。恢复方法：停止Play，打开RhythmDemo，执行 `21Days/音游/虫儿飞试水/恢复原版入口` 并保存；不删除试点校准档或旧曲库。场景另序列化既有默认 `autoEnterDemo=1`，语义未变。

编辑器已退出Play并恢复原 SampleScene，回放节奏保持原x1。已查看以下实际Unity渲染图；最终观感仍由开发者确认。

- [1280×800 当前／下一组](rhythm-qte-20261020/1280x800-qte-prompts.png)
- [1920×1080 当前／下一组](rhythm-qte-20261020/1920x1080-qte-prompts.png)
- [1280×800 中文结果](rhythm-qte-20261020/1280x800-qte-success.png)
- [1920×1080 中文结果](rhythm-qte-20261020/1920x1080-qte-success.png)
- [原58枚谱完整演奏结果](rhythm-qte-20261020/original-58-success.png)

等待阶段gc_scan曾因既有三份动态字体缓存过大退出1；全部回放后已通过编辑器清理相关Dynamic字形数据。无临时InitTestScene残留；最终gc_scan退出0，链接、模块目录、钩子自测及工程静态不变量全部通过。diff空白检查通过，改动脚本缺失meta为0，最终控制台error为0；ProjectSettings仅保留任务开始前已有的PackageManagerSettings差异。

未做Windows构建、移动端／手柄接线、真人试听与对拍、声卡／显示器／键盘物理延迟测量、性能验收。独立Boss横幅与战斗伤害未接。自动成功不等于手感已调好，缺失书库／水口环境锚点仍沿用Narrative记录。
