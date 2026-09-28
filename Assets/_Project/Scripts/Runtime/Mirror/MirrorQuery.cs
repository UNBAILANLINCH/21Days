// 职责：一次照镜判定的全部输入（玩家逻辑位置与朝向、作用距离、扇形半角、候选、线索判定）。
// 为什么新建：PRP/mirror-core 2.3 约定 MirrorRules.Resolve(in MirrorQuery)；参数多达六个，
//   打包成只读值类型让规则签名稳定、测试构造直观。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Mirror
{
    /// <summary><see cref="MirrorRules.Resolve"/> 的输入。只读值类型，不持有场景对象。</summary>
    public readonly struct MirrorQuery
    {
        public MirrorQuery(Vector2 origin, Vector2 facing, float range, float halfAngleDeg,
            IReadOnlyList<MirrorCandidate> candidates, Func<int, bool> hasClues)
        {
            Origin = origin;
            Facing = facing;
            Range = range;
            HalfAngleDeg = halfAngleDeg;
            Candidates = candidates;
            HasClues = hasClues;
        }

        /// <summary>玩家逻辑位置。</summary>
        public Vector2 Origin { get; }

        /// <summary>玩家朝向（不必归一化；零向量视为无朝向，只有贴在脚下的候选算在扇形内）。</summary>
        public Vector2 Facing { get; }

        /// <summary>当前作用距离（已计入裂痕缩减）；≤ 0 时一律照不到。</summary>
        public float Range { get; }

        /// <summary>扇形半角（度），夹到 [0, 180]。</summary>
        public float HalfAngleDeg { get; }

        /// <summary>候选列表；为空或 null 时照不到。</summary>
        public IReadOnlyList<MirrorCandidate> Candidates { get; }

        /// <summary>「某只妖的线索是否齐」判定；为 null 时一律视为不齐（只给模糊轮廓）。</summary>
        public Func<int, bool> HasClues { get; }
    }
}
