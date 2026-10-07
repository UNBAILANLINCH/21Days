// 职责：场景中的巡逻点和占位视觉；把逻辑位置投影到场景（XZ 模式可贴地爬台阶）、状态色与朝向翻转；规则数据仍由 PlayerModel / MonsterModel 持有。
// 渲染位置在两逻辑 tick 之间插值（Lerp(PreviousPosition, Position, alpha)），alpha 由 Bind 的调用方给；不给时 alpha = 1，行为同旧版。
// 为什么新建：SampleView 是示例商品面板，现有场景中没有角色表现组件可复用。
// S3 视线遮挡（2026-10-07）：本视图另提供「场景里哪些东西挡视线」的纯数据清单（CollectSightOccluders），
//   与 obstacleMask 分工不同——obstacleMask 走 EncounterCollision 的胶囊扫掠「挡人」（表现层），
//   本清单是进确定性内核的「挡视线」几何，只在场景就绪时转一次，tick 路径不做物理查询。
// 背后处决提示（2026-10-07，接进正式流程那一波）：面板第三行显示「此刻能不能按 F」——读的是
//   ExecutionInteractor.Inspect()（无副作用查询，不写事实、不埋点），没接线时如实写「未接线」。
//   与潜行 / 伪装两行同一块白盒面板：不引入新 UI 资产、不动预制体。
using System;
using Game.Player;
using Game.Stealth;
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

        // —— S3 视线遮挡：**由场景显式提供**的清单，不做任何自动收集（不查物理、不按层扫）。
        //    与上面的 obstacleMask 是两件事：那个挡人（表现层胶囊扫掠），这个挡视线（纯数据几何、进确定性内核）。
        //    清单为空 = 视线不被遮挡，判定与接线前一致（字典 §4.2 `stealth.cover` 的「未做时该键恒不写」）。
        [Tooltip("视线遮挡体：列进来的物体按下面的尺寸挡视线（纯数据，tick 里不做物理查询）。为空 = 不挡视线")]
        [SerializeField] private SightOccluderEntry[] sightOccluders;

        // —— 感知范围可视化（2026-10-07）：把「判定用的那几个半径」画成 Game 视图可见的白盒线框。
        //    出处：`03_潜行与暗杀.md:234` 把「视野扇形不画在画面上」列为已知缺口——玩家看不到范围就只能试错，
        //    暗杀因此显得不讲道理（同文 `:251` 那条待拍板正是「潜行速度下绕过去要几秒？」）。
        //    它**只读**判定参数、不参与任何判定；不勾开关或没拖配置时一行都不画，玩法零影响。
        [Tooltip("红区（敌对）半径的出处：全局 MonsterConfig.HostileRadius。为空则不画感知范围")]
        [SerializeField] private MonsterConfig awarenessConfig;
        [Tooltip("不勾就不画感知范围线框（判定完全不受影响）")]
        [SerializeField] private bool showAwarenessRanges = true;

        private MonsterAwarenessRanges awarenessRanges;

        /// <summary>场景显式登记的一个视线遮挡体。</summary>
        [Serializable]
        private struct SightOccluderEntry
        {
            [Tooltip("遮挡体所在物体；为空则跳过这一条")]
            [SerializeField] private Transform source;
            [Tooltip("形状：勾 = 圆（只用 size.x 当直径），不勾 = 轴对齐矩形（size.x × size.y）")]
            [SerializeField] private bool circle;
            [Tooltip("尺寸：矩形是全宽 × 全高，圆只用 x 当直径。非正数的那条会被跳过并打警告")]
            [SerializeField] private Vector2 size;

            public Transform Source => source;
            public bool IsCircle => circle;
            public Vector2 Size => size;
        }

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
        // 处决提示三件套：绑定的交互入口 + 上一次渲染的提示文字 + 上一次的判定形状（只在变化时重建字符串）。
        private ExecutionInteractor execution;
        private string executionStatus = ExecutionHint.UnboundLabel;
        private bool executionBound;
        private bool lastExecutionHasTarget;
        private bool lastExecutionAllowed;
        private ExecutionReject lastExecutionReject;

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
        /// 把场景里登记的视线遮挡体转成纯数据几何（逻辑 XY）。**场景就绪 / 关卡加载时调一次**，
        /// 结果喂给 <c>EncounterStep.Sight.SetOccluders</c>；tick 路径不再碰场景与物理。
        /// <para>
        /// 没登记任何遮挡体时返回空数组——此时视线判定与接线前一致（只看距离与夹角）。
        /// 尺寸非正数的条目**跳过并打警告**而不是抛：一条没填尺寸的条目不该让整个遭遇场景进不去，
        /// 但也不能静默失效，所以警告里点名是第几条、哪个物体。
        /// </para>
        /// </summary>
        public StealthOccluder[] CollectSightOccluders()
        {
            if (sightOccluders == null || sightOccluders.Length == 0)
            {
                return Array.Empty<StealthOccluder>();
            }

            var collected = new StealthOccluder[sightOccluders.Length];
            int count = 0;
            for (int i = 0; i < sightOccluders.Length; i++)
            {
                SightOccluderEntry entry = sightOccluders[i];
                Transform source = entry.Source;
                if (source == null)
                {
                    continue;
                }

                Vector2 center = ToLogicPosition(source.position);
                Vector2 size = entry.Size;
                if (size.x <= 0f || (!entry.IsCircle && size.y <= 0f))
                {
                    Debug.LogWarning($"EncounterSceneView：第 {i} 条视线遮挡体（{source.name}）尺寸非正数，已跳过", this);
                    continue;
                }

                count++;
                short id = (short)count;
                collected[count - 1] = entry.IsCircle
                    // 圆的 size.x 是**直径**——与上面两个 Tooltip（:78 / :80）以及矩形分支「作者给全长、几何存半」
                    // 是同一个约定。2026-10-07 修正：原先直接把 size.x 当半径传，三处约定里只有这一处不一致，
                    // 会把作者填的尺寸放大一倍。当时**没有任何场景登记过遮挡体**（`sightOccluders` 处处为空），
                    // 所以这次改动的迁移成本为零；改前先确认仍是这个前提。
                    ? StealthOccluder.MakeCircle(center, size.x * 0.5f, id)
                    : StealthOccluder.MakeRectangle(center, size, id);
            }

            if (count == collected.Length)
            {
                return collected;
            }

            var trimmed = new StealthOccluder[count];
            Array.Copy(collected, trimmed, count);
            return trimmed;
        }

        /// <summary>
        /// 绑定要显示的模型。<paramref name="alphaSource"/> 每个渲染帧取一次两 tick 之间的插值比例 [0, 1]
        /// （正式流程读 SimulationRunner.Accumulator，独立场景读 FixedUpdate 相位）；为空时按 1，直接显示当前 tick 位置。
        /// <para>
        /// <paramref name="executionSource"/> 是处决交互入口（可为空）：给了就在状态面板上多画一行
        /// 「此刻能不能按 F」。它只被读（<c>Inspect()</c>），本视图不驱动处决、不写任何事实。
        /// </para>
        /// </summary>
        public void Bind(PlayerModel playerModel, MonsterModel monsterModel, Func<float> alphaSource = null,
            ExecutionInteractor executionSource = null)
        {
            player = playerModel;
            monster = monsterModel;
            interpolationAlpha = alphaSource;
            execution = executionSource;
            EnsureBodies();
            EnsureCamera();
        }

        public void Unbind()
        {
            player = null;
            monster = null;
            interpolationAlpha = null;
            execution = null;
            if (awarenessRanges != null)
            {
                awarenessRanges.Clear();
            }
        }

        /// <summary>
        /// 接线感知范围可视化。视野角 / 橙区半径 / 贴身察觉半径取自 <paramref name="kind"/>（按种类，
        /// 与 `MonsterRules.Sense` 读的是同一批值），红区半径取自 <paramref name="config"/>（全局）。
        /// <paramref name="kind"/> 为 null（没接种类表）时全部退回全局默认值。
        /// </summary>
        /// <param name="config">
        /// 全局怪物配置，由接线方（`MonsterEncounterState`）从容器注入传入。**不靠 Inspector 手拖**：
        /// 漏拖会静默少一层可视化，而那正是「看不到范围就靠试错」的老问题。序列化字段只在没有接线方时兜底
        /// （直接播放场景那条路的 `StandaloneEncounterController` 还没接这一层）。
        /// </param>
        public void BindAwarenessRanges(MonsterKind kind, MonsterConfig config)
        {
            MonsterConfig source = config != null ? config : awarenessConfig;
            if (!showAwarenessRanges || source == null)
            {
                return;
            }

            if (awarenessRanges == null)
            {
                awarenessRanges = gameObject.AddComponent<MonsterAwarenessRanges>();
            }

            awarenessRanges.SetProfile(
                kind == null ? source.VisionAngle : kind.VisionAngle,
                kind == null ? source.AlertRadius : kind.AlertRadius,
                source.HostileRadius,
                kind == null ? source.NearSenseRadius : kind.NearSenseRadius);
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
            // 感知范围线框跟着纸片走：位置用刚投影出来的场景点，朝向用**逻辑朝向**
            // （纸片本身只做左右翻转、不转身，所以朝向不能从 transform 读）。
            if (awarenessRanges != null)
            {
                awarenessRanges.UpdatePose(monsterBody.position, monster.Facing);
            }

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
                // 照镜 demo 验收 V8「界面不出现生命数字或血条」：玩家受击改由镜的裂痕表现，此处不再显示生命数字（见 mirror-module-guide.md）。
                playerStatus = $"潜行 {player.IsSneaking}  伪装 {player.IsDisguised}";
            }

            if (lastMonsterHealth != monster.Health || lastMode != monster.Mode)
            {
                lastMonsterHealth = monster.Health;
                lastMode = monster.Mode;
                monsterStatus = $"怪物生命 {monster.Health}  状态 {monster.Mode}";
            }

            RefreshExecutionStatus();
        }

        // 处决提示：每帧问一次「此刻能不能下刀」（纯浮点比较，不分配、不埋点），
        // **只在判定形状变化时重建字符串**——面板每帧拼字符串会持续产生垃圾。
        private void RefreshExecutionStatus()
        {
            bool bound = execution != null;
            ExecutionHint hint = bound ? execution.Inspect() : default;
            if (bound == executionBound && hint.HasTarget == lastExecutionHasTarget
                && hint.Allowed == lastExecutionAllowed && hint.Reject == lastExecutionReject)
            {
                return;
            }

            executionBound = bound;
            lastExecutionHasTarget = hint.HasTarget;
            lastExecutionAllowed = hint.Allowed;
            lastExecutionReject = hint.Reject;
            executionStatus = hint.ToLabel();
        }

        /// <summary>
        /// 场景坐标 → 逻辑 XY：XZ 模式取 (x, z)，否则取 (x, y)。PlayerStart / PatrolPositions 与场景标记（Mirror 的 MirrorSubject、
        /// SpiritSightZone）都经它换算，保证全工程只有这一处投影约定。公开只读换算，不改任何状态。
        /// </summary>
        public Vector2 ToLogicPosition(Vector3 position) =>
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
            float line3Top = buttonTop - LineHeight;
            float line2Top = line3Top - LineHeight;
            float line1Top = line2Top - LineHeight;
            float alertTop = line1Top - AlertBarGapAboveLines;

            GUI.Box(new Rect(DebugPanelMargin, alertTop, AlertBarWidth, AlertBarOuterHeight), string.Empty);
            GUI.Box(new Rect(DebugPanelMargin + AlertBarPadding, alertTop + AlertBarPadding,
                (AlertBarWidth - AlertBarPadding * 2f) * monster.Alert, AlertBarInnerHeight), string.Empty);
            GUI.Label(new Rect(DebugPanelMargin, line1Top, DebugPanelWidth, LineHeight), playerStatus);
            GUI.Label(new Rect(DebugPanelMargin, line2Top, DebugPanelWidth, LineHeight), monsterStatus);
            // 第三行：背后处决提示（能否按 F）。没接线时写「处决：未接线」，不是留空。
            GUI.Label(new Rect(DebugPanelMargin, line3Top, DebugPanelWidth, LineHeight), executionStatus);
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
