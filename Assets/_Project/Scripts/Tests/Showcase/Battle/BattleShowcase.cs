// 职责：回合制战斗（Battle 模块）回放——SampleScene 村南围栏外的「阶段一 BOSS（占位）」：
//   走到跟前按 E → 开战对白 → 叠加加载战斗场景（战斗相机接管、世界暂停）→ 出招 / 用药 → 胜负 → 回到世界；
//   ① Victory_BossLeavesAndFlagWritten：打赢后相机 / 输入 / 暂停 / HUD 全部恢复，写「已击败」标记、BOSS 退场、再按 E 不触发；
//   ② Downed_RetreatsAndCanFightAgain：被击倒走 retreat，BOSS 还在、标记没写，再按 E 能重新开打。
//   胜负大字等完全淡入再截（只等「可见」会截到淡入第一帧，横条和字几乎透明）；BOSS 退场逐帧盯着收场黑幕：
//   必须在黑幕还盖着时就已隐藏，揭幕后第一帧画面里没有它（剧情回写排在揭幕之前，见 BattleFlow 文件头）。
//
// ── 舞台：ScenePath 返回 null + EnterDemoWorld（走 Boot 真实流程，标题「开始」→ SampleScene）───────────────
//   BOSS NPC 是 SampleScene 里的 demo 内容（Npc_SampleBoss，(-4, -8)，2026-10-16 W7b 由院子 (-4.2, 8.4) 挪到院外南面）：
//   离出生点 11.4 米、离巡逻线 21 米、在村口演出区外；院子出口只有演出触发区东边那条缝，所以去它跟前一律
//   GoThroughSouthGap（框架 DemoScene：缝口候场、预判两条巡逻线西端的视野、一口气跑过）→ 往西南到 BOSS 正东 6 米的候场点。
//   接法 = DialogueInteractable（焦点 / 交互提示 / 头顶标记）+ NarrativeTrigger（targetKind SampleBoss，交互转交）
//   + NarrativeFlagVisibility（world.sampleboss.defeated 成立即退场）。站位点在它正东 1.4 米，最近的别的 NPC（长者）在 10 米外。
//
// ── 输入：全走真实路径 ──────────────────────────────────────────────────────────────────────────
//   走路推虚拟摇杆；交互按 Gameplay/Interact（E）；对白按 Dialogue/Advance；出招按 Dialogue/Choice1–3（战斗期间借用的键盘 1/2/3）；
//   道具没有键位，点道具格的按钮（同玩家鼠标点击走的 onClick）。
//
// ── 胜负怎么做到可控：回放内换一份数值（BattleSetup.OverrideSettings），不改 TurnBasedConfig 资产 ───────────
//   战斗随机流从会话主种子派生、没有测试口（BattleSetup 文件头 D8），所以靠数值定死 BOSS 的行为：
//   · 打赢那条：BOSS 只出招式 1（权重 1:0:0，不喝酒不重击）、伤害 1。没调成 0 是因为满血喝药回血为 0、看不到回血飘字；
//     最坏每回合挨 1 下（BOSS 220 血、玩家 120 血都不至于被打死）。玩家侧用原文值：招式 1/2/3 = 18/23/32，招式 3 后 3 次 +8，
//     按「怒气满就放招式 3，否则招式 1」约 9 个回合打完 220 血（18×3+32 → 26×3+32 …）。
//   · 被击倒那条：同样只出招式 1，伤害 120（= 本场玩家生命，sp07:129-133），BOSS 第一次出手就击倒玩家。
//   微醺 20% 跳过回合只会让 BOSS 少出手，不影响结论。
//
// ── 2026-10-16 数值落地（本文件跟着改的地方）─────────────────────────────────────────────────────
//   资产里 `sample_boss` 血量 12 → 220、本场玩家生命 10 → 120（照 sp07:149、:129-133 的原文值），
//   于是 DownedBossDamage 从 10 改成 120（含义仍是「等于本场玩家生命」）；打赢那条的出招循环回合数变多，超时仍是 90 秒。
//
// ── 统一交互第二波（PRP/interaction）：交互提示随文字加宽，「挑战 · 阶段一 BOSS（占位）」整句显示不截断；
//   按 E 时 BOSS 小人转向玩家、玩家小人转向 BOSS（D11，朝向保持）。战斗舞台上的两只小人是另一份实例
//   （BOSS 按 StagePrefab 现实例化、玩家是战斗场景里摆好的），朝向由 BattleActor 立即翻面定，不带世界里的朝向保持。
//
// ── 给物：经背包公开口 LootService.SettleMonsterDrop 放一瓶治疗药水（1004）入包 ─────────────────────────
//   与开箱同一条「写背包分区 → 弹获得通知」的路径；村里能走到的箱子没有 1004（Crate_C 在巡逻线以南），这条是最短的真实入包口。
//
// ── 2026-10-15 BOSS 主动发难（PRP/combat-iteration D6 / T6.4）────────────────────────────────────────────
//   `Npc_SampleBoss` 挂了 BossAggro（BossRosterConfig `sample_boss`：发难半径 5 米以原位为圆心、反应 3 秒、冲刺 3 米/秒、拴绳自动 10 米；
//   接触距离 = 交互半径 2 × 0.6 = 1.2 米）：玩家进 5 米 → 警觉（头顶 MarkerAggro 红「!」）→ 3 秒内走到跟前按 E = 正面攻击、玩家先手、不冲；
//   磨蹭满 3 秒 → 冲刺 → 接触 → 照常嘲讽对白（与按 E 同一段，对白 1004）→「被打」开战、BOSS 先手；
//   战后回原位、解除武装，玩家**同时**离开察觉范围（全局 MonsterConfig 警戒半径 6）与发难半径 5 才恢复待机。
//   · 新增三条：BossAggro_ChallengeWithinReaction_FrontalAttack / BossAggro_ChargesTauntsAndAmbushes / BossAggro_RetreatThenRearmsAfterLeaving。
//     开战方式只读战斗界面（左上角回合提示、两根生命条），框架 ShowcaseScenario.TurnBattle 的 WatchBattleOpening。
//   · 原 ① ② 的「走近 → 按 E」压进同一步连续完成（WalkToBossAndInteractWithinReaction）：从发难半径外的候场点直走到站位，
//     半径里只走 3.6 米（约 1.2 秒），提示几帧落定后立即按 E；提示的两条检查点挪到按 E 之后（读的是按 E 之前那一刻记下的状态），断言语义不变。
//     不为回放在 Runtime 里加关发难的测试口。
//   · ④ Drill_Downed：巡逻线上市令打中你那一下现在按升级规则交给回合制（市令配了战斗数据，「被打」市令先手）。
//     回放内换一份数值让回合制里一下击倒（同 ②，不需要输入），实时生命照样归零 → 遭遇桥回写 Downed，断言不变。
//
// ── 2026-10-16 BOSS 挪到院外南面 (-4, -8)（W7b 用户拍板，PRP/combat-iteration w7-plan 任务 A）─────────────────────
//   · 站位从「BOSS 正南 1.4 米」改为「正东 1.4 米」(-2.6, -8)：过了缝口是从东边过来的，沿东西一条线直走进去，
//     半径里那段与挪位前一样是 3.6 米（反应期 3 秒内做完按 E）；正南要在半径里绕过它半圈，超出反应期；正北则站在纸片 BOSS
//     的身后（相机在南面朝北看），截图里玩家被它挡住。发难三条用例的站点同理沿这条东西线放：4 米 / 5.5 米 / 6.5 米，
//     离开 / 回来都往东走——离主怪 / 巡逻者乙的巡逻线（x ≥ 13.86、z 3.4 / 5）15 米以上，不会把玩家带进巡逻怪视野。
//   · 演示桩那两条改回挪位前的原站位 (0.6, 8.8) 与原走法（出生点直走过去）：W6b 为避开旧 BOSS 发难半径挪到东侧 (2.4, 8.8)
//     并经 (0.5, 5.5) 拐一下，BOSS 挪走后（离原站位 17 米）不再需要。
using System;
using System.Collections;
using System.Globalization;
using Game.Battle;
using Game.CharacterPuppet;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Interaction;
using Game.Loot;
using Game.Player;
using Game.TurnBased;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
// 两个同名 BattleOutcome（Game.TurnBased / Game.Narrative）：剧情侧只取用到的类型，不 using 整个命名空间（PRP §7 坑 4）。
using NarrativeService = Game.Narrative.NarrativeService;
using NarrativeSaveData = Game.Narrative.NarrativeSaveData;

