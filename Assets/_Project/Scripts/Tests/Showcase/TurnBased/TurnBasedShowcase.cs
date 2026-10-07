// 职责：回合制作战内核（TurnBased）回放——把「规则全绿、但一步都没在游戏里跑过」的内核搬进实例场景，
//   在 Game 视图里演给人看四件用户看得见的事：
//     ① Entry_ThreeWays_DifferInInitiativeAndHealth —— 三种进入方式的先手与血量差异（偷袭 / 正面攻击 / 被打）；
//     ② PlayerSkills_RageEconomy_AndRejections    —— 玩家三招式的怒气收支、减疗与额外伤害，以及怒气不足被拒；
//     ③ DrunkTiers_SkipTurns_ShowHintTexts        —— BOSS 醉酒四档的跳过回合与三句原文提示（概率确定性地演）；
//     ④ Victory_EndsBattle_ExitKeyIsVictory       —— 打完收工：战斗结束与交给剧情侧的出口键。
//   舞台与接法见下；每一步的「期望」都写成检查点，进度与截图见 `Logs/verify/turnbased/<时间戳>/report.md`。
//
// ── 舞台：ScenePath 返回 null + EnterWorldFromTitle（走 Boot 真实流程）────────────────────────────
//   理由三条：
//   ① `docs/module-dev-spec.md` §1 与 DoD 第 3 条的默认接法就是「走 Boot 真实流程进场」（标题「开始」→
//      Addressables 加载 SampleScene），Identity / Stealth 两条刚落盘的同类回放用的也是这一条，TurnBased 没有理由破例；
//   ② `Assets/Scenes/SampleScene.unity` 本波被另一条波次独占（本任务明令禁改）。走真实流程**一个场景资产都不动**，
//      不会与并行的场景实装撞车；白盒面板是运行时生成的物体，`Track()` 登记，收尾统一销毁；
//   ③ 内核本来就没有场景依赖（只吃快照 + 注入随机源），进场不是为了给它找东西读，而是为了「在实例场景的
//      Game 视图里真的看见它在跑」。所以本回放**不读 SampleScene 里的任何物体**，也不会因为别人改场景而读错东西。
//
// ── 内核怎么进这一场：在回放里自己装配（正式接线见报告「建议补丁」）──────────────────────────────
//   `new TurnBasedKernel(config, random)` 的 `config` 是**同一份配置资产**
//   `Assets/_Project/Data/TurnBased/TurnBasedConfig.asset`——正式接线时这一步由 `MonsterInstaller` 完成
//   （`turnbased-module-guide.md`「接线清单」：进战斗判定 / 血量快照 / 背包口 / 结果回写四件事都在接线侧），
//   本波禁改 `Boot.unity`，所以回放自己 new：装配来源完全同源，只有「谁 new 它」不同。
//
// ── 为什么直达模块公开接口，而不是虚拟手柄 / 真实输入 ──────────────────────────────────────────
//   本模块**没有任何输入路径**：没有界面、没有场景物体、没有绑定任何输入动作
//   （`turnbased-module-guide.md` 开头就写着「本模块目前没有调用方」）。所以这里驱动的是模块公开接口
//   （`TryStartBattle` → `TryCastSkill` / `SkipPlayerTurn` / `RunBossTurn`，读 `Phase` / `Events`），
//   属于 `.claude/rules/module-verify.md` 里「模块没有输入路径」那一类退路，不是绕开真实输入端偷懒。
//   道具那一档同理没演：`IBattleItemInventory` 的适配器要等 Inventory 模块稳定（guide「接线清单」），
//   本回放传 null = 「什么道具都没有」，不假造一个背包。
//
// ── 概率怎么做到确定性可演示 ────────────────────────────────────────────────────────────────
//   概率只走注入的 `IRandomStream`（guide「随机数与回放」），回放注入 `ScriptedRandomStream`（剧本流），
//   把「这次掷出几点」写死：微醺掷 19（<20 → 跳过）、再掷 20（≥20 → 不跳过）；薄醉掷 39（<40 → 跳过）；
//   酩酊 100% 跳过且**一个随机数都不抽**（`DrunkTierRules.ShouldSkipTurn` 对 0% / 100% 不走 random）。
//   **没有种子**——剧本流不是伪随机序列，是逐次指定的点数。正式接线时这条流应当是 `IRandomService` 的
//   `logic.*` 流（guide 明确要求），种子由存档 / 回放系统给，那时回放对不上就要靠种子 + 状态快照复现。
//
// ── 演示用的注入值（都不属于模块，原文没写、等 C91）───────────────────────────────────────────
//   玩家血量 30（`09_BOSS战.md:239`「血量、伤害数值……原文都没写」，模块不设占位血量，一律外部注入）；
//   BOSS 血量 100（第①条：偷袭 -20% 一眼看得出 100 → 80）/ 40（第②条：让「饮酒被减疗砍半」不被回血封顶吃掉）；
//   BOSS 战斗外醉酒值 0 / 50 / 80 / 100（分别对应 07:70-73 的正常 / 微醺 / 薄醉 / 酩酊）。
//
// ── 已知边界（本波不就地改，如实演出来并写进报告）─────────────────────────────────────────────
//   1. 正面攻击 / 被打的第三条用例里，BOSS 招式 1 的伤害占位值是 1，「饮酒后下次招式 1 +30%」（07:80）
//      整数向下取整后仍是 1 —— 增伤在占位数值下**看不见**，等 C91 给伤害数值（面板上的「下次招式1 +30%：是」是判据）。
//   2. 「同一个玩家回合不能出第二招」在占位口径（`StunTurnPolicy.SkipTurn`）下表现为
//      `SkillCastReject.NotPlayerTurn`（出招后阶段立刻转 BOSS 回合，07:56），不是 `AlreadyCastThisTurn`。
//   3. 醉酒值降到 50（微醺档）时，若还在「酩酊持续 2 回合」期内，本回合仍按酩酊跳过、提示仍是 07:73 那句
//      （`BossDrunkRules.BeginMonsterTurn` 先判持续期）——第③条用例把这条边界钉成检查点。

