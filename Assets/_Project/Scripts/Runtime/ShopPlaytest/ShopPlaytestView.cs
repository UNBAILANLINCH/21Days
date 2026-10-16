// 职责：主场景店前操作与反馈，开发版本可单独重试当前店铺。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace Game.ShopPlaytest
{
    public sealed class ShopPlaytestView : UIView
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text status;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Button[] shopButtons;
        [SerializeField] private Button[] roleButtons;
        [SerializeField] private TMP_Text[] roleLabels;
        [SerializeField] private Button[] commandButtons;
        [SerializeField] private TMP_Text[] commandLabels;
        [SerializeField] private Button bodyButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button backButton;
        private UnityAction[] shops, roles, commands;
        public override UILayer Layer => UILayer.Hud;
        public event Action<int> OnRole;
        public event Action<int> OnCommand;
        public event Action OnBody;
        public event Action OnRetry;
        public event Action OnBack;
        public override UniTask OnOpenAsync(object arg, CancellationToken ct)
        {
            foreach (Button button in shopButtons) button.gameObject.SetActive(false);
            retryButton.gameObject.SetActive(Debug.isDebugBuild);
            retryButton.GetComponentInChildren<TMP_Text>(true).text = "重试本店（调试）";
            backButton.gameObject.SetActive(false);
            roles = Bind(roleButtons, i => OnRole?.Invoke(i));
            commands = Bind(commandButtons, i => OnCommand?.Invoke(i));
            Hook(bodyButton, Body); Hook(retryButton, Retry); Hook(backButton, Back);
            return UniTask.CompletedTask;
        }
        public override UniTask OnCloseAsync(CancellationToken ct)
        {
            Unbind(roleButtons, roles); Unbind(commandButtons, commands);
            Unhook(bodyButton, Body); Unhook(retryButton, Retry); Unhook(backButton, Back);
            return UniTask.CompletedTask;
        }
        public void Setup(string heading, string[] people, string[] operations)
        {
            title.text = heading;
            for (int i = 0; i < roleButtons.Length; i++)
            { roleButtons[i].gameObject.SetActive(i < people.Length); if (i < people.Length) roleLabels[i].text = "借 " + people[i]; }
            for (int i = 0; i < commandButtons.Length; i++)
            { commandButtons[i].gameObject.SetActive(i < operations.Length); if (i < operations.Length) commandLabels[i].text = operations[i]; }
        }
        public void Status(string text) => status.text = text;
        public void Message(string text) => message.text = text;
        public void HighlightCommand(int index)
        { for (int i = 0; i < commandLabels.Length; i++) commandLabels[i].color = i == index ? Color.yellow : Color.white; }
        private static UnityAction[] Bind(Button[] buttons, Action<int> handler)
        {
            var result = new UnityAction[buttons.Length];
            for (int i = 0; i < buttons.Length; i++) { int index = i; result[i] = () => handler(index); buttons[i].onClick.AddListener(result[i]); }
            return result;
        }
        private static void Unbind(Button[] buttons, UnityAction[] handlers)
        { if (handlers == null) return; for (int i = 0; i < buttons.Length; i++) buttons[i].onClick.RemoveListener(handlers[i]); }
        private void Body() => OnBody?.Invoke();
        private void Retry() => OnRetry?.Invoke();
        private void Back() => OnBack?.Invoke();
    }
}
