// 职责：在固定 tick 中处理巡逻、感知、警戒、追击和受伤；MonoBehaviour 只负责表现。
// 为什么新建：GameFlow 管场景而非单个怪物，Core 也不应认识玩法规则。
using System;
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Disguise;
using Game.Player;
using UnityEngine;

namespace Game.Monster
{
    public sealed class MonsterRules : IReplayState
    {
        private readonly MonsterConfig config;
        private readonly MonsterModel model;
        private readonly IRandomStream patrolRandom;
        private readonly ITelemetryScope telemetry;
        private readonly float coneCosine;
        private Vector2[] waypoints = Array.Empty<Vector2>();

        public MonsterRules(MonsterConfig config, MonsterModel model, IRandomService random, ITelemetryScope telemetry)
        {
            this.config = config == null ? throw new ArgumentNullException(nameof(config)) : config;
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            patrolRandom = (random ?? throw new ArgumentNullException(nameof(random))).Stream("logic.monster.patrol");
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            if (config.PatrolSpeed <= 0f || config.AlertSpeedMultiplier <= 0f || config.HostileSpeedMultiplier <= 0f
                || config.VisionAngle <= 0f || config.VisionAngle >= 360f || config.HostileRadius <= 0f
                || config.AlertRadius <= config.HostileRadius || config.NearSenseRadius <= 0f
                || config.AttackRange <= 0f || config.AlertFillSeconds <= 0f || config.AlertFallSeconds <= 0f
                || config.HostileLoseSeconds <= 0f || config.PatrolPauseSeconds <= 0f
                || config.PatrolMinSeconds <= 0 || config.PatrolMaxSeconds < config.PatrolMinSeconds
                || config.PatrolMaxSeconds == int.MaxValue || config.AttackCooldown < 0f
                || config.MaxHealth <= 0 || config.AttackDamage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "MonsterConfig 含非法范围或非正数");
            }

            coneCosine = GameMath.Cos(config.VisionAngle * 0.5f * 0.01745329252f);
        }

        public MonsterModel Model => model;
        public int AttackDamage => config.AttackDamage;

