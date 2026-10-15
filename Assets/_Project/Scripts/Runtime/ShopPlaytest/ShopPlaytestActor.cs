// 职责：预摆角色的交互与显式身份元数据；通用交互不应认识四店职业。
using System;
using Game.Interaction;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Game.ShopPlaytest
{
    public sealed class ShopPlaytestActor : MonoBehaviour, IInteractable, ISecondaryInteractable, IPointerClickHandler
    {
        [SerializeField] private string stableId;
        [SerializeField] private string role;
        [SerializeField] private string displayName;
        [SerializeField] private int shop;
        [SerializeField] private float borrowRadius = 2f;
        [SerializeField] private BoxCollider clickCollider;
        private Renderer[] renderers;
        private bool[] originalVisibility;
        private Action<ShopPlaytestActor> talk;
        private Action<ShopPlaytestActor> select;
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
        public bool CanInteract => talk != null && IsVisible && allowed != null && allowed(this);
        public InteractionPrompt Prompt => new InteractionPrompt("对话", displayName);
        public bool CanInteractSecondary => CanInteract && !string.IsNullOrEmpty(role) && borrow != null;
        public InteractionPrompt SecondaryPrompt => new InteractionPrompt("借用身份", displayName);
        public void SetVisible(bool visible)
        {
            if (renderers == null)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                originalVisibility = new bool[renderers.Length];
                for (int i = 0; i < renderers.Length; i++) originalVisibility[i] = renderers[i].enabled;
            }
            IsVisible = visible;
            if (clickCollider != null) clickCollider.enabled = visible;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = visible && originalVisibility[i];
        }
        public void Bind(Action<ShopPlaytestActor> handler, Func<ShopPlaytestActor, bool> canInteract,
            Action<ShopPlaytestActor> borrowIdentity = null, Action<ShopPlaytestActor> selectActor = null)
        { talk = handler; allowed = canInteract; borrow = borrowIdentity; select = selectActor; }
        public void Interact() { if (CanInteract) talk(this); }
        public void InteractSecondary() { if (CanInteractSecondary) borrow(this); }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !CanInteract) return;
            if (select != null) select(this);
            else Interact();
        }
        public void OnFocusChanged(bool focused) { }
    }
}
