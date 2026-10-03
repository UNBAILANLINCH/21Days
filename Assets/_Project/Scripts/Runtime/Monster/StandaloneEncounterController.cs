// 职责：直接播放遭遇原型场景时，用场景内输入驱动现有 Player/Monster 规则循环。
// 为什么新建：Boot 的 SimulationRunner 只在完整游戏流程存在；EncounterSceneView 只负责表现，不应兼任输入与规则调度。
using Game.Core.Boot;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Player;
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
        private InputAction move;
        private InputAction sneak;
        private InputAction disguise;
        private InputAction attack;
        private InputAction run;
        private InputAction tame;
        private bool pendingTame;
        private int pendingSelection;
        private uint pendingSelectionButton;
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
            view.ConfigureTaming(step);
            step.Begin(view.PlayerStart, view.PatrolPositions());
            view.Bind(Player, Enemy, ReadInterpolationAlpha);
            view.BindControl(step, RequestControl);
            view.OnPlayerBlocked += step.CorrectPlayerPosition;

            playerInput.enabled = true;
            playerInput.ActivateInput();
            InputActionMap gameplay = playerInput.actions.FindActionMap("Gameplay", true);
            move = gameplay.FindAction("Move", true);
            sneak = gameplay.FindAction("Sneak", true);
            disguise = gameplay.FindAction("Disguise", true);
            attack = gameplay.FindAction("Attack", true);
            run = gameplay.FindAction("Run", true);
            tame = gameplay.FindAction("Tame", true);
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
            if (tame.IsPressed() || pendingTame) buttons |= InputCommand.ButtonTame;
            if (pendingTame || (pendingSelectionButton & InputCommand.ButtonTame) != 0) buttons |= InputCommand.ButtonTamePressed;
            buttons |= pendingSelectionButton;
            pendingAttack = false;
            pendingDisguise = false;
            pendingRun = false;

            var command = new InputCommand(move.ReadValue<Vector2>(), new Vector2(pendingSelection, 0f), buttons, Vector2.zero, 0);
            pendingTame = false;
            pendingSelection = 0;
            pendingSelectionButton = 0;
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
            tame.performed += OnTame;
        }

        private void OnDisable()
        {
            if (attack == null) return;
            attack.performed -= OnAttack;
            disguise.performed -= OnDisguise;
            run.performed -= OnRun;
            tame.performed -= OnTame;
            pendingAttack = false;
            pendingDisguise = false;
            pendingRun = false;
            pendingTame = false;
            pendingSelection = 0;
            pendingSelectionButton = 0;
        }

        private void OnAttack(InputAction.CallbackContext context) => pendingAttack = true;
        private void OnDisguise(InputAction.CallbackContext context) => pendingDisguise = true;
        private void OnRun(InputAction.CallbackContext context) => pendingRun = true;
        private void OnTame(InputAction.CallbackContext context) => pendingTame = true;
        private void RequestControl(int slot, bool tameTarget)
        {
            pendingSelection = slot;
            pendingSelectionButton = tameTarget ? InputCommand.ButtonTame : InputCommand.ButtonSelectControl;
        }

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
