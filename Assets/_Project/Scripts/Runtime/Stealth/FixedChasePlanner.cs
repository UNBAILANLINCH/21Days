// 职责：脚本化「固定追逐」的路线读法——进哪条路线、怎么循环取下一段。
//
// 为什么新建：
// - 复用：Monster 的巡逻是「按场景里 patrolPoints 数组顺序循环 + 随机停步」（`MonsterRules.Reset`
//   与 `NextPauseInterval`，见 `Assets/_Project/Scripts/Runtime/Monster/MonsterRules.cs:103-132、392`），
//   没有「路线分支 / 固定追逐段」的概念，也没有终点。
// - 扩展：`MonsterRules` 本次任务明令不动；且固定追逐的「路线、追兵、终点都事先排好」
//   （`04:112` R28）是关卡数据，不该写进怪物的 AI 规则。
// - 新建：以上两条都不成立，故新建。
//
// 出处（真源）：
// - `docs/design/features-spotlight/04_追逐.md:14`（sp03 阶段3：超过一定数量触发固定追逐）
// - `04:112`（R28「固定追逐」可能是路线、追兵、终点都事先排好的一种形式 [推断]）
// - `04:111`（R27「『固定追逐』的『固定』是指预设路线、脚本化的追逐段落，还是追逐的结果固定，
//   原文也没写」→ 本类只实现「预设路线」这一读法，另外两种读法留待拍板）
// - `04:193`（Q6 同上）、`04:204`（§8 检验问题「账簿的固定追逐能反复触发吗」）
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>
    /// 固定追逐路线的读取器。持有编队定义，按段推进；只算「下一段是哪一段」，
    /// 移动本身由调用方（未来的追逐控制器）做。
    /// </summary>
    public sealed class FixedChasePlanner
    {
        private readonly ChaseFormation formation;
        private int segmentIndex;

        public FixedChasePlanner(in ChaseFormation formation)
        {
            this.formation = formation;
        }

        /// <summary>被规划的编队。</summary>
        public ChaseFormation Formation => formation;

        /// <summary>当前段下标。</summary>
        public int SegmentIndex => segmentIndex;

        /// <summary>
        /// 这个编队算不算「固定追逐」：路线类型是脚本化、且标了脚本化航点。
        /// 两者都为真才写 `chase.fixed`（字典 §4.3）。
        /// </summary>
        public bool IsFixedChase =>
            formation.RouteKind == ChaseRouteKind.Scripted && formation.ScriptedWaypoints;

        /// <summary>进入固定追逐该写的事实键：`chase.fixed`；不是固定追逐时空串。</summary>
        public string FixedFactKey => IsFixedChase ? StealthFactKeys.ChaseFixed : string.Empty;

        /// <summary>路线是否可用：至少一段，且每段不是零长度。</summary>
        public bool HasUsableRoute
        {
            get
            {
                if (formation.SegmentCount == 0)
                {
                    return false;
                }

                for (int i = 0; i < formation.SegmentCount; i++)
                {
                    if (formation.GetSegment(i).Length <= 0f)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>从头开始（重新触发固定追逐时调）。</summary>
        public void Reset() => segmentIndex = 0;

        /// <summary>
        /// 取当前段。路线不可用时返回 false 且 <paramref name="current"/> 为默认值——
        /// 固定追逐没路线就跑不起来，调用方该回退到自主追击。
        /// </summary>
        public bool TryGetCurrentSegment(out PatrolSegment current)
        {
            if (!HasUsableRoute)
            {
                current = default;
                return false;
            }

            current = formation.GetSegment(segmentIndex);
            return true;
        }

        /// <summary>推进到下一段并返回；到终点后回到第一段（`04:204`「能不能反复触发」未定，
        /// 本实现按「循环」处理，接线侧要「只触发一次」就自己判 <see cref="SegmentIndex"/> 是否回绕）。</summary>
        public PatrolSegment Advance()
        {
            if (!HasUsableRoute)
            {
                throw new System.InvalidOperationException("固定追逐路线不可用（没有段，或有零长度的段）");
            }

            segmentIndex = (segmentIndex + 1) % formation.SegmentCount;
            return formation.GetSegment(segmentIndex);
        }

        /// <summary>取第 <paramref name="index"/> 段的中点，供召唤落点与调试绘制用。</summary>
        public Vector2 GetSegmentMidpoint(int index) => formation.GetSegment(index).Midpoint;

        /// <summary>
        /// 路线总长（各段直线长度之和）。用来估「固定追逐跑完要几秒」，
        /// 与追兵速度一起做关卡节奏估算（`04:181` 检验问题：追逐要有威胁靠什么）。
        /// </summary>
        public float TotalRouteLength
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < formation.SegmentCount; i++)
                {
                    total += formation.GetSegment(i).Length;
                }

                return total;
            }
        }

        /// <summary>按追兵速度估算跑完全程的秒数；速度非正时抛（配置错误不静默）。</summary>
        public float EstimatedRouteSeconds(float chaseSpeed)
        {
            if (chaseSpeed <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(chaseSpeed), "追兵速度必须为正数");
            }

            return TotalRouteLength / chaseSpeed;
        }

        /// <summary>判断某个位置是否算「跑到了终点那一带」，供固定追逐的收尾判定用。</summary>
        public bool IsAtFinalWaypoint(Vector2 position, float tolerance)
        {
            if (tolerance < 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(tolerance), "容差不可为负");
            }

            if (formation.SegmentCount == 0)
            {
                return false;
            }

            return GameMath.Distance(position, formation.GetSegment(formation.SegmentCount - 1).To) <= tolerance;
        }
    }
}
