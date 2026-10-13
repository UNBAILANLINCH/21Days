// 职责：真实试玩按钮验证借身份、错误推理、唯一领衣及当前店重试。
using System.Collections;
using Game.Tests.Showcase.ShopPlaytest;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace Game.Tests.Showcase.Clothing
{
    [Category("Showcase")]
    public sealed class ClothingShowcase : ShopPlaytestShowcase
    {
        protected override string Module => "Clothing";
        [UnityTest]
        public IEnumerator Investigate_WrongClaimThenCorrectClaimAndRetry()
        {
            yield return Ready(1);
            yield return Borrow(0);
            yield return Step("查看甲证词后尝试领衣", () => { Click("Command0"); Click("Command3"); });
            yield return Check("甲不符合掌柜线索，没有发放衣物", () => State.Clothing.HasInvestigated(0) && !State.Clothing.Received);
            yield return Snapshot("错误答案拒绝");
            yield return Borrow(2);
            yield return Step("查看丙证词", () => Click("Command2"));
            yield return Borrow(1);
            yield return Step("查看订金线索并领衣", () => { Click("Command1"); Click("Command4"); });
            yield return Check("衣物领取完成，场景成果出现", () => State.Clothing.Received);
            yield return Snapshot("领衣完成");
            yield return Step("重看已调查证词并重复领衣", () => { Click("Command0"); Click("Command4"); });
            yield return Check("调查和成果保留，重复领取拒绝", () => State.Clothing.Received && State.Clothing.HasInvestigated(0));
            yield return CheckPreplacedObjects();
        }
    }
}
