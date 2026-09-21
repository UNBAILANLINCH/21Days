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
        private bool pendingAttack;
        private bool pendingDisguise;
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
            view.Bind(Player, Enemy);

            playerInput.enabled = true;
            playerInput.ActivateInput();
            InputActionMap gameplay = playerInput.actions.FindActionMap("Gameplay", true);
            move = gameplay.FindAction("Move", true);
            sneak = gameplay.FindAction("Sneak", true);
            disguise = gameplay.FindAction("Disguise", true);
            attack = gameplay.FindAction("Attack", true);
        }

        private void FixedUpdate()
        {
            if (ManualSimulation) return;
            uint buttons = 0u;
            if (sneak.IsPressed()) buttons |= InputCommand.ButtonSneak;
            if (disguise.IsPressed() || pendingDisguise) buttons |= InputCommand.ButtonDisguise;
            if (attack.IsPressed() || pendingAttack) buttons |= InputCommand.ButtonAttack;
            pendingAttack = false;
            pendingDisguise = false;

            var command = new InputCommand(move.ReadValue<Vector2>(), Vector2.zero, buttons, Vector2.zero, 0);
            Simulate(in command, Time.fixedDeltaTime); // lint-ok: 独立场景原型以 FixedUpdate 作为唯一逻辑 tick，不参与正式回放
        }

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
        }

        private void OnDisable()
        {
            if (attack == null) return;
            attack.performed -= OnAttack;
            disguise.performed -= OnDisguise;
            pendingAttack = false;
            pendingDisguise = false;
        }

        private void OnAttack(InputAction.CallbackContext context) => pendingAttack = true;
        private void OnDisguise(InputAction.CallbackContext context) => pendingDisguise = true;

        private void OnDestroy()
        {
            if (step == null)
            {
                return;
            }

            step.End();
            view.Unbind();
        }
    }
}
