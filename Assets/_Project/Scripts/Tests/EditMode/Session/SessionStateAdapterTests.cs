// 职责：钉住存档闸里「战斗未了」那一项（SessionStateAdapter.BattleResultPending）的口径：
//   ① 实时遭遇升级回合制在途（PRP/combat-iteration D5，用户拍板「战中不可存档」）→ 为真；交回结果或放弃之后 → 为假；
//   ② 回归：原有的「终局已出且未被消费」照旧为真，消费后为假；什么都没发生时为假。
// 为什么新建：适配器此前没有自己的用例（GameSession 的测试用的是假 ISessionStateSource）；本波把升级在途并进这一项，
//   「存档只经 SaveGateRules 闸门、不另开通道」（session-module-guide :139）的落点就是它，需要一条从真实 EncounterStep 读出来的用例。
// 夹具：EncounterStep 是真的（玩家 + 一只不接种类表的怪 + 假升级端口）；适配器的其余构造依赖（对白、UI、遭遇状态、任务、存档）
//   本组属性一个都不读，只为过构造的判空——用 FormatterServices.GetUninitializedObject 造未初始化实例，不跑它们的构造函数
//   （真构造要拉起 Addressables / 输入 / 表数据，与被测的那一行无关）。
using System.Runtime.Serialization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Flow;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Dialogue;
using Game.Monster;
using Game.Player;
using Game.Quest;
using Game.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Session
{
    public sealed class SessionStateAdapterTests
    {
        /// <summary>怪在玩家右侧 0.5 米、背对玩家：玩家面朝右一刀打得到，巡逻态 = 未察觉（偷袭情形）。</summary>
        private static readonly Vector2[] AheadUnaware = { new Vector2(0.5f, 0f), new Vector2(10f, 0f) };

        private PlayerConfig playerConfig;
        private MonsterConfig monsterConfig;
        private SessionConfig sessionConfig;
        private PlayerRules player;
        private MonsterRules monster;
        private EncounterStep step;
        private SessionStateAdapter adapter;

        [TestCase(2, "enter_r0026", true)]
        [TestCase(2, "action_r0070", true)]
        [TestCase(2, "enter_r0083", true)]
        [TestCase(2, "enter_r0129", true)]
        [TestCase(2, "enter_r0171", false)]
        [TestCase(2, "enter_r0188", false)]
        [TestCase(2, "action_r0197", false)]
        [TestCase(3, "enter_r0026", false)]
        [TestCase(3, "enter_r0171", false)]
        public void LegacySceneRoute_OnlyMigratesOldHumanWaits(int version, string stage, bool human)
        {
            var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<EncounterSaveData>("{\"SceneKey\":\"IsometricEncounter\",\"Active\":true}");
            if (version < saved.Version) saved.Migrate(version);
            var narrative = new Game.Narrative.NarrativeSaveData { Current = new Game.Narrative.NarrativeSaveData.Frame {
                StoryId = "spotlight_current", StageId = stage } };
            Assert.That(SessionStateAdapter.ResolveLegacyStoryScene(saved, narrative), Is.EqualTo(human ? "SpotlightHuman" : "IsometricEncounter"));
        }

        [SetUp]
        public void SetUp()
        {
            playerConfig = ScriptableObject.CreateInstance<PlayerConfig>();
            monsterConfig = ScriptableObject.CreateInstance<MonsterConfig>();
            sessionConfig = ScriptableObject.CreateInstance<SessionConfig>();
            var serialized = new SerializedObject(monsterConfig);
            serialized.FindProperty("escalateOnSneakStrike").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            player = new PlayerRules(playerConfig, new PlayerModel(), NullTelemetryScope.Instance);
            monster = new MonsterRules(monsterConfig, new MonsterModel(), new RandomService(3ul), NullTelemetryScope.Instance);
            step = new EncounterStep(player, monster);
            step.BindEscalation(new AcceptingPort());
            step.Begin(Vector2.zero, AheadUnaware);

            adapter = new SessionStateAdapter(new StubFlow(), Placeholder<DialogueService>(), Placeholder<UIService>(), step,
                Placeholder<MonsterEncounterState>(), Placeholder<QuestService>(), Placeholder<JsonSaveService>(), sessionConfig);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(playerConfig);
            Object.DestroyImmediate(monsterConfig);
            Object.DestroyImmediate(sessionConfig);
        }

        [Test]
        public void BattleResultPending_NothingHappened_IsFalse() =>
            Assert.That(adapter.BattleResultPending, Is.False);

        /// <summary>升级在途 → 存档闸关；战斗交回结果（这里是打赢）→ 闸开。</summary>
        [Test]
        public void BattleResultPending_WhileEscalating_IsTrue_ThenFalseAfterCompletion()
        {
            Attack();
            Assert.That(step.IsEscalating, Is.True, "前提：这一刀升级成了回合制");

            Assert.That(adapter.BattleResultPending, Is.True, "战中不可存档");
            Assert.That(SaveGateRules.CanSave(true, false, false, adapter.BattleResultPending), Is.False, "经同一道闸门挡住");

            Assert.That(step.CompleteEscalation(true, 10, 10), Is.True);

            Assert.That(adapter.BattleResultPending, Is.False, "打完（自由探索里没有登记「一仗」，不留待消费的结果）");
        }

        /// <summary>战斗没开成、遭遇放弃升级 → 闸开。</summary>
        [Test]
        public void BattleResultPending_AfterAbort_IsFalse()
        {
            Attack();
            Assert.That(adapter.BattleResultPending, Is.True);

            step.AbortEscalation();

            Assert.That(adapter.BattleResultPending, Is.False);
        }

        /// <summary>回归：原有口径——登记过「一仗」、终局已出且未消费 → 真；消费之后 → 假（整段战后探索仍可存档）。</summary>
        [Test]
        public void BattleResultPending_UnconsumedResult_IsTrueUntilConsumed()
        {
            step.StartBattle(1, 1);
            Assert.That(monster.ApplyExecution(), Is.True);
            Tick(0u);
            Assert.That(step.PendingResult, Is.EqualTo(EncounterStep.Result.Victory), "前提：结果已出");

            Assert.That(adapter.BattleResultPending, Is.True);

            Assert.That(step.ConsumeResult(1, 1), Is.True);
            Assert.That(adapter.BattleResultPending, Is.False);
        }

        private void Attack() => Tick(InputCommand.ButtonAttack);

        private void Tick(uint buttons)
        {
            var command = new InputCommand(Vector2.zero, Vector2.zero, buttons, Vector2.zero, 0);
            var context = new SimulationContext(0, 0.1f, in command, new RandomService(3ul));
            step.Step(in context);
        }

        // 只为过适配器构造的判空：不跑构造函数、不初始化任何字段（被测属性不读它们，见文件头）。
        private static T Placeholder<T>() where T : class => (T)FormatterServices.GetUninitializedObject(typeof(T));

        /// <summary>一律接受、什么都不做的升级端口（战斗侧由 Battle 组的用例覆盖）。</summary>
        private sealed class AcceptingPort : IEncounterEscalationPort
        {
            public bool CanEscalate(int kindId) => true;

            public bool TryEscalate(EncounterStep encounter, in EncounterEscalationRequest request, out string rejectReason)
            {
                rejectReason = null;
                return true;
            }
        }

        private sealed class StubFlow : IGameFlow
        {
            public GameState Current => null;

            public UniTask GoToAsync<TState>(CancellationToken ct = default) where TState : GameState => UniTask.CompletedTask;
        }
    }
}
