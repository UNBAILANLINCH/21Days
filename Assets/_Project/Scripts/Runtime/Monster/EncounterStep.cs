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
        public enum Result : byte { None, Victory, Defeat, Aborted }
        private readonly PlayerRules player;
        private readonly MonsterRules monster;

        public EncounterStep(PlayerRules player, MonsterRules monster)
        {
            this.player = player;
            this.monster = monster;
        }

        public bool IsActive { get; private set; }
        public long EncounterId { get; private set; }
        public long ActivationId { get; private set; }
        public Result PendingResult { get; private set; }
        public bool ResultConsumed { get; private set; }

        // 对已经在探索场景中的双方建立战斗关联，不重置生命和位置。
        public void StartBattle(long encounterId, long activationId)
        {
            if (!IsActive || encounterId < 1 || activationId < 1) throw new System.InvalidOperationException("战斗未准备或身份非法");
            if (EncounterId == encounterId && ActivationId == activationId) return;
            if (EncounterId != 0 && !ResultConsumed) throw new System.InvalidOperationException("旧战斗尚未结算");
            EncounterId = encounterId;
            ActivationId = activationId;
            PendingResult = Result.None;
            ResultConsumed = false;
        }
        public bool ConsumeResult(long encounterId, long activationId)
        {
            if (EncounterId != encounterId || ActivationId != activationId || PendingResult == Result.None || ResultConsumed) return false;
            ResultConsumed = true;
            return true;
        }
        public void AbortBattle()
        {
            if (EncounterId != 0 && PendingResult == Result.None) PendingResult = Result.Aborted;
        }
        public EncounterSaveData Capture(long tick) => new EncounterSaveData
        {
            Tick = tick, Active = IsActive, EncounterId = EncounterId, ActivationId = ActivationId,
            Result = PendingResult, ResultConsumed = ResultConsumed, Player = player.Model.Capture(), Monster = monster.Capture(),
        };
        public void Restore(EncounterSaveData saved)
        {
            if (saved == null) throw new System.ArgumentNullException(nameof(saved));
            saved.Validate();
            player.Model.Restore(saved.Player);
            monster.Restore(saved.Monster);
            IsActive = saved.Active;
            EncounterId = saved.EncounterId;
            ActivationId = saved.ActivationId;
            PendingResult = saved.Result;
            ResultConsumed = saved.ResultConsumed;
        }

        public void Begin(Vector2 playerSpawn, Vector2[] patrolPoints)
        {
            player.Reset(playerSpawn);
            monster.Reset(patrolPoints);
            EncounterId = ActivationId = 0;
            PendingResult = Result.None;
            ResultConsumed = false;
            IsActive = true;
        }

        public void End() => IsActive = false;

        public void Step(in SimulationContext context)
        {
            if (!IsActive || (PendingResult != Result.None && !ResultConsumed))
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
            if (EncounterId != 0 && PendingResult == Result.None)
                PendingResult = player.Model.Health == 0 ? Result.Defeat : monster.Model.Health == 0 ? Result.Victory : Result.None;
        }

        public void Serialize(IStateWriter writer)
        {
            writer.WriteBool(IsActive);
            writer.WriteLong(EncounterId);
            writer.WriteLong(ActivationId);
            writer.WriteByte((byte)PendingResult);
            writer.WriteBool(ResultConsumed);
        }

        public void Deserialize(IStateReader reader)
        {
            IsActive = reader.ReadBool();
            EncounterId = reader.ReadLong();
            ActivationId = reader.ReadLong();
            PendingResult = (Result)reader.ReadByte();
            ResultConsumed = reader.ReadBool();
        }
    }
}
