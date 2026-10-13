// 职责：预摆角色的交互与显式身份元数据；通用交互不应认识四店职业。
using System;
using Game.Interaction;
using UnityEngine;
namespace Game.ShopPlaytest
{
    public sealed class ShopPlaytestActor : MonoBehaviour, IInteractable
    {
        [SerializeField] private string stableId;
        [SerializeField] private string role;
        [SerializeField] private string displayName;
        [SerializeField] private int shop;
        [SerializeField] private float borrowRadius = 2f;
        private Renderer[] renderers;
        private bool[] originalVisibility;
        private Action<ShopPlaytestActor> borrow;
        private Func<ShopPlaytestActor, bool> allowed;
        public string StableId => stableId;
        public string Role => role;
        public string DisplayName => displayName;
        public int Shop => shop;
        public bool IsVisible { get; private set; }
        public Vector3 Position => transform.position;
        public float InteractionRadius => borrowRadius;
        public int InteractionPriority => 0;
        public bool CanInteract => borrow != null && IsVisible && allowed != null && allowed(this);
        public InteractionPrompt Prompt => new InteractionPrompt(string.IsNullOrEmpty(role) ? "查看" : "借身份", displayName);
        public void SetVisible(bool visible)
        {
            if (renderers == null)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                originalVisibility = new bool[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) originalVisibility[i] = renderers[i].enabled;
            }
            IsVisible = visible;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = visible && originalVisibility[i];
        }
        public void Bind(Action<ShopPlaytestActor> handler, Func<ShopPlaytestActor, bool> canBorrow) { borrow = handler; allowed = canBorrow; }
        public void Interact() { if (CanInteract) borrow(this); }
        public void OnFocusChanged(bool focused) { }
    }
}
