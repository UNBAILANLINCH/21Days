# 音游选曲流程：整合与验收入口

## 当前结果

最新实际验证、回放报告、已修复问题与交还状态以 [验收记录](stage-report.md) 为准。

2026-10-04：本目录候选已在用户授权结束试玩后整合进 Assets；RhythmDemo 的场景改动仅新增曲库引用，虫儿飞资产与整合前备份 hash 一致。没有改 Packages、提交、打包或发布；Unity 回归使用隔离测试档案，不写用户真实进度。本目录的 `.cs.txt` 是整合候选快照，正式实现以 Assets 当前代码为准，不可盲覆盖。

现有 .NET SDK 8.0.303 离线编译三份纯 C# 逻辑及项目真实 `ISaveData`，0 警告、0 错误；32 个场景全部通过，JSON 往返使用项目实际 Newtonsoft 后端。正式修改的十个 C# 文件已手动 lint 通过；`Math.Ceiling` 的精度理由保留在原行，不修改 lint 规则。两份上传音频身份与现有本地 bytes/hash 一致，已解码生成测试谱；详见 [音频与谱面证据](audio-and-chart-evidence.md)。两新谱真实 Rules/InputQueue 30 个场景通过，Unity 全量刷新编译成功，EditMode 120/120 通过。原四项 PlayMode 通过；首次新增长流程超时后完成 Attention 与实际存档重读定向补验 1/1（81.66 秒）。不把超时改写成通过，详细分段证据见验收记录。

| 接入曲目 | 音频截取（秒） | 测试谱 | 自动分析 BPM |
| --- | --- | --- | --- |
| 虫儿飞 | 保留原 58 音符谱与原资产 | 入门 Lv1；56 Tap / 2 Hold | 保留原配置 |
| hybeboy 吉他伴奏 | 24.495–101.295 | Lv3；157 Tap / 7 Hold，共 164 | 100 |
| Attention 伴奏 | 25.490–98.494 | Lv4；214 Tap / 15 Hold，共 229 | 105.2 |

初始曲库开放虫儿飞，完整结算达到 60%（34800 分）后解锁两首进阶曲；通过百分比可逐曲配置。界面已接入选曲、门槛、最高分、通关状态及返回选曲。自动起音/网格制谱尚未经过人工听感校准，不把 BPM、四拍单元或 Hold 当作人工验证的音乐结构。

## 重跑纯逻辑

```powershell
& PRP/rhythm-song-progression-20261004/verify-pure.ps1
```

脚本清空 NuGet 包来源，直接编译这里的源文本，使用已安装 SDK；生成物进入系统临时目录，结束恢复进程环境变量。测试只进行内存 JSON 往返，不写玩家档案，不载入 Unity API；首次受限运行若 SDK 无权读取默认 NuGet 配置，须通过已授权的执行环境运行，不能据此声称测试已通过。

## 验证范围

| 范围 | 当前证据 |
| --- | --- |
| 门槛 | 58 音符 ×1000 满分，整数百分比 60%，目标 34800；比例、数量和溢出边界拒绝 |
| 解锁 | 无前置入门开放；达标完整结算才能标记通关；低分、未完全结算、中断不能通关 |
| 最佳分 | 只增不减，通关不被后续失败撤回，重复结算幂等，三曲隔离 |
| 版本隔离 | 曲目 ID、chartId、revision 共同生成转义组合键；任一变化不借用旧进度，冒号/转义字符无组合碰撞 |
| 档案数据 | 版本 1 DTO、缺字段默认值、null 容器恢复、内存 JSON 往返、异步写快照与后续变更分离 |
| 曲库关系 | ID 唯一，数组一一对应，前置存在，拒绝自环/循环 |
| 换曲请求 | 锁定请求不打断当前曲，准备中重复点击拒绝，退出/重试/换曲/中断使旧代次失效 |
| 结算资格 | 陈旧或错误曲目标识不能完成；练习、未完整结束不能获歌曲成绩资格；完成仅消费一次 |

`RhythmSelectionRules` 控制请求和结果资格，已接入正式 State；音频与输入清理由现有 StopRound/ReleaseActions 完成。纯逻辑 JSON 测试使用项目已有 Newtonsoft DLL；实际 ISaveService 写盘、重新进入和两曲演奏证据分别列于验收记录，不将纯逻辑结果等同于 Unity 结果。

## 文件职责与后续文档

| 候选 | 用途 |
| --- | --- |
| RhythmProgressData / RhythmProgressRules | 本地进度 DTO、稳定组合键、门槛与最佳分、前置校验 |
| RhythmSelectionRules | 选曲请求代次和结算资格纯逻辑 |
| RhythmSongData / RhythmCatalogConfig | 正式只读曲目描述与曲库 SO；本目录文本留作实施快照 |
| RhythmState / RhythmView / RhythmInstaller | 既有模块扩展的整合快照；后续修复以 Assets 正式代码为准 |
| tests/ / verify-pure.ps1 | 32 项可执行离线测试，引用实际 ISaveData 契约 |
| analyze-audio.mjs / make-charts.mjs | 已运行的解码/起音候选分析与网格制谱；生成 audio-analysis.json 和两份测试谱 JSON |
| verify-charts.mjs / verify-runtime-charts.ps1 | 文件 hash、谱面边界与真实既有判定程序集只读校验 |
| RhythmProgressTests / RhythmShowcase | 首次整合的测试快照；正式测试包含 32 项进度 NUnit 和分段补验，原回归保留 |
| integrate-assets.cs.txt | 已执行的目标接线历史片段；已存在资产不可盲目重跑 |
| verify-installed.mjs / stage-report.md | 正式资产字节/字段/引用核验与实际验收记录 |
| spec.md | 范围、验收、授权和素材/编辑器初检 |

正式文档按 generate-doc 增量同步：module-guide 记录选曲→会话→结算→档案的数据流与接线；external-api 记录选曲/返回、配置及查询前置条件；extension-guide 记录新增曲目/变更 revision/通过百分比与谱面冲突检查。HANDOVER 记录实际结果及剩余人工验收。

## 来源确认与维护

Library 本机规范下载与一次重试在 Windows 身份元数据步骤失败，未使用临时产物。父线程随后以支持的 Library 取用流程核实真实上传 bytes/hash，与已有本地文件完全一致；来源确认阻塞已解除。用户结束试玩并授权使用 Unity 后完成正式整合。

维护时先读取 Assets 与模块三件套，不盲覆盖这里的初始快照。素材、SO 与 meta 均已由 Unity 导入/生成；本次没有提交、打包或发布。
