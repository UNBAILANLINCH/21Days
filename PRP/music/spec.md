# spec.md — 画面打印机 × 节奏天国式音游

状态：待实现的技术规格，不代表已经验证了目标设备上的音频或输入精度。

## 0. 给执行 Codex 的指令

按阶段 0 → 6 实现。默认首次执行只完成阶段 0、1，交付可运行的 Rhythm Audio Core 和检查结果；后续按用户指定阶段继续。若用户明确要求执行全部阶段，则逐阶段验收后继续，不必反复请求确认。禁止提前搭建后续阶段的空框架。

开始前检查现有工程、AGENTS.md、Unity 版本、Packages/manifest.json、输入方案、渲染管线及已有音频组件。复用已有能力，不擅自升级 Unity、切换渲染管线或替换全局输入配置。有冲突先说明；仅在必须由用户决定时询问。没有 Unity 工程时，明确需要创建工程或提供工程路径，不把普通 C# 编译成功当作 Unity 验收。

每阶段交付：实际改动、运行入口、配置步骤、自动检查结果、人工验收记录、未验证项。缺少 Unity 编辑器或目标硬件时保留可运行检查和操作步骤，写明“未验证”，不得声称通过。测试从第一阶段开始；最后阶段负责扩大覆盖，不能把所有验证推迟到最后。

## 1. 产品目标与边界

玩家通过声音提示学习节奏，在预期时机按下、按住或松开按钮，驱动屏幕中的打印机完成盖印、滚印、送纸或裁切。一局结束生成一张由本局操作结果决定的图像。核心体验是听觉提示、节奏预期和动作反馈，不以音符轨道作为主要引导。

这里的“打印机”默认指虚拟 Gameplay Actor，输出为屏幕纹理及 PNG 文件；不接真实打印设备。上述玩法是本项目的设计选择，并非对某商业游戏内部实现的断言。

首个纵向原型范围：

- Windows 桌面、键盘 Space 单按钮；沿用现有 Unity 工程版本。空工程建议使用用户已安装的 Unity 6 稳定版本，记录完整版本号。
- 恒定 BPM、4/4 拍、单首 30–60 秒原创或授权曲目；没有曲目时使用生成的节拍测试音。
- 第一阶段只做 Press；第二阶段加入明确配对的 HoldStart / HoldEnd。
- Stamp、Roll、Cut 三种表现；最终一张 512×512 图像。
- 调试 HUD 可显示拍数和误差；正式游玩时可关闭，关闭后仍能依靠 Cue 完成玩法。

非目标：通用 AudioManager、FMOD/Wwise 集成、服务定位器、事件总线框架、联网排行、谱面商店、自动扒谱、变速变拍、音乐伸缩、复杂分支谱面、真实打印机驱动、移动端与 WebGL 的性能承诺。

## 2. 必须遵守的时间约定

### 2.1 DSP 是播放时间基准，Beat 是内容坐标

Unity 的 `AudioSettings.dspTime` 基于音频系统处理的采样计时；`PlayScheduled` 接受同一时间轴上的绝对时间，允许提前安排播放。它们不保证声音到达耳朵、显示到屏幕或输入到系统的整条链路无延迟。[官方 dspTime 文档](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings-dspTime.html)、[官方 PlayScheduled 文档](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.PlayScheduled.html)。

全部计算使用 double；Inspector 中单位明确标注。首拍偏移和玩家校准绝不能混用：

```text
clipStartDsp           = 播放音频 sample 0 的 DSP 时刻
firstBeatOffsetSec    = 音频 sample 0 到谱面 Beat 0 的距离，首版要求 >= 0
beatZeroDsp           = clipStartDsp + firstBeatOffsetSec
secondsPerBeat        = 60.0 / bpm
songTimeSec           = dspNow - clipStartDsp
beat                  = (dspNow - beatZeroDsp) / secondsPerBeat
BeatToDsp(b)          = beatZeroDsp + b * secondsPerBeat
DspToBeat(t)          = (t - beatZeroDsp) / secondsPerBeat
barIndex              = floor(beat / beatsPerBar)       // 内部从 0 开始
beatInBar             = beat - barIndex * beatsPerBar
```

BPM 以四分音符为单位；首版固定 beatsPerBar=4，不声称支持任意拍号。倒数期间 Beat 可以为负；HUD 把负拍显示为倒数，正式小节显示为 barIndex+1。

