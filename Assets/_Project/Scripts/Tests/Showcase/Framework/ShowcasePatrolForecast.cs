// 职责：回放走位用的「巡逻者预判」工具——场上巡逻者怎么取（Monster 侧口径）、每只正在用的巡逻路点怎么取（MonsterRules.Capture），
//   以及「玩家沿一条折线走过去（再在终点待一会儿）的这几秒里，某只巡逻者会不会察觉到玩家」的前推判定。
//   判定一律用产品自己的档位（MonsterRules.SenseFrom，不含遮挡 = 更保守），不在回放里重抄半径与夹角。
//
// 用法（先进世界，EncounterStep 可解析）：
//   MonsterRules[] patrollers = ShowcasePatrolForecast.Patrollers(step);          // 与 Taming.TargetIds 同序，主怪在首位
//   Vector2[] route = ShowcasePatrolForecast.RouteOf(patrollers[i]);              // 它此刻正在用的路点（只读快照）
//   bool blocked = ShowcasePatrolForecast.AnyMaySee(patrollers, routes, path, horizon,
//       ShowcasePatrolForecast.Options.Crossing, player.Snapshot, skipIndex: -1);     // 谁会扫到这条路（-1 = 一只都不跳过）
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— StealthShowcase（PatrolMaySeePath / CollectPatrolRoutes）与 IdentityShowcase（PatrolMaySee）各有一份私有实现，
//           Monster 回放（升级回合制 / 被抓那几条）要同一套预判；第三次复制就是三份会各自过期的真源。
//   扩展 —— 它不用基类的报告 / 输入 / 容器成员，是纯计算；塞进 ShowcaseScenario 的任何一个 partial 职责都说不通，
//           所以做成 Framework 下的静态工具类。Stealth / Identity 本波不迁移（逐字不动它们已验过的走位），
//           Identity 只改用这里的 RouteOf 取路线（W6a 发现的错位坐标，见 RouteOf 的注释）。
using System;
using System.Collections.Generic;
using Game.Monster;
using Game.Player;
using UnityEngine;

namespace Game.Tests.Showcase
{
    /// <summary>巡逻者预判工具（纯计算，只读模型与配置，不推进任何状态）。</summary>
    public static class ShowcasePatrolForecast
    {
        /// <summary>
        /// 前推判定的参数：玩家推进速度区间、两头余量、沿路取样间隔、前推步长、到终点后算不算「原地待着」、探针何时按潜行判。
        /// </summary>
        public readonly struct Options
        {
            public Options(float speedLow, float speedHigh, float slackMeters, float sampleMeters, float stepSeconds,
                bool holdAtEnd, float sneakWithin)
            {
                SpeedLow = speedLow;
                SpeedHigh = speedHigh;
                SlackMeters = slackMeters;
                SampleMeters = sampleMeters;
                StepSeconds = stepSeconds;
                HoldAtEnd = holdAtEnd;
                SneakWithin = sneakWithin;
            }

            /// <summary>玩家推进速度下限（米/秒）：起步、拐弯都按它估「最慢走到哪」。</summary>
            public float SpeedLow { get; }

            /// <summary>玩家推进速度上限（米/秒）：按它估「最快走到哪」。</summary>
            public float SpeedHigh { get; }

            /// <summary>玩家位置区间两头各多放的余量（米）。</summary>
            public float SlackMeters { get; }

            /// <summary>沿路线取样的间隔（米）。</summary>
            public float SampleMeters { get; }

            /// <summary>前推步长（秒）。</summary>
            public float StepSeconds { get; }

            /// <summary>为真时，走到终点之后的那几秒也按「玩家站在终点」去探（要在终点待一会儿的走位用）。</summary>
            public bool HoldAtEnd { get; }

            /// <summary>探针离这只怪多近时按「在潜行」判（回放在那一圈里确实按着潜行键）；0 = 一律按没潜行（更保守）。</summary>
            public float SneakWithin { get; }

            /// <summary>
            /// 一口气跑过一段路（StealthShowcase ④ 穿缝那一组参数，逐字照抄）：奔跑 5（PlayerConfig.asset），下限 3.5 留给起步与拐弯，
            /// 两头各放 0.6 米、每 0.4 米取样、步长 0.05 秒；不在终点停留、探针一律按没潜行。
            /// </summary>
            public static Options Crossing => new Options(3.5f, 5.5f, 0.6f, 0.4f, 0.05f, false, 0f);
        }

