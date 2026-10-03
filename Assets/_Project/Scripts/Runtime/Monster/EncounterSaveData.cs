// 职责：战斗持久快照与待消费结果；不复用会随回放版本作废的二进制布局。
using System;
using Game.Core.Save;
using Game.Player;
using Game.Taming;
using System.Collections.Generic;

namespace Game.Monster
{
    public sealed class EncounterSaveData : ISaveData
    {
        public int Version => 2;
        public string SceneKey { get; set; } = "IsometricEncounter";
        public long Tick { get; set; }
        public bool Active { get; set; }
        public long EncounterId { get; set; }
        public long ActivationId { get; set; }
        public EncounterStep.Result Result { get; set; }
        public bool ResultConsumed { get; set; }
        public PlayerSaveData Player { get; set; }
        public MonsterSaveData Monster { get; set; }
        // v1 缺失这些字段时保留原怪物快照，其余场景巡逻者从作者路线初始化。
        public TamingTargetSaveData[] TamingTargets { get; set; }
        public string ControlledActorId { get; set; }
        public string PlayerActorId { get; set; } = "player";
        public bool PreviousTame { get; set; }
        public void Migrate(int fromVersion) { }
        public void Validate()
        {
            if (SceneKey != "IsometricEncounter" || Tick < 0 || EncounterId < 0 || ActivationId < 0 ||
                (EncounterId == 0) != (ActivationId == 0) || Player == null || Monster == null ||
                !Enum.IsDefined(typeof(EncounterStep.Result), Result) ||
                (Result != EncounterStep.Result.None && EncounterId == 0) ||
                (ResultConsumed && Result == EncounterStep.Result.None))
                throw new ArgumentException("遭遇快照身份或结果非法");
            Player.Validate();
            Monster.Validate();
            if (TamingTargets != null)
            {
                if (TamingTargets.Length == 0 || TamingTargets.Length > 128) throw new ArgumentException("巡逻者快照数量非法");
                var ids = new HashSet<string>(StringComparer.Ordinal);
                bool validOwner = ControlledActorId == PlayerActorId;
                foreach (TamingTargetSaveData entry in TamingTargets)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 128 || !ids.Add(entry.Id) || entry.Monster == null)
                        throw new ArgumentException("巡逻者快照标识缺失或重复");
                    entry.Monster.Validate();
                    if (entry.Id == PlayerActorId) throw new ArgumentException("巡逻者与玩家标识重复");
                    if (entry.Id == ControlledActorId)
                        validOwner = entry.IsTamed && entry.Monster.Health > 0 && Player.Health > 0;
                }
                if (!Monster.HasSameState(TamingTargets[0].Monster)) throw new ArgumentException("主巡逻者快照不一致");
                if (string.IsNullOrWhiteSpace(PlayerActorId) || PlayerActorId.Length > 128 || string.IsNullOrWhiteSpace(ControlledActorId) || !validOwner)
                    throw new ArgumentException("控制标识缺失或目标未驯服/已死亡");
            }
            if (Result == EncounterStep.Result.Victory && (Monster.Health != 0 || Player.Health == 0))
                throw new ArgumentException("胜利结果与生命不符");
            if (Result == EncounterStep.Result.Defeat && Player.Health != 0)
                throw new ArgumentException("失败结果与生命不符");
        }
    }
}
