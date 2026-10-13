// 职责：真实按钮走拨料、取消退款、点位纠错、打造与提交。
using System.Collections;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace Game.Tests.Showcase.Forging
{
    [Category("Showcase")]
    public sealed class ForgingShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Forging";
        [UnityTest]
        public IEnumerator Quota_CancelOrderedStrikesAndSubmit()
        {
            yield return Ready(2);
            yield return Borrow(0);
            yield return Step("官证修改拨料", () => Click("Command0"));
            yield return Borrow(1);
            yield return Step("匠人开始金钥匙", () => Click("Command1"));
            yield return Check("金钥匙材料已预留", () => State.Forging.IsForging && State.Forging.Available == 4);
            yield return Step("先错点位4，再取消工件", () => { Click("Command6"); Click("Command7"); });
            yield return Check("取消释放未完成工件额度，不产钥匙", () => State.Forging.Available == 8 && State.Forging.Keys == 0);
            yield return Step("按四点位顺序完成金钥匙", () => { Click("Command1"); Strike(); });
            yield return Check("金钥匙1把，材料只扣一次", () => State.Forging.Keys == 1 && State.Forging.Spent == 4);
            yield return Snapshot("金钥匙完成");
            yield return Step("打造规定首饰并提交", () => { Click("Command2"); Strike(); Click("Command8"); });
            yield return Check("首饰已提交，钥匙保留", () => State.Forging.Submitted && State.Forging.Keys == 1 && State.Forging.Ornaments == 0);
            yield return Snapshot("交付完成");
            yield return Step("重复提交", () => Click("Command8"));
            yield return Check("成果和一次扣料保留", () => State.Forging.Submitted && State.Forging.Spent == 6 && State.Forging.Keys == 1);
            yield return CheckPreplacedObjects();
        }
        private void Strike() { for (int i = 3; i <= 6; i++) Click("Command" + i); }
    }
}
