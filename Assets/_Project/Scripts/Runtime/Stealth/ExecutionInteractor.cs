// 职责：背后处决的**交互层**——读 `Gameplay/Execute`（键鼠 `F` / 手柄 `South`），选最近的可处决目标，
//   算条件 ②（物种是否允许被处决），交给 ExecutionResolver 结算。
//
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：`DialogueInteractionFocus` / `SupplyCrateFocus` 读的是 Interact 且只认自己的焦点类型；
//      `MirrorInputPresenter` 是照镜的入口点。没有一个能把「按 F → 绕背处决」接起来。
//   2. 扩展不行：塞进 `ExecutionResolver` 会让结算认识 Input System 与场景（就没法在 EditMode 里直接 new 出来测）；
//      塞进 `MonsterRules` 会让怪物模块反向认识潜行语义与输入（PRP §2.3 明确把落点定在本模块）。
//   3. 新建：以上两条都不成立，故新建一个「输入 + 选目标」的薄壳，判定与结算都在被它调用的纯类型里。
//
// 为什么不是确定性内核的一部分（PRP §2.4，代价要记住）：
//   处决是**玩家实时输入**触发的交互，而 `EncounterStep` 的 tick 是确定性的。本组件因此做成
//   「输入 → 立即结算」，与 DialogueKeyboardInput / MirrorInputPresenter 同一层，**不进 InputCommand 位掩码**。
//   **代价：处决不可回放**——回放跑的是确定性 tick，喂不进实时按键，所以录像里看不到这一刀。
//   将来若要求回放能驱动处决：补一个 `InputCommand` 按钮位、把结算挪进 tick，**并在同一次改动里**
//   同步位断言测试；若同时改到快照的字节布局，还要升 `ReplayFormat.CurrentFormatVersion`。
//
// 与 `Interact` 共用 F / 手柄 South 的说明（2026-10-07 核对，**不是重复绑定写错**）：
//   `GameInput.inputactions` 里 `Interact` = E / **F** / South，本波新加的 `Execute` = **F** / South，
//   该动作图没有控制方案（`controlSchemes: []`），所以按 F 会**同时**让两个动作各收到一次按下。
//   这是**有意保留**的：两个动作各自有严格的目标门槛，互不重叠——
//     · `Interact` 只在「焦点非空」时生效（`DialogueInteractionFocus.Tick` / `SupplyCrateFocus.Tick`：
//       `Current != null` 才处理），焦点是最近的 NPC / 物资箱，且沉浸模式与对白中会清空；
//     · 本组件只在「背后 + 暗杀距离 + 目标未察觉 + 物种 defeat_method = 暗杀」四条同时成立时才杀人。
//   于是「站在怪物背后按 F」只有在**同一位置同时存在一个可交互 NPC/箱子**时才会两边都生效，
//   而那种布局在当前内容里不存在。**没有替策划改 `Interact` 的既有绑定**（F 是既有约定，不是本波加的），
//   改不改 F 的归属登记在交付报告的「待策划拍板」一节。
//
// 接线现状（2026-10-07「接进正式流程」那一波）：本组件此前**没有任何生产调用方**（只被测试与注释引用），
//   现在由两处接线，两处都走 `Configure`：
//     · 正式流程：`MonsterEncounterState.OnSceneReadyAsync` 在场景根里找本组件（与找 `EncounterSceneView`
//       同一模式），再由公开的 `MonsterEncounterState.BindExecution` 喂进玩家 / 怪物规则 / 事实写入点 /
//       判定内核 / 遭遇结算 / 埋点 / 动作资产；
//     · 独立原型场景（直接 Play SampleScene）：`StandaloneEncounterController.Awake` 喂它自己 new 的那套规则。
//       那条路**没有种类表**（`MonsterRules.Kind` 为 null），所以物种门槛恒拒——
//       按 F 只会得到 `SpeciesNotExecutable`。这是如实的结果，不是接线失败。
//   白盒可见性：`Inspect()` 是「此刻能不能下刀」的**无副作用**查询（由 `EncounterSceneView` 的状态面板显示），
//   `LastVerdict` 记最近一次按 F 的结果。两者都只作观测，不参与判定、不改任何状态。
//
// 出处（真源）：
//   - `docs/design/spotlight/06_怪物状态与交互设计文档.md:69`「对于部分怪物。玩家可以在怪物背后按F处决」
//   - `PRP/stealth-execution/prp.md` §2.2（输入）/ §2.3（落点与三件事）/ §2.4（为什么不进内核）
using System;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Stealth
{
    /// <summary>
    /// 「此刻能不能下刀」的一次只读快照（<see cref="ExecutionInteractor.Inspect"/> 的返回值）。
    /// <para>
    /// 存在的理由：判定结果此前**没有任何 UI 消费方**，玩家无从知道什么时候能按 F。本类型只描述状态，
    /// 不写事实、不埋点、不改任何规则对象——`stealth.assassinated` 仍然只在真的处决成功之后才写
    /// （能力 ≠ 事实，见 <see cref="ExecutionVerdict.FactKey"/> 的说明）。
    /// </para>
    /// </summary>
    public readonly struct ExecutionHint
    {
        /// <summary>没接线时的界面文字。放在这里是为了让面板初始化与 <see cref="ToLabel"/> 用同一份，
        /// 不在第二处再写一遍中文。</summary>
        public const string UnboundLabel = "处决：未接线";

        public ExecutionHint(bool configured, bool hasTarget, ExecutionVerdict verdict)
        {
            Configured = configured;
            HasTarget = hasTarget;
            Verdict = verdict;
        }

        /// <summary>组件是否已经接线（没接线时按键也不响应，界面要如实说这一条）。</summary>
        public bool Configured { get; }

        /// <summary>附近有没有候选目标（有 = <see cref="Reject"/> 说的是「离他最近这只为什么不行」）。</summary>
        public bool HasTarget { get; }

        /// <summary>这一次的判定结果（<see cref="HasTarget"/> 为假时 <see cref="ExecutionReject.NoTarget"/>）。</summary>
        public ExecutionVerdict Verdict { get; }

        /// <summary>此刻能不能下刀。</summary>
        public bool Allowed => Verdict.Allowed;

        /// <summary>不能下刀的原因；能下刀时是 <see cref="ExecutionReject.None"/>。</summary>
        public ExecutionReject Reject => Verdict.Reject;

        /// <summary>白盒面板用的一句话（中文）。</summary>
        public string ToLabel()
        {
            if (!Configured)
            {
                return UnboundLabel;
            }

            if (Allowed)
            {
                return "处决：可处决（按 F）";
            }

            return HasTarget
                ? "处决：不可（" + ExecutionRules.Describe(Reject) + "）"
                : "处决：附近没有目标";
        }
    }

    /// <summary>
    /// 背后处决的交互入口点。挂在玩家身上（或场景里任一常驻物体），接上玩家、目标与事实写入点即可用。
    /// <para>
    /// 场景接线两步：① Inspector 里把 <c>Data/Input/GameInput.inputactions</c> 拖到 <c>Input Actions</c>；
    /// ② 由接线方调 <see cref="Configure"/>（玩家模型 / 目标 / 事实写入点都是运行时对象，Inspector 拖不了）。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExecutionInteractor : MonoBehaviour
    {
        [Tooltip("GameInput.inputactions。本组件自己 Instantiate 一份并只启停 Gameplay/Execute，不抢别的组件的动作图状态。")]
        [SerializeField] private InputActionAsset inputActions;

        private InputActionAsset ownedActions;
        private InputAction execute;
        private AssassinationRules assassination;
        private PlayerModel player;
        private MonsterRules[] targets = Array.Empty<MonsterRules>();
        private ExecutionResolver resolver;

        /// <summary>是否已经接好线（没接线时按 F 什么都不做，也不埋点）。</summary>
        public bool IsConfigured => resolver != null;

        /// <summary>结算器；接线后可订阅它的 <see cref="ExecutionResolver.OnExecuted"/> 播被处决动画。</summary>
        public ExecutionResolver Resolver => resolver;

        /// <summary>
        /// 最近一次 <see cref="TryExecute"/> 的结果；<c>null</c> = **还没按过**（不是「被拒」）。
        /// <para>
        /// **只作观测用**（白盒面板、回放断言「按 F 到底走到哪一步了」）。它不参与判定，也不是持久事实——
        /// 「已经用暗杀解决过目标」的唯一痕迹是 `stealth.assassinated`（命中那一刻才写）。
        /// 用可空类型而不是让默认值兜底：`default(ExecutionVerdict)` 是 `Allowed = false, Reject = None`，
        /// 那读起来像「通过了却被拒」，会把「没按过」和「按了的结果」混成一件事。
        /// </para>
        /// <para>
        /// 没接线时的早退**不算一次尝试**（那条路连埋点都不写），所以它不会改写这个值。
        /// 没接输入资产、只是被测试或别处直接调 <see cref="TryExecute"/> 时照记。
        /// </para>
        /// </summary>
        public ExecutionVerdict? LastVerdict { get; private set; }

        /// <summary>
        /// 接线：玩家、目标、事实写入点，外加可选的判定内核 / 遭遇结算 / 埋点 / 动作资产。
        /// <para>
        /// 单目标重载：当前遭遇只有一只怪（<c>EncounterStep</c> 持一只 <c>MonsterRules</c>），
        /// 多怪时用数组重载，选目标规则见 <see cref="TryExecute"/>。
        /// </para>
        /// </summary>
        public void Configure(PlayerModel playerModel, MonsterRules target, IStealthFactSink factSink,
            AssassinationRules rules = null, EncounterStep encounter = null, ITelemetryScope telemetry = null,
            InputActionAsset actions = null)
        {
            Configure(playerModel, target == null ? Array.Empty<MonsterRules>() : new[] { target }, factSink,
                rules, encounter, telemetry, actions);
        }

        /// <summary>接线（多目标重载）。</summary>
        /// <param name="actions">
        /// 动作资产；为 null 时用 Inspector 上那个 <c>Input Actions</c> 字段。**代码接线（Showcase / 原型场景）
        /// 传这个参数**，就不必去改场景里组件的序列化字段。
        /// </param>
        public void Configure(PlayerModel playerModel, MonsterRules[] monsterTargets, IStealthFactSink factSink,
            AssassinationRules rules = null, EncounterStep encounter = null, ITelemetryScope telemetry = null,
            InputActionAsset actions = null)
        {
            player = playerModel ?? throw new ArgumentNullException(nameof(playerModel));
            if (factSink == null) throw new ArgumentNullException(nameof(factSink));
            targets = monsterTargets ?? Array.Empty<MonsterRules>();
            assassination = rules;
            if (assassination == null)
            {
                // 没接 StealthKernel 时按占位阈值跑（与 EncounterStep 未接线时的行为一致），**但一定要留痕**：
                // 否则策划改 StealthConfig.asset 不生效，而现场看不出是「没接线」还是「参数就是这样」。
                assassination = new AssassinationRules(AssassinationSettings.PlaceholderDefault);
                Log.Warn("ExecutionInteractor：没有接 AssassinationRules（StealthKernel），"
                         + "按占位阈值判定（背后 120°、距离 1.2、不要求潜行）。", this);
            }

            resolver = new ExecutionResolver(assassination, factSink, encounter, telemetry);
            BindInput(actions == null ? inputActions : actions);
        }

        /// <summary>换目标（遭遇重开 / 场景切换时由接线方调）；传 null 或空表示当前没有可处决目标。</summary>
        public void SetTargets(MonsterRules[] monsterTargets) => targets = monsterTargets ?? Array.Empty<MonsterRules>();

        /// <summary>
        /// 找最近的可处决目标并结算一次（不读输入，供按键路径、测试与手动触发共用）。
        /// <para>
        /// 选择规则：遍历全部候选，取**最近的、判定允许的**那一个执行；都不允许时，
        /// 用**最近的那个候选**的拒绝原因埋点（玩家想知道的是离他最近这只为什么不行）。
        /// 一个候选都没有时走 <see cref="ExecutionResolver.RejectNoTarget"/>——「对着空气按 F」
        /// 也要留一条 <c>stealth_execute_rejected</c>，否则它和「功能坏了」在现场分不开。
        /// </para>
        /// </summary>
        public ExecutionVerdict TryExecute()
        {
            if (resolver == null || player == null)
            {
                // 没接线：静默返回（连埋点都不写——埋点作用域也是接线的一部分）。这是「组件还没接好」，不是玩法拒绝。
                return new ExecutionVerdict(false, ExecutionReject.NoTarget);
            }

            if (targets.Length == 0)
            {
                return Remember(resolver.RejectNoTarget());
            }

            Candidate pick = Select(player.Snapshot);

            // 有允许的 → 对最近的那个下手；一个都不允许 → 把最近那只的拒绝原因交给结算器埋点
            //（「玩家按了没反应」时最想知道的就是离他最近这只为什么不行）。
            // 两处都要先把属性落成局部：结算的入参是 `in`，属性不能按引用传（CS8156）。
            if (pick.AllowedTarget != null)
            {
                ExecutionInput allowed = pick.AllowedInput;
                return Remember(resolver.TryExecute(in allowed, pick.AllowedTarget));
            }

            if (pick.NearestTarget == null)
            {
                return Remember(resolver.RejectNoTarget());
            }

            ExecutionInput nearest = pick.NearestInput;
            return Remember(resolver.TryExecute(in nearest, pick.NearestTarget));
        }

        /// <summary>
        /// **只读查询**：此刻最近候选能不能下刀、不能的话差在哪一条。白盒面板与回放用它看状态。
        /// <para>
        /// 与 <see cref="TryExecute"/> 共用同一套选目标规则（<see cref="Select"/>），所以「面板说能下刀」
        /// 与「按 F 真的下刀」不会各算一套。**它什么都不改**：不写事实、不埋点、不动 <see cref="LastVerdict"/>、
        /// 不结算战斗结果。判定是纯函数，每次查询重算一遍（几次浮点比较），不值得为它加缓存。
        /// </para>
        /// </summary>
        public ExecutionHint Inspect()
        {
            var noTarget = new ExecutionVerdict(false, ExecutionReject.NoTarget);
            if (resolver == null || player == null)
            {
                return new ExecutionHint(false, false, noTarget);
            }

            if (targets.Length == 0)
            {
                return new ExecutionHint(true, false, noTarget);
            }

            Candidate pick = Select(player.Snapshot);
            if (pick.AllowedTarget != null)
            {
                // 判定吃的是 `in` 参数，而 Candidate 的成员是只读属性（属性不能按引用传），先落到局部。
                ExecutionInput allowed = pick.AllowedInput;
                return new ExecutionHint(true, true, ExecutionRules.Evaluate(in allowed, assassination));
            }

            if (pick.NearestTarget == null)
            {
                return new ExecutionHint(true, false, noTarget);
            }

            // 最近的那只不允许：把它的拒绝原因交出去（玩家最想知道的就是这一条）。
            ExecutionInput nearest = pick.NearestInput;
            return new ExecutionHint(true, true, ExecutionRules.Evaluate(in nearest, assassination));
        }

        /// <summary>一次选目标的结果：允许下刀的最近一个 + 无论允不允许都最近的那一个。</summary>
        private readonly struct Candidate
        {
            public Candidate(MonsterRules allowedTarget, in ExecutionInput allowedInput,
                MonsterRules nearestTarget, in ExecutionInput nearestInput)
            {
                AllowedTarget = allowedTarget;
                AllowedInput = allowedInput;
                NearestTarget = nearestTarget;
                NearestInput = nearestInput;
            }

            /// <summary>最近的、判定允许的目标；没有就是 null。</summary>
            public MonsterRules AllowedTarget { get; }

            /// <summary>上一个目标对应的判定输入。</summary>
            public ExecutionInput AllowedInput { get; }

            /// <summary>最近的目标（不管允不允许）；一个候选都没有时是 null。</summary>
            public MonsterRules NearestTarget { get; }

            /// <summary>最近那个目标对应的判定输入。</summary>
            public ExecutionInput NearestInput { get; }
        }

        // 选目标：遍历全部候选，取**最近的、判定允许的**那一个；都不允许时用**最近的那只**的拒绝原因。
        // TryExecute 与 Inspect 共用本方法，保证「面板说能下刀」与「按 F 真的下刀」永远是同一条规则。
        private Candidate Select(in PlayerSnapshot attacker)
        {
            MonsterRules nearestAllowed = null;
            ExecutionInput nearestAllowedInput = default;
            float nearestAllowedDistance = float.MaxValue;
            MonsterRules nearestAny = null;
            ExecutionInput nearestAnyInput = default;
            float nearestAnyDistance = float.MaxValue;

            for (int i = 0; i < targets.Length; i++)
            {
                MonsterRules target = targets[i];
                if (target == null)
                {
                    continue;
                }

                ExecutionInput input = BuildInput(in attacker, target);
                float distance = GameMath.Distance(attacker.Position, target.Model.Position);
                if (distance < nearestAnyDistance)
                {
                    nearestAnyDistance = distance;
                    nearestAny = target;
                    nearestAnyInput = input;
                }

                if (ExecutionRules.Evaluate(in input, assassination).Allowed && distance < nearestAllowedDistance)
                {
                    nearestAllowedDistance = distance;
                    nearestAllowed = target;
                    nearestAllowedInput = input;
                }
            }

            return new Candidate(nearestAllowed, in nearestAllowedInput, nearestAny, in nearestAnyInput);
        }

        // 记录最近一次的真实结果（观测用），并把同一个 verdict 原样返回给调用方。
        private ExecutionVerdict Remember(ExecutionVerdict verdict)
        {
            LastVerdict = verdict;
            return verdict;
        }

        private void OnEnable()
        {
            if (execute != null)
            {
                execute.Enable();
            }
        }

        private void OnDisable()
        {
            if (execute != null)
            {
                execute.Disable();
            }
        }

        private void Update()
        {
            // 没接输入资产 = 不响应按键（组件可以被手动 TryExecute 驱动，见 Showcase）。
            if (execute == null || !execute.WasPressedThisFrame())
            {
                return;
            }

            TryExecute();
        }

        private void OnDestroy()
        {
            // 自己 Instantiate 的那份要自己销毁，否则动作资产随场景切换泄漏。
            if (ownedActions != null)
            {
                Destroy(ownedActions);
                ownedActions = null;
            }

            execute = null;
        }

        // 组装一次判定的全部门槛输入：条件 ①（位置 / 朝向 / 存活 / 察觉 / 潜行）+ 条件 ②（物种）。
        private ExecutionInput BuildInput(in PlayerSnapshot attacker, MonsterRules target)
        {
            MonsterModel model = target.Model;
            var snapshot = new AssassinationInput(
                attacker.Position, attacker.Facing, model.Position, model.Facing,
                model.Health > 0, IsAware(model.Mode), attacker.IsSneaking);
            return new ExecutionInput(in snapshot, ExecutionRules.SpeciesExecutable(DefeatMethodOf(target)));
        }

        // 「察觉」的口径与 EncounterStep.SettleStealth 里算 stealth.behind 时用的那条**必须一致**：
        // PatrolWalk / PatrolPause = 未察觉，其余（Alert / Hostile / Dead）= 已察觉。
        // 两处各写一份是历史遗留（那处在 EncounterStep 的 tick 里，本波不动它的内部）；
        // 改这个口径时两处一起改，否则「HUD 说能下刀、按 F 说目标已察觉」。
        private static bool IsAware(MonsterMode mode) =>
            mode != MonsterMode.PatrolWalk && mode != MonsterMode.PatrolPause;

        private static string DefeatMethodOf(MonsterRules target) =>
            target.Kind == null ? null : target.Kind.DefeatMethod;

        // 复制一份动作资产再启用 Gameplay/Execute：与 TamingSceneController 同一做法。
        // 不直接动 GameInput.inputactions 的实例状态，避免和 InputService / 别的入口点抢「谁 Enable 了动作图」。
        private void BindInput(InputActionAsset asset)
        {
            if (ownedActions != null)
            {
                Destroy(ownedActions);
                ownedActions = null;
                execute = null;
            }

            // InputActionAsset 是 UnityEngine.Object，判空只用 == null。
            if (asset == null)
            {
                return;
            }

            ownedActions = Instantiate(asset);
            execute = ownedActions.FindAction("Gameplay/Execute", throwIfNotFound: false);
            if (execute == null)
            {
                // 动作不存在 = GameInput.inputactions 还没重新生成 / 被改回去了。点名说清楚，别静默失灵。
                Log.Error("ExecutionInteractor：动作 Gameplay/Execute 不在传进来的动作资产里。"
                          + "检查 Assets/_Project/Data/Input/GameInput.inputactions 的 Gameplay 图里有没有 Execute，"
                          + "改完等 Unity 重新生成 GameInput.cs。", this);
                Destroy(ownedActions);
                ownedActions = null;
                return;
            }

            if (isActiveAndEnabled)
            {
                execute.Enable();
            }
        }
    }
}
