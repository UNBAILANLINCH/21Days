// 职责：验证环境只由剧情快照派生；现有引导测试不覆盖天气与局部遭遇继承。
using Game.Narrative;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Game.Tests.EditMode.Narrative
{
    public sealed class SpotlightEnvironmentRulesTests
    {
        [TestCase(4, 4, 2, 2, false, true, true)]
        [TestCase(4, 5, 2, 2, false, true, false)]
        [TestCase(4, 4, 2, 3, false, true, false)]
        [TestCase(4, 4, 2, 2, true, true, false)]
        [TestCase(4, 4, 2, 2, false, false, false)]
        public void TransitionOwnership_RejectsReplacedOperationSessionOrFlow(int operation, int currentOperation,
            int generation, int currentGeneration, bool changing, bool sameOwner, bool expected)
        {
            Assert.That(SpotlightEnvironmentRules.OwnsTransition(operation, currentOperation,
                generation, currentGeneration, changing, sameOwner), Is.EqualTo(expected));
        }

        [Test]
        public void Resolve_NewGameDream_UsesSnowAndAltarFire()
        {
            var state = SpotlightEnvironmentRules.Resolve(Snapshot("talk_21000"));
            Assert.That(state.Lighting, Is.EqualTo(SpotlightEnvironmentRules.Preset.MoonNight));
            Assert.That(state.Snow, Is.True);
            Assert.That(state.AltarFire, Is.True);
            Assert.That(state.Candles, Is.False);
            Assert.That(state.WaterMist, Is.False);
        }

        [TestCase("talk_21001")]
        [TestCase("talk_21002")]
        public void Resolve_LaterDream_RemovesAltarFire(string stage)
        {
            var state = SpotlightEnvironmentRules.Resolve(Snapshot(stage));
            Assert.That(state.Snow, Is.True);
            Assert.That(state.AltarFire, Is.False);
        }

        [TestCase("talk_21003")]
        [TestCase("enter_r0026")]
        [TestCase("talk_21004")]
        [TestCase("action_r0070")]
        [TestCase("talk_21005")]
        public void Resolve_WakeAndMentor_UsesDawnWithoutWeather(string stage)
        {
            Assert.That(SpotlightEnvironmentRules.Resolve(Snapshot(stage)),
                Is.EqualTo(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.Dawn)));
        }

        [TestCase("enter_r0083")]
        [TestCase("talk_21006")]
        public void Resolve_ZhaoShisi_RemainsDayUntilDialogueCompletes(string stage)
        {
            Assert.That(SpotlightEnvironmentRules.Resolve(Snapshot(stage)).Lighting,
                Is.EqualTo(SpotlightEnvironmentRules.Preset.Day));
        }

        [TestCase("enter_r0129")]
        [TestCase("talk_21007")]
        public void Resolve_StageOneLibrary_ChangesToNightWithCandles(string stage)
        {
            Assert.That(SpotlightEnvironmentRules.Resolve(Snapshot(stage)),
                Is.EqualTo(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.MoonNight, candles: true)));
        }

        [TestCase("spotlight_mentor_duel", "action_r0070", SpotlightEnvironmentRules.Preset.Dawn)]
        [TestCase("shop_boss_battle", "action_r0197", SpotlightEnvironmentRules.Preset.MoonNight)]
        public void Resolve_LocalEncounter_InheritsFormalParent(string story, string parentStage,
            SpotlightEnvironmentRules.Preset expected)
        {
            var saved = Snapshot("battle", story);
            saved.Parent = Snapshot(parentStage).Current;
            Assert.That(SpotlightEnvironmentRules.Resolve(saved).Lighting, Is.EqualTo(expected));
        }

        [TestCase("enter_r0215", false, false)]
        [TestCase("talk_21011", false, false)]
        [TestCase("enter_r0293", false, false)]
        [TestCase("action_r0304", false, false)]
        [TestCase("enter_r0339", true, false)]
        [TestCase("talk_21014", true, false)]
        [TestCase("enter_r0384", false, true)]
        [TestCase("talk_21015", false, true)]
        [TestCase("enter_r0397", false, true)]
        [TestCase("talk_21016", false, true)]
        [TestCase("enter_r0427", false, true)]
        [TestCase("talk_21017", false, true)]
        [TestCase("action_r0434", false, true)]
        [TestCase("enter_r0435", false, false)]
        [TestCase("action_r0445", false, false)]
        [TestCase("enter_r0469", true, false)]
        [TestCase("talk_21020", true, false)]
        [TestCase("action_r0478", true, false)]
        public void Resolve_StageTwoAndThree_KeepNightWithLocalEffects(string stage, bool candles, bool mist)
        {
            Assert.That(SpotlightEnvironmentRules.Resolve(Snapshot(stage)),
                Is.EqualTo(new SpotlightEnvironmentRules.State(SpotlightEnvironmentRules.Preset.MoonNight,
                    candles: candles, waterMist: mist)));
        }

        [TestCase("talk_21000")]
        [TestCase("action_r0070")]
        [TestCase("enter_r0083")]
        [TestCase("enter_r0129")]
        [TestCase("enter_r0384")]
        public void Resolve_SameStageAndJsonLoad_AreIdenticalAndDoNotMutateSave(string stage)
        {
            var snapshot = Snapshot(stage);
            string before = JsonConvert.SerializeObject(snapshot);
            var first = SpotlightEnvironmentRules.Resolve(snapshot);
            var loaded = JsonConvert.DeserializeObject<NarrativeSaveData>(before);
            Assert.That(SpotlightEnvironmentRules.Resolve(snapshot), Is.EqualTo(first));
            Assert.That(SpotlightEnvironmentRules.Resolve(loaded), Is.EqualTo(first));
            Assert.That(JsonConvert.SerializeObject(snapshot), Is.EqualTo(before));
        }

        [TestCase("spotlight_current", "talk_29999")]
        [TestCase("spotlight_current", "enter_r0500")]
        [TestCase("spotlight_current", "end")]
        [TestCase("sample_wait", "enter_r0384")]
        [TestCase("shop_boss_battle", "battle")]
        [TestCase("", "")]
        public void Resolve_UnknownOrNonFormal_ReturnsNone(string story, string stage)
        {
            Assert.That(SpotlightEnvironmentRules.Resolve(Snapshot(stage, story)),
                Is.EqualTo(default(SpotlightEnvironmentRules.State)));
        }

        [Test]
        public void Resolve_MissingSaveOrFrames_ReturnsNone()
        {
            Assert.That(SpotlightEnvironmentRules.Resolve(null), Is.EqualTo(default(SpotlightEnvironmentRules.State)));
            Assert.That(SpotlightEnvironmentRules.Resolve(new NarrativeSaveData()),
                Is.EqualTo(default(SpotlightEnvironmentRules.State)));
        }

        private static NarrativeSaveData Snapshot(string stage, string story = "spotlight_current") =>
            new NarrativeSaveData { Current = new NarrativeSaveData.Frame { StoryId = story, StageId = stage } };
    }
}
