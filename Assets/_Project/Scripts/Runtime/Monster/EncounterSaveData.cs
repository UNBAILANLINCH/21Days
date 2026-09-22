// 职责：战斗持久快照与待消费结果；不复用会随回放版本作废的二进制布局。
using System;
using Game.Core.Save;
using Game.Player;

namespace Game.Monster
{
    public sealed class EncounterSaveData : ISaveData
    {
        public int Version => 1;
        public string SceneKey { get; set; } = "IsometricEncounter";
        public long Tick { get; set; }
        public bool Active { get; set; }
        public long EncounterId { get; set; }
        public long ActivationId { get; set; }
        public EncounterStep.Result Result { get; set; }
        public bool ResultConsumed { get; set; }
        public PlayerSaveData Player { get; set; }
        public MonsterSaveData Monster { get; set; }
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
            if (Result == EncounterStep.Result.Victory && (Monster.Health != 0 || Player.Health == 0))
                throw new ArgumentException("胜利结果与生命不符");
            if (Result == EncounterStep.Result.Defeat && Player.Health != 0)
                throw new ArgumentException("失败结果与生命不符");
        }
    }
}
