// 职责：场景中的巡逻点和占位视觉；把逻辑位置投影到场景（XZ 模式可贴地爬台阶）、状态色与朝向翻转；规则数据仍由 PlayerModel / MonsterModel 持有。
// 为什么新建：SampleView 是示例商品面板，现有场景中没有角色表现组件可复用。
using System;
using Game.Player;
using UnityEngine;

namespace Game.Monster
{
    public sealed class EncounterSceneView : MonoBehaviour
    {
        private const float MinFlipDelta = 0.0001f;

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

        private PlayerModel player;
        private MonsterModel monster;
        private Sprite placeholderSprite;
        private string playerStatus;
        private string monsterStatus;
        private int lastPlayerHealth = -1;
        private int lastMonsterHealth = -1;
        private MonsterMode lastMode = (MonsterMode)255;
        private bool lastSneaking;
        private bool lastDisguised;

        public event Action OnBackClicked;

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

        public void Bind(PlayerModel playerModel, MonsterModel monsterModel)
        {
            player = playerModel;
            monster = monsterModel;
            EnsureBodies();
            EnsureCamera();
        }

        public void Unbind()
        {
            player = null;
            monster = null;
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

            Vector3 lastPlayerScene = playerBody.position;
            Vector3 lastMonsterScene = monsterBody.position;
            playerBody.position = ToScenePosition(player.Position, lastPlayerScene);
            monsterBody.position = ToScenePosition(monster.Position, lastMonsterScene);
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

        private Vector3 ToScenePosition(Vector2 position, Vector3 current)
        {
            if (!useXZPlane)
            {
                return new Vector3(position.x, position.y, current.z);
            }

            float y = current.y;
            if (groundMask.value != 0)
            {
                var origin = new Vector3(position.x, current.y + groundProbeHeight, position.y);
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbeHeight + groundProbeDepth, // lint-ok: 纯表现，只定纸片高度，不参与判定
                        groundMask, QueryTriggerInteraction.Ignore))
                {
                    y = EncounterProjection.ResolveGroundY(current.y, hit.point.y, maxStepHeight);
                }
            }

            return new Vector3(position.x, y, position.y);
        }

        private static void ApplyFlip(SpriteRenderer renderer, float previousX, float currentX)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.flipX = EncounterProjection.ResolveFlipX(previousX, currentX, renderer.flipX, MinFlipDelta);
        }

        private void OnGUI()
        {
            if (player == null || monster == null)
            {
                return;
            }

            GUI.Label(new Rect(16f, 16f, 480f, 28f), playerStatus);
            GUI.Label(new Rect(16f, 44f, 480f, 28f), monsterStatus);
            GUI.Box(new Rect(16f, 76f, 208f, 20f), string.Empty);
            GUI.Box(new Rect(20f, 80f, 200f * monster.Alert, 12f), string.Empty);
            if (GUI.Button(new Rect(Screen.width - 130f, 16f, 114f, 40f), "返回标题"))
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
