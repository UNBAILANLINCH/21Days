// 职责：无战斗 NPC 的叙事点击入口；稳定 ID 进存档，运行时对象销毁/禁用即目标不可用。
// 不创建新输入系统：点击沿用 EventSystem 射线，程序生成对象通过 Configure 显式绑定。
// 交互键（E）与交互提示（PRP/turnbased-battle W2b）：同物体上挂了 DialogueInteractable 时，绑定即把交互转交过来——
//   焦点与底部「[E] 动词 · 名字」提示由统一交互（Game.Interaction）驱动、头顶「…/!」标记由 Dialogue 驱动，动词取
//   DialogueInteractable 的 verb 字段（BOSS 配「挑战」，PRP/interaction D10）；按 E / 点提示 / 点 NPC 走本组件的
//   InteractAsync（距离、生命周期守卫不变）。这时点击由 DialogueInteractable 接（它带沉浸模式判定），本组件不再重复响应。
//   没挂 DialogueInteractable 的目标与原来一样只有点击入口。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Dialogue;
using Game.Interaction;
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
        private InteractionActor actor;
        private CancellationTokenSource lifetime;
        private DialogueInteractable focusEntry;
        private Action handover;

        public string TargetId => targetId;
        public string TargetKind => targetKind;
        /// <summary>交互键 / 交互提示是否已经经同物体的 DialogueInteractable 转交到本组件。</summary>
        public bool HasFocusEntry => focusEntry != null;
        internal CancellationToken LifetimeToken => lifetime == null ? CancellationToken.None : lifetime.Token;

        public void Configure(string id, string kind, NarrativeService narrative, InteractionActor player)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("叙事目标配置不可为空");
            Unbind();
            targetId = id;
            targetKind = kind;
            Bind(narrative, player);
        }

        public void Bind(NarrativeService narrative, InteractionActor player)
        {
            Unbind();
            service = narrative ?? throw new ArgumentNullException(nameof(narrative));
            actor = player;
            service.Conditions.Register(this);
            lifetime = new CancellationTokenSource();
            // 绑定时取一次（不在每帧路径上）；转交的回调固定成一个实例，解绑时只撤自己那一份。
            if (TryGetComponent(out DialogueInteractable entry))
            {
                focusEntry = entry;
                handover ??= HandleHandover;
                entry.SetInteractionHandover(handover);
            }
        }

        public UniTask<bool> InteractAsync(CancellationToken ct = default)
        {
            // 服务不在可用状态（根作用域已销毁 / 退出 Play 时场景里的 NPC 还挂着旧绑定，或尚未初始化）：直接返回 false，
            // 不往已释放的服务上调——否则 TryEncounterAsync 会抛「尚未初始化或已释放」，经交互转交的 Forget() 变成未处理异常。
            if (!isActiveAndEnabled || service == null || !service.IsReady || actor == null ||
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
            // 已转交给 DialogueInteractable 时点击由它接（同一次点击两个处理器都会收到，避免触发两次）。
            if (focusEntry != null) return;
            if (eventData.button == PointerEventData.InputButton.Left) InteractAsync().Forget();
        }

        private void HandleHandover() => InteractAsync().Forget();

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
            if (focusEntry != null) focusEntry.ReleaseInteractionHandover(handover);
            focusEntry = null;
        }
    }
}
