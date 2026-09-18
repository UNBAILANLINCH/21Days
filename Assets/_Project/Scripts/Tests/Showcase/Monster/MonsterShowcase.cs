// 职责：以占位图和警戒条回放怪物巡逻、感知、追击、攻击及死亡。
using System.Collections;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Monster
{
    [Category("Showcase")]
    public sealed class MonsterShowcase : ShowcaseScenario
    {
        private PlayerModel player;
        private PlayerRules playerRules;
        private MonsterModel monster;
        private MonsterRules rules;

        protected override string Module => "Monster";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator MonsterStates_AreVisible()
        {
            EnsureCamera();
            var config = Track(ScriptableObject.CreateInstance<MonsterConfig>());
            player = new PlayerModel();
            playerRules = new PlayerRules(Track(ScriptableObject.CreateInstance<PlayerConfig>()), player,
                NullTelemetryScope.Instance);
            playerRules.Reset(new Vector2(4f, 0f));
            monster = new MonsterModel();
            rules = new MonsterRules(config, monster, new RandomService(123ul), NullTelemetryScope.Instance);
            rules.Reset(new[] { Vector2.zero, new Vector2(2f, 0f) });
            EncounterSceneView view = Track(new GameObject("Monster Showcase View")).AddComponent<EncounterSceneView>();
            view.Bind(player, monster);

            yield return Step("沿巡逻点行走", () => StepMonster(NoTarget(), 1f));
            yield return Check("巡逻中", () => monster.Mode == MonsterMode.PatrolWalk);
            yield return Snapshot("巡逻");

            yield return Step("在橙区发现玩家", () =>
            {
                rules.Reset(new[] { Vector2.zero, new Vector2(2f, 0f) });
                playerRules.Reset(monster.Position + Vector2.right * 4f);
                StepMonster(player.Snapshot, 1f);
            });
            yield return Check("警戒条开始增长", () => monster.Mode == MonsterMode.Alert && monster.Alert > 0f);
            yield return Snapshot("警戒");

            yield return Step("持续暴露后追击", () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    playerRules.Reset(monster.Position + Vector2.right * 4f);
                    StepMonster(player.Snapshot, 1f);
                }
            });
            yield return Check("怪物进入敌对", () => monster.Mode == MonsterMode.Hostile);
            yield return Snapshot("追击");

            yield return Step("靠近后攻击", () =>
            {
                playerRules.Reset(monster.Position + Vector2.right * 0.5f);
                PlayerSnapshot target = player.Snapshot;
                if (StepMonster(target, 0.5f))
                {
                    playerRules.ApplyDamage(new DamageIntent(config.AttackDamage));
                }
            });
            yield return Check("玩家受到伤害", () => player.Health == 2);
            yield return Snapshot("命中");

            yield return Step("玩家反击击败怪物", () =>
            {
                var damage = new DamageIntent(3);
                PlayerSnapshot attacker = player.Snapshot;
                rules.ApplyDamage(in damage, in attacker);
            });
            yield return Check("怪物死亡", () => monster.Mode == MonsterMode.Dead);
            yield return Snapshot("死亡");
        }

        private bool StepMonster(PlayerSnapshot target, float seconds)
        {
            var intent = new MonsterIntent(target, seconds);
            return rules.Step(in intent);
        }

        private static PlayerSnapshot NoTarget() => Target(Vector2.right * 1000f);

        private static PlayerSnapshot Target(Vector2 position) =>
            new PlayerSnapshot(position, Vector2.left, false, false, 3);
    }
}
