// 职责：镜模块（Game.Mirror）回放——走 Boot 真实流程（标题「开始」→ SampleScene），虚拟键盘 / 手柄像玩家一样操作，
//   验证照镜辨认链上玩家看得见的行为：照人（V1）、照妖线索不足得模糊轮廓 → 开箱拿信 → 再照得真形（V2 V3）、自照空白（V4）、
//   镜缘刻痕（V6，每个结果画面都检查并截图）、昏暗区通灵视影子提示出现 / 消失（V7）、被巡逻怪击中三次的裂痕与暗角（V8）、
//   剧情裂痕只缩视野不致碎（V9）、镜碎页 → 按确认重开本场（V10）、结果显示期间世界暂停与对话中照镜键无效（V11）。
//   覆盖 PRP/mirror-core T10；V5（照不到）与 V12（加妖不改代码）的细则在 EditMode，本回放里第二只妖（巡逻怪）照见真形即 V12 的场景证据。
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— 既有回放没有一份认识镜模块；PlayerShowcase 的「受击 → 死亡」验的是旧的战败停留状态，与镜碎页重开是两件事（T11 另改）。
//   扩展 —— 塞进 Player / Exploration 回放会让它们认识 Mirror（依赖方向是 Mirror → 它们），按模块一份回放（module-verify.md）。
// 场景对象一律运行时按名字 / 组件属性找（Yao_WellWoman、SpiritSightZone_Well、enerme、Npc_Elder、ItemId == 1005 的物资箱），
//   逻辑坐标由 MirrorSceneBinder 按遭遇场景投影换算，不写死新物体坐标；去巡逻怪沿用 ShowcaseScenario.DemoScene.cs 的路线常量，
//   其余走动经本文件的小绕行规划（PlanRoute：绕开村口演出触发区与沿途 NPC / 箱子）。
// 读的状态都是容器里的公开只读成员：MirrorService.LastResult、MirrorResultView.Current、MirrorHudView.ShownCracks、
//   MirrorVisionView.ShownRadius、SpiritSightPresenter.ActiveZone / HintRoot、MirrorCrackPresenter.IsRestarting 等；
//   「画面上看得见某段文字」一类检查扫面板下全部文字组件，不按预制体子物体名找。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Dialogue;
using Game.Loot;
using Game.Mirror;
using Game.Monster;
using Game.Player;
using Game.Quest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests.Showcase.Mirror
{
    [Category("Showcase")]
    public sealed class MirrorShowcase : ShowcaseScenario
    {
        private const string WellWomanName = "Yao_WellWoman";
        private const string WellZoneName = "SpiritSightZone_Well";
        private const string PatrolMonsterName = "enerme";

        /// <summary>线索物品「破旧信笺」（tbitem 1005）：妖物表里井边妇人的 clue_items；场景里装着它的箱子按 ItemId 找。</summary>
        private const int ClueItemId = 1005;

        /// <summary>镜碎页上绝不能出现的字样（PRD V10）。</summary>
        private const string LoadGameText = "读档";

        /// <summary>按下照镜键到结果画面显示的上限（真实秒；含首次经 Addressables 加载面板与真形图）。</summary>
        private const float ResultTimeoutSeconds = 3f;

        /// <summary>截图前等面板的缩放过渡走完（真实秒，不乘节奏倍率；结果画面 2.5 秒会自动关，不能等太久）。</summary>
        private const float UiSettleSeconds = 0.35f;

        /// <summary>对话进行中按照镜键后，观察结果画面「始终没出现」的时长（真实秒）。</summary>
        private const float BlockedWatchSeconds = 1.2f;

        /// <summary>一路推进对白到结束的上限（真实秒）。</summary>
        private const float DialogueTimeoutSeconds = 30f;

        /// <summary>镜碎页重开本场（卸载并重载场景）的上限（真实秒）。</summary>
        private const float RestartTimeoutSeconds = 20f;

        /// <summary>绕行途经点的到达距离与每段上限。</summary>
        private const float WaypointStopDistance = 0.4f;
        private const float LegTimeoutSeconds = 12f;

        /// <summary>村口演出触发区外扩的安全边（米）：胶囊半径 0.3 + 余量。</summary>
        private const float TriggerMargin = 0.6f;

        /// <summary>路线离障碍（NPC / 箱子）中心小于这个距离就绕：NPC 碰撞盒沿 x 宽 1.6，半宽 0.8 + 胶囊 0.3 + 余量。</summary>
        private const float ObstacleClearance = 1.2f;

        /// <summary>绕行点离障碍中心的距离。</summary>
        private const float ObstacleDetourOffset = 1.5f;

        /// <summary>终点本来就贴着的障碍不参与绕行（要站到它旁边）；起点旁的障碍只要在身后（投影 ≤ 0）也不绕。</summary>
        private const float ObstacleNearSkip = 1.0f;

        /// <summary>开箱站位离 NPC 至少这么远：对白交互半径 2 + 余量；落进 NPC 交互范围时物资箱焦点会让位，按 E 反而拉起对白。</summary>
        private const float TalkerClearance = 2.6f;

        /// <summary>开箱站位离箱子中心的距离（Exploration / Session 回放实测 0.9 可开）。</summary>
        private const float CrateStandOffset = 0.9f;

        /// <summary>照妇人时的站位：离她这么远（候选依次放宽），且落在昏暗区内侧。</summary>
        private static readonly float[] StandDistances = { 2f, 1.6f, 1.3f };

        /// <summary>站位方向相对「来的方向」的偏转候选（度）。</summary>
        private static readonly float[] StandAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

        /// <summary>站位离昏暗区边沿至少这么远（米）。</summary>
        private const float ZoneInset = 0.4f;

        /// <summary>
        /// 站位空地检查：在候选点立一根比玩家略粗的胶囊（玩家挡墙胶囊半径 0.3、底 0.35、顶 1.5 米，这里半径放宽到 0.4），
        /// 与场景里非触发的碰撞体（长凳、墙、塔、NPC 碰撞盒）重叠就换下一个候选。2026-09-28 首跑时站位落在长凳 Bench_2 上走不到。
        /// </summary>
        private const float StandClearRadius = 0.4f;
        private const float StandClearBottom = 0.35f;
        private const float StandClearTop = 1.5f;

        /// <summary>路线障碍里「小摆件」的水平尺寸上限（米）与沿路线外扩的搜索范围（米），见 AddPropObstacles。</summary>
        private const float PropMaxSize = 2.5f;
        private const float PropSearchMargin = 2f;

        /// <summary>走出昏暗区时越过边沿的距离（米）。</summary>
        private const float ZoneExitMargin = 0.8f;

        /// <summary>对准朝向：摇杆轻推的幅度、最短 / 最长时长与角度容差（同 FaceMonster 的 0.3 推法）。</summary>
        private const float FaceStick = 0.3f;
        private const float FaceMinSeconds = 0.06f;
        private const float FaceMaxSeconds = 0.4f;
        private const float FaceToleranceDegrees = 6f;

        /// <summary>走向巡逻怪直到它转敌对：推到这个距离内就松杆站住（近身感知 1.5，攻击距离 0.8）。</summary>
        private const float ProvokeStopDistance = 1.0f;
        private const float ProvokeTimeoutSeconds = 8f;

        /// <summary>等第一次被击中的上限（真实秒）：转敌对后要先追到身边；之后每次间隔一个攻击冷却（1 秒）。</summary>
        private const float FirstHitTimeoutSeconds = 8f;
        private const float NextHitTimeoutSeconds = 4f;

        private IInputService inputService;
        private IUIService ui;
        private IWorldPauseService worldPause;
        private PlayerModel player;
        private MonsterModel monster;
        private EncounterStep step;
        private MirrorService mirror;
        private MirrorConfig mirrorConfig;
        private MirrorSceneBinder binder;
        private MirrorInputPresenter inputPresenter;
        private MirrorCrackPresenter crackPresenter;
        private SpiritSightPresenter sight;
        private LootService loot;
        private LootConfig lootConfig;
        private SupplyCrateFocus crateFocus;
        private DialogueService dialogue;
        private DialogueRules dialogueRules;
        private DialogueInteractionFocus dialogueFocus;
        private QuestService quest;

        protected override string Module => "Mirror";

        /// <summary>世界由流程加载（标题「开始」→ MonsterEncounterState → SampleScene），基类不再叠加载一份。</summary>
        protected override string ScenePath => null;

        /// <summary>开箱、首次照见会请求自动保存，离场还有一次保存：收尾多等 0.5 秒，免得写盘落在临时存档目录删除之后（同 SessionShowcase）。</summary>
        protected override float BootShutdownSettleSeconds => 0.5f;

        // ───────────────────────── 用例 1：照人（V1 V6 V11） ─────────────────────────

        [UnityTest]
        public IEnumerator LookAtVillager_ShowsHuman()
        {
            yield return EnterMirrorWorld();
            MirrorSubject elder = RequireSubject(ElderName);
            Vector2 elderPos = binder.LogicPositionOf(elder);

            yield return Step("走到长者身前（他西侧 1.2 米），面朝长者", null, 0f);
            yield return WalkAround(elderPos + ElderStandOffset, 0.3f, elder.transform);
            yield return FaceToward(() => elderPos);

            yield return Step("按照镜键（Gameplay/Mirror，键盘 R）", null, 0f);
            yield return Input.Press(inputService.Actions.Gameplay.Mirror);
            yield return Check($"结果画面出现：标题「{mirrorConfig.HumanTitle}」，照到的是长者（显示他的名字与形象），镜缘刻痕可见",
                () => ResultShows(MirrorResultKind.Human) && mirror.LastSubject == elder && ShowsSubjectIdentity(elder),
                ResultTimeoutSeconds);
            yield return Check("结果显示期间世界暂停（时间停住，玩家与巡逻怪都不动）", WorldPaused, 1f);
            yield return SettleForCapture();
            yield return Snapshot("照长者·是人");

            yield return CloseResultWithConfirm();
            yield return Snapshot("关闭结果·世界恢复");
        }

        // ───────────────────────── 用例 2：照妖——模糊轮廓 → 开箱拿信 → 真形；通灵视（V2 V3 V6 V7） ─────────────────────────

        [UnityTest]
        public IEnumerator WellWoman_BlurryThenTrueForm()
        {
            yield return EnterMirrorWorld();
            MirrorSubject woman = RequireSubject(WellWomanName);
            SpiritSightZone zone = RequireZone(WellZoneName);
            SupplyCrate crate = RequireClueCrate();
            Rect zoneRect = RequireZoneRect(zone);
            Vector2 womanPos = binder.LogicPositionOf(woman);
            Vector2 spawn = SpawnPoint();
            int yaoId = woman.YaoId;
            string trueName = TrueNameOf(yaoId);

            yield return Check("前提：玩家在昏暗区外、通灵视未生效；妇人尚未照见；背包里还没有旧信（1005）",
                () => !zoneRect.Contains(player.Position) && !sight.IsActive && sight.ShownCount == 0
                      && !mirror.IsIdentified(yaoId) && !HasItem(ClueItemId),
                3f);

            yield return Step("走进井边的昏暗区，停在井边妇人身前", null, 0f);
            yield return WalkAround(PickStandPoint(womanPos, player.Position, zoneRect, woman.transform.position.y), 0.3f,
                woman.transform);
            yield return Check("通灵视生效（生效区域就是井边昏暗区）：半径内的妖头顶出现影子提示（提示数 = 半径内妖数 ≥ 1，普通人头顶没有），提示不带名字",
                () => sight.IsActive && sight.ActiveZone == zone && sight.ShownCount >= 1
                      && sight.ShownCount == YaoWithinSight() && HintsCarryNoText(),
                3f);
            yield return Snapshot("通灵视·影子提示");

            yield return Step("面朝妇人按照镜键（R）", null, 0f);
            yield return FaceToward(() => womanPos);
            yield return Input.Press(inputService.Actions.Gameplay.Mirror);
            yield return Check($"结果「{mirrorConfig.BlurryTitle}」：镜里只有压暗的轮廓，画面上没有真形名「{trueName}」；"
                               + "记为见过轮廓、尚未照见；镜缘刻痕可见",
                () => ResultShows(MirrorResultKind.Blurry) && mirror.LastSubject == woman && mirror.LastResult.YaoId == yaoId
                      && ShowsBlurryWithoutName(trueName)
                      && mirror.HasGlimpsed(yaoId) && !mirror.IsIdentified(yaoId),
                ResultTimeoutSeconds);
            yield return SettleForCapture();
            yield return Snapshot("照妇人·模糊轮廓");
            yield return CloseResultWithConfirm();

            yield return Step($"走到线索箱「{crate.name}」旁，按交互键（E）开箱", null, 0f);
            yield return WalkAround(CrateStandPoint(crate), 0.3f, crate.transform);
            yield return OpenCrate(crate);
            yield return Check("箱子打开，背包里有了旧信（1005）", () => crate.IsOpened && HasItem(ClueItemId), 3f);

            Vector2 cameFrom = player.Position;
            yield return Step("回到妇人身前，面朝她再按照镜键（R）", null, 0f);
            yield return WalkAround(PickStandPoint(womanPos, cameFrom, zoneRect, woman.transform.position.y), 0.3f,
                woman.transform);
            yield return FaceToward(() => womanPos);
            yield return Input.Press(inputService.Actions.Gameplay.Mirror);
            yield return Check($"结果「{mirrorConfig.TrueFormTitle}」：显示真形名「{trueName}」与真形图；记为已照见；镜缘刻痕可见",
                () => ResultShows(MirrorResultKind.TrueForm) && mirror.LastSubject == woman && mirror.LastResult.YaoId == yaoId
                      && ShowsTrueForm(trueName) && mirror.IsIdentified(yaoId),
                ResultTimeoutSeconds);
            yield return SettleForCapture();
            yield return Snapshot("照妇人·真形");
            yield return CloseResultWithConfirm();

            yield return Step("走出昏暗区（朝出生点方向）", null, 0f);
            yield return WalkAround(ExitPoint(zoneRect, player.Position, spawn), 0.3f, null);
            yield return Check("通灵视熄灭：影子提示全部收起",
                () => !sight.IsActive && sight.ActiveZone == null && sight.ShownCount == 0, 3f);
            yield return Snapshot("走出昏暗区·提示消失");
        }

        // ───────────────────────── 用例 3：自照（V4 V6） ─────────────────────────

        [UnityTest]
        public IEnumerator SelfLook_ShowsBlank()
        {
            yield return EnterMirrorWorld();
            MirrorSubject elder = RequireSubject(ElderName);
            Vector2 elderPos = binder.LogicPositionOf(elder);

            yield return Step("走到长者身前并面朝他（前方有人，自照也不该照到他）", null, 0f);
            yield return WalkAround(elderPos + ElderStandOffset, 0.3f, elder.transform);
            yield return FaceToward(() => elderPos);

            yield return Step("按自照键（Gameplay/MirrorSelf，键盘 V）", null, 0f);
            yield return Input.Press(inputService.Actions.Gameplay.MirrorSelf);
            yield return Check($"结果画面出现：标题「{mirrorConfig.SelfTitle}」，镜面是空镜（没有长者的名字与形象），镜缘刻痕可见",
                () => ResultShows(MirrorResultKind.Self) && mirror.LastSubject == null && ShowsBlankMirror(elder),
                ResultTimeoutSeconds);
            yield return SettleForCapture();
            yield return Snapshot("自照·空镜");

            yield return CloseResultWithConfirm();
        }

        // ───────────────────────── 用例 4：对话中照镜键无效（V11） ─────────────────────────

        [UnityTest]
        public IEnumerator DialogueBlocksMirror()
        {
            yield return EnterMirrorWorld();
            MirrorSubject elderSubject = RequireSubject(ElderName);
            DialogueInteractable elder = FindRequired<DialogueInteractable>(ElderName);
            Vector2 elderPos = binder.LogicPositionOf(elderSubject);

            yield return Step("走到长者身前，面朝他", null, 0f);
            yield return WalkAround(elderPos + ElderStandOffset, 0.3f, elder.transform);
            yield return FaceToward(() => elderPos);
            yield return Check("长者进入交互范围（屏幕下方出现交互提示）", () => dialogueFocus.Current == elder, 3f);

            yield return Step("按交互键（Gameplay/Interact，键盘 E）和长者交谈", null, 0f);
            yield return Input.Press(inputService.Actions.Gameplay.Interact);
            yield return Check("对白面板打开", () => dialogue.IsRunning && ui.Get<DialogueView>() != null, 5f);

            bool resultSeen = false;
            yield return Step("对话进行中按照镜键（R）", null, 0f);
            yield return Input.Press(inputService.Actions.Gameplay.Mirror);
            yield return WatchFor(() => inputPresenter.IsShowing || ResultView() != null, BlockedWatchSeconds,
                () => resultSeen = true);
            yield return Check($"照镜结果画面始终没有出现（观察 {BlockedWatchSeconds:0.#} 秒），这次按键根本没照（没有照镜结果），对白仍在进行",
                () => !resultSeen && mirror.LastResult.Kind == MirrorResultKind.Nothing && mirror.LastSubject == null
                      && dialogue.IsRunning);
            yield return Snapshot("对话中按R·无结果画面");

            yield return RunDialogueToEnd();
            yield return Check("对白结束，世界恢复", () => !dialogue.IsRunning && !worldPause.IsPaused, 5f);

            yield return Step("对话结束后再按照镜键（R）", null, 0f);
            yield return FaceToward(() => elderPos);
            yield return Input.Press(inputService.Actions.Gameplay.Mirror);
            yield return Check($"照镜键恢复：结果画面出现、标题「{mirrorConfig.HumanTitle}」、照到的是长者",
                () => ResultShows(MirrorResultKind.Human) && mirror.LastSubject == elderSubject, ResultTimeoutSeconds);
            yield return SettleForCapture();
            yield return Snapshot("对话结束后·照镜恢复");
            yield return CloseResultWithConfirm();
        }

        // ───────────────────────── 用例 5：三次被击 → 镜碎页 → 重开本场（V8 V10，顺带 V3 / V12 的第二只妖） ─────────────────────────

        [UnityTest]
        public IEnumerator ThreeHits_ShatterAndRestart()
        {
            yield return EnterMirrorWorld();
            MirrorSubject patrol = RequireSubject(PatrolMonsterName);
            SupplyCrate crate = RequireClueCrate();
            string crateKey = crate.Key;
            int patrolYaoId = patrol.YaoId;
            string patrolTrueName = TrueNameOf(patrolYaoId);
            Vector2 spawn = SpawnPoint();
            float baseRange = mirrorConfig.BaseRange;
            float lossPerCrack = mirrorConfig.RangeLossPerCrack;

            // ① 先留下两样「重开后应保留」的进度：开过的箱子、照见过的妖。
            yield return Step($"先走到线索箱「{crate.name}」旁按交互键开箱（重开后箱子应仍是开的）", null, 0f);
            yield return WalkAround(CrateStandPoint(crate), 0.3f, crate.transform);
            yield return OpenCrate(crate);
            yield return Check("箱子打开，背包里有了旧信", () => crate.IsOpened && HasItem(ClueItemId), 3f);

            yield return GoToPatrolLookout();
            yield return Step("面朝巡逻怪按照镜键（R）", null, 0f);
            yield return FaceToward(() => monster.Position);
            yield return Input.Press(inputService.Actions.Gameplay.Mirror);
            yield return Check($"照见巡逻怪的真形「{patrolTrueName}」（它无线索要求，一照就见），记为已照见",
                () => ResultShows(MirrorResultKind.TrueForm) && mirror.LastSubject == patrol
                      && mirror.LastResult.YaoId == patrolYaoId && ShowsTrueForm(patrolTrueName) && mirror.IsIdentified(patrolYaoId),
                ResultTimeoutSeconds);
            yield return SettleForCapture();
            yield return Snapshot("照巡逻怪·真形");
            yield return CloseResultWithConfirm();

            // ② 挨打：不潜行、不伪装，走进近身感知范围让它转敌对，然后站着不动。
            yield return Step("走近巡逻怪，站着不动任它攻击（不潜行、不伪装）", null, 0f);
            yield return ApproachUntilHostile();
            yield return Check("巡逻怪转为敌对；玩家没有潜行、没有伪装",
                () => monster.Mode == MonsterMode.Hostile && !player.IsSneaking && !player.IsDisguised, 5f);
            string questBefore = QuestSignature();

            yield return Check($"第 1 次被击中：镜图标出现第 1 道裂痕、画面四周出现暗角（可见范围 < 1），作用距离 {baseRange:0.#} → {baseRange - lossPerCrack:0.#}",
                () => mirror.HitCracks == 1 && HudCracks() == 1 && ShownVision() < 1f
                      && Mathf.Approximately(ShownVision(), mirror.VisionRadius)
                      && Mathf.Approximately(mirror.EffectiveRange, Mathf.Max(0f, baseRange - lossPerCrack)),
                FirstHitTimeoutSeconds);
            float visionAfterFirst = ShownVision();
            yield return Snapshot("第1道裂痕");

            yield return Check($"第 2 次被击中：第 2 道裂痕，暗角进一步收紧，作用距离 → {baseRange - 2f * lossPerCrack:0.#}",
                () => mirror.HitCracks == 2 && HudCracks() == 2 && ShownVision() < visionAfterFirst
                      && Mathf.Approximately(ShownVision(), mirror.VisionRadius)
                      && Mathf.Approximately(mirror.EffectiveRange, Mathf.Max(0f, baseRange - 2f * lossPerCrack)),
                NextHitTimeoutSeconds);
            yield return Snapshot("第2道裂痕");

            yield return Check($"第 3 次被击中：出现镜碎页，页面上只有「{mirrorConfig.ShatterText}」二字（没有「{LoadGameText}」等其它文字），遭遇停下",
                () => crackPresenter.IsShatterShowing && ShatterPageShowsOnlyText() && !step.IsActive,
                NextHitTimeoutSeconds);
            yield return SettleForCapture();
            yield return Snapshot("镜碎页");

            // ③ 任意键重开：页面出现后有按键保护时间（防止挨打时连按的键把它直接跳过），过了再按确认。
            yield return Step("等按键保护时间过去，按确认键（Enter）", null, 0f);
            yield return new WaitForSecondsRealtime(mirrorConfig.ShatterInputDelay + 0.15f);
            yield return DismissShatter();
            yield return Check("按键生效：镜碎页交回，开始重开本场", () => crackPresenter.IsRestarting || step.IsActive, 3f);
            yield return WaitUntil("重开本场：场景重进、遭遇逻辑重新开始",
                () => !crackPresenter.IsRestarting && step.IsActive && mirror.HitCracks == 0, RestartTimeoutSeconds);

            yield return Check("回到出生点；裂痕清零（镜图标无裂痕、暗角消失）；镜碎页已关",
                () => step.IsActive && mirror.HitCracks == 0 && Vector2.Distance(player.Position, spawn) < 0.6f
                      && HudCracks() == 0 && ShownVision() >= 1f
                      && !crackPresenter.IsShatterShowing && !crackPresenter.IsRestarting
                      && ui.Get<MirrorShatterView>() == null,
                10f);
            yield return Check("进度保留：线索箱仍是开的、背包仍有旧信、巡逻怪仍记为已照见、任务进度不变",
                () =>
                {
                    SupplyCrate fresh = FindClueCrate();
                    return fresh != null && fresh.IsOpened && loot.IsCollected(crateKey) && HasItem(ClueItemId)
                           && mirror.IsIdentified(patrolYaoId) && QuestSignature() == questBefore;
                },
                5f);
            yield return Snapshot("重开后·回到出生点");
        }

        // ───────────────────────── 用例 6：剧情裂痕只缩视野、不致镜碎（V9） ─────────────────────────

        [UnityTest]
        public IEnumerator StoryCrack_ShrinksVisionOnly()
        {
            yield return EnterMirrorWorld();
            float rangeBefore = mirror.EffectiveRange;
            yield return Check("初始：没有任何裂痕，画面没有暗角", () => mirror.HitCracks == 0 && mirror.StoryCracks == 0
                                                             && ShownVision() >= 1f && HudCracks() == 0, 3f);

            // 剧情裂痕本波没有剧情接入（PRD 范围），直调的接口 AddStoryCrack 本身就是被验对象。
            yield return Step("调剧情裂痕接口 MirrorService.AddStoryCrack() 一次", () => mirror.AddStoryCrack());
            yield return Check("画面四周出现暗角（可见范围缩小）、作用距离缩短；镜图标没有裂痕，击中裂痕仍为 0",
                () => mirror.StoryCracks == 1 && ShownVision() >= 0f && ShownVision() < 1f && mirror.EffectiveRange < rangeBefore
                      && mirror.HitCracks == 0 && HudCracks() == 0,
                3f);
            float visionAfterOne = ShownVision();
            yield return Snapshot("剧情裂痕1道·暗角");

            bool shatterSeen = false;
            yield return Step("再调两次（累计 3 道剧情裂痕，数目已等于击中的镜碎线）", () =>
            {
                mirror.AddStoryCrack();
                mirror.AddStoryCrack();
            }, 0f);
            yield return WatchFor(() => crackPresenter.IsShatterShowing || ui.Get<MirrorShatterView>() != null, 1.5f,
                () => shatterSeen = true);
            yield return Check("暗角更重；不出镜碎页，遭遇照常进行",
                () => mirror.StoryCracks == 3 && ShownVision() >= 0f && ShownVision() < visionAfterOne
                      && !shatterSeen && step.IsActive && mirror.HitCracks == 0,
                3f);
            yield return Snapshot("剧情裂痕3道·不镜碎");
        }

        // ───────────────────────── 进场与服务 ─────────────────────────

        /// <summary>标题「开始」进 SampleScene（EnterDemoWorld），再等镜图标、视野遮罩打开且场景里的照镜对象已登记。</summary>
        private IEnumerator EnterMirrorWorld()
        {
            yield return EnterDemoWorld("遭遇逻辑在跑、输入服务与镜模块服务可用", ConnectServices,
                "进世界后容器里取不到镜模块服务（检查 Boot 的 GameBootstrap 是否挂了 MirrorInstaller 并拖了 MirrorConfig），"
                + "或取不到 PlayerModel / MonsterModel / IInputService / LootService / DialogueService / QuestService，后续步骤无法驱动");
            yield return WaitUntil("镜模块就绪：右上角镜图标与视野遮罩已打开、场景里的照镜对象已登记",
                () => ui.Get<MirrorHudView>() != null && ui.Get<MirrorVisionView>() != null && binder.Subjects.Count > 0,
                DemoEnterTimeoutSeconds);
        }

        /// <summary>从根容器取本回放要用的服务；全部取到返回 true（EnterDemoWorld 逐帧调用）。</summary>
        private bool ConnectServices()
        {
            inputService = ResolveService<IInputService>();
            ui = ResolveService<IUIService>();
            worldPause = ResolveService<IWorldPauseService>();
            player = ResolveService<PlayerModel>();
            monster = ResolveService<MonsterModel>();
            step = ResolveService<EncounterStep>();
            mirror = ResolveService<MirrorService>();
            mirrorConfig = ResolveService<MirrorConfig>();
            binder = ResolveService<MirrorSceneBinder>();
            inputPresenter = ResolveService<MirrorInputPresenter>();
            crackPresenter = ResolveService<MirrorCrackPresenter>();
            sight = ResolveService<SpiritSightPresenter>();
            loot = ResolveService<LootService>();
            lootConfig = ResolveService<LootConfig>();
            crateFocus = ResolveService<SupplyCrateFocus>();
            dialogue = ResolveService<DialogueService>();
            dialogueRules = ResolveService<DialogueRules>();
            dialogueFocus = ResolveService<DialogueInteractionFocus>();
            quest = ResolveService<QuestService>();
            // MirrorConfig / LootConfig 是 ScriptableObject，判空只用 != null。
            return inputService != null && inputService.Actions != null && ui != null && worldPause != null
                   && player != null && monster != null && step != null
                   && mirror != null && mirrorConfig != null && binder != null
                   && inputPresenter != null && crackPresenter != null && sight != null
                   && loot != null && lootConfig != null && crateFocus != null
                   && dialogue != null && dialogueRules != null && dialogueFocus != null && quest != null;
        }

        /// <summary>按物体名取照镜标记（挂在物体本身或其子物体上）。找不到属于前置条件不成立（SampleScene 布置未落地），直接中断。</summary>
        private static MirrorSubject RequireSubject(string objectName)
        {
            GameObject found = GameObject.Find(objectName);
            if (found == null)
            {
                Assert.Fail($"场上找不到「{objectName}」：SampleScene 的镜 demo 布置还没落地？（PRP/mirror-core T9）");
                return null;
            }

            MirrorSubject subject = found.GetComponentInChildren<MirrorSubject>(true);
            if (subject == null)
            {
                Assert.Fail($"「{objectName}」上没有 MirrorSubject 标记（PRP/mirror-core T9）");
                return null;
            }

            return subject;
        }

        private static SpiritSightZone RequireZone(string objectName)
        {
            GameObject found = GameObject.Find(objectName);
            SpiritSightZone zone = found == null ? null : found.GetComponentInChildren<SpiritSightZone>(true);
            if (zone == null)
            {
                Assert.Fail($"场上找不到挂 SpiritSightZone 的「{objectName}」（PRP/mirror-core T9）");
                return null;
            }

            return zone;
        }

        /// <summary>昏暗区在逻辑平面上的矩形（与 MirrorSceneBinder.FindActiveZone 同一换算）。</summary>
        private Rect RequireZoneRect(SpiritSightZone zone)
        {
            if (!zone.TryGetWorldCorners(out Vector3 min, out Vector3 max))
            {
                Assert.Fail($"「{zone.name}」没有 BoxCollider，算不出昏暗区范围");
                return default;
            }

            Vector2 a = binder.ToLogicPosition(min);
            Vector2 b = binder.ToLogicPosition(max);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>场上装着线索（ItemId == 1005）的物资箱；按组件属性找，不按名字。</summary>
        private static SupplyCrate FindClueCrate()
        {
            SupplyCrate[] crates = UnityEngine.Object.FindObjectsOfType<SupplyCrate>();
            for (int i = 0; i < crates.Length; i++)
            {
                if (crates[i].ItemId == ClueItemId)
                {
                    return crates[i];
                }
            }

            return null;
        }

        private static SupplyCrate RequireClueCrate()
        {
            SupplyCrate crate = FindClueCrate();
            if (crate == null)
            {
                Assert.Fail($"场上没有 ItemId = {ClueItemId} 的物资箱（线索箱，PRP/mirror-core T9）");
            }

            return crate;
        }

        private Vector2 SpawnPoint()
        {
            EncounterSceneView view = UnityEngine.Object.FindObjectOfType<EncounterSceneView>();
            return view == null ? DemoSpawnPoint : view.PlayerStart;
        }

        /// <summary>
        /// 妖物表里的真形名。<c>MirrorService.TryGetYao</c> 的出参类型 cfg.yao.Yao 继承 Luban.Runtime 的 BeanBase，
        /// 本程序集不引用 Luban.Runtime（写出它的成员就是 CS0012），所以走反射取（同 ExplorationShowcase 取 tbitem 行）。取不到返回 null。
        /// </summary>
        private string TrueNameOf(int yaoId)
        {
            try
            {
                MethodInfo method = typeof(MirrorService).GetMethod("TryGetYao");
                object[] args = { yaoId, null };
                if (method == null || !(bool)method.Invoke(mirror, args) || args[1] == null)
                {
                    return null;
                }

                FieldInfo field = args[1].GetType().GetField("TrueName");
                return field == null ? null : field.GetValue(args[1]) as string;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[Mirror] 读妖物表 {yaoId} 的真形名失败：{e.GetType().Name}：{e.Message}");
                return null;
            }
        }

        private bool HasItem(int itemId)
        {
            return loot.Items.TryGetValue(itemId, out int count) && count > 0;
        }

        /// <summary>进行中任务的签名：按 id 排序的「id:状态:目标序号:计数」+ 追踪 id（同 SessionShowcase）。</summary>
        private string QuestSignature()
        {
            var parts = new List<string>();
            IReadOnlyList<QuestProgress> list = quest.InProgress;
            for (int i = 0; i < list.Count; i++)
            {
                QuestProgress p = list[i];
                parts.Add($"{p.Id}:{p.State}:{p.ObjectiveIndex}:{p.Count}");
            }

            parts.Sort(StringComparer.Ordinal);
            return quest.TrackedId + "|" + string.Join(" ", parts);
        }

        // ───────────────────────── 结果画面 / 镜图标 / 暗角 / 镜碎页查询 ─────────────────────────

        private MirrorResultView ResultView()
        {
            return ui.Get<MirrorResultView>();
        }

        /// <summary>结果画面正在显示的内容（<see cref="MirrorResultView.Current"/>）；没在显示时为 null。</summary>
        private MirrorResultInfo ShownResult()
        {
            MirrorResultView view = ResultView();
            return inputPresenter.IsShowing && view != null ? view.Current : null;
        }

        /// <summary>
        /// 结果画面在显示，内容种类与服务记下的最近结果（<see cref="MirrorService.LastResult"/>）都是 <paramref name="kind"/>，
        /// 标题确实画在屏幕上，镜缘刻痕在内容里且画在屏幕上（V6）。
        /// </summary>
        private bool ResultShows(MirrorResultKind kind)
        {
            MirrorResultInfo info = ShownResult();
            return info != null && info.Kind == kind && mirror.LastResult.Kind == kind
                   && !string.IsNullOrEmpty(info.Title) && TextsContain(info.Title) && EngravingShown(info);
        }

        private bool ResultClosed()
        {
            return !inputPresenter.IsShowing && ResultView() == null;
        }

        private bool WorldPaused()
        {
            return worldPause.IsPaused && Mathf.Approximately(Time.timeScale, 0f);
        }

        /// <summary>刻痕文案第一行在内容的刻痕里、也画在屏幕上。刻痕没配置也算失败（V6 要求每个结果画面都有）。</summary>
        private bool EngravingShown(MirrorResultInfo info)
        {
            string first = FirstEngravingLine();
            return first != null && info.Engraving.Contains(first) && TextsContain(first);
        }

        private string FirstEngravingLine()
        {
            IReadOnlyList<string> lines = mirrorConfig.Engraving;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!string.IsNullOrEmpty(lines[i]))
                {
                    return lines[i];
                }
            }

            return null;
        }

        /// <summary>结果画面上某段可见文字含 <paramref name="value"/>（扫面板下全部文字组件，不按子物体名）。</summary>
        private bool TextsContain(string value)
        {
            List<string> texts = VisibleTexts(ResultView());
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i].Contains(value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>照人 / 照物：内容里的名字与形象就是标记自带的，名字也确实画在屏幕上（标记没填名字时不查屏幕）。</summary>
        private bool ShowsSubjectIdentity(MirrorSubject subject)
        {
            MirrorResultInfo info = ShownResult();
            string expectedName = subject.DisplayName ?? string.Empty;
            return info != null && info.Name == expectedName && info.Image == subject.Portrait
                   && (expectedName.Length == 0 || TextsContain(expectedName));
        }

        /// <summary>自照：空镜面（内容不带图、不带名字），前方那人的名字没出现在屏幕上（V4）。</summary>
        private bool ShowsBlankMirror(MirrorSubject inFront)
        {
            MirrorResultInfo info = ShownResult();
            string frontName = inFront.DisplayName;
            return info != null && info.BlankMirror && info.Image == null && string.IsNullOrEmpty(info.Name)
                   && (string.IsNullOrEmpty(frontName) || !TextsContain(frontName));
        }

        /// <summary>模糊轮廓：压暗的真形图、不带名字；真形名既不在内容里、也没画在屏幕上（V2）。</summary>
        private bool ShowsBlurryWithoutName(string trueName)
        {
            MirrorResultInfo info = ShownResult();
            return info != null && info.Blurred && info.Image != null && string.IsNullOrEmpty(info.Name)
                   && !string.IsNullOrEmpty(trueName) && !info.Title.Contains(trueName) && !info.Body.Contains(trueName)
                   && !TextsContain(trueName);
        }

        /// <summary>真形：内容带真形名与真形图（不压暗），真形名画在屏幕上（V3）。</summary>
        private bool ShowsTrueForm(string trueName)
        {
            MirrorResultInfo info = ShownResult();
            return info != null && !info.Blurred && info.Image != null
                   && !string.IsNullOrEmpty(trueName) && info.Name == trueName && TextsContain(trueName);
        }

        /// <summary>镜图标上显示的裂痕级数（<see cref="MirrorHudView.ShownCracks"/>）；镜图标没打开返回 -1。</summary>
        private int HudCracks()
        {
            MirrorHudView hud = ui.Get<MirrorHudView>();
            return hud == null ? -1 : hud.ShownCracks;
        }

        /// <summary>
        /// 暗角对应的可见范围（<see cref="MirrorVisionView.ShownRadius"/>：&lt; 1 即画面四周有暗角、越小越重，≥ 1 不遮）；
        /// 遮罩没打开返回 -1（所以判「有暗角」要连 ≥ 0 一起判）。
        /// </summary>
        private float ShownVision()
        {
            MirrorVisionView vision = ui.Get<MirrorVisionView>();
            return vision == null ? -1f : vision.ShownRadius;
        }

        /// <summary>镜碎页上的可见文字只有一段、就是配置的「镜碎」，且不含「读档」。</summary>
        private bool ShatterPageShowsOnlyText()
        {
            MirrorShatterView view = ui.Get<MirrorShatterView>();
            if (view == null)
            {
                return false;
            }

            List<string> texts = VisibleTexts(view);
            return texts.Count == 1 && texts[0] == mirrorConfig.ShatterText && !texts[0].Contains(LoadGameText);
        }

        /// <summary><paramref name="root"/> 下所有激活且有内容的文字（TMP 与旧版 UI.Text），去首尾空白。</summary>
        private static List<string> VisibleTexts(Component root)
        {
            var texts = new List<string>();
            if (root == null)
            {
                return texts;
            }

            TMP_Text[] tmps = root.GetComponentsInChildren<TMP_Text>(false);
            for (int i = 0; i < tmps.Length; i++)
            {
                if (tmps[i].enabled && !string.IsNullOrWhiteSpace(tmps[i].text))
                {
                    texts.Add(tmps[i].text.Trim());
                }
            }

            UnityEngine.UI.Text[] legacy = root.GetComponentsInChildren<UnityEngine.UI.Text>(false);
            for (int i = 0; i < legacy.Length; i++)
            {
                if (legacy[i].enabled && !string.IsNullOrWhiteSpace(legacy[i].text))
                {
                    texts.Add(legacy[i].text.Trim());
                }
            }

            return texts;
        }

        // ───────────────────────── 通灵视查询 ─────────────────────────

        /// <summary>通灵视半径内、激活的妖的数目（与 SpiritSightPresenter 同一判定：只数妖，不数人 / 物）。</summary>
        private int YaoWithinSight()
        {
            float radius = mirrorConfig.SightRadius;
            Vector2 origin = player.Position;
            IReadOnlyList<MirrorSubject> subjects = binder.Subjects;
            int count = 0;
            for (int i = 0; i < subjects.Count; i++)
            {
                MirrorSubject subject = subjects[i];
                if (subject == null || !subject.isActiveAndEnabled || subject.Kind != MirrorSubjectKind.Yao)
                {
                    continue;
                }

                if ((binder.LogicPositionOf(subject) - origin).sqrMagnitude <= radius * radius)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>影子提示池根（<see cref="SpiritSightPresenter.HintRoot"/>）下没有任何文字组件（提示不含名字，V7）。池根还没建出来按失败算。</summary>
        private bool HintsCarryNoText()
        {
            Transform root = sight.HintRoot;
            return root != null
                   && root.GetComponentsInChildren<TMP_Text>(true).Length == 0
                   && root.GetComponentsInChildren<TextMesh>(true).Length == 0;
        }

        // ───────────────────────── 输入驱动 ─────────────────────────

        /// <summary>
        /// 对准朝向：玩家朝向随移动方向（PlayerRules），所以朝目标轻推摇杆（0.3）至少 0.06 秒、直到朝向夹角 &lt; 6° 或 0.4 秒，
        /// 松杆等停稳。贴得太近（&lt; 0.3 米）先退半步，免得方向判不准（同 FaceMonster）。目标取委托，巡逻怪这类会动的目标逐帧重算。
        /// </summary>
        private IEnumerator FaceToward(Func<Vector2> target)
        {
            Vector2 gap = target() - player.Position;
            if (gap.magnitude < 0.3f)
            {
                Vector2 away = gap.magnitude > 0.01f ? -gap.normalized : -player.Facing;
                float backUntil = Time.realtimeSinceStartup + 0.12f;
                while (Time.realtimeSinceStartup < backUntil)
                {
                    Input.SetStick(away);
                    yield return null;
                }
            }

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < FaceMaxSeconds)
            {
                Vector2 toTarget = target() - player.Position;
                if (toTarget.magnitude > 0.01f)
                {
                    Input.SetStick(toTarget.normalized * FaceStick);
                }

                yield return null;
                bool pushedLongEnough = Time.realtimeSinceStartup - start >= FaceMinSeconds;
                if (pushedLongEnough && Vector2.Angle(player.Facing, target() - player.Position) < FaceToleranceDegrees)
                {
                    break;
                }
            }

            Input.ReleaseStick();
            yield return WaitPlayerStable();
        }

        /// <summary>按确认键关结果画面：界面刚打开时控制器可能还没接管按键，没关就隔一会儿再按（最多 3 次）；结果画面到时也会自己关。</summary>
        private IEnumerator CloseResultWithConfirm()
        {
            yield return Step("按确认键（Enter）关闭结果画面", null, 0f);
            for (int attempt = 0; attempt < 3 && !ResultClosed(); attempt++)
            {
                yield return Input.Press(inputService.Actions.UI.Submit);
                float until = Time.realtimeSinceStartup + 0.8f;
                while (!ResultClosed() && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
            }

            yield return Check("结果画面关闭，世界恢复走动", () => ResultClosed() && !worldPause.IsPaused, ResultTimeoutSeconds);
        }

        /// <summary>等箱子成为交互焦点再按交互键；焦点不在它身上时不按（此时按 E 可能拉起旁边 NPC 的对白），由后续检查点记失败。</summary>
        private IEnumerator OpenCrate(SupplyCrate crate)
        {
            yield return WaitUntil("箱子成为交互焦点（屏幕下方出现开箱提示）", () => crateFocus.Current == crate, 3f);
            if (crateFocus.Current != crate)
            {
                yield break;
            }

            yield return Input.Press(inputService.Actions.Gameplay.Interact);
        }

        /// <summary>朝巡逻怪推摇杆，进到近身感知范围（它立刻转敌对）或转敌对后松杆站住。</summary>
        private IEnumerator ApproachUntilHostile()
        {
            float deadline = Time.realtimeSinceStartup + ProvokeTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline && monster.Mode != MonsterMode.Hostile && player.Health > 0)
            {
                Vector2 toMonster = monster.Position - player.Position;
                if (toMonster.magnitude > ProvokeStopDistance)
                {
                    Input.SetStick(toMonster.normalized);
                }
                else
                {
                    Input.ReleaseStick();
                }

                yield return null;
            }

            Input.ReleaseStick();
            yield return null;
        }

        /// <summary>
        /// 按确认键交回镜碎页，没交回就隔 1.5 秒再按（最多 3 次）。镜碎期间 Gameplay 图是关的，所以按的是 UI/Submit（Enter）。
        /// 「已交回」见 <see cref="ShatterDismissed"/>；镜碎页没出现过就一次都不按。
        /// </summary>
        private IEnumerator DismissShatter()
        {
            for (int attempt = 0; attempt < 3 && !ShatterDismissed(); attempt++)
            {
                yield return Input.Press(inputService.Actions.UI.Submit);
                float until = Time.realtimeSinceStartup + 1.5f;
                while (!ShatterDismissed() && Time.realtimeSinceStartup < until)
                {
                    yield return null;
                }
            }
        }

        /// <summary>镜碎页已交回：正在重进遭遇（<see cref="MirrorCrackPresenter.IsRestarting"/>）、或遭遇已重新开始、或镜碎页已不在。</summary>
        private bool ShatterDismissed()
        {
            return crackPresenter.IsRestarting || step.IsActive || ui.Get<MirrorShatterView>() == null;
        }

        /// <summary>在 <paramref name="seconds"/> 真实秒内逐帧看 <paramref name="happened"/>，一旦成立就回调 <paramref name="onSeen"/> 并返回。求值抛异常按没发生算。</summary>
        private static IEnumerator WatchFor(Func<bool> happened, float seconds, Action onSeen)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                bool seen;
                try
                {
                    seen = happened();
                }
                catch (Exception)
                {
                    seen = false;
                }

                if (seen)
                {
                    onSeen();
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>截图前等一小会儿，让面板的缩放过渡走完（真实时间）。</summary>
        private static IEnumerator SettleForCapture()
        {
            yield return new WaitForSecondsRealtime(UiSettleSeconds);
        }

        // ───────────────────────── 对白推进（照 SessionShowcase） ─────────────────────────

        /// <summary>一路推进到对白结束：打字中 / 等待推进 → 点 TapArea；出选项 → 点第一个激活选项。</summary>
        private IEnumerator RunDialogueToEnd()
        {
            yield return Step("一路点对白区推进，出选项选第一项，直到对白结束", null, 0f);
            float deadline = Time.realtimeSinceStartup + DialogueTimeoutSeconds;
            while (dialogue.IsRunning && Time.realtimeSinceStartup < deadline)
            {
                try
                {
                    DialogueSaveData.Phase phase = dialogueRules.Phase;
                    if (phase == DialogueSaveData.Phase.AwaitChoice)
                    {
                        List<Button> choices = ActiveChoices();
                        if (choices.Count > 0)
                        {
                            choices[0].onClick.Invoke();
                        }
                    }
                    else if (phase == DialogueSaveData.Phase.Typing || phase == DialogueSaveData.Phase.AwaitAdvance)
                    {
                        Button tap = FindInDialogue<Button>("TapArea");
                        if (tap != null)
                        {
                            tap.onClick.Invoke();
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{ShowcaseOptions.Prefix}[Mirror] 推进对白时出错：{e.GetType().Name}：{e.Message}");
                }

                yield return new WaitForSecondsRealtime(0.15f);
            }
        }

        private T FindInDialogue<T>(string objectName) where T : Component
        {
            DialogueView view = ui.Get<DialogueView>();
            return view == null ? null : FindDeep<T>(view.transform, objectName);
        }

        /// <summary>ChoiceRoot 下当前激活的选项按钮（排除隐藏模板本身）。</summary>
        private List<Button> ActiveChoices()
        {
            var result = new List<Button>();
            Transform root = FindInDialogue<Transform>("ChoiceRoot");
            if (root == null)
            {
                return result;
            }

            Button[] buttons = root.GetComponentsInChildren<Button>(false);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].name != "ChoiceTemplate")
                {
                    result.Add(buttons[i]);
                }
            }

            return result;
        }

        // ───────────────────────── 站位与绕行 ─────────────────────────

        /// <summary>按规划的路线逐段走（WalkTo）；最后一段用 <paramref name="stopDistance"/>，途经点 0.4 米算到。</summary>
        private IEnumerator WalkAround(Vector2 target, float stopDistance, Transform exclude)
        {
            List<Vector2> route = PlanRoute(player.Position, target, exclude);
            for (int i = 0; i < route.Count; i++)
            {
                bool last = i == route.Count - 1;
                yield return WalkTo(route[i], last ? stopDistance : WaypointStopDistance, LegTimeoutSeconds);
            }
        }

        /// <summary>
        /// 小绕行规划（单趟，不递归）：直线穿过村口演出触发区（外扩 0.6）就改走 <see cref="ShowcaseScenario.RouteToPatrol"/> 的北侧两点；
        /// 每一段再看沿途的 NPC / 箱子 / 静态照镜对象 / 小摆件（路障锥、长凳，见 <see cref="AddPropObstacles"/>），
        /// 离路线中心小于 1.2 米的，在它外侧 1.5 米插一个绕行点。
        /// 目标自身（<paramref name="exclude"/>）不算障碍。返回不含起点、以 <paramref name="to"/> 结尾的途经点。
        /// </summary>
        private List<Vector2> PlanRoute(Vector2 from, Vector2 to, Transform exclude)
        {
            var coarse = new List<Vector2> { from };
            if (SegmentTouches(from, to, TriggerBuffer()))
            {
                AddTriggerDetour(coarse, from, to);
            }

            coarse.Add(to);
            List<Vector2> obstacles = CollectObstacles(exclude);
            AddPropObstacles(obstacles, coarse, exclude);
            var route = new List<Vector2>();
            for (int i = 1; i < coarse.Count; i++)
            {
                AddObstacleDetours(route, coarse[i - 1], coarse[i], obstacles);
                route.Add(coarse[i]);
            }

            return route;
        }

        private static Rect TriggerBuffer()
        {
            return Inflate(VillageEntranceTriggerArea, TriggerMargin);
        }

        private static Rect Inflate(Rect rect, float margin)
        {
            return Rect.MinMaxRect(rect.xMin - margin, rect.yMin - margin, rect.xMax + margin, rect.yMax + margin);
        }

        /// <summary>线段是否碰到矩形（每 0.2 米采样一点）。</summary>
        private static bool SegmentTouches(Vector2 a, Vector2 b, Rect rect)
        {
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 0.2f));
            for (int i = 0; i <= samples; i++)
            {
                if (rect.Contains(Vector2.Lerp(a, b, i / (float)samples)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>从北侧绕开村口触发区：(3.5,5) → (8.5,7.2) → 触发区东沿外 1 米（同纬度），按行进方向取需要的几点。</summary>
        private static void AddTriggerDetour(List<Vector2> route, Vector2 from, Vector2 to)
        {
            Vector2 west = RouteToPatrol[0];
            Vector2 gate = RouteToPatrol[1];
            Vector2 east = new Vector2(VillageEntranceTriggerArea.xMax + 1f, gate.y);
            if (to.x >= from.x)
            {
                if (from.x < west.x) route.Add(west);
                route.Add(gate);
                if (to.x > east.x) route.Add(east);
            }
            else
            {
                if (from.x > east.x) route.Add(east);
                route.Add(gate);
                if (to.x < west.x) route.Add(west);
            }
        }

        /// <summary>场上的静态障碍（NPC、物资箱、不跟随巡逻怪的照镜对象）的逻辑位置，去重；目标自身及其父子物体除外。</summary>
        private List<Vector2> CollectObstacles(Transform exclude)
        {
            var points = new List<Vector2>();
            DialogueInteractable[] talkers = UnityEngine.Object.FindObjectsOfType<DialogueInteractable>();
            for (int i = 0; i < talkers.Length; i++)
            {
                AddObstacle(points, talkers[i].transform, exclude);
            }

            SupplyCrate[] crates = UnityEngine.Object.FindObjectsOfType<SupplyCrate>();
            for (int i = 0; i < crates.Length; i++)
            {
                AddObstacle(points, crates[i].transform, exclude);
            }

            IReadOnlyList<MirrorSubject> subjects = binder.Subjects;
            for (int i = 0; i < subjects.Count; i++)
            {
                MirrorSubject subject = subjects[i];
                if (subject != null && subject.isActiveAndEnabled && !subject.FollowsMonster)
                {
                    AddObstacle(points, subject.transform, exclude);
                }
            }

            return points;
        }

        private void AddObstacle(List<Vector2> points, Transform candidate, Transform exclude)
        {
            if (candidate == null)
            {
                return;
            }

            if (exclude != null && (candidate == exclude || candidate.IsChildOf(exclude) || exclude.IsChildOf(candidate)))
            {
                return;
            }

            AddObstaclePoint(points, binder.ToLogicPosition(candidate.position));
        }

        /// <summary>按逻辑位置加一个障碍点；离已有点不到 0.3 米的算同一个，不重复加。</summary>
        private static void AddObstaclePoint(List<Vector2> points, Vector2 point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if ((points[i] - point).sqrMagnitude < 0.09f)
                {
                    return;
                }
            }

            points.Add(point);
        }

        /// <summary>
        /// 路线途经范围（外扩 2 米）里的小摆件：与玩家挡墙胶囊同一高度带（离地 0.35～1.5 米）的非触发碰撞体，
        /// 水平尺寸都不超过 <see cref="PropMaxSize"/> 的（路障锥、长凳的座板与腿）按包围盒中心记为障碍点；
        /// 墙、塔、地面这类大块不记（点状绕行处理不了，场景路线本身已避开）。玩家、巡逻怪、目标自身不算。
        /// 2026-09-28 次跑时直线撞上路障锥 Cone_1 正中、推杆 12 秒原地不动。遭遇场景是 XY 平面时不查。
        /// </summary>
        private void AddPropObstacles(List<Vector2> points, List<Vector2> coarse, Transform exclude)
        {
            if (binder.ToLogicPosition(Vector3.up) != Vector2.zero || coarse.Count == 0)
            {
                return;
            }

            EncounterSceneView view = UnityEngine.Object.FindObjectOfType<EncounterSceneView>();
            if (view == null)
            {
                return;
            }

            Vector2 min = coarse[0];
            Vector2 max = coarse[0];
            for (int i = 1; i < coarse.Count; i++)
            {
                min = Vector2.Min(min, coarse[i]);
                max = Vector2.Max(max, coarse[i]);
            }

            min -= Vector2.one * PropSearchMargin;
            max += Vector2.one * PropSearchMargin;
            float groundY = view.PlayerScenePosition.y;
            var center = new Vector3((min.x + max.x) * 0.5f, groundY + (StandClearBottom + StandClearTop) * 0.5f,
                (min.y + max.y) * 0.5f);
            var half = new Vector3((max.x - min.x) * 0.5f, (StandClearTop - StandClearBottom) * 0.5f, (max.y - min.y) * 0.5f);
            Collider[] hits = Physics.OverlapBox(center, half, Quaternion.identity, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hit = hits[i].transform;
                if (IsSameBody(hit, view.PlayerBody) || IsSameBody(hit, view.MonsterBody) || IsSameBody(hit, exclude))
                {
                    continue;
                }

                Bounds bounds = hits[i].bounds;
                if (bounds.size.x > PropMaxSize || bounds.size.z > PropMaxSize)
                {
                    continue;
                }

                AddObstaclePoint(points, binder.ToLogicPosition(bounds.center));
            }
        }

        /// <summary>给 a → b 这一段按沿途障碍插绕行点（按离 a 的远近排序）；绕行点落进触发区缓冲带时改绕另一侧。</summary>
        private static void AddObstacleDetours(List<Vector2> route, Vector2 a, Vector2 b, List<Vector2> obstacles)
        {
            Vector2 ab = b - a;
            float length = ab.magnitude;
            if (length < 0.01f)
            {
                return;
            }

            Vector2 dir = ab / length;
            Rect trigger = TriggerBuffer();
            var detours = new List<KeyValuePair<float, Vector2>>();
            for (int i = 0; i < obstacles.Count; i++)
            {
                Vector2 obstacle = obstacles[i];
                if (Vector2.Distance(obstacle, b) < ObstacleNearSkip)
                {
                    continue;
                }

                // 在起点身后（t ≤ 0）或终点之外的不挡路；起点旁但在前方的照绕（刚开完的箱子常在这个位置）。
                float t = Vector2.Dot(obstacle - a, dir);
                if (t <= 0f || t >= length)
                {
                    continue;
                }

                Vector2 away = a + dir * t - obstacle;
                if (away.magnitude >= ObstacleClearance)
                {
                    continue;
                }

                Vector2 normal = away.sqrMagnitude > 1e-4f ? away.normalized : new Vector2(-dir.y, dir.x);
                Vector2 detour = obstacle + normal * ObstacleDetourOffset;
                if (trigger.Contains(detour))
                {
                    detour = obstacle - normal * ObstacleDetourOffset;
                }

                detours.Add(new KeyValuePair<float, Vector2>(t, detour));
            }

            detours.Sort((x, y) => x.Key.CompareTo(y.Key));
            for (int i = 0; i < detours.Count; i++)
            {
                route.Add(detours[i].Value);
            }
        }

        /// <summary>
        /// 照妖的站位：从「来的方向」起，依次试 2 / 1.6 / 1.3 米、偏转 0 / ±45 / ±90 / ±135 / 180 度，
        /// 取第一个落在昏暗区内侧（离边沿 ≥ 0.4 米）、不在触发区缓冲带里、且脚下是空地（<see cref="StandPointClear"/>）的点；
        /// 都不行就取来向 2 米处。<paramref name="groundY"/> 是妖脚下的场景高度，空地检查从这里立胶囊。
        /// </summary>
        private Vector2 PickStandPoint(Vector2 subject, Vector2 approachFrom, Rect zone, float groundY)
        {
            Vector2 baseDir = approachFrom - subject;
            baseDir = baseDir.sqrMagnitude > 1e-4f ? baseDir.normalized : Vector2.left;
            Rect inner = Inflate(zone, -ZoneInset);
            Rect trigger = TriggerBuffer();
            for (int d = 0; d < StandDistances.Length; d++)
            {
                for (int a = 0; a < StandAngles.Length; a++)
                {
                    Vector2 point = subject + Rotate(baseDir, StandAngles[a]) * StandDistances[d];
                    if (inner.Contains(point) && !trigger.Contains(point) && StandPointClear(point, groundY))
                    {
                        return point;
                    }
                }
            }

            return subject + baseDir * StandDistances[0];
        }

        /// <summary>
        /// 站位是不是空地：逻辑点按 XZ 平面放回场景（遭遇场景是 XY 平面时不查，直接算空地），立一根胶囊查与非触发碰撞体的重叠；
        /// 玩家与巡逻怪自己的碰撞体不算（<see cref="EncounterSceneView.PlayerBody"/> / <see cref="EncounterSceneView.MonsterBody"/>）。
        /// 绕行规划只认 NPC / 箱子，长凳、墙这类摆件要靠这里避开。
        /// </summary>
        private bool StandPointClear(Vector2 logic, float groundY)
        {
            if (binder.ToLogicPosition(Vector3.up) != Vector2.zero)
            {
                return true;
            }

            Vector3 foot = new Vector3(logic.x, groundY, logic.y);
            Vector3 low = foot + Vector3.up * (StandClearBottom + StandClearRadius);
            Vector3 high = foot + Vector3.up * (StandClearTop - StandClearRadius);
            Collider[] hits = Physics.OverlapCapsule(low, high, StandClearRadius, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            EncounterSceneView view = UnityEngine.Object.FindObjectOfType<EncounterSceneView>();
            Transform playerBody = view == null ? null : view.PlayerBody;
            Transform monsterBody = view == null ? null : view.MonsterBody;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hit = hits[i].transform;
                if (IsSameBody(hit, playerBody) || IsSameBody(hit, monsterBody))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>碰撞体所在物体与角色身体是同一个、或互为父子（碰撞体常挂在根上，身体是子物体）。</summary>
        private static bool IsSameBody(Transform hit, Transform body)
        {
            return body != null && (hit == body || hit.IsChildOf(body) || body.IsChildOf(hit));
        }

        /// <summary>从区内一点朝 <paramref name="towards"/>（出生点，保证在区外）走到越过昏暗区边沿 0.8 米的位置。</summary>
        private static Vector2 ExitPoint(Rect zone, Vector2 inside, Vector2 towards)
        {
            Rect outer = Inflate(zone, ZoneExitMargin);
            Vector2 dir = towards - inside;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.left;
            Vector2 point = inside;
            for (int i = 0; i < 400 && outer.Contains(point); i++)
            {
                point += dir * 0.2f;
            }

            return point;
        }

        /// <summary>
        /// 开箱站位：箱子四周 0.9 米（不超过交互半径的 0.6 倍）的候选（来向、南、西、东、北），
        /// 优先取离所有 NPC 都 ≥ 2.6 米（不落进对白交互范围）且离玩家最近的；都不满足就取离 NPC 最远的。
        /// </summary>
        private Vector2 CrateStandPoint(SupplyCrate crate)
        {
            Vector2 center = binder.ToLogicPosition(crate.Position);
            float offset = Mathf.Min(CrateStandOffset, lootConfig.CrateInteractRadius * 0.6f);
            Vector2 approach = player.Position - center;
            approach = approach.sqrMagnitude > 1e-4f ? approach.normalized : Vector2.down;
            Vector2[] directions = { approach, Vector2.down, Vector2.left, Vector2.right, Vector2.up };
            Rect trigger = TriggerBuffer();
            Vector2 best = center + approach * offset;
            float bestScore = float.MinValue;
            for (int i = 0; i < directions.Length; i++)
            {
                Vector2 point = center + directions[i] * offset;
                if (trigger.Contains(point))
                {
                    continue;
                }

                float talker = NearestTalkerDistance(point);
                float score = talker >= TalkerClearance ? 1000f - Vector2.Distance(player.Position, point) : talker;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = point;
                }
            }

            return best;
        }

        private float NearestTalkerDistance(Vector2 point)
        {
            float nearest = float.MaxValue;
            DialogueInteractable[] talkers = UnityEngine.Object.FindObjectsOfType<DialogueInteractable>();
            for (int i = 0; i < talkers.Length; i++)
            {
                nearest = Mathf.Min(nearest, Vector2.Distance(point, binder.ToLogicPosition(talkers[i].transform.position)));
            }

            return nearest;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
