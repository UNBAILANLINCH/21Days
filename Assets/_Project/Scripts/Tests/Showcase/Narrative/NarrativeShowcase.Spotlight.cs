// 职责：正式新游戏至会面的连续流程；复用五店输入助手，不能用任务回写或发道具冒充完成。
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.UI;
using Game.Forging;
using Game.Narrative;
using Game.Quest;
using Game.Rhythm;
using Game.Session;
using Game.Tavern;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Game.Tests.Showcase.Narrative
{
    public sealed partial class NarrativeShowcase
    {
        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Spotlight_ThreeShopsGuessBossAndReunion()
        {
            yield return Ready(0);
            yield return Step("三店自由探索：戏班、衣肆、金银行");
            yield return CompleteStoryShop(0);
            yield return GoToShop(1); yield return CompleteStoryShop(1);
            yield return GoToShop(2); yield return CompleteStoryShop(2);
            yield return ContinueToReunion();
        }
        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Spotlight_ReverseShopOrderAlsoGrantsTavernEntry()
        {
            yield return Ready(2);
            yield return Step("换顺序完成：金银行、衣肆、戏班");
            yield return CompleteStoryShop(2);
            yield return Check("只完成金银行仍不能进酒肆", () => !State.CanEnterTavern);
            yield return GoToShop(1); yield return CompleteStoryShop(1);
            yield return Check("还缺戏班成果仍不能进酒肆", () => !State.CanEnterTavern);
            yield return GoToShop(0); yield return CompleteStoryShop(0);
            yield return Check("另一顺序也取得三组真实成果", () => State.CanEnterTavern && State.Theater.Completed && State.Clothing.Received && State.Forging.Submitted, 5);
            yield return Snapshot("反向三店完成");
        }
        private IEnumerator CompleteStoryShop(int shop)
        {
            if (shop == 0)
            {
            yield return Borrow(0); yield return Command(0);
            yield return Borrow(1); yield return Command(1);
            yield return Borrow(2); yield return Command(2, false);
            yield return CompleteRhythm(); yield return FinishReplies();
            }
            else if (shop == 1) { yield return Borrow(1); yield return Command(4); }
            else
            {
            yield return Borrow(1);
            yield return OpenStoryForge();
            for (int product = 0; product < 3; product++)
            {
                var forge = ResolveService<IUIService>().Get<ForgingView>();
                FindDeep<Button>(forge.transform, "Product" + product).onClick.Invoke();
                for (int count = 0; count <= product; count++)
                {
                    if (State.Forging.Available < State.Forging.ProductCost)
                    {
                        FindDeep<Button>(forge.transform, "BackButton").onClick.Invoke();
                        yield return WaitUntil("打造台关闭", () => ResolveService<IUIService>().Get<ForgingView>() == null, 5);
                        yield return Borrow(0); yield return Command(0); yield return Borrow(1); yield return OpenStoryForge();
                        forge = ResolveService<IUIService>().Get<ForgingView>();
                        FindDeep<Button>(forge.transform, "Product" + product).onClick.Invoke();
                    }
                    FindDeep<Button>(forge.transform, "CraftButton").onClick.Invoke(); yield return null;
                    while (State.Forging.IsForging)
                    { FindDeep<Button>(forge.transform, "Point" + State.Forging.NextPoint).onClick.Invoke(); yield return null; }
                }
            }
            var completedForge = ResolveService<IUIService>().Get<ForgingView>();
            FindDeep<Button>(completedForge.transform, "SubmitButton").onClick.Invoke();
            FindDeep<Button>(completedForge.transform, "BackButton").onClick.Invoke();
            yield return WaitUntil("打造提交并回到街面", () => State.Forging.Submitted && ResolveService<IUIService>().Get<ForgingView>() == null, 5);
            }
        }
        private IEnumerator ContinueToReunion()
        {
            yield return WaitUntil("三店成果齐备", () => State.CanEnterTavern && ResolveService<IUIService>().Get<ForgingView>() == null, 5);
            yield return Snapshot("三店真实成果进入背包");
            bool saved = false;
            yield return ResolveService<GameSession>().SaveNowAsync("spotlight_three_shops").ToCoroutine(value => saved = value);
            yield return Check("主线稳定等待处可保存", () => saved);
            yield return GoToShop(4); yield return Borrow(0); yield return Command(1, false);
            yield return WaitUntil("划拳可以操作", () => ResolveService<IUIService>().Get<TavernFingerGuessView>() != null, 5);
            var guess = ResolveService<IUIService>().Get<TavernFingerGuessView>();
            var random = new Game.Core.Simulation.XorShiftRandomStream(85);
            for (int round = 0; round < 400 && State.Tavern.BossDrunk < 100; round++)
            {
                if (!guess.IsAwaitingGuess) FindDeep<Button>(guess.transform, "Next").onClick.Invoke();
                FindDeep<Button>(guess.transform, "Finger" + random.Range(0, 6)).onClick.Invoke();
                FindDeep<Button>(guess.transform, "Call" + random.Range(0, 6)).onClick.Invoke();
                FindDeep<Button>(guess.transform, "Confirm").onClick.Invoke(); yield return null;
            }
            yield return Check("划拳获胜使主人喝酒并解锁挑战", () => State.CanChallengeTavern);
            yield return Snapshot("划拳揭晓");
            FindDeep<Button>(guess.transform, "Back").onClick.Invoke();
            yield return WaitUntil("离席恢复街面", () => ResolveService<IUIService>().Get<TavernFingerGuessView>() == null, 5);
            yield return Command(3, false); yield return AdvanceFormalDialogue(); yield return PlayBattle(); yield return AdvanceFormalDialogue();
            yield return Check("实际胜利续回正式主线", () => State.TavernCompleted && ResolveService<NarrativeService>().CurrentStageId == "enter_r0198", 5);
            yield return StoryPoint("enter_r0198"); yield return AdvanceFormalDialogue();
            yield return StoryPoint("enter_r0215"); yield return AdvanceFormalDialogue();
            yield return Check("会面完成，停在下一段前", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0293", 5);
            yield return Snapshot("会面后本段完成");
            yield return ResolveService<GameSession>().SaveNowAsync("spotlight_reunion").ToCoroutine(value => saved = value);
            Assert.That(saved, Is.True);
            yield return ResolveService<GameSession>().ContinueAsync(1).ToCoroutine();
            yield return WaitCurtainRevealed();
            yield return Check("继续保持会面后的目标，不重播开场", () => ResolveService<NarrativeService>().CurrentStageId == "enter_r0293" &&
                ResolveService<QuestService>().CurrentMainId == 900, 8);
            yield return Snapshot("继续后保留正式进度");
        }
        private IEnumerator OpenStoryForge()
        {
            yield return OpenConversation(System.Array.Find(State.World.Actors, actor => actor.StableId == "shop_station_2"));
            yield return Choose("forge");
            yield return WaitUntil("打造界面打开", () => ResolveService<IUIService>().Get<ForgingView>() != null, 5);
        }
        private IEnumerator CompleteRhythm()
        {
            var rhythm = ResolveService<RhythmState>();
            for (int attempt = 0; attempt < 3 && !State.Theater.Completed; attempt++)
            {
            if (attempt > 0) { yield return FinishReplies(); yield return Command(2, false); }
            yield return WaitUntil("完整演奏就绪", () => rhythm.IsActivityOpen && ResolveService<IUIService>().Get<RhythmView>() != null && !DialogueRunning, 10);
            var view = ResolveService<IUIService>().Get<RhythmView>();
            yield return ClickWhenReady("开始演奏", () => FindDeep<Button>(view.transform, "StartButton"), () => rhythm.IsPlaying, 8);
            var edges = new List<(double time, int lane, bool press)>();
            for (int i = 0; i < rhythm.Rules.Count; i++)
            {
                edges.Add((rhythm.Rules.NoteTime(i) - .02, rhythm.Rules.NoteLane(i), true));
                edges.Add((rhythm.Rules.NoteType(i) == RhythmNoteType.Hold ? rhythm.Rules.NoteEnd(i) + .02 : rhythm.Rules.NoteTime(i) + .07, rhythm.Rules.NoteLane(i), false));
            }
            edges.Sort((a, b) => a.time.CompareTo(b.time));
            foreach (var edge in edges)
            {
                yield return WaitUntil("演奏按键时刻", () => !rhythm.IsPlaying || rhythm.SongSeconds >= edge.time, 8);
                if (!rhythm.IsPlaying) break;
                if (edge.press) yield return Input.Hold(rhythm.LaneAction(edge.lane)); else yield return Input.Release(rhythm.LaneAction(edge.lane));
            }
            yield return WaitUntil("本次演奏收尾", () => !rhythm.IsActivityOpen, 55);
            }
            yield return Check("演奏达标并正常回店", () => State.Theater.Completed && !rhythm.IsActivityOpen);
        }
    }
}
