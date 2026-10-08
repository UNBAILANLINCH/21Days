// 职责：回合制战斗界面（PRP D7，07 画面表现 + 07_回合制作战文档.md 的「界面布局」「BOSS状态显示」两张图）——
//   顶部可用道具格、底部三招式格、右侧怒气槽、招式栏左上方玩家状态（角标 = 剩余回合 / 次数）与剩余回合、
//   BOSS 头顶血条（上方状态字、下方醉酒条）、玩家头顶血条与晕眩标记、伤害 / 回血飘字、屏幕中央闪现提示、悬停详情框。
//   只显示与抛事件（点了哪个招式 / 道具 / 结束回合），不读输入设备、不注入服务；怎么判亮暗、写什么字全在 BattleHudRules。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：现有面板（背包 / 任务 / 暂停）都是菜单式列表，没有跟随世界坐标的头顶条、飘字与中央闪现。
//   2. 扩展不行：战斗界面的元素与任何现有面板都不重合，塞进去名实不符。
// Esc：CloseOnCancel = false → UICancelRouter 判 Blocked，既不关本面板，也不抛「没东西可关」给暂停菜单（PRP D7）。
// 头顶条锚在角色**站立时**的头顶（BattleActor.StandingHeadPosition），每帧投到屏幕上（LateUpdate 两次 WorldToScreenPoint，无分配；
//   震屏时跟着画面一起抖）。不跟冲刺 / 后仰 / 跳 / 醉晃 / 倒地走：倒地时头顶锚点会横甩出去，BOSS 的条曾被带到屏幕右缘截断。
//   冲刺也不跟：一来一回只有半秒多，跟着走只会让条横飞一趟；而且冲到对手身前那一刻，攻击方的条会正好压在对手的条上，
//   挡住此刻最该看清的对手掉血。条留在原位，人回来时自然对上。飘字同样从站立头顶起飘。
// 胜负大字（ShowResultAsync）与「跳过回合」等中央提示是同一条深色横条 + 白字，区别只是不淡出、留到落幕收场。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Simulation;
using Game.Core.UI;
using Game.TurnBased;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Battle
{
    /// <summary>
    /// 战斗界面。预制体 <c>Assets/_Project/Prefabs/UI/BattleView.prefab</c>，Addressables 地址 <c>BattleView</c>（= 类名）。
    /// 结构由菜单 <c>21Days/战斗/重建战斗白盒</c>（Game.Editor.Battle.BattleWhiteboxBuilder）生成。
    /// </summary>
    public sealed class BattleView : UIView
    {
        [Header("左上回合提示")]
        [SerializeField] private TMP_Text turnLabel;

        [Header("顶部可用道具（07 布局图「可用道具」）")]
        [SerializeField] private RectTransform itemRow;
        [Tooltip("道具格模板（运行时隐藏，按需复制）：带 Button 的 BattleSlotWidget。")]
        [SerializeField] private BattleSlotWidget itemTemplate;
        [Tooltip("一件可显示的道具都没有时的提示。")]
        [SerializeField] private GameObject itemEmpty;

        [Header("底部招式栏（07 布局图「招式1 / 2 / 3」）")]
        [SerializeField] private BattleSlotWidget[] skillSlots = new BattleSlotWidget[3];
        [Tooltip("出不了招时（晕眩回合按 ItemsOnly 口径留在玩家这边）才出现的「结束回合」。")]
        [SerializeField] private Button endTurnButton;

        [Header("玩家状态（招式栏左上方；角标 = 剩余回合 / 次数）")]
        [SerializeField] private RectTransform statusRow;
        [SerializeField] private BattleSlotWidget statusTemplate;
        [Tooltip("玩家状态区右下角的剩余回合（07:43）。")]
        [SerializeField] private BattleSlotWidget roundsSlot;

        [Header("右侧怒气槽")]
        [Tooltip("怒气槽外框：满时换成更鲜艳的颜色；悬停弹详情。")]
        [SerializeField] private BattleSlotWidget rageSlot;
        [SerializeField] private RectTransform ragePipRow;
        [SerializeField] private Image ragePipTemplate;

        [Header("BOSS 头顶（07「BOSS状态显示」：血条上方状态、下方醉酒值）")]
        [SerializeField] private RectTransform bossHud;
        [SerializeField] private TMP_Text bossName;
        [SerializeField] private TMP_Text bossStatus;
        [SerializeField] private BattleBarWidget bossHealth;
        [SerializeField] private BattleBarWidget bossDrunk;
        [Tooltip("盖在 BOSS 两根条上的悬停区：弹醉酒档位效果 / 减疗 / 下次普攻加成。")]
        [SerializeField] private BattleSlotWidget bossInfo;

        [Header("玩家头顶")]
        [SerializeField] private RectTransform playerHud;
        [SerializeField] private BattleBarWidget playerHealth;
        [Tooltip("晕眩标记（07:82 薄醉重击晕眩玩家）。")]
        [SerializeField] private GameObject stunMark;
        [Tooltip("头顶条相对头顶锚点的屏幕偏移（参考分辨率像素）。")]
        [SerializeField] private Vector2 headOffset = new Vector2(0f, 24f);

        [Header("飘字 / 中央提示 / 详情框")]
        [SerializeField] private RectTransform floatRoot;
        [SerializeField] private TMP_Text floatTemplate;
        [SerializeField] private CanvasGroup hintGroup;
        [SerializeField] private TMP_Text hintText;
        [SerializeField] private RectTransform tooltipRoot;
        [SerializeField] private CanvasGroup tooltipGroup;
        [SerializeField] private TMP_Text tooltipText;

        [Header("颜色")]
        [SerializeField] private Color litColor = new Color(0.96f, 0.78f, 0.36f, 1f);
        [SerializeField] private Color dimColor = new Color(0.32f, 0.32f, 0.36f, 1f);
        [SerializeField] private Color rageEmptyColor = new Color(0.22f, 0.22f, 0.25f, 1f);
        [SerializeField] private Color rageColor = new Color(0.72f, 0.24f, 0.18f, 1f);
        [Tooltip("怒气满时的颜色（07:41「更鲜艳」）。")]
        [SerializeField] private Color rageFullColor = new Color(1f, 0.32f, 0.08f, 1f);
        [SerializeField] private Color rageFrameColor = new Color(0.25f, 0.18f, 0.16f, 0.9f);
        [Tooltip("怒气满时槽外框的颜色（比格子颜色暗一档，格子才看得出来）。")]
        [SerializeField] private Color rageFrameFullColor = new Color(0.62f, 0.2f, 0.06f, 0.95f);
        [SerializeField] private Color damageColor = new Color(1f, 0.32f, 0.28f, 1f);
        [SerializeField] private Color healColor = new Color(0.45f, 0.95f, 0.5f, 1f);
        [SerializeField] private Color infoColor = new Color(1f, 0.88f, 0.45f, 1f);

        [Header("节奏（秒，不受时间缩放）")]
        [SerializeField, Min(0.01f)] private float hintFadeSeconds = 0.15f;
        [SerializeField, Min(0.01f)] private float floatSeconds = 0.9f;
        [Tooltip("飘字上升的高度（参考分辨率像素）。")]
        [SerializeField] private float floatRise = 70f;
        [Tooltip("飘字起点相对头顶锚点的高度（参考分辨率像素，负 = 在锚点下方的上半身处，不压头顶条）。")]
        [SerializeField] private float floatStartLift = -110f;
        [Tooltip("同时飘两行时每行错开的高度（参考分辨率像素）。")]
        [SerializeField] private float floatLaneHeight = 46f;

        private readonly List<BattleSlotWidget> itemSlots = new List<BattleSlotWidget>();
        private readonly List<BattleSlotWidget> statusSlots = new List<BattleSlotWidget>();
        private readonly List<Image> ragePips = new List<Image>();
        private readonly List<BattleStatusEntry> statusBuffer = new List<BattleStatusEntry>();
        private readonly List<TMP_Text> floatPool = new List<TMP_Text>();
        private Camera stageCamera;
        private Vector3 playerHead;
        private Vector3 bossHead;
        private bool anchored;
        private BattleSlotWidget hovered;
        private MotionHandle hintMotion;
        private bool hooked;

        public override UILayer Layer => UILayer.Panel;

        public override bool IsFullScreen => true;

        /// <summary>战斗中 Esc 不关战斗界面，也不开暂停菜单（UICancelRouter 判 Blocked）。</summary>
        public override bool CloseOnCancel => false;

        /// <summary>点了一个招式格。</summary>
        public event Action<PlayerSkill> OnSkillChosen;

        /// <summary>点了一个道具格（道具 id = BattleItemSlot.ItemId）。</summary>
        public event Action<string> OnItemChosen;

        /// <summary>点了「结束回合」。</summary>
        public event Action OnEndTurn;

        /// <summary>左上角回合提示当前的字。</summary>
        public string TurnText => turnLabel == null ? string.Empty : turnLabel.text;

        /// <summary>中央提示当前的字（回放 / 测试读）。</summary>
        public string HintText => hintText == null ? string.Empty : hintText.text;

        /// <summary>中央提示是否正显示着（alpha &gt; 0，含淡入淡出途中）。</summary>
        public bool HintVisible => hintGroup != null && hintGroup.alpha > 0f;

        /// <summary>中央提示已完全淡入（回放截图前等它：只等 <see cref="HintVisible"/> 会截到淡入第一帧，横条和字都几乎透明）。</summary>
        public bool HintOpaque => hintGroup != null && hintGroup.alpha >= 0.999f;

        /// <summary>第 <paramref name="index"/> 个招式格是否高亮（回放读）。</summary>
        public bool IsSkillLit(int index) => index >= 0 && index < skillSlots.Length && skillSlots[index] != null && skillSlots[index].IsLit;

        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            Validate();
            itemTemplate.gameObject.SetActive(false);
            statusTemplate.gameObject.SetActive(false);
            ragePipTemplate.gameObject.SetActive(false);
            floatTemplate.gameObject.SetActive(false);
            hintGroup.alpha = 0f;
            HideTooltip();
            if (stunMark != null) stunMark.SetActive(false);
            if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
            Hook();
            return UniTask.CompletedTask;
        }

        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            Unhook();
            if (hintMotion.IsActive()) hintMotion.Cancel();
            stageCamera = null;
            anchored = false;
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// 开场配置：BOSS 名字、怒气格数、头顶条投影用的相机，以及两人**站立时**的头顶位置（世界坐标，
        /// 取 <see cref="BattleActor.StandingHeadPosition"/>；条不跟出招 / 倒地等动作走，理由见文件头）。
        /// </summary>
        public void Configure(string bossDisplayName, int rageMax, Camera camera, Vector3 playerHeadWorld, Vector3 bossHeadWorld)
        {
            if (bossName != null) bossName.text = bossDisplayName ?? string.Empty;
            stageCamera = camera;
            playerHead = playerHeadWorld;
            bossHead = bossHeadWorld;
            anchored = true;
            EnsureRagePips(rageMax);
            FollowHeads();
        }

        /// <summary>
        /// 按会话刷新整张界面。<paramref name="awaitingCommand"/> 为真时高亮的格子才能点；<paramref name="turnText"/> 由表现层给
        /// （BOSS 回合播放时会话已经回到玩家回合，回合提示不能现读会话）；<paramref name="playerTurnShown"/> 为假（正在演敌方回合）时
        /// 招式格、道具格一律置暗，同理不能现读会话。<paramref name="animateBars"/> 为假时条直接跳到位。
        /// </summary>
        public void Render(BattleSession session, IReadOnlyList<BattleItemSlot> items, in BattleSettings settings, bool awaitingCommand,
            string turnText, bool playerTurnShown, bool animateBars)
        {
            if (session == null) return;
            if (turnLabel != null) turnLabel.text = turnText ?? string.Empty;
            RenderSkills(session, settings, awaitingCommand, playerTurnShown);
            RenderItems(session, items, settings, awaitingCommand, playerTurnShown);
            RenderStatuses(session, settings);
            RenderRage(session.Player.Rage, settings.PlayerSkills);
            RenderBars(session, settings, animateBars);
            if (endTurnButton != null)
            {
                bool show = awaitingCommand && BattleHudRules.NeedsEndTurn(session);
                endTurnButton.gameObject.SetActive(show);
                endTurnButton.interactable = show;
            }

            if (hovered != null) ShowTooltip(hovered); // 详情跟着数值变
        }

        /// <summary>只换左上角回合提示（播放演出时切「你的回合 / 敌方回合」，不动别的）。</summary>
        public void SetTurnText(string text)
        {
            if (turnLabel != null) turnLabel.text = text ?? string.Empty;
        }

        /// <summary>
        /// 屏幕中央闪现提示：淡入 → 停 <paramref name="holdSeconds"/> → 淡出，返回时已淡出。新提示会顶掉正在播的旧提示。
        /// </summary>
        public async UniTask FlashHintAsync(string text, float holdSeconds, CancellationToken ct)
        {
            if (!await HoldHintAsync(text, holdSeconds, ct)) return;
            if (hintText.text != text) return; // 已被新提示顶掉，淡出交给新提示
            hintMotion = FadeHint(1f, 0f);
            await hintMotion.ToUniTask(CancelBehavior.Cancel, false, ct);
        }

        /// <summary>
        /// 胜负结果大字（胜利 / 战败 / 回合用尽）：与 <see cref="FlashHintAsync"/> 同一条中央横条，淡入 → 停 <paramref name="holdSeconds"/> 后返回，
        /// **不淡出**——留在屏上直到流程落幕收场（BattleArena.ExitAsync 先落黑幕再关界面），画面暗下去前最后看到的就是结果。
        /// 下次开场 <see cref="OnOpenAsync"/> 会把它清掉。
        /// </summary>
        public UniTask ShowResultAsync(string text, float holdSeconds, CancellationToken ct) => HoldHintAsync(text, holdSeconds, ct).AsUniTask();

        // 中央横条淡入到不透明、再停 holdSeconds；没东西可显示（空字 / 预制体缺引用）返回 false。新提示会顶掉正在播的旧提示。
        private async UniTask<bool> HoldHintAsync(string text, float holdSeconds, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(text) || hintGroup == null) return false;
            if (hintMotion.IsActive()) hintMotion.Cancel();
            hintText.text = text;
            hintMotion = FadeHint(hintGroup.alpha, 1f);
            await hintMotion.ToUniTask(CancelBehavior.Cancel, false, ct);
            await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), true, PlayerLoopTiming.Update, ct);
            return true;
        }

        /// <summary>
        /// 在 <paramref name="anchorWorld"/>（世界坐标，通常是 <see cref="BattleActor.StandingHeadPosition"/>）上方飘一行字：上升并淡出，不阻塞。
        /// <paramref name="lane"/> 是起点往上错开几行（同一时刻飘两行字时不叠在一起）。
        /// </summary>
        public void ShowFloat(Vector3 anchorWorld, string text, BattleFloatTone tone, bool big, int lane)
        {
            if (string.IsNullOrEmpty(text) || !TryProject(anchorWorld, out Vector2 local)) return;
            TMP_Text label = TakeFloat();
            label.text = text;
            label.fontSize = floatTemplate.fontSize * (big ? 1.5f : 1f);
            Color color = tone == BattleFloatTone.Damage ? damageColor : tone == BattleFloatTone.Heal ? healColor : infoColor;
            label.color = color;
            var rect = (RectTransform)label.transform;
            Vector2 start = local + new Vector2(0f, floatStartLift + lane * floatLaneHeight);
            rect.anchoredPosition = start;
            label.gameObject.SetActive(true);
            float rise = floatRise * (big ? 1.3f : 1f);
            LMotion.Create(0f, 1f, floatSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() => label.gameObject.SetActive(false))
                .WithOnCancel(() => label.gameObject.SetActive(false))
                .Bind(t =>
                {
                    rect.anchoredPosition = start + new Vector2(0f, rise * t);
                    Color c = color;
                    c.a = 1f - t * t;
                    label.color = c;
                })
                .AddTo(label.gameObject);
        }

        private void LateUpdate() => FollowHeads();

        // ── 渲染各块 ──────────────────────────────────────────────────────────

        private void RenderSkills(BattleSession session, in BattleSettings settings, bool awaiting, bool playerTurnShown)
        {
            for (int i = 0; i < skillSlots.Length && i < BattleHudRules.Skills.Count; i++)
            {
                BattleSlotWidget slot = skillSlots[i];
                if (slot == null) continue;
                PlayerSkill skill = BattleHudRules.Skills[i];
                bool lit = playerTurnShown && BattleHudRules.SkillLit(session, skill, settings.PlayerSkills);
                slot.SetTexts(BattleHudRules.SkillTitle(skill), BattleHudRules.SkillCostText(skill, settings.PlayerSkills), ((int)skill).ToString());
                slot.SetTooltip(BattleHudRules.SkillTooltip(skill, settings.PlayerSkills, session.Player.ExtraDamageCharges));
                slot.SetLook(lit, litColor, dimColor, awaiting && lit);
            }
        }

        private void RenderItems(BattleSession session, IReadOnlyList<BattleItemSlot> items, in BattleSettings settings, bool awaiting,
            bool playerTurnShown)
        {
            bool canUseNow = playerTurnShown && BattleHudRules.CanUseItemsNow(session, settings.Flow.StunTurnPolicy);
            int count = items == null ? 0 : items.Count;
            int shown = 0;
            for (int i = 0; i < count; i++)
            {
                BattleItemSlot item = items[i];
                BattleItemLook look = BattleHudRules.ItemLook(item, canUseNow);
                if (look == BattleItemLook.Hidden) continue;
                BattleSlotWidget slot = ItemSlotAt(shown++);
                slot.gameObject.SetActive(true);
                slot.Key = item.ItemId;
                slot.SetTexts(item.DisplayName, "×" + item.Count, string.Empty);
                slot.SetTooltip(BattleHudRules.ItemTooltip(item, settings.Items, settings.ItemOncePerBattle));
                bool lit = look == BattleItemLook.Lit;
                slot.SetLook(lit, litColor, dimColor, awaiting && lit);
            }

            for (int i = shown; i < itemSlots.Count; i++) itemSlots[i].gameObject.SetActive(false);
            if (itemEmpty != null) itemEmpty.SetActive(shown == 0);
        }

        private void RenderStatuses(BattleSession session, in BattleSettings settings)
        {
            BattleHudRules.CollectPlayerStatuses(session.Player, settings, statusBuffer);
            for (int i = 0; i < statusBuffer.Count; i++)
            {
                BattleStatusEntry entry = statusBuffer[i];
                BattleSlotWidget slot = StatusSlotAt(i);
                slot.gameObject.SetActive(true);
                slot.SetTexts(entry.Icon, string.Empty, entry.Badge.ToString());
                slot.SetTooltip(entry.Tooltip);
                slot.SetLook(true, litColor, dimColor, false);
            }

            for (int i = statusBuffer.Count; i < statusSlots.Count; i++) statusSlots[i].gameObject.SetActive(false);
            if (stunMark != null) stunMark.SetActive(session.Player.StunRoundsRemaining > 0 || session.Player.CannotActThisTurn);
            if (roundsSlot != null)
            {
                int limit = settings.Flow.RoundLimit;
                roundsSlot.SetTexts("剩余回合", BattleHudRules.RemainingRoundsText(limit, session.CompletedRounds), null);
                roundsSlot.SetTooltip(BattleHudRules.RoundsTooltip(limit, session.CompletedRounds));
            }
        }

        private void RenderRage(int rage, in PlayerSkillSettings skills)
        {
            EnsureRagePips(skills.RageMax);
            bool full = BattleHudRules.RageFull(rage, skills.RageMax);
            for (int i = 0; i < ragePips.Count; i++)
                ragePips[i].color = i < rage ? (full ? rageFullColor : rageColor) : rageEmptyColor;
            if (rageSlot != null)
            {
                rageSlot.SetTexts("怒气", rage + "/" + skills.RageMax, null);
                rageSlot.SetTooltip(BattleHudRules.RageTooltip(rage, skills));
                rageSlot.SetLook(full, rageFrameFullColor, rageFrameColor, false);
            }
        }

        private void RenderBars(BattleSession session, in BattleSettings settings, bool animate)
        {
            BossBattleRules boss = session.Boss;
            int drunkMax = settings.Drunk.MaxDrunkValue;
            if (bossHealth != null) bossHealth.Set(boss.Health, boss.MaxHealth, "生命 " + BattleHudRules.BarText(boss.Health, boss.MaxHealth), animate);
            if (bossDrunk != null) bossDrunk.Set(boss.DrunkValue, drunkMax, "醉酒 " + BattleHudRules.BarText(boss.DrunkValue, drunkMax), animate);
            if (bossStatus != null)
            {
                string status = BattleHudRules.BossStatusText(boss.Tier, boss.HealReductionPercent);
                bossStatus.text = status;
                bossStatus.gameObject.SetActive(status.Length > 0); // 正常态不显示（07:70）
            }

            if (bossInfo != null) bossInfo.SetTooltip(BattleHudRules.BossTooltip(boss, settings));
            PlayerBattleRules player = session.Player;
            if (playerHealth != null)
                playerHealth.Set(player.Health, player.MaxHealth, "生命 " + BattleHudRules.BarText(player.Health, player.MaxHealth), animate);
        }

        // ── 跟随 / 详情框 / 池 ────────────────────────────────────────────────

        private void FollowHeads()
        {
            Place(bossHud, bossHead);
            Place(playerHud, playerHead);
        }

        private void Place(RectTransform hud, Vector3 anchorWorld)
        {
            if (hud == null) return;
            Vector2 local = default;
            bool visible = anchored && TryProject(anchorWorld, out local);
            if (hud.gameObject.activeSelf != visible) hud.gameObject.SetActive(visible);
            if (visible) hud.anchoredPosition = local + headOffset;
        }

        // 世界坐标 → 本面板根节点的局部坐标（Screen Space Overlay，换算不需要相机）。
        private bool TryProject(Vector3 world, out Vector2 local)
        {
            local = default;
            if (stageCamera == null || !stageCamera.isActiveAndEnabled) return false;
            Vector3 screen = stageCamera.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screen, null, out local);
        }

        private void HandleHoverStart(BattleSlotWidget slot)
        {
            hovered = slot;
            ShowTooltip(slot);
        }

        private void HandleHoverEnd(BattleSlotWidget slot)
        {
            if (hovered != slot) return;
            hovered = null;
            HideTooltip();
        }

        private void ShowTooltip(BattleSlotWidget slot)
        {
            if (tooltipRoot == null || slot == null || string.IsNullOrEmpty(slot.Tooltip))
            {
                HideTooltip();
                return;
            }

            tooltipText.text = slot.Tooltip;
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRoot);
            var slotRect = (RectTransform)slot.transform;
            Rect area = Rect.rect;
            Vector2 slotCenter = Rect.InverseTransformPoint(slotRect.TransformPoint(slotRect.rect.center));
            float slotHalfHeight = slotRect.rect.height * 0.5f;
            // 格子在屏幕上半部就往下弹，否则往上弹；横向夹在面板内。
            bool below = slotCenter.y > area.center.y;
            Vector2 size = tooltipRoot.rect.size;
            tooltipRoot.pivot = new Vector2(0.5f, below ? 1f : 0f);
            float x = GameMath.Clamp(slotCenter.x, area.xMin + size.x * 0.5f, area.xMax - size.x * 0.5f);
            float y = below ? slotCenter.y - slotHalfHeight - 8f : slotCenter.y + slotHalfHeight + 8f;
            tooltipRoot.anchoredPosition = new Vector2(x, y);
            tooltipGroup.alpha = 1f;
        }

        private void HideTooltip()
        {
            if (tooltipGroup != null) tooltipGroup.alpha = 0f;
        }

        private MotionHandle FadeHint(float from, float to) =>
            LMotion.Create(from, to, hintFadeSeconds)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .BindToAlpha(hintGroup)
                .AddTo(gameObject);

        private BattleSlotWidget ItemSlotAt(int index)
        {
            while (itemSlots.Count <= index)
            {
                BattleSlotWidget slot = Instantiate(itemTemplate, itemRow, false);
                slot.name = "Item" + itemSlots.Count;
                slot.OnClicked += HandleItemClicked;
                slot.OnHoverStart += HandleHoverStart;
                slot.OnHoverEnd += HandleHoverEnd;
                itemSlots.Add(slot);
            }

            return itemSlots[index];
        }

        private BattleSlotWidget StatusSlotAt(int index)
        {
            while (statusSlots.Count <= index)
            {
                BattleSlotWidget slot = Instantiate(statusTemplate, statusRow, false);
                slot.name = "Status" + statusSlots.Count;
                slot.OnHoverStart += HandleHoverStart;
                slot.OnHoverEnd += HandleHoverEnd;
                statusSlots.Add(slot);
            }

            return statusSlots[index];
        }

        private void EnsureRagePips(int count)
        {
            while (ragePips.Count < count)
            {
                Image pip = Instantiate(ragePipTemplate, ragePipRow, false);
                pip.name = "Pip" + ragePips.Count;
                pip.gameObject.SetActive(true);
                ragePips.Add(pip);
            }

            for (int i = 0; i < ragePips.Count; i++) ragePips[i].gameObject.SetActive(i < count);
        }

        private TMP_Text TakeFloat()
        {
            for (int i = 0; i < floatPool.Count; i++)
            {
                if (!floatPool[i].gameObject.activeSelf) return floatPool[i];
            }

            TMP_Text created = Instantiate(floatTemplate, floatRoot, false);
            created.name = "Float" + floatPool.Count;
            floatPool.Add(created);
            return created;
        }

        private void Hook()
        {
            if (hooked) return;
            hooked = true;
            for (int i = 0; i < skillSlots.Length; i++)
            {
                if (skillSlots[i] == null) continue;
                skillSlots[i].OnClicked += HandleSkillClicked;
                skillSlots[i].OnHoverStart += HandleHoverStart;
                skillSlots[i].OnHoverEnd += HandleHoverEnd;
            }

            foreach (BattleSlotWidget slot in new[] { roundsSlot, rageSlot, bossInfo })
            {
                if (slot == null) continue;
                slot.OnHoverStart += HandleHoverStart;
                slot.OnHoverEnd += HandleHoverEnd;
            }

            Hook(endTurnButton, HandleEndTurn);
        }

        private void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            for (int i = 0; i < skillSlots.Length; i++)
            {
                if (skillSlots[i] == null) continue;
                skillSlots[i].OnClicked -= HandleSkillClicked;
                skillSlots[i].OnHoverStart -= HandleHoverStart;
                skillSlots[i].OnHoverEnd -= HandleHoverEnd;
            }

            foreach (BattleSlotWidget slot in new[] { roundsSlot, rageSlot, bossInfo })
            {
                if (slot == null) continue;
                slot.OnHoverStart -= HandleHoverStart;
                slot.OnHoverEnd -= HandleHoverEnd;
            }

            Unhook(endTurnButton, HandleEndTurn);
            hovered = null;
            HideTooltip();
        }

        private void HandleSkillClicked(BattleSlotWidget slot)
        {
            int index = Array.IndexOf(skillSlots, slot);
            if (index >= 0 && index < BattleHudRules.Skills.Count) OnSkillChosen?.Invoke(BattleHudRules.Skills[index]);
        }

        private void HandleItemClicked(BattleSlotWidget slot) => OnItemChosen?.Invoke(slot.Key);

        private void HandleEndTurn() => OnEndTurn?.Invoke();

        // 预制体由白盒生成器建，缺引用说明生成器没跑或被手改坏了：报清楚该怎么修。
        private void Validate()
        {
            if (itemRow == null || itemTemplate == null || statusRow == null || statusTemplate == null || ragePipRow == null
                || ragePipTemplate == null || floatRoot == null || floatTemplate == null || hintGroup == null || hintText == null
                || tooltipRoot == null || tooltipGroup == null || tooltipText == null || skillSlots == null || skillSlots.Length < 3)
            {
                throw new InvalidOperationException(
                    "BattleView 预制体引用不全。修法：菜单 21Days/战斗/重建战斗白盒 重新生成 Assets/_Project/Prefabs/UI/BattleView.prefab。");
            }
        }
    }
}
