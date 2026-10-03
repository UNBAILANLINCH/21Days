// 职责：在同一固定 tick 中先应用玩家意图，再处理怪物感知与战斗。
// 为什么新建：SimulationRunner 只负责调度步骤，Player 与 Monster 的先后属于遭遇玩法。
using Game.Core.Replay;
using Game.Core.Simulation;
using Game.Player;
using Game.Taming;
using Game.Core.Telemetry;
using System;
using UnityEngine;

namespace Game.Monster
{
    public sealed class EncounterStep : ISimulationStep, IReplayState
    {
        public enum Result : byte { None, Victory, Defeat, Aborted }
        private readonly PlayerRules player;
        private readonly MonsterRules monster;
        private Vector2[][] authoredRoutes;

        public EncounterStep(PlayerRules player, MonsterRules monster)
        {
            this.player = player;
            this.monster = monster;
            Taming = new TamingRules(player, monster, NullTelemetryScope.Instance);
        }

        public TamingRules Taming { get; }
        public string CurrentControlId => Taming.CurrentControlId;

        public void ConfigureTaming(string playerId, string playerName, string[] ids, string[] names, Vector2[][] routes)
        {
            if (ids == null || names == null || routes == null || ids.Length == 0 || ids.Length > 128
                || ids.Length != names.Length || ids.Length != routes.Length)
                throw new ArgumentException("巡逻者身份和路线数量不匹配");
            var unique = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { playerId };
            for (int i = 0; i < ids.Length; i++)
                if (string.IsNullOrWhiteSpace(ids[i]) || ids[i].Length > 128 || !unique.Add(ids[i]) || string.IsNullOrWhiteSpace(names[i])
                    || routes[i] == null || routes[i].Length == 0 || routes[i].Length > 1024)
                    throw new ArgumentException("场景巡逻者稳定标识缺失或重复：" + ids[i]);
            Taming.Configure(playerId, playerName);
            authoredRoutes = new Vector2[routes.Length][];
            for (int i = 0; i < ids.Length; i++)
            {
                MonsterRules rules = i == 0 ? monster : monster.CreateForActor(ids[i]);
                authoredRoutes[i] = (Vector2[])routes[i].Clone();
                Taming.RegisterTarget(ids[i], names[i], rules);
            }
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
            TamingTargets = Taming.Capture(), ControlledActorId = CurrentControlId, PlayerActorId = Taming.PlayerId, PreviousTame = Taming.PreviousToggle,
        };
        public void Restore(EncounterSaveData saved)
        {
            if (saved == null) throw new System.ArgumentNullException(nameof(saved));
            saved.Validate();
            if (saved.TamingTargets != null && saved.PlayerActorId != Taming.PlayerId)
                throw new ArgumentException("存档玩家稳定标识与场景不匹配");
            Taming.ValidateRestore(saved.TamingTargets);
            player.Model.Restore(saved.Player);
            if (saved.TamingTargets == null)
            {
                ResetAdditionalTargets();
                monster.Restore(saved.Monster);
            }
            Taming.Restore(saved.TamingTargets, saved.ControlledActorId, saved.PreviousTame);
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
            ResetAdditionalTargets();
            Taming.Reset();
            EncounterId = ActivationId = 0;
            PendingResult = Result.None;
            ResultConsumed = false;
            IsActive = true;
        }

        public void End()
        {
            IsActive = false;
            Taming.TryControl(Taming.PlayerId);
        }

        private void ResetAdditionalTargets()
        {
            if (authoredRoutes == null) return;
            for (int i = 1; i < authoredRoutes.Length; i++) Taming.GetTarget(Taming.TargetIds[i]).Reset(authoredRoutes[i]);
        }

        /// <summary>
        /// 把玩家逻辑位置改成表现层碰撞解算后的结果（PRP/exploration-whitebox 波 9）。
        /// 只允许 EncounterSceneView.OnPlayerBlocked 的回写调用：它是白盒阶段「障碍不在确定性内核里」的补丁，
        /// 其他玩法不要借它挪人（挪人用 PlayerRules.Reset）。未激活时忽略。
        /// <para>
        /// 视图传来的是分轴合成值：被挡的轴是插值点扫掠后的修正值，没被挡的轴原样是逻辑值。
        /// 被改写的轴同时把 PreviousPosition 设成同一值（下一帧不会从墙里倒插回来）；
        /// 没改写的轴保留 PreviousPosition，贴墙滑动时沿墙那一轴继续平滑插值、不损失速度。
        /// </para>
        /// </summary>
        public void CorrectPlayerPosition(Vector2 logicPosition)
        {
            if (!IsActive) return;
            PlayerModel model = player.Model;
            Vector2 current = model.Position;
            Vector2 previous = model.PreviousPosition;
            model.PreviousPosition = new Vector2(
                EncounterProjection.CorrectPreviousAxis(previous.x, current.x, logicPosition.x),
                EncounterProjection.CorrectPreviousAxis(previous.y, current.y, logicPosition.y));
            model.Position = logicPosition;
        }

