// 职责：回合制战斗（Battle 模块）回放——SampleScene 村西北角的「阶段一 BOSS（占位）」：
//   走到跟前按 E → 开战对白 → 叠加加载战斗场景（战斗相机接管、世界暂停）→ 出招 / 用药 → 胜负 → 回到世界；
//   ① Victory_BossLeavesAndFlagWritten：打赢后相机 / 输入 / 暂停 / HUD 全部恢复，写「已击败」标记、BOSS 退场、再按 E 不触发；
//   ② Downed_RetreatsAndCanFightAgain：被击倒走 retreat，BOSS 还在、标记没写，再按 E 能重新开打。
//   胜负大字等完全淡入再截（只等「可见」会截到淡入第一帧，横条和字几乎透明）；BOSS 退场逐帧盯着收场黑幕：
//   必须在黑幕还盖着时就已隐藏，揭幕后第一帧画面里没有它（剧情回写排在揭幕之前，见 BattleFlow 文件头）。
//
// ── 舞台：ScenePath 返回 null + EnterDemoWorld（走 Boot 真实流程，标题「开始」→ SampleScene）───────────────
//   BOSS NPC 是 SampleScene 里的 demo 内容（Npc_SampleBoss，(-4.2, 8.4)）：离出生点 5 米、离巡逻线十几米、在村口演出区外；
//   接法 = DialogueInteractable（焦点 / 交互提示 / 头顶标记）+ NarrativeTrigger（targetKind SampleBoss，交互转交）
//   + NarrativeFlagVisibility（world.sampleboss.defeated 成立即退场）。站位点在它正南 1.4 米，最近的别的 NPC 在 5 米外。
//
// ── 输入：全走真实路径 ──────────────────────────────────────────────────────────────────────────
//   走路推虚拟摇杆；交互按 Gameplay/Interact（E）；对白按 Dialogue/Advance；出招按 Dialogue/Choice1–3（战斗期间借用的键盘 1/2/3）；
//   道具没有键位，点道具格的按钮（同玩家鼠标点击走的 onClick）。
//
// ── 胜负怎么做到可控：回放内换一份数值（BattleSetup.OverrideSettings），不改 TurnBasedConfig 资产 ───────────
//   战斗随机流从会话主种子派生、没有测试口（BattleSetup 文件头 D8），所以靠数值定死 BOSS 的行为：
//   · 打赢那条：BOSS 只出招式 1（权重 1:0:0，不喝酒不重击）、伤害 1。没调成 0 是因为满血喝药回血为 0、看不到回血飘字；
//     最坏每回合挨 1 下，玩家 10 血 + 药水，7 个回合内必打死 12 血的 BOSS（1+1+1 → 招式 3 的 3 → 带加伤的招式 1 各 2）。
//   · 被击倒那条：同样只出招式 1，伤害 10（= 本场玩家生命），BOSS 第一次出手就击倒玩家。
//   微醺 20% 跳过回合只会让 BOSS 少出手，不影响结论。
//
// ── 给物：经背包公开口 LootService.SettleMonsterDrop 放一瓶治疗药水（1004）入包 ─────────────────────────
//   与开箱同一条「写背包分区 → 弹获得通知」的路径；村里能走到的箱子没有 1004（Crate_C 在巡逻线以南），这条是最短的真实入包口。
using System;
using System.Collections;
using System.Globalization;
using Game.Battle;
using Game.Core.Flow;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Loot;
using Game.Player;
using Game.TurnBased;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private const string DefeatedFlag = "world.sampleboss.defeated";
        private const string RetreatOutcome = "Downed";
        private const int HealItemId = 1004;

        /// <summary>给物借道「怪物掉落」入包口时填的妖物 id（只进埋点与事件载荷，没有订阅方）。</summary>
        private const int GiftYaoId = 1;

        /// <summary>打赢那条 BOSS 招式 1 的伤害：挨得到、回血看得见，又打不死玩家（推演见文件头）。</summary>
        private const int VictoryBossDamage = 1;

        /// <summary>被击倒那条 BOSS 招式 1 的伤害：等于本场玩家生命（BossRosterConfig 的 playerHealth 占位 10）。</summary>
        private const int DownedBossDamage = 10;

        /// <summary>站位点：BOSS 正南 1.4 米（交互半径 2 内；离长者 / 旅人 5 米以上，焦点只会落在 BOSS 上）。</summary>
        private static readonly Vector2 BossStandPoint = new Vector2(-4.2f, 7.0f);

        private IInputService input;
        private PlayerModel player;
        private NarrativeService narrative;
        private DialogueService dialogue;
        private DialogueRules dialogueRules;
        private DialogueInteractionFocus focus;
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

            yield return WalkToBossAndCheckPrompt("走到BOSS跟前·交互提示");
            yield return Step("按交互键（E）", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Interact);
            yield return ReadTaunt(true);
            yield return CheckEnteredBattle("进战斗场景：战斗相机接管（世界相机关掉）、世界暂停（timeScale 0）、战斗界面打开");
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
                () => narrative.HasStoryFlag(DefeatedFlag) && !bossObject.activeSelf && focus.Current != bossEntry && !HudShowsBoss(), 5f);

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

            yield return WalkToBossAndCheckPrompt(null);
            yield return Step("按交互键（E）", null, 0f);
            yield return Input.Press(input.Actions.Gameplay.Interact);
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
            yield return Check("BOSS 仍是交互焦点，提示照常显示", () => focus.Current == bossEntry && HudShowsBoss(), 3f);
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

        // ───────────────────────── 进场 / 走位 / 对白 ─────────────────────────

        private IEnumerator EnterWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入 / 玩家 / 剧情 / 对白 / 背包 / 战斗服务可用", ResolveAll,
                "进世界后容器里取不到输入 / 玩家 / 剧情 / 对白 / 背包 / 战斗服务（检查 Boot 场景 GameBootstrap 上的 BattleInstaller 与两份配置）");
            bossEntry = FindRequired<DialogueInteractable>(BossObjectName);
            bossObject = bossEntry.gameObject;
            worldCamera = Camera.main;
            yield return Check("村里站着 BOSS NPC，世界相机在渲染", () => bossObject.activeSelf && worldCamera != null && worldCamera.enabled);
        }

        private bool ResolveAll()
        {
            input = ResolveService<IInputService>();
            player = ResolveService<PlayerModel>();
            narrative = ResolveService<NarrativeService>();
            dialogue = ResolveService<DialogueService>();
            dialogueRules = ResolveService<DialogueRules>();
            focus = ResolveService<DialogueInteractionFocus>();
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

        private IEnumerator WalkToBossAndCheckPrompt(string snapshot)
        {
            yield return Step("走到村西北角的 BOSS 跟前（离巡逻怪十几米，绕开村口演出区）", null, 0f);
            yield return WalkTo(BossStandPoint, 0.3f, 8f);
            yield return Check($"焦点落在 BOSS 上，底部交互提示写着「对话 · {BossDisplayName}」",
                () => focus.Current == bossEntry && HudShowsBoss(), 3f);
            if (!string.IsNullOrEmpty(snapshot)) yield return Snapshot(snapshot);
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

        /// <summary>
        /// 轮到玩家就出招，直到 <paramref name="done"/> 成立或超时：<paramref name="skill1Only"/> 时只按 1；
        /// 否则怒气满（招式 3 格亮）按 3，不然按 1（招式 1 攒怒气）。按下后等这一手被接住再进下一轮。
        /// </summary>
        private IEnumerator FightUntil(Func<bool> done, float timeout, bool skill1Only)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                if (!presenter.IsAwaitingCommand || presenter.View == null)
                {
                    yield return null;
                    continue;
                }

                InputAction key = !skill1Only && presenter.View.IsSkillLit(2)
                    ? input.Actions.Dialogue.Choice3
                    : input.Actions.Dialogue.Choice1;
                yield return Input.Press(key);
                float accepted = Time.realtimeSinceStartup + 2f;
                while (presenter.IsAwaitingCommand && !done() && Time.realtimeSinceStartup < accepted)
                {
                    yield return null;
                }
            }
        }

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
        private int PlayerHealthShown() => ReadPlayerBar(0);

        private int PlayerMaxShown() => ReadPlayerBar(1);

        private int ReadPlayerBar(int part)
        {
            BattleView view = presenter.View;
            Transform hud = view == null ? null : FindDeep<Transform>(view.transform, "PlayerHud");
            Transform bar = hud == null ? null : FindDeep<Transform>(hud, "HealthBar");
            TMP_Text label = bar == null ? null : FindDeep<TMP_Text>(bar, "Label");
            if (label == null) return -1;
            string text = label.text;
            int space = text.IndexOf(' ');
            string[] parts = (space >= 0 ? text.Substring(space + 1) : text).Split('/');
            return parts.Length == 2 && int.TryParse(parts[part].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : -1;
        }

        // ───────────────────────── 杂项 ─────────────────────────

        private bool HudShowsBoss()
        {
            DialogueInteractHudView hud = ui.Get<DialogueInteractHudView>();
            return hud != null && hud.IsShown && hud.LabelText.Contains(BossDisplayName);
        }

        private int Potions() => loot.Items.TryGetValue(HealItemId, out int count) ? count : 0;

        /// <summary>只换 BOSS 招式：只出招式 1（权重 1:0:0）、伤害 <paramref name="skill1Damage"/>；其余数值照装配时的那份。</summary>
        private static BattleSettings WithBossOnlySkill1(in BattleSettings source, int skill1Damage)
        {
            BossSkillSettings boss = source.BossSkills;
            var replaced = new BossSkillSettings(skill1Damage, boss.Skill2DrinkAddDrunk, boss.Skill2HealPercent, boss.Skill2HealBase,
                boss.Skill2NextSkill1DamageBonusPercent, boss.Skill3Damage, boss.Skill3StunRounds, 1, 0, 0);
            return new BattleSettings(source.Entry, source.PlayerSkills, replaced, source.Drunk, source.Flow,
                source.ItemOncePerBattle, source.InheritsDrunkValue, source.Items);
        }
    }
}
