// 职责：钉住战斗界面的纯规则——招式格亮暗（怒气够 / 不够 / 不是玩家回合）、怒气槽满、道具格显隐与明暗、
//   能用道具的时点、剩余回合文案（不限 / 递减 / 不为负）、BOSS 状态字（正常不显示）、玩家状态格角标、悬停详情取配置真实数值。每条配负对照。
// 为什么新建：BattleHudRules 是 W2a 新增的纯规则；一个被测类一个测试类。会话用 BattleSetup 真开（醉酒 0 → 正常态，BOSS 回合不抽随机数）。
using System.Collections.Generic;
using Game.Battle;
using Game.TurnBased;
using NUnit.Framework;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleHudRulesTests
    {
        // ── 招式格 ──────────────────────────────────────────────────────────

        [Test]
        public void SkillLit_EnoughRageOnPlayerTurn_IsLit_NotEnough_IsDim()
        {
            Assert.That(BattleHudRules.SkillLit(rage: 3, rageCost: 3, canCastNow: true), Is.True, "怒气刚好够 → 高亮（07:40）");
            Assert.That(BattleHudRules.SkillLit(rage: 2, rageCost: 3, canCastNow: true), Is.False, "怒气不够 → 置暗");
            Assert.That(BattleHudRules.SkillLit(rage: 0, rageCost: 0, canCastNow: true), Is.True, "招式1 无消耗 → 开场即亮");
        }

        [Test]
        public void SkillLit_NotPlayerTurn_IsDimEvenWithRage()
        {
            BattleSession session = Open(BattleSettings.PlaceholderDefault);
            Assert.That(BattleHudRules.SkillLit(session, PlayerSkill.Skill1, BattleSettings.PlaceholderDefault.PlayerSkills), Is.True, "前置：玩家回合、招式1 亮");

            Assert.That(session.TryCastSkill(PlayerSkill.Skill1).Accepted, Is.True);

            Assert.That(session.Phase, Is.EqualTo(BattlePhase.BossTurn));
            Assert.That(BattleHudRules.SkillLit(session, PlayerSkill.Skill1, BattleSettings.PlaceholderDefault.PlayerSkills), Is.False, "敌方回合一律置暗");
            Assert.That(BattleHudRules.SkillLit(rage: 3, rageCost: 0, canCastNow: false), Is.False);
        }

        [Test]
        public void SkillLit_FollowsSessionRage()
        {
            BattleSettings settings = BattleSettings.PlaceholderDefault; // 招式2 消耗 1，招式1 +1
            BattleSession session = Open(settings);
            Assert.That(BattleHudRules.SkillLit(session, PlayerSkill.Skill2, settings.PlayerSkills), Is.False, "开场 0 怒气，招式2 置暗");

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(session.Phase, Is.EqualTo(BattlePhase.PlayerTurn), "前置：BOSS 回合打完回到玩家");
            Assert.That(session.Player.Rage, Is.EqualTo(1));
            Assert.That(BattleHudRules.SkillLit(session, PlayerSkill.Skill2, settings.PlayerSkills), Is.True, "1 怒气够招式2");
            Assert.That(BattleHudRules.SkillLit(session, PlayerSkill.Skill3, settings.PlayerSkills), Is.False, "不够招式3（3 怒气）");
        }

        [Test]
        public void RageFull_OnlyAtMax()
        {
            Assert.That(BattleHudRules.RageFull(3, 3), Is.True, "满 → 槽更鲜艳（07:41）");
            Assert.That(BattleHudRules.RageFull(2, 3), Is.False);
            Assert.That(BattleHudRules.RageFull(0, 0), Is.False, "上限非正视为永不满");
        }

        // ── 道具格 ──────────────────────────────────────────────────────────

        [Test]
        public void ItemLook_OwnedAndUsable_IsLit()
        {
            var slot = new BattleItemSlot("1004", "治疗药水", 2, false);
            Assert.That(BattleHudRules.ItemLook(slot, canUseItemsNow: true), Is.EqualTo(BattleItemLook.Lit));
            Assert.That(BattleHudRules.ItemLook(slot, canUseItemsNow: false), Is.EqualTo(BattleItemLook.Dim), "不是能用道具的时点 → 置暗");
        }

        [Test]
        public void ItemLook_UsedThisBattle_IsDimEvenIfStillOwned()
        {
            // W1 结论：置暗看 UsedThisBattle，不看拒绝原因；件数还有也置暗（每场限一次）。
            Assert.That(BattleHudRules.ItemLook(new BattleItemSlot("1004", "治疗药水", 1, true), true), Is.EqualTo(BattleItemLook.Dim));
            Assert.That(BattleHudRules.ItemLook(new BattleItemSlot("1004", "治疗药水", 0, true), true), Is.EqualTo(BattleItemLook.Dim), "用掉最后一件也留着置暗（07:42）");
        }

        [Test]
        public void ItemLook_NotOwned_IsHidden()
        {
            Assert.That(BattleHudRules.ItemLook(new BattleItemSlot("1004", "治疗药水", 0, false), true), Is.EqualTo(BattleItemLook.Hidden), "未拥有不显示（07:42）");
        }

        [Test]
        public void CanUseItemsNow_BeforeCastOnly()
        {
            BattleSession session = Open(BattleSettings.PlaceholderDefault);
            Assert.That(BattleHudRules.CanUseItemsNow(session, StunTurnPolicy.SkipTurn), Is.True, "玩家回合、出招前（07:58）");

            session.TryCastSkill(PlayerSkill.Skill1);

            Assert.That(BattleHudRules.CanUseItemsNow(session, StunTurnPolicy.SkipTurn), Is.False, "出过招 / 敌方回合不能用");
            Assert.That(BattleHudRules.CanUseItemsNow(null, StunTurnPolicy.SkipTurn), Is.False);
        }

        // ── 回合 / 状态 ─────────────────────────────────────────────────────

        [Test]
        public void RemainingRoundsText_ZeroLimit_IsUnlimited()
        {
            Assert.That(BattleHudRules.RemainingRoundsText(0, 4), Is.EqualTo("不限"));
            Assert.That(BattleHudRules.RemainingRoundsText(5, 2), Is.EqualTo("3"));
            Assert.That(BattleHudRules.RemainingRoundsText(5, 9), Is.EqualTo("0"), "不出负数");
        }

        [Test]
        public void BossStatusText_NormalHidden_TiersNamed()
        {
            Assert.That(BattleHudRules.BossStatusText(DrunkTier.Normal, 0), Is.Empty, "正常态不显示（07:70）");
            Assert.That(BattleHudRules.BossStatusText(DrunkTier.Tipsy, 0), Is.EqualTo("微醺"));
            Assert.That(BattleHudRules.BossStatusText(DrunkTier.Drunk, 50), Is.EqualTo("薄醉 · 减疗"));
            Assert.That(BattleHudRules.BossStatusText(DrunkTier.Normal, 50), Is.EqualTo("减疗"));
        }

        [Test]
        public void TurnText_ByPhaseAndRound()
        {
            BattleSession session = Open(BattleSettings.PlaceholderDefault);
            Assert.That(BattleHudRules.TurnText(session), Is.EqualTo("第 1 回合 · 你的回合"));
            Assert.That(BattleHudRules.TurnText(2, false), Is.EqualTo("第 2 回合 · 敌方回合"));
        }

        [Test]
        public void PlayedRound_BossBatchUsesIncrementedCount_PlayerBatchUsesNext()
        {
            BattleSession session = Open(BattleSettings.PlaceholderDefault);
            Assert.That(BattleHudRules.PlayedRound(session, bossBatch: false), Is.EqualTo(1), "第 1 回合玩家出招");

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();

            Assert.That(session.CompletedRounds, Is.EqualTo(1), "前置：BOSS 回合打完，回合数已 +1");
            Assert.That(BattleHudRules.PlayedRound(session, bossBatch: true), Is.EqualTo(1), "刚演完的 BOSS 回合仍属第 1 回合");
            Assert.That(BattleHudRules.PlayedRound(session, bossBatch: false), Is.EqualTo(2), "负对照：接下来玩家出招是第 2 回合");
        }

        [Test]
        public void CollectPlayerStatuses_ExtraDamageAfterSkill3_BadgeIsCharges()
        {
            BattleSettings settings = WithPlayer(new PlayerSkillSettings(3, 0, 3, 1, 1, 2, 50, 0, 3, 3, 2, 3, true)); // 招式1 +3 怒气，一招攒满
            BattleSession session = Open(settings);
            var statuses = new List<BattleStatusEntry>();
            BattleHudRules.CollectPlayerStatuses(session.Player, settings, statuses);
            Assert.That(statuses, Is.Empty, "负对照：开场没有状态");

            session.TryCastSkill(PlayerSkill.Skill1);
            session.RunBossTurn();
            session.TryCastSkill(PlayerSkill.Skill3);
            BattleHudRules.CollectPlayerStatuses(session.Player, settings, statuses);

            Assert.That(statuses.Count, Is.EqualTo(1));
            Assert.That(statuses[0].Kind, Is.EqualTo(BattleStatusKind.ExtraDamage));
            Assert.That(statuses[0].Badge, Is.EqualTo(3), "角标 = 还剩几次（07:54「接下来的3次」）");
            Assert.That(statuses[0].Tooltip, Does.Contain("+2"), "详情取配置的额外伤害 2");
        }

        // ── 悬停详情取真实配置 ──────────────────────────────────────────────

        [Test]
        public void SkillTooltip_UsesConfiguredNumbers()
        {
            var skills = new PlayerSkillSettings(5, 0, 2, 7, 2, 9, 40, 3, 4, 11, 6, 2, true);

            string skill1 = BattleHudRules.SkillTooltip(PlayerSkill.Skill1, skills, 0);
            string skill2 = BattleHudRules.SkillTooltip(PlayerSkill.Skill2, skills, 0);
            string skill3 = BattleHudRules.SkillTooltip(PlayerSkill.Skill3, skills, 0);

            Assert.That(skill1, Does.Contain("伤害：7").And.Contain("怒气 +2"));
            Assert.That(skill2, Does.Contain("怒气消耗：2").And.Contain("伤害：9").And.Contain("-40%").And.Contain("持续 3 个敌方回合"));
            Assert.That(skill3, Does.Contain("怒气消耗：4").And.Contain("伤害：11").And.Contain("2 次攻击各 +6"));
            Assert.That(skill1, Does.Not.Contain("额外伤害"), "负对照：没有额外伤害次数时不写");
            Assert.That(BattleHudRules.SkillTooltip(PlayerSkill.Skill1, skills, 2), Does.Contain("本次附带额外伤害 +6（还剩 2 次）"));
        }

        [Test]
        public void ItemTooltip_HealPlaceholderFromSettings()
        {
            var items = new BattleItemSettings("1004", 30, HealthPercentBase.MaxHealth);

            string potion = BattleHudRules.ItemTooltip(new BattleItemSlot("1004", "治疗药水", 2, false), items, true);
            string other = BattleHudRules.ItemTooltip(new BattleItemSlot("1001", "铜钱", 1, true), items, false);

            Assert.That(potion, Does.Contain("治疗药水").And.Contain("×2").And.Contain("30%").And.Contain("每场战斗限用一次"));
            Assert.That(potion, Does.Not.Contain("本场已使用"));
            Assert.That(other, Does.Contain("暂无效果").And.Contain("本场已使用").And.Not.Contain("限用一次"));
        }

        [Test]
        public void BossTooltip_TipsyShowsSkipChanceFromConfig()
        {
            BattleSession session = Open(BattleSettings.PlaceholderDefault, drunk: 50);

            string text = BattleHudRules.BossTooltip(session.Boss, BattleSettings.PlaceholderDefault);

            Assert.That(text, Does.Contain("微醺").And.Contain("醉酒 50 / 100").And.Contain("20% 概率无法行动"));
        }

        [Test]
        public void NeedsEndTurn_OnlyWhenStuckOnPlayerTurn()
        {
            BattleSession session = Open(BattleSettings.PlaceholderDefault);
            Assert.That(BattleHudRules.NeedsEndTurn(session), Is.False, "能出招时不出现「结束回合」");
            session.TryCastSkill(PlayerSkill.Skill1);
            Assert.That(BattleHudRules.NeedsEndTurn(session), Is.False, "敌方回合不出现");
        }

        // ── 帮手 ────────────────────────────────────────────────────────────

        private static BattleSession Open(in BattleSettings settings, int drunk = 0)
        {
            var setup = new BattleSetup(settings, () => 7UL, new BattleItemInventory(new BattleFakes.Backpack()));
            Assert.That(setup.TryCreate(new BossDefinition("boss", "测试 BOSS", 100, drunk), out BattleSession session, out string error), Is.True, error);
            return session;
        }

        private static BattleSettings WithPlayer(in PlayerSkillSettings player)
        {
            BattleSettings basis = BattleSettings.PlaceholderDefault;
            return new BattleSettings(basis.Entry, player, basis.BossSkills, basis.Drunk, basis.Flow,
                basis.ItemOncePerBattle, basis.InheritsDrunkValue, basis.Items);
        }
    }
}