        public void Step(in SimulationContext context)
        {
            if (!IsActive || (PendingResult != Result.None && !ResultConsumed))
            {
                // 本 tick 双方规则都不推进：仍把上一 tick 位置对齐，否则视图会在结算前最后一步的两点间来回插值。
                player.Model.SyncPreviousPosition();
                monster.Model.SyncPreviousPosition();
                foreach (string id in Taming.TargetIds) Taming.GetTarget(id).Model.SyncPreviousPosition();
                return;
            }

            InputCommand command = context.Input;
            string requested = SelectionId(command.Axis1.x);
            if (command.HasButton(InputCommand.ButtonSelectControl)) Taming.TryControl(requested);
            if (command.HasButton(InputCommand.ButtonTamePressed)) Taming.ProcessToggle(false);
            Taming.ProcessToggle(command.HasButton(InputCommand.ButtonTame) && (command.Axis1.x == 0f || requested != null), requested);
            bool enemyControlled = Taming.IsControllingEnemy;
            var playerIntent = new PlayerIntent(
                enemyControlled ? Vector2.zero : command.Axis0,
                !enemyControlled && command.HasButton(InputCommand.ButtonSneak),
                !enemyControlled && command.HasButton(InputCommand.ButtonDisguise),
                !enemyControlled && command.HasButton(InputCommand.ButtonAttack),
                !enemyControlled && command.HasButton(InputCommand.ButtonRun));
            bool attacked = player.Step(in playerIntent, context.DeltaTime);
            PlayerSnapshot target = player.Model.Snapshot;
            foreach (string id in Taming.TargetIds)
            {
                MonsterRules rules = Taming.GetTarget(id);
                MonsterModel enemy = rules.Model;
                if (!attacked || Taming.IsTargetTamed(id) || !Taming.IsAvailable(id) || enemy.Health <= 0
                    || GameMath.Distance(target.Position, enemy.Position) > player.AttackRange) continue;
                Vector2 difference = enemy.Position - target.Position;
                if (GameMath.SqrMagnitude(difference) == 0f
                    || GameMath.Dot(target.Facing, GameMath.Normalize(difference)) >= 0f)
                {
                    var damage = new DamageIntent(player.AttackDamage);
                    rules.ApplyDamage(in damage, in target);
                }
            }

            Taming.AdvanceTargets(command.Axis0, context.DeltaTime);
            if (EncounterId != 0 && Taming.IsTamed && PendingResult == Result.None) AbortBattle();
            if (EncounterId != 0 && PendingResult == Result.None)
                PendingResult = player.Model.Health == 0 ? Result.Defeat : monster.Model.Health == 0 ? Result.Victory : Result.None;
        }

        private string SelectionId(float selection)
        {
            // 0 表示没有定向请求；1 对应玩家，后续槽位按场景显式数组顺序映射稳定 ID。
            if (float.IsNaN(selection) || float.IsInfinity(selection) || selection < 1f
                || selection > Taming.TargetIds.Count + 1 || selection != (int)selection) return null;
            int index = (int)selection - 1;
            return index == 0 ? Taming.PlayerId : index <= Taming.TargetIds.Count ? Taming.TargetIds[index - 1] : null;
        }

        public void Serialize(IStateWriter writer)
        {
            writer.WriteBool(IsActive);
            writer.WriteLong(EncounterId);
            writer.WriteLong(ActivationId);
            writer.WriteByte((byte)PendingResult);
            writer.WriteBool(ResultConsumed);
            WriteId(writer, Taming.PlayerId);
            WriteId(writer, CurrentControlId);
            writer.WriteBool(Taming.PreviousToggle);
            writer.WriteInt(Taming.TargetIds.Count);
            foreach (string id in Taming.TargetIds)
            {
                WriteId(writer, id);
                writer.WriteBool(Taming.IsTargetTamed(id));
                Taming.GetTarget(id).Serialize(writer);
            }
        }

        public void Deserialize(IStateReader reader)
        {
            IsActive = reader.ReadBool();
            EncounterId = reader.ReadLong();
            ActivationId = reader.ReadLong();
            PendingResult = (Result)reader.ReadByte();
            ResultConsumed = reader.ReadBool();
            if (ReadId(reader) != Taming.PlayerId) throw new InvalidOperationException("回放玩家标识与场景不匹配");
            string controlled = ReadId(reader);
            bool held = reader.ReadBool();
            int count = reader.ReadInt();
            if (count != Taming.TargetIds.Count) throw new InvalidOperationException("回放巡逻者数量与场景不匹配");
            var flags = new bool[count];
            for (int i = 0; i < count; i++)
            {
                string id = ReadId(reader);
                if (id != Taming.TargetIds[i]) throw new InvalidOperationException("回放巡逻者标识与场景不匹配");
                flags[i] = reader.ReadBool();
                Taming.GetTarget(id).Deserialize(reader);
            }
            Taming.RestoreReplayFlags(flags, controlled, held);
        }

        private static void WriteId(IStateWriter writer, string id)
        {
            writer.WriteInt(id.Length);
            foreach (char c in id) writer.WriteUShort(c);
        }

        private static string ReadId(IStateReader reader)
        {
            int count = reader.ReadInt();
            if (count < 1 || count > 128) throw new InvalidOperationException("回放标识长度非法");
            var chars = new char[count];
            for (int i = 0; i < count; i++) chars[i] = (char)reader.ReadUShort();
            return new string(chars);
        }
    }
}
