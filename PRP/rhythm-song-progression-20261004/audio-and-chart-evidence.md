# 进阶测试谱证据

## 来源

父线程通过支持的 Library 取用流程取得真实上传字节，核实大小与 SHA-256；本机 Downloads 两文件完全匹配，可直接采用，不再需要用户确认来源。Windows 本机规范下载的元数据失败没有绕过，失败临时产物未使用。

| 文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| hybeboy吉他伴奏.mp3 | 9188876 | `237399cc3025c86a8f746372c8a6903d1bdbf03a9e1425a97d204abafbafeab0` |
| attention原曲伴奏.mp3 | 7448076 | `04412c1a31551c8e34719e795bc40af1b3068be02b701f5a65bde667d82956b3` |

使用现有 Chrome AudioContext 解码，44100Hz、双声道；不安装软件，不发布或上传素材。此时间域未人工与 Unity PCM 对齐，保持 chartOffsetMs=0，不自动添加过去虫儿飞测得的 11ms。

## 节拍候选与片段

| 曲目 | 全音频秒 | 周期候选 BPM | 第一/次候选相关分 | 候选拍相位秒 | 测试片段秒 |
| --- | ---: | ---: | --- | ---: | --- |
| hybeboy 吉他 | 229.645351 | 100.0 | 17.802 / 7.270（102.1） | 0.495 | 24.495–101.295 |
| attention | 186.131156 | 105.2 | 5.760 / 2.850（103.1） | 0.395 | 25.490057–98.493859 |

分析先提取 10ms 音量包络和低频包络正向变化，局部归一化起音；15–145 秒搜索 75–160 BPM 的一拍/两拍相关性，再搜索最强相位。完整候选、片段起音和 RMS 包络见 `audio-analysis.json`，每枚音符依据见两份 `*.chart.json` 的 noteEvidence。

先取 128 拍网格（32 个四拍单元）以验证选曲和难度递进，保留完整音频以扩展后续段落。这是数学网格测试片段；拍号、完整乐句边界和声部归属尚未人工听审，不声称已经乐句校准。

## 难度与可玩性

| 曲目 | 总数 | Tap | Hold | 双键同时额外头 | 音符/秒 | 最大 +300ms 补偿末窗截止/片段长度 ms |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| 原虫儿飞 | 58 | 56 | 2 | 既有谱面 | 1.450 | 39906 / 40000 |
| 吉他 Lv.3 | 164 | 157 | 7 | 14 | 2.135 | 76645 / 76800 |
| attention Lv.4 | 229 | 214 | 15 | 18 | 3.137 | 72873 / 73003.802 |

主拍保留，八分拍仅采用网格附近且强度达标的起音；少量强拍双键，长按按乐句单元分布。长按是持续保持手势，不宣称音频有对应持续旋律音；RMS 检查排除静音区。不把每个波形瞬态都做成音符。

ID、类型、同轨 ±140ms 头窗、Hold 占用、预滚覆盖 ±300ms 视觉值、末尾正补偿窗口、资源标识和文件 hash 均通过纯校验。现有真实 Game.Runtime Rules/InputQueue 再测 30 场景：两曲各 -300/0/+300ms 逆序积压全 Perfect、全部 22 Hold 各早放 1ms 只记一次 Miss、两曲全漏正确结算。

## 重现

```powershell
$rhythmPlaybackTools = Join-Path $env:APPDATA 'npm/node_modules/playwright'
$rhythmAudioFolder = Join-Path $env:USERPROFILE 'Downloads'
node PRP/rhythm-song-progression-20261004/analyze-audio.mjs $rhythmPlaybackTools PRP/rhythm-song-progression-20261004/audio-analysis.json (Join-Path $rhythmAudioFolder 'hybeboy吉他伴奏.mp3') (Join-Path $rhythmAudioFolder 'attention原曲伴奏.mp3')
node PRP/rhythm-song-progression-20261004/make-charts.mjs
node PRP/rhythm-song-progression-20261004/verify-charts.mjs (Join-Path $rhythmAudioFolder 'hybeboy吉他伴奏.mp3') (Join-Path $rhythmAudioFolder 'attention原曲伴奏.mp3')
& PRP/rhythm-song-progression-20261004/verify-runtime-charts.ps1
```

## 当前限制

两份音频现已导入 Unity（PCM、DecompressOnLoad、预加载），三曲正式曲库与 RhythmDemo 引用已经接线。吉他已完成真实 Input System 全曲验证；Attention 和档案重读的最终结果见 [验收记录](stage-report.md)。`integrate-assets.cs.txt` 是已执行的历史整合片段，不能对已有资产自动重跑。素材仅按本机测试使用，不声称获发布授权。人工音乐贴合度、声卡物理输出延迟以及浏览器/Unity 解码逐采样对齐仍未验证。
