// 职责：守住 PRP/combat-iteration 在 SampleScene 与四份配置资产里落下的「战斗迭代」接线（T6.5）：
//   1. 巡夜人（Patrol_Xunye）登记在 EncounterSceneView.patrolActors 的下标 2，种类 1001、勾了完整参与者；
//   2. 巡逻者乙（Patrol_B，协作者的线）两个新字段仍是默认值（0 / false）——它靠默认值保持旧行为（TamingActor 文件头）；
//   3. Npc_SampleBoss 挂了 BossAggro（bossId = sample_boss，头顶提示物体已拖、默认关着）；
//   4. 四份资产的口径：露馅后果 = 追逐、六种原因都失去身份；被抓 = 先击倒没血才死、不按次数判死；
//      普通巡逻不按种类升级回合制；显式剧情/BOSS 行仍在、sample_boss 开了发难（半径 5、反应 3 秒）。
// 为什么新建（复用 → 扩展 → 新建）：
//   复用——SampleSceneObstacleWiringTests 守「谁挡人」，ExecutionSceneWiringTests 守处决的容器接线（new GameObject 搭），
//   都不读这几项；扩展——塞进前者职责说不通（碰撞与战斗口径是两件事），后者不读真实场景。
//   刻意读真实场景与真实资产路径（同 SampleSceneObstacleWiringTests）：它守的就是用户拍板后落进 demo 舞台与资产的那份口径，
//   正式场景照 SampleScene 抄（unity-assets.md），这里守住了，抄过去的也对；改了口径要先改这里，等于留一道「有意为之」的门。
// 场景按 Additive 打开、测完关掉；开发者已经开着 SampleScene 时直接读内存里的那份、不关，免得关掉别人的未保存改动。
using System.Collections.Generic;
using Game.Battle;
using Game.Identity;
using Game.Monster;
using Game.Stealth;
using Game.Taming;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode.Monster
{
    public sealed class SampleSceneCombatWiringTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/SampleScene.unity";
        private const string IdentityConfigPath = "Assets/_Project/Data/Identity/IdentityConfig.asset";
        private const string StealthConfigPath = "Assets/_Project/Data/Stealth/StealthConfig.asset";
        private const string MonsterConfigPath = "Assets/_Project/Data/Monster/MonsterConfig.asset";
        private const string BossRosterConfigPath = "Assets/_Project/Data/Battle/BossRosterConfig.asset";

        /// <summary>巡夜人的稳定标识与它在 patrolActors 里的下标（追加在主怪、巡逻者乙之后）。</summary>
        private const string XunyeStableId = "patrol_xunyeren";
        private const int XunyeIndex = 2;
        private const int XunyeKindId = 1001;

        /// <summary>巡逻者乙的稳定标识与下标（协作者的线，本波不改它）。</summary>
        private const string PatrolBStableId = "patrol-b";
        private const int PatrolBIndex = 1;

        /// <summary>普通巡逻的主怪种类：保留实时警戒、追逐与抓捕，不自动升级回合制。</summary>
        private const int ShilingKindId = 1003;

        private const string BossObjectName = "Npc_SampleBoss";
        private const string SampleBossId = "sample_boss";

        private Scene scene;
        private bool openedHere;
        private bool wasListed;

        [SetUp]
        public void SetUp()
        {
            scene = SceneManager.GetSceneByPath(ScenePath);
            wasListed = scene.IsValid();
            openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.True, "打不开 " + ScenePath);
        }

        [TearDown]
        public void TearDown()
        {
            // 只关自己打开的；原本就在层级里（未加载）的只卸载、不移除。
            if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, !wasListed);
        }

        // ───────────────────────── 场景 ─────────────────────────

        [Test]
        public void Xunye_IsThirdPatrolActor_WithKind1001AndFullParticipant()
        {
            TamingActor[] actors = RequireSingleView().PatrolActors;
            Assert.That(actors, Is.Not.Null.And.Length.GreaterThan(XunyeIndex),
                $"patrolActors 至少要有 {XunyeIndex + 1} 只（主怪、巡逻者乙、巡夜人）");

            TamingActor xunye = actors[XunyeIndex];
            Assert.That(xunye != null, Is.True, $"patrolActors[{XunyeIndex}] 是空引用");
            Assert.That(xunye.StableId, Is.EqualTo(XunyeStableId), $"patrolActors[{XunyeIndex}] 应是巡夜人");
            Assert.That(xunye.KindId, Is.EqualTo(XunyeKindId), "巡夜人的种类覆盖应是 1001");
            Assert.That(xunye.IsFullParticipant, Is.True, "巡夜人应勾选完整参与者（可处决、掉落、露馅、被抓）");
        }

        [Test]
        public void PatrolB_KeepsDefaultKindAndParticipation()
        {
            TamingActor[] actors = RequireSingleView().PatrolActors;
            Assert.That(actors, Is.Not.Null.And.Length.GreaterThan(PatrolBIndex));

            TamingActor patrolB = actors[PatrolBIndex];
            Assert.That(patrolB != null, Is.True, $"patrolActors[{PatrolBIndex}] 是空引用");
            Assert.That(patrolB.StableId, Is.EqualTo(PatrolBStableId), $"patrolActors[{PatrolBIndex}] 应仍是巡逻者乙");
            Assert.That(patrolB.KindId, Is.EqualTo(0), "巡逻者乙的种类覆盖必须保持默认 0（沿用主怪种类，旧行为）");
            Assert.That(patrolB.IsFullParticipant, Is.False, "巡逻者乙必须保持「非完整参与者」（旧行为：只巡逻 / 感知 / 攻击）");
        }

        [Test]
        public void SampleBoss_HasBossAggro_WithIdAndInactiveIndicator()
        {
            GameObject boss = FindByName(BossObjectName);
            Assert.That(boss != null, Is.True, ScenePath + " 里没有 " + BossObjectName);

            var aggro = boss.GetComponent<BossAggro>();
            Assert.That(aggro != null, Is.True, BossObjectName + " 上没挂 BossAggro");
            Assert.That(aggro.BossId, Is.EqualTo(SampleBossId));

            // 提示物体是私有序列化字段，按序列化名读落盘值（同 SampleSceneObstacleWiringTests 的做法）。
            GameObject indicator;
            using (var so = new SerializedObject(aggro))
                indicator = so.FindProperty("alertIndicator").objectReferenceValue as GameObject;
            Assert.That(indicator != null, Is.True, "BossAggro 的 alertIndicator 没拖（头顶「要冲过来了」提示）");
            Assert.That(indicator.activeSelf, Is.False, "提示物体应默认关着，只在警觉 / 冲刺期间打开");
        }

        // ───────────────────────── 资产 ─────────────────────────

        [Test]
        public void FormalShopBoss_HasLiveNarrativeTarget_ForChallengeRouting()
        {
            var world = FindAll<Game.ShopPlaytest.ShopPlaytestWorld>()[0];
            var boss = System.Array.Find(world.Actors, actor => actor.StableId == "shop_boss");
            var trigger = boss.GetComponent<Game.Narrative.NarrativeTrigger>();
            Assert.That(trigger, Is.Not.Null, "移除目标组件会使划拳达标后的挑战被拒绝");
            Assert.That(trigger.enabled && trigger.gameObject.activeInHierarchy, Is.True);
            Assert.That(trigger.TargetId, Is.EqualTo("shop_boss"));
            Assert.That(trigger.TargetKind, Is.EqualTo("SpotlightShopBoss"));
        }

        [Test]
        public void FormalAfterBattlePoints_KeepClearOfShopInteractionCenters()
        {
            Physics.SyncTransforms();
            var world = FindAll<Game.ShopPlaytest.ShopPlaytestWorld>()[0];
            var bindings = FindAll<Game.Narrative.SpotlightSceneBindings>().Find(item => !item.IsHumanStory);
            foreach (string stage in new[] { "enter_r0198", "enter_r0215" })
            {
                var point = bindings.Find(stage);
                Assert.That(point.Marker, Is.Not.Null, "当前剧情地点应有光点");
                Assert.That(System.Array.Exists(Physics.OverlapCapsule(point.Position + Vector3.up * .35f,
                    point.Position + Vector3.up * 1.5f, .4f, LayerMask.GetMask("Obstacle")),
                    collider => collider.gameObject.scene == scene), Is.False, stage + "不能摆进桌椅或箱子");
                foreach (var actor in world.Actors)
                    Assert.That(Vector3.Distance(point.Position, actor.Position), Is.GreaterThan(1.6f),
                        stage + "不能再挤到店铺人物交互中心：" + actor.StableId);
            }
        }

        [Test]
        public void FormalStory_LegacyIsolationIncludesOldPerformanceAndBosses()
        {
            var bindings = FindAll<Game.Narrative.SpotlightSceneBindings>().Find(item => !item.IsHumanStory);
            Assert.That(bindings != null, Is.True);
            var names = new List<string>();
            using (var serialized = new SerializedObject(bindings))
            {
                var examples = serialized.FindProperty("legacyExamples");
                for (int i = 0; i < examples.arraySize; i++)
                {
                    var example = examples.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (example != null) names.Add(example.name);
                }
            }
            Assert.That(names, Does.Contain("Trigger_VillageEntrance"));
            Assert.That(names, Does.Contain("Npc_SampleBoss"));
            Assert.That(names, Does.Contain("Npc_Stage9Boss"));
            Assert.That(names, Does.Contain("Npc_BattleDrill"));
        }

        [Test]
        public void IdentityConfig_ExposureChases_AndAllSixCausesLoseIdentity()
        {
            IdentitySettings settings = LoadAsset<IdentityConfig>(IdentityConfigPath).Settings;
            const ExposureCause allSix = ExposureCause.PersonaTrait | ExposureCause.Checkpoint | ExposureCause.Ledger
                                         | ExposureCause.Suspicion | ExposureCause.Reveal | ExposureCause.Alertness;

            Assert.That(settings.ExposureOutcome, Is.EqualTo(ExposureOutcome.Chase), "露馅后果应是「追逐」");
            Assert.That((int)settings.IdentityLossCauses, Is.EqualTo(63), "六种露馅原因都应让身份失效（位掩码 63）");
            Assert.That(settings.IdentityLossCauses, Is.EqualTo(allSix));
        }

        [Test]
        public void StealthConfig_CaughtKnocksDownFirst_AndNeverFailsByCount()
        {
            ChaseOutcomeSettings outcome = LoadAsset<StealthConfig>(StealthConfigPath).ChaseOutcome;

            Assert.That(outcome.Policy, Is.EqualTo(ChaseCaughtPolicy.KnockdownThenDeath), "被抓口径应是「先击倒，没血才判死」");
            Assert.That(outcome.FailAfterCaughtCount, Is.EqualTo(0), "被抓次数判死应关掉（0 = 只看血量）");
        }

        [Test]
        public void MonsterConfig_AllThreeEscalationSwitchesOn()
        {
            MonsterConfig config = LoadAsset<MonsterConfig>(MonsterConfigPath);

            Assert.That(config.EscalateOnSneakStrike, Is.True, "偷袭（打中未察觉的怪）升级开关");
            Assert.That(config.EscalateOnFrontalStrike, Is.True, "正面攻击（打中已警戒 / 敌对的怪）升级开关");
            Assert.That(config.EscalateWhenStruck, Is.True, "被打（怪打中玩家）升级开关");
        }

        [Test]
        public void BossRoster_PatrolKindsDoNotEscalate_AndExplicitBossEntriesRemain()
        {
            BossRoster roster = LoadAsset<BossRosterConfig>(BossRosterConfigPath).CreateRoster();

            Assert.That(roster.TryGetByKind(ShilingKindId, out _), Is.False, "普通巡逻市令不应映射自动回合制入口");
            Assert.That(roster.TryGetByKind(XunyeKindId, out _), Is.False, "普通巡逻巡夜人不应映射自动回合制入口");
            Assert.That(roster.TryGet("monster_1003", out BossDefinition shiling), Is.True, "保留市令的显式ID入口");
            Assert.That(shiling.MonsterKindId, Is.EqualTo(0), "仅解除按种类自动映射，不删除显式战斗定义");
            Assert.That(roster.TryGet("stage9_boss", out _), Is.True, "保留阶段九BOSS入口");
            Assert.That(roster.TryGet("spotlight_mentor", out _), Is.True, "保留序章切磋入口");

            Assert.That(roster.TryGet(SampleBossId, out BossDefinition sampleBoss), Is.True, "名册里应有 sample_boss");
            Assert.That(sampleBoss.AggroEnabled, Is.True, "sample_boss 应开主动发难");
            Assert.That(sampleBoss.AggroRadius, Is.EqualTo(5f), "发难半径占位 5 米");
            Assert.That(sampleBoss.AggroReactionSeconds, Is.EqualTo(3f), "反应时间占位 3 秒");
        }

        // ───────────────────────── 工具 ─────────────────────────

        private EncounterSceneView RequireSingleView()
        {
            List<EncounterSceneView> views = FindAll<EncounterSceneView>();
            Assert.That(views.Count, Is.EqualTo(1), ScenePath + " 里应恰好有一个 EncounterSceneView");
            return views[0];
        }

        private GameObject FindByName(string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == name) return child.gameObject;
                }
            }

            return null;
        }

        private List<T> FindAll<T>() where T : Component
        {
            var result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        private static T LoadAsset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset != null, Is.True, $"读不到 {path}（{typeof(T).Name}）");
            return asset;
        }
    }
}
