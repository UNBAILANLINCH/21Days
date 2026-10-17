// 职责：从人物对白进入圆盘界面，验证真实按钮、取消退款、点位纠错和关闭恢复。
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Forging;
using Game.Session;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
namespace Game.Tests.Showcase.Forging
{
    [Category("Showcase")]
    public sealed class ForgingShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Forging";
        [UnityTest]
        public IEnumerator ABC_CancelOrderedStrikesRefillAndSubmit()
        {
            yield return Ready(2);
            yield return OpenWorkbench(false);
            yield return Check("没有匠人身份不能打开打造界面", () => View == null);
            yield return Borrow(1);
            yield return OpenWorkbench();
            yield return Step("金属额度、圆盘四点和右侧工件", null);
            yield return Check("圆盘恰好四点，未达数量不能提交", () => View.transform.Find("WorkbenchPanel/FourPointDisk").childCount == 4 && !Button("SubmitButton").interactable);
            yield return Snapshot("金银行圆盘打造界面");
            yield return Click("CraftButton");
            yield return Check("物品A预留5金属，显示15/24", () => State.Forging.IsForging && State.Forging.Available == 15);
            yield return Click("Point1");
            yield return Check("点错仅显示提示，不推进不扣料", () => State.Forging.Progress == 0 && State.Forging.Spent == 0 &&
                View.transform.Find("Message").GetComponent<TMP_Text>().text.Contains("点位不符"));
            yield return Click("Point0"); yield return Click("CancelWorkpieceButton");
            yield return Check("取消释放5金属，不产物品A", () => State.Forging.Available == 20 && State.Forging.Count(0) == 0);
            yield return Click("CraftButton"); yield return Strike();
            yield return Check("A完成1件且只扣5金属", () => State.Forging.Count(0) == 1 && State.Forging.Spent == 5 && State.Forging.Available == 15);
            yield return Snapshot("物品A完成");
            yield return Click("Product1");
            yield return Click("CraftButton"); yield return Strike(); yield return Click("CraftButton"); yield return Strike();
            yield return Click("Product2"); yield return Click("CraftButton"); yield return Strike();
            yield return Check("库存不足时不能打造或提交，ABC进度1/1、2/2、1/3", () => State.Forging.Available == 0 && State.Forging.Spent == 20 &&
                !Button("CraftButton").interactable && !Button("SubmitButton").interactable && State.Forging.Count(1) == 2 && State.Forging.Count(2) == 1);
            yield return Snapshot("库存不足提示官证补料");
            yield return Click("BackButton"); yield return WaitClosed();
            yield return Borrow(0); yield return Command(0);
            yield return Check("官证补库存至24，累计消耗仍20", () => State.Forging.Available == 24 && State.Forging.Spent == 20);
            yield return Borrow(1); yield return OpenWorkbench(); yield return Click("Product2");
            yield return Click("CraftButton"); yield return Strike(); yield return Click("CraftButton"); yield return Strike();
            yield return Check("ABC数量达标，累计消耗30超过库存上限，提交可用", () => State.Forging.Count(0) == 1 && State.Forging.Count(1) == 2 &&
                State.Forging.Count(2) == 3 && State.Forging.Spent == 30 && State.Forging.Available == 14 && Button("SubmitButton").interactable);
            yield return Snapshot("ABC达标可提交");
            yield return Click("SubmitButton");
            yield return Check("ABC已提交且三个数量清零", () => State.Forging.Submitted && State.Forging.Count(0) + State.Forging.Count(1) + State.Forging.Count(2) == 0);
            yield return Check("金饰按1、2、3件正式入包", () => {
                var items = ResolveService<Game.Loot.LootService>().Items;
                return items[Game.ShopPlaytest.ShopRewardRules.ClaspId] == 1 && items[Game.ShopPlaytest.ShopRewardRules.BeltHookId] == 2 && items[Game.ShopPlaytest.ShopRewardRules.RingId] == 3;
            });
            yield return Snapshot("交付完成");
            yield return Check("完成后不能重复打造或提交", () => !Button("CraftButton").interactable && !Button("SubmitButton").interactable && State.Forging.Spent == 30);
            yield return Click("BackButton"); yield return WaitClosed();
            yield return CheckPreplacedObjects();
        }
        [UnityTest]
        public IEnumerator Workbench_CloseSaveRestoreAndEscKeepsUnfinishedProduct()
        {
            yield return Ready(2); yield return Borrow(1);
            yield return OpenWorkbench(); yield return Click("CraftButton"); yield return Click("Point0");
            yield return Step("未完成工件返回街面，保留进度", null);
            yield return Click("BackButton"); yield return WaitClosed();
            yield return Check("关闭释放暂停和玩法输入，保留工件", () => !ResolveService<IWorldPauseService>().IsPaused &&
                ResolveService<IInputService>().Actions.Gameplay.enabled && State.Forging.Progress == 1 && State.Forging.Available == 15);
            bool saved = false;
            yield return ResolveService<GameSession>().SaveNowAsync("forging_showcase").ContinueWith(ok => saved = ok).ToCoroutine();
            yield return Check("隔离存档保存成功", () => saved);
            yield return ResolveService<GameSession>().ContinueAsync(1).ToCoroutine();
            yield return WaitCurtainRevealed(); yield return OpenWorkbench();
            yield return Check("读档后恢复第二点和预留额度", () => State.Forging.NextPoint == 1 && State.Forging.Available == 15 &&
                View.transform.Find("WorkbenchPanel/Progress").GetComponent<TMP_Text>().text.Contains("高亮点 2"));
            yield return Snapshot("未完成工件读档恢复");
            yield return Input.Press(ResolveService<IInputService>().Actions.UI.Cancel); yield return WaitClosed();
            Vector2 before = State.ControlledPosition;
            yield return Walk(MapDirection(Vector3.forward), .25f);
            yield return Check("Esc关闭后原控制人物可以继续移动", () => View == null && !ResolveService<IWorldPauseService>().IsPaused &&
                Vector2.Distance(State.ControlledPosition, before) > .15f && State.Forging.Progress == 1);
            yield return Snapshot("关闭打造台恢复街面");
            yield return Step("打造界面打开时切换到隔离新局", null);
            yield return OpenWorkbench();
            yield return ResolveService<GameSession>().NewGameAsync(2).ToCoroutine();
            yield return Check("切局关闭界面并释放暂停，新槽没有旧工件", () => View == null &&
                !ResolveService<IWorldPauseService>().IsPaused && ResolveService<IInputService>().Actions.Gameplay.enabled &&
                !State.Forging.IsForging && State.Forging.Quota == 20 && State.CurrentRole == "", 8);
            yield return Snapshot("打造界面切局清理");
        }
        private ForgingView View => ResolveService<IUIService>().Get<ForgingView>();
        private Button Button(string name) => FindDeep<Button>(View.transform, name);
        private IEnumerator OpenWorkbench(bool allowed = true)
        {
            Game.ShopPlaytest.ShopPlaytestActor station = null;
            foreach (var actor in State.World.Actors) if (actor.StableId == "shop_station_2") station = actor;
            yield return OpenConversation(station);
            if (!allowed)
            {
                yield return ShowChoices();
                Assert.That(System.Array.Exists(ResolveService<Game.Dialogue.DialogueRules>().Current.Choices,
                    choice => choice.Id == "forge"), Is.False, "本体没有匠人打造选项");
                yield return Choose("leave");
                yield break;
            }
            yield return Choose("forge");
            yield return WaitUntil("打造界面可操作", () => View != null && View.GetComponent<CanvasGroup>().interactable && View.GetComponent<CanvasGroup>().alpha >= .99f, 5);
        }
        private IEnumerator Click(string name)
        {
            Button button = Button(name);
            Assert.That(button, Is.Not.Null, name);
            Assert.That(button.interactable, Is.True, name + "应可操作");
            button.onClick.Invoke(); yield return null;
        }
        private IEnumerator WaitClosed() => WaitUntil("打造界面收尾恢复街面", () => View == null && !ResolveService<IWorldPauseService>().IsPaused, 5);
        private IEnumerator Strike() { while (State.Forging.IsForging) yield return Click("Point" + State.Forging.NextPoint); }
    }
}