namespace Game.Tests.Showcase.Battle
{
    [Category("Showcase")]
    public sealed class BattleShowcase : ShowcaseScenario
    {
        private const string BossObjectName = "Npc_SampleBoss";
        private const string BossDisplayName = "阶段一 BOSS（占位）";

        /// <summary>BOSS 的交互提示动词（SampleScene 里 Npc_SampleBoss 的 DialogueInteractable.verb，PRP/interaction D10）。</summary>
        private const string BossVerb = "挑战";
        private const string DefeatedFlag = "world.sampleboss.defeated";
        private const string RetreatOutcome = "Downed";
        private const int HealItemId = 1004;

        /// <summary>
        /// 实时遭遇演示桩（SampleScene 里 `Npc_BattleDrill`，站在村北 (1.5, 8.8)，与长者 / 旅人都不在一条线上）：
        /// 按 E 拉起对白 1002 → 剧情走到**空 payload 的战斗档** → 十几米外的巡逻怪那一仗由 `EncounterBattleBridge`
        /// 认领（roadmap C5 剩下那一半）。
        /// </summary>
        private const string DrillObjectName = "Npc_BattleDrill";

        /// <summary>演示桩的交互提示动词（`DialogueInteractable.verb`）。</summary>
        private const string DrillVerb = "较量";

        /// <summary>演示桩的显示名（`DialogueInteractable.displayName`，提示整句是「较量 · 演练桩」）。</summary>
        private const string DrillDisplayName = "演练桩";

        /// <summary>
        /// 演示桩那一局用的剧情（`sample_battle_drill`）：brief → fight（空 payload，声明 Victory + Downed）→
        /// cleared / downed（见 `Tables/Data/narrative/sample_battle_drill.json`）。
        /// </summary>
        private const string DrillStory = "sample_battle_drill";
        private const string DrillClearedFlag = "world.samplebattle.cleared";
        private const string DrillDownedFlag = "world.samplebattle.downed";

        /// <summary>
        /// 走到演示桩西侧 0.9 米（交互半径 2 内；长者 / BOSS / 旅人都在 5 米外，焦点只会落在它身上）。
        /// 2026-10-16 改回挪位前的原站位：W6b 为避开 BOSS 旧位置 (-4.2, 8.4) 的发难半径挪到东侧 (2.4, 8.8)，BOSS 挪到 (-4, -8) 后离这里 17 米。
        /// </summary>
        private static readonly Vector2 DrillStandPoint = new Vector2(0.6f, 8.8f);

        /// <summary>给物借道「怪物掉落」入包口时填的妖物 id（只进埋点与事件载荷，没有订阅方）。</summary>
        private const int GiftYaoId = 1;

        /// <summary>打赢那条 BOSS 招式 1 的伤害：挨得到、回血看得见，又打不死玩家（推演见文件头）。</summary>
        private const int VictoryBossDamage = 1;

        /// <summary>被击倒那条 BOSS 招式 1 的伤害：等于本场玩家生命（`BossRosterConfig` 的 playerHealth 120，sp07:129-133）。</summary>
        private const int DownedBossDamage = 120;

        /// <summary>
        /// 站位点：BOSS（(-4, -8)）正东 1.4 米（交互半径 2 内、接触距离 1.2 之外；离长者等 NPC 10 米以上，焦点只会落在 BOSS 上）。
        /// 为什么是正东不是挪位前的正南：见文件头「2026-10-16 BOSS 挪到院外南面」。
        /// </summary>
        private static readonly Vector2 BossStandPoint = new Vector2(-2.6f, -8f);

        /// <summary>
        /// 去 BOSS 跟前的候场点：BOSS 原位正东 6 米（发难半径 5 之外）。过了缝口跑到这里停下、切回步行，再一口气直走到
        /// <see cref="BossStandPoint"/> 按 E——半径里只走 3.6 米。缝口出口 (11.6, -2) 到这里一路在围栏（z −1）以南，没有碰撞体。
        /// </summary>
        private static readonly Vector2 BossStagingPoint = new Vector2(2f, -8f);

        /// <summary>名册里这只 BOSS 的 id（发难的半径 / 反应时间 / 生命上限都按它取）。</summary>
        private const string SampleBossId = "sample_boss";

        /// <summary>BOSS 头顶「要冲过来了」的红「!」（Npc_SampleBoss 下的子物体，BossAggro.alertIndicator）。</summary>
        private const string AggroMarkerName = "MarkerAggro";

        /// <summary>开战嘲讽对白（`sample_boss_battle` 的 taunt 阶段 payload）：按 E 挑战与它冲上来接触，都是这一段。</summary>
        private const int TauntDialogueId = 1004;

        /// <summary>发难半径里的站点：BOSS 原位 (-4, -8) 正东 4 米（发难半径 5 之内、接触距离 1.2 之外），离候场点 2 米。</summary>
        private static readonly Vector2 AggroWatchPoint = new Vector2(0f, -8f);

        /// <summary>两个圈之间的站点：离原位 5.5 米（发难半径 5 之外、察觉范围 6 之内），同在原位正东那条线上。</summary>
        private static readonly Vector2 AggroBetweenRingsPoint = new Vector2(1.5f, -8f);

        /// <summary>
        /// 两个圈之外的站点：离原位 6.5 米（发难半径 5 与察觉范围 6 都在外面），同在原位正东那条线上；
        /// 离围栏（z −1）7 米、离两条巡逻线 15 米以上，往这边退不会进巡逻怪的视野。
        /// </summary>
        private static readonly Vector2 AggroOutsidePoint = new Vector2(2.5f, -8f);

        private IInputService input;
        private PlayerModel player;
        private NarrativeService narrative;
        private DialogueService dialogue;
        private DialogueRules dialogueRules;
        private IInteractionFocus focus;
        private IUIService ui;
        private IWorldPauseService pause;
        private ILoadingCurtain curtain;
        private LootService loot;
        private BattleFlow flow;
        private BattleArena arena;
        private BattleSetup setup;
        private BattleScenePresenter presenter;
        private DialogueInteractable bossEntry;
        private GameObject bossObject;
        private Camera worldCamera;
        private IDisposable settingsOverride;

