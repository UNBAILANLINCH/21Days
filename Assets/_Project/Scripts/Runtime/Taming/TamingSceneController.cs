// 职责：独立驯服验证场景的输入、生命周期与镜头适配。
// 为什么新建：不把尚未确定前置条件的驯服接入正式 EncounterStep；复用现有模型、视图与相机。
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.IsometricExploration;
using Game.Monster;
using Game.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Taming
{
    public sealed class TamingSceneController : MonoBehaviour
    {
        [SerializeField] private EncounterSceneView view;
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private PlayerConfig playerConfig;
        [SerializeField] private MonsterConfig monsterConfig;
        [SerializeField] private SmoothCameraFollow cameraFollow;

        private InputActionAsset actions;
        private InputAction move;
        private InputAction tame;
        private bool pendingToggle;
        public TamingRules Rules { get; private set; }
        public PlayerModel Player { get; private set; }
        public MonsterModel Enemy { get; private set; }
        public bool ManualSimulation { get; set; }

        private void Awake()
        {
            Player = new PlayerModel();
            Enemy = new MonsterModel();
            var player = new PlayerRules(playerConfig, Player, NullTelemetryScope.Instance);
            var enemy = new MonsterRules(monsterConfig, Enemy, new RandomService(21ul), NullTelemetryScope.Instance);
            player.Reset(view.PlayerStart);
            enemy.Reset(view.PatrolPositions());
            Rules = new TamingRules(player, enemy, NullTelemetryScope.Instance);
            view.Bind(Player, Enemy);
            actions = Instantiate(inputActions);
            move = actions.FindAction("Gameplay/Move", true);
            tame = actions.FindAction("Gameplay/Tame", true);
            tame.performed += OnToggle;
        }

        private void OnEnable() => actions.FindActionMap("Gameplay", true).Enable();

        private void OnDisable()
        {
            actions.Disable();
            pendingToggle = false;
        }

        private void OnToggle(InputAction.CallbackContext context) => pendingToggle = true;

        private void FixedUpdate()
        {
            if (ManualSimulation) return;
            bool toggle = pendingToggle;
            pendingToggle = false;
            Simulate(new TamingIntent(move.ReadValue<Vector2>(), toggle), Time.fixedDeltaTime); // lint-ok: 独立验证场景固定 tick，不接正式回放
            // 消费一次动作事件后释放边缘，避免短按被物理帧漏采。
            if (toggle) Simulate(new TamingIntent(Vector2.zero, false), 0f);
        }

        public void Simulate(in TamingIntent intent, float deltaTime)
        {
            Rules.Step(in intent, deltaTime);
            Transform target = Rules.IsControllingEnemy ? view.MonsterBody : view.PlayerBody;
            if (cameraFollow.Target != target) cameraFollow.SetTarget(target);
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(16, 110, 600, 30), Rules.IsControllingEnemy
                ? "当前控制：敌人 | WASD 移动 | T 返回玩家"
                : "当前控制：玩家 | WASD 移动 | T 驯服并控制敌人");
        }

        private void OnDestroy()
        {
            if (actions != null)
            {
                tame.performed -= OnToggle;
                Destroy(actions);
            }
            if (view != null) view.Unbind();
        }
    }
}
