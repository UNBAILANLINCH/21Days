// 职责：场景中的巡逻点和占位视觉；把逻辑位置投影到场景（XZ 模式可贴地爬台阶）、状态色与朝向翻转；规则数据仍由 PlayerModel / MonsterModel 持有。
// 渲染位置在两逻辑 tick 之间插值（Lerp(PreviousPosition, Position, alpha)），alpha 由 Bind 的调用方给；不给时 alpha = 1，行为同旧版。
// 为什么新建：SampleView 是示例商品面板，现有场景中没有角色表现组件可复用。
using System;
using Game.Player;
using UnityEngine;

namespace Game.Monster
{
    public sealed class EncounterSceneView : MonoBehaviour
    {
        private const float MinFlipDelta = 0.0001f;
        // 碰撞回写分轴判定的容差：扫掠只在浮点舍入层面偏离期望值时不算被挡，避免把没被挡的轴拉回插值点。
        private const float BlockedAxisTolerance = 0.0001f;
        // 波 12：调试面板挪到左下角像素坐标（不随画布缩放），让出右上角给沉浸 / 重置按钮与对话三键。
        private const float DebugPanelMargin = 16f;
        private const float DebugPanelWidth = 480f;
        private const float BackButtonWidth = 114f;
        private const float BackButtonHeight = 40f;
        private const float LineHeight = 28f;
        private const float AlertBarWidth = 208f;
        private const float AlertBarOuterHeight = 20f;
        private const float AlertBarInnerHeight = 12f;
        private const float AlertBarPadding = 4f;
        private const float AlertBarGapAboveLines = 24f;

        [SerializeField] private Transform playerSpawn;
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private bool useXZPlane;
        [SerializeField] private Transform playerBody;
        [SerializeField] private Transform monsterBody;
        [SerializeField] private SpriteRenderer playerSprite;
        [SerializeField] private SpriteRenderer monsterSprite;

        [Tooltip("贴地射线只打这些层；为 0 时不贴地，XZ 模式保留当前高度")]
        [SerializeField] private LayerMask groundMask;
        [Tooltip("贴地射线从当前 Y 往上多少开始打")]
        [SerializeField] private float groundProbeHeight = 2f;
        [Tooltip("贴地射线从当前 Y 往下最多打多远")]
        [SerializeField] private float groundProbeDepth = 4f;
        [Tooltip("单帧允许抬升的最大高度；超过视为墙顶或家具，保持原高度。随关卡台阶高度调；灰盒每级 0.3")]
        [SerializeField] private float maxStepHeight = 0.32f;
        [Tooltip("玩家状态色染在这个 Renderer 上；为空时染玩家本体")]
        [SerializeField] private SpriteRenderer playerStateIndicator;
        [Tooltip("怪物状态色染在这个 Renderer 上；为空时染怪物本体")]
        [SerializeField] private SpriteRenderer monsterStateIndicator;
        [Tooltip("按场景 X 方向的移动翻转角色纸片：左移 flipX，右移还原。只在 XZ 等距场景勾选；2D 验证场景保持关闭")]
        [SerializeField] private bool flipByMoveDirection;

        // —— PRP/exploration-whitebox 波 9：白盒遮挡碰撞（表现层解算、回写逻辑位置；取舍见 EncounterCollision 文件头）。
        [Tooltip("玩家纸片会被这些层的碰撞体挡住（先 X 后 Z 胶囊扫掠，贴墙滑动）；为 0 时不碰撞，行为与旧版一致。只在 XZ 模式生效")]
        [SerializeField] private LayerMask obstacleMask;
        [Tooltip("碰撞胶囊下沿离脚底的高度；要高于单级台阶（灰盒 0.3），否则台阶和坡面会被当成墙")]
        [SerializeField, Min(0f)] private float obstacleBottomOffset = 0.35f;
        [Tooltip("碰撞胶囊上沿离脚底的高度；低于它的桥底 / 甲板底不挡人")]
        [SerializeField, Min(0f)] private float obstacleTopOffset = 1.5f;
        [Tooltip("碰撞胶囊半径，与 player 根节点 CapsuleCollider 一致")]
        [SerializeField, Min(0f)] private float obstacleRadius = 0.3f;
        [Tooltip("单帧场景位移超过这个距离视为瞬移（读档 / 重置 / 回放挪位），不做碰撞解算，只贴地；两 tick 逻辑位置相距超过它时也不插值")]
        [SerializeField, Min(0f)] private float obstacleTeleportDistance = 1.5f;

