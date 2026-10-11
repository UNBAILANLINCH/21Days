// 职责：把怪物的感知范围画成 Game 视图可见的白盒线框——前方视野扇形（橙区半径）、红区扇形、背后贴身察觉圈。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「把判定半径画到场景里」的现成类型；`EncounterSceneView` 的投影只回答
//      「逻辑 XY 对应场景哪里」，不带任何画线能力。
//   2. 扩展不行：塞进 `EncounterSceneView` 会让那个已经很长的白盒表现类再长一截，而画线持有的是运行期
//      创建的 LineRenderer 与材质（有创建 / 销毁生命周期），与「模型 → 纸片」的投影不是同一件事。
//   3. 新建：以上两条不成立，故新建一个只画范围的组件；由 `EncounterSceneView` 创建并每帧喂位置与朝向。
//
// 数值必须与判定同源（`03_潜行与暗杀.md:234` 把「视野扇形不画在画面上」列为已知缺口）：
//   视野角 / 橙区半径 / 贴身察觉半径来自**按种类**的 `MonsterKind`，红区半径是**全局**的
//   `MonsterConfig.HostileRadius`——与 `MonsterRules.Sense` 读的是同一批值，所以画出来的就是判定用的。
//
// 为什么这里直接用 Mathf（lint-ok 逐处留痕）而不引 GameMath：本组件是**纯表现层**，
//   只把半径画成线框，不参与任何判定、不进确定性内核、不影响回放。GameMath 是「将来换定点数」的改造点，
//   那是给确定性内核用的，表现层用它反而语义错位（同 `EncounterSceneView` 贴地那处的取舍）。
//
// 画法的三个取舍：
//   ① 顶点在**局部坐标**只构建一次（`SetProfile` 时），每帧只写 transform 的位置与 yaw，不重建顶点；
//   ② 逻辑 XY → 场景 XZ 的朝向映射与 `EncounterSceneView.ToLogicPosition` 互逆：yaw 0 指向 +Z；
//   ③ `LineAlignment.View`（默认）而不是 `TransformZ`：贴地的环在斜俯视相机下用 TransformZ 会「平躺」看不见。
//
// 已知边界：画的是**判定半径**，不含视线遮挡——掩体挡视线走 `StealthSight` 那条链，与本组件无关。
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Monster
{
    /// <summary>
    /// 怪物感知范围的可视化（白盒）。由 <see cref="EncounterSceneView"/> 创建并驱动，不自己找玩家或怪物。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterAwarenessRanges : MonoBehaviour
    {
        /// <summary>线材质用的着色器，按顺序取第一个存在的（URP 与内置管线的可用名不同）。</summary>
        private static readonly string[] LineShaderNames =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        /// <summary>扇形弧的分段数；够白盒用，不值得为它加配置。</summary>
        private const int ArcSegments = 24;

        /// <summary>贴身察觉圈的环分段数。</summary>
        private const int RingSegments = 36;

        [Tooltip("线宽（场景单位）")]
        [SerializeField, Min(0.001f)] private float lineWidth = 0.05f;

        [Tooltip("视野（橙区）扇形颜色；橙区对未伪装玩家累积警戒")]
        [SerializeField] private Color visionColor = new Color(1f, 0.72f, 0.2f, 0.95f);

        [Tooltip("红区扇形颜色；进红区直接敌对")]
        [SerializeField] private Color hostileColor = new Color(1f, 0.25f, 0.2f, 0.95f);

        [Tooltip("背后贴身察觉圈颜色；潜行可免这一层")]
        [SerializeField] private Color nearSenseColor = new Color(0.35f, 0.9f, 1f, 0.95f);

        private LineRenderer visionFan;
        private LineRenderer hostileFan;
        private LineRenderer nearSenseRing;
        private Material lineMaterial;

        private float visionAngle = 75f;
        private float alertRadius = 6f;
        private float hostileRadius = 2f;
        private float nearSenseRadius = 1.5f;

        /// <summary>
        /// 按与判定同源的数值重建三条线。参数依次是视野总角（度）、橙区半径、红区半径、贴身察觉半径。
        /// 同一次遭遇里这些值不变，所以只在接线时调一次。
        /// </summary>
        public void SetProfile(float visionAngleDegrees, float alert, float hostile, float nearSense)
        {
            visionAngle = Sanitize(visionAngleDegrees, 1f, 359f);
            alertRadius = Sanitize(alert, 0.01f, float.MaxValue);
            hostileRadius = Sanitize(hostile, 0.01f, float.MaxValue);
            nearSenseRadius = Sanitize(nearSense, 0.01f, float.MaxValue);

            EnsureLines();
            BuildFan(visionFan, alertRadius, visionAngle);
            BuildFan(hostileFan, hostileRadius, visionAngle);
            BuildRing(nearSenseRing, nearSenseRadius);
        }

        /// <summary>
        /// 每帧由视图喂一次：怪纸片的**场景位置**与**逻辑朝向**（XY）。
        /// 朝向为零向量时只挪位置、不改朝向（与 `MonsterRules` 的「静止不转身」一致）。
        /// </summary>
        public void UpdatePose(Vector3 scenePosition, Vector2 facing)
        {
            transform.position = scenePosition;
            if (facing.sqrMagnitude < 1e-6f)
            {
                return;
            }

            // 逻辑 (x, y) → 场景 (x, z)，所以 yaw 用 atan2(x, z)：yaw 0 指向 +Z。
            float yaw = Mathf.Atan2(facing.x, facing.y) * Mathf.Rad2Deg; // lint-ok: 纯表现层，只算线框朝向
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>关掉可视化时由视图调：拆掉三条线与材质，不留运行期资源。</summary>
        public void Clear()
        {
            if (visionFan != null) DestroyObject(visionFan.gameObject);
            if (hostileFan != null) DestroyObject(hostileFan.gameObject);
            if (nearSenseRing != null) DestroyObject(nearSenseRing.gameObject);
            visionFan = null;
            hostileFan = null;
            nearSenseRing = null;

            if (lineMaterial != null)
            {
                DestroyObject(lineMaterial);
                lineMaterial = null;
            }
        }

        private void OnDestroy() => Clear();

        // —— 几何：三处 Mathf 收在两个 helper 里，README 式的理由见文件头。——

        private static float Sanitize(float value, float minimum, float maximum) =>
            Mathf.Clamp(value, minimum, maximum); // lint-ok: 纯表现层，夹的是画线半径不是判定参数

        private static Vector3 Polar(float radians, float radius) =>
            new Vector3(Mathf.Sin(radians) * radius, 0f, Mathf.Cos(radians) * radius); // lint-ok: 纯表现层几何

        // 三条线各自一个子物体（一个 GameObject 只挂一个 LineRenderer 更清楚，也便于在层级里单独关掉）。
        private void EnsureLines()
        {
            if (visionFan != null)
            {
                return;
            }

            lineMaterial = new Material(ResolveLineShader());
            visionFan = CreateLine("AwarenessVisionFan", visionColor);
            hostileFan = CreateLine("AwarenessHostileFan", hostileColor);
            nearSenseRing = CreateLine("AwarenessNearSenseRing", nearSenseColor);
        }

        private LineRenderer CreateLine(string childName, Color color)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);

            var line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = lineMaterial;
            line.widthMultiplier = lineWidth;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            // 排序给高一点：白盒线要压在灰盒地板与纸片之上，否则斜俯视下会被地板吃掉。
            line.sortingOrder = 100;
            return line;
        }

        // 扇形：圆心 → 弧（-half .. +half）→ 由 loop 把弧尾接回圆心，闭合。
        private static void BuildFan(LineRenderer line, float radius, float angleDegrees)
        {
            float half = angleDegrees * 0.5f;
            var points = new Vector3[ArcSegments + 2];
            points[0] = Vector3.zero;
            for (int i = 0; i <= ArcSegments; i++)
            {
                float radians = Mathf.Lerp(-half, half, i / (float)ArcSegments) * Mathf.Deg2Rad; // lint-ok: 纯表现层几何（弧的取样角）
                points[i + 1] = Polar(radians, radius);
            }

            line.positionCount = points.Length;
            line.SetPositions(points);
            line.loop = true;
        }

        private static void BuildRing(LineRenderer line, float radius)
        {
            var points = new Vector3[RingSegments];
            for (int i = 0; i < RingSegments; i++)
            {
                float radians = i / (float)RingSegments * Mathf.PI * 2f; // lint-ok: 纯表现层几何（整圆取样角）
                points[i] = Polar(radians, radius);
            }

            line.positionCount = points.Length;
            line.SetPositions(points);
            line.loop = true;
        }

        // 线材质：URP 下 "Sprites/Default" 不一定在，逐个试；都找不到时用默认材质兜底并留一条警告，
        // 不静默变成看不见（那样会被当成「可视化没做出来」）。
        private static Shader ResolveLineShader()
        {
            for (int i = 0; i < LineShaderNames.Length; i++)
            {
                Shader shader = Shader.Find(LineShaderNames[i]);
                if (shader != null)
                {
                    return shader;
                }
            }

            Debug.LogWarning("MonsterAwarenessRanges：找不到可用的线着色器，改用默认材质（可能是粉红/不透明）。");
            return null;
        }

        // 运行期用 Destroy、编辑期用 DestroyImmediate：EditMode 下 Destroy 会被拒并发一条 Error。
        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
