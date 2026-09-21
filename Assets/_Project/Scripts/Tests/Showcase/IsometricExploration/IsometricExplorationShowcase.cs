// 职责：在等距纸片场景回放潜行接近、红区警戒、追击、普通攻击与死亡。
// 为什么新建：Player/Monster Showcase 只使用代码生成的二维占位图，无法验证 XZ 场景适配。
using System.Collections;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Monster;
using Game.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.IsometricExploration
{
    [Category("Showcase")]
    public sealed class IsometricExplorationShowcase : ShowcaseScenario
    {
        private EncounterSceneView view;
        private PlayerRules player;
        private MonsterRules monster;
        private EncounterStep encounter;
        private RandomService random;
        private long tick;

        protected override string Module => "IsometricExploration";
        protected override string ScenePath => "Assets/Scenes/SampleScene.unity";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator SneakApproach_ThenAttack_KillsMonster()
        {
            Vector2 patrolStart = default;
            Vector2 chaseStart = default;
            view = Object.FindObjectOfType<EncounterSceneView>();
            Assert.That(view, Is.Not.Null, "IsometricEncounter 场景必须显式接入 EncounterSceneView");
            var standalone = Object.FindObjectOfType<StandaloneEncounterController>();
            if (standalone != null) standalone.ManualSimulation = true;
            PlayerConfig playerConfig = Track(ScriptableObject.CreateInstance<PlayerConfig>());
            MonsterConfig monsterConfig = Track(ScriptableObject.CreateInstance<MonsterConfig>());
            var playerModel = new PlayerModel();
            player = new PlayerRules(playerConfig, playerModel, NullTelemetryScope.Instance);
            var monsterModel = new MonsterModel();
            random = new RandomService(123ul);
            monster = new MonsterRules(monsterConfig, monsterModel, random, NullTelemetryScope.Instance);
            encounter = new EncounterStep(player, monster);
            encounter.Begin(view.PlayerStart, view.PatrolPositions());
            view.Bind(playerModel, monsterModel);

            yield return Step("敌人沿路线巡逻", () =>
            {
                patrolStart = monster.Model.Position;
                Step(0u, 0.5f);
            });
            yield return Check("敌人发生巡逻位移", () => monster.Model.Position != patrolStart);

            yield return Step("玩家在怪物背后潜行接近", () =>
            {
                player.Reset(monster.Model.Position - Vector2.right);
                Step(InputCommand.ButtonSneak);
            });
            yield return Check("潜行避免背后近距警戒", () => monster.Model.Mode == MonsterMode.PatrolWalk);
            yield return Snapshot("潜行接近");

            yield return Step("玩家潜行进入正面红区", () =>
            {
                player.Reset(monster.Model.Position + Vector2.right);
                Step(InputCommand.ButtonSneak);
                chaseStart = monster.Model.Position;
            });
            yield return Check("红区仍触发敌对", () => monster.Model.Mode == MonsterMode.Hostile);
            yield return Step("敌人向玩家追击", () => Step(InputCommand.ButtonSneak, 0.5f));
            yield return Check("敌人发生追击位移", () => monster.Model.Position != chaseStart);
            yield return Snapshot("敌对追击");

            yield return Step("玩家近身连续普通攻击", () =>
            {
                player.Reset(monster.Model.Position - Vector2.right * 0.5f);
                for (int i = 0; i < 3; i++)
                {
                    Step(InputCommand.ButtonAttack, 1f);
                    Step(0u, 0f);
                }
            });
            yield return Check("怪物生命归零并停止行动",
                () => monster.Model.Health == 0 && monster.Model.Mode == MonsterMode.Dead);
            yield return Snapshot("普通攻击击杀");
        }

        private void Step(uint buttons, float deltaTime = 0f)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(tick++, deltaTime, in command, random);
            encounter.Step(in context);
        }
    }
}