        /// <summary>BOSS 的发难组件、头顶红「!」、名册里那一行（半径 / 反应时间 / 生命上限）。</summary>
        private BossAggro aggro;
        private GameObject aggroMarker;
        private BossDefinition bossRow;

        /// <summary>最近一次开始的对白 id（订阅 DialogueService.OnStarted；嘲讽是不是按 E 那一段就看它）。</summary>
        private int lastDialogueId;
        private DialogueService subscribedDialogue;

        /// <summary>剧情服务（实时遭遇那两条用例要读当前阶段 / 故事标记）。</summary>
        private NarrativeService stageReader;

        protected override string Module => "Battle";

        /// <summary>世界由流程加载（标题「开始」→ SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        /// <summary>收尾经流程退回标题时有一次离场保存，多等一下再销毁根作用域（同 Narrative 回放）。</summary>
        protected override float BootShutdownSettleSeconds => 0.5f;

        [TearDown]
        public void ReleaseSettingsOverride()
        {
            settingsOverride?.Dispose();
            settingsOverride = null;
            if (subscribedDialogue != null)
            {
                subscribedDialogue.OnStarted -= RecordDialogueStarted;
                subscribedDialogue = null;
            }
        }

        // ───────────────────────── ① 打赢：回世界、写标记、BOSS 退场 ─────────────────────────

        [UnityTest]
        public IEnumerator Victory_BossLeavesAndFlagWritten()
        {
            yield return EnterWorld();
            int potionsBefore = 0;
            yield return Step("演示前置：背包里放一瓶治疗药水（经背包公开口入包，屏幕弹「获得」通知）", () =>
            {
                loot.SettleMonsterDrop(GiftYaoId, new[] { HealItemId });
                potionsBefore = Potions();
            });
            yield return Check("背包里有治疗药水", () => potionsBefore >= 1);
            yield return Step($"回放内换一份数值（不改资产）：BOSS 只出招式 1、伤害 {VictoryBossDamage}——胜负可控，又能挨一下好演示回血",
                () => settingsOverride = setup.OverrideSettings(WithBossOnlySkill1(setup.Settings, VictoryBossDamage)), 0f);
            yield return GoToBossStaging();

            ChibiPuppet worldBossPuppet = bossObject.GetComponentInChildren<ChibiPuppet>();
            ChibiPuppet worldPlayerPuppet = WorldPlayerPuppet();
            yield return WalkToBossAndInteractWithinReaction("走到BOSS跟前·交互提示");
            yield return Check("交互转向：BOSS 小人转向玩家、玩家小人转向 BOSS（两边都进入朝向保持）",
                () => worldBossPuppet != null && worldBossPuppet.FacingHeld && worldPlayerPuppet != null && worldPlayerPuppet.FacingHeld, 3f);
            yield return ReadTaunt(true);
            yield return CheckEnteredBattle("进战斗场景：战斗相机接管（世界相机关掉）、世界暂停（timeScale 0）、战斗界面打开");
            yield return Check("战斗舞台的两只小人是另一份实例、没有带进世界里的朝向保持，朝向等于 BattleActor 的站位朝向（立即翻面布台）",
                () => StagePuppetFollowsActor(presenter.Stage.Boss, worldBossPuppet)
                      && StagePuppetFollowsActor(presenter.Stage.Player, worldPlayerPuppet));
            yield return Snapshot("开战·玩家先手");

            // 先出招式 1，直到 BOSS 打中玩家一下（微醺可能跳过回合），好让药水真的回血。
            yield return Step("出招式 1（键盘 1），等 BOSS 回合打中一下", null, 0f);
            yield return FightUntil(() => PlayerHealthShown() < PlayerMaxShown(), 30f, false);
            yield return Check("玩家挨了一下（头顶血条不满）", () => PlayerHealthShown() >= 0 && PlayerHealthShown() < PlayerMaxShown(), 3f);

            int healthBeforeItem = PlayerHealthShown();
            yield return WaitUntil("轮到玩家出手", () => presenter.IsAwaitingCommand, 10f);
            yield return Step("点顶部道具格「治疗药水」", null, 0f);
            yield return ClickWhenReady("道具已用掉（背包少 1）",
                () => ItemSlot(HealItemId) == null ? null : ItemSlot(HealItemId).Button,
                () => Potions() == potionsBefore - 1, 3f);
            yield return Check("飘出回血字「+N」、头顶血条回升", () => HealFloatShown() && PlayerHealthShown() > healthBeforeItem, 3f);
            yield return Snapshot("用药回血飘字");
            yield return Check($"道具格本场置暗、背包里的治疗药水 {potionsBefore} → {potionsBefore - 1}",
                () => ItemSlot(HealItemId) != null && !ItemSlot(HealItemId).IsLit && Potions() == potionsBefore - 1, 3f);

            yield return Step("继续出招（怒气满就放招式 3，否则招式 1），直到打倒 BOSS", null, 0f);
            yield return FightUntil(() => HintShows(BattleCueRules.VictoryText), 90f, false);
            yield return Check($"BOSS 倒地，屏幕中央深色横条上的白字「{BattleCueRules.VictoryText}」完全显出", () => HintShows(BattleCueRules.VictoryText), 1f);
            yield return Check("BOSS 头顶的名字 / 状态字 / 两根条整块都在画面内（锚在站立时的头顶，不跟倒地动作走）", () => HudInsideScreen("BossHud"));
            yield return Snapshot("胜利一瞬");

            // 逐帧盯收场：黑幕落下 → 幕下回到世界并回写剧情 → 揭幕。记下 BOSS 是不是在幕下就隐藏了、揭幕后第一帧它在不在。
            bool coverSeen = false, hiddenUnderCurtain = false, shownAtReveal = true;
            yield return WaitUntil("收场黑幕落下又完全揭开", () =>
            {
                if (curtain.IsCovered)
                {
                    coverSeen = true;
                    if (!arena.IsLoaded && !bossObject.activeSelf) hiddenUnderCurtain = true;
                    return false;
                }

                if (!coverSeen) return false;
                shownAtReveal = bossObject.activeSelf;
                return true;
            }, 15f);
            yield return Check("BOSS 在黑幕还盖着时就已退场；揭幕后第一帧世界里没有它（不会当着玩家凭空消失）",
                () => hiddenUnderCurtain && !shownAtReveal);
            yield return Snapshot("黑幕揭开后第一帧·BOSS已不在");

            yield return CheckBackToWorld();
            yield return Check($"写入 {DefeatedFlag}；BOSS NPC 退场（物体已隐藏）；交互提示不再显示 BOSS",
                () => narrative.HasStoryFlag(DefeatedFlag) && !bossObject.activeSelf && !ReferenceEquals(focus.Current, bossEntry) && !HudShowsBoss(), 5f);

            yield return Step("站在原地再按一次交互键（E）", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Interact);
            bool triggered = false;
            float watchUntil = Time.realtimeSinceStartup + 1.5f;
            yield return WaitUntil("观察 1.5 秒", () =>
            {
                triggered |= ui.Get<DialogueView>() != null || flow.IsBattleRunning || arena.IsLoaded;
                return Time.realtimeSinceStartup >= watchUntil;
            }, 3f);
            yield return Check("不再触发：没有开战对白、没有进战斗", () => !triggered && ui.Get<DialogueView>() == null && !flow.IsBattleRunning);
        }

