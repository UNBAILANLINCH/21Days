# 虫儿飞调试谱接入记录

本次依据明确指令“这个是最新版的调试，你直接接进游戏就行”接入上传谱面；没有授权提交、推送或打包。保留空的 voice/reason，不推断旋律或伴奏意图，也不改写上传文件的 unreviewed-draft 状态。

2026-10-05 已获得稳定部分的本地提交授权。本谱面批次只包含权威资产、上传文本来源、回退字段和定向核验；HEAD 已支持 schema2/Tap/Hold 与四枚默认练习谱，无新增运行时代码依赖。当前滚动曲库/结果接口/确认式校准仍留工作区，不混入本批；历史回放结果仅对应下文所列范围。本节点重跑 `verify-import.mjs --after` 与现有真实 Rules/Queue 的五个离线场景均通过，没有操作 Unity；不推送或打包。

## 来源与回退

- `chongerfei-unreviewed-draft-v3.library-read.json` 是 Library 对 `chongerfei-unreviewed-draft-v3.json` 完整读取的文本副本：622 行，返回大小 13,659 bytes，has_more=false。不是原始字节下载，不声称验证了原上传文件的字节 SHA-256。该文本副本 SHA-256 为 `6c3df2c3511e59deab7c0f1c097cfbb93398e418e04b8a1720fc350fc28d160c`。
- `ChongErFei.before.asset.txt` 是正式资产的逐字节备份，SHA-256 为 `8d418063eaceea8c037ce382dbbab529f80ac9ce616e6a60b6f5ce1f897fb1ee`。音频 SHA-256 为 `f5c98e150d1684b8308da39ee3384cf72b8f8e77c4f0028fe58438292f2362c4`；导入前均与上传 source 匹配。
- `game-chart.fields.json` 是转换后的游戏 schema v2 字段，仅含 notes/schemaVersion 和清空的旧数组。`game-chart.before-fields.json` 保留旧 56Tap 字段；回退通过 Unity 克隆预检、SerializedObject 写回这些字段并只保存该资产，不能重建 GUID 或全局 SaveAssets。
- `apply-chart.cs.txt` 是协调后的空闲编辑器执行片段：校验源 hash、meta、现有参数，克隆预检、Undo/SerializedObject 写回，仅 SaveAssetIfDirty 目标资产；重复执行已应用谱面时不再写。它不是运行时 JSON 导入器，不自动运行。

## 数据校验

58 枚 = 56 Tap + 2 Hold；6 move、1 remove、3 add。删除 legacy-51；所有其余 ID/lane/type/timeMs/durationMs 按上传保留。legacy-46 的 `32490.000000000004` 原样保留，没有节奏规范化或 11ms 解码补偿。新增记录虽位于原数组末尾，Runtime 只在编译副本内稳定排序。

时间域保持原曲 16–56 秒，歌曲时刻 0–40 秒，chartOffsetMs=0。首音 464ms，末音 39466ms；lane2 Hold 从 33058 到 34223ms，lane0 Hold 从 35388 到 36408ms。同轨头部窗口及 Hold 尾部与下一头部窗口均无冲突。最大 +300ms 输入补偿时末音窗口截止 39906ms；音频仍预约在 40 秒停止，会话尾部按原配置再加 100ms 缓冲结算。

预备倒数沿用现有至少 3 秒预滚；首音在计划音乐起点后 464ms 到线，不额外延后谱面，不将首音变成倒数终点。

## 验证与当前状态

```powershell
node PRP/rhythm-chart-import-20261004/verify-import.mjs
& PRP/rhythm-chart-import-20261004/verify-rules.ps1
# Unity 保存后核对实际落盘资产：
node PRP/rhythm-chart-import-20261004/verify-import.mjs --after
```

导入前数据校验 PASS。纯逻辑脚本加载现有 Game.Runtime/Game.Core 编译程序集，拒绝源码晚于程序集；逆序积压批次在 -300/0/+300ms 下均为 58 Perfect、2 Hold、58000 分，两个上传 Hold 各提前 1ms 松开只记一次 Miss，共 5 个场景 PASS。没有调用编辑器 API，未影响 Laila 测试。

已在 Laila 明确交还的空闲、非 Play 编辑器，通过 Unity SerializedObject 写入正式 SO 并回读与上传 chart 完全一致。资产 SHA-256 为 `f9ed355eb7fb4317e2100e5a99941d0f71c0b901a12cad4840a02e35943b9bd2`，GUID 仍为 `ae3746d723e6a814099d7ebebffa3b45`，meta SHA-256 仍为 `5561bab9da2df87dd675d5fe5e9c8f75bf81e5c0f58e5bf913bae2f79d3b5944`；音频未改。Unity 保存时显式序列化了已有的默认四枚练习谱，不改变其行为。

RhythmView 改为按当前谱面显示数量及长按操作，不再标注“56Tap/只有单击”；完整回放按头尾事件时间交错输入，包含同刻跨轨 Tap/Hold，不因等待长按尾部漏掉其他轨道。

三份 C# lint 与编译已通过；Unity EditMode 完成 88 个用例、失败 0，Showcase 4/4 通过（skipped=0），节奏倍率 x1。回放报告 `Logs/verify/rhythm/20261004-191705/report.md` 检查点失败 0、运行时异常 0；完整歌曲 58 枚全部命中、两枚长按完成、Miss=0，并检查全漏结算、重试、校准及生命周期。导出错误场景故意产生一条 `diagnostic_save_failed`，按框架预期错误机制过滤，不能据此声称原始 Console 没有 error。

已核对完整谱面与练习长条截图；完整歌曲操作说明最后一行曾挤到校准按钮上，随后减少空行，C# lint、编译及定向完整谱面回放 1/1 通过（`Logs/verify/rhythm/20261004-192249/report.md`）。修正后截图确认说明与按钮分开。截图和自动输入不能替代音乐贴合度、声卡实际输出延迟或人工手感验收。

正式入口仍为 `Assets/_Project/Scenes/RhythmDemo.unity`，其 RhythmInstaller 使用上述正式资产。旧 Player 未重打包，不含此次谱面。
