// 职责：把角色起墨接入现有加载黑幕；全遮后等 LoadingCurtain 的真实就绪揭幕调用。
// 扩展现有预览的受控 API，跨场景常驻于 LoadingView，不修改叙事或世界切换状态机。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.UI;
using Game.Monster;
using UnityEngine;
using UnityEngine.UI;

namespace Game.InkTransition
{
    [DisallowMultipleComponent]
    public sealed class InkLoadingPresentation : MonoBehaviour, ILoadingTransitionPresentation
    {
        [SerializeField] private InkTransitionPreview preview;
        [SerializeField] private Image backdrop;

        private sealed class Playback
        {
            public readonly UniTaskCompletionSource Covered = new UniTaskCompletionSource();
            public readonly UniTaskCompletionSource Revealed = new UniTaskCompletionSource();

            public void Cancel()
            {
                Covered.TrySetCanceled();
                Revealed.TrySetCanceled();
            }
        }

        private Playback current;

        public async UniTask CoverAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            HideImmediate();
            if (preview == null || backdrop == null) throw new InvalidOperationException("加载水墨缺少预览或黑底引用");
            EncounterSceneView[] views = FindObjectsOfType<EncounterSceneView>();
            preview.BindCurrentActor(views.Length == 1 ? views[0] : null);
            var playback = new Playback();
            current = playback;
            backdrop.enabled = false;
            if (!preview.BeginCover(() => playback.Covered.TrySetResult(), () => playback.Revealed.TrySetResult(), playback.Cancel))
            {
                HideImmediate();
                throw new InvalidOperationException("加载水墨未初始化，不能开始盖幕");
            }

            try
            {
                await playback.Covered.Task.AttachExternalCancellation(ct);
            }
            catch
            {
                if (current == playback) HideImmediate();
                throw;
            }
        }

        public async UniTask RevealAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Playback playback = current;
            if (playback == null) return;
            if (preview == null || !preview.RevealAfterReady())
            {
                HideImmediate();
                return;
            }

            try
            {
                await playback.Revealed.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                if (current == playback) HideImmediate();
            }
        }

        public void HideImmediate()
        {
            Playback playback = current;
            current = null;
            if (preview != null) preview.StopPreview();
            playback?.Cancel();
            if (backdrop != null) backdrop.enabled = true;
        }

        private void OnDisable() => HideImmediate();
    }
}
