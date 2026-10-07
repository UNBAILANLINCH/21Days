// 职责：钉住遭遇快照的场景键判据「去写死」之后的口径——旧档（IsometricEncounter）仍通过、
//   空 / 带空白 / 带分隔符的键被拒、**格式合法但表里查不到**的键不由这里拒（那是调用方的事）。
// 为什么新建：这是 PRP/world-scenes §2.2 的硬阻塞点的修复，而它最容易改错的方向是「顺手把判据收紧」——
//   收紧就会让遗留原型路径（MonsterEncounterState 的常量地址）的旧档一存就读不回来。
using System;
using Game.Monster;
using Game.Player;
using NUnit.Framework;

namespace Game.Tests.EditMode.Monster
{
    /// <summary><see cref="EncounterSaveData"/> 的场景键校验测试。</summary>
    public sealed class EncounterSaveDataSceneKeyTests
    {
        /// <summary>遗留原型路径的地址（MonsterEncounterState.cs:46 的常量，本波禁改，两条并存）。</summary>
        private const string LegacyAddress = "IsometricEncounter";

        // ---------------------------------------------------------------- 旧档仍通过（改判据前必须先有这条）

        [Test]
        public void Validate_LegacyIsometricEncounterKey_StillPasses()
        {
            EncounterSaveData snapshot = Snapshot(LegacyAddress);

            Assert.That(() => snapshot.Validate(), Throws.Nothing,
                "旧档里的 IsometricEncounter 必须照旧通过——它是遗留原型路径的地址，不是 snake_case");
        }

        [Test]
        public void Validate_WorldSceneKey_Passes()
        {
            // 负对照的另一半：新路径的世界场景键（TbScene.scene_key）也要通过，
            // 否则「去写死」只换了一个字面量而已。
            Assert.That(() => Snapshot("human_jingyang").Validate(), Throws.Nothing);
            Assert.That(() => Snapshot("yao_fangshi").Validate(), Throws.Nothing);
        }

        [Test]
        public void DefaultSceneKey_IsStillTheLegacyAddress()
        {
            // 属性初始化器不动：MonsterEncounterState 那条路依赖这个默认值（本波禁改它）。
            Assert.That(new EncounterSaveData().SceneKey, Is.EqualTo(LegacyAddress));
        }

        // ---------------------------------------------------------------- 格式非法的四种（负对照）

        [Test]
        public void Validate_EmptySceneKey_Throws()
        {
            Assert.That(() => Snapshot(string.Empty).Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => Snapshot(null).Validate(), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Validate_SceneKeyWithWhitespace_Throws()
        {
            // 地址里出现空白一定是配错了（TbScene.scene_key 与 Addressables 地址都不含空白）。
            Assert.That(() => Snapshot("   ").Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => Snapshot("human jingyang").Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => Snapshot("human_jingyang\n").Validate(), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Validate_SceneKeyWithScopeSeparator_Throws()
        {
            // SceneStateKey 用「场景键::实体标识」拼键（Game.World.SceneStateKey.Separator），
            // 场景键里再带一次分隔符会拼出两义键。
            Assert.That(() => Snapshot("human_jingyang::street").Validate(), Throws.TypeOf<ArgumentException>());
        }

        // ---------------------------------------------------------------- 责任边界：表里有没有这个地址不归 Validate 判

        [Test]
        public void Validate_WellFormedButUnknownSceneKey_Passes_ExistenceIsTheCallersJob()
        {
            // 这是本波的分工（PRP §2.2）：Validate 是**纯数据方法**（不许依赖 IConfigService），
            // 所以「这个地址在不在 TbScene / Addressables 里」它不判。
            // 那一半的落点：世界场景入口 Game.World.WorldTransition.TryConsume（调用方，按 WorldCatalog 校验，
            // 见 WorldTransitionTests.TryConsume_SceneNotInTable_ReportsSceneUnknownAndListsKnownKeys）。
            Assert.That(() => Snapshot("nowhere_scene").Validate(), Throws.Nothing,
                "格式合法的未知场景键在 Validate 这一层是合法的；它由调用方按表拒");
        }

        [Test]
        public void Validate_OtherRulesStillHold()
        {
            // 负对照：只放宽了场景键这一条，别的判据一条都没松（Tick / 结果 / 生命那几条照旧）。
            EncounterSaveData negativeTick = Snapshot("human_jingyang");
            negativeTick.Tick = -1;
            Assert.That(() => negativeTick.Validate(), Throws.TypeOf<ArgumentException>());

            EncounterSaveData missingPlayer = Snapshot("human_jingyang");
            missingPlayer.Player = null;
            Assert.That(() => missingPlayer.Validate(), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Version_IsTwo_AndLegacySnapshotRemainsValidAfterMigration()
        {
            // 多目标控制已升级到 v2；场景键放宽不再升级版本，v1 单怪物快照仍须兼容。
            var data = Snapshot(LegacyAddress);
            PlayerSaveData originalPlayer = data.Player;
            MonsterSaveData originalMonster = data.Monster;

            Assert.That(data.Version, Is.EqualTo(2));
            Assert.That(() => data.Migrate(1), Throws.Nothing);
            Assert.That(data.SceneKey, Is.EqualTo(LegacyAddress));
            Assert.That(data.Player, Is.SameAs(originalPlayer));
            Assert.That(data.Monster, Is.SameAs(originalMonster));
            Assert.That(data.TamingTargets, Is.Null);
            Assert.That(data.PlayerActorId, Is.EqualTo("player"));
            Assert.That(() => data.Validate(), Throws.Nothing);
        }

        // ---------------------------------------------------------------- 辅助

        private static EncounterSaveData Snapshot(string sceneKey) => new EncounterSaveData
        {
            SceneKey = sceneKey,
            Player = new PlayerSaveData { Health = 10 },
            Monster = new MonsterSaveData
            {
                PositionX = 0f,
                PositionY = 0f,
                Mode = MonsterMode.PatrolWalk,
                Health = 10,
                WaypointX = new[] { 0f },
                WaypointY = new[] { 0f },
                NextPauseAfter = 2f,
            },
        };
    }
}
