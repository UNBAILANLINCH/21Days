// 职责：实时输入源。每 tick 采样一次设备状态，打成一条定长 InputCommand，供推进器与录制端共用。
//   它是「设备世界」与「可重放的逻辑世界」之间唯一的那道闸门。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：Core/Input/InputService 只负责创建 GameInput、启停动作图与释放，它从不「读值」，
//      工程里没有第二个地方把设备状态定格成一帧数据。
//   2. 扩展不行：不能把采样加进 InputService。IInputService 暴露的是 Input System 的动作集本身，
//      读它拿到的是「当前设备状态」而不是「可序列化的一帧输入」；把录制关注点塞进一个只管启停
//      Action Map 的接口，等于让 UI 导航、调试快捷键这些不需要确定性的用法也跟着背上录制格式的包袱。
//      分成两层之后，InputService 继续服务那些场合，一个字都不用改。

using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Input;
using Game.Core.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Core.Simulation
{
    /// <summary>
    /// 从 <see cref="IInputService"/> 的动作集上采样，产出 <see cref="InputCommand"/>。
    /// <para>
    /// 动作引用在<b>初始化时一次性 <c>FindAction</c> 缓存</b>，采样路径里绝不做查找——
    /// <c>FindAction</c> 是按字符串遍历动作图，而 <see cref="Sample"/> 是每 tick 都要走的热路径。
    /// </para>
    /// <para>
    /// 动作不存在时只记一条 Warn、对应槽位留空，<b>不抛异常</b>：动作图是会改的，
    /// 少一个动作应该表现为「那一路输入录不到」，而不是整局崩掉。
    /// </para>
    /// <para>
    /// 实现 <see cref="IGameService"/> 是为了让容器把初始化排在 <see cref="IInputService"/> 之后
    /// （那时 <c>Actions</c> 才非 null）。万一注册时没走这条路，<see cref="Sample"/> 第一次调用
    /// 会兜底初始化一次。推进器比 <see cref="InitializeAsync"/> 先跑起来的那几帧，兜底会拿到
    /// null 的 <c>Actions</c>——那是启动期的正常状态，<b>静默重试、不报警</b>，见
    /// <see cref="Initialize"/> 里的说明。
    /// </para>
    /// </summary>
    public sealed class LiveInputSource : IInputSource, IGameService
    {
        private const string MoveActionPath = "Gameplay/Move";
        private const string ConfirmActionPath = "Gameplay/Confirm";
        private const string CancelActionPath = "Gameplay/Cancel";
        private const string PauseActionPath = "Gameplay/Pause";
        private const string SneakActionPath = "Gameplay/Sneak";
        private const string DisguiseActionPath = "Gameplay/Disguise";
        private const string AttackActionPath = "Gameplay/Attack";
        private const string RunActionPath = "Gameplay/Run";

        private readonly IInputService inputService;

        private InputAction moveAction;
        private InputAction confirmAction;
        private InputAction cancelAction;
        private InputAction pauseAction;
        private InputAction sneakAction;
        private InputAction disguiseAction;
        private InputAction attackAction;
        private InputAction runAction;

        private InputCommand current;
        private bool ready;
        private bool initFailed;

        // InitializeAsync 有没有被容器调用过。用来区分「启动期还没轮到我」和「轮到我了但拿不到东西」：
        // SimulationRunner 是 VContainer 的 EntryPoint，容器一建完（LifetimeScope.Awake）就开始每帧
        // Tick，而 InputService.InitializeAsync 要到 GameBootstrap.Start 之后才跑完。这中间的几帧里
        // Sample 会走兜底 Initialize()，此时 Actions 还是 null——注册顺序其实是对的，只是还没轮到。
        private bool initializeCalled;

        /// <param name="inputService">输入服务，动作集的持有者。初始化时从它身上取动作引用。</param>
        public LiveInputSource(IInputService inputService)
        {
            this.inputService = inputService;
        }

        /// <summary>最近一次 <see cref="Sample"/> 采到的命令。还没采过时是 <see cref="InputCommand.Empty"/>。</summary>
        public InputCommand Current => current;

        /// <summary>动作引用是否已经缓存成功。false 时 <see cref="Sample"/> 的动作槽位全空（<see cref="HeldButtons"/> 照样生效）。</summary>
        public bool IsReady => ready;

        /// <summary>
        /// 软件侧按住位：每次 <see cref="Sample"/> 时原样 OR 进 <see cref="InputCommand.Buttons"/>。
        /// 给不走动作图的软件按钮用（例如 UI 上的奔跑钮：按下期间置 <see cref="InputCommand.ButtonRun"/>、
        /// 松开清位，与键盘按住同一语义；切换由玩法规则按按下沿完成）。
        /// 它必须进 <see cref="InputCommand"/> 才能被确定性内核与回放看到；若由上层直接改玩法状态，
        /// 重放时读不到这一路，录像就会分叉。置位与清位由持有该按钮的一方负责；这里不校验位，也不清。
        /// </summary>
        public uint HeldButtons { get; set; }

        /// <summary>
        /// 缓存动作引用。要求注册顺序排在 <see cref="IInputService"/> 之后，
        /// 否则这里只会记一条 Warn（<c>Actions</c> 还是 null），整局采不到输入。
        /// </summary>
        public UniTask InitializeAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // 先置位再初始化：从这一刻起，拿不到 Actions 就不再是「还没轮到」，而是真出了问题，
            // 下面的 Initialize 该打的 Warn 一条不少。
            initializeCalled = true;
            Initialize();
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 一次性缓存动作引用。已经成功过就直接返回；失败过也不会被 <see cref="Sample"/> 反复重试
        /// （<c>FindAction</c> 不能上每 tick 的热路径），但可以手动再调一次重试。
        /// <para>
        /// 启动期「还没轮到」不算失败：那时只做一次空引用判断就返回，不触碰 <c>FindAction</c>，
        /// 所以让 <see cref="Sample"/> 每 tick 重试是安全的。
        /// </para>
        /// </summary>
        public void Initialize()
        {
            if (ready)
            {
                return;
            }

            if (inputService == null)
            {
                Fail("LiveInputSource 没拿到 IInputService，实时输入将全部为空");
                return;
            }

            GameInput actions = inputService.Actions;
            if (actions == null)
            {
                // 「尚未就绪」和「真的失败」是两回事，必须分开，否则启动期每次都会打一条
                // 指向根本不存在的问题的 Warn（怀疑注册顺序，但顺序其实是对的）：
                //   · InitializeAsync 还没被调用过 → 现在是启动期的正常状态。SimulationRunner 作为
                //     EntryPoint 从 LifetimeScope.Awake 起就每帧 Tick，而 InputService.InitializeAsync
                //     要等到 GameBootstrap.Start 之后才跑完；这中间 Actions 本来就还是 null。
                //     静默返回：不打 Warn、不置 initFailed，Current 保持 Empty，下次 Sample 再试，
                //     等 InputService 就绪后自然会成功。
                //   · InitializeAsync 已经被调用过 → 轮到我了还是拿不到，这才是真问题，照原样打 Warn。
                if (!initializeCalled)
                {
                    return;
                }

                Fail("LiveInputSource 初始化时 IInputService.Actions 还是 null——注册顺序要排在 InputService 之后；实时输入将全部为空");
                return;
            }

            InputActionAsset asset = actions.asset;
            if (asset == null)
            {
                Fail("LiveInputSource 初始化时 GameInput.asset 为 null，实时输入将全部为空");
                return;
            }

            moveAction = FindAction(asset, MoveActionPath);
            confirmAction = FindAction(asset, ConfirmActionPath);
            cancelAction = FindAction(asset, CancelActionPath);
            pauseAction = FindAction(asset, PauseActionPath);
            sneakAction = FindAction(asset, SneakActionPath);
            disguiseAction = FindAction(asset, DisguiseActionPath);
            attackAction = FindAction(asset, AttackActionPath);
            runAction = FindAction(asset, RunActionPath);
            ready = true;
            initFailed = false;
        }

        /// <summary>
        /// 采样一次当前设备状态，产出这一 tick 的命令，写进 <see cref="Current"/>。
        /// <b>由推进器每 tick 调用一次</b>，采完从 <see cref="Current"/> 读。
        /// <para>
        /// 一帧内推进多个 tick 时，每个 tick 各调一次，会得到多条内容相同的命令——
        /// <b>这是预期行为，不是冗余，不要「优化」成一帧只采一条</b>：重放是按 tick 逐条回放的，
        /// 录制端少记一条，重放的 tick 数当场就跟录制对不上，之后每一 tick 的输入都错位。
        /// </para>
        /// <para>
        /// <paramref name="tick"/> 在实时源里<b>用不上</b>：这里读的是「此刻的设备状态」，
        /// 设备并不知道逻辑推到第几格了。参数仍然保留，是因为它属于
        /// <see cref="IInputSource"/> 的契约——录像源要靠它断言取到的命令与推进器的 tick 对齐，
        /// 实时源用不上就把参数去掉，接口两边的实现就不再是同一个签名，切换壳也就无从转发。
        /// </para>
        /// <para>
        /// 零分配：命令是 <c>readonly struct</c>（在栈上构造，不走 GC），路径里没有查找、
        /// 没有字符串拼接、没有日志——Warn 只在初始化时记。
        /// </para>
        /// </summary>
        /// <param name="tick">推进器正要推进的那个 tick 号；实时采样不使用它，见上。</param>
        public void Sample(long tick)
        {
            // 兜底：容器没把它按 IGameService 注册时，第一次采样顺手初始化一次。
            // 失败过就不再重试，免得 FindAction 的字符串查找落到每 tick 的路径上。
            if (!ready && !initFailed)
            {
                Initialize();
            }

            Vector2 axis0 = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;

            uint buttons = HeldButtons;
            if (confirmAction != null && confirmAction.IsPressed())
            {
                buttons |= InputCommand.ButtonConfirm;
            }

            if (cancelAction != null && cancelAction.IsPressed())
            {
                buttons |= InputCommand.ButtonCancel;
            }

            if (pauseAction != null && pauseAction.IsPressed())
            {
                buttons |= InputCommand.ButtonPause;
            }

            if (sneakAction != null && sneakAction.IsPressed())
            {
                buttons |= InputCommand.ButtonSneak;
            }

            if (disguiseAction != null && disguiseAction.IsPressed())
            {
                buttons |= InputCommand.ButtonDisguise;
            }

            if (attackAction != null && attackAction.IsPressed())
            {
                buttons |= InputCommand.ButtonAttack;
            }

            if (runAction != null && runAction.IsPressed())
            {
                buttons |= InputCommand.ButtonRun;
            }

            // HeldButtons（软件侧按住位）已在上面作为初值 OR 进来。
            // Axis1 / Pointer 当前没有对应动作，恒为零；Flags 预留，恒为 0。
            // bit31 的 QA 打点标记不在这里置位——它不来自动作图，由录制系统的热键按到命令上。
            current = new InputCommand(axis0, Vector2.zero, buttons, Vector2.zero, 0);
        }

        private static InputAction FindAction(InputActionAsset asset, string path)
        {
            InputAction action = asset.FindAction(path, false);
            if (action == null)
            {
                Log.Warn($"GameInput 里找不到动作 {path}，对应槽位在录制里恒为空；动作图改名后记得同步 LiveInputSource");
            }

            return action;
        }

        private void Fail(string message)
        {
            initFailed = true;
            Log.Warn(message);
        }
    }
}