using System.Collections;
using Game.TurnBased;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.TurnBased
{
    [Category("Showcase")]
    public sealed class TurnBasedShowcase : ShowcaseScenario
    {
        /// <summary>数值真源：与正式接线取的是同一份配置资产（40 项占位值，逐项标了 07 的出处）。</summary>
        private const string ConfigAssetPath = "Assets/_Project/Data/TurnBased/TurnBasedConfig.asset";

        /// <summary>第①条用例的 BOSS 血量：偷袭扣 20% 时 100 → 80，肉眼一眼可判。</summary>
        private const int EntryCaseBossHealth = 100;

        /// <summary>第②条用例的 BOSS 血量：让「饮酒回血被减疗砍半」落在封顶之前（40 的 10% 是 4，砍半 2，不触顶）。</summary>
        private const int SkillCaseBossHealth = 40;

        /// <summary>玩家血量（演示注入值，原文没写玩家血量，等 C91）。</summary>
        private const int DemoPlayerHealth = 30;

        /// <summary>第④条用例的 BOSS 血量：招式1（1 点）+ 招式2（2 点）正好打死，用来演「打完 → 出口键」。</summary>
        private const int FinishCaseBossHealth = 3;

        // 07:71 / :72 / :73 的原文，逐字照抄（用来钉「屏幕上出现的字 = 策划原文」，不用 BattleHintTexts 常量比对，
        // 否则常量改了这条检查点会跟着改，就钉不住原文了）。
        private const string TipsyHintLiteral = "怪物处于微醺状态，本回合无法行动。";
        private const string DrunkHintLiteral = "怪物处于薄醉状态，本回合无法行动。";
        private const string DeadDrunkHintLiteral = "怪物处于醉酒状态，本回合无法行动。";

        private TurnBasedConfig config;
        private BattleSettings settings;
        private TurnBasedKernel kernel;
        private ScriptedRandomStream random;
        private BattleWhiteboxView view;
        private BattleSession session;

        protected override string Module => "TurnBased";

        /// <summary>世界由 Boot 流程加载（标题「开始」→ SampleScene），基类不再叠加载一份（理由见文件头）。</summary>
        protected override string ScenePath => null;

        // ───────────────────────── ① 三种进入方式：先手与血量差异 ─────────────────────────

        [UnityTest]
        public IEnumerator Entry_ThreeWays_DifferInInitiativeAndHealth()
        {
            yield return EnterWorld();
            yield return Step("挂上白盒状态板（TurnBased 白盒 UI 组件）：先手方 / 怒气 / BOSS 生命与醉酒 / 剩余回合都从它看",
                () => CreatePanel("三种进入方式"));
            yield return CheckPanelRendered();

            // ── 第 3 步：偷袭（玩家先手 + BOSS 生命 -20%，07:15-18）──
            int expectedSneakHealth = EntryCaseBossHealth
                                      - EntryCaseBossHealth * settings.Entry.SneakBossHealthLossPercent / 100;
            bool started = false;
            BattleEntryReject reject = BattleEntryReject.None;
            yield return Step("玩家潜行并偷袭命中：从背后开这一仗（07:15-18）",
                () => started = StartBattle(SneakRequest(), EntryCaseBossHealth, 0, "偷袭开战", out reject));
            yield return Check("偷袭进战斗：先手方是玩家（07:18），BOSS 生命先扣 20%（"
                               + EntryCaseBossHealth + " → " + expectedSneakHealth + "），玩家立刻轮到出手",
                () => started
                      && session.Entry.Kind == BattleEntryKind.SneakAttack
                      && session.Entry.Initiative == BattleInitiative.Player
                      && session.Phase == BattlePhase.PlayerTurn
                      && session.Boss.Health == expectedSneakHealth);
            yield return Check("屏幕上看得见：面板写着「先手方：玩家」与「生命 "
                               + expectedSneakHealth + "/" + EntryCaseBossHealth + "」",
                () => view.ScreenText.Contains("先手方：玩家")
                      && view.ScreenText.Contains("生命 " + expectedSneakHealth + "/" + EntryCaseBossHealth), 1f);
            yield return Snapshot("偷袭开战：玩家先手、BOSS 掉 20%");

            // ── 第 4 步：负对照——潜行了但没打中，且不在警戒 / 敌对区域，进不了战斗（07:17、:22）──
            bool refusedStarted = true;
            BattleEntryReject refusedReject = BattleEntryReject.None;
            yield return Step("负对照：潜行着但这一下没偷袭成功，又不在警戒 / 敌对区域——按 07:17、:22 这不构成进入战斗",
                () => refusedStarted = StartBattle(SneakFailedRequest(), EntryCaseBossHealth, 0, "(不该开战)", out refusedReject));
            yield return Check("被拒绝：没有开出战斗，理由是「潜行中但偷袭未成功」（负对照成立）",
                () => !refusedStarted && refusedReject == BattleEntryReject.SneakStrikeNotLanded && session == null);

            // ── 第 5 步：正面攻击（同样玩家先手，但不扣血，07:20-23）──
            yield return Step("玩家在警戒区域里正面攻击（怪物处于警戒态）：同样是玩家攻击，但没有偷袭那一下扣血（07:20-23）",
                () => started = StartBattle(FrontalRequest(), EntryCaseBossHealth, 0, "正面攻击开战", out reject));
            yield return Check("正面攻击：先手方仍是玩家，但 BOSS 生命不扣（"
                               + EntryCaseBossHealth + "/" + EntryCaseBossHealth + "）——与偷袭的差别就在这 20%",
                () => started
                      && session.Entry.Kind == BattleEntryKind.FrontalAttack
                      && session.Entry.Initiative == BattleInitiative.Player
                      && session.Boss.Health == EntryCaseBossHealth);
            yield return Snapshot("正面攻击：玩家先手、BOSS 满血");

            // ── 第 6 步：被打（BOSS 先手，07:25-28）──
            yield return Step("玩家站在敌对区域里被 BOSS 攻击：这一条先手不在玩家手上（07:25-28）",
                () => started = StartBattle(AmbushRequest(), EntryCaseBossHealth, 0, "被 BOSS 攻击开战", out reject));
            yield return Check("被打进战斗：先手方是 BOSS——会话一开出来就直接停在 BOSS 回合，玩家还没轮到（怒气 0、已打回合 0）",
                () => started
                      && session.Entry.Kind == BattleEntryKind.Ambushed
                      && session.Entry.Initiative == BattleInitiative.Boss
                      && session.Phase == BattlePhase.BossTurn
                      && session.Player.Rage == 0
                      && session.CompletedRounds == 0);
            yield return Check("屏幕上看得见：面板的先手方换成「BOSS」、阶段是「BOSS 回合」",
                () => view.ScreenText.Contains("先手方：BOSS") && view.ScreenText.Contains("阶段：BOSS 回合"), 1f);
            yield return Snapshot("被 BOSS 攻击：BOSS 先手");
        }

        // ───────────────────────── ② 玩家三招式：怒气收支与拒绝 ─────────────────────────

        [UnityTest]
        public IEnumerator PlayerSkills_RageEconomy_AndRejections()
        {
            yield return EnterWorld();
            yield return Step("挂上白盒状态板，开一场正面攻击的仗（玩家先手，BOSS 醉酒 0 = 正常档，跳过概率 0%）",
                () => CreatePanel("玩家三招式"));
            yield return CheckPanelRendered();

            // 剧本＝逐次指定的选招点数（Range(0,10) 的取值域）：0 → 招式1（权重 6，落在 0..5）、7 → 招式2 饮酒（权重 3，落在 6..8）。
            // 正常档（0-49）跳过概率 0%，按内核约定不掷跳过骰，所以怪物回合**恰好**只消耗这里的一个数。
            random = new ScriptedRandomStream().Roll(10, 0).Roll(10, 7).Roll(10, 0).Roll(10, 0).Roll(10, 0).Roll(10, 0);
            kernel = new TurnBasedKernel(config, random);

            bool started = false;
            BattleEntryReject reject = BattleEntryReject.None;
            yield return Step("正面攻击开战（07:20-23）",
                () => started = StartBattle(FrontalRequest(), SkillCaseBossHealth, 0, "招式与怒气", out reject));
            yield return Check("开出来了：玩家先手、玩家回合、怒气从 0 起（怒气上限与招式伤害都是占位值，等 C91）",
                () => started
                      && session.Phase == BattlePhase.PlayerTurn
                      && session.Player.Rage == 0
                      && session.Boss.Health == SkillCaseBossHealth);

            // ── 第 4 步：负对照——怒气 0 时招式 3（耗 3）被拒 ──
            SkillCastResult shortRage = default;
            yield return Step("负对照：怒气 0 就想放招式 3（耗 3 怒气，07:54）",
                () => shortRage = session.TryCastSkill(PlayerSkill.Skill3));
            yield return Check("被拒绝：理由 NotEnoughRage，怒气仍是 0、BOSS 一点血没掉（负对照成立）",
                () => !shortRage.Accepted
                      && shortRage.Reject == SkillCastReject.NotEnoughRage
                      && session.Player.Rage == 0
                      && session.Boss.Health == SkillCaseBossHealth);
            yield return Check("屏幕上看得见：面板把招式 3 画成「✗ 怒气不足」（07:40 正式界面是置暗，白盒版写成原因）",
                () => view.ScreenText.Contains("招式3（耗 3）：✗ 怒气不足"), 1f);

            // ── 第 5 步：招式 1（无消耗 +1 怒气，07:50），顺带做「同回合第二招」的负对照 ──
            SkillCastResult skill1 = default;
            SkillCastResult sameTurn = default;
            yield return Step("放招式 1（无消耗、用后 +1 怒气，并造成伤害，07:50）；紧接着在同一回合再出一招做负对照",
                () =>
                {
                    skill1 = CastAndPresent(PlayerSkill.Skill1);
                    sameTurn = session.TryCastSkill(PlayerSkill.Skill1);
                });
            yield return Check("招式 1：怒气 0 → 1，BOSS 生命 -" + settings.PlayerSkills.Skill1Damage
                               + "，阶段交给敌方（07:56）",
                () => skill1.Accepted
                      && skill1.RageBefore == 0
                      && skill1.RageAfter == 1
                      && skill1.Damage == settings.PlayerSkills.Skill1Damage
                      && session.Boss.Health == SkillCaseBossHealth - settings.PlayerSkills.Skill1Damage
                      && session.Phase == BattlePhase.BossTurn);
            // 已知边界（写进报告）：占位口径下出招后阶段立刻转敌方，所以「同回合第二招」在这里是 NotPlayerTurn。
            yield return Check("同一回合再出一招被拒（占位口径下理由是 NotPlayerTurn，不是 AlreadyCastThisTurn；见文件头「已知边界」2）",
                () => !sameTurn.Accepted && sameTurn.Reject == SkillCastReject.NotPlayerTurn);

            // ── 第 6 步：怪物回合（正常档不掷跳过骰，只掷一次选招）──
            yield return Step("走一个怪物回合：正常档（0-49）跳过概率 0%，所以只掷一次骰子选招——剧本掷 0 → BOSS 招式 1（07:79、:84）",
                AdvanceBossTurn);
            yield return Check("怪物回合走完：BOSS 用招式 1 打掉玩家 " + settings.BossSkills.Skill1Damage
                               + " 点，阶段回到玩家回合、怒气保留 1，而且**只消耗了一个随机数**（0% 不掷跳过骰）",
                () => session.Phase == BattlePhase.PlayerTurn
                      && session.Player.Health == DemoPlayerHealth - settings.BossSkills.Skill1Damage
                      && session.Player.Rage == 1
                      && session.CompletedRounds == 1
                      && random.DrawCount == 1);

            // ── 第 7 步：招式 2（耗 1 怒气 + 减对方 50% 治疗，07:52）；紧接着让 BOSS 饮酒，看减疗真的把回血砍半（07:80）──
            SkillCastResult skill2 = default;
            int bossHealthBeforeDrink = 0;
            yield return Step("放招式 2：消耗 1 怒气、造成伤害，并给对方挂上「减少 50% 治疗效果」（07:52）；"
                              + "再走一个怪物回合（剧本掷 7 → BOSS 招式 2 饮酒）看它回血被砍半",
                () =>
                {
                    skill2 = CastAndPresent(PlayerSkill.Skill2);
                    bossHealthBeforeDrink = session.Boss.Health;
                    AdvanceBossTurn();
                });
            yield return Check("招式 2：怒气 1 → 0，BOSS 生命 -" + settings.PlayerSkills.Skill2Damage
                               + "，BOSS 身上的减疗 = " + settings.PlayerSkills.Skill2HealReductionPercent + "%",
                () => skill2.Accepted
                      && skill2.RageBefore == 1
                      && skill2.RageAfter == 0
                      && skill2.Damage == settings.PlayerSkills.Skill2Damage
                      && session.Boss.HealReductionPercent == settings.PlayerSkills.Skill2HealReductionPercent);
            int rawHeal = session.Boss.MaxHealth * settings.BossSkills.Skill2HealPercent / 100;
            int expectedHeal = rawHeal - rawHeal * settings.PlayerSkills.Skill2HealReductionPercent / 100;
            yield return Check("减疗生效：BOSS 只回了 " + expectedHeal + " 点血（未减疗本该回 " + rawHeal
                               + "），醉酒 0 → " + settings.BossSkills.Skill2DrinkAddDrunk
                               + "（仍是正常档），「下次招式1 +30%」挂上",
                () => DrankHealedAmount() == expectedHeal
                      && session.Boss.Health == bossHealthBeforeDrink + expectedHeal
                      && session.Boss.DrunkValue == settings.BossSkills.Skill2DrinkAddDrunk
                      && session.Boss.NextSkill1DamageBonusPending);
            yield return Check("屏幕上看得见：面板写着醉酒 " + settings.BossSkills.Skill2DrinkAddDrunk + "/"
                               + settings.Drunk.MaxDrunkValue + "、减疗 " + settings.PlayerSkills.Skill2HealReductionPercent + "%",
                () => view.ScreenText.Contains("醉酒 " + settings.BossSkills.Skill2DrinkAddDrunk + "/"
                                               + settings.Drunk.MaxDrunkValue)
                      && view.ScreenText.Contains("减疗 " + settings.PlayerSkills.Skill2HealReductionPercent + "%"), 1f);
            yield return Snapshot("招式2 的减疗把 BOSS 饮酒回血砍半");

            // ── 第 8 步：攒满 3 怒气 → 招式 3（耗 3，接下来 3 次攻击附带额外伤害，07:54）──
            SkillCastResult skill3 = default;
            yield return Step("攒怒气：招式 1 打三次（每次 +1、中间三个怪物回合），攒到上限 3 后放招式 3（耗 3 怒气）",
                () =>
                {
                    CastAndPresent(PlayerSkill.Skill1);
                    AdvanceBossTurn();
                    CastAndPresent(PlayerSkill.Skill1);
                    AdvanceBossTurn();
                    CastAndPresent(PlayerSkill.Skill1);
                    AdvanceBossTurn();
                    skill3 = CastAndPresent(PlayerSkill.Skill3);
                });
            yield return Check("招式 3：怒气 3 → 0，伤害 " + settings.PlayerSkills.Skill3Damage
                               + "，并留下 " + settings.PlayerSkills.Skill3ExtraDamageAttacks + " 次「额外伤害」",
                () => skill3.Accepted
                      && skill3.RageBefore == 3
                      && skill3.RageAfter == 0
                      && skill3.Damage == settings.PlayerSkills.Skill3Damage
                      && skill3.ExtraDamageChargesAfter == settings.PlayerSkills.Skill3ExtraDamageAttacks
                      && session.Player.ExtraDamageCharges == settings.PlayerSkills.Skill3ExtraDamageAttacks);
            yield return Snapshot("招式3：怒气清空、挂上 3 次额外伤害");

            // ── 第 9 步：下一次攻击吃掉一次额外伤害 ──
            SkillCastResult boosted = default;
            yield return Step("走一个怪物回合后再出一招：验证「接下来 3 次攻击附带额外伤害」真的加在伤害上（07:54）",
                () =>
                {
                    AdvanceBossTurn();
                    boosted = CastAndPresent(PlayerSkill.Skill1);
                });
            int expectedBoostedDamage = settings.PlayerSkills.Skill1Damage + settings.PlayerSkills.Skill3ExtraDamage;
            yield return Check("额外伤害生效：这一击打出 " + expectedBoostedDamage + " = 招式1 的 "
                               + settings.PlayerSkills.Skill1Damage + " + 额外 " + settings.PlayerSkills.Skill3ExtraDamage
                               + "，剩余次数 " + settings.PlayerSkills.Skill3ExtraDamageAttacks + " → "
                               + (settings.PlayerSkills.Skill3ExtraDamageAttacks - 1),
                () => boosted.Accepted
                      && boosted.ExtraDamageApplied
                      && boosted.Damage == expectedBoostedDamage
                      && session.Player.ExtraDamageCharges == settings.PlayerSkills.Skill3ExtraDamageAttacks - 1);
            yield return Snapshot("额外伤害生效");
        }

        // ───────────────────────── ③ BOSS 醉酒四档：跳过回合与提示文案 ─────────────────────────

        [UnityTest]
        public IEnumerator DrunkTiers_SkipTurns_ShowHintTexts()
        {
            yield return EnterWorld();
            yield return Step("挂上白盒状态板，开一场「战斗外已经喝到 50」的仗：BOSS 继承战斗外的醉酒值（07:66）",
                () => CreatePanel("醉酒四档"));
            yield return CheckPanelRendered();

            // 剧本：微醺第一个怪物回合掷 19（<20 → 跳过）、第二个回合掷 20（≥20 → 不跳过，再掷 0 选到招式 1）。
            random = new ScriptedRandomStream().Roll(100, 19).Roll(100, 20).Roll(10, 0);
            kernel = new TurnBasedKernel(config, random);
            bool started = false;
            BattleEntryReject reject = BattleEntryReject.None;
            yield return Step("带着 50 点醉酒值进战斗：档位应当是微醺（50-79，跳过概率 20%，07:71）",
                () => started = StartBattle(FrontalRequest(), EntryCaseBossHealth, 50, "微醺档", out reject));
            yield return Check("进战斗继承了战斗外的醉酒值 50 → 档位微醺、面板写「微醺」、跳过概率 20%",
                () => started
                      && session.Boss.DrunkValue == 50
                      && session.Boss.Tier == DrunkTier.Tipsy
                      && session.Boss.Drunk.SkipChancePercent == settings.Drunk.TipsySkipPercent
                      && view.ScreenText.Contains("= 微醺（跳过 " + settings.Drunk.TipsySkipPercent + "%）"), 1f);

            // ── 第 4 步：微醺掷 19 → 跳过回合 + 屏幕中央闪现原文（07:71）──
            yield return Step("怪物回合（剧本掷 19，落在 20% 里）→ 跳过回合，屏幕中央闪现 07:71 的原文",
                AdvanceBossTurn);
            yield return Check("跳过了：本回合 BOSS 一步没动（玩家没掉血、醉酒值不变、回合数照常 +1）",
                () => SkippedHint() != null
                      && session.Player.Health == DemoPlayerHealth
                      && session.Boss.DrunkValue == 50
                      && session.CompletedRounds == 1);
            yield return Check("屏幕中央闪现的提示 = 策划原文（07:71）逐字：「" + TipsyHintLiteral + "」",
                () => view.HintText == TipsyHintLiteral
                      && view.HintText == BattleHintTexts.TipsySkip
                      && view.ScreenText.Contains(TipsyHintLiteral), 1f);
            yield return Snapshot("微醺：跳过回合 + 中央提示");

            // ── 第 5 步：负对照——同一档位掷 20，不跳过 ──
            int healthBeforeBossTurn = session.Player.Health;
            yield return Step("负对照：同一档位再走一个怪物回合（剧本掷 20，落在 20% 之外）→ 不跳过，BOSS 照常出招",
                AdvanceBossTurn);
            yield return Check("没跳过：BOSS 用招式 1 打掉玩家 " + settings.BossSkills.Skill1Damage
                               + " 点血，且这一回合没有跳过事件（提示不出现）",
                () => SkippedHint() == null
                      && session.Player.Health == healthBeforeBossTurn - settings.BossSkills.Skill1Damage
                      && view.HintText == null);
            yield return Snapshot("微醺：这次没跳过（20% 的另一侧）");

            // ── 第 6 步：薄醉（80-99，40%）──
            random = new ScriptedRandomStream().Roll(100, 39).Roll(10, 0);
            kernel = new TurnBasedKernel(config, random);
            yield return Step("换一场：战斗外喝到 80（薄醉，跳过概率 40%，07:72）；怪物回合剧本掷 39 → 跳过",
                () =>
                {
                    StartBattle(FrontalRequest(), EntryCaseBossHealth, 80, "薄醉档", out reject);
                    AdvanceBossTurn();
                });
            yield return Check("薄醉：档位薄醉、跳过概率 " + settings.Drunk.DrunkSkipPercent
                               + "%，掷 39 落在 40% 里 → 跳过，提示是 07:72 的原文逐字：「" + DrunkHintLiteral + "」",
                () => session.Boss.Tier == DrunkTier.Drunk
                      && session.Boss.Drunk.SkipChancePercent == settings.Drunk.DrunkSkipPercent
                      && SkippedHint() == DrunkHintLiteral
                      && view.HintText == DrunkHintLiteral
                      && view.ScreenText.Contains(DrunkHintLiteral), 1f);
            yield return Snapshot("薄醉：跳过回合 + 中央提示");

            // ── 第 7 步：酩酊（100，跳过概率 100%、-50、持续 2 回合，07:73）──
            random = new ScriptedRandomStream();
            kernel = new TurnBasedKernel(config, random);
            yield return Step("换一场：战斗外喝到 100（酩酊）；剧本里一个随机数都不放——100% 是确定事件，不该抽骰子",
                () =>
                {
                    StartBattle(FrontalRequest(), EntryCaseBossHealth, 100, "酩酊档", out reject);
                    AdvanceBossTurn();
                });
            yield return Check("酩酊：必定跳过（不掷骰子，随机数抽取次数 0），醉酒值 100 → "
                               + (settings.Drunk.MaxDrunkValue - settings.Drunk.DeadDrunkDropValue)
                               + "（-" + settings.Drunk.DeadDrunkDropValue + "），持续 "
                               + settings.Drunk.DeadDrunkDurationRounds + " 回合里还剩 "
                               + (settings.Drunk.DeadDrunkDurationRounds - 1) + " 个怪物回合",
                () => SkippedHint() == DeadDrunkHintLiteral
                      && random.DrawCount == 0
                      && session.Boss.DrunkValue == settings.Drunk.MaxDrunkValue - settings.Drunk.DeadDrunkDropValue
                      && session.Boss.Drunk.DeadDrunkRoundsRemaining == settings.Drunk.DeadDrunkDurationRounds - 1
                      && view.ScreenText.Contains(DeadDrunkHintLiteral), 1f);
            yield return Snapshot("酩酊：必定跳过 + -50 + 中央提示");

            // ── 第 8 步：持续期内的第二个怪物回合 ──
            yield return Step("酩酊持续期里的第二个怪物回合（此时醉酒值已经降到 50，档位算微醺，但持续期没走完）",
                AdvanceBossTurn);
            yield return Check("仍然跳过（持续期内不重新掷骰，抽取次数仍是 0）、醉酒值不再下降、提示仍是 07:73 那句"
                               + "——这是 BossDrunkRules 先判持续期带来的边界，见文件头「已知边界」3",
                () => SkippedHint() == DeadDrunkHintLiteral
                      && random.DrawCount == 0
                      && session.Boss.DrunkValue == settings.Drunk.MaxDrunkValue - settings.Drunk.DeadDrunkDropValue
                      && session.Boss.Drunk.DeadDrunkRoundsRemaining == 0
                      && session.CompletedRounds == 2);
            yield return Snapshot("酩酊持续期的第二回合：仍然跳过");
        }

        // ───────────────────────── ④ 打死 BOSS：战斗结束与剧情出口键 ─────────────────────────

        [UnityTest]
        public IEnumerator Victory_EndsBattle_ExitKeyIsVictory()
        {
            yield return EnterWorld();
            yield return Step("挂上白盒状态板，开一场 BOSS 只剩 3 点血的仗（演示用注入值：血量原文没给，等 C91）",
                () => CreatePanel("打完与出口键"));
            yield return CheckPanelRendered();

            random = new ScriptedRandomStream().Roll(10, 0);
            kernel = new TurnBasedKernel(config, random);
            bool started = false;
            BattleEntryReject reject = BattleEntryReject.None;
            yield return Step("正面攻击开战（07:20-23）：BOSS 生命 " + FinishCaseBossHealth + "，玩家先手",
                () => started = StartBattle(FrontalRequest(), FinishCaseBossHealth, 0, "打完收工", out reject));
            yield return Check("开出来了：玩家回合、BOSS 满血 " + FinishCaseBossHealth + "、战斗还没结束（出口键为空）",
                () => started
                      && session.Phase == BattlePhase.PlayerTurn
                      && session.Boss.Health == FinishCaseBossHealth
                      && !session.IsOver
                      && session.ExitKey == null);

            // ── 第 4 步：先打没打死的那一下，再把回合交给怪物（07:56 出招后立刻转敌方回合）──
            SkillCastResult first = default;
            yield return Step("放招式 1（伤害 " + settings.PlayerSkills.Skill1Damage + "）：BOSS 还剩 "
                              + (FinishCaseBossHealth - settings.PlayerSkills.Skill1Damage)
                              + " 点血；接着走一个怪物回合（剧本掷 0 → BOSS 招式 1），好让玩家回到自己的回合",
                () =>
                {
                    first = CastAndPresent(PlayerSkill.Skill1);
                    AdvanceBossTurn();
                });
            yield return Check("还没结束：BOSS 活着、战斗仍在进行、出口键仍是空（没结束就没有结果键，07 没有写胜负条件，按模块的口径）",
                () => first.Accepted
                      && !session.Boss.IsDefeated
                      && !session.IsOver
                      && session.ExitKey == null
                      && session.Phase == BattlePhase.PlayerTurn);
            yield return Snapshot("BOSS 还剩一口气");

            // ── 第 5 步：招式 2 补掉最后两点血（怒气：招式 1 攒的 1 点刚好够它耗）──
            SkillCastResult second = default;
            yield return Step("放招式 2（耗 1 怒气、伤害 " + settings.PlayerSkills.Skill2Damage + "）：BOSS 生命归零 → 战斗结束",
                () => second = CastAndPresent(PlayerSkill.Skill2));
            yield return Check("打赢了：BOSS 生命归零、战斗结束、结果是 Victory，出口键 = \""
                               + BattleExitKeys.Victory + "\"（交给剧情侧 NarrativeRules.CompleteBattle 的唯一入口）",
                () => second.Accepted
                      && session.Boss.Health == 0
                      && session.IsOver
                      && session.Outcome == BattleOutcome.Victory
                      && session.ExitKey == BattleExitKeys.Victory);
            yield return Check("屏幕上看得见：面板阶段变成「战斗结束」",
                () => view.ScreenText.Contains("阶段：战斗结束"), 1f);
            yield return Snapshot("打完：出口键 Victory");
        }

        // ───────────────────────── 进场与装配 ─────────────────────────

        /// <summary>
        /// 走 Boot 真实流程进世界（标题「开始」→ SampleScene），再把内核装起来。
        /// 配置资产取不到、或配置自检不过，都属于前置条件不成立，直接 Assert.Fail 中断（后面的步骤全是噪音）。
        /// </summary>
        private IEnumerator EnterWorld()
        {
            yield return EnterWorldFromTitle();

            config = LoadConfigAsset();
            settings = config.Settings;
            string problem = config.Validate();
            Assert.IsNull(problem, "TurnBasedConfig.asset 自检没过：" + problem + "（这是配置的问题，不是回放的）");

            // 本回放不演道具：IBattleItemInventory 适配器要等 Inventory 模块（guide「接线清单」），
            // 传 null 就是模块定义的「什么道具都没有」，不假造一个背包。
            random = new ScriptedRandomStream();
            kernel = new TurnBasedKernel(config, random);
        }

        /// <summary>按资产路径取数值真源（与正式接线同一份资产）。</summary>
        private static TurnBasedConfig LoadConfigAsset()
        {
#if UNITY_EDITOR
            TurnBasedConfig asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TurnBasedConfig>(ConfigAssetPath);
            if (asset == null)
            {
                Assert.Fail("加载不到配置资产 " + ConfigAssetPath + "：TurnBased 的数值真源就是它，回放自己装配内核时取的也是它。"
                            + "资产不在就先把它补回来（正式接线时由 MonsterInstaller 引用同一个字段）。");
            }

            return asset;
#else
            Assert.Fail("TurnBased 回放只在编辑器里跑：要按资产路径读 " + ConfigAssetPath);
            return null;
#endif
        }

        /// <summary>生成白盒状态板（回放运行时生成，Track() 登记，收尾统一销毁）。</summary>
        private void CreatePanel(string title)
        {
            GameObject host = new GameObject("TurnBasedWhitebox");
            view = host.AddComponent<BattleWhiteboxView>();
            Track(host);
            view.Note("白盒状态板就位：" + title);
        }

        /// <summary>
        /// 「面板真的画在屏幕上了」：后面所有「屏幕上看得见」的检查点读的都是 <see cref="BattleWhiteboxView.ScreenText"/>，
        /// 它在 OnGUI 没跑过时会退回「按当前状态现算」——那样就算面板根本没画出来，检查点也会绿。
        /// 所以每条用例开面板之后先钉一次「OnGUI 跑过」；批处理没有图形设备时按不适用跳过（不假红）。
        /// </summary>
        private IEnumerator CheckPanelRendered()
        {
            yield return Check("白盒面板确实画在屏幕上了（OnGUI 跑过；批处理下这一条不适用）",
                () => Application.isBatchMode || view.HasRendered, 2f);
        }

        /// <summary>
        /// 按判定开一场仗：快照是演示注入值（玩家血量、BOSS 血量与战斗外醉酒值，原文都没写，等 C91）。
        /// 被拒时 <see cref="session"/> 置空（= 没开成），面板保留上一场的显示，方便对照。
        /// </summary>
        private bool StartBattle(
            BattleEntryRequest request,
            int bossMaxHealth,
            int bossDrunkValue,
            string title,
            out BattleEntryReject reject)
        {
            PlayerBattleSnapshot playerSnapshot = new PlayerBattleSnapshot(DemoPlayerHealth, DemoPlayerHealth);
            BossBattleSnapshot bossSnapshot = new BossBattleSnapshot(bossMaxHealth, bossMaxHealth, bossDrunkValue);

            bool started = kernel.TryStartBattle(request, playerSnapshot, bossSnapshot, out BattleSession opened, out reject);
            session = started ? opened : null;

            if (!started)
            {
                view.Note("这一下没进战斗：" + BattleEntryRules.Describe(reject));
                return false;
            }

            view.Bind(session, settings, title);
            view.Note("开战：" + session.Entry.Describe());
            view.Present();
            return true;
        }

        /// <summary>结束玩家回合并把怪物回合走完（界面上的「结束回合」走的也是 <see cref="BattleSession.SkipPlayerTurn"/>）。</summary>
        private void AdvanceBossTurn()
        {
            session.SkipPlayerTurn();
            session.RunBossTurn();
            view.Present();
        }

        /// <summary>放一个招式，并把这次动作的事件播到面板上（表现层照 <c>BattleSession.Events</c> 播）。</summary>
        private SkillCastResult CastAndPresent(PlayerSkill skill)
        {
            SkillCastResult result = session.TryCastSkill(skill);
            view.Present();
            return result;
        }

        // ───────────────────────── 入参快照与事件读数 ─────────────────────────

        /// <summary>偷袭：潜行 + 偷袭命中 + 玩家先动手（07:15-18）。</summary>
        private static BattleEntryRequest SneakRequest() => new BattleEntryRequest(
            playerSneaking: true,
            sneakAttackSucceeded: true,
            playerInAlertZone: false,
            playerInHostileZone: false,
            monsterState: MonsterAlertState.Unaware,
            initiator: BattleInitiator.PlayerAttacked);

        /// <summary>负对照：潜行了但偷袭没成功，且不在警戒 / 敌对区域（07:17、:22 的条件各少一条）。</summary>
        private static BattleEntryRequest SneakFailedRequest() => new BattleEntryRequest(
            playerSneaking: true,
            sneakAttackSucceeded: false,
            playerInAlertZone: false,
            playerInHostileZone: false,
            monsterState: MonsterAlertState.Unaware,
            initiator: BattleInitiator.PlayerAttacked);

        /// <summary>正面攻击：玩家在警戒区域内（或怪物警戒）且玩家先动手（07:20-23）。</summary>
        private static BattleEntryRequest FrontalRequest() => new BattleEntryRequest(
            playerSneaking: false,
            sneakAttackSucceeded: false,
            playerInAlertZone: true,
            playerInHostileZone: false,
            monsterState: MonsterAlertState.Alert,
            initiator: BattleInitiator.PlayerAttacked);

        /// <summary>被打：玩家在敌对区域内（或怪物敌对）且 BOSS 先动手（07:25-28）。</summary>
        private static BattleEntryRequest AmbushRequest() => new BattleEntryRequest(
            playerSneaking: false,
            sneakAttackSucceeded: false,
            playerInAlertZone: false,
            playerInHostileZone: true,
            monsterState: MonsterAlertState.Hostile,
            initiator: BattleInitiator.BossAttacked);

        /// <summary>最近一次动作里「怪物跳过回合」的提示文案；没跳过返回 null。</summary>
        private string SkippedHint()
        {
            var events = session.Events;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i].Kind == BattleEventKind.BossTurnSkipped)
                {
                    return events[i].HintText;
                }
            }

            return null;
        }

        /// <summary>最近一次动作里 BOSS 饮酒**实际**回了多少血（已吃减疗）；没有饮酒事件返回 -1。</summary>
        private int DrankHealedAmount()
        {
            var events = session.Events;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i].Kind == BattleEventKind.BossDrank)
                {
                    return events[i].SecondaryAmount;
                }
            }

            return -1;
        }
    }
}
