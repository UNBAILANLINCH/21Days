// 职责：镜头约束的**共享策略**——「现在有没有边界」，有就给出约束后的镜头位置，没有就明说「不约束」。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：CameraConstraintRules 是纯数学值类型（没有「此刻有没有边界」这一层状态），
//      SmoothCameraFollow 只有一个初始构图偏移；「边界从哪来、为空时怎么办」在工程里没有落点。
//   2. 扩展不行：把这份状态塞进 SmoothCameraFollow，A6 的另一半（进对话时的构图切换，接线之后做）
//      就得反向去找场景相机；塞进 WorldCatalog（表适配层）会让只读查询认识相机。
//   3. 所以单独一份可注入的策略对象：表现层读它，容器侧（WorldInstaller 注册、WorldSceneState 推进相机）
//      与将来的构图切换共用同一个实例。
// 与 SmoothCameraFollow 的分工：**组件持有数据**（边界由作者在 Inspector 上摆，PRP §2.3 方案①），
//   本类持有判定（有边界就约束、没边界就原样放行）。数据与判定分开，本类才能在 EditMode 里直接测。
using UnityEngine;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 当前生效的镜头边界与死区。**没有边界 = 不约束**（<see cref="TryResolve"/> 返回 false），
    /// 调用方此时必须按原样跟随——这条是「接线前行为零变化」的关键（PRP §2.3）。
    /// </summary>
    public sealed class CameraConstraintPolicy
    {
        private CameraConstraintRules rules;
        private bool hasBounds;

        /// <summary>当前有没有边界。false = 不约束（旧行为）。</summary>
        public bool HasBounds => hasBounds;

        /// <summary>当前生效的约束；没有边界时是 <c>default</c>（别拿它算，先看 <see cref="HasBounds"/>）。</summary>
        public CameraConstraintRules Rules => rules;

        /// <summary>
        /// 设一套边界。<paramref name="boundsSize"/> 有分量为 0 时那是「退化的边界」而不是「没有边界」：
        /// 退化的边界仍然生效（结果落在边界上），要**取消约束**请用 <see cref="ClearBounds"/>。
        /// </summary>
        /// <param name="boundsCenter">边界矩形中心（XZ）。</param>
        /// <param name="boundsSize">边界矩形尺寸（宽 x、高 z）。</param>
        /// <param name="deadZoneSize">死区尺寸；全 0 = 严丝合缝跟随。</param>
        /// <param name="viewportSize">相机视野在世界 XZ 上的尺寸；全 0 = 不做取景修正。</param>
        public void SetBounds(Vector2 boundsCenter, Vector2 boundsSize, Vector2 deadZoneSize, Vector2 viewportSize)
        {
            rules = new CameraConstraintRules(boundsCenter, boundsSize, deadZoneSize, viewportSize);
            hasBounds = true;
        }

        /// <summary>取消约束（回到「旧行为」）。没有边界时是空操作。</summary>
        public void ClearBounds()
        {
            rules = default;
            hasBounds = false;
        }

        /// <summary>
        /// 按当前约束算镜头该在哪。
        /// </summary>
        /// <param name="cameraPosition">镜头当前位置（y 原样保留）。</param>
        /// <param name="followPoint">跟随点：镜头想跟到哪（一般是「目标位置 + 构图偏移」）。</param>
        /// <param name="resolved">约束后的镜头位置。</param>
        /// <returns>true = 有边界、<paramref name="resolved"/> 有效；false = 没有边界，调用方按原样跟随（旧行为，
        /// 此时 <paramref name="resolved"/> 等于 <paramref name="followPoint"/>）。</returns>
        public bool TryResolve(Vector3 cameraPosition, Vector3 followPoint, out Vector3 resolved)
        {
            if (!hasBounds)
            {
                resolved = followPoint;
                return false;
            }

            resolved = rules.Resolve(cameraPosition, followPoint);
            return true;
        }
    }
}