        // ───────────────────────── ② 被击倒：retreat，BOSS 还在，可重打 ─────────────────────────

        [UnityTest]
        public IEnumerator Downed_RetreatsAndCanFightAgain()
        {
            yield return EnterWorld();
            yield return Step($"回放内换一份数值（不改资产）：BOSS 只出招式 1、伤害 {DownedBossDamage}（= 本场玩家生命），一下击倒",
                () => settingsOverride = setup.OverrideSettings(WithBossOnlySkill1(setup.Settings, DownedBossDamage)), 0f);
            yield return GoToBossStaging();

            yield return WalkToBossAndInteractWithinReaction(null);
            yield return ReadTaunt(false);
            yield return CheckEnteredBattle("进战斗场景：战斗相机接管、世界暂停、战斗界面打开");

            yield return Step("出招式 1（键盘 1），等 BOSS 还手", null, 0f);
            yield return FightUntil(() => HintShows(BattleCueRules.DefeatText), 60f, true);
            yield return Check($"玩家倒地，屏幕中央深色横条上的白字「{BattleCueRules.DefeatText}」完全显出", () => HintShows(BattleCueRules.DefeatText), 1f);
            yield return Check("玩家头顶血条在画面内（锚在站立时的头顶，不跟倒地动作走）", () => HudInsideScreen("PlayerHud"));
            yield return Snapshot("被击倒一瞬");

            yield return CheckBackToWorld();
            yield return WaitCurtainRevealed();
            yield return Check($"剧情走了 retreat（结局 {RetreatOutcome}、故事结束）；BOSS 还在；{DefeatedFlag} 没写",
                () =>
                {
                    NarrativeSaveData saved = narrative.Capture();
                    return saved.Current == null && saved.Outcome == RetreatOutcome && !narrative.HasStoryFlag(DefeatedFlag)
                           && bossObject.activeSelf;
                }, 5f);
            yield return Check("BOSS 仍是交互焦点，提示照常显示", () => ReferenceEquals(focus.Current, bossEntry) && HudShowsBoss(), 3f);
            yield return Snapshot("被击倒后BOSS还在");

            yield return Step("再按交互键（E），重打一次", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Interact);
            yield return ReadTaunt(false);
            yield return CheckEnteredBattle("第二次也进了战斗场景：战斗相机接管、世界暂停");
            yield return Snapshot("第二次开战");
            yield return FightUntil(() => HintShows(BattleCueRules.DefeatText), 60f, true);
            yield return CheckBackToWorld();
            yield return Check("第二场打完同样退回：结局仍是 retreat、BOSS 还在",
                () => narrative.Capture().Outcome == RetreatOutcome && bossObject.activeSelf && !narrative.HasStoryFlag(DefeatedFlag), 5f);
        }

        // ───────────────────────── ③ 实时遭遇演示桩：第一次交互要有回应 ─────────────────────────

        /// <summary>
        /// demo 场景村北的「较量」演示桩（`Npc_BattleDrill`）：走过去按一下 E，**必须看得见回应**——
        /// 交互提示写着动词、对白 1002 拉起来、剧情从 `brief` 走到空 payload 的 `fight`。
        /// <para>
        /// 为什么单开一条：上面两条只覆盖 BOSS 那条路（剧情 NPC → 黑幕 → 回合制战场）。实时遭遇那条完全是另一条
        /// 时间线（不换场景、没有战斗界面），而它的入口此前**一条回放都没有**——「按了 E 什么都没有」这种
        /// 现象只会在回放里现形，EditMode 测不出来。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator Drill_InteractStartsTheBriefAndEntersTheLiveEncounter()
        {
            yield return EnterWorld();
            yield return WalkToDrillAndCheckPrompt("走到演示桩跟前·交互提示");

            yield return Step("按交互键（E）", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Interact);
            yield return Check("按 E 有回应：拉起演示对白（1002）", () => ui.Get<DialogueView>() != null, 5f);
            yield return Check("剧情停在这一局的入口阶段 brief（demo 内容 sample_battle_drill）",
                () => StageId() == "brief", 3f);
            yield return Snapshot("较量·对白");

            yield return Step("按推进键（空格）读完演示对白", null, 0f);
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((dialogue.IsRunning || ui.Get<DialogueView>() != null) && Time.realtimeSinceStartup < deadline)
            {
                if (dialogueRules.Phase == DialogueSaveData.Phase.AwaitAdvance)
                {
                    yield return Input.Press(input.Actions.Dialogue.Advance);
                }
                else
                {
                    yield return null;
                }
            }

            yield return Check("对白收起：不卡在对白上（剧情阶段因此才推进得动）",
                () => !dialogue.IsRunning && ui.Get<DialogueView>() == null, 5f);
            yield return Check("剧情走到空 payload 的实时遭遇档 fight：这一仗已交给遭遇桥（不是回合制战场，没有 BattleView）",
                () => StageId() == "fight" && ui.Get<BattleView>() == null && !flow.IsBattleRunning, 5f);
            yield return Snapshot("较量·实时遭遇档");
        }

        // ───────────────────────── ④ 实时遭遇演示桩：被击倒 → 这一关结束 ─────────────────────────

        /// <summary>
        /// 演示桩那一局的**失败出口**：走进巡逻线被击倒 → 遭遇桥用 `Downed` 回写剧情 → 剧情走到 `downed`
        /// （End，写 `world.samplebattle.downed`），而不是像 2026-10-10 00:52 那一局那样卡在战斗阶段
        /// （那份内容当时只声明了 `Victory`，遥测里留下的是 `battle_result_rejected {result: Downed}`）。
        /// </summary>
        [UnityTest]
        public IEnumerator Drill_DownedEndsTheStageInsteadOfSticking()
        {
            yield return EnterWorld();
            yield return WalkToDrillAndCheckPrompt(null);
            yield return Step("按交互键（E）拉起演示对白", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Interact);
            yield return Check("对白起来（剧情停在 brief）", () => ui.Get<DialogueView>() != null && StageId() == "brief", 5f);

            yield return Step("按推进键（空格）读完对白，让剧情走到实时遭遇档", null, 0f);
            float deadline = Time.realtimeSinceStartup + 15f;
            while (StageId() != "fight" && Time.realtimeSinceStartup < deadline)
            {
                if (dialogueRules.Phase == DialogueSaveData.Phase.AwaitAdvance)
                {
                    yield return Input.Press(input.Actions.Dialogue.Advance);
                }
                else
                {
                    yield return null;
                }
            }

            yield return Check("停在实时遭遇档 fight（内容是 `sample_battle_drill`）", () => StageId() == "fight", 5f);

            yield return Step($"回放内换一份数值（不改资产）：回合制里 BOSS 只出招式 1、伤害 {DownedBossDamage}——巡逻线上市令打中你那一下"
                              + "会按升级规则交给回合制（「被打」市令先手），让它在回合制里一下击倒（不需要输入）",
                () => settingsOverride = setup.OverrideSettings(WithBossOnlySkill1(setup.Settings, DownedBossDamage)), 0f);
            yield return Step("走巡逻线那条老路：切跑 → 北侧绕行 → 观察点 → 再往南踏进巡逻线（潜行档），等被打倒", null, 0f);
            yield return GoToPatrolLookout();
            yield return Walk(new Vector2(0f, -1f), 3.5f);
            yield return Check("玩家被打倒（探索生命归零）", () => player.Health <= 0, 60f);

            yield return Check("剧情没有卡在战斗阶段：走到 downed 出口、写 world.samplebattle.downed，这一关结束",
                () => StageId() == null && stageReader.HasStoryFlag(DrillDownedFlag), 20f);
            yield return Snapshot("较量·被击倒后这一关结束");
        }

