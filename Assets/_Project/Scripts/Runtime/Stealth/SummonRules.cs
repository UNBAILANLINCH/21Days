// 职责：「追逐编队」的数据形状与召唤 / 集群巡逻规则——参与者、召唤半径、集群规模、巡逻路线。
// 纯数据 + 纯规则，不 Instantiate、不认识场景、不认识预制体。
//
// 为什么新建：
// - 复用：工程里没有「编队」概念，场景里也只有一只怪（`04:165` 缺口：只有一个怪，
//   没有召唤、集群巡逻队、跨房间追击，也没有脚本化的「固定追逐」）。
// - 扩展：`MonsterModel` / `MonsterRules` 是「一只怪」的模型，编队是「一群怪」的数据；
//   本次任务明令不动 Monster 侧文件。
// - 新建：以上两条都不成立，故新建。
//
// 出处（真源）：
// - `docs/design/features-spotlight/04_追逐.md:26`（sp03 阶段九：会按一定规律揭露玩家身份
//   并在玩家周围房间召唤巡逻队）
// - `04:29`（巡逻队「和水族一样，只是通常 3-4 集群，能在房间中流通」）
// - `04:30`（都统「发现玩家后会直接在当前房间内召唤巡逻队」）
// - `04:28`（龙窟「房间内怪物除巡逻队不可流通」→ 巡逻队是唯一跨房间的追兵）
// - `04:72`（T6 龙族感知 / T7 都统：追兵都是「召唤的巡逻队」）
// - `04:84`（R9：原文没把「召唤巡逻队」叫作追逐，本文把它当作阶段九的追逐来源 [推断]）
// - `04:86`（R11：巡逻队通常 3–4 只一群，能在房间之间流动；R19 玩家还能混进巡逻队）
// - `04:112、193`（R27、Q6「固定追逐」是什么意思：预设路线、脚本化的追逐段落，还是结果固定，原文没写）
// - `04:14`（sp03 阶段3：超过一定数量触发「固定追逐」）、`04:196`（Q10 都统召唤几群未定）
using System.Collections.Generic;
using Game.Core.Simulation;
using UnityEngine;

namespace Game.Stealth
{
    /// <summary>编队里一名成员的动态形状。位置由接线侧从各自的 `MonsterModel` 填进来。</summary>
    public readonly struct FormationParticipant
    {
        public FormationParticipant(long monsterId, Vector2 position)
        {
            MonsterId = monsterId;
            Position = position;
        }

        /// <summary>怪物实例 id（遭遇 id / 实体 id 都行，只用来回传「该召谁」）。</summary>
        public long MonsterId { get; }

        /// <summary>当前逻辑位置。</summary>
        public Vector2 Position { get; }
    }

    /// <summary>召唤规则里跟具体触发器无关的参数（半径、集群规模）。</summary>
    public readonly struct SummonSettings
    {
        /// <summary>
        /// [待拍板] 召唤半径：以触发点为中心，多远以内的可召唤成员会被召来。
        /// 占位 8，等 `04:196` Q10「都统召唤的巡逻队有几群」与 `00_功能总览.md:381` §8.1 #3。
        /// </summary>
        public float Radius { get; }

        /// <summary>[已拍板] 一群巡逻队的常规规模下限：3 只（`04:29` 原文「通常 3-4 集群」）。</summary>
        public int MinGroupSize { get; }

        /// <summary>[已拍板] 一群巡逻队的常规规模上限：4 只（`04:29` 原文「通常 3-4 集群」）。</summary>
        public int MaxGroupSize { get; }

        public SummonSettings(float radius, int minGroupSize, int maxGroupSize)
        {
            if (radius <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(radius), "召唤半径必须为正数");
            }

            if (minGroupSize <= 0 || maxGroupSize < minGroupSize)
            {
                throw new System.ArgumentOutOfRangeException(nameof(minGroupSize), "集群规模必须满足 0 < 下限 <= 上限");
            }

            Radius = radius;
            MinGroupSize = minGroupSize;
            MaxGroupSize = maxGroupSize;
        }

