// 职责：金银行圆盘打造界面的数据显示和点击意图；不直接修改配方或发放物品。
// 新建原因：旧五店面板是集中调试按钮，扩展会继续混用人物操作；复用UIView生命周期，另建专用打造视图。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Forging
{
    public sealed class ForgingView : UIView
    {
        [SerializeField] private TMP_Text quotaLabel;
        [SerializeField] private TMP_Text materialLabel;
        [SerializeField] private TMP_Text progressLabel;
        [SerializeField] private TMP_Text messageLabel;
        [SerializeField] private TMP_Text costLabel;
        [SerializeField] private TMP_Text[] productLabels;
        [SerializeField] private Button[] productButtons;
        [SerializeField] private Button[] pointButtons;
        [SerializeField] private Button craftButton;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text[] productNameLabels;
        [SerializeField] private Sprite idlePointSprite;
        [SerializeField] private Sprite nextPointSprite;
        [SerializeField] private Image[] productSelections;
        [SerializeField] private Color nextPointColor = new Color(1f, .76f, .25f);
        [SerializeField] private Color idlePointColor = new Color(.55f, .63f, .68f);
        private ForgingController controller;
        private Func<ForgingAction, int, string> apply;
        private UniTaskCompletionSource closed;
        private int selected;
        public override UILayer Layer => UILayer.Panel;
        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            if (!(arg is ValueTuple<ForgingController, Func<ForgingAction, int, string>> session) ||
                session.Item1 == null || session.Item2 == null || productButtons == null || productButtons.Length != 3 ||
                productLabels == null || productLabels.Length != 3 || pointButtons == null || pointButtons.Length != 4)
                throw new ArgumentException("打造界面参数或预制体接线不完整");
            controller = session.Item1; apply = session.Item2;
            closed = new UniTaskCompletionSource();
            selected = controller.Rules.IsForging ? controller.Rules.CurrentProduct : 0;
            Hook(productButtons[0], SelectA); Hook(productButtons[1], SelectB);
            Hook(productButtons[2], SelectC);
            Hook(pointButtons[0], Strike0); Hook(pointButtons[1], Strike1);
            Hook(pointButtons[2], Strike2); Hook(pointButtons[3], Strike3);
            Hook(craftButton, Craft); Hook(submitButton, Submit); Hook(cancelButton, Cancel); Hook(backButton, Back);
            messageLabel.text = controller.Rules.Submitted ? "金饰已提交。" : "选择右侧工件，点击打造，再按高亮顺序点击圆盘点位。";
            OnRefresh();
            return UniTask.CompletedTask;
        }
        public override void OnRefresh()
        {
            if (controller == null) return;
            ForgingRules rules = controller.Rules;
            if (rules.IsForging) selected = rules.CurrentProduct;
            quotaLabel.text = $"{rules.Available}/{controller.MaximumQuota}";
            materialLabel.text = $"库存 {rules.Quota}\n累计消耗 {rules.Spent}\n预留 {rules.Reserved}";
            for (int i = 0; i < productLabels.Length; i++)
            {
                string name = controller.ProductName(i);
                bool separateName = productNameLabels != null && i < productNameLabels.Length && productNameLabels[i] != null;
                if (separateName) productNameLabels[i].text = name;
                string count = rules.Submitted ? "已提交" : $"{rules.Count(i)}/{rules.Required(i)}";
                productLabels[i].text = separateName ? count : $"{name}\n{count}";
                if (productSelections != null && i < productSelections.Length && productSelections[i] != null)
                    productSelections[i].enabled = selected == i;
            }
            foreach (Button button in productButtons) button.interactable = !rules.IsForging && !rules.Submitted;
            for (int i = 0; i < pointButtons.Length; i++)
            {
                pointButtons[i].interactable = rules.IsForging && !rules.Submitted;
                bool next = rules.IsForging && rules.NextPoint == i;
                bool useArt = idlePointSprite != null && nextPointSprite != null;
                if (useArt) pointButtons[i].image.sprite = next ? nextPointSprite : idlePointSprite;
                pointButtons[i].image.color = useArt ? Color.white : next ? nextPointColor : idlePointColor;
            }
            progressLabel.text = rules.Submitted ? "打造任务已完成" : rules.IsForging
                ? $"{controller.ProductName(selected)} · {rules.Progress}/{rules.RequiredStrikes} · 点击高亮点 {rules.NextPoint + 1}"
                : rules.CanSubmit ? "金饰数量已完成，可以提交" : rules.Available < rules.ProductCost
                    ? "金属不足，请返回街面找官证补料" : "选择工件后开始打造";
            int cost = rules.ProductCost;
            costLabel.text = $"{cost} 金属";
            craftButton.interactable = !rules.Submitted && !rules.IsForging && rules.Available >= cost && rules.Count(selected) < rules.Required(selected);
            cancelButton.interactable = rules.IsForging && !rules.Submitted;
            submitButton.interactable = rules.CanSubmit;
        }
        private void SelectA() { selected = 0; OnRefresh(); }
        private void SelectB() { selected = 1; OnRefresh(); }
        private void SelectC() { selected = 2; OnRefresh(); }
        private void Craft() => Execute(ForgingAction.StartProduct, selected);
        private void Submit() => Execute(ForgingAction.Submit);
        private void Cancel() => Execute(ForgingAction.Cancel);
        private void Strike0() => Execute(ForgingAction.Strike, 0);
        private void Strike1() => Execute(ForgingAction.Strike, 1);
        private void Strike2() => Execute(ForgingAction.Strike, 2);
        private void Strike3() => Execute(ForgingAction.Strike, 3);
        private void Execute(ForgingAction action, int value = 0)
        {
            if (apply == null || closed == null || closed.Task.Status != UniTaskStatus.Pending) return;
            messageLabel.text = apply(action, value);
            OnRefresh();
        }
        private void Back() => closed?.TrySetResult();
        public UniTask WaitAsync(CancellationToken ct) => closed.Task.AttachExternalCancellation(ct);
        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            if (productButtons != null && productButtons.Length == 3)
            { Unhook(productButtons[0], SelectA); Unhook(productButtons[1], SelectB); Unhook(productButtons[2], SelectC); }
            if (pointButtons != null && pointButtons.Length == 4)
            {
                Unhook(pointButtons[0], Strike0); Unhook(pointButtons[1], Strike1);
                Unhook(pointButtons[2], Strike2); Unhook(pointButtons[3], Strike3);
            }
            Unhook(craftButton, Craft); Unhook(submitButton, Submit); Unhook(cancelButton, Cancel); Unhook(backButton, Back);
            closed?.TrySetResult(); controller = null; apply = null;
            return UniTask.CompletedTask;
        }
    }
}