        // ───────────────────────── ⑤ BOSS 发难：反应期内按 E = 正面攻击、玩家先手、它不冲 ─────────────────────────

        /// <summary>
        /// 走进 BOSS 的发难半径（5 米）→ 它警觉、头顶红「!」亮 → 3 秒反应期内走到跟前按 E →
        /// 照常的开战对白 → 正面攻击、玩家先手（第一次轮到你出招时 BOSS 满血、你没挨过打），它一次都没冲。
        /// 验完开战方式用回放内数值让它一下击倒，撤退收尾。
        /// </summary>
        [UnityTest]
        public IEnumerator BossAggro_ChallengeWithinReaction_FrontalAttack()
        {
            yield return EnterWorld();
            yield return Step($"回放内换一份数值（不改资产）：BOSS 只出招式 1、伤害 {DownedBossDamage}——验完开战方式后一下击倒、撤退收尾",
                () => settingsOverride = setup.OverrideSettings(WithBossOnlySkill1(setup.Settings, DownedBossDamage)), 0f);
            yield return Check("BOSS 发难已武装：站在原位待机，头顶没有红「!」",
                () => aggro.IsArmed && aggro.State == BossAggroState.Idle && BossAtHome() && !AggroMarkerShown());
            yield return GoToBossStaging();

            yield return Step($"走进 BOSS 的发难半径（{bossRow.AggroRadius} 米，以原位为圆心）：离原位 4 米", null, 0f);
            yield return WalkTo(AggroWatchPoint, 0.2f, 5f);
            yield return Check($"BOSS 警觉：头顶红「!」亮起，开始计反应时间（{bossRow.AggroReactionSeconds} 秒）",
                () => aggro.State == BossAggroState.Alerted && AggroMarkerShown(), 1f);
            yield return Snapshot("BOSS警觉·头顶红感叹号");

            yield return WalkToBossAndInteractWithinReaction(null);
            yield return ReadTaunt(false);
            var opening = new BattleOpeningRecord();
            yield return WatchBattleOpening(opening, 20f);
            yield return CheckEnteredBattle("进战斗场景：战斗相机接管、世界暂停、战斗界面打开");
            yield return Check($"开战方式：正面攻击、玩家先手——第一次轮到你出招时是「{BattleHudRules.TurnText(1, true)}」、出招前没挨打，"
                               + $"BOSS 满血 {bossRow.MaxHealth}/{bossRow.MaxHealth}（{opening.Describe()}）",
                () => opening.CommandAwaited && !opening.PlayerHitBeforeCommand
                      && opening.TurnTextAtCommand == BattleHudRules.TurnText(1, true)
                      && opening.BossHealthAtCommand == bossRow.MaxHealth && opening.BossMaxAtCommand == bossRow.MaxHealth);
            yield return Check("BOSS 发难进入「战斗中」：这一仗是你按 E 开的（它没冲、没接触，开战理由一直是「玩家先动手」）",
                () => aggro.State == BossAggroState.InBattle && BossEntryInitiator() == BattleInitiator.PlayerAttacked);
            yield return Snapshot("反应期内按E·正面攻击·玩家先手");

            yield return Step("出招式 1（键盘 1），BOSS 还手一下击倒（回放内数值），撤退收尾", null, 0f);
            yield return FightUntil(() => !flow.IsBattleRunning, 60f, true);
            yield return CheckBackToWorld();
        }

        // ───────────────────────── ⑥ BOSS 发难：磨蹭满 3 秒 → 冲刺 → 接触 → 嘲讽 →「被打」BOSS 先手 ─────────────────────────

        /// <summary>
        /// 走进发难半径后站定不动（在接触距离外）、不按 E → 反应时间计满它冲过来 → 停在你跟前（接触距离）→
        /// 照常的嘲讽对白（与按 E 同一段）→「被打」开战、BOSS 先手（开场是「敌方回合」，你还没出招就先挨了一下）。
        /// 回放内数值让那一下就击倒，撤退收尾。
        /// </summary>
        [UnityTest]
        public IEnumerator BossAggro_ChargesTauntsAndAmbushes()
        {
            yield return EnterWorld();
            yield return Step($"回放内换一份数值（不改资产）：BOSS 只出招式 1、伤害 {DownedBossDamage}（= 本场玩家生命）——它先手那一下就击倒、撤退收尾",
                () => settingsOverride = setup.OverrideSettings(WithBossOnlySkill1(setup.Settings, DownedBossDamage)), 0f);
            yield return Check("BOSS 发难已武装：站在原位待机", () => aggro.IsArmed && aggro.State == BossAggroState.Idle && BossAtHome());
            yield return GoToBossStaging();

            yield return Step($"走进发难半径站定不动、不按 E：离原位 4 米（接触距离 {aggro.ContactDistance:0.##} 米之外）", null, 0f);
            yield return WalkTo(AggroWatchPoint, 0.2f, 5f);
            yield return Check("BOSS 警觉：头顶红「!」亮起", () => aggro.State == BossAggroState.Alerted && AggroMarkerShown(), 1f);

            float reactionSeen = 0f;
            yield return WaitUntil($"站着不动，等 BOSS 愣满 {bossRow.AggroReactionSeconds} 秒冲过来", () =>
            {
                if (aggro.State == BossAggroState.Alerted)
                {
                    reactionSeen = Mathf.Max(reactionSeen, aggro.ReactionElapsed);
                }

                return aggro.State == BossAggroState.Charging || aggro.State == BossAggroState.Contact;
            }, bossRow.AggroReactionSeconds + 3f);
            yield return Check($"它是愣满反应时间才冲的：警觉期计到 {reactionSeen:0.##} 秒（反应时间 {bossRow.AggroReactionSeconds} 秒），头顶红「!」冲刺中仍亮着",
                () => reactionSeen >= bossRow.AggroReactionSeconds - 0.1f
                      && (aggro.State != BossAggroState.Charging || AggroMarkerShown()));
            yield return Snapshot("BOSS冲刺");

            float contactGap = -1f;
            yield return Check($"冲到你跟前停住（接触距离 {aggro.ContactDistance:0.##} 米，没贴到身上），照常的嘲讽对白出现——和按 E 挑战是同一段（对白 {TauntDialogueId}）",
                () =>
                {
                    if (ui.Get<DialogueView>() == null || lastDialogueId != TauntDialogueId) return false;
                    contactGap = BossGapToPlayer();
                    return contactGap <= aggro.ContactDistance + 0.15f && contactGap >= aggro.ContactDistance - 0.3f;
                }, 5f);
            yield return Snapshot("接触·嘲讽对白");
            yield return ReadTaunt(false);

            var opening = new BattleOpeningRecord();
            yield return WatchBattleOpening(opening, 20f);
            // BOSS 先手 = 开场就是它的回合：它要么先打了你（血条掉了 / 一下击倒），要么醉酒跳过了这一回合
            // （sample_boss 战斗外醉酒 50，微醺每回合 20% 跳过，W6b 第二跑实测遇到）——那时你第一次出招已经是第 2 回合。
            yield return Check($"开战方式：被打、BOSS 先手——开场是「{BattleHudRules.TurnText(1, false)}」，你还没出招它的回合就先走完了"
                               + $"（先打了你，或醉酒跳过、轮到你时已是「{BattleHudRules.TurnText(2, true)}」）（{opening.Describe()}）",
                () => opening.FirstTurnText == BattleHudRules.TurnText(1, false)
                      && (opening.PlayerHitBeforeCommand || opening.TurnTextAtCommand == BattleHudRules.TurnText(2, true)));
            yield return Snapshot("被打·BOSS先手");

            yield return Step("BOSS 出手一下就击倒（回放内数值；它醉酒跳过回合时你按招式 1 等它出手），撤退收尾", null, 0f);
            yield return FightUntil(() => !flow.IsBattleRunning, 60f, true);
            yield return CheckBackToWorld();
        }

