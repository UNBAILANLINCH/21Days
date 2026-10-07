# 两首进阶曲资源独立提交

本批仅保存已导入的音频、现有 RhythmConfig 类型的两份谱面 SO、Unity 生成的配套 meta 和只读验证证据。它不包含曲库类型、RhythmDemo 场景或当前 State/View 增量；拉取本批不会自动改变选曲入口。

两份音频此前经支持的 Library 取用流程核实上传 bytes/hash，与本地导入文件一致。本批保留原 MP3 bytes，不重新编码；沿用仓库现有 binary 属性，不新增 LFS 配置。

| 资源 | 字节 / SHA-256 | 测试谱 |
| --- | --- | --- |
| HybeboyGuitar.mp3 | 9188876 / `237399cc3025c86a8f746372c8a6903d1bdbf03a9e1425a97d204abafbafeab0` | 164 枚：157 Tap、7 Hold；截取 24.495–101.295 秒 |
| AttentionInstrumental.mp3 | 7448076 / `04412c1a31551c8e34719e795bc40af1b3068be02b701f5a65bde667d82956b3` | 229 枚：214 Tap、15 Hold；截取约 25.490–98.494 秒 |

谱面来源为现有 Chrome 解码后的起音包络、BPM/相位候选和固定网格分析。两份 `*.chart.json` 保存每枚音符依据，`audio-analysis.json` 保存解码时长及自动分析结果。自动候选不是人工音乐验收；Hold 音乐语义、乐句边界与浏览器/Unity PCM 对齐尚未确认，chartOffsetMs 保持 0。素材按本机测试用途保存，不据此推断发布授权。

## 只读复核

在项目根目录运行：

```powershell
node PRP/rhythm-song-progression-20261004/verify-charts.mjs Assets/_Project/Audio/Rhythm/HybeboyGuitar.mp3 Assets/_Project/Audio/Rhythm/AttentionInstrumental.mp3
node PRP/rhythm-song-progression-20261004/verify-installed.mjs --resources-only
& PRP/rhythm-song-progression-20261004/verify-runtime-charts.ps1
```

前两项检查来源 bytes/hash、每枚 SO 音符字段、片段和判定边界、已存在的脚本/输入 GUID 与 meta。第三项加载现有且不旧于被测源码的程序集，不启动 Unity、不调用编辑器 API、不写档案；若没有运行中的编辑器可供发现路径，传 `-UnityEditorPath '<本机已有 Unity.exe 路径>'`。

2026-10-05 本批提交前重跑：来源/边界与正式资源字段检查通过；真实 Rules/InputQueue 30 场景通过，包括两曲各 -300/0/+300ms 逆序积压全 Perfect、全部 22 Hold 提前 1ms 松开只结算一次 Miss、两曲全漏结算。

当前曲库增量的 171 项 EditMode、5 项新回放与独立审查 21 项通过属于其他验证节点；确认式校准仍有 Suggested→Drift 回放失败。它们不作为本批运行时接线通过的声明。后续另处理该失败和异常时钟诊断先于守卫的容错顺序问题。
