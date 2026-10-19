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
        public IEnumerator Guess_LearningSaveBattleAndLeaveReset()
        {
            yield return Ready(4);
            yield return Check("三店尚未完成，酒肆资格不足", () => !State.CanEnterTavern);
            // 聚焦酒肆的夹具；三店真实奖励链另由连续剧情回放完成。
            var loot = ResolveService<Game.Loot.LootService>();
            loot.GrantItems(ShopRewardRules.CostumeId, 1); loot.GrantItems(ShopRewardRules.ClothesId, 1);
            loot.GrantItems(ShopRewardRules.ClaspId, 1); loot.GrantItems(ShopRewardRules.BeltHookId, 2); loot.GrantItems(ShopRewardRules.RingId, 3);
            yield return Borrow(0);
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
            Vector3 station = State.World.Station(4).position;
            yield return MoveTo(new Vector2(station.x - 7, station.z));
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