        public void Reset(Vector2[] patrolPoints)
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                throw new ArgumentException("怪物至少需要一个巡逻点", nameof(patrolPoints));
            }

            waypoints = (Vector2[])patrolPoints.Clone();
            model.Position = waypoints[0];
            model.Facing = waypoints.Length > 1
                ? GameMath.Normalize(waypoints[1] - waypoints[0]) : Vector2.right;
            if (model.Facing == Vector2.zero)
            {
                model.Facing = Vector2.right;
            }

            model.LastKnownTarget = model.Position;
            model.Mode = MonsterMode.PatrolWalk;
            model.Health = config.MaxHealth;
            model.WaypointIndex = waypoints.Length > 1 ? 1 : 0;
            model.Alert = 0f;
            model.PatrolWalkElapsed = 0f;
            model.NextPauseAfter = NextPauseInterval();
            model.PatrolPauseLeft = 0f;
            model.AlertAtLoss = 0f;
            model.AlertDecayElapsed = 0f;
            model.HostileLostElapsed = 0f;
            model.AttackCooldownLeft = 0f;
        }

        public bool Step(in MonsterIntent intent)
        {
            float deltaTime = intent.DeltaTime;
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(intent), "固定步长不可为负");
            }

            if (model.Health <= 0 || waypoints.Length == 0)
            {
                return false;
            }

            model.AttackCooldownLeft = GameMath.Max(0f, model.AttackCooldownLeft - deltaTime);
            int sensed = Sense(intent.Target);
            if (sensed == 2)
            {
                EnterHostile(intent.Target.Position);
            }
            else if (model.Mode == MonsterMode.Hostile)
            {
                if (sensed == 1)
                {
                    model.HostileLostElapsed = 0f;
                    model.LastKnownTarget = intent.Target.Position;
                }
                else
                {
                    model.HostileLostElapsed += deltaTime;
                    if (model.HostileLostElapsed >= config.HostileLoseSeconds)
                    {
                        SetMode(MonsterMode.Alert);
                        model.Alert = 1f;
                        model.AlertAtLoss = 1f;
                        model.AlertDecayElapsed = 0f;
                        return false;
                    }
                }
            }
            else if (sensed == 1)
            {
                SetMode(MonsterMode.Alert);
                model.AlertDecayElapsed = 0f;
                model.Alert = GameMath.Clamp01(model.Alert + deltaTime / config.AlertFillSeconds);
                if (model.Alert >= 1f)
                {
                    EnterHostile(intent.Target.Position);
                }
            }
            else if (model.Mode == MonsterMode.Alert)
            {
                if (model.AlertDecayElapsed == 0f)
                {
                    model.AlertAtLoss = model.Alert;
                }

                model.AlertDecayElapsed += deltaTime;
                float fraction = model.AlertDecayElapsed / config.AlertFallSeconds;
                model.Alert = GameMath.Max(0f, model.AlertAtLoss - fraction * fraction);
                if (model.Alert <= 0f)
                {
                    SetMode(MonsterMode.PatrolWalk);
                    model.PatrolWalkElapsed = 0f;
                    model.NextPauseAfter = NextPauseInterval();
                    model.WaypointIndex = FindNearestWaypoint();
                }
            }

            switch (model.Mode)
            {
                case MonsterMode.PatrolWalk:
                    MoveTowards(waypoints[model.WaypointIndex], config.PatrolSpeed, deltaTime);
                    if (GameMath.Distance(model.Position, waypoints[model.WaypointIndex]) <= 0.001f)
                    {
                        model.WaypointIndex = (model.WaypointIndex + 1) % waypoints.Length;
                    }

                    model.PatrolWalkElapsed += deltaTime;
                    if (model.PatrolWalkElapsed >= model.NextPauseAfter)
                    {
                        SetMode(MonsterMode.PatrolPause);
                        model.PatrolPauseLeft = config.PatrolPauseSeconds;
                    }
                    break;
                case MonsterMode.PatrolPause:
                    model.PatrolPauseLeft -= deltaTime;
                    if (model.PatrolPauseLeft <= 0f)
                    {
                        SetMode(MonsterMode.PatrolWalk);
                        model.PatrolWalkElapsed = 0f;
                        model.NextPauseAfter = NextPauseInterval();
                    }
                    break;
                case MonsterMode.Alert:
                    if (sensed > 0)
                    {
                        MoveTowards(intent.Target.Position, config.PatrolSpeed * config.AlertSpeedMultiplier, deltaTime);
                    }
                    break;
                case MonsterMode.Hostile:
                    MoveTowards(model.LastKnownTarget, config.PatrolSpeed * config.HostileSpeedMultiplier, deltaTime);
                    if (sensed > 0 && intent.Target.IsAlive
                        && DisguiseRules.AllowsEnemyAttack(intent.Target.IsDisguised)
                        && model.AttackCooldownLeft <= 0f
                        && GameMath.Distance(model.Position, intent.Target.Position) <= config.AttackRange)
                    {
                        model.AttackCooldownLeft = config.AttackCooldown;
                        telemetry.Track("attack", ("damage", config.AttackDamage));
                        return true;
                    }
                    break;
            }

            return false;
        }

        // 驯服验证的公开移动入口；调用方接管期间不再调用 AI Step。
        public void MoveControlled(Vector2 movement, float deltaTime)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (model.Health <= 0) return;
            if (GameMath.SqrMagnitude(movement) > 1f) movement = GameMath.Normalize(movement);
            if (GameMath.SqrMagnitude(movement) > 0f)
            {
                model.Facing = GameMath.Normalize(movement);
                model.Position += movement * config.PatrolSpeed * deltaTime;
            }
        }

        public void ApplyDamage(in DamageIntent intent, in PlayerSnapshot attacker)
        {
            if (intent.Amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intent), "伤害必须为正数");
            }

            if (model.Health <= 0)
            {
                return;
            }

            model.Health = GameMath.Max(0, model.Health - intent.Amount);
            telemetry.Track("hit", ("hp", model.Health), ("damage", intent.Amount));
            if (model.Health == 0)
            {
                SetMode(MonsterMode.Dead);
            }
            else
            {
                EnterHostile(attacker.Position);
            }
        }

        public void Serialize(IStateWriter writer)
        {
            model.Serialize(writer);
            writer.WriteULong(patrolRandom.State);
            writer.WriteInt(waypoints.Length);
            for (int i = 0; i < waypoints.Length; i++)
            {
                writer.WriteVector2(waypoints[i]);
            }
        }

        public void Deserialize(IStateReader reader)
        {
            model.Deserialize(reader);
            patrolRandom.State = reader.ReadULong();
            int count = reader.ReadInt();
            if (count < 0 || count > 1024)
            {
                throw new InvalidOperationException($"巡逻点数量非法：{count}");
            }

            waypoints = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                waypoints[i] = reader.ReadVector2();
            }
        }

        private int Sense(in PlayerSnapshot target)
        {
            if (!target.IsAlive)
            {
                return 0;
            }

            Vector2 difference = target.Position - model.Position;
            float distance = GameMath.Magnitude(difference);
            bool inCone = distance == 0f || GameMath.Dot(model.Facing, difference / distance) >= coneCosine;
            if (inCone && distance <= config.HostileRadius)
            {
                return 2;
            }

            if ((inCone && distance <= config.AlertRadius && !target.IsDisguised)
                || (distance <= config.NearSenseRadius && !target.IsSneaking))
            {
                return 1;
            }

            return 0;
        }

        private void EnterHostile(Vector2 target)
        {
            SetMode(MonsterMode.Hostile);
            model.Alert = 1f;
            model.LastKnownTarget = target;
            model.HostileLostElapsed = 0f;
        }

        private void SetMode(MonsterMode next)
        {
            if (model.Mode == next)
            {
                return;
            }

            MonsterMode previous = model.Mode;
            model.Mode = next;
            telemetry.Track("state_changed", ("from", (int)previous), ("to", (int)next));
        }

        private void MoveTowards(Vector2 target, float speed, float deltaTime)
        {
            Vector2 direction = target - model.Position;
            float distance = GameMath.Magnitude(direction);
            if (distance <= 0f)
            {
                return;
            }

            model.Facing = direction / distance;
            model.Position += model.Facing * GameMath.Min(distance, speed * deltaTime);
        }

        private int FindNearestWaypoint()
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < waypoints.Length; i++)
            {
                float distance = GameMath.SqrMagnitude(waypoints[i] - model.Position);
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            return nearest;
        }

        private int NextPauseInterval() => patrolRandom.Range(config.PatrolMinSeconds, config.PatrolMaxSeconds + 1);
    }
}
