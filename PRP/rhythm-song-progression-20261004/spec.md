# 音游选曲与进阶流程

## 范围与验收

现有 58 枚虫儿飞作为入门关；两份新伴奏作为进阶测试曲，提供选曲、锁定状态、达标解锁、最佳成绩和本机持久进度。沿用四轨 Tap/Hold、校准、暂停及中断保护。素材仅本机测试；自动制作谱面不能宣称已人工听感验收。

1. 新档仅开放虫儿飞；未达标及中断不能解锁。
2. 完整结束且达标后两首进阶曲开放，最佳成绩与通过状态重启后恢复。
3. 选曲→开始→结算→重试/返回选曲成立；换曲清除旧输入、长按、时间轴及成绩。
4. 两曲来自实际可读音频及分析证据，片段范围、谱面窗口/长按冲突和音频引用经过校验。
5. 三种偏移分别保持原语义，练习/校准不能写歌曲进度；测试存档隔离，重复点击和中断安全。
6. Unity 回放、规则测试与文档反映实际结果，人工听感单独待验收。

## 工程决定

- 扩展既有 Rhythm 模块，不建立另一套音频/输入或存档服务。
- 曲库用只读 SO，歌曲仍是原 RhythmConfig；曲目稳定 ID、标题、难度、前置关和通过比例可配置。
- 原评分 Perfect=1000/Good=500/Miss=0；没有旧通关配置，采用温和的满分比例 60% 作为初始可调门槛，只完整歌曲结算可达标。
- 进度使用 ISaveService 独立档案，以曲目及谱面标识隔离记录；最佳分只增、达标不因后续失败撤回。
- 选曲在既有面板中提供明确页面；换曲先停止会话，锁准备按钮，加载并验证新资源后重建规则和输入资产，出错恢复可重试状态。
- 输入/视觉偏移属于本机用户设置可跨曲保留；谱面对齐仍各曲独立，不把其中一种写到另一种。

## 计划文件

Runtime/Rhythm：新增 RhythmCatalogConfig、RhythmSongData、RhythmProgressData、RhythmProgressRules、RhythmSelectionRules；修改 RhythmInstaller、RhythmState、RhythmView。
Tests/EditMode/Rhythm：新增进度规则测试；Tests/Showcase/Rhythm/RhythmShowcase.cs 增加完整选曲解锁回放。
Data/Rhythm：曲库及两份新谱面；Audio/Rhythm：两份获授权本机音频副本；既有 RhythmDemo 仅补曲库引用。
文档：Rhythm 三件套、PRP 接入/分析说明及 HANDOVER；不会提交或打包。

## 素材与编辑器初检

Library 元数据已确认两份音频和大小。Windows 规范下载及一次重试均在身份元数据步骤失败（缺少 os.setxattr），未绕过或采用临时下载产物。父线程后续通过支持的 Library 取用核实上传 bytes/hash，与本机 Downloads 文件完全匹配，来源阻塞解除；实际解码和候选谱证据见 audio-and-chart-evidence.md。
历史初检为用户正在 Play RhythmDemo，先在 PRP 准备；用户结束试玩并明确授权后已整合。当前实施与验收以 [实际报告](stage-report.md) 为准，不再等待素材身份确认或重复部署。