        private PlayerModel player;
        private MonsterModel monster;
        private Func<float> interpolationAlpha;
        private Sprite placeholderSprite;
        private string playerStatus;
        private string monsterStatus;
        private int lastPlayerHealth = -1;
        private int lastMonsterHealth = -1;
        private MonsterMode lastMode = (MonsterMode)255;
        private bool lastSneaking;
        private bool lastDisguised;

        public event Action OnBackClicked;

        /// <summary>
        /// 玩家这一帧被遮挡物挡住时发出，参数是修正后的逻辑 XY；由持有 EncounterStep 的一方回写（CorrectPlayerPosition）。
        /// 分轴合成：被挡的轴取插值点扫掠后的修正值，没被挡的轴原样是当前逻辑值（不往回拉到插值点）。
        /// </summary>
        public event Action<Vector2> OnPlayerBlocked;

        public Transform PlayerBody => playerBody;
        public Transform MonsterBody => monsterBody;
        public Vector3 PlayerScenePosition => playerBody == null ? Vector3.zero : playerBody.position;

        public Vector2 PlayerStart => playerSpawn == null ? Vector2.zero : ToLogicPosition(playerSpawn.position);

        public void ConfigureXZ(Transform spawn, Transform[] points, Transform playerVisual,
            SpriteRenderer playerRenderer, Transform monsterVisual, SpriteRenderer monsterRenderer)
        {
            playerSpawn = spawn != null ? spawn : throw new ArgumentNullException(nameof(spawn));
            patrolPoints = points != null && points.Length > 0
                ? points : throw new ArgumentException("至少需要一个巡逻点", nameof(points));
            playerBody = playerVisual != null ? playerVisual : throw new ArgumentNullException(nameof(playerVisual));
            monsterBody = monsterVisual != null ? monsterVisual : throw new ArgumentNullException(nameof(monsterVisual));
            playerSprite = playerRenderer;
            monsterSprite = monsterRenderer;
            useXZPlane = true;
        }

        public Vector2[] PatrolPositions()
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                throw new InvalidOperationException("EncounterSceneView 至少要拖一个 Patrol Point");
            }

