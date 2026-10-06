// 职责：2.5D 探索镜头的**纯数学**约束规则——给定目标点，算出镜头该在哪：矩形边界钳制 + 跟随死区。
//   没有 MonoBehaviour、不读 Time、不碰 Transform，可以在 EditMode 里直接测。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何镜头约束规则。SmoothCameraFollow 只做 Vector3.SmoothDamp 缓动，
//      它的目标点永远等于「跟随目标 + 初始构图偏移」，没有任何钳制或死区。
//   2. 扩展不行：把那几行数学塞进 SmoothCameraFollow 会把它变成「缓动 + 约束 + 构图」三件事的组件，
//      而且它挂着 [RequireComponent(Camera)]、有 Start/LateUpdate 生命周期，测起来要造场景对象；
//      本规则要按各种边界 / 死区组合穷举测试，单独一份才测得干净。
//   3. 所以规则单独一份，表现层（谁在 LateUpdate 里调它）留给既有组件接线。
// 本文件**不修改** SmoothCameraFollow.cs：接线与进对话时的构图切换不在本任务范围（roadmap A6 的另一半）。
// 数值一律经 GameMath（本工程换定点数的唯一改动点，见 docs/architecture.md 5.10）。
using Game.Core.Simulation;
using UnityEngine;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 镜头约束（纯数学，值语义）。两个输入：
    /// <list type="bullet">
    /// <item>边界矩形：镜头中心允许落在的 XZ 范围（相机看到的世界范围 = 边界，镜头是它的取景框）；</item>
    /// <item>死区矩形：以镜头中心为基准的一圈「不动区」，跟随目标在里面动时镜头不动。</item>
    /// </list>
    /// <para>
    /// 判定顺序固定为「<b>先死区、后钳制</b>」：
    /// ① 目标在死区内 → 镜头原地不动（哪怕目标已经越过了场景边界）；
    /// ② 目标在死区外 → 把镜头挪到「刚好让目标落在最近的死区边上」的位置；
    /// ③ 再用边界钳制（含 <see cref="ViewportSize"/> 的取景修正，见 <see cref="ClampToBounds"/>）；
    /// ④ 最后做一次死区检查：钳制后如果目标反而落回死区**里面**，就退回原位置——
    ///    边界与死区打架时（典型：场景边角）宁可让目标贴到死区边缘，也不要镜头来回抖。
    /// </para>
    /// <para>
    /// <b>y 轴不参与约束</b>：镜头高度由构图偏移决定，钳制只改 x / z，y 原样穿过。
    /// </para>
    /// </summary>
    public readonly struct CameraConstraintRules
    {
        private readonly float minX;
        private readonly float maxX;
        private readonly float minZ;
        private readonly float maxZ;
        private readonly float deadZoneHalfWidth;
        private readonly float deadZoneHalfHeight;
        private readonly float halfViewportWidth;
        private readonly float halfViewportHeight;

        /// <summary>
        /// 构造一套约束。
        /// </summary>
        /// <param name="boundsCenter">边界矩形中心（XZ）。</param>
        /// <param name="boundsSize">边界矩形尺寸（宽 x、高 z，单位与场景一致）。分量 &lt; 0 按 0 处理。</param>
        /// <param name="deadZoneSize">死区尺寸；分量 &lt; 0 按 0 处理。</param>
        /// <param name="viewportSize">
        /// 相机视野在世界 XZ 上的尺寸。全填 0（默认）= 不做取景修正，
        /// 「边界」就是镜头中心的活动范围；填了则保证画面不越出边界（见 <see cref="Resolve"/> 的边界规则）。
        /// </param>
        public CameraConstraintRules(Vector2 boundsCenter, Vector2 boundsSize, Vector2 deadZoneSize, Vector2 viewportSize)
        {
            float halfWidth = GameMath.Max(0f, boundsSize.x) * 0.5f;
            float halfHeight = GameMath.Max(0f, boundsSize.y) * 0.5f;

            minX = boundsCenter.x - halfWidth;
            maxX = boundsCenter.x + halfWidth;
            minZ = boundsCenter.y - halfHeight;
            maxZ = boundsCenter.y + halfHeight;

            deadZoneHalfWidth = GameMath.Max(0f, deadZoneSize.x) * 0.5f;
            deadZoneHalfHeight = GameMath.Max(0f, deadZoneSize.y) * 0.5f;

            halfViewportWidth = GameMath.Max(0f, viewportSize.x) * 0.5f;
            halfViewportHeight = GameMath.Max(0f, viewportSize.y) * 0.5f;
        }

        /// <summary>不含取景修正的边界（镜头中心的活动范围），主要给测试与调试看。</summary>
        public Rect CenterBounds =>
            new Rect(minX, minZ, GameMath.Max(0f, maxX - minX), GameMath.Max(0f, maxZ - minZ));

        /// <summary>
        /// 按当前镜头位置与目标位置，算出镜头的新位置。
        /// </summary>
        /// <param name="cameraPosition">当前镜头位置（y 原样保留）。</param>
        /// <param name="targetPosition">跟随目标的位置。</param>
        public Vector3 Resolve(Vector3 cameraPosition, Vector3 targetPosition)
        {
            var position = new Vector2(cameraPosition.x, cameraPosition.z);

            // ① 目标在死区内：镜头不动（「死区」的全部意义就在这一步）。
            if (InsideDeadZone(position, targetPosition))
            {
                return cameraPosition;
            }

            // ② 逐轴推：把镜头推到「目标刚好落在该轴死区边界上」的位置。
            //    目标在该轴死区**以外**才推（推到 目标 ∓ 半径，让目标停在死区边上）；
            //    在死区以内就推到目标本身（该轴上镜头与目标重合，目标在画面里居中）。
            //    两个轴各自独立算，不越界的那个轴因此不会漂移。
            //    这里不写成 Clamp(target, camera-radius, camera+radius)：那是把「目标钳进死区带」，
            //    谁在里谁在外分不出来（目标在带内时会被钳到带边，镜头反而把它顶到边上）。
            //    「越界才推、不越界就跟到目标」必须显式写。
            if (targetPosition.x > position.x + deadZoneHalfWidth)
            {
                position.x = targetPosition.x - deadZoneHalfWidth;
            }
            else if (targetPosition.x < position.x - deadZoneHalfWidth)
            {
                position.x = targetPosition.x + deadZoneHalfWidth;
            }
            else
            {
                position.x = targetPosition.x;
            }

            if (targetPosition.z > position.y + deadZoneHalfHeight)
            {
                position.y = targetPosition.z - deadZoneHalfHeight;
            }
            else if (targetPosition.z < position.y - deadZoneHalfHeight)
            {
                position.y = targetPosition.z + deadZoneHalfHeight;
            }
            else
            {
                position.y = targetPosition.z;
            }

            // ③ 边界钳制（含取景修正）。
            position = ClampToBounds(position);
            var resolved = new Vector3(position.x, cameraPosition.y, position.y);

            // ④ 边界与死区打架时（典型：场景边角）不抖：钳制之后目标**落进死区内部**，
            //    说明这个钳制位置对这条死区规则而言等于没动，那就退回原位置，别让镜头一帧一帧地蹭。
            //    判据必须用**严格包含**（< 半径，不含边界）：正常推镜的结果恰好是「目标停在最近的死区边上」
            //    （距离 == 半径），用宽松判据会把正常推镜一起取消掉——那是本规则唯一真正要做的事。
            if (StrictlyInsideDeadZone(new Vector2(resolved.x, resolved.z), targetPosition))
            {
                return cameraPosition;
            }

            return resolved;
        }

        /// <summary>
        /// 跟随目标是否落在死区**内部**（不含边界）。与 <see cref="InsideDeadZone"/>（含边界）的区别只在
        /// <see cref="Resolve"/> 的第 ④ 步：正常推镜的结果正好落在死区边上，那不算「落进死区」。
        /// </summary>
        private bool StrictlyInsideDeadZone(Vector2 cameraCenter, Vector3 targetPosition)
        {
            return GameMath.Abs(targetPosition.x - cameraCenter.x) < deadZoneHalfWidth
                && GameMath.Abs(targetPosition.z - cameraCenter.y) < deadZoneHalfHeight;
        }

        /// <summary>
        /// 把镜头中心钳到边界内。给了 <c>viewportSize</c> 时按「相机取景框不越出边界」钳：
        /// 镜头中心的可动范围收缩半个视野。这么做是因为场景边界说的是「画面能看到的世界范围」，
        /// 不是「镜头中心能到哪」。
        /// <para>
        /// <b>边界比视野还小时</b>（半视野 &gt; 半边界）：可动范围退化成一点（区间中点），
        /// 也就是把镜头摆在边界正中——此时画面必然看到边界之外，这是几何上无解的，不是本函数的错，
        /// 所以不做小数取整之类的额外处理，只保证结果确定、不抖。
        /// </para>
        /// </summary>
        public Vector2 ClampToBounds(Vector2 center)
        {
            // 视野比边界大时 [minX + halfViewportWidth, maxX - halfViewportWidth] 会翻转，
            // Clamp 遇到 min > max 的行为是未定义的，所以先按边界中点收成一点。
            float lowX = minX + halfViewportWidth;
            float highX = maxX - halfViewportWidth;
            float lowZ = minZ + halfViewportHeight;
            float highZ = maxZ - halfViewportHeight;

            // Vector2 的 y 装的是世界 z（镜头在 XZ 平面上动），别把这两个轴看混。
            float x = lowX > highX ? (minX + maxX) * 0.5f : GameMath.Clamp(center.x, lowX, highX);
            float z = lowZ > highZ ? (minZ + maxZ) * 0.5f : GameMath.Clamp(center.y, lowZ, highZ);

            return new Vector2(x, z);
        }

        /// <summary>跟随目标是否落在死区内（含边界）。</summary>
        public bool InsideDeadZone(Vector2 cameraCenter, Vector3 targetPosition)
        {
            return GameMath.Abs(targetPosition.x - cameraCenter.x) <= deadZoneHalfWidth
                && GameMath.Abs(targetPosition.z - cameraCenter.y) <= deadZoneHalfHeight;
        }
    }
}