        /// <summary>
        /// 场上全部巡逻者：Monster 侧自己的口径 <c>EncounterStep.Taming.TargetIds</c>（主怪在首位）。
        /// 返回数组与 TargetIds **逐位对齐**（取不到的那一位为 null），所以下标可以直接拿去对 <see cref="IndexOf"/>。
        /// </summary>
        public static MonsterRules[] Patrollers(EncounterStep step)
        {
            if (step == null || step.Taming == null)
            {
                return Array.Empty<MonsterRules>();
            }

            IReadOnlyList<string> ids = step.Taming.TargetIds;
            var found = new MonsterRules[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                found[i] = step.Taming.GetTarget(ids[i]);
            }

            return found;
        }

        /// <summary>某个稳定标识在 <see cref="Patrollers"/> 里的下标（= <c>Taming.TargetIds</c> 的顺序）；找不到为 -1。</summary>
        public static int IndexOf(EncounterStep step, string stableId)
        {
            if (step == null || step.Taming == null)
            {
                return -1;
            }

            IReadOnlyList<string> ids = step.Taming.TargetIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], stableId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 这只怪**自己正在用的**巡逻路点（<see cref="MonsterRules.Capture"/> 的 WaypointX / WaypointY，只读快照）；取不到为 null。
        /// <para>
        /// 为什么不读 <c>TamingActor.PatrolPositions(view)</c>（2026-10-15 W6a 发现）：那是从巡逻点 Transform 的**此刻**世界坐标换算的，
        /// 产品只在场景就绪那一刻读一次（<c>EncounterSceneView.ConfigureTaming</c>）；回放进场后再读，巡逻者乙与巡夜人拿到的是另一套坐标
        /// （实测乙站在 (15.86, 5.00)，取到的路线却是 (36.28, -23.86) → (36.28, -27.86)），预判就朝一个不存在的路点推。
        /// </para>
        /// </summary>
        public static Vector2[] RouteOf(MonsterRules rules)
        {
            if (rules == null)
            {
                return null;
            }

            MonsterSaveData saved = rules.Capture();
            int count = Mathf.Min(saved.WaypointX.Length, saved.WaypointY.Length);
            var route = new Vector2[count];
            for (int k = 0; k < count; k++)
            {
                route[k] = new Vector2(saved.WaypointX[k], saved.WaypointY[k]);
            }

            return route;
        }

        /// <summary>与 <paramref name="patrollers"/> 逐位对齐的巡逻路线（<see cref="RouteOf"/>）。</summary>
        public static Vector2[][] RoutesOf(MonsterRules[] patrollers)
        {
            var routes = new Vector2[patrollers.Length][];
            for (int i = 0; i < routes.Length; i++)
            {
                routes[i] = RouteOf(patrollers[i]);
            }

            return routes;
        }

        /// <summary>折线总长（米）。</summary>
        public static float PathLength(IReadOnlyList<Vector2> points)
        {
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                length += (points[i] - points[i - 1]).magnitude;
            }

            return length;
        }

        /// <summary>折线上从起点量 <paramref name="distance"/> 米处的点（超出末端取末端）。</summary>
        public static Vector2 PointAlong(IReadOnlyList<Vector2> points, float distance)
        {
            for (int i = 1; i < points.Count; i++)
            {
                float segment = (points[i] - points[i - 1]).magnitude;
                if (distance <= segment && segment > 0f)
                {
                    return Vector2.Lerp(points[i - 1], points[i], distance / segment);
                }

                distance -= segment;
            }

            return points[points.Count - 1];
        }