        /// <summary>工程占位默认值：半径 8、一群 3–4 只（集群规模是原文值）。</summary>
        public static SummonSettings PlaceholderDefault => new SummonSettings(8f, 3, 4);
    }

    /// <summary>巡逻路线的一段。编队成员按段循环走，不需要寻路（第一版直线巡逻，`04:165` 缺口：没有寻路）。</summary>
    public readonly struct PatrolSegment
    {
        public PatrolSegment(Vector2 from, Vector2 to)
        {
            From = from;
            To = to;
        }

        /// <summary>段起点。</summary>
        public Vector2 From { get; }

        /// <summary>段终点。</summary>
        public Vector2 To { get; }

        /// <summary>段长。</summary>
        public float Length => GameMath.Distance(From, To);

        /// <summary>段中点（召唤筛选与落点用）。</summary>
        public Vector2 Midpoint => (From + To) * 0.5f;
    }

    /// <summary>
    /// 一只怪在编队里的**静态**角色：属于哪条路线、哪条段、在群里排第几、能不能被召唤。
    /// 动态部分（位置、朝向、当前段）仍由各自的 `MonsterModel` 持有，本模块不复制它们。
    /// </summary>
    public readonly struct FormationMember
    {
        public FormationMember(long monsterId, short routeId, int segmentIndex, int slotInGroup, bool summonable)
        {
            MonsterId = monsterId;
            RouteId = routeId;
            SegmentIndex = segmentIndex;
            SlotInGroup = slotInGroup;
            Summonable = summonable;
        }

        /// <summary>怪物实例 id。</summary>
        public long MonsterId { get; }

        /// <summary>巡逻路线 id（同一编队里可以有多条路线，例如不同房间）。</summary>
        public short RouteId { get; }

        /// <summary>在这条路线的第几段上。</summary>
        public int SegmentIndex { get; }

        /// <summary>在集群里排第几（0 起），用来分配落点，避免叠在一起。</summary>
        public int SlotInGroup { get; }

        /// <summary>能不能被召唤。龙窟里「房间内怪物除巡逻队不可流通」（`04:28`），非巡逻队成员就是 false。</summary>
        public bool Summonable { get; }
    }

    /// <summary>路线类型。</summary>
    public enum ChaseRouteKind : byte
    {
        /// <summary>自主追击：朝玩家最后已知位置走（即工程现有的敌对追击，`04:165`）。</summary>
        SelfDirected = 0,

        /// <summary>脚本化固定追逐：沿预设路线走，不受玩家位置影响（`04:112` R28）。</summary>
        Scripted = 1,
    }

    /// <summary>
    /// 追逐编队的静态定义。这是一份「可以被关卡表 / Luban 生成、也可以手配」的数据形状，
    /// 规则只读它，不改它。
    /// </summary>
    public readonly struct ChaseFormation
    {
        private readonly FormationMember[] members;
        private readonly PatrolSegment[] segments;

        public ChaseFormation(
            short formationId,
            string displayName,
            ChaseRouteKind routeKind,
            bool scriptedWaypoints,
            FormationMember[] members,
            PatrolSegment[] segments)
        {
            FormationId = formationId;
            DisplayName = displayName;
            RouteKind = routeKind;
            ScriptedWaypoints = scriptedWaypoints;
            this.members = members;
            this.segments = segments;
        }

        /// <summary>编队 id。</summary>
        public short FormationId { get; }

        /// <summary>编队名（调试 / 关卡表用）。</summary>
        public string DisplayName { get; }

        /// <summary>路线类型：自主追击还是脚本化固定追逐。</summary>
        public ChaseRouteKind RouteKind { get; }

        /// <summary>
        /// 是否脚本化航点（`04:112` R28 的「路线、追兵、终点都事先排好」）。
        /// 与 <see cref="RouteKind"/> 一起判：两者都为真才算「固定追逐」。
        /// </summary>
        public bool ScriptedWaypoints { get; }

        /// <summary>成员数组（可能为 null，调用方一律用 <see cref="MemberCount"/>）。</summary>
        public FormationMember[] Members => members;

        /// <summary>路线段数组（可能为 null，调用方一律用 <see cref="SegmentCount"/>）。</summary>
        public PatrolSegment[] Segments => segments;

        /// <summary>成员数量。</summary>
        public int MemberCount => members == null ? 0 : members.Length;

        /// <summary>路线段数量。</summary>
        public int SegmentCount => segments == null ? 0 : segments.Length;

        /// <summary>取成员；越界抛异常（越界是接线错误，不是软条件）。</summary>
        public FormationMember GetMember(int index)
        {
            if (members == null || index < 0 || index >= members.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index), "编队成员下标越界");
            }

            return members[index];
        }

        /// <summary>取路线段；越界抛异常。</summary>
        public PatrolSegment GetSegment(int index)
        {
            if (segments == null || index < 0 || index >= segments.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index), "编队路线段下标越界");
            }

            return segments[index];
        }

        /// <summary>数一遍某条路线上有多少可召唤成员（`04:86` R11 的「3–4 只一群」按这个算）。</summary>
        public int CountSummonableOnRoute(short routeId)
        {
            int total = 0;
            for (int i = 0; i < MemberCount; i++)
            {
                if (members[i].Summonable && members[i].RouteId == routeId)
                {
                    total++;
                }
            }

            return total;
        }
    }

    /// <summary>一次召唤请求。规则只回答「召谁、落在哪」，实例化与接线由调用方做。</summary>
    public readonly struct SummonRequest
    {
        private readonly long[] monsterIds;
        private readonly Vector2[] spawnPositions;

        internal SummonRequest(long[] monsterIds, Vector2[] spawnPositions)
        {
            this.monsterIds = monsterIds;
            this.spawnPositions = spawnPositions;
        }

        /// <summary>这次实际会召唤出来的数量。</summary>
        public int Count => monsterIds == null ? 0 : monsterIds.Length;

        /// <summary>取第 <paramref name="index"/> 个被召唤者的实例 id；越界返回 0。</summary>
        public long GetMonsterId(int index) =>
            monsterIds == null || index < 0 || index >= monsterIds.Length ? 0L : monsterIds[index];

        /// <summary>取第 <paramref name="index"/> 个落点；越界返回原点。</summary>
        public Vector2 GetSpawnPosition(int index) =>
            spawnPositions == null || index < 0 || index >= spawnPositions.Length ? Vector2.zero : spawnPositions[index];

        /// <summary>空请求（什么都没召）。</summary>
        public static SummonRequest Empty => new SummonRequest(null, null);
    }

    /// <summary>召唤判定被拒的原因。</summary>
    public enum SummonReject : byte
    {
        /// <summary>没被拒。</summary>
        None = 0,

        /// <summary>编队里一个成员都没有。</summary>
        NoMembers = 1,

        /// <summary>编队一条路线都没有。</summary>
        NoSegments = 2,

        /// <summary>调用方给的「当前可召唤成员数」不足集群下限（3 只）。</summary>
        BelowGroupSize = 3,

        /// <summary>触发点半径内没有可召唤的成员。</summary>
        NothingInRadius = 4,
    }

    /// <summary>
    /// 召唤 / 集群巡逻规则。
    /// <para>
    /// 判定链：编队有成员与路线 → 半径内有可召唤成员 → 数量达到集群下限（3）→
    /// 取不超过集群上限（4）的一批 → 落点均匀铺在触发点周围（避免叠在一起互相挡视线）。
    /// </para>
    /// </summary>
    public static class SummonRules
    {
        /// <summary>
        /// 算一次召唤。<paramref name="formation"/> 不合法或半径内没人时返回
        /// <see cref="SummonRequest.Empty"/>，原因写到 <paramref name="reject"/>。
        /// </summary>
        public static SummonRequest Evaluate(
            in ChaseFormation formation,
            Vector2 triggerPosition,
            in SummonSettings settings,
            out SummonReject reject)
        {
            reject = SummonReject.None;
            if (formation.MemberCount == 0)
            {
                reject = SummonReject.NoMembers;
                return SummonRequest.Empty;
            }

            if (formation.SegmentCount == 0)
            {
                reject = SummonReject.NoSegments;
                return SummonRequest.Empty;
            }

            var candidates = new List<int>();
            for (int i = 0; i < formation.MemberCount; i++)
            {
                FormationMember member = formation.GetMember(i);
                if (!member.Summonable)
                {
                    continue;
                }

                if (member.SegmentIndex < 0 || member.SegmentIndex >= formation.SegmentCount)
                {
                    // 成员挂在不存在的段上 = 关卡表配错，跳过而不是崩（Validate 会把它报出来）。
                    continue;
                }

                float distance = GameMath.Distance(triggerPosition, formation.GetSegment(member.SegmentIndex).Midpoint);
                if (distance <= settings.Radius)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                reject = SummonReject.NothingInRadius;
                return SummonRequest.Empty;
            }

            if (candidates.Count < settings.MinGroupSize)
            {
                // 「通常 3-4 集群」：不够一群就不召，避免一只只被玩家钓出来（`04:86` R11）。
                reject = SummonReject.BelowGroupSize;
                return SummonRequest.Empty;
            }

            int take = GameMath.Min(candidates.Count, settings.MaxGroupSize);
            var ids = new long[take];
            var positions = new Vector2[take];
            for (int i = 0; i < take; i++)
            {
                FormationMember member = formation.GetMember(candidates[i]);
                ids[i] = member.MonsterId;
                positions[i] = SpawnPositionAround(triggerPosition, member.SlotInGroup, take, settings.Radius);
            }

            return new SummonRequest(ids, positions);
        }

        /// <summary>
        /// 把 <paramref name="count"/> 个落点铺在圆心周围的同心环上（每环最多 3 个，环半径递增）。
        /// 纯函数，测试可以直接验「都不重合、都在召唤半径内」。
        /// </summary>
        public static Vector2 SpawnPositionAround(Vector2 center, int slot, int count, float radius)
        {
            if (count <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(count), "落点数量必须为正数");
            }

            if (radius <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(nameof(radius), "落点半径必须为正数");
            }

            if (slot < 0 || slot >= count)
            {
                throw new System.ArgumentOutOfRangeException(nameof(slot), "落点槽位必须在 [0, count) 内");
            }

            const int PerRing = 3;
            int ringCount = (count + PerRing - 1) / PerRing;
            int ring = slot / PerRing;
            int slotInRing = slot % PerRing;

            // 最外环半径 = 召唤半径的一半，保证落点一定落在召唤半径内。
            float ringRadius = radius * 0.5f * (ring + 1) / ringCount;
            float angle = 6.2831853f * slotInRing / PerRing + ring * 1.0471976f;
            return center + new Vector2(GameMath.Cos(angle), GameMath.Sin(angle)) * ringRadius;
        }

        /// <summary>
        /// 校验编队的静态数据：成员与段非空、成员 id 唯一、段下标不越界、每条路线的可召唤成员不超过集群上限。
        /// 返回第一个问题；没问题返回 null。
        /// </summary>
        public static string Validate(in ChaseFormation formation, in SummonSettings settings)
        {
            if (formation.MemberCount == 0)
            {
                return "编队没有任何成员";
            }

            if (formation.SegmentCount == 0)
            {
                return "编队没有任何路线段";
            }

            var seen = new HashSet<long>();
            var routes = new HashSet<short>();
            for (int i = 0; i < formation.MemberCount; i++)
            {
                FormationMember member = formation.GetMember(i);
                if (!seen.Add(member.MonsterId))
                {
                    return $"成员实例 id 重复：{member.MonsterId}";
                }

                if (member.SegmentIndex < 0 || member.SegmentIndex >= formation.SegmentCount)
                {
                    return $"成员 {member.MonsterId} 指向不存在的路线段 {member.SegmentIndex}";
                }

                routes.Add(member.RouteId);
            }

            foreach (short route in routes)
            {
                int onRoute = formation.CountSummonableOnRoute(route);
                if (onRoute > settings.MaxGroupSize)
                {
                    return $"路线 {route} 上的可召唤成员 {onRoute} 超过集群上限 {settings.MaxGroupSize}";
                }
            }

            return null;
        }

        /// <summary>被拒原因的中文描述。</summary>
        public static string Describe(SummonReject reject)
        {
            switch (reject)
            {
                case SummonReject.None:
                    return "可以召唤";
                case SummonReject.NoMembers:
                    return "编队没有成员";
                case SummonReject.NoSegments:
                    return "编队没有路线段";
                case SummonReject.BelowGroupSize:
                    return "半径内可召唤成员不足一群（少于 3 只）";
                case SummonReject.NothingInRadius:
                    return "召唤半径内没有可召唤的巡逻队";
                default:
                    return "未知原因";
            }
        }
    }
}
