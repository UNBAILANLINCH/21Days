// 职责：在同一固定 tick 中先应用玩家意图，再处理怪物感知与战斗。
// 为什么新建：SimulationRunner 只负责调度步骤，Player 与 Monster 的先后属于遭遇玩法。
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Player;
using UnityEngine;

namespace Game.Monster
{
    public sealed class EncounterStep : ISimulationStep, IReplayState
    {
        private readonly PlayerRules player;
        private readonly MonsterRules monster;

        public EncounterStep(PlayerRules player, MonsterRules monster)
        {
            this.player = player;
            this.monster = monster;
        }

        public bool IsActive { get; private set; }

        public void Begin(Vector2 playerSpawn, Vector2[] patrolPoints)
        {
            player.Reset(playerSpawn);
            monster.Reset(patrolPoints);
            IsActive = true;
        }

        public void End() => IsActive = false;

        public void Step(in SimulationContext context)
        {
            if (!IsActive)
            {
                return;
            }

            InputCommand command = context.Input;
            var playerIntent = new PlayerIntent(
                command.Axis0,
                command.HasButton(InputCommand.ButtonSneak),
                command.HasButton(InputCommand.ButtonDisguise),
                command.HasButton(InputCommand.ButtonAttack));
            bool attacked = player.Step(in playerIntent, context.DeltaTime);
            PlayerSnapshot target = player.Model.Snapshot;
            MonsterModel enemy = monster.Model;
            if (attacked && enemy.Health > 0
                && GameMath.Distance(target.Position, enemy.Position) <= player.AttackRange)
            {
                Vector2 difference = enemy.Position - target.Position;
                if (GameMath.SqrMagnitude(difference) == 0f
                    || GameMath.Dot(target.Facing, GameMath.Normalize(difference)) >= 0f)
                {
                    var damage = new DamageIntent(player.AttackDamage);
                    monster.ApplyDamage(in damage, in target);
                }
            }

            var monsterIntent = new MonsterIntent(target, context.DeltaTime);
            if (monster.Step(in monsterIntent))
            {
                var damage = new DamageIntent(monster.AttackDamage);
                player.ApplyDamage(in damage);
            }
        }

        public void Serialize(IStateWriter writer) => writer.WriteBool(IsActive);

        public void Deserialize(IStateReader reader) => IsActive = reader.ReadBool();
    }
}
