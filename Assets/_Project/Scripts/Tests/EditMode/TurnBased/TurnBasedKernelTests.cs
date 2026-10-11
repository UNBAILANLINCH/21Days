// 职责：内核装配层（TurnBasedKernel）的测试——进入战斗的拒绝路径、脏输入、以及默认配置资产的自检。
//
// 出处：`docs/design/spotlight/07_回合制作战文档.md:13-28`（拒绝路径）；
// `docs/design/features-spotlight/09_BOSS战.md:239`（数值待拍板，但配置必须自洽）。

using Game.Core.Simulation;
using Game.TurnBased;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.TurnBased
{
    public sealed class TurnBasedKernelTests
    {
        private static BattleEntryRequest FrontOutsideZones() =>
            new BattleEntryRequest(false, false, false, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        private static BattleEntryRequest FrontalInsideZone() =>
            new BattleEntryRequest(false, false, true, false, MonsterAlertState.Unaware, BattleInitiator.PlayerAttacked);

        private static TurnBasedKernel NewKernel(IBattleItemInventory inventory = null) =>
            new TurnBasedKernel(BattleSettings.PlaceholderDefault, new XorShiftRandomStream(20261007UL), inventory);

        [Test]
        public void Validate_OnPlaceholderSettings_IsClean()
        {
            Assert.That(NewKernel().Validate(), Is.Null);
        }

        [Test]
        public void TryStartBattle_WithoutAnyTrigger_IsRefusedWithReason()
        {
            // 负对照：三种触发条件一条都不成立时不开战，并且点名原因。
            bool started = NewKernel().TryStartBattle(
                FrontOutsideZones(),
                new PlayerBattleSnapshot(10, 10),
                new BossBattleSnapshot(100, 100, 0),
                out BattleSession session,
                out BattleEntryReject reject);

            Assert.That(started, Is.False);
            Assert.That(session, Is.Null);
            Assert.That(reject, Is.EqualTo(BattleEntryReject.NoTrigger));
        }

        [Test]
        public void TryStartBattle_WithFrontalAttack_StartsTheBattle()
        {
            bool started = NewKernel().TryStartBattle(
                FrontalInsideZone(),
                new PlayerBattleSnapshot(10, 10),
                new BossBattleSnapshot(100, 100, 0),
                out BattleSession session,
                out BattleEntryReject reject);

            Assert.That(started, Is.True);
            Assert.That(reject, Is.EqualTo(BattleEntryReject.None));
            Assert.That(session, Is.Not.Null);
            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }

        [Test]
        public void TryStartBattle_AgainstAnAlreadyDeadBoss_IsRefused()
        {
            // 负对照：生命已经归零的 BOSS 不该开出一场「一进去就赢」的仗。
            bool started = NewKernel().TryStartBattle(
                FrontalInsideZone(),
                new PlayerBattleSnapshot(10, 10),
                new BossBattleSnapshot(100, 0, 0),
                out BattleSession session,
                out BattleEntryReject reject);

            Assert.That(started, Is.False);
            Assert.That(session, Is.Null);
            Assert.That(reject, Is.EqualTo(BattleEntryReject.InvalidSnapshot));
        }

        [Test]
        public void Kernel_WithoutRandomStream_Throws()
        {
            // 负对照：概率必须走注入的确定性随机源，没给就不许跑。
            Assert.Throws<System.ArgumentNullException>(
                () => new TurnBasedKernel(BattleSettings.PlaceholderDefault, null));
        }

        [Test]
        public void Kernel_WithoutConfig_Throws()
        {
            // 负对照：配置为 null 时不许静默用默认值（策划改的资产会不生效）。
            Assert.Throws<System.ArgumentNullException>(
                () => new TurnBasedKernel((TurnBasedConfig)null, new XorShiftRandomStream(1UL)));
        }

        [Test]
        public void Kernel_WithoutInventory_RefusesEveryItem()
        {
            // 负对照：没接背包时宁可「什么都没有」。
            TurnBasedKernel kernel = NewKernel();
            kernel.TryStartBattle(
                FrontalInsideZone(),
                new PlayerBattleSnapshot(10, 10),
                new BossBattleSnapshot(100, 100, 0),
                out BattleSession session,
                out _);

            Assert.That(session.TryUseItem("item.yao").Reject, Is.EqualTo(ItemUseReject.NotOwned));
        }

        [Test]
        public void Kernel_WithInventory_UsesTheInjectedOwnership()
        {
            var inventory = new FakeBattleItemInventory("item.yao");
            TurnBasedKernel kernel = NewKernel(inventory);
            kernel.TryStartBattle(
                FrontalInsideZone(),
                new PlayerBattleSnapshot(10, 10),
                new BossBattleSnapshot(100, 100, 0),
                out BattleSession session,
                out _);

            Assert.That(session.TryUseItem("item.yao").Allowed, Is.True);
            Assert.That(inventory.QueryCount, Is.EqualTo(1));
        }

        [Test]
        public void DefaultConfigAsset_PassesItsOwnValidation()
        {
            // 默认占位资产必须自洽：字段默认值就是占位口径，不许自相矛盾。
            TurnBasedConfig config = ScriptableObject.CreateInstance<TurnBasedConfig>();
            try
            {
                Assert.That(config.Validate(), Is.Null);
                Assert.That(config.Drunk.TipsyThreshold, Is.EqualTo(50));
                Assert.That(config.Drunk.DrunkThreshold, Is.EqualTo(80));
                Assert.That(config.Drunk.DeadDrunkThreshold, Is.EqualTo(100));
                Assert.That(config.Drunk.TipsySkipPercent, Is.EqualTo(20));
                Assert.That(config.Drunk.DrunkSkipPercent, Is.EqualTo(40));
                Assert.That(config.Drunk.DeadDrunkSkipPercent, Is.EqualTo(100));
                Assert.That(config.Drunk.DeadDrunkDropValue, Is.EqualTo(50));
                Assert.That(config.Drunk.DeadDrunkDurationRounds, Is.EqualTo(2));
                Assert.That(config.BossSkills.Skill1Weight, Is.EqualTo(6));
                Assert.That(config.BossSkills.Skill2Weight, Is.EqualTo(3));
                Assert.That(config.BossSkills.Skill3Weight, Is.EqualTo(1));
                Assert.That(config.Entry.SneakBossHealthLossPercent, Is.EqualTo(20));
                Assert.That(config.PlayerSkills.Skill1RageCost, Is.EqualTo(0));
                Assert.That(config.PlayerSkills.Skill1RageGain, Is.EqualTo(1));
                Assert.That(config.PlayerSkills.Skill2RageCost, Is.EqualTo(1));
                Assert.That(config.PlayerSkills.Skill3RageCost, Is.EqualTo(3));
                Assert.That(config.PlayerSkills.Skill2HealReductionPercent, Is.EqualTo(50));
                Assert.That(config.PlayerSkills.Skill3ExtraDamageAttacks, Is.EqualTo(3));
                Assert.That(config.InheritsDrunkValue, Is.True);
                Assert.That(config.ItemOncePerBattle, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void DefaultConfigAsset_MatchesPlaceholderSettings()
        {
            // 资产里的字段默认值必须与 `BattleSettings.PlaceholderDefault` 逐项一致：
            // 两处各写一遍是有意为之（配置要能在 Inspector 里手改，规则要能脱离 Unity 测），
            // 这条测试就是把「两边不许漂移」钉住——曾经 `deadDrunkLowersOncePerEntry`
            // 的字段默认（false）与占位设置（true）不一致，被这条测试抓出来。
            TurnBasedConfig config = ScriptableObject.CreateInstance<TurnBasedConfig>();
            try
            {
                BattleSettings asset = config.Settings;
                BattleSettings placeholder = BattleSettings.PlaceholderDefault;

                Assert.That(asset.Entry.SneakBossHealthLossPercent, Is.EqualTo(placeholder.Entry.SneakBossHealthLossPercent));
                Assert.That(asset.Entry.SneakLossBase, Is.EqualTo(placeholder.Entry.SneakLossBase));

                Assert.That(asset.PlayerSkills.RageMax, Is.EqualTo(placeholder.PlayerSkills.RageMax));
                Assert.That(asset.PlayerSkills.Skill1RageCost, Is.EqualTo(placeholder.PlayerSkills.Skill1RageCost));
                Assert.That(asset.PlayerSkills.Skill1RageGain, Is.EqualTo(placeholder.PlayerSkills.Skill1RageGain));
                Assert.That(asset.PlayerSkills.Skill1Damage, Is.EqualTo(placeholder.PlayerSkills.Skill1Damage));
                Assert.That(asset.PlayerSkills.Skill2RageCost, Is.EqualTo(placeholder.PlayerSkills.Skill2RageCost));
                Assert.That(asset.PlayerSkills.Skill2Damage, Is.EqualTo(placeholder.PlayerSkills.Skill2Damage));
                Assert.That(asset.PlayerSkills.Skill2HealReductionPercent, Is.EqualTo(placeholder.PlayerSkills.Skill2HealReductionPercent));
                Assert.That(asset.PlayerSkills.Skill2HealReductionRounds, Is.EqualTo(placeholder.PlayerSkills.Skill2HealReductionRounds));
                Assert.That(asset.PlayerSkills.Skill3RageCost, Is.EqualTo(placeholder.PlayerSkills.Skill3RageCost));
                Assert.That(asset.PlayerSkills.Skill3Damage, Is.EqualTo(placeholder.PlayerSkills.Skill3Damage));
                Assert.That(asset.PlayerSkills.Skill3ExtraDamage, Is.EqualTo(placeholder.PlayerSkills.Skill3ExtraDamage));
                Assert.That(asset.PlayerSkills.Skill3ExtraDamageAttacks, Is.EqualTo(placeholder.PlayerSkills.Skill3ExtraDamageAttacks));
                Assert.That(asset.PlayerSkills.Skill3ExtraDamageStacks, Is.EqualTo(placeholder.PlayerSkills.Skill3ExtraDamageStacks));

                Assert.That(asset.BossSkills.Skill1Damage, Is.EqualTo(placeholder.BossSkills.Skill1Damage));
                Assert.That(asset.BossSkills.Skill2DrinkAddDrunk, Is.EqualTo(placeholder.BossSkills.Skill2DrinkAddDrunk));
                Assert.That(asset.BossSkills.Skill2HealPercent, Is.EqualTo(placeholder.BossSkills.Skill2HealPercent));
                Assert.That(asset.BossSkills.Skill2HealBase, Is.EqualTo(placeholder.BossSkills.Skill2HealBase));
                Assert.That(asset.BossSkills.Skill2NextSkill1DamageBonusPercent, Is.EqualTo(placeholder.BossSkills.Skill2NextSkill1DamageBonusPercent));
                Assert.That(asset.BossSkills.Skill3Damage, Is.EqualTo(placeholder.BossSkills.Skill3Damage));
                Assert.That(asset.BossSkills.Skill3StunRounds, Is.EqualTo(placeholder.BossSkills.Skill3StunRounds));
                Assert.That(asset.BossSkills.TotalWeight, Is.EqualTo(placeholder.BossSkills.TotalWeight));

                Assert.That(asset.Drunk.TipsyThreshold, Is.EqualTo(placeholder.Drunk.TipsyThreshold));
                Assert.That(asset.Drunk.DrunkThreshold, Is.EqualTo(placeholder.Drunk.DrunkThreshold));
                Assert.That(asset.Drunk.DeadDrunkThreshold, Is.EqualTo(placeholder.Drunk.DeadDrunkThreshold));
                Assert.That(asset.Drunk.MaxDrunkValue, Is.EqualTo(placeholder.Drunk.MaxDrunkValue));
                Assert.That(asset.Drunk.TipsySkipPercent, Is.EqualTo(placeholder.Drunk.TipsySkipPercent));
                Assert.That(asset.Drunk.DrunkSkipPercent, Is.EqualTo(placeholder.Drunk.DrunkSkipPercent));
                Assert.That(asset.Drunk.DeadDrunkSkipPercent, Is.EqualTo(placeholder.Drunk.DeadDrunkSkipPercent));
                Assert.That(asset.Drunk.DeadDrunkDropValue, Is.EqualTo(placeholder.Drunk.DeadDrunkDropValue));
                Assert.That(asset.Drunk.DeadDrunkDurationRounds, Is.EqualTo(placeholder.Drunk.DeadDrunkDurationRounds));
                Assert.That(asset.Drunk.DeadDrunkLowersOncePerEntry, Is.EqualTo(placeholder.Drunk.DeadDrunkLowersOncePerEntry));
                Assert.That(asset.Drunk.DeadDrunkLowersOncePerEntry, Is.True, "07:73 的占位口径是「进入酩酊时降一次」");

                Assert.That(asset.Flow.RoundLimit, Is.EqualTo(placeholder.Flow.RoundLimit));
                Assert.That(asset.Flow.RoundLimitOutcome, Is.EqualTo(placeholder.Flow.RoundLimitOutcome));
                Assert.That(asset.Flow.StunTurnPolicy, Is.EqualTo(placeholder.Flow.StunTurnPolicy));

                Assert.That(asset.ItemOncePerBattle, Is.EqualTo(placeholder.ItemOncePerBattle));
                Assert.That(asset.InheritsDrunkValue, Is.EqualTo(placeholder.InheritsDrunkValue));

                // 道具效果占位（PRP/turnbased-battle D10，等 C91）：1004 治疗药水回复 30% 生命上限。
                Assert.That(asset.Items.HealItemId, Is.EqualTo(placeholder.Items.HealItemId));
                Assert.That(asset.Items.HealPercent, Is.EqualTo(placeholder.Items.HealPercent));
                Assert.That(asset.Items.HealBase, Is.EqualTo(placeholder.Items.HealBase));
                Assert.That(asset.Items.HealItemId, Is.EqualTo("1004"));
                Assert.That(asset.Items.HealPercent, Is.EqualTo(30));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ConfigBackedKernel_StartsABattleFromTheAsset()
        {
            TurnBasedConfig config = ScriptableObject.CreateInstance<TurnBasedConfig>();
            try
            {
                var kernel = new TurnBasedKernel(config, new XorShiftRandomStream(1UL));
                Assert.That(kernel.Config, Is.SameAs(config));
                Assert.That(kernel.Validate(), Is.Null);

                bool started = kernel.TryStartBattle(
                    FrontalInsideZone(),
                    new PlayerBattleSnapshot(10, 10),
                    new BossBattleSnapshot(100, 100, 60),
                    out BattleSession session,
                    out _);

                Assert.That(started, Is.True);
                // 战斗外的醉酒值被继承进战斗（07:66）。
                Assert.That(session.Boss.DrunkValue, Is.EqualTo(60));
                Assert.That(session.Boss.Tier, Is.EqualTo(DrunkTier.Tipsy));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
