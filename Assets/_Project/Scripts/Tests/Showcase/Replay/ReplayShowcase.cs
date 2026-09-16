// ReplayShowcase —— 回放系统的回放验证场景：在真实运行环境（进 Play、跑完整启动流程、用容器里那一套
//   录制器 / 播放器 / 推进器）里，把「录一段 → 存盘 → 重放 → 查漂移 → 漂移后从快照续跑」跑成一条
//   可重复、给人看的闭环，每一步都把关键数字摆出来。
//
// 为什么不建 .unity 验证场景：本期刻意零场景改动（ScenePath 返回 null，世界在代码里搭）。
//   同一工作区还有另一个会话在活动，场景文件是最容易冲突、且冲突后只能手工重做的东西。
//   代码搭世界对这个模块没有损失：要验的是逻辑与字节，不是美术摆位。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：Showcase/SelfTest 那条用例验的是回放**框架**（停顿、叠加层、截图、报告），
//      刻意不依赖任何 Core 能力；把回放系统塞进去，第一次红就分不清是框架坏了还是回放坏了。
//   2. 扩展不行：EditMode 测试够不着「真实容器 + 真实启动流程 + 每帧驱动的播放器」这三样，
//      而这三样正是之前 execute_code 一次性验证没覆盖、也最容易出问题的部分。
//   3. 于是只能新建，并按 module-verify.md 的固定写法落在 Showcase/<Module>/<Module>Showcase.cs。
//
// 【关于输入】录制这一段用的是脚本输入源（轴 + 按钮按固定节律变化），不是真实设备：
//   自动化跑起来时没人按键，全零输入等于没验「输入录得下、放得回」。脚本输入必须经
//   InputSourceSwitch.SwitchToReplay 挂上去，而那会让真实的 ReplayRecordDriver 按设计让位
//   （放录像时不录），所以录制这一段由本文件自己的 DemoRecordStep 喂给录制器。
//   真实接线件没闲着：第 1 步会断言它确实挂在推进器上，第 3 步会断言它在这段期间**正确地**
//   一个 tick 都没重复录——那正是它该有的行为。

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Core.Config;
using Game.Core.Replay;
using Game.Core.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Replay
{
    /// <summary>
    /// 回放系统的 Showcase。一条 <see cref="UnityTestAttribute"/> 走完「录 → 放 → 查漂移」，
    /// 中间的检查点全部对着**看得见的数字**：录了多少 tick、文件多大、校验了几个哈希点、
    /// 首次漂移在哪一 tick、从快照拉回几次。
    /// </summary>
    [Category("Showcase")]
    public sealed class ReplayShowcase : ShowcaseScenario
    {
        /// <summary>容器里那个根作用域的类型名。本程序集没引用 VContainer，只能按名字找，理由见 <see cref="TryConnectContainer"/>。</summary>
        private const string ScopeTypeName = "Game.Core.Boot.GameLifetimeScope, Game.Core";

        /// <summary>等启动流程跑到「回放接线件已挂上」的最长秒数。</summary>
        private const float BootReadyTimeoutSeconds = 30f;

        /// <summary>重放倍速。4 倍是「人还看得清、又不用干等」的折中（播放器上限 8）。</summary>
        private const float ReplaySpeed = 4f;

        /// <summary>等一次重放跑完的最长秒数。900 tick 在 4 倍速下约 4 秒，这里留足余量。</summary>
        private const float ReplayTimeoutSeconds = 60f;

        /// <summary>录制长度的下限：太短就跨不过一个完整快照间隔，验不了「漂移后从快照续跑」。</summary>
        private const int MinRecordTicks = 300;

        /// <summary>演示方块的贴图边长（像素）与世界边长（单位）。纯色，4×4 够用。</summary>
        private const int MarkerTextureSize = 4;
        private const float MarkerWorldSize = 0.35f;

        /// <summary>非玩家实体的配色。玩家（0 号）用白色，好一眼认出来。</summary>
        private static readonly Color[] MarkerColors =
        {
            new Color(0.35f, 0.72f, 0.95f),
            new Color(0.95f, 0.63f, 0.30f),
            new Color(0.55f, 0.85f, 0.45f),
            new Color(0.85f, 0.45f, 0.75f),
            new Color(0.90f, 0.85f, 0.35f),
        };

        // --- 容器（反射持有，理由见 TryConnectContainer） ---
        private object container;
        private MethodInfo resolveMethod;
        private string containerError = "还没连上容器";

        // --- 从真实容器里取到的那一套 ---
        private SimulationRunner runner;
        private ReplayRecorder recorder;
        private ReplayPlayer player;
        private ReplayRecordDriver recordDriver;
        private RandomService random;
        private InputSourceSwitch inputSwitch;
        private IConfigService configService;

        // --- 本次回放自己搭的东西 ---
        private DemoWorld world;
        private DemoStateProvider provider;
        private ScriptedInputSource scriptedInput;
        private DemoRecordStep recordStep;
        private EndStateLatch endStateLatch;

        // --- 过程中攒下来给检查点看的数字 ---
        private int recordTicks;
        private int hashIntervalTicks;
        private int snapshotIntervalTicks;
        private long skippedByDriverBefore;
        private long skippedByDriverAfter;
        private string savedPath;
        private long savedBytes;
        private byte[] recordEndState;
        private byte[] replayEndState;
        private bool loadOk;
        private string loadError;

        /// <summary>
        /// 进本条 Showcase 之前播放器原本的倍速，收尾时要还原回去。负数表示还没存过。
        /// <para>
        /// 和状态提供者是同一类问题：本条 Showcase 为了跑得快把倍速设成 <see cref="ReplaySpeed"/>，
        /// 不还原的话「跑完验证接着手动开回放窗口」拿到的默认倍速就是它，一直持续到下次域重载。
        /// 存的是**运行时原本的值**而不是写死 1f——写死初值正是这次要修掉的毛病。
        /// </para>
        /// </summary>
        private float speedBeforeReplay = -1f;

        /// <inheritdoc />
        protected override string Module
        {
            get { return "Replay"; }
        }

        /// <summary>不建验证场景：世界在代码里搭（理由见文件头）。</summary>
        protected override string ScenePath
        {
            get { return null; }
        }

        /// <summary>必须加载 Boot：这条回放要验的就是「真实启动流程跑完之后容器里那一套能用」。</summary>
        protected override bool LoadBootScene
        {
            get { return true; }
        }

        /// <summary>
        /// 等启动串行跑到回放那一段。判据是「录制接线件已经挂进推进器」——它在 InitializeAsync 里挂，
        /// 挂上了就说明容器建好了、启动队列也走到了回放。录制总开关关着时接线件不会挂，
        /// 那就直接放行，让第 1 步的检查点把这件事明明白白记进报告。
        /// </summary>
        protected override IEnumerator WaitForBootReady()
        {
            float deadline = Time.realtimeSinceStartup + BootReadyTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                ReplayRecorder currentRecorder = Resolve<ReplayRecorder>();
                ReplayRecordDriver currentDriver = Resolve<ReplayRecordDriver>();
                if (currentRecorder != null && currentDriver != null
                    && (currentDriver.IsAttached || !currentRecorder.Enabled))
                {
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning($"{ShowcaseOptions.Prefix}[{Module}] 等了 {BootReadyTimeoutSeconds} 秒也没等到"
                             + $"回放接线件挂进推进器（{containerError ?? "容器已连上，但接线件没挂"}）。"
                             + "后面的检查点会把具体缺了什么记进报告。");
        }

        [UnityTest]
        public IEnumerator RecordReplayDetectDrift_FullLoopWorks()
        {
            yield return Step("接上正在运行的容器，取出录制器与播放器", ConnectServices);

            // 这条是前一个任务留下的缺口：编辑器回放窗口靠
            // FindObjectOfType<GameLifetimeScope>() → Container.TryResolve(out ReplayPlayer) 拿播放器，
            // 那条解析路径一直没在真实容器里验过。Showcase 够不着 Game.Editor 的窗口类，
            // 所以这里验的是**解析路径本身**：窗口用的 TryResolve<T> 只是
            // IObjectResolver.TryResolve(Type, out object, object) 的泛型外壳，本用例反射调的就是它。
            yield return Check(DescribeResolve(), () => player != null && recorder != null);
            yield return Check(
                DescribeCoreResolve(),
                () => runner != null && random != null && inputSwitch != null && configService != null);
            yield return Check(
                DescribeDriver(),
                () => recorder != null && recorder.Enabled && recordDriver != null && recordDriver.IsAttached);
            yield return Check(
                DescribeIntervals(),
                () => hashIntervalTicks > 0 && snapshotIntervalTicks > 0);

            yield return Step("搭最小演示世界，挂进确定性内核与状态注册表", SetUpDemoWorld);
            yield return Check(
                DescribeProvider(),
                () => provider != null && provider.Count == 1 && provider.Capture().Length > 0);

            yield return Step(
                $"录一段：脚本输入（轴 + 按钮）驱动 {recordTicks} 个 tick，逐 tick 记输入、哈希与快照",
                RecordSession);
            yield return Check(
                DescribeRecorded(),
                () => recorder != null
                      && recorder.BufferedInputCount == recordTicks
                      && recorder.BufferedStateHashCount >= ExpectedHashRecords()
                      && recorder.BufferedSnapshotCount >= ExpectedSnapshotRecords());
            yield return Check(
                DescribeDriverStandDown(),
                () => recordTicks > 0 && skippedByDriverAfter - skippedByDriverBefore == recordTicks);

            yield return Step("把这段现场存成回放文件", SaveReplayFile);
            yield return Check(
                DescribeSavedFile(),
                () => !string.IsNullOrEmpty(savedPath) && File.Exists(savedPath) && savedBytes > 0L);

            yield return Step($"载入这份文件并按 {ReplaySpeed} 倍速重放", LoadAndPlay);
            yield return Check(
                DescribeLoaded(),
                () => loadOk && player != null && player.IsActive && player.InputCount == recordTicks);

            yield return WaitUntil("重放推进过半", () => player.Progress >= 0.5f, ReplayTimeoutSeconds);
            yield return Step(DescribeProgress());
            yield return Snapshot("重放中");
            yield return WaitUntil("重放跑到终点", () => player.IsFinished, ReplayTimeoutSeconds);

            yield return Check(
                DescribeCleanReplay(),
                () => player != null && !player.HasDrifted && player.DriftCount == 0);
            yield return Check(
                DescribeCheckedPoints(),
                () => player != null && player.CheckedHashCount > 0);
            yield return Check(
                DescribeEndStates(),
                () => BytesEqual(recordEndState, TakeReplayEndState()));

            yield return Step(
                $"注入一个不确定来源：逻辑里读墙上时间，从第 {DemoWorld.DriftFirstTick} 个 tick 起生效",
                () => world.EnableDriftSource(true));
            yield return Step("拿同一份文件再放一遍，看漂移报不报得出来", LoadAndPlay);
            yield return WaitUntil("带着不确定来源的重放跑到终点", () => player.IsFinished, ReplayTimeoutSeconds);

            yield return Check(
                DescribeDrift(),
                () => player != null && player.HasDrifted && player.DriftCount > 0);
            yield return Check(
                DescribeFirstDrift(),
                () => player != null && player.FirstDriftTick == ExpectedFirstDriftTick()
                      && player.DriftCount > 1);
            yield return Check(
                DescribeResync(),
                () => player != null && player.ResyncCount >= 1 && player.IsFinished
                      && player.CurrentTick > player.StartTick && player.Progress >= 1f);

            yield return Snapshot("漂移回放结束");
            yield return Step("收尾：摘掉演示世界，把推进器与输入源还给实时", RestoreRuntime);
        }

        /// <summary>
        /// 收尾兜底：用例中途红掉时上面的收尾步走不到，这里再做一次（幂等）。
        /// 不清干净的话，演示世界会继续挂在推进器上，污染同一次 Play 里后面的用例。
        /// </summary>
        [UnityTearDown]
        public IEnumerator ReplayShowcaseTearDown()
        {
            RestoreRuntime();
            yield return null;
        }

        /// <summary>第 1 步：把容器里那一套取出来，并抄下回放配置的两个间隔。</summary>
        private void ConnectServices()
        {
            runner = Resolve<SimulationRunner>();
            recorder = Resolve<ReplayRecorder>();
            player = Resolve<ReplayPlayer>();
            recordDriver = Resolve<ReplayRecordDriver>();
            random = Resolve<RandomService>();
            inputSwitch = Resolve<InputSourceSwitch>();
            configService = Resolve<IConfigService>();

            if (recorder != null)
            {
                hashIntervalTicks = recorder.CurrentOptions.StateHashIntervalTicks;
                snapshotIntervalTicks = recorder.CurrentOptions.SnapshotIntervalTicks;
            }

            // 录制长度按快照间隔算，保证「起点一个快照 + 中途至少还有一个快照」——
            // 中途那个快照是第 7 步「漂移后从快照续跑」的前提，写死一个数的话，
            // 哪天有人把 ReplayConfig 的快照间隔调大，这条回放就会静悄悄地验不到那件事。
            recordTicks = snapshotIntervalTicks > 0
                ? Mathf.Max(MinRecordTicks, snapshotIntervalTicks + (snapshotIntervalTicks / 2))
                : MinRecordTicks;
        }

        /// <summary>第 2 步：造世界、接线、把推进器切成外部驱动并归零。</summary>
        private void SetUpDemoWorld()
        {
            world = new DemoWorld(random);
            provider = new DemoStateProvider();
            provider.Register(world);
            scriptedInput = new ScriptedInputSource();
            recordStep = new DemoRecordStep(recorder);
            endStateLatch = new EndStateLatch(provider, recordTicks - 1);

            recorder.AttachStateProvider(provider);
            player.AttachStateProvider(provider);

            // 步骤顺序就是一 tick 内的执行顺序，不能换：
            // 记录（拿到的是「这一 tick 开始之前的世界」）→ 推进世界 → 末态存根。
            // 记录挪到后面的话，录下的会是「这一 tick 跑完之后的世界」，重放整份稳定偏一格。
            runner.AddStep(recordStep);
            runner.AddStep(world);
            runner.AddStep(endStateLatch);

            // 换成脚本输入（理由见文件头）。真实接线件会因此按设计让位，第 3 步会断言这一点。
            inputSwitch.SwitchToReplay(scriptedInput);

            // 推进节奏交给本用例：Live 模式下推几格由渲染帧时长说了算，录出来的长度就不稳定了。
            runner.SetMode(SimulationRunner.Mode.Driven);
            runner.Reset();
            world.ResetWorld();

            // 开一局干净的录制：容器启动到现在这几秒，环里已经攒了一批「没有状态提供者」的空记录，
            // 不清掉的话它们会把文件的起始 tick 拖到一个没有起点快照的位置。
            recorder.BeginSession(random.MasterSeed, ReadConfigHash(), runner.Clock.FixedDeltaTime);

            BuildView();
        }

        /// <summary>第 3 步：在一帧里把整段 tick 推完（不等真实时间），推完当场抄一份末态。</summary>
        private void RecordSession()
        {
            skippedByDriverBefore = recordDriver.SkippedWhileReplaying;
            for (int i = 0; i < recordTicks; i++)
            {
                runner.AdvanceOneTick();
            }

            skippedByDriverAfter = recordDriver.SkippedWhileReplaying;

            // 推进器此刻仍是 Driven、播放器也没启动，世界不会再动，所以这里抄到的就是录制末态。
            recordEndState = provider.Capture();
            endStateLatch.Clear();
        }

        /// <summary>第 4 步：存盘并量一下文件大小。</summary>
        private void SaveReplayFile()
        {
            savedPath = recorder.Save(ReplayRecorder.SaveReason.Api);
            savedBytes = string.IsNullOrEmpty(savedPath) || !File.Exists(savedPath)
                ? 0L
                : new FileInfo(savedPath).Length;

            // 录完就把录制接线摘掉：后面全是重放，再留着它只会把重放的 tick 又记一遍进环
            // （重放期间真实接线件会自己让位，摘掉这个之后环就彻底干净了）。
            runner.RemoveStep(recordStep);
        }

        /// <summary>第 5 / 8 步：载入同一份文件并开播。两次重放走的是同一段代码，只有世界的状态不同。</summary>
        private void LoadAndPlay()
        {
            replayEndState = null;
            endStateLatch.Clear();

            // 只存第一次：本方法被两次重放共用，第二次进来时 Speed 已经是 ReplaySpeed 了，
            // 再存一次就把「原本的倍速」覆盖成了本 Showcase 自己设的值，还原就成了空操作。
            if (speedBeforeReplay < 0f)
            {
                speedBeforeReplay = player.Speed;
            }

            player.Speed = ReplaySpeed;
            loadOk = player.Load(savedPath, out loadError);
            if (!loadOk)
            {
                return;
            }

            player.Play();
        }

        /// <summary>把推进器、输入源与状态注册表还原成实时玩法的样子。可重复调用。</summary>
        private void RestoreRuntime()
        {
            // 状态提供者要还原成**容器里那个**，不是 null。
            // 早先写这段时容器还没注册状态提供者（那正是当时的缺口），还原成 null 是对的；
            // 现在 GameLifetimeScope.RegisterReplay 已经注册了 ReplayStateRegistry 并在
            // RegisterBuildCallback 里挂给了录制器与播放器，再挂回 null 就成了**破坏**：
            // 这条 Showcase 跑完，真实容器里的回放只剩输入流，没有状态哈希也没有完整快照，
            // 一直持续到下次域重载——「跑完验证接着手动进 Play 玩一下」录出来的就是残缺回放。
            // 解析复用本类既有的 Resolve<T>()（反射调容器的 TryResolve，理由见 TryConnectContainer）；
            // 解析不到（容器没建好 / 没注册）再退回 null，那种情形下本来也没有提供者可还原。
            IReplayStateProvider runtimeStateProvider = Resolve<IReplayStateProvider>();

            if (player != null)
            {
                player.Stop();
                player.AttachStateProvider(runtimeStateProvider);

                // 倍速同样要还原（理由见 speedBeforeReplay 的注释）。可重复调用：
                // 还原后把哨兵置回负数，第二次调用不会拿一个过期的值再盖一遍。
                if (speedBeforeReplay >= 0f)
                {
                    player.Speed = speedBeforeReplay;
                    speedBeforeReplay = -1f;
                }
            }

            if (recorder != null)
            {
                recorder.AttachStateProvider(runtimeStateProvider);
            }

            if (runner != null)
            {
                if (recordStep != null)
                {
                    runner.RemoveStep(recordStep);
                }

                if (world != null)
                {
                    runner.RemoveStep(world);
                }

                if (endStateLatch != null)
                {
                    runner.RemoveStep(endStateLatch);
                }

                runner.SetMode(SimulationRunner.Mode.Live);
            }

            if (inputSwitch != null)
            {
                inputSwitch.SwitchToLive();
            }

            recordStep = null;
            endStateLatch = null;
        }

        /// <summary>重放末态从存根里取：播放器放完会当场把推进器切回 Live，晚一帧再抄就已经多跑了。</summary>
        private byte[] TakeReplayEndState()
        {
            if (replayEndState == null && endStateLatch != null)
            {
                replayEndState = endStateLatch.Captured;
            }

            return replayEndState;
        }

        /// <summary>造几个方块把世界画出来，好让重放与漂移在 Game 视图里看得见。</summary>
        private void BuildView()
        {
            EnsureCamera();

            Texture2D texture = Track(new Texture2D(MarkerTextureSize, MarkerTextureSize));
            texture.filterMode = FilterMode.Point;
            Color[] pixels = new Color[MarkerTextureSize * MarkerTextureSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();

            Sprite sprite = Track(Sprite.Create(
                texture,
                new Rect(0f, 0f, MarkerTextureSize, MarkerTextureSize),
                new Vector2(0.5f, 0.5f),
                MarkerTextureSize / MarkerWorldSize));
            sprite.name = "DemoEntitySprite";

            Transform[] markers = new Transform[world.Count];
            for (int i = 0; i < markers.Length; i++)
            {
                GameObject host = Track(new GameObject("DemoEntity" + i));
                SpriteRenderer renderer = host.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = i == 0 ? Color.white : MarkerColors[(i - 1) % MarkerColors.Length];
                markers[i] = host.transform;
            }

            GameObject viewHost = Track(new GameObject("DemoWorldView"));
            viewHost.AddComponent<DemoWorldView>().Bind(world, markers);
        }

        // --- 下面全是给步骤标题 / 检查点文案算数字的小函数 ---
        // 它们**必须一个都不抛异常**：标题串是在测试方法体里算的，不在 Step / Check 的 try 里面，
        // 这里抛一下会把整条回放当场打断，连报告都写不出来（实测踩过：前一个服务没解析到，
        // 下一条标题去读它的属性，NullReferenceException 直接掀翻用例）。所以一律先判空再取值。

        private string DescribeResolve()
        {
            return player != null && recorder != null
                ? "从真实容器里解析得出 ReplayPlayer 与 ReplayRecorder（两者都是容器里的同一个单例）"
                : $"从真实容器里解析得出 ReplayPlayer 与 ReplayRecorder —— 没解析到："
                  + $"{containerError ?? "容器已连上，但这两个服务没注册"}";
        }

        private string DescribeCoreResolve()
        {
            return runner != null && random != null && inputSwitch != null && configService != null
                ? "确定性内核那一套也在：推进器 / 随机源 / 输入切换壳 / 配置服务，四样都取到了"
                : $"确定性内核那一套也在：推进器 / 随机源 / 输入切换壳 / 配置服务 —— 缺东西："
                  + $"{containerError ?? "容器已连上，但有服务没注册"}";
        }

        private string DescribeDriver()
        {
            string enabled = recorder == null ? "?" : (recorder.Enabled ? "开" : "关");
            string attached = recordDriver == null ? "?" : (recordDriver.IsAttached ? "已挂" : "没挂");
            string steps = runner == null ? "?" : runner.StepCount.ToString();
            return $"录制总开关是{enabled}的，接线件{attached}进推进器（推进器上已注册 {steps} 个步骤）";
        }

        private string DescribeIntervals()
        {
            return $"回放配置会记哈希与快照：每 {hashIntervalTicks} tick 一个哈希点、"
                   + $"每 {snapshotIntervalTicks} tick 一个完整快照";
        }

        private string DescribeProvider()
        {
            int count = provider == null ? 0 : provider.Count;
            int bytes = CaptureLength();
            return $"世界已进状态注册表：{count} 份状态、快照 {bytes} 字节"
                   + $"（{DemoWorld.EntityCount} 个实体 + 2 条 logic.* 流的状态）";
        }

        private string DescribeRecorded()
        {
            if (recorder == null)
            {
                return "录下这一段输入、哈希与快照 —— 录制器没取到，什么都没录";
            }

            return $"录下 {recorder.BufferedInputCount} 条输入"
                   + $"（tick {recorder.OldestBufferedTick}~{recorder.NewestBufferedTick}）、"
                   + $"{recorder.BufferedStateHashCount} 个状态哈希点（应有 {ExpectedHashRecords()} 个）、"
                   + $"{recorder.BufferedSnapshotCount} 个完整快照（应有 {ExpectedSnapshotRecords()} 个）";
        }

        private string DescribeDriverStandDown()
        {
            return $"真实录制接线件全程在岗、并按设计让位：这 {recordTicks} 个 tick 它一条都没重复录"
                   + $"（跳过计数 {skippedByDriverBefore} → {skippedByDriverAfter}）";
        }

        /// <summary>
        /// 存盘结果。文件路径**只显示存档目录下的相对部分**：绝对路径带本机用户名，
        /// 不该写进报告（CLAUDE.md 硬规则第 2 条）。
        /// </summary>
        private string DescribeSavedFile()
        {
            string shown = string.IsNullOrEmpty(savedPath)
                ? "（没存出来）"
                : $"{ReplayRecorder.ReplayFolderName}/{Path.GetFileName(savedPath)}";
            return $"存出一份回放：{shown}，{savedBytes} 字节";
        }

        private string DescribeLoaded()
        {
            if (!loadOk || player == null)
            {
                return $"载入失败：{loadError ?? "播放器没取到"}";
            }

            return $"载入成功：tick {player.StartTick}~{player.EndTick}，输入 {player.InputCount} 条、"
                   + $"状态哈希 {player.StateHashCount} 条、完整快照 {player.SnapshotCount} 个";
        }

        private string DescribeProgress()
        {
            if (player == null)
            {
                return "重放进行中 —— 播放器没取到";
            }

            return $"重放进行中：第 {player.CurrentTick} tick，进度 "
                   + $"{Mathf.RoundToInt(player.Progress * 100f)}%";
        }

        private string DescribeCleanReplay()
        {
            if (player == null)
            {
                return "全程状态哈希逐点一致、零漂移 —— 播放器没取到";
            }

            return $"全程状态哈希逐点一致：{player.DriftCount} 次漂移"
                   + $"（校验了 {player.CheckedHashCount} 个哈希点）";
        }

        private string DescribeCheckedPoints()
        {
            int checkedPoints = player == null ? 0 : player.CheckedHashCount;
            return $"校验点数 {checkedPoints} 个 > 0——「零漂移」是真的逐点校验过，不是根本没在校验";
        }

        private string DescribeEndStates()
        {
            byte[] replayed = TakeReplayEndState();
            int recorded = recordEndState == null ? 0 : recordEndState.Length;
            int played = replayed == null ? 0 : replayed.Length;
            string sizes = recorded == played ? $"各 {recorded}" : $"录制 {recorded} / 重放 {played}";
            return $"重放末态与录制末态逐位相同（{sizes} 字节）";
        }

        private string DescribeDrift()
        {
            int drifts = player == null ? 0 : player.DriftCount;
            return $"报出了漂移：共 {drifts} 个校验点对不上";
        }

        private string DescribeFirstDrift()
        {
            long first = player == null ? -1L : player.FirstDriftTick;
            return $"首次漂移记在 tick {first}——注入点 {DemoWorld.DriftFirstTick} 之后的第一个校验点"
                   + $"（应为 {ExpectedFirstDriftTick()}），不是最后一个"
                   + $"（这份录像最后一个校验点在 tick {LastCheckpointTick()}）";
        }

        private string DescribeResync()
        {
            if (player == null)
            {
                return "漂移后从完整快照续跑、一路跑到终点 —— 播放器没取到";
            }

            return $"漂移后从完整快照拉回正轨 {player.ResyncCount} 次，回放一路跑到终点 "
                   + $"tick {player.CurrentTick}（终点 {player.EndTick}）、没有中断";
        }

        /// <summary>抄一份世界字节只为了报个长度，失败不许掀翻标题串。</summary>
        private int CaptureLength()
        {
            if (provider == null)
            {
                return 0;
            }

            try
            {
                return provider.Capture().Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>录制这段里应该被记下几个状态哈希点（tick 0 也算一个）。</summary>
        private int ExpectedHashRecords()
        {
            return hashIntervalTicks <= 0 ? 0 : ((recordTicks - 1) / hashIntervalTicks) + 1;
        }

        /// <summary>录制这段里应该被记下几个完整快照（起点那个也算）。</summary>
        private int ExpectedSnapshotRecords()
        {
            return snapshotIntervalTicks <= 0 ? 0 : ((recordTicks - 1) / snapshotIntervalTicks) + 1;
        }

        /// <summary>
        /// 注入点之后第一个**应该**报出漂移的校验点：注入从 DriftFirstTick 这一 tick 的逻辑开始生效，
        /// 而 tick T 的哈希记的是「T 开始之前的世界」，所以第一个受影响的校验点是大于注入点的
        /// 第一个哈希间隔倍数。
        /// </summary>
        private long ExpectedFirstDriftTick()
        {
            if (hashIntervalTicks <= 0)
            {
                return -1L;
            }

            return ((DemoWorld.DriftFirstTick / hashIntervalTicks) + 1L) * hashIntervalTicks;
        }

        /// <summary>这份录像里最后一个校验点的 tick，用来说明「首次漂移记的确实不是最后一个」。</summary>
        private long LastCheckpointTick()
        {
            return hashIntervalTicks <= 0 ? -1L : ((recordTicks - 1) / hashIntervalTicks) * (long)hashIntervalTicks;
        }

        /// <summary>
        /// 取配置指纹写进录像头。取不到就记 0——播放器那边同样取不到时会跳过版本校验，两边口径一致，
        /// 不会因为一个取不到的指纹把整份回放判成「配置换版了」。
        /// </summary>
        private ulong ReadConfigHash()
        {
            try
            {
                return configService == null ? 0UL : configService.ContentHash;
            }
            catch (Exception)
            {
                return 0UL;
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 连上运行中的 VContainer 容器。**只能走反射**：<c>Game.Tests.Showcase</c> 没有引用 VContainer，
        /// 而 <c>GameLifetimeScope</c> 的基类 <c>LifetimeScope</c> 就在那个程序集里——
        /// 源码里但凡写出 <c>GameLifetimeScope</c> 这个名字就是 CS0012（实测过）。
        /// 容器的 <c>TryResolve&lt;T&gt;(out T)</c> 只是 <c>TryResolve(Type, out object, object)</c> 的泛型外壳，
        /// 所以这里反射调后者，验到的就是编辑器回放窗口走的那条解析路径。
        /// </summary>
        private bool TryConnectContainer()
        {
            if (container != null && resolveMethod != null)
            {
                return true;
            }

            Type scopeType = Type.GetType(ScopeTypeName);
            if (scopeType == null)
            {
                containerError = $"找不到类型 {ScopeTypeName}";
                return false;
            }

            UnityEngine.Object scopeObject = UnityEngine.Object.FindObjectOfType(scopeType);
            if (scopeObject == null)
            {
                containerError = "场上没有 GameLifetimeScope（Boot 场景没加载成功？）";
                return false;
            }

            PropertyInfo containerProperty = scopeType.GetProperty("Container");
            if (containerProperty == null)
            {
                containerError = "LifetimeScope 上没有 Container 属性（VContainer 换版本了？）";
                return false;
            }

            object resolver = containerProperty.GetValue(scopeObject);
            if (resolver == null)
            {
                containerError = "LifetimeScope.Container 还是空的（容器没建完）";
                return false;
            }

            // 找的是 IObjectResolver.TryResolve(Type type, out object resolved, object key = null)——
            // 编辑器回放窗口用的 TryResolve<T>(out T) 就是它的泛型外壳（VContainer 的
            // IObjectResolverExtensions），所以这里验到的是同一条解析路径。
            // 注意别去找 Resolve(Type)：那个方法带一个可选的 key 参数，按单参数签名找是找不到的（实测过）。
            MethodInfo method = resolver.GetType().GetMethod(
                "TryResolve",
                new[] { typeof(Type), typeof(object).MakeByRefType(), typeof(object) });
            if (method == null)
            {
                containerError = "容器上找不到 TryResolve(Type, out object, object)（VContainer 换版本了？）";
                return false;
            }

            container = resolver;
            resolveMethod = method;
            containerError = null;
            return true;
        }

        /// <summary>
        /// 从容器里解析一个服务，没注册就返回 null。
        /// <para>这里**故意吞掉异常不打日志**：没注册时 VContainer 抛的是异常，而本方法在
        /// <see cref="WaitForBootReady"/> 里每帧都会被调一次，打日志会把控制台刷爆；
        /// 真正没解析到时，第 1 步的检查点会连同 <see cref="containerError"/> 一起记进报告。</para>
        /// </summary>
        private T Resolve<T>()
            where T : class
        {
            if (!TryConnectContainer())
            {
                return null;
            }

            try
            {
                object[] args = { typeof(T), null, null };
                bool resolved = (bool)resolveMethod.Invoke(container, args);
                return resolved ? args[1] as T : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 状态注册表的最小实现。框架层只给了 <see cref="IReplayStateProvider"/> 契约，没给实现
        /// （该由接线层按玩法结构定注册顺序），所以这里给回放 Showcase 自己接一个。
        /// </summary>
        private sealed class DemoStateProvider : IReplayStateProvider
        {
            private readonly List<IReplayState> states = new List<IReplayState>();
            private readonly StateBuffer buffer = new StateBuffer();

            /// <inheritdoc />
            public int Count
            {
                get { return states.Count; }
            }

            /// <inheritdoc />
            public void Register(IReplayState state)
            {
                if (state == null)
                {
                    throw new ArgumentNullException(nameof(state));
                }

                states.Add(state);
            }

            /// <inheritdoc />
            public void SerializeAll(IStateWriter writer)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    states[i].Serialize(writer);
                }
            }

            /// <inheritdoc />
            public void DeserializeAll(IStateReader reader)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    states[i].Deserialize(reader);
                }
            }

            /// <summary>抄一份此刻的世界字节，用来做「录制末态 vs 重放末态」的逐位比较。</summary>
            internal byte[] Capture()
            {
                buffer.Reset();
                SerializeAll(buffer);
                byte[] copy = new byte[buffer.Length];
                Array.Copy(buffer.GetBuffer(), copy, buffer.Length);
                return copy;
            }
        }

        /// <summary>
        /// 脚本输入源：按 tick 算出一条命令，轴每隔一阵换一次、按钮按固定周期点一下。
        /// 值只跟 tick 有关，所以录制那一遍本身也是可复现的。
        /// </summary>
        private sealed class ScriptedInputSource : IInputSource
        {
            /// <summary>轴保持同一个值多少 tick。取质数，免得和按钮周期对齐成一个短循环。</summary>
            private const int AxisHoldTicks = 37;

            /// <summary>Confirm / Cancel 的按下周期（tick）。</summary>
            private const int ConfirmPeriodTicks = 53;
            private const int CancelPeriodTicks = 211;

            private static readonly Vector2[] AxisPattern =
            {
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 1f),
                new Vector2(-0.75f, 0.25f),
                new Vector2(-1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(0.25f, -0.75f),
            };

            private InputCommand current;

            /// <inheritdoc />
            public InputCommand Current
            {
                get { return current; }
            }

            /// <inheritdoc />
            public void Sample(long tick)
            {
                Vector2 axis = AxisPattern[(int)(tick / AxisHoldTicks % AxisPattern.Length)];
                uint buttons = 0u;
                if (tick % ConfirmPeriodTicks == 0L)
                {
                    buttons |= InputCommand.ButtonConfirm;
                }

                if (tick % CancelPeriodTicks == 0L)
                {
                    buttons |= InputCommand.ButtonCancel;
                }

                current = new InputCommand(axis, Vector2.zero, buttons, Vector2.zero, 0);
            }
        }

        /// <summary>
        /// 录制接线：每 tick 把「这一 tick 的序号 + 它用掉的那条输入」交给录制器。
        /// 它是 <c>ReplayRecordDriver</c> 在本用例里的替身——脚本输入必须挂成「录像源」，
        /// 而真实接线件看到「正在放录像」就会按设计让位（理由见文件头）。
        /// </summary>
        private sealed class DemoRecordStep : ISimulationStep
        {
            private readonly ReplayRecorder recorder;

            internal DemoRecordStep(ReplayRecorder recorder)
            {
                this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            }

            /// <inheritdoc />
            public void Step(in SimulationContext context)
            {
                recorder.RecordTick((uint)context.Tick, context.Input);
            }
        }

        /// <summary>
        /// 末态存根：在指定 tick 跑完之后把整个世界抄一份留着。
        /// <para>
        /// 为什么要它：播放器放到最后一格会当场把推进器切回 Live，世界从下一帧起就继续动了。
        /// 等协程醒过来再抄，抄到的可能已经多跑了几个 tick——那样「重放末态 vs 录制末态」这条
        /// 检查点就会时红时绿，而红的原因和回放一点关系都没有。存根跟着 tick 走，没有这个时序缝。
        /// </para>
        /// <para>它只在唯一那个 tick 上分配一次 byte[]，不在每 tick 的热路径上。</para>
        /// </summary>
        private sealed class EndStateLatch : ISimulationStep
        {
            private readonly DemoStateProvider provider;
            private readonly long latchTick;

            private byte[] captured;

            internal EndStateLatch(DemoStateProvider provider, long latchTick)
            {
                this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
                this.latchTick = latchTick;
            }

            /// <summary>存下来的那一份；还没走到那个 tick 时是 null。</summary>
            internal byte[] Captured
            {
                get { return captured; }
            }

            /// <summary>清掉上一轮的存货，开始下一轮之前调。</summary>
            internal void Clear()
            {
                captured = null;
            }

            /// <inheritdoc />
            public void Step(in SimulationContext context)
            {
                if (context.Tick != latchTick)
                {
                    return;
                }

                captured = provider.Capture();
            }
        }

        /// <summary>
        /// 把世界状态同步到画面上的几个方块。只读位置，不参与任何逻辑——
        /// 它要是能影响逻辑，本身就成了一个新的不确定来源。
        /// </summary>
        private sealed class DemoWorldView : MonoBehaviour
        {
            private DemoWorld world;
            private Transform[] markers;

            internal void Bind(DemoWorld demoWorld, Transform[] demoMarkers)
            {
                world = demoWorld;
                markers = demoMarkers;
            }

            private void LateUpdate()
            {
                if (world == null || markers == null)
                {
                    return;
                }

                int count = Mathf.Min(markers.Length, world.Count);
                for (int i = 0; i < count; i++)
                {
                    if (markers[i] == null)
                    {
                        continue;
                    }

                    Vector2 position = world.PositionOf(i);
                    markers[i].position = new Vector3(position.x, position.y, 0f);
                }
            }
        }
    }
}
