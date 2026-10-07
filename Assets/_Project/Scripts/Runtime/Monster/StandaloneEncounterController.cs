// 职责：直接播放遭遇原型场景时，用场景内输入驱动现有 Player/Monster 规则循环。
// 为什么新建：Boot 的 SimulationRunner 只在完整游戏流程存在；EncounterSceneView 只负责表现，不应兼任输入与规则调度。
// 背后处决（2026-10-07）：这条路也接上 ExecutionInteractor——喂本场景自己 new 的那套规则。
//   **它没有种类表**（MonsterRules.Kind 为 null），所以物种门槛（defeat_method == 暗杀）恒拒：
//   按 F 只会得到 SpeciesNotExecutable。判定内核也没有（本场景没有容器）→ 组件按占位阈值跑并打一条 Warn。
//   两条都是如实结果，不是接线失败。正式流程那条路见 MonsterEncounterState.BindExecution。
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Player;
using Game.Stealth;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Monster
{
    public sealed class StandaloneEncounterController : MonoBehaviour
    {
        [SerializeField] private EncounterSceneView view;
        [SerializeField] private PlayerInput playerInput;
        [SerializeField] private PlayerConfig playerConfig;
        [SerializeField] private MonsterConfig monsterConfig;

        private EncounterStep step;
        private RandomService random;
        private ExecutionInteractor execution;
        private InputAction move;
        private InputAction sneak;
        private InputAction disguise;
        private InputAction attack;
        private InputAction run;
        private bool pendingAttack;
        private bool pendingDisguise;
        private bool pendingRun;
        private long tick;
        public PlayerModel Player { get; private set; }
        public MonsterModel Enemy { get; private set; }
        public bool ManualSimulation { get; set; }

        private void Awake()
        {
            if (FindObjectOfType<GameBootstrap>() != null)
            {
                playerInput.enabled = false;
                enabled = false;
                return;
            }

            Player = new PlayerModel();
            Enemy = new MonsterModel();
            random = new RandomService(21ul);
            var playerRules = new PlayerRules(playerConfig, Player, NullTelemetryScope.Instance);
            var monsterRules = new MonsterRules(monsterConfig, Enemy, random, NullTelemetryScope.Instance);
            step = new EncounterStep(playerRules, monsterRules);
            step.Begin(view.PlayerStart, view.PatrolPositions());

            // 背后处决：本场景自己那套规则（没有种类表、没有内核，理由见文件头）。
            // 动作资产用 PlayerInput 上那一份——与正式流程从 IInputService 拿是同一个来源。
            execution = FindObjectOfType<ExecutionInteractor>(true); // lint-ok: 只在 Awake 找一次并缓存，不在每帧路径上
            if (execution != null)
            {
                execution.Configure(Player, monsterRules, step.FactSink, null, step, NullTelemetryScope.Instance,
                    playerInput.actions);
            }
            else
            {
                // 面板第三行也会写「处决：未接线」，但那条只在 Game 视图里看得见——控制台里也该留一句。
                Log.Warn("独立遭遇场景里没有 ExecutionInteractor：按 F 不会处决。"
                         + "照 SampleScene 的接法挂在 Encounter 物体上。", this);
            }

            view.Bind(Player, Enemy, ReadInterpolationAlpha, execution);
            view.OnPlayerBlocked += step.CorrectPlayerPosition;

            playerInput.enabled = true;
            playerInput.ActivateInput();
            InputActionMap gameplay = playerInput.actions.FindActionMap("Gameplay", true);
            move = gameplay.FindAction("Move", true);
            sneak = gameplay.FindAction("Sneak", true);
            disguise = gameplay.FindAction("Disguise", true);
            attack = gameplay.FindAction("Attack", true);
            run = gameplay.FindAction("Run", true);
        }

        private void FixedUpdate()
        {
            if (ManualSimulation) return;
            uint buttons = 0u;
            if (sneak.IsPressed()) buttons |= InputCommand.ButtonSneak;
            if (disguise.IsPressed() || pendingDisguise) buttons |= InputCommand.ButtonDisguise;
            if (attack.IsPressed() || pendingAttack) buttons |= InputCommand.ButtonAttack;
            // 走 / 跑是按下沿切换，短按同样可能落在两个物理帧之间，照伪装的做法缓存一次按下。
            if (run.IsPressed() || pendingRun) buttons |= InputCommand.ButtonRun;
            pendingAttack = false;
            pendingDisguise = false;
            pendingRun = false;

            var command = new InputCommand(move.ReadValue<Vector2>(), Vector2.zero, buttons, Vector2.zero, 0);
            Simulate(in command, Time.fixedDeltaTime); // lint-ok: 独立场景原型以 FixedUpdate 作为唯一逻辑 tick，不参与正式回放
        }

        // 独立场景以 FixedUpdate 为逻辑 tick：LateUpdate 时「当前时间 − 最近一次固定步时间」就是未满一步的余量。
        // 时停（timeScale = 0）两者都不走，alpha 恒定；Showcase 手动单步（ManualSimulation）时不插值，直接显示当前 tick。
        private float ReadInterpolationAlpha() => ManualSimulation
            ? 1f
            : EncounterProjection.InterpolationAlpha(Time.time - Time.fixedTime, Time.fixedDeltaTime); // lint-ok: 表现层插值相位，只影响渲染位置，不进逻辑

        public void Simulate(in InputCommand command, float deltaTime)
        {
            var context = new SimulationContext(tick++, deltaTime, in command, random);
            step.Step(in context);
        }

        private void OnEnable()
        {
            if (attack == null) return;
            attack.performed += OnAttack;
            disguise.performed += OnDisguise;
            run.performed += OnRun;
        }

        private void OnDisable()
        {
            if (attack == null) return;
            attack.performed -= OnAttack;
            disguise.performed -= OnDisguise;
            run.performed -= OnRun;
            pendingAttack = false;
            pendingDisguise = false;
            pendingRun = false;
        }

        private void OnAttack(InputAction.CallbackContext context) => pendingAttack = true;
        private void OnDisguise(InputAction.CallbackContext context) => pendingDisguise = true;
        private void OnRun(InputAction.CallbackContext context) => pendingRun = true;

        private void OnDestroy()
        {
            if (step == null)
            {
                return;
            }

            step.End();
            view.OnPlayerBlocked -= step.CorrectPlayerPosition;
            view.Unbind();
        }
    }
}
