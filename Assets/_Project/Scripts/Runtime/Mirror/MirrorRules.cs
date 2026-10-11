// 职责：照镜的纯规则——扇形内选最近对象、按种类与线索给结果、自照恒空白、线索是否齐、结果写进辨认记录、
//   候选逻辑位置取场景投影还是巡逻怪模型；不认识存档服务、场景、事件与配置表。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：InteractionSelector.Select（Game.Interaction，原物资箱焦点的选最近已并入）只按半径选最近的可交互对象，没有扇形、种类与线索判定。
//   2. 扩展不行：塞进 MonsterRules / PlayerRules 会让确定性内核认识妖物表与辨认记录（PRP/mirror-core 2.3「不改内核」）。
//   同 LootRules / QuestRules 的分法：规则抽成纯 C#，脱离容器在 EditMode 穷举。
using System.Collections.Generic;
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Mirror
{
    /// <summary>照镜规则。全部静态；除首次加入记录列表外无分配；非法参数按「照不到 / 不记录」处理，不抛。</summary>
    public static class MirrorRules
    {
        private const float DegToRad = 0.017453292f;
        private const float MaxHalfAngle = 180f;

        /// <summary>
        /// 照镜判定：在以 <see cref="MirrorQuery.Origin"/> 为顶点、<see cref="MirrorQuery.Facing"/> 为轴、
        /// 半角 <see cref="MirrorQuery.HalfAngleDeg"/> 的扇形内，距离 ≤ <see cref="MirrorQuery.Range"/>（含边界）的候选里取最近的一个；
        /// 等距取列表里靠前的。无 → Nothing；人 → Human；物 → Object；妖且线索齐 → TrueForm，否则 Blurry。
        /// </summary>
        public static MirrorResult Resolve(in MirrorQuery query)
        {
            IReadOnlyList<MirrorCandidate> candidates = query.Candidates;
            float range = query.Range;
            if (candidates == null || candidates.Count == 0 || range <= 0f) return MirrorResult.Nothing;

            float cosHalf = GameMath.Cos(GameMath.Clamp(query.HalfAngleDeg, 0f, MaxHalfAngle) * DegToRad);
            Vector2 facing = GameMath.Normalize(query.Facing);
            float rangeSqr = range * range;
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2 delta = candidates[i].Position - query.Origin;
                float sqr = GameMath.SqrMagnitude(delta);
                if (sqr > rangeSqr || sqr >= bestSqr) continue;
                if (!InFan(delta, sqr, facing, cosHalf)) continue;
                best = i;
                bestSqr = sqr;
            }

            if (best < 0) return MirrorResult.Nothing;

            MirrorCandidate target = candidates[best];
            float distance = GameMath.Sqrt(bestSqr);
            switch (target.Kind)
            {
                case MirrorSubjectKind.Human:
                    return new MirrorResult(MirrorResultKind.Human, best, 0, distance);
                case MirrorSubjectKind.Object:
                    return new MirrorResult(MirrorResultKind.Object, best, 0, distance);
                case MirrorSubjectKind.Yao:
                    bool clues = query.HasClues != null && query.HasClues(target.YaoId);
                    return new MirrorResult(clues ? MirrorResultKind.TrueForm : MirrorResultKind.Blurry,
                        best, target.YaoId, distance);
                default:
                    return MirrorResult.Nothing;
            }
        }

        /// <summary>自照：恒为空白，无论前方有无对象。</summary>
        public static MirrorResult ResolveSelf() => MirrorResult.Self;

        /// <summary>
        /// 线索是否齐：<paramref name="clueItems"/> 为 null 或空 = 无要求，返回 true；
        /// 否则每个 id 在 <paramref name="items"/> 里数量都 &gt; 0 才返回 true。items 为 null 视为什么都没有。
        /// </summary>
        public static bool CluesSatisfied(IReadOnlyList<int> clueItems, IReadOnlyDictionary<int, int> items)
        {
            if (clueItems == null || clueItems.Count == 0) return true;
            if (items == null) return false;
            for (int i = 0; i < clueItems.Count; i++)
            {
                if (!items.TryGetValue(clueItems[i], out int count) || count <= 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 候选的逻辑位置：挂在巡逻怪上（<paramref name="followsMonster"/>）时取模型位置，否则取场景坐标的投影。
        /// 巡逻怪的场景位置是两 tick 间的插值表现，判定必须以 MonsterModel.Position 为准（PRP/mirror-core 4 风险）。
        /// </summary>
        public static Vector2 CandidatePosition(bool followsMonster, Vector2 projectedScenePosition, Vector2 monsterPosition) =>
            followsMonster ? monsterPosition : projectedScenePosition;

        /// <summary>
        /// 把一次结果写进辨认记录：TrueForm → 加入 <see cref="MirrorSaveData.Identified"/>（首次返回 true）；
        /// Blurry → 加入 <see cref="MirrorSaveData.GlimpsedBlurry"/>；Self → <see cref="MirrorSaveData.SelfLooks"/> +1；其余不记。
        /// 返回值只表示「这次是首次照见真形」。data 为 null 时空操作返回 false。
        /// </summary>
        public static bool Record(MirrorSaveData data, in MirrorResult result)
        {
            if (data == null) return false;
            switch (result.Kind)
            {
                case MirrorResultKind.TrueForm:
                    data.Identified ??= new List<int>();
                    return AddUnique(data.Identified, result.YaoId);
                case MirrorResultKind.Blurry:
                    data.GlimpsedBlurry ??= new List<int>();
                    AddUnique(data.GlimpsedBlurry, result.YaoId);
                    return false;
                case MirrorResultKind.Self:
                    data.SelfLooks++;
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>列表里是否有该 id；列表为 null 返回 false。</summary>
        public static bool Contains(List<int> list, int id)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id) return true;
            }
            return false;
        }

        private static bool AddUnique(List<int> list, int id)
        {
            if (Contains(list, id)) return false;
            list.Add(id);
            return true;
        }

        // 贴在脚下（距离 0）的候选算在扇形内；朝向为零向量时其余一律不在扇形内。
        private static bool InFan(Vector2 delta, float sqr, Vector2 facingNormalized, float cosHalf)
        {
            if (sqr <= 0f) return true;
            if (GameMath.SqrMagnitude(facingNormalized) <= 0f) return false;
            float cos = GameMath.Dot(delta, facingNormalized) / GameMath.Sqrt(sqr);
            return cos >= cosHalf;
        }
    }
}
