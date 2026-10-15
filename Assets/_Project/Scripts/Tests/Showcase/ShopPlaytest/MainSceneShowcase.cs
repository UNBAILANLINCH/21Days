// 职责：正常Boot验证按店显隐、当前控制人物出店、客栈对白与新局/跨槽恢复。
using System.Collections;
using Cysharp.Threading.Tasks;
using Game.Core.Input;
using Game.Dialogue;
using Game.Identity;
using Game.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Game.Tests.Showcase.ShopPlaytest
{
    [Category("Showcase")]
    public sealed class MainSceneShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "ShopPlaytest";
        [UnityTest]
        public IEnumerator Groups_InnDialogueAndControlledActorLeavingRemainPlayable()
        {
            yield return Ready(3);
            int local = VisibleRenderers();
            foreach (var actor in State.World.Actors) actor.SetVisible(true);
            int all = VisibleRenderers();
            State.World.Display(3, State.Taming.CurrentControlId);
            yield return Check("按店显隐确实减少启用的渲染对象", () => all > local && CorrectMask());
            Debug.Log("[VERIFY] 五店人物/操作物启用Renderer：全显=" + all + "，客栈分组=" + local + "；此计数不等同CPU/GPU耗时。");
            yield return Snapshot("客栈人物分组");
            yield return Command(0, false);
            var dialogue = ResolveService<DialogueService>(); var rules = ResolveService<DialogueRules>();
            var input = ResolveService<IInputService>();
            yield return Check("已有对白暂停和输入通路生效", () => dialogue.IsRunning, 5);
            float deadline = Time.realtimeSinceStartup + 25;
            while (dialogue.IsRunning && Time.realtimeSinceStartup < deadline)
            {
                if (rules.Phase == DialogueSaveData.Phase.AwaitChoice) yield return Input.Press(input.Actions.Dialogue.Choice1);
                else yield return Input.Press(input.Actions.Dialogue.Advance);
            }
            yield return Check("对白结束恢复街面操作", () => !dialogue.IsRunning, 5);
            yield return GoToShop(1);
            yield return Borrow(0);
            yield return MoveTo(State.ControlledPosition + MapDirection(Vector3.forward) * 5);
            yield return Check("借用人物出店仍可见、其它店人物隐藏", () => State.ActiveShop == -1 &&
                State.Taming.CurrentControlId == "shop_customer0" && CorrectMask(), 3);
            yield return Snapshot("人物出店继续控制");
            yield return CheckPreplacedObjects();
        }
        [UnityTest]
        public IEnumerator SavedProgress_NewSlotDoesNotReceiveOldSceneAndContinueRestoresOwnResult()
        {
            yield return Ready(1); yield return Borrow(1);
            yield return Command(4);
            yield return Check("旧槽成果成立", () => State.Clothing.Received);
            var session = ResolveService<GameSession>(); bool saved = false;
            yield return session.SaveNowAsync("shop_showcase").ContinueWith(ok => saved = ok).ToCoroutine();
            yield return Check("隔离槽1保存成功", () => saved);
            yield return Step("在旧现场开启隔离槽2", null, 0);
            yield return session.NewGameAsync(2).ToCoroutine();
            yield return Check("新槽没有旧领衣进度或旧店铺身份", () => !State.Clothing.Received &&
                State.Forging.Quota == 20 && State.CurrentRole == "" && !ResolveService<IdentityState>().IsInEffect, 8);
            yield return Step("从槽2读回隔离槽1", null, 0);
            yield return session.ContinueAsync(1).ToCoroutine();
            yield return Check("旧槽成果和原控制人物恢复，未被槽2卸载覆盖", () => State.Clothing.Received &&
                State.Taming.CurrentControlId == "shop_customer1" && State.CurrentRole == "customer1", 8);
            yield return Input.Press(ResolveService<IInputService>().Actions.Gameplay.Interact);
            yield return Check("读档后正式人物对白可重新打开", () => DialogueRunning, 4);
            yield return Choose("leave");
            yield return Snapshot("跨槽恢复衣物成果");
        }
        [UnityTest]
        public IEnumerator Retry_CurrentShopKeepsOtherShopsControlAndPosition()
        {
            yield return Ready(1); yield return Borrow(1);
            yield return Command(4);
            yield return Check("领衣成果已成立", () => State.Clothing.Received);
            yield return GoToShop(2); yield return Borrow(0);
            yield return Command(0);
            yield return Check("库存已补满", () => State.Forging.Quota == 24);
            Vector2 position = State.ControlledPosition; string control = State.Taming.CurrentControlId;
            yield return Step("仅调试接口重试本店", State.Retry); yield return FinishReplies();
            yield return Check("只重置金银行，不清衣肆、不切人、不传送", () => State.Forging.Quota == 20 &&
                State.Clothing.Received && State.Taming.CurrentControlId == control &&
                Vector2.Distance(State.ControlledPosition, position) < .25f);
            yield return Snapshot("局部重试保留其它店成果");
            yield return CheckPreplacedObjects();
        }
        private bool CorrectMask()
        {
            foreach (var actor in State.World.Actors)
                if (actor.IsVisible != (actor.Shop == State.ActiveShop || actor.StableId == State.Taming.CurrentControlId)) return false;
            return true;
        }
        private int VisibleRenderers()
        {
            int count = 0;
            foreach (var actor in State.World.Actors)
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true)) if (renderer.enabled) count++;
            return count;
        }
    }
}
