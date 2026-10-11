// 职责：钉住战斗界面对暂停菜单的契约（PRP D7）——BattleView 在 Panel 层、CloseOnCancel = false，
//   于是栈顶是它时 UICancelRouter 判 Blocked：Esc 既不关战斗界面，也不抛「没东西可关」去开暂停菜单（P 键那一路由 BattleWorldLock 关 Gameplay 图挡住，
//   见 BattleFlowTests.NormalEnd_HoldsWorldDuringBattle_ThenRestoresEverything）。负对照：可关面板判 Close、没有面板判 NothingToClose。
// 为什么新建：BattleView 是 W2a 新增的 UIView；不改 Core 的 PauseMenuController（Core 不能认识 Game.Battle），挡暂停菜单全靠这条契约，要钉住。
//   不读预制体（unity-tests：测试不依赖具体资产路径），只挂组件读契约。
using Game.Battle;
using Game.Core.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Battle
{
    public sealed class BattleViewContractTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void BattleView_IsPanelThatEscCannotClose()
        {
            host = new GameObject("BattleView", typeof(RectTransform));
            var view = host.AddComponent<BattleView>();

            Assert.That(view.Layer, Is.EqualTo(UILayer.Panel));
            Assert.That(view.CloseOnCancel, Is.False);
            Assert.That(UICancelRouter.Decide(hasTop: true, topCloseOnCancel: view.CloseOnCancel), Is.EqualTo(UICancelRouter.Decision.Blocked),
                "栈顶是战斗界面：Esc 被挡下，不开暂停菜单");
        }

        [Test]
        public void Router_NegativeControls()
        {
            Assert.That(UICancelRouter.Decide(true, true), Is.EqualTo(UICancelRouter.Decision.Close), "可关面板照常被 Esc 关");
            Assert.That(UICancelRouter.Decide(false, false), Is.EqualTo(UICancelRouter.Decision.NothingToClose), "没有面板时才轮到暂停菜单");
        }
    }
}