            var result = new Vector2[patrolPoints.Length];
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null)
                {
                    throw new InvalidOperationException($"Patrol Points 第 {i} 个引用为空");
                }

                result[i] = ToLogicPosition(patrolPoints[i].position);
            }

            return result;
        }

        /// <summary>
        /// 绑定要显示的模型。<paramref name="alphaSource"/> 每个渲染帧取一次两 tick 之间的插值比例 [0, 1]
        /// （正式流程读 SimulationRunner.Accumulator，独立场景读 FixedUpdate 相位）；为空时按 1，直接显示当前 tick 位置。
        /// </summary>
        public void Bind(PlayerModel playerModel, MonsterModel monsterModel, Func<float> alphaSource = null)
        {
            player = playerModel;
            monster = monsterModel;
            interpolationAlpha = alphaSource;
            EnsureBodies();
            EnsureCamera();
        }

        public void Unbind()
        {
            player = null;
            monster = null;
            interpolationAlpha = null;
        }

        private void EnsureBodies()
        {
            if (playerBody != null && playerSprite == null)
            {
                playerSprite = playerBody.GetComponentInChildren<SpriteRenderer>();
            }

            if (monsterBody != null && monsterSprite == null)
            {
                monsterSprite = monsterBody.GetComponentInChildren<SpriteRenderer>();
            }

            if (playerBody != null && playerSprite != null && monsterBody != null && monsterSprite != null)
            {
                EnsureSprite(playerSprite);
                EnsureSprite(monsterSprite);
                return;
            }

            EnsurePlaceholderSprite();
            if (playerBody == null || playerSprite == null)
            {
                playerSprite = CreateBody("Player Placeholder", new Color(0.2f, 0.55f, 1f));
                playerBody = playerSprite.transform;
            }

            if (monsterBody == null || monsterSprite == null)
            {
                monsterSprite = CreateBody("Monster Placeholder", Color.gray);
                monsterBody = monsterSprite.transform;
            }
        }

        private void EnsureSprite(SpriteRenderer renderer)
        {
            if (renderer.sprite != null)
            {
                return;
            }

            EnsurePlaceholderSprite();
            renderer.sprite = placeholderSprite;
        }

        private void EnsurePlaceholderSprite()
        {
            if (placeholderSprite == null)
            {
                placeholderSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                    new Vector2(0.5f, 0.5f), 1f);
            }
        }

        private SpriteRenderer CreateBody(string objectName, Color color)
        {
            var body = new GameObject(objectName);
            body.transform.SetParent(transform, false);
            SpriteRenderer renderer = body.AddComponent<SpriteRenderer>();
            renderer.sprite = placeholderSprite;
            renderer.color = color;
            return renderer;
        }

        private static void EnsureCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var cameraObject = new GameObject("Encounter Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7f;
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        }

        private void LateUpdate()
        {
            if (player == null || monster == null)
            {
                return;
            }

            // 逻辑位置只在固定 tick 里跳变；按余量比例在上一 tick 与当前 tick 之间插值，每个渲染帧的位移才连续。
            // 时停 / 暂停时余量不变，alpha 恒定，画面静止不抖。
            float alpha = interpolationAlpha == null ? 1f : interpolationAlpha();
            Vector2 playerLogic = Interpolate(player.PreviousPosition, player.Position, alpha);
            Vector2 monsterLogic = Interpolate(monster.PreviousPosition, monster.Position, alpha);
            Vector3 lastPlayerScene = playerBody.position;
            Vector3 lastMonsterScene = monsterBody.position;
            playerBody.position = ResolvePlayerScenePosition(playerLogic, lastPlayerScene);
            monsterBody.position = ToScenePosition(monsterLogic, lastMonsterScene);
            if (flipByMoveDirection)
            {
                ApplyFlip(playerSprite, lastPlayerScene.x, playerBody.position.x);
                ApplyFlip(monsterSprite, lastMonsterScene.x, monsterBody.position.x);
            }

            SpriteRenderer playerTint = playerStateIndicator != null ? playerStateIndicator : playerSprite;
            SpriteRenderer monsterTint = monsterStateIndicator != null ? monsterStateIndicator : monsterSprite;
            playerTint.color = player.Health <= 0 ? Color.black
                : player.IsDisguised ? Color.green
                : player.IsSneaking ? Color.cyan : new Color(0.2f, 0.55f, 1f);
            monsterTint.color = monster.Mode == MonsterMode.Dead ? Color.black
                : monster.Mode == MonsterMode.Hostile ? Color.red
                : monster.Mode == MonsterMode.Alert ? new Color(1f, 0.55f, 0f) : Color.gray;

            if (lastPlayerHealth != player.Health || lastSneaking != player.IsSneaking
                || lastDisguised != player.IsDisguised)
            {
                lastPlayerHealth = player.Health;
                lastSneaking = player.IsSneaking;
                lastDisguised = player.IsDisguised;
                playerStatus = $"玩家生命 {player.Health}  潜行 {player.IsSneaking}  伪装 {player.IsDisguised}";
            }

            if (lastMonsterHealth != monster.Health || lastMode != monster.Mode)
            {
                lastMonsterHealth = monster.Health;
                lastMode = monster.Mode;
                monsterStatus = $"怪物生命 {monster.Health}  状态 {monster.Mode}";
            }
        }

        private Vector2 ToLogicPosition(Vector3 position) =>
            useXZPlane ? new Vector2(position.x, position.z) : new Vector2(position.x, position.y);

        private Vector2 Interpolate(Vector2 previous, Vector2 current, float alpha)
        {
            EncounterProjection.InterpolatePosition(previous.x, previous.y, current.x, current.y, alpha,
                obstacleTeleportDistance, out float x, out float y);
            return new Vector2(x, y);
        }

        // 玩家投影：先按本帧（插值后）逻辑位置贴地；开了 obstacleMask 且不是瞬移时，再做 XZ 扫掠，
        // 被挡就对修正后的 XZ 重新贴地，并按轴回写逻辑位置（只改被挡的轴，见 OnPlayerBlocked）。
        private Vector3 ResolvePlayerScenePosition(Vector2 logicPosition, Vector3 lastScene)
        {
            Vector3 desired = ToScenePosition(logicPosition, lastScene);
            if (!useXZPlane || obstacleMask.value == 0)
            {
                return desired;
            }

            float dx = desired.x - lastScene.x;
            float dz = desired.z - lastScene.z;
            if (dx * dx + dz * dz > obstacleTeleportDistance * obstacleTeleportDistance)
            {
                return desired;
            }

            Vector3 slid = EncounterCollision.Slide(lastScene, desired, obstacleRadius, obstacleBottomOffset,
                obstacleTopOffset, obstacleMask);
            if (slid.x == desired.x && slid.z == desired.z)
            {
                return desired;
            }

            var corrected = new Vector2(slid.x, slid.z);
            Vector3 final = new Vector3(slid.x, ResolveGroundY(corrected, lastScene.y), slid.z);
            // 分轴回写：插值点落后逻辑位置最多一个 tick，整点回写会把沿墙那一轴也往回拉，贴墙滑动每 tick 都丢一截速度。
            Vector2 logic = player.Position;
            float logicX = EncounterProjection.ResolveBlockedAxis(logic.x, desired.x, slid.x, BlockedAxisTolerance);
            float logicY = EncounterProjection.ResolveBlockedAxis(logic.y, desired.z, slid.z, BlockedAxisTolerance);
            if (logicX != logic.x || logicY != logic.y)
            {
                OnPlayerBlocked?.Invoke(new Vector2(logicX, logicY));
            }

            return final;
        }

        private Vector3 ToScenePosition(Vector2 position, Vector3 current)
        {
            if (!useXZPlane)
            {
                return new Vector3(position.x, position.y, current.z);
            }

            return new Vector3(position.x, ResolveGroundY(position, current.y), position.y);
        }

        // 贴地：从当前高度上方往下打射线，按 maxStepHeight 裁决是否采用新高度；groundMask 为 0 时保持当前高度。
        private float ResolveGroundY(Vector2 position, float currentY)
        {
            if (groundMask.value == 0)
            {
                return currentY;
            }

            var origin = new Vector3(position.x, currentY + groundProbeHeight, position.y);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbeHeight + groundProbeDepth, // lint-ok: 纯表现，只定纸片高度，不参与判定
                    groundMask, QueryTriggerInteraction.Ignore))
            {
                return EncounterProjection.ResolveGroundY(currentY, hit.point.y, maxStepHeight);
            }

            return currentY;
        }

        private static void ApplyFlip(SpriteRenderer renderer, float previousX, float currentX)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.flipX = EncounterProjection.ResolveFlipX(previousX, currentX, renderer.flipX, MinFlipDelta);
        }

        // 波 12：调试块挪到左下角，把右上角一列让给沉浸 / 重置按钮与对话三键（DialogueView.Controls）。
        // Time.timeScale <= 0f（对白 / 面板暂停期间）整块不画，避免压在对话框或暂停面板上。
        private void OnGUI()
        {
            if (player == null || monster == null || Time.timeScale <= 0f)
            {
                return;
            }

            float buttonTop = Screen.height - DebugPanelMargin - BackButtonHeight;
            float line2Top = buttonTop - LineHeight;
            float line1Top = line2Top - LineHeight;
            float alertTop = line1Top - AlertBarGapAboveLines;

            GUI.Box(new Rect(DebugPanelMargin, alertTop, AlertBarWidth, AlertBarOuterHeight), string.Empty);
            GUI.Box(new Rect(DebugPanelMargin + AlertBarPadding, alertTop + AlertBarPadding,
                (AlertBarWidth - AlertBarPadding * 2f) * monster.Alert, AlertBarInnerHeight), string.Empty);
            GUI.Label(new Rect(DebugPanelMargin, line1Top, DebugPanelWidth, LineHeight), playerStatus);
            GUI.Label(new Rect(DebugPanelMargin, line2Top, DebugPanelWidth, LineHeight), monsterStatus);
            if (GUI.Button(new Rect(DebugPanelMargin, buttonTop, BackButtonWidth, BackButtonHeight), "返回标题"))
            {
                OnBackClicked?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (placeholderSprite != null)
            {
                Destroy(placeholderSprite);
            }
        }
    }
}
