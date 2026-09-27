// 职责：以占位图展示玩家移动、潜行、伪装、攻防及死亡。
using System.Collections;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.Player
{
    [Category("Showcase")]
    public sealed class PlayerShowcase : ShowcaseScenario
    {
        private PlayerConfig config;
        private PlayerModel player;
        private PlayerRules rules;

        protected override string Module => "Player";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator PlayerActions_AreVisible()
        {
            EnsureCamera();
            config = Track(ScriptableObject.CreateInstance<PlayerConfig>());
            player = new PlayerModel();
            rules = new PlayerRules(config, player, NullTelemetryScope.Instance);
            rules.Reset(Vector2.left * 2f);

            MonsterConfig monsterConfig = Track(ScriptableObject.CreateInstance<MonsterConfig>());
            var monster = new MonsterModel();
            var monsterRules = new MonsterRules(monsterConfig, monster,
                new Game.Core.Simulation.RandomService(1ul), NullTelemetryScope.Instance);
            monsterRules.Reset(new[] { Vector2.right * 2f });
            EncounterSceneView view = Track(new GameObject("Player Showcase View")).AddComponent<EncounterSceneView>();
            view.Bind(player, monster);

            yield return Step("玩家向右移动", () => rules.Step(
                new PlayerIntent(Vector2.right, false, false, false, false), 0.5f));
            yield return Check("玩家已移动", () => player.Position.x > -2f);
            yield return Snapshot("移动");

            yield return Step("潜行并开启伪装", () => rules.Step(
                new PlayerIntent(Vector2.right, true, true, false, false), 0.5f));
            yield return Check("潜行与伪装状态已生效", () => player.IsSneaking && player.IsDisguised);
            yield return Snapshot("潜行伪装");

            yield return Step("攻击并受到致命伤害", () =>
            {
                rules.Step(new PlayerIntent(Vector2.zero, false, false, true, false), 0f);
                rules.ApplyDamage(new DamageIntent(3));
            });
            yield return Check("玩家死亡", () => !player.Snapshot.IsAlive);
            yield return Snapshot("死亡");
        }
    }
}
