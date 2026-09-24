// 职责：在等距纸片场景回放潜行接近、红区警戒、追击、普通攻击与死亡，以及角色走上灰盒楼梯时身体贴地抬升。
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
            BindEncounter();

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

        [UnityTest]
        public IEnumerator WalkOntoStairs_RaisesBody()
        {
            BindEncounter();
            // 楼梯各级的逻辑坐标 = 场景 XZ；只读场景标记位置，不碰视图私有实现。
            Vector2 step1 = ToLogic(FindRequired<Transform>("Stairs_Step_1").position);
            Vector2 step2 = ToLogic(FindRequired<Transform>("Stairs_Step_2").position);
            Vector2 step3 = ToLogic(FindRequired<Transform>("Stairs_Step_3").position);
            Vector2 flat = view.PlayerStart;

            yield return Step("玩家回到平地出生点，记录地面高度基准", () => { player.Reset(flat); Step(0u, 0.1f); });
            yield return null;
            float baseline = view.PlayerScenePosition.y;

            // 每级抬升 0.3，低于单帧步高上限；逐级走上去，每级至少过一帧让视图贴地。
            yield return Step("玩家走上楼梯第 1 级", () => { player.Reset(step1); Step(0u, 0.1f); });
            yield return null;
            yield return Step("玩家走上楼梯第 2 级", () => { player.Reset(step2); Step(0u, 0.1f); });
            yield return null;
            yield return Step("玩家走上楼梯第 3 级", () => { player.Reset(step3); Step(0u, 0.1f); });
            yield return Check("玩家身体抬到第 3 级台阶上（比地面基准高 0.55 以上）",
                () => view.PlayerScenePosition.y >= baseline + 0.55f, 2f);
            yield return Snapshot("站上楼梯第3级");

            yield return Step("玩家回到平地出生点", () => { player.Reset(flat); Step(0u, 0.1f); });
            yield return Check("玩家身体落回地面高度基准",
                () => Mathf.Abs(view.PlayerScenePosition.y - baseline) <= 0.05f, 2f);
            yield return Snapshot("回到平地");
        }

        private static Vector2 ToLogic(Vector3 scenePosition) => new Vector2(scenePosition.x, scenePosition.z);

        private void BindEncounter()
        {
            tick = 0;
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
        }

        private void Step(uint buttons, float deltaTime = 0f)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(tick++, deltaTime, in command, random);
            encounter.Step(in context);
        }
    }
}
