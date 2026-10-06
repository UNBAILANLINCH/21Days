// 职责：实时输入源。每 tick 采样一次设备状态，打成一条定长 InputCommand，供推进器与录制端共用。
//   它是「设备世界」与「可重放的逻辑世界」之间唯一的那道闸门。
// 为什么不复用、不扩展（加能力的顺序：复用 → 扩展 → 新建）：
//   1. 复用不行：Core/Input/InputService 只负责创建 GameInput、启停动作图与释放，它从不「读值」，
//      工程里没有第二个地方把设备状态定格成一帧数据。
//   2. 扩展不行：不能把采样加进 InputService。IInputService 暴露的是 Input System 的动作集本身，
//      读它拿到的是「当前设备状态」而不是「可序列化的一帧输入」；把录制关注点塞进一个只管启停
//      Action Map 的接口，等于让 UI 导航、调试快捷键这些不需要确定性的用法也跟着背上录制格式的包袱。
//      分成两层之后，InputService 继续服务那些场合，一个字都不用改。

using System;
using System.Collections.Generic;
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
    /// 初始化时另跑一次<b>守卫</b>：把 Gameplay 动作图里既没有对应内核位、又没写进
    /// <see cref="ExemptActionNames"/> 的动作点名报出来。以前「动作图加了新动作、内核没接」的表现是
    /// 采样一路静默丢到重放分叉为止，中间没有任何迹象；现在至少有一条点名的 Warn，
    /// 判据本身也有 EditMode 用例守着（<see cref="FindUnmappedGameplayActions"/>）。
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
        private const string TameActionPath = "Gameplay/Tame";
        private const string InteractActionPath = "Gameplay/Interact";
        private const string InventoryActionPath = "Gameplay/Inventory";

        /// <summary>玩法动作图的名字。守卫按它遍历动作图，与 <c>InputService.GameplayMap</c> 是同一个名字。</summary>
        private const string GameplayMapName = "Gameplay";

        /// <summary>
        /// 已经接进 <see cref="InputCommand"/> 的 Gameplay 动作名（不带地图前缀），必须与上面的路径常量一一对应。
        /// <b>新增一路输入的顺序</b>：加路径常量 → 加字段 → 加 <see cref="Initialize"/> 里那句 <c>FindAction</c>
        /// → 加 <see cref="Sample"/> 里那一段 → 在这里加一条名字。
        /// 漏掉最后一步不会变成静默丢失：守卫会把新动作报成「没人接」的 Warn，测试里同一条判据直接红。
        /// </summary>
        private static readonly string[] WiredActionNames =
        {
            "Move", "Confirm", "Cancel", "Pause", "Sneak", "Disguise", "Attack", "Run",
            "Tame", "Interact", "Inventory",
        };

        /// <summary>
        /// **明确决定不进确定性内核**的 Gameplay 动作名，每条都写清理由。
        /// 它们采样不到是刻意的、不是漏接；守卫不报它们，免得把 Warn 刷成人人忽略的背景噪音。
        /// <para>这张表只让守卫闭嘴，不让动作真的进内核——将来某一路要给玩法用，就得按
        /// <see cref="WiredActionNames"/> 的步骤补位，并把名字从这张表里删掉。</para>
        /// </summary>
        private static readonly string[] ExemptActionNames =
        {
            // 沉浸键（ExplorationHudPresenter）：只切 HUD 显隐，属表现层，不影响逻辑推进，也不进模型快照。
            "Immersive",
            // 任务键（QuestHudPresenter）：开面板是 UI 行为，不进确定性模拟与回放（QuestHudPresenter 里写明了这条判断）。
            "Journal",
            // 照镜 / 自照（MirrorInputPresenter）：Mirror 模块随旧版策划冻结（2026-10-06），
            // 现在接在它上面的也只是表现层按下沿。聚光灯阶段九「随身镜识破」真要落地时更可能复用 Interact，
            // 到那时再补位——补位是纯追加、不改字节布局，成本与现在一样，不必先押一个可能用不上的动作名。
            "Mirror", "MirrorSelf",
            // 处决键（ExecutionInteractor，PRP/stealth-execution §2.4）：背后按 F 处决是**玩家实时输入 → 立即结算**，
            // 与 DialogueKeyboardInput / MirrorInputPresenter 同一层，不做成 tick 里的一步——所以不进 InputCommand。
            // 代价：**处决不可回放**（回放跑的是确定性 tick，喂不进实时按键）。
            // 将来要让它可回放：按 WiredActionNames 的步骤补一个位 + 在 Sample 里接上 + 同步位断言测试
            //（若同时动到快照字节布局，还要升 ReplayFormat.CurrentFormatVersion）。
            "Execute",
        };

        private readonly IInputService inputService;

        private InputAction moveAction;
        private InputAction confirmAction;
        private InputAction cancelAction;
        private InputAction pauseAction;
        private InputAction sneakAction;
        private InputAction disguiseAction;
        private InputAction attackAction;
        private InputAction runAction;
        private InputAction tameAction;
        private InputAction interactAction;
        private InputAction inventoryAction;

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
            tameAction = FindAction(asset, TameActionPath);
            interactAction = FindAction(asset, InteractActionPath);
            inventoryAction = FindAction(asset, InventoryActionPath);

            // 守卫：动作图里有、内核里没有的动作，在这里点名报一次。只在初始化路径上，不进 Sample。
            ReportUnmappedActions(asset);
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

            // 下面三路是聚光灯 S1 / S3 要用的输入（附身、交互、背包），位定义见 InputCommand 上各自的常量。
            // 现在还没有玩法消费它们，但采样必须先接上：等玩法接进来时再补，中间那段时间的录像里
            // 这几路是空的，而且不会有任何报错。
            if (tameAction != null && tameAction.IsPressed())
            {
                buttons |= InputCommand.ButtonTame;
            }

            if (interactAction != null && interactAction.IsPressed())
            {
                buttons |= InputCommand.ButtonInteract;
            }

            if (inventoryAction != null && inventoryAction.IsPressed())
            {
                buttons |= InputCommand.ButtonInventory;
            }

            // HeldButtons（软件侧按住位）已在上面作为初值 OR 进来。
            // Axis1 / Pointer 当前没有对应动作，恒为零；Flags 预留，恒为 0。
            // bit31 的 QA 打点标记不在这里置位——它不来自动作图，由录制系统的热键按到命令上。
            current = new InputCommand(axis0, Vector2.zero, buttons, Vector2.zero, 0);
        }

        /// <summary>
        /// 找出动作图里既没有对应内核位、也没有写进 <see cref="ExemptActionNames"/> 的 Gameplay 动作名。
        /// <para>
        /// <b>这是「不报错地丢输入」的守卫</b>：动作图随时会加动作，加了而没人接进 <see cref="InputCommand"/> 时，
        /// 采样会一路静默丢到重放分叉为止，中间没有任何迹象可看。初始化时调一次，把它变成一条点名的 Warn；
        /// EditMode 用例直接对这个方法断言，动作图加了新动作而没人接就当场变红。
        /// </para>
        /// <para>
        /// <b>不要挪到 <see cref="Sample"/> 里</b>：<c>FindActionMap</c> 要按字符串遍历地图与动作，
        /// 而 Sample 是每 tick 都要走的热路径。它只在初始化与测试里用，返回的列表不进热路径。
        /// </para>
        /// </summary>
        /// <param name="asset">动作集资产。为 null 时返回空列表——那种情况由调用方另外的 Warn 管。</param>
        /// <returns>没接进内核、也没被明确豁免的动作名（不带地图前缀），顺序同动作图里声明的顺序。</returns>
        public static List<string> FindUnmappedGameplayActions(InputActionAsset asset)
        {
            var unmapped = new List<string>();
            if (asset == null)
            {
                return unmapped;
            }

            InputActionMap map = asset.FindActionMap(GameplayMapName, false);
            if (map == null)
            {
                return unmapped;
            }

            var actions = map.actions;
            for (int i = 0; i < actions.Count; i++)
            {
                string name = actions[i].name;
                if (Contains(WiredActionNames, name) || Contains(ExemptActionNames, name))
                {
                    continue;
                }

                unmapped.Add(name);
            }

            return unmapped;
        }

        /// <summary>
        /// 把守卫的结果报成一条 Warn，一次列出全部漏接的动作。只在初始化时走一次，不进热路径。
        /// <para>用 Warn 不用 Error：Error 会被回放录制器的日志回调接住、变成一次自动保存——
        /// 而这只说明「动作图比内核多了一个动作」，不该顺手存一份现场。</para>
        /// </summary>
        private static void ReportUnmappedActions(InputActionAsset asset)
        {
            List<string> unmapped = FindUnmappedGameplayActions(asset);
            if (unmapped.Count == 0)
            {
                return;
            }

            Log.Warn(
                $"GameInput 的 Gameplay 动作图里有 {unmapped.Count} 个动作没接进确定性内核，采样时会静默丢："
                + string.Join("、", unmapped)
                + "。要么按 InputCommand 里现有位的样子补一个位、在 LiveInputSource.Sample 里接上并把名字加进 WiredActionNames，"
                + "要么明确它不进内核、把名字加进 LiveInputSource.ExemptActionNames 并写清理由。"
                + "两样都不做的话，这几路输入在录制与回放里就是缺的，而且不会报错。");
        }

        /// <summary>名字表里的线性查找。表只有十几个常量字符串，初始化时走一次，不上字典。</summary>
        private static bool Contains(string[] names, string value)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
