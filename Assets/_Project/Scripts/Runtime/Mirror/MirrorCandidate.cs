// 职责：照镜候选的值快照——逻辑位置、种类、妖 id；让 MirrorRules 不碰 MonoBehaviour 与场景。
// 为什么新建：MirrorRules 要在 EditMode 里脱离场景穷举（PRP/mirror-core 2.3），候选必须是纯值；
//   MirrorSubject 是 MonoBehaviour，直接交给规则会把 UnityEngine.Object 的伪空与激活状态带进纯规则。
using UnityEngine;

namespace Game.Mirror
{
    /// <summary>一个照镜候选。由 <see cref="MirrorSceneBinder.CollectCandidates"/> 从场景标记换算，或测试直接构造。</summary>
    public readonly struct MirrorCandidate
    {
        public MirrorCandidate(Vector2 position, MirrorSubjectKind kind, int yaoId)
        {
            Position = position;
            Kind = kind;
            YaoId = yaoId;
        }

        /// <summary>逻辑 XY（与 PlayerModel.Position 同一坐标系）。</summary>
        public Vector2 Position { get; }

        public MirrorSubjectKind Kind { get; }

        /// <summary>妖物表主键；只在 <see cref="Kind"/> 为 <see cref="MirrorSubjectKind.Yao"/> 时有意义。</summary>
        public int YaoId { get; }
    }
}
