// 职责：无战斗 NPC 的叙事点击入口；稳定 ID 进存档，运行时对象销毁/禁用即目标不可用。
// 不创建新输入系统：点击沿用 EventSystem 射线，程序生成对象通过 Configure 显式绑定。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Dialogue;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Narrative
{
    public sealed class NarrativeTrigger : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField, Tooltip("跨存档稳定且场景内唯一的 NPC 标识。")]
        private string targetId = string.Empty;
        [SerializeField, Tooltip("对应 Narrative 遭遇表的 targetKind。")]
        private string targetKind = string.Empty;
        [SerializeField, Min(0.1f), Tooltip("玩家到 NPC 的最大交互距离。")]
        private float radius = 2f;
        private NarrativeService service;
        private DialogueInteractionActor actor;
        private CancellationTokenSource lifetime;

        public string TargetId => targetId;
        public string TargetKind => targetKind;
        internal CancellationToken LifetimeToken => lifetime == null ? CancellationToken.None : lifetime.Token;

        public void Configure(string id, string kind, NarrativeService narrative, DialogueInteractionActor player)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("叙事目标配置不可为空");
            Unbind();
            targetId = id;
            targetKind = kind;
            Bind(narrative, player);
        }

        public void Bind(NarrativeService narrative, DialogueInteractionActor player)
        {
            Unbind();
            service = narrative ?? throw new ArgumentNullException(nameof(narrative));
            actor = player;
            service.Conditions.Register(this);
            lifetime = new CancellationTokenSource();
        }

        public UniTask<bool> InteractAsync(CancellationToken ct = default)
        {
            if (!isActiveAndEnabled || service == null || actor == null ||
                (actor.Anchor.position - transform.position).sqrMagnitude > radius * radius) return UniTask.FromResult(false);
            return InteractBoundAsync(ct);
        }

        private async UniTask<bool> InteractBoundAsync(CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            return await service.TryEncounterAsync(new[] { new EncounterRules.Candidate
            {
                TriggerId = targetId, TriggerKind = "Interact", Context = service.Conditions.Snapshot(targetId),
            } }, linked.Token);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) InteractAsync().Forget();
        }

        private void OnDisable() => lifetime?.Cancel();
        private void OnEnable()
        {
            if (lifetime == null || !lifetime.IsCancellationRequested) return;
            lifetime.Dispose();
            lifetime = new CancellationTokenSource();
        }
        private void OnDestroy() => Unbind();
        private void Unbind()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            service?.Conditions.Unregister(this);
            service = null;
        }
    }
}