        // ───────────────────────── ⑦ 被打开战后打输：BOSS 回原位、解除武装，离开两个圈才重新武装 ─────────────────────────

        /// <summary>
        /// 让 BOSS 冲上来「被打」开战并打输（撤退，BOSS 不被击败）→ 回到世界：它在原位、解除武装 →
        /// 只退出发难半径 5 米、还在察觉范围 6 米内：不恢复 → 同时离开 6 米与 5 米：恢复待机 → 再进 5 米：重新警觉。
        /// 最后赶在反应时间计满之前退出半径收尾（别让它再冲一次）。
        /// </summary>
        [UnityTest]
        public IEnumerator BossAggro_RetreatThenRearmsAfterLeaving()
        {
            yield return EnterWorld();
            yield return Step($"回放内换一份数值（不改资产）：BOSS 只出招式 1、伤害 {DownedBossDamage}（= 本场玩家生命）——这一仗打输",
                () => settingsOverride = setup.OverrideSettings(WithBossOnlySkill1(setup.Settings, DownedBossDamage)), 0f);
            yield return GoToBossStaging();

            yield return Step("走进发难半径站定不动，等 BOSS 冲过来、嘲讽、「被打」开战", null, 0f);
            yield return WalkTo(AggroWatchPoint, 0.2f, 5f);
            yield return WaitUntil("BOSS 冲到跟前，开战嘲讽对白出现", () => ui.Get<DialogueView>() != null, bossRow.AggroReactionSeconds + 5f);
            yield return ReadTaunt(false);
            yield return CheckEnteredBattle("「被打」开战：战斗相机接管、世界暂停、战斗界面打开");

            yield return Step("BOSS 先手一下击倒（回放内数值）：这一仗打输、撤退", null, 0f);
            yield return FightUntil(() => !flow.IsBattleRunning, 60f, true);
            yield return CheckBackToWorld();
            yield return WaitCurtainRevealed();
            yield return Check($"撤退：剧情走了 retreat（结局 {RetreatOutcome}）、{DefeatedFlag} 没写，BOSS 还站在村里",
                () =>
                {
                    NarrativeSaveData saved = narrative.Capture();
                    return saved.Current == null && saved.Outcome == RetreatOutcome && !narrative.HasStoryFlag(DefeatedFlag)
                           && bossObject.activeSelf;
                }, 5f);
            yield return Check($"BOSS 回到原位、解除武装：头顶没有红「!」；你还站在离原位 {PlayerHomeDistance():0.##} 米处（发难半径里）也不再警觉",
                () => aggro.State == BossAggroState.Disarmed && BossAtHome() && !AggroMarkerShown(), 3f);
            yield return Snapshot("撤退后·BOSS回原位解除武装");

            yield return Step("退出发难半径（5 米）、但还在察觉范围（6 米）里：走到离原位 5.5 米处站 1.5 秒", null, 0f);
            yield return WalkTo(AggroBetweenRingsPoint, 0.2f, 5f);
            bool stayedDisarmed = true;
            float watchUntil = Time.realtimeSinceStartup + 1.5f;
            yield return WaitUntil("站 1.5 秒", () =>
            {
                stayedDisarmed &= aggro.State == BossAggroState.Disarmed;
                return Time.realtimeSinceStartup >= watchUntil;
            }, 3f);
            yield return Check($"只出了 5 米、还在 6 米察觉范围内（离原位 {PlayerHomeDistance():0.##} 米）：BOSS 一直解除武装，不恢复待机",
                () => stayedDisarmed && aggro.State == BossAggroState.Disarmed);

            yield return Step("再往外走，同时离开察觉范围（6 米）与发难半径（5 米）：离原位 6.5 米", null, 0f);
            yield return WalkTo(AggroOutsidePoint, 0.2f, 5f);
            yield return Check($"两个圈都出了（离原位 {PlayerHomeDistance():0.##} 米）：BOSS 恢复待机（重新武装）",
                () => aggro.State == BossAggroState.Idle && !AggroMarkerShown(), 3f);
            yield return Snapshot("离开察觉范围·恢复待机");

            yield return Step("再走进发难半径（离原位 4 米）", null, 0f);
            yield return WalkTo(AggroWatchPoint, 0.2f, 5f);
            yield return Check("BOSS 重新警觉：头顶红「!」再亮", () => aggro.State == BossAggroState.Alerted && AggroMarkerShown(), 1f);
            yield return Snapshot("再进半径·重新警觉");

            yield return Step("赶在反应时间计满之前退出半径收尾（别让它再冲一次）", null, 0f);
            yield return WalkTo(AggroOutsidePoint, 0.2f, 5f);
            yield return Check("退出半径：BOSS 回到待机、仍在原位（没冲过来）",
                () => aggro.State == BossAggroState.Idle && BossAtHome(), 2f);
        }

        // ───────────────────────── 进场 / 走位 / 对白 ─────────────────────────

        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入 / 玩家 / 剧情 / 对白 / 背包 / 战斗服务可用", ResolveAll,
                "进世界后容器里取不到输入 / 玩家 / 剧情 / 对白 / 背包 / 战斗服务（检查 Boot 场景 GameBootstrap 上的 BattleInstaller 与两份配置）");
            bossEntry = FindRequired<DialogueInteractable>(BossObjectName);
            bossObject = bossEntry.gameObject;
            worldCamera = Camera.main;
            aggro = FindRequired<BossAggro>(BossObjectName);
            Transform marker = FindDeep<Transform>(bossObject.transform, AggroMarkerName);
            aggroMarker = marker == null ? null : marker.gameObject;
            BossRoster roster = ResolveService<BossRoster>();
            if (roster == null || !roster.TryGet(SampleBossId, out bossRow))
            {
                Assert.Fail($"容器里取不到 BossRoster，或名册里没有「{SampleBossId}」那一行（BossRosterConfig.asset）。");
            }

