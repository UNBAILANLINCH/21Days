// 职责：钉住战斗界面对暂停菜单的契约（PRP D7）——BattleView 在 Panel 层、CloseOnCancel = false，
//   于是栈顶是它时 UICancelRouter 判 Blocked：Esc 既不关战斗界面，也不抛「没东西可关」去开暂停菜单（P 键那一路由 BattleWorldLock 关 Gameplay 图挡住，
//   见 BattleFlowTests.NormalEnd_HoldsWorldDuringBattle_ThenRestoresEverything）。负对照：可关面板判 Close、没有面板判 NothingToClose。
// 为什么新建：BattleView 是 W2a 新增的 UIView；不改 Core 的 PauseMenuController（Core 不能认识 Game.Battle），挡暂停菜单全靠这条契约，要钉住。
//   不读预制体（unity-tests：测试不依赖具体资产路径），只挂组件读契约。
using Game.Battle;
using Game.Core.UI;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
        public void ResultArt_ReplacedByHint_RestoresEditableText()
        {
            host = new GameObject("BattleView", typeof(RectTransform));
            var view = host.AddComponent<BattleView>();
            var hint = new GameObject("Hint", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            hint.transform.SetParent(host.transform, false);
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(hint.transform, false);
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            backdrop.transform.SetParent(hint.transform, false);
            var title = new GameObject("Title", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            title.transform.SetParent(hint.transform, false);
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            try
            {
                void Bind(string field, object value) => typeof(BattleView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, value);
                Bind("hintGroup", hint.GetComponent<CanvasGroup>()); Bind("hintText", label);
                Bind("resultTitle", title); Bind("resultBackdrop", backdrop); Bind("victorySprite", sprite); Bind("defeatSprite", sprite);
                host.SetActive(false); // 无帧循环，验证状态切换；实际动画由回放覆盖。
                void ShowArt(string text) => typeof(BattleView).GetMethod("SetResultArt", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { text });
                ShowArt(BattleCueRules.VictoryText);
                Assert.That(title.gameObject.activeSelf, Is.True);
                Assert.That(label.enabled, Is.False, "固定美术不能与动态文字叠字");
                label.text = "跳过回合";
                ShowArt(null);
                Assert.That(title.gameObject.activeSelf, Is.False);
                Assert.That(backdrop.gameObject.activeSelf, Is.False);
                Assert.That(label.enabled, Is.True);
                Assert.That(hint.GetComponent<Image>().enabled, Is.True);
                Assert.That(view.HintText, Is.EqualTo("跳过回合"));
                ShowArt("回合用尽");
                Assert.That(title.gameObject.activeSelf, Is.False, "未知结果保留可编辑文案，不能误画胜败");
            }
            finally { Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); }
        }

        [Test]
        public void Router_NegativeControls()
        {
            Assert.That(UICancelRouter.Decide(true, true), Is.EqualTo(UICancelRouter.Decision.Close), "可关面板照常被 Esc 关");
            Assert.That(UICancelRouter.Decide(false, false), Is.EqualTo(UICancelRouter.Decision.NothingToClose), "没有面板时才轮到暂停菜单");
        }
    }
}
