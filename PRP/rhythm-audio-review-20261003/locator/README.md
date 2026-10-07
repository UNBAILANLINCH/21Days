# 虫儿飞试听定位工具

独立 B 任务交付，范围仅本目录；不改原 A/B 研究、Runtime、正式谱面、共享 spec/HANDOVER。现有静态 WAV 页无法按 ID 寻址或导出，因此在子目录增加无依赖 Node + WebAudio 工具。

## 运行

推荐双击本目录 `launch.cmd`。它从脚本自身目录启动，不依赖调用方工作目录；检查现有 Node，打开浏览器并在可见窗口显示 READY、PID、来源 hash。**请保持服务窗口打开**，关闭窗口或 Ctrl+C 会停止服务。没有系统自启动、后台持久服务或防火墙修改。

重复启动会核对 `/health` 的工具标识与 `/source` 的谱面/音频 hash，确认是同一工具才复用；其他端口占用会明确报错，不强杀进程。可执行 `launch.cmd 8767` 改用 8767。Node 不在 PATH 时尝试标准 Program Files/nodejs；仍缺失则显示错误并停留，不安装依赖。

在项目根目录执行：

```powershell
node PRP/rhythm-audio-review-20261003/locator/server.mjs
```

浏览器打开 **http://127.0.0.1:8766**，点击“加载本地原谱与音频”。服务器仅绑定本机；Ctrl+C 停止。端口可作为第二参数，例如 `server.mjs 8767`。不要双击 HTML：页面需要同源读取 `/source` 和 `/audio`。无 npm install、Unity 操作、Git index 操作或上传。

可访问性修复检查（2026-10-04）：修复前实测本机 8766 无监听、HTTP 连接被拒绝；没有旧服务退出日志，无法断言具体退出原因。此前的工具会话启动只能证明当时可访问，未证明会话结束后继续存活。新入口以独立可见进程启动，启动命令返回后另一次 HTTP/进程检查确认服务仍存在；`/health` 提供 PID/启动时间以便核对。

启动器验证：`node PRP/rhythm-audio-review-20261003/locator/verify-launch.mjs`（服务器需已启动）。验证项目外 cwd、重复启动复用同一 PID、非法端口拒绝，以及由测试自身创建的无关端口服务在冲突报错后仍可访问。2026-10-04 修复后本机 Chrome 核心 E2E 再次 PASS，HTML/source/audio 全部 HTTP 200，浏览器测试结束后独立 health 请求仍返回同一服务 PID。

只读来源：`Assets/_Project/Data/Rhythm/ChongErFei.asset`（当前 canonical schema v2 SO）及 `Assets/_Project/Audio/Rhythm/ChongErFei.mp3`。当前无 canonical JSON，服务器只解析当前已知 SO 字段；非空 BPM 段或未知音符字段会拒绝。每次 `/source` 重新读取磁盘及计算原谱/音频 SHA-256，导出和回载时再次核对。没有任何写盘 HTTP 接口，只开放工具文件和这两个来源；POST 返回 405。

## 使用

1. 中段按钮显示片段 18.9–21.5s（原曲 34.9–37.5s），选择 legacy-26；可改短循环区间，拖动位置滑杆或点击时间轴寻址。
2. A 是原谱，B 是临时候选；音频始终是同一原 MP3。勾选音符 click 后，A/B 会使用各自时间；可添加手动 BPM 与节拍原点参考 click。对照时保持相同区间、click 设置和输出设备。
3. 选择 noteID，数字修改候选 timeMs；B 模式可拖动橙色音符，松开后校验。只支持时间调整，不增删音符，不改 lane、类型、Hold 时长或全局 offset。非法范围、同轨 Good 窗/Hold 占用冲突拒绝更新。
4. **明确选择跟旋律还是伴奏**，填写对应声音、接受/拒绝依据，再导出。自动分析不参与此工具，不会宣布“已贴拍”。
5. 下载候选 JSON 和差异 CSV；浏览器可能询问允许多文件下载，以 JSON 内的 `differences` 为完整差异依据。可从页面选择刚下载的 JSON 回载核对。刷新/重新加载/还原全部会丢弃未导出的候选。