            // 嘲讽对白是不是按 E 那一段：记下每次开始的对白 id（收尾在 TearDown 里退订）。
            lastDialogueId = 0;
            subscribedDialogue = dialogue;
            subscribedDialogue.OnStarted += RecordDialogueStarted;
            yield return Check("村里站着 BOSS NPC，世界相机在渲染", () => bossObject.activeSelf && worldCamera != null && worldCamera.enabled);
        }

        private void RecordDialogueStarted(DialogueStartedEvent started) => lastDialogueId = started.DialogueId;

        private bool ResolveAll()
        {
            input = ResolveService<IInputService>();
            player = ResolveService<PlayerModel>();
            narrative = ResolveService<NarrativeService>();
            stageReader = narrative;
            dialogue = ResolveService<DialogueService>();
            dialogueRules = ResolveService<DialogueRules>();
            focus = ResolveService<IInteractionFocus>();
            ui = ResolveService<IUIService>();
            pause = ResolveService<IWorldPauseService>();
            curtain = ResolveService<ILoadingCurtain>();
            loot = ResolveService<LootService>();
            flow = ResolveService<BattleFlow>();
            arena = ResolveService<BattleArena>();
            setup = ResolveService<BattleSetup>();
            presenter = ResolveService<IBattlePresenter>() as BattleScenePresenter;
            return input != null && input.Actions != null && player != null && narrative != null && narrative.IsReady
                   && dialogue != null && dialogueRules != null && focus != null && ui != null && pause != null && curtain != null && loot != null
                   && flow != null && arena != null && setup != null && presenter != null;
        }

        /// <summary>
        /// 从出生点去 BOSS 那边（2026-10-16 挪到院外南面之后）：经缝口绕出院子（<see cref="ShowcaseScenario.GoThroughSouthGap"/>：
        /// 缝口候场、等两条巡逻线西端接下来几秒都扫不到缝里那段路再一口气跑过）→ 往西南跑到 <see cref="BossStagingPoint"/>
        /// （BOSS 正东 6 米，发难半径外）停下、切回步行。全程不进发难半径，BOSS 保持待机。
        /// </summary>
        private IEnumerator GoToBossStaging()
        {
            yield return GoThroughSouthGap(new[] { BossStagingPoint });
        }

        /// <summary>
        /// 走到 BOSS 跟前并**立刻**按 E（2026-10-15 BOSS 发难之后的写法，见文件头）：从候场点走进它 5 米发难半径到按下 E 要在
        /// 3 秒反应期内一口气做完，所以这一整段只记一步、中间不放检查点——提示几帧落定（最多再等 1 秒，不记条目）后记下
        /// 「按 E 之前那一刻」的焦点 / 提示状态，按下 E，再把原来的两条提示检查点与一条「它还在反应期里」放到按 E 之后判。
        /// </summary>
        private IEnumerator WalkToBossAndInteractWithinReaction(string snapshot)
        {
            yield return Step("走到村南围栏外的 BOSS 跟前（它正东 1.4 米）并立刻按交互键（E）——它开了发难：踏进 5 米半径到按下 E 要在 3 秒反应期内连续做完"
                              + "（检查点挪到按 E 之后，读的是按 E 之前那一刻记下的状态）", null, 0f);
            yield return WalkTo(BossStandPoint, 0.3f, 8f);
            // 提示要几帧才落定（焦点 → 提示显示 → 量宽）：最多再等 1 秒，不记报告条目。
            float settleUntil = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < settleUntil
                   && !(ReferenceEquals(focus.Current, bossEntry) && HudShowsChallenge() && HudPromptFitsText()))
            {
                yield return null;
            }

            // 进战斗前这一步钉住提示动词（PRP/interaction D10）：BOSS 经 handover 进战斗，提示应是「挑战」而不是「对话」。
            bool focusedOnBoss = ReferenceEquals(focus.Current, bossEntry) && HudShowsChallenge();
            bool promptFits = HudPromptFitsText();
            if (!string.IsNullOrEmpty(snapshot)) yield return Snapshot(snapshot);
            BossAggroState stateAtPress = aggro.State;
            float reactionAtPress = aggro.ReactionElapsed;
            bool homeAtPress = BossAtHome();
            yield return Input.Press(input.Actions.Gameplay.Interact);
            yield return Check($"焦点落在 BOSS 上，底部交互提示写着「{BossVerb} · {BossDisplayName}」（按 E 之前那一刻）", () => focusedOnBoss);
            yield return Check("提示整句完整显示：胶囊随文字加宽（超过最小宽 320），文字没有被截成省略号（按 E 之前那一刻）", () => promptFits);
            yield return Check($"按 E 时 BOSS 还在反应期里、站在原位没冲（发难状态 {stateAtPress}，已计 {reactionAtPress:0.##} 秒，"
                               + $"不到 {bossRow.AggroReactionSeconds} 秒）",
                () => stateAtPress == BossAggroState.Alerted && reactionAtPress < bossRow.AggroReactionSeconds && homeAtPress);
        }

        private IEnumerator WalkToDrillAndCheckPrompt(string snapshot)
        {
            yield return Step("走到村北的「较量」演示桩跟前（(0.6, 8.8)：长者 / BOSS / 旅人都在 5 米外，焦点只会落在它身上）", null, 0f);
            yield return WalkTo(DrillStandPoint, 0.3f, 15f);
            yield return Check($"焦点落在演示桩上，底部交互提示写着「{DrillVerb} · {DrillDisplayName}」",
                () => ReferenceEquals(focus.Current, DrillEntry()) && HudShowsDrill(), 3f);
            if (!string.IsNullOrEmpty(snapshot)) yield return Snapshot(snapshot);
        }

        private DialogueInteractable DrillEntry() => FindRequired<DialogueInteractable>(DrillObjectName);

        private bool HudShowsDrill()
        {
            InteractPromptHudView hud = ui.Get<InteractPromptHudView>();
            return hud != null && hud.IsShown
                   && hud.LabelText == InteractPromptHudView.FormatLabel(DrillVerb, DrillDisplayName);
        }

        /// <summary>剧情当前阶段 id；没有在跑的剧情（整段走完 / 还没开始）返回 null。用 `stageReader` 读，避免和 `narrative` 混。</summary>
        private string StageId()
        {
            NarrativeSaveData.Frame frame = stageReader == null ? null : stageReader.Capture().Current;
            return frame == null ? null : frame.StageId;
        }

        /// <summary>开战对白：出现 → 打完字按推进键 → 对白收起。</summary>
        private IEnumerator ReadTaunt(bool snapshot)
        {
            yield return Check("开战对白出现（BOSS 的挑衅）", () => ui.Get<DialogueView>() != null, 5f);
            if (snapshot)
            {
                yield return Wait(1.5f);
                yield return Snapshot("开战对白");
            }

            yield return Step("按推进键（空格）读完开战对白", null, 0f);
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((dialogue.IsRunning || ui.Get<DialogueView>() != null) && Time.realtimeSinceStartup < deadline)
            {
                if (dialogueRules.Phase == DialogueSaveData.Phase.AwaitAdvance)
                {
                    yield return Input.Press(input.Actions.Dialogue.Advance);
                }
                else
                {
                    yield return null;
                }
            }

            yield return Check("开战对白结束", () => !dialogue.IsRunning, 3f);
        }

        // ───────────────────────── 战斗 ─────────────────────────

        private IEnumerator CheckEnteredBattle(string expect)
        {
            yield return Check(expect, InBattle, 20f);
            yield return WaitCurtainRevealed();
        }

        private bool InBattle()
        {
            BattleStage stage = presenter.Stage;
            return stage != null && stage.Camera != null && stage.Camera.enabled
                   && worldCamera != null && !worldCamera.enabled
                   && pause.IsPaused && Time.timeScale == 0f
                   && presenter.View != null && ui.Get<BattleView>() != null
                   && arena.IsLoaded && flow.IsBattleEngaged;
        }

        private IEnumerator CheckBackToWorld()
        {
            yield return Check("回到世界：世界相机恢复、Gameplay 输入恢复、世界不再暂停、HUD 层可见、战斗场景已卸载",
                () => !flow.IsBattleRunning && !arena.IsLoaded && ui.Get<BattleView>() == null
                      && worldCamera != null && worldCamera.enabled && Camera.main == worldCamera
                      && input.Actions.Gameplay.enabled && !pause.IsPaused && Time.timeScale > 0f
                      && ui.IsLayerVisible(UILayer.Hud), 15f);
        }

        // 出招循环（FightUntil）、读头顶生命条（ReadBattleHealthBar）、换 BOSS 招式数值（WithBossOnlySkill1）
        // 2026-10-15 收进框架 ShowcaseScenario.TurnBattle（Monster 回放的升级回合制也要用），行为逐字不变。

        // 要完全淡入：只看 HintVisible（alpha > 0）会在淡入第一帧就成立，截图里横条和字都几乎透明（20261007-194728 批次的 08 / 01）。
        private bool HintShows(string text) =>
            presenter.View != null && presenter.View.HintOpaque && presenter.View.HintText == text;

        /// <summary>战斗界面里名为 <paramref name="hudName"/> 的头顶块是否显示着且四角都在屏幕内（Overlay 画布，世界坐标即屏幕像素）。</summary>
        private bool HudInsideScreen(string hudName)
        {
            BattleView view = presenter.View;
            RectTransform hud = view == null ? null : FindDeep<RectTransform>(view.transform, hudName);
            if (hud == null || !hud.gameObject.activeInHierarchy) return false;
            var corners = new Vector3[4];
            hud.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                if (corner.x < 0f || corner.x > Screen.width || corner.y < 0f || corner.y > Screen.height) return false;
            }

            return true;
        }

        private BattleSlotWidget ItemSlot(int itemId)
        {
            BattleView view = presenter.View;
            if (view == null) return null;
            string key = itemId.ToString(CultureInfo.InvariantCulture);
            foreach (BattleSlotWidget slot in view.GetComponentsInChildren<BattleSlotWidget>(false))
            {
                if (slot.Key == key) return slot;
            }

            return null;
        }

        private bool HealFloatShown()
        {
            BattleView view = presenter.View;
            if (view == null) return false;
            foreach (TMP_Text text in view.GetComponentsInChildren<TMP_Text>(false))
            {
                if (text.name.StartsWith("Float", StringComparison.Ordinal) && text.text.StartsWith("+", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>玩家头顶血条上的「生命 当前 / 上限」里的当前值；取不到返回 -1。</summary>
        private int PlayerHealthShown() => ReadBattleHealthBar(presenter.View, PlayerHudName, 0);

        private int PlayerMaxShown() => ReadBattleHealthBar(presenter.View, PlayerHudName, 1);

        // ───────────────────────── 杂项 ─────────────────────────

        private bool HudShowsBoss()
        {
            InteractPromptHudView hud = ui.Get<InteractPromptHudView>();
            return hud != null && hud.IsShown && hud.LabelText.Contains(BossDisplayName);
        }

        /// <summary>交互提示文字没被截断：TMP 没走省略号，且胶囊比最小宽度 320 更宽（这句放不进 320）。</summary>
        private bool HudPromptFitsText()
        {
            InteractPromptHudView hud = ui.Get<InteractPromptHudView>();
            if (hud == null || !hud.IsShown) return false;
            TMP_Text label = FindDeep<TMP_Text>(hud.transform, "Label");
            RectTransform root = FindDeep<RectTransform>(hud.transform, "Root");
            return label != null && root != null && !label.isTextTruncated && root.rect.width > 320f;
        }

        /// <summary>世界里玩家的小人（统一交互登记表里玩家标记层级下的 ChibiPuppet）。</summary>
        private ChibiPuppet WorldPlayerPuppet()
        {
            IInteractionRegistry registry = ResolveService<IInteractionRegistry>();
            return registry == null || registry.Actor == null ? null : registry.Actor.GetComponentInChildren<ChibiPuppet>();
        }

        /// <summary>舞台角色的小人不是世界里那一只、不在朝向保持中、朝向等于 BattleActor 的站位朝向。</summary>
        private static bool StagePuppetFollowsActor(BattleActor actor, ChibiPuppet worldPuppet)
        {
            ChibiPuppet puppet = actor == null ? null : actor.GetComponentInChildren<ChibiPuppet>();
            return puppet != null && puppet != worldPuppet && !puppet.FacingHeld && puppet.FaceLeft == actor.FaceLeft;
        }

        /// <summary>交互提示整句正好是「挑战 · BOSS 名」（动词来自 BOSS 的 DialogueInteractable.verb）。</summary>
        private bool HudShowsChallenge()
        {
            InteractPromptHudView hud = ui.Get<InteractPromptHudView>();
            return hud != null && hud.IsShown && hud.LabelText == InteractPromptHudView.FormatLabel(BossVerb, BossDisplayName);
        }

        private int Potions() => loot.Items.TryGetValue(HealItemId, out int count) ? count : 0;

        // ───────────────────────── BOSS 发难 ─────────────────────────

        /// <summary>BOSS 在原位（水平距离 5 厘米内）。</summary>
        private bool BossAtHome()
        {
            Vector3 position = bossObject.transform.position;
            Vector3 home = aggro.Home;
            return new Vector2(position.x - home.x, position.z - home.z).magnitude <= 0.05f;
        }

        /// <summary>头顶红「!」此刻亮着。</summary>
        private bool AggroMarkerShown() => aggroMarker != null && aggroMarker.activeInHierarchy;

        /// <summary>玩家离 BOSS 原位的水平距离（米，逻辑平面 = 场景 XZ）。</summary>
        private float PlayerHomeDistance()
        {
            Vector3 home = aggro.Home;
            return Vector2.Distance(new Vector2(home.x, home.z), player.Position);
        }

        /// <summary>BOSS 此刻离玩家的水平距离（米，逻辑平面 = 场景 XZ）。</summary>
        private float BossGapToPlayer()
        {
            Vector3 position = bossObject.transform.position;
            return Vector2.Distance(new Vector2(position.x, position.z), player.Position);
        }

        /// <summary>BOSS 局势里记的开战理由（谁先动的手）：它冲上来碰到你才置成「BOSS 先动手」。</summary>
        private BattleInitiator BossEntryInitiator()
        {
            BossEntryContext context = bossObject.GetComponent<BossEntryContext>();
            return context == null ? BattleInitiator.PlayerAttacked : context.Initiator;
        }
    }
}
