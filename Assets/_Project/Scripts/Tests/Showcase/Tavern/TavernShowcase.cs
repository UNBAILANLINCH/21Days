// 职责：真实按钮验证划拳、动态学习、稳定存档、战斗继承和离区清理。
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Save;
using Game.Core.Simulation;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Narrative;
using Game.Session;
using Game.ShopPlaytest;
using Game.Tavern;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Game.Tests.Showcase.Tavern
{
    [Category("Showcase")]
    public sealed class TavernShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Tavern";
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator BattleArt_DefeatRetryVictoryUsesDeliveredSprites()
        {
            yield return ReadyUnlockedShop(4);
            yield return Step("美术验收夹具：三店成果入包；挑战仍走取皮、划拳及主人对白", () =>
            {
                var loot = ResolveService<Game.Loot.LootService>();
                loot.GrantItems(ShopRewardRules.CostumeId, 1); loot.GrantItems(ShopRewardRules.ClothesId, 1);
                loot.GrantItems(ShopRewardRules.ClaspId, 1); loot.GrantItems(ShopRewardRules.BeltHookId, 2); loot.GrantItems(ShopRewardRules.RingId, 3);
                loot.GrantItems(2001, 2);
            }, 0);
            yield return Borrow(0);
            yield return GuessUntil(1, "shop_boss", 20, false);
            var setup = ResolveService<Game.Battle.BattleSetup>();
            var battle = ResolveService<Game.Battle.BattleFlow>();
            var worldView = Object.FindObjectOfType<Game.Monster.EncounterSceneView>();
            var worldCamera = Camera.main;
            var innStage = GameObject.Find("InnBattleStage").GetComponent<Game.Battle.BattleStage>();
            var innkeeper = GameObject.Find("shop_innkeeper").GetComponentInChildren<SpriteRenderer>();
            var playerPosition = worldView.PlayerBody.position;
            var playerRotation = worldView.PlayerBody.rotation;
            bool hudVisible = ResolveService<IUIService>().IsLayerVisible(UILayer.Hud);
            foreach (int outcome in new[] { -1, 0, 1 })
            {
                bool victory = outcome == 1;
                using (setup.OverrideSettings(WithBossOnlySkill1(setup.Settings, victory ? 1 : 120)))
                {
                    playerPosition = worldView.PlayerBody.position;
                    playerRotation = worldView.PlayerBody.rotation;
                    yield return Command(3, false);
                    yield return AdvanceFormalDialogue();
                    yield return WaitUntil("正式战斗接受指令", () => BattlePresenter.View != null && BattlePresenter.IsAwaitingCommand, 20);
                    yield return WaitCurtainRevealed();
                    Assert.That(BattlePresenter.Stage, Is.SameAs(innStage));
                    Assert.That(innStage.Camera.enabled && !worldCamera.enabled, Is.True);
                    Assert.That(innStage.Camera.CompareTag("MainCamera"), Is.False);
                    Assert.That(innStage.Player.RendererCount > 0 && innStage.Boss.RendererCount > 0, Is.True);
                    Assert.That(innStage.Boss.GetComponentInChildren<Game.CharacterPuppet.ChibiPuppet>().name,
                        Does.StartWith("Chibi_stage1_boss"), "取酒肆主人交付外观，不能回退原占位");
                    Assert.That(innStage.Player.GetComponentInChildren<Game.CharacterPuppet.ChibiPuppet>().name,
                        Does.StartWith("Chibi_customers"), "玩家应保留当前穿着的酒客皮");
                    Assert.That(innkeeper.forceRenderingOff, Is.True);
                    foreach (var actor in new[] { innStage.Player, innStage.Boss })
                    {
                        var screen = innStage.Camera.WorldToViewportPoint(actor.HomePosition);
                        Assert.That(screen.x, Is.InRange(.1f, .9f));
                        Assert.That(screen.y, Is.InRange(.22f, .7f), "脚底避开下方战斗HUD");
                        Assert.That(Physics.Raycast(actor.HomePosition + Vector3.up, Vector3.down, out var hit, 2,
                            LayerMask.GetMask("Ground")), Is.True);
                        Assert.That(actor.HomePosition.y - hit.point.y, Is.InRange(0, .1f));
                    }
                    var itemBounds = ScreenBounds((RectTransform)BattlePresenter.View.transform.Find("ItemBar"));
                    Assert.That(itemBounds.Overlaps(ScreenBounds((RectTransform)BattlePresenter.View.transform.Find("BossHud/Name"))), Is.False, "道具栏不能盖住Boss姓名");
                    Assert.That(itemBounds.Overlaps(ScreenBounds((RectTransform)BattlePresenter.View.transform.Find("TurnBox"))), Is.False, "道具栏不能盖住回合栏");
                    yield return Snapshot(victory ? "美术验收_重试满血与初始怒气" : "美术验收_正式Boss界面");
                    if (outcome < 0)
                    {
                        yield return BattlePresenter.CloseAsync(System.Threading.CancellationToken.None).ToCoroutine();
                        yield return WaitUntil("取消表现后原战斗流程完成收尾", () => !battle.IsBattleRunning, 15);
                        yield return Check("取消后恢复相机、输入、暂停与局部人物", () => worldCamera.enabled &&
                            !innStage.Camera.enabled && !innkeeper.forceRenderingOff &&
                            !ResolveService<IWorldPauseService>().IsPaused && ResolveService<IInputService>().Actions.Gameplay.enabled, 5);
                        Assert.That(innStage.GetComponentsInChildren<Game.CharacterPuppet.ChibiPuppet>(true).Length, Is.Zero);
                        continue;
                    }
                    if (victory)
                    {
                        yield return FightUntil(() => BattlePresenter.View.IsSkillLit(1), 30, true);
                        Assert.That(BattlePresenter.View.IsSkillLit(1), Is.True, "先通过第一招式积累实际怒气");
                        string previousTurn = BattlePresenter.View.TurnText;
                        yield return Input.Press(ResolveService<IInputService>().Actions.Dialogue.Choice2);
                        yield return Snapshot("客栈战场_第二招式动作");
                        yield return WaitUntil("第二招式接受并恢复玩家回合", () => BattlePresenter.IsAwaitingCommand && BattlePresenter.View.TurnText != previousTurn, 15);
                        var item = System.Array.Find(BattlePresenter.View.GetComponentsInChildren<Game.Battle.BattleSlotWidget>(), slot => slot.Key == "2001");
                        Assert.That(item, Is.Not.Null);
                        Assert.That(item.Button.interactable, Is.True);
                        int potionsBefore = ResolveService<Game.Loot.LootService>().Items[2001];
                        Canvas.ForceUpdateCanvases();
                        var rect = (RectTransform)item.Button.transform;
                        var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) {
                            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
                            position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
                        var hits = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                        UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointer, hits);
                        Assert.That(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(item.Button.transform), Is.True, "道具装饰不得挡住真实点击");
                        UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                        yield return null;
                        yield return Check("药剂按真实库存扣一且本场置暗", () => ResolveService<Game.Loot.LootService>().Items[2001] == potionsBefore - 1 && !item.Button.interactable, 5);
                        yield return Snapshot("美术验收_真实道具禁用");
                    }
                    if (!victory)
                    {
                        var delivered = innStage.Boss.GetComponentInChildren<Game.CharacterPuppet.ChibiPuppet>();
                        yield return Input.Press(ResolveService<IInputService>().Actions.Dialogue.Choice1);
                        yield return WaitUntil("真实回合驱动Boss预备动作", () => delivered != null && delivered.Animator.GetCurrentAnimatorStateInfo(0).IsName("attack"), 15);
                        int phase = 0;
                        float deadline = Time.realtimeSinceStartup + 5f;
                        float[] thresholds = { 0f, .30f, .49f, .75f };
                        while (phase < thresholds.Length && Time.realtimeSinceStartup < deadline && delivered != null)
                        {
                            var pose = delivered.Animator.GetCurrentAnimatorStateInfo(0);
                            if (pose.IsName("attack") && pose.normalizedTime >= thresholds[phase])
                            {
                                yield return Snapshot("阶段一Boss_" + new[] { "起势", "预备", "命中", "回收" }[phase]);
                                phase++;
                            }
                            else yield return null;
                        }
                        yield return Check("Boss拆件出招覆盖起势、预备、命中和回收", () => phase == thresholds.Length);
                    }
                    string result = victory ? Game.Battle.BattleCueRules.VictoryText : Game.Battle.BattleCueRules.DefeatText;
                    yield return FightUntil(() => BattlePresenter.View != null && BattlePresenter.View.HintText == result && BattlePresenter.View.HintVisible, 120, false);
                    var view = BattlePresenter.View;
                    Assert.That(view, Is.Not.Null);
                    var title = view.transform.Find("Hint/ResultTitle").GetComponent<Image>();
                    Assert.That(title.gameObject.activeInHierarchy && title.sprite != null, Is.True);
                    Assert.That(title.sprite.name, Is.EqualTo(victory ? "胜利" : "失败"));
                    Assert.That(view.transform.Find("Hint").GetComponentInChildren<TMPro.TMP_Text>(true).enabled, Is.False);
                    Assert.That(FindDeep<Game.Battle.BattleBarWidget>(view.transform.Find(victory ? BossHudName : PlayerHudName), "HealthBar").Ratio, Is.LessThan(.001f));
                    yield return CaptureBattleResultMotion(victory ? "victory" : "defeat");
                    yield return WaitUntil("战斗释放舞台及界面", () => !battle.IsBattleRunning && BattlePresenter.View == null, 20);
                    Assert.That(innStage.Camera.enabled, Is.False);
                    Assert.That(worldCamera.enabled, Is.True, "原探索相机必须恢复启用");
                    Assert.That(innkeeper.forceRenderingOff, Is.False);
                    Assert.That(innStage.GetComponentsInChildren<Game.CharacterPuppet.ChibiPuppet>(true).Length, Is.Zero);
                    Assert.That(Vector3.Distance(worldView.PlayerBody.position, playerPosition), Is.LessThan(.01f));
                    Assert.That(Quaternion.Angle(worldView.PlayerBody.rotation, playerRotation), Is.LessThan(.01f));
                }
                yield return AdvanceFormalDialogue();
                yield return FinishReplies();
                yield return Check("战斗结束释放暂停与输入", () => !ResolveService<IWorldPauseService>().IsPaused &&
                    ResolveService<IInputService>().Actions.Gameplay.enabled, 5);
                yield return Check("战后对白结束恢复原HUD层", () => ResolveService<IUIService>().IsLayerVisible(UILayer.Hud) == hudVisible, 5);
                if (!victory)
                {
                    yield return GoToShop(3);
                    yield return OpenConversation(GameObject.Find("shop_innkeeper").GetComponent<ShopPlaytestActor>());
                    yield return Check("战斗后客栈掌柜仍能交谈", () => DialogueRunning, 5);
                    yield return ShowChoices();
                    yield return Snapshot("客栈战后_掌柜交互恢复");
                    yield return Choose("leave");
                    yield return GoToShop(4);
                    yield return GuessUntil(1, "shop_boss", 20, false);
                }
            }
            yield return Check("真实胜利标记独立于旧示例Boss", () => State.TavernCompleted &&
                ResolveService<NarrativeService>().HasStoryFlag("world.shopboss.defeated") && !ResolveService<NarrativeService>().HasStoryFlag("world.sampleboss.defeated"), 5);
        }
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator InnBattle_LeaveDuringCommandUnloadsWithoutLeakingPause()
        {
            yield return ReadyUnlockedShop(3);
            // 隔离存档定位到真实战斗等待帧，只验证退出；真实挑战入口由上方用例覆盖。
            var narrative = ResolveService<NarrativeService>();
            narrative.CaptureIntoPartition();
            var frame = ResolveService<Game.Core.Save.ISaveService>().Get<NarrativeSaveData>().Current;
            frame.StoryId = "shop_boss_battle";
            frame.StageId = "fight";
            frame.TargetId = "shop_boss";
            frame.RequestIssued = false;
            narrative.ReloadFromSave();
            var battle = ResolveService<Game.Battle.BattleFlow>();
            var pause = ResolveService<IWorldPauseService>();
            var presenter = BattlePresenter;
            yield return WaitUntil("客栈战场等待指令", () => presenter.IsAwaitingCommand && presenter.Stage.IsWorldStage, 20);
            yield return Snapshot("客栈战场_退出前");
            bool left = false;
            yield return Step("在等待指令时经原流程返回标题", () => BeginLeaveToTitle(ResolveService<Game.Core.Flow.IGameFlow>(), () => left = true), 0);
            yield return Check("退出完成且没有战斗界面、舞台实例或暂停令牌", () => left && !battle.IsBattleRunning &&
                presenter.View == null && !pause.IsPaused && Object.FindObjectsOfType<Game.Battle.BattleStage>().Length == 0, 20);
            yield return Check("标题开始按钮与面板实际恢复可见", () =>
            {
                var title = ResolveService<IUIService>().Get<Game.Core.UI.Views.TitleView>();
                if (title == null || !title.gameObject.activeInHierarchy) return false;
                var group = title.GetComponent<CanvasGroup>();
                var start = FindDeep<Button>(title.transform, "StartButton");
                var entrance = title.GetComponent<TitleEntranceMotion>();
                return ResolveService<Game.Core.Flow.IGameFlow>().Current is Game.Core.Flow.TitleState &&
                    (group == null || group.alpha > .99f) && start != null && start.IsActive() && start.IsInteractable() &&
                    (entrance == null || !entrance.IsRunning);
            }, 20);
            yield return Snapshot("客栈战场_退出后标题");
        }
        private static Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
        private IEnumerator CaptureBattleResultMotion(string result)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var frames = new System.Collections.Generic.List<Texture2D>();
            var times = new System.Collections.Generic.List<string>();
            var overlay = FindRequired<ShowcaseOverlay>("ShowcaseOverlay"); bool visible = overlay.enabled;
            overlay.enabled = false;
            int opaqueFrame = -1;
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    yield return null; yield return new WaitForEndOfFrame();
                    if (opaqueFrame < 0 && BattlePresenter.View != null && BattlePresenter.View.HintOpaque) opaqueFrame = i;
                    frames.Add(ScreenCapture.CaptureScreenshotAsTexture());
                    times.Add(Time.realtimeSinceStartup.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                Assert.That(opaqueFrame, Is.GreaterThanOrEqualTo(0), "采集必须覆盖完全显出的结算标题");
                string path = System.IO.Path.Combine(ShowcaseOptions.ReportRoot, "battle-art-motion", result + "-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                System.IO.Directory.CreateDirectory(path);
                for (int i = 0; i < frames.Count; i++) System.IO.File.WriteAllBytes(System.IO.Path.Combine(path, "frame-" + i.ToString("00") + ".png"), frames[i].EncodeToPNG());
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(path, "opaque.png"), frames[opaqueFrame].EncodeToPNG());
                System.IO.File.WriteAllLines(System.IO.Path.Combine(path, "times.txt"), times);
            }
            finally
            {
                foreach (Texture2D frame in frames) if (frame != null) Object.Destroy(frame);
                overlay.enabled = visible;
            }
        }
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Guess_LearningSaveBattleAndLeaveReset()
        {
            yield return Ready(4);
            yield return Check("三店尚未完成，酒肆资格不足", () => !State.CanEnterTavern);
            // 聚焦酒肆的夹具；三店真实奖励链另由连续剧情回放完成。
            var loot = ResolveService<Game.Loot.LootService>();
            loot.GrantItems(ShopRewardRules.CostumeId, 1); loot.GrantItems(ShopRewardRules.ClothesId, 1);
            loot.GrantItems(ShopRewardRules.ClaspId, 1); loot.GrantItems(ShopRewardRules.BeltHookId, 2); loot.GrantItems(ShopRewardRules.RingId, 3);
            var station = System.Array.Find(State.World.Actors, actor => actor.StableId == "shop_station_4");
            yield return OpenConversation(station); yield return ShowChoices();
            yield return Check("本体问酒桌会提示取酒客皮，而非只有告辞", () =>
                FindDeep<TMPro.TMP_Text>(ResolveService<IUIService>().Get<Game.Dialogue.DialogueView>().transform, "Body").text.Contains("刺杀取皮"));
            yield return Snapshot("未穿皮_酒桌说明酒客身份与规则");
            yield return Choose("command0"); yield return FinishReplies();
            yield return Borrow(0);
            yield return OpenConversation(station); yield return Choose("body");
            yield return WaitUntil("通过正常对白换回本体", () => State.CurrentRole.Length == 0 && !DialogueRunning, 5);
            yield return OpenConversation(station); yield return ShowChoices();
            yield return Check("已有酒客皮时提示背包使用，不要求再刺杀死者", () =>
                FindDeep<TMPro.TMP_Text>(ResolveService<IUIService>().Get<Game.Dialogue.DialogueView>().transform, "Body").text.Contains("酒客皮已在背包"));
            yield return Snapshot("已取皮未穿_提示从背包换皮");
            yield return Choose("leave");
            yield return Borrow(0);
            yield return OpenConversation(station); yield return ShowChoices();
            yield return Check("穿酒客皮后酒桌说明近距找客人或主人划拳", () =>
                FindDeep<TMPro.TMP_Text>(ResolveService<IUIService>().Get<Game.Dialogue.DialogueView>().transform, "Body").text.Contains("划拳请走近普通客人甲"));
            yield return Snapshot("穿好酒客皮_酒桌指向真实划拳对象");
            yield return Choose("leave");
            yield return Step("与主人出拳：先选手指，再喊总数");
            yield return GuessUntil(1, "shop_boss", 100, true);
            yield return GuessUntil(2, "shop_customer_a", 20, false);
            yield return Check("主人与两客人的醉意独立", () => State.Tavern.BossDrunk == 100 &&
                State.Tavern.CustomerDrunkFor("shop_customer_a") == 20 && State.Tavern.CustomerDrunkFor("shop_customer_b") == 0);
            bool saved = false;
            yield return ResolveService<GameSession>().SaveNowAsync("guess_showcase").ToCoroutine(value => saved = value);
            yield return Check("离席后可稳定保存学习记录", () => saved &&
                ResolveService<ISaveService>().Get<ShopProgressSaveData>().TavernLearning["shop_boss"].History.Count > 0);
            yield return Step("醉意继承到正式酒肆战斗");
            yield return Command(3, false);
            yield return AdvanceFormalDialogue();
            yield return PlayBattle();
            yield return AdvanceFormalDialogue();
            yield return Check("酒肆胜利推进到楼梯口，原示例标记独立", () => State.TavernCompleted &&
                ResolveService<NarrativeService>().CurrentStageId == "enter_r0198" && !ResolveService<NarrativeService>().HasStoryFlag("world.sampleboss.defeated"), 5);
            yield return Snapshot("划拳后挑战完成");
            Vector3 exit = State.World.Station(4).position;
            yield return MoveTo(new Vector2(exit.x - 7, exit.z));
            yield return Check("离区清醉意，历史学习保留", () => State.Tavern.PlayerDrunk + State.Tavern.BossDrunk == 0 &&
                ResolveService<ISaveService>().Get<ShopProgressSaveData>().TavernLearning["shop_boss"].History.Count > 0);
        }
        private IEnumerator GuessUntil(int command, string target, int goal, bool checkSave)
        {
            yield return Command(command, false);
            var ui = ResolveService<IUIService>();
            yield return WaitUntil("划拳界面可操作", () => ui.Get<TavernFingerGuessView>() != null && ui.Get<TavernFingerGuessView>().IsAwaitingGuess, 5);
            var view = ui.Get<TavernFingerGuessView>();
            Assert.That(view, Is.Not.Null);
            if (checkSave)
            {
                bool saved = true;
                yield return ResolveService<GameSession>().SaveNowAsync("pending_guess").ToCoroutine(value => saved = value);
                yield return Check("未揭晓的一拳不进入存档", () => !saved);
            }
            var random = new XorShiftRandomStream(7123);
            int Drunk() => target == "shop_boss" ? State.Tavern.BossDrunk : State.Tavern.CustomerDrunkFor(target);
            for (int i = 0; i < 400 && Drunk() < goal; i++)
            {
                if (!view.IsAwaitingGuess) FindDeep<Button>(view.transform, "Next").onClick.Invoke();
                FindDeep<Button>(view.transform, "Finger" + random.Range(0, 6)).onClick.Invoke();
                FindDeep<Button>(view.transform, "Call" + random.Range(0, 6)).onClick.Invoke();
                FindDeep<Button>(view.transform, "Confirm").onClick.Invoke();
                if (i == 0) yield return Snapshot("双方同时揭晓_" + target);
                yield return null;
            }
            yield return Check("输者饮酒达到目标，平局没有重复结算", () => Drunk() == goal);
            yield return Snapshot("划拳结果与线索_" + target);
            FindDeep<Button>(view.transform, "Back").onClick.Invoke();
            yield return WaitUntil("离席恢复输入与时间", () => ui.Get<TavernFingerGuessView>() == null &&
                !ResolveService<IWorldPauseService>().IsPaused && ResolveService<IInputService>().Actions.Gameplay.enabled, 5);
        }
    }
}