禁止通过 `deltaTime` 累加歌曲位置、用协程等待作为精确节拍器、用动画事件触发判定，或只在 `floor(beat)` 改变时补发一个拍点。低帧率跨越多个事件时必须逐个处理。

### 2.2 输入时间必须换算，不能直接相减

Input System 的 `CallbackContext.time` 表示触发 Action 的时间；底层输入事件时间轴与实时启动时间关联，不能假定它等于 DSP 时间。[CallbackContext 文档](https://docs.unity.cn/Packages/com.unity.inputsystem@1.19/api/UnityEngine.InputSystem.InputAction.CallbackContext.html)、[Input Events 文档](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/Events.html)。执行时核对工程所安装的包版本，不使用不存在的 API 或内部反射。

首版 InputClockBridge：开局在同一主线程调用中夹取 realtimeBefore、dspNow、realtimeAfter，取 realtime 中点；采集 16 组，选调用跨度最小的有效样本，得到 offset=dspNow-realtimeMid。若目标包文档明确要求其他公开时间源，使用它并补对应测试。映射是工程近似，精度须测量，不称为采样级输入精度。

```text
inputDspEstimated = context.time + inputToDspOffsetSec
effectiveInputDsp = inputDspEstimated - inputCalibrationSec
timingErrorSec    = effectiveInputDsp - BeatToDsp(targetBeat)
timingErrorMs     = timingErrorSec * 1000
```

误差负值为 Early，正值为 Late；正校准值表示扣除观测到的迟到偏差。例如原始晚 80ms、校准 +80ms，修正后为 0ms。歌曲 offset 修正素材起点，inputCalibration 修正玩家/设备合成偏差，visualOffset 只调整画面。禁止同时在多个模块重复补偿。

每局固定映射和校准，不逐帧改变以免判定跳动。调试时定期测量映射残差但不偷偷修正；暂停、失焦或音频配置改变后终止本局并重开，重新采样。若长局残差超限，先分析漂移再升级映射，不能靠放宽判定掩盖问题。

### 2.3 生命周期与调度

状态：Idle → Preparing → Scheduled → Playing → Completed；任意活动状态可 Stop 回 Idle，资源错误进入 Failed。

- Preparing 校验数据并等待 Clip 加载成功；失败或超时给出可理解的错误。
- 音乐预留 1 秒启动提前量，Cue 滚动前瞻 0.5 秒，均可配置。测试素材用预加载 PCM/WAV、短 Cue；这些数值是起始设置，不是全平台保证。
- 音乐与关键 Cue 的 AudioSource 固定 pitch=1、关闭 Doppler、使用 2D 音频，不允许 Mixer 改变时间语义。
- Cue 需要独立可用声源；一个已预约且尚未结束的声源不可被重新赋 Clip 或再次预约覆盖。按测试谱面最大并发加余量创建小型声源列表，不做通用对象池框架。
- Cue 调度过期时不集中补播，记录 lateCueCount；测试中关键 Cue 过期使该轮同步验收失败。视觉事件可补进度到当前时刻。
- Stop/Restart 必须停止全部本局音乐和预约 Cue、清空输入队列和事件状态；sessionId 隔离旧回调。重复 Play 不产生第二份播放。
- MVP 不支持中途续播/Seek。暂停按钮执行终止并提示“从头重试”；失焦也终止，不伪装为可恢复暂停。后续真正续播必须同时重建音乐、预约 Cue、事件状态和时钟映射。
- 收尾需等待音乐结束与最后一个判定窗口关闭，取较晚时刻；允许尾窗内输入，Completed 仅触发一次。

## 3. 目录与职责

下列是最终目标布局，不要求第一阶段创建全部文件。小型枚举、结构体可合并在同一文件；遵守现有项目目录风格。

```text
Assets/RhythmPrinter/
  Runtime/
    RhythmConductor.cs       # DSP 起点、Beat 换算、播放状态
    RhythmInput.cs           # InputAction 绑定、边沿、时间戳队列及映射
    RhythmTypes.cs           # 序列化数据及不可变运行结果
    SongData.cs              # 音频、BPM、首拍偏移、Pattern 实例
    RhythmTimeline.cs        # 展开后的事件、Cue 前瞻、声源占用
    RhythmJudge.cs           # 纯 C# 匹配、误差、状态转换
    RhythmSession.cs         # 按确定次序协调组件和重开
    PrinterActor.cs          # 盖印、滚印、裁切表现
    PrintResultBuilder.cs    # 结果记录 → 图像 → PNG
    RhythmDebugHUD.cs        # 开发 HUD、测试入口
    CalibrationController.cs # 阶段 6
  Editor/
    SongDataEditor.cs        # 阶段 5，编辑校验和轻量预览
  Tests/EditMode/
    RhythmCoreTests.cs
  Tests/PlayMode/
    RhythmSessionTests.cs
  Data/                     # 歌曲、Pattern、判定配置资产
  Audio/                    # 原创/授权音乐与 Cue
  Art/                      # 印章、纸张、材质；按需添加 Shader
  Prefabs/
  Scenes/RhythmCoreTest.unity
  Scenes/PrinterDemo.unity
Docs/                        # 运行、谱面制作、验收记录
```

依赖方向：SongData → Conductor/Timeline → Judge → 判定结果 → PrinterActor/PrintResultBuilder。表现不得反向修改音乐时间或判定。Session 负责连接，采用直接方法调用和少量 C# event；不引入 DI 容器或全局单例。

## 4. 核心数据与接口契约

以下为契约示意，不是可以原样复制编译的完整实现。持久化使用 Unity 可序列化字段与 ScriptableObject；运行中状态与资产分离，Play Mode 不改写源资产。

```csharp
enum InputEdge { Press, Release }
enum EventKind { Press, HoldStart, HoldEnd }
enum PrintAction { Stamp, Roll, Cut }
enum Grade { Perfect, Good, Miss }

// SongData : ScriptableObject
// songId, AudioClip music, double bpm, double firstBeatOffsetSec,
// int beatsPerBar=4, PatternInstance[] patterns, JudgeSettings judge

// PatternDefinition : ScriptableObject
// patternId, double lengthBeats, CueData[] cues, RhythmEvent[] events

[Serializable] struct CueData {
    public string id;
    public double beat;       // Pattern 内相对拍
    public AudioClip clip;
}
[Serializable] struct RhythmEvent {
    public string id;
    public double targetBeat; // Pattern 内相对拍
    public EventKind kind;
    public string holdId;     // 同一实例内起止配对，普通 Press 为空
    public PrintAction action;
    public int printSlot;     // 对应结果图的确定位置
}
[Serializable] struct PatternInstance {
    public string instanceId;
    public PatternDefinition pattern;
    public double startBeat;
}
```

运行时 key=instanceId/eventId，绝对拍=startBeat+相对拍。Cue 和目标分别排序，不能假定两者顺序一致；同拍用稳定 key 排序。每条 Cue 可提示多个目标，不强制一个目标对应一个声音。首版不支持同一按钮同一时刻的多个目标。

运行记录至少包含 sessionId、eventKey、kind、targetDsp、rawInputDsp?、effectiveInputDsp?、errorMs?、grade、reason。自动 Miss 的 input/error 为 null，不能伪造“误差 0”；reason 区分 Timeout、EarlyRelease、MissingHoldStart。保存本局校准值和谱面版本，供结果复现。

```csharp
// RhythmConductor
void Play(SongData song);
void Stop();
double BeatToDsp(double beat);
double DspToBeat(double dsp);
// 只读 State、SongTimeSec、Beat、BarIndex、BeatInBar

// RhythmInput：回调时立即复制必要字段，不持有 CallbackContext
// InputSample = sessionId, edge, rawTimestamp, estimatedDsp

// RhythmJudge：纯逻辑，不读 Unity 时钟、不播放音频
void Reset(CompiledEvent[] events, JudgeSettings settings);
void ProcessInput(InputSample sample, double calibrationSec);
void AdvanceTo(double effectiveNowDsp);
// 输出 Judgement；无匹配输入记录 Stray，不额外制造谱面 Miss

// PrinterActor / PrintResultBuilder
void ApplyJudgement(Judgement result);
// Builder 使用记录的事件及结果，生成 Texture2D；导出是单独用户操作
```

### 4.1 判定与输入顺序

初始 Perfect ≤35ms，Good ≤80ms；超过 80ms 不匹配。数值是可调的设计假设，不是某商业游戏真实窗口。Inspector 校验 0<Perfect≤Good；窗口含边界，自动 Miss 在严格超过 Good 后生成。

每帧先收集本次 Input System 更新中的输入，按原始时间戳稳定排序；逐个推进 Judge 到该输入的有效时刻，再匹配它；最后推进到当前 DSP 减校准值。确保该流程在 Dynamic Input 更新之后执行。不得在处理同帧积压输入之前按“现在”批量判 Miss，否则卡顿时会误杀有效输入。若收到早于已推进水位的输入，记录 lateInputDelivery，丢弃并标记该轮诊断异常，不倒转状态。首版不做回滚判定。

匹配规则：筛选未消费、边沿类型一致、Hold 前置条件满足且位于 ±Good 内的目标，取绝对误差最小者；相同误差取目标拍更早者，再按稳定 key。每个输入最多消费一个目标，每个目标最多产出一个结果。Stray 不直接计分，单独统计。

首版谱面校验要求同类可竞争目标的窗口不重叠（目标距离 >2×Good 秒）；这是为避免单键原型抢判的设计限制。未来需要密集音符时，先定义产品规则再放开。

### 4.2 按住语义

不要依赖 Input System 的 Hold interaction 计时替代谱面判定。采集按钮原始 Press/Release 边沿，关闭额外 Tap/Hold interaction；实现时核对 Button action 的 press/release 阈值与回调语义，过滤重复 Press。

- HoldStart 命中才激活对应 holdId；成功结果一次。
- Release 位于 HoldEnd ±Good 内：结算终点；早于下界：立即给该 HoldEnd 一次 EarlyRelease Miss 并结束 Hold。
- 未释放且超过上界：HoldEnd Timeout Miss，解除活动 Hold。
- HoldStart 超时：Start 和配对 End 分别记一次 Miss，End 原因为 MissingHoldStart；后续不重复结算。
- 无活动 Hold 的 Release 为 Stray；首版禁用嵌套/重叠 Hold，Hold 内不放其他按钮目标。
- 计分按起点、终点两个目标统计；Roll 的完整成功要求两端均非 Miss。中途早放后的重新按下不能恢复本次 Roll。

## 5. 分阶段实施

### 阶段 0：工程盘点与可重复测试素材

目标：知道工程实际可用的基础能力，建立一个干净测试入口。

非目标：业务框架、正式美术、自定义编辑器。

步骤：

1. 记录 Unity、输入包、渲染管线、目标平台、已有音频/输入代码及可复用组件。没有 Input System 时评估并添加与工程兼容的官方包，说明项目设置影响。
2. 建立 RhythmCoreTest 场景，配置单个 AudioListener、Music AudioSource、最小启动按钮与 HUD。
3. 创建恒定 120 BPM、4/4、60 秒的测试音，已知每 0.5 秒的脉冲位置；保留生成方法或来源及导入设置。测试音用于时间基准，不冒充正式音乐。
4. 建立最小测试文件和执行说明。已有 Unity Test Framework 则复用；没有时添加兼容官方测试包，不引入其他测试框架。

验收：场景可打开，无新增编译错误；音频资源可加载；版本与入口有记录。

风险与约束：资源授权未明确时只用合成音；不要通过修改全项目 AudioManager 设置解决局部问题。

### 阶段 1：Rhythm Audio Core，首要交付

目标：单首测试音可准确预约播放，输出 Beat/Bar，能记录 Space 相对已知目标的 timing error。

非目标：Pattern 资产、正式 Judge、打印机、美术、复杂混音。

步骤：

1. 实现 SongData 最小字段、Conductor 和第 2 节生命周期。所有时间转换来自唯一公式。
2. 使用 PlayScheduled 播放测试音；开始前确保载入成功。第一阶段脉冲可预先烘焙在测试 Clip 内，无需先造 Cue 系统。
3. 实现 InputClockBridge 和 Space Press 时间戳。调试目标固定为 Beat 4、6、8……；临时用最近测试目标显示误差，不把它当作最终消费规则。
4. HUD 显示 State、SongTime、Beat、Bar、映射残差、原始/修正误差、Early/Late。默认 inputCalibrationSec=0，但保留调参入口与单位。
5. 实现 Stop、Restart、资源错误、失焦终止；把时钟数学提取成可传入数值的纯函数。
6. 编写公式、首拍偏移、校准符号、重开清理检查；保存测量记录。

验收：

- 120 BPM 时 Beat 4 相对 Beat 0 为 2 秒；Beat 8 为 4 秒；Beat 4 的 barIndex=1、beatInBar=0。加入 0.25 秒首拍偏移后所有目标相应后移。
- 人工构造输入 target−0.020s → −20ms；target+0.050s → +50ms；原始 +80ms 配 +80ms 校准 → 0ms，数值误差 <0.001ms。
- 数学往返换算误差 <1e−6 秒；这是软件公式验收，不等同于硬件精度。
- 30/60/144 FPS 设置下同一时间戳输入得到同一误差；连续重开 20 次不叠音、不保留上一局时间和输入。
- 60 秒测试中记录桥接残差的 median/P95/max；目标 P95≤5ms。超出则标记失败并定位，不宣称跨设备保证。尚无回环设备时音频物理误差写“未测”。

风险与约束：不能把回调到达时的 dspTime 当作实际按键时刻；不能以手动敲击的散布来证明系统本身抖动。

### 阶段 2：RhythmEvent、Cue、Pattern、Judge

目标：一个包含学习提示和操作目标的 30–60 秒单键关卡，支持 Press 和配对 Hold。

非目标：轨道谱面编辑器、变 BPM、多按键和复杂连招。

步骤：

1. 建立 PatternDefinition、PatternInstance，编译为独立 Cue 表和目标表；检查 key、时间范围、Hold 配对和竞争窗口。
2. 制作 Stamp 模式示例：cue 位于相对 Beat 0、1，Press 位于 Beat 2；Roll：Cue 0，HoldStart 2、HoldEnd 4；Cut：Cue 0，Press 2。先逐个教学，再组合，节奏可听辨。
3. Timeline 用 DSP 前瞻预约 Cue，记录声源占用结束时间；按最大并发配置容量。声源不足明确报错并计数，不抢占已安排 Cue。
4. 实现第 4 节 Judge 匹配、单次消费、自动 Miss 和 Hold 状态；Session 按确定顺序推进。
5. 输出结果列表和简单统计，隐藏 HUD 后进行听觉试玩。第一版分数为 Perfect=2、Good=1、Miss=0，归一化到满分；Stray 单独展示。

验收：

- ±35ms 为 Perfect，±80ms 为 Good，越界不消费；无输入在窗末之后自动 Miss。
- 重复输入不重复得分，错误边沿不消费目标；Hold 的早放、晚放、未按、未放都能结束且不重复结果。
- 模拟一帧跨越多个目标，所有超时目标恰好结算一次；积压但时间戳有效的输入不会因当前帧晚到而错误 Miss。
- Pattern 重复两次时 key 不冲突；重开不残留预约 Cue；正常测试 lateCueCount=0。
- 200ms 主线程卡顿在前瞻范围内时，已预约 Cue 不因主线程未 Update 而被主动改时；超过前瞻的卡顿记录失败，不补播成一串。

风险与约束：主线程调度预算、音频解码和声源并发可能影响实际播放；Cue 与 BGM 都不得依赖逐帧 PlayOneShot 的调用时刻取得精确同步。

### 阶段 3：打印机 Gameplay Actor 与打印结果

目标：判定能改变机器动作和最终图像，打一局即可看到独有结果。

非目标：复杂物理纸张、真实墨水模拟、可编辑绘画应用。

步骤：

1. 用基础 Sprite/Transform 搭出纸张、打印头和裁切部件；PrinterActor 只接收 Cue/结果及只读歌曲时间。
2. Stamp 命中盖章；Roll 起点成功后展示滚印过程，终点结算长度/质量；Cut 命中完成预设区域裁切表现。失败可表现卡顿、漏印或偏移。
3. PrintResultBuilder 从结果记录按目标拍、key 的稳定顺序合成，而不是按动画完成顺序画图。保持唯一事件消费表，防止重复印章。
4. 首版用 CPU Texture2D 合成 512×512 RGBA；使用预制小印章和规则图案。先完整重建再考虑增量/RenderTexture，不为小图引入计算 Shader。
5. 固定映射：Perfect 使用预设位置与满墨；Good 的横向偏移 clamp(errorMs/80,−1,1)×8 像素、墨量 0.8；Miss 留白并增加对应失败标记。Roll 完整成功打印预定条带，否则根据已记录失败时刻截断；Cut 失败保留边框。所有映射参数集中在一个打印配置中。
6. 结束页显示图像、判定数和重试/导出按钮。PNG 导出到 Application.persistentDataPath 下的 Results 目录，自动生成安全文件名、不覆盖旧结果，处理权限/磁盘错误。

验收：相同谱面与结果记录在同一实现版本下生成相同像素；不同帧率不会改变图像；全 Perfect/全 Miss/早放 Hold 三种输入产生符合规则的不同图像；PNG 能重新解码且尺寸正确；失败导出不丢失屏幕结果。

风险与约束：若加入随机污点必须固定并记录种子；纹理创建与销毁需成对管理，连续重开不累积 Texture2D。GPU 跨平台逐像素一致不在首版承诺内。

### 阶段 4：动画、Shader、SFX

目标：完善打印触感和节奏可读性，保持判定独立。

非目标：电影级渲染、全套粒子系统、重写渲染管线。

步骤：

1. 已知的预备动作根据 DSP/Beat 计算归一化进度，直接设置 Transform/Animator 表现。卡帧后跳到当前进度，不能靠逐帧积分永久落后。
2. 玩家命中动作收到结果后立即反馈；不能预约一个尚未发生的成功，也不能为“更同步”延迟输入判定。
3. 先用 Sprite 遮罩、颜色和材质属性做出纸、墨量变化；只有无法表达目标效果时添加一个适配现有管线的纸张显影 Shader。保留关闭特效的基础路径。
4. 音频分 Music/Cue/Feedback 三组，使用 AudioMixer 控制音量。关键 Cue 继续预约；玩家反馈 SFX 可即时播放，不宣称它采样级对齐。
5. 视觉 offset 只影响预备动作/进度显示；提供 Cue 音量和减少晃动选项，避免闪烁成为唯一提示。

验收：关掉动画/Shader/反馈 SFX 后，同一注入输入序列的判定完全相同；30/60/144 FPS 下关键视觉动作偏差原则上不超过一帧加显示链路延迟，实测记录；无额外 Shader 编译错误、连续重开无音频源泄漏。

风险与约束：录屏只能提供有限的视听同步证据；不可把高帧率显示测试替代音频链路测量。反馈 SFX 必须避免掩盖下一条 Cue。

### 阶段 5：数据驱动与轻量编辑器

目标：制作第二段关卡无需修改运行时代码。

非目标：完整 DAW、波形编辑、谱面格式生态、运行时热更新。

步骤：

1. 完善 SongData/Pattern Inspector；先提供排序、验证、定位错误、重复 Pattern 实例的基础操作。
2. 校验 BPM/偏移为有限有效数值、资源非空、key 唯一、Cue 与目标非负、Pattern 长度覆盖所有子项、Hold 成对有序、printSlot 合法、同按钮窗口不竞争。
3. 目标中心必须位于歌曲有效范围内；最后判定窗口可延伸到尾音之后。Cue 与 Pattern 关系可给可读警告，但不强行规定每个目标必须有一条 Cue。
4. 提供拍网格列表、1/4 拍吸附和选中事件信息；需要试听时进入测试场景从头播放，首版不偷偷引入任意位置 Seek。
5. 校验复用同一纯逻辑入口，编辑器和运行时加载都调用；编辑器修改支持 Undo，不自动覆盖作者数据。
6. 写一页制作说明：BPM、首拍偏移、Cue、目标、Hold、结果槽位和测试流程。第二首/第二段数据只新增资产。

验收：一份无效数据能定位具体资产与 eventKey，并阻止开局；重启编辑器后数据不变；第二个关卡无需增加 gameplay 分支；构建不引用 UnityEditor。

风险与约束：数据驱动从阶段 1 的 SongData 和阶段 2 的 Pattern 开始，本阶段是完善工具，不能在前期把谱面写死再整体重写。

### 阶段 6：延迟校准、回归测试和最终验收

目标：可以解释、测量并复现偏差，在目标桌面设备上稳定完成一局。

非目标：宣称自动测得真实硬件延迟、保证蓝牙与所有平台同等精度。

步骤：

1. 制作校准流程：120 BPM，先 8 拍适应，再采集 32 次点击；每拍最多匹配一个输入，超过 ±250ms 排除并计数。有效输入少于 24 次时不覆盖旧值。
2. 基于未扣除旧校准值的原始误差取中位数作为候选 inputCalibrationSec；记录 MAD、有效数和异常数。MAD>30ms 时提示样本不稳定并建议重测，不强制保存。
3. 显示“观测到的综合偏差”，允许试听、手动微调、保存与恢复 0。保存范围 ±250ms，超出时先检查素材 offset 和设备配置，不静默截断为有效结果。
4. 音频节拍点击得到的偏差混有输出延迟、输入延迟和人的预判偏差，不能分别辨识这些量。视觉偏移独立手动调整，不反写 inputCalibration。
5. 本地保存带版本号的设置；切换设备、蓝牙状态或音频配置时提醒重新校准。只有检测到配置变化才触发提示，不把无法可靠检测的物理设备身份当事实。
6. 补齐下表回归与实机构建记录；运行 Profiler 检查热路径分配和资源释放。

验收矩阵：

| 检查 | 方法 | 通过条件 |
| --- | --- | --- |
| 时间换算 | EditMode，多个 BPM/offset/负倒数 | 往返误差 <1e−6 秒；非法 NaN/Infinity 被拒绝 |
| 窗口边界 | 注入 0、±35、±80、±80.1ms | 分级和消费规则严格符合规格 |
| 消费/Hold | 重复键、空按、缺起点、早放、未松手 | 无重复结果、无卡住状态 |
| 卡帧顺序 | 用旧时间戳积压输入模拟 200ms 卡顿 | 有效输入先于当前时刻 Timeout 处理 |
| 校准符号 | 原始 +80ms，候选 +80ms | 修正为 0；重复校准不叠加旧值 |
| 校准有效性 | 少量样本/高 MAD/超范围 | 保留旧设置，给出明确失败原因 |
| 重开与中断 | PlayMode 重开20次、失焦、设备变化 | 旧预约声音和输入被清理，无错误续播 |
| 数据验证 | 故意损坏 key、Hold、Clip、时间 | 开局前拦截并定位 |
| 图像 | 相同记录重建、导出后重读 | 同版本像素一致、PNG有效 |
| 构建 | Windows 独立构建运行完整关卡 | 无 Editor 依赖、无未处理异常 |
| 音频调度 | 有条件时使用物理/虚拟回环录音测脉冲 | 记录偏差和抖动，不以 HUD 时间代替声学测量 |
| 性能 | 目标设备 60 秒正常运行，30/60/144 FPS | 不累积漂移；正常 lateCue=0；核心稳态无逐帧托管分配 |

实机同步目标：在同一音频配置下扣除固定偏差后，回环脉冲残差 P95≤10ms；若未提供回环录音条件，本条为“未验证”，不得写“已达到”。该阈值是原型工程目标，不是 Unity 官方保证。人工敲击数据单独列出，不能拿人的操作波动推断软件误差。

风险与约束：校准能修正稳定偏移，不能消除随机抖动；设备延迟可能随配置改变。InputSystem、DSP、操作系统和音频驱动之间的关系必须在最终目标版本上验证。

## 6. 最终完成定义

1. 能启动、听提示、按下/按住/松开、得到判定、看到机器动作、生成并导出结果图，再完整重开。
2. Debug HUD 关闭后仍可游玩；除教学与必要状态提示外，不依赖音符轨道。
3. 音乐位置、Cue、输入判定使用明确且可检查的时间关系，动画不控制判定。
4. 核心检查通过；人工、硬件和构建验证各自记录，未测部分显式标注。
5. 另一段关卡通过数据资产制作；不引入庞大 AudioManager 或无当前用途的抽象。

## 7. 交付给下一轮 Codex 的执行提示

> 阅读本 spec.md 及工程内 AGENTS.md。先盘点现有 Unity、音频与输入代码并复用。当前只执行阶段 0、1：实现基于 dspTime/PlayScheduled 的 Rhythm Audio Core、BPM/Beat/Bar、首拍偏移、带明确时钟换算的输入 timing error、Stop/Restart 与对应检查。不要提前实现通用 AudioManager、Pattern 编辑器或打印机美术。完成后提供场景入口、配置方法、检查证据和未验证项；缺少编辑器或设备时如实说明，不把静态代码检查当实机通过。

## 8. 依据与尚待检验的假设

官方 API 行为参考第 2 节链接；实施时以已安装 Unity/Input System 对应版本为准。原对话提供的是设计背景，不作为延迟、性能或商业游戏内部实现的证据。

本规格中的 35/80ms 窗口、0.5 秒前瞻、1 秒启动提前量、512×512 结果图、5/10ms 工程阈值及校准样本数均为原型设计选择。是否好玩、窗口是否公平、声音提示是否足够清晰，目前证据不足，需要试玩和设备测量；若这些结果不支持当前设置，应修改参数和验收记录，而非把假设写成结论。