## 时间与候选契约

原曲秒 = `16 + (timeMs + chartOffsetMs)/1000`；片段是 0–40s，对应原曲 16–56s。波形来自项目 MP3 的浏览器解码结果。A/B 标尺共同显示原谱与候选的 noteID/lane/time；click 以实际目标时间（含 chartOffset）安排。拖动编辑量化到整数毫秒，原谱已有浮点时间保持原值。

JSON `format=21days-rhythm-review`、`version=1`、`status=unapproved-candidate`。它是审核工具候选契约，**不是正式 Runtime 导入格式**。包含来源路径/原文件 SHA-256、时间域 `decoded-project-mp3`、解码补偿 0、声部、人工说明、完整候选 chart 与 ID 差异。回读严格校验版本、身份、来源 hash、字段、时间、轨道、ID 唯一性、Tap/Hold、40s 边界、同轨头部 Good 窗及 Hold 占用；保持原 ID 顺序和所有非时间字段，差异必须与 chart 重算一致。导出前 JSON stringify/parse 后再验一遍。未知版本/时间域或来源变化均拒绝。

安全手工导入：先人工接受声部与逐音符变化，再核对当前 SO 的 SHA-256 等于候选 source.chartSha256，逐 ID 应用差异并执行项目正式谱面/Unity 回归。页面不会覆盖 SO，也不表示已获得导入批准。

## 调度和证据边界

音频与 click 使用同一 AudioContext.currentTime，提前 80ms 启动；音频循环由 AudioBufferSource 的 sample 循环实现，click 每 25ms 检查并提前 200ms 预约。寻址、切换 A/B、修改 click 或临时候选会取消旧节点再预约。错过 click 调度、隐藏/失焦会暂停，避免补发迟到 click。循环起止含开始不含结束，最短 50ms。画面由 requestAnimationFrame 更新，波形峰值按视区缓存。

这能验证内部时间映射和调度行为，不能证明声学输出的毫秒精度；输出设备/蓝牙延迟、后台限流、浏览器重采样与 MP3 decoder 原点均可能影响听感。历史 Unity/Windows 11ms 差异不自动套用。未做物理回环、人耳贴拍验收、Unity PCM 对齐校准或其他浏览器兼容性验证。需要用户选择旋律/伴奏并人工 AB 接受。

## 实际验证（2026-10-04）

使用现有 Node 24.13.1、全局 Playwright 和已安装 Chrome headless，未安装依赖。测试不是静态截图：浏览器同源获取 MP3 返回 200，解码 163.008s/48000Hz，表格 56 音符，实际预约播放原谱/候选与两种 click；0.6s 短循环 1.5s 后仍在区间，滑杆寻址显示片段 20.100s / 原曲 36.100s；B 拖动 legacy-27 到 20340ms，再数字改到 20300ms（原 20260ms，+40ms）。下载 JSON/CSV 后重新解析验证单条差异，页面还原后回载成功。重复 ID、lane=4、NaN、Tap 非零时长、越界、同轨冲突、错误版本/hash/差异清单均被拒绝；页面 JS 错误为 0。截图与验证候选在系统临时目录，不属于正式谱面。

复跑（需运行服务器，并提供本机已有 Playwright 包路径）：

```powershell
node PRP/rhythm-audio-review-20261003/locator/verify.mjs <现有playwright包路径>
```

原谱 SHA-256：`8d418063eaceea8c037ce382dbbab529f80ac9ce616e6a60b6f5ce1f897fb1ee`。

音频 SHA-256：`f5c98e150d1684b8308da39ee3384cf72b8f8e77c4f0028fe58438292f2362c4`。测试前后均未变化。

项目 hooks 信任状态在本会话无法确认，未将其作为已启用验证。无 C# 修改，未运行 C# lint/Unity。手动执行 `gc_scan.py`：失败，报告本目录外的 Gameplay/Game.LailaFace 命名空间 3 处、并行 RhythmDiagnosticTests.cs 缺 meta、动态字体 30871KB；未修改这些文件。最终复跑记录见工具任务交付。