        /// <summary>
        /// 场上（除下标 <paramref name="skipIndex"/> 那一只外）有没有谁会挡住这条路：活着的巡逻者里**任何一只已经不在巡逻两态**（被惊动了）就算挡，
        /// 否则逐只 <see cref="MaySeePath"/>。<paramref name="skipIndex"/> 传 -1 = 一只都不跳过。
        /// </summary>
        public static bool AnyMaySee(MonsterRules[] patrollers, Vector2[][] routes, IReadOnlyList<Vector2> points, float horizon,
            in Options forecast, in PlayerSnapshot player, int skipIndex)
        {
            for (int i = 0; i < patrollers.Length; i++)
            {
                MonsterRules rules = patrollers[i];
                if (i == skipIndex || rules == null || rules.Model.Health <= 0)
                {
                    continue;
                }

                if (rules.Model.Mode != MonsterMode.PatrolWalk && rules.Model.Mode != MonsterMode.PatrolPause)
                {
                    return true;
                }

                Vector2[] route = routes != null && i < routes.Length ? routes[i] : null;
                if (MaySeePath(rules, route, points, horizon, in forecast, in player))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 把一只巡逻怪按巡逻两态往前推 <paramref name="horizon"/> 秒（与 <c>MonsterRules.Step</c> 的 PatrolWalk / PatrolPause 同一口径：
        /// 朝当前路点走、到点换下一个、走够 NextPauseAfter 停 PatrolPauseSeconds；下一次停顿的随机间隔取下限），
        /// 每一刻判它能不能看见「玩家这一刻可能在的那一小段路」（按 <see cref="Options.SpeedLow"/>..<see cref="Options.SpeedHigh"/>
        /// 推进、两头各放余量）。算法与 StealthShowcase.PatrolMaySeePath 逐字相同，只把参数收成 <see cref="Options"/>。
        /// </summary>
        /// <param name="rules">要前推的巡逻怪。</param>
        /// <param name="route">它正在用的路点（<see cref="RouteOf"/>）；null 时只按它此刻的位置 / 朝向判（不前推移动）。</param>
        /// <param name="points">玩家要走的折线（首点 = 脚下）。</param>
        /// <param name="horizon">往前推多少秒。</param>
        /// <param name="forecast">推进速度、取样、停留与潜行探针的参数。</param>
        /// <param name="player">玩家此刻的快照：探针沿用它的朝向、伪装与生命，只换位置与潜行。</param>
        public static bool MaySeePath(MonsterRules rules, Vector2[] route, IReadOnlyList<Vector2> points, float horizon,
            in Options forecast, in PlayerSnapshot player)
        {
            MonsterModel model = rules.Model;
            MonsterConfig config = rules.Config;
            Vector2 position = model.Position;
            Vector2 facing = model.Facing;
            int index = model.WaypointIndex;
            bool pausing = model.Mode == MonsterMode.PatrolPause;
            float walked = model.PatrolWalkElapsed;
            float pauseAfter = model.NextPauseAfter;
            float pauseLeft = model.PatrolPauseLeft;
            float length = PathLength(points);
            for (float t = 0f; t <= horizon; t += forecast.StepSeconds)
            {
                float from = Mathf.Max(0f, forecast.SpeedLow * t - forecast.SlackMeters);
                float to = Mathf.Min(length, forecast.SpeedHigh * t + forecast.SlackMeters);
                if (forecast.HoldAtEnd)
                {
                    // 走得最慢的那一种也早该到了：此后玩家就站在终点上等，终点照样要探。
                    from = Mathf.Min(from, length);
                }

                for (float s = from; s <= to; s += forecast.SampleMeters)
                {
                    Vector2 point = PointAlong(points, s);
                    bool sneaking = forecast.SneakWithin > 0f && (point - position).magnitude <= forecast.SneakWithin;
                    var probe = new PlayerSnapshot(point, player.Facing, sneaking, player.IsDisguised, player.Health);
                    if (MonsterRules.SenseFrom(config, rules.Kind, position, facing, in probe) > 0)
                    {
                        return true;
                    }
                }

                if (pausing)
                {
                    pauseLeft -= forecast.StepSeconds;
                    if (pauseLeft <= 0f)
                    {
                        pausing = false;
                        walked = 0f;
                        pauseAfter = config.PatrolMinSeconds;
                    }
                }
                else if (route != null && route.Length > 0)
                {
                    index = Mathf.Clamp(index, 0, route.Length - 1);
                    Vector2 toWaypoint = route[index] - position;
                    float distance = toWaypoint.magnitude;
                    if (distance > 0f)
                    {
                        facing = toWaypoint / distance;
                        position += facing * Mathf.Min(distance, config.PatrolSpeed * forecast.StepSeconds);
                    }

                    if ((route[index] - position).magnitude <= 0.001f)
                    {
                        index = (index + 1) % route.Length;
                    }

                    walked += forecast.StepSeconds;
                    if (walked >= pauseAfter)
                    {
                        pausing = true;
                        pauseLeft = config.PatrolPauseSeconds;
                    }
                }
            }

            return false;
        }
    }
}
