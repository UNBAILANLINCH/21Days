// 职责：音效与玩法事实的表现接线；Core 不能认识玩法，现有规则和场景状态也不应拥有跨模块音频生命周期。
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Core.Assets;
using Game.Core.Audio;
using Game.Core.Boot;
using Game.Core.Timing;
using Game.Core.UI;
using Game.Identity;
using Game.Interaction;
using Game.Loot;
using Game.Monster;
using Game.Player;
using MessagePipe;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Game.Audio
{
    public sealed class GameplayAudio : IGameService, ITickable, ILateTickable, IDisposable
    {
        private static readonly string[] Keys = {
            "aware", "chase_start", "exposed", "action_denied", "keypoint_found",
            "possession_succeed", "possession_detach", "attack_stab_behind", "been_attacked",
            "knocked_down", "execute_succeed", "lowkey_intense_kicks", "lowkey_intense_kicks_faster",
            "walksteps", "runsteps", "silence_steps", "ui_select", "mirror_vibrate",
            "mirror_resonance_and_stop", "wind_start", "wind_loop", "wind_stop"
        };
        private readonly IObjectResolver container;
        private readonly Dictionary<string, AssetHandle<AudioClip>> clips = new Dictionary<string, AssetHandle<AudioClip>>();
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        private readonly Dictionary<MonsterRules, MonsterMode> modes = new Dictionary<MonsterRules, MonsterMode>();
        private readonly Dictionary<Button, UnityAction> buttons = new Dictionary<Button, UnityAction>();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private IAudioService audio;
        private ISfxVoiceService voices;
        private IClock clock;
        private AudioConfig config;
        private EncounterStep step;
        private PlayerRules player;
        private IdentityRules identity;
        private IInteractionRegistry registry;
        private IWorldPauseService pause;
        private UIService ui;
        private Game.Stealth.ExecutionInteractor execution;
        private Game.Narrative.SpotlightStoryFlow story;
        private readonly List<SfxVoice> mirrorVoices = new List<SfxVoice>();
        private SfxVoice danger, footsteps;
        private string dangerKey, footstepsKey;
        private Transform anchor;
        private Vector3 position;
        private bool ready, disposed;
        public GameplayAudio(IObjectResolver container) { this.container = container; }

        public async UniTask InitializeAsync(CancellationToken ct)
        {
            if (ready || disposed || !container.TryResolve(out audio) || !container.TryResolve(out voices)) return;
            container.TryResolve(out clock); container.TryResolve(out config);
            container.TryResolve(out step); container.TryResolve(out player);
            container.TryResolve(out identity); container.TryResolve(out registry);
            container.TryResolve(out pause); container.TryResolve(out ui);
            var assets = container.Resolve<IAssetService>();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token))
                foreach (string key in Keys)
                {
                    AssetHandle<AudioClip> handle = await assets.LoadAsync<AudioClip>("SFX/" + key, linked.Token);
                    if (linked.IsCancellationRequested) { handle.Dispose(); linked.Token.ThrowIfCancellationRequested(); }
                    clips.Add(key, handle);
                }
            ready = true;
            Application.quitting += Dispose;
            SceneManager.sceneLoaded += Loaded; SceneManager.sceneUnloaded += Unloaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) Loaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
            if (container.TryResolve(out story)) { story.OnMirrorCrossingStarted += MirrorStarted; story.OnMirrorCrossingFinished += MirrorFinished; }
            if (step != null)
            {
                step.OnAdvanced += Advanced; step.OnPresentationReset += Reset;
                step.Taming.OnControlChanged += ControlChanged;
            }
            if (player != null) { player.OnDamaged += Damaged; player.OnDied += Died; }
            if (identity != null)
            {
                identity.OnEntered += Entered; identity.OnExited += Exited;
                identity.OnExposed += Exposed; identity.OnRefused += Refused;
            }
            if (container.TryResolve(out ISubscriber<CrateCollectedEvent> collected)) subscriptions.Add(collected.Subscribe(_ => Reward()));
            if (container.TryResolve(out ISubscriber<BackpackChangedEvent> added)) subscriptions.Add(added.Subscribe(_ => Reward()));
            if (container.TryResolve(out ISubscriber<MonsterDroppedEvent> dropped)) subscriptions.Add(dropped.Subscribe(_ => Reward()));
            if (ui != null) { ui.OnViewOpened += Opened; ui.OnViewClosed += Closed; }
            Reset();
        }
        private void Reward() => Enqueue("keypoint_found");
        private void Entered(IdentityOrigin origin) { if (origin == IdentityOrigin.Possession) Enqueue("possession_succeed"); }
        private void Exited() => Enqueue("possession_detach");
        private void Exposed() => Enqueue("exposed");
        private void Refused() => Enqueue("action_denied");
        private void ControlChanged(string id)
        {
            if (step.IsActive) Enqueue(id == step.Taming.PlayerId ? "possession_detach" : "possession_succeed");
        }
        private void Damaged()
        {
            bool down = player.Model.Health == 0 || player.Knockdown != null && player.Knockdown.Phase != Game.Stealth.KnockdownPhase.None;
            Enqueue("been_attacked");
            if (down) Enqueue("knocked_down");
        }
        private void Died() => Enqueue("knocked_down");
        private void Hit() => Enqueue("attack_stab_behind");
        private void Executed() => Enqueue("execute_succeed");
        private void Loaded(Scene scene, LoadSceneMode mode)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (var interactor in root.GetComponentsInChildren<Game.Stealth.ExecutionInteractor>(true)) BindExecution(interactor);
        }
        private void Unloaded(Scene scene)
        {
            if (execution == null || execution.gameObject.scene == scene) BindExecution(null);
        }
        private void BindExecution(Game.Stealth.ExecutionInteractor interactor)
        {
            if (execution != null) execution.OnResolved -= ExecutionResolved;
            execution = interactor;
            if (execution != null) execution.OnResolved += ExecutionResolved;
        }
        private void ExecutionResolved(Game.Stealth.ExecutionVerdict verdict)
        {
            if (!verdict.Allowed && verdict.Reject != Game.Stealth.ExecutionReject.YieldedToInteraction) Refused();
        }
        private void MirrorStarted()
        {
            MirrorFinished(false);
            foreach (string key in new[] { "wind_start", "wind_loop", "mirror_vibrate" })
            {
                SfxVoice voice = PlayOwned(key, key == "wind_loop");
                if (voice != null) mirrorVoices.Add(voice);
            }
        }
        private void MirrorFinished(bool success)
        {
            foreach (SfxVoice voice in mirrorVoices) voice.Dispose();
            mirrorVoices.Clear();
            if (success) { Play("mirror_resonance_and_stop"); Play("wind_stop"); }
        }
        private void Enqueue(string key) { if (ready && !disposed && (step == null || !step.IsReplaying)) pending.Enqueue(key); }
        private void Reset()
        {
            StopLoops();
            foreach (MonsterRules target in modes.Keys) { target.OnDamaged -= Hit; target.OnExecuted -= Executed; }
            modes.Clear(); anchor = null;
            if (step != null && step.IsActive) { pending.Clear(); lastPlayed.Clear(); }
            if (disposed || step == null || !step.IsActive) return;
            foreach (string id in step.Taming.TargetIds)
            {
                MonsterRules target = step.Taming.GetTarget(id);
                modes[target] = target.Model.Mode;
                target.OnDamaged += Hit; target.OnExecuted += Executed;
            }
        }
        private void Advanced(float delta)
        {
            if (!ready || step.IsReplaying) return;
            foreach (string id in step.Taming.TargetIds)
            {
                MonsterRules target = step.Taming.GetTarget(id);
                if (!modes.TryGetValue(target, out MonsterMode previous)) continue;
                MonsterMode current = target.Model.Mode;
                if (!step.Taming.IsPassive(id) && !step.Taming.IsObservationOnly(id) && !step.Taming.IsTargetTamed(id) && step.Taming.IsAvailable(id))
                {
                    if (current == MonsterMode.Hostile && previous != current) Enqueue("chase_start");
                    else if (current == MonsterMode.Alert && previous != current && previous != MonsterMode.Hostile) Enqueue("aware");
                }
                modes[target] = current;
            }
        }
        public void Tick()
        {
            if (!ready || disposed) return;
            while (pending.Count > 0) Play(pending.Dequeue());
            if (step == null || player == null || !step.IsActive || step.IsEscalating || step.IsReplaying || player.Model.Health <= 0)
            { StopLoops(); return; }
            bool hostile = false;
            foreach (string id in step.Taming.TargetIds)
                if (step.Taming.IsAvailable(id) && !step.Taming.IsPassive(id) && !step.Taming.IsObservationOnly(id)
                    && !step.Taming.IsTargetTamed(id) && step.Taming.GetTarget(id).Model.Mode == MonsterMode.Hostile) hostile = true;
            string key = hostile ? (player.Model.Snapshot.IsRunning ? "lowkey_intense_kicks_faster" : "lowkey_intense_kicks") : null;
            SetLoop(ref danger, ref dangerKey, key, config == null ? 0.35f : config.DangerVolume);
            if (danger != null) danger.SetPaused(pause != null && pause.IsPaused);
        }
        public void LateTick()
        {
            if (!ready || disposed || registry == null || step == null || player == null) return;
            InteractionActor actor = registry.Actor;
            Transform current = actor == null ? null : actor.Anchor;
            bool changed = current != anchor;
            Vector3 next = current == null ? Vector3.zero : current.position;
            float distance = changed ? 0f : (next - position).sqrMagnitude;
            bool moving = current != null && current.gameObject.activeInHierarchy && !changed && distance > 0.000001f && distance < 2.25f
                && step.IsActive && !step.IsEscalating && !step.IsReplaying && player.Model.Health > 0 && (pause == null || !pause.IsPaused);
            string key = !moving ? null : player.Model.IsSneaking ? "silence_steps" : player.Model.Snapshot.IsRunning ? "runsteps" : "walksteps";
            SetLoop(ref footsteps, ref footstepsKey, key, config == null ? 0.6f : config.FootstepsVolume);
            anchor = current; position = next;
        }
        private void SetLoop(ref SfxVoice voice, ref string activeKey, string key, float volume)
        {
            if (activeKey == key) return;
            if (voice != null) voice.Dispose();
            voice = null; activeKey = key;
            if (key != null && clips.TryGetValue(key, out var handle)) voice = voices.PlayVoice(handle.Asset, true, volume);
        }
        private void StopLoops()
        {
            SetLoop(ref danger, ref dangerKey, null, 0f);
            SetLoop(ref footsteps, ref footstepsKey, null, 0f);
        }
        public void Play(string key)
        {
            if (!ready || disposed || !clips.TryGetValue(key, out var handle)) return;
            float now = clock == null ? 0f : clock.UnscaledTime;
            float gap = config == null ? 0.15f : config.FeedbackRetriggerSeconds;
            if (lastPlayed.TryGetValue(key, out float previous) && now - previous < gap) return;
            lastPlayed[key] = now;
            audio.PlaySfx(handle.Asset);
        }
        public SfxVoice PlayOwned(string key, bool loop) =>
            ready && !disposed && clips.TryGetValue(key, out var handle) ? voices.PlayVoice(handle.Asset, loop) : null;
        private void Opened(UIView view)
        {
            foreach (Button button in view.GetComponentsInChildren<Button>(true))
            {
                if (buttons.ContainsKey(button)) continue;
                UnityAction callback = () => Play("ui_select");
                buttons.Add(button, callback); button.onClick.AddListener(callback);
            }
        }
        private void Closed(UIView view)
        {
            foreach (Button button in view.GetComponentsInChildren<Button>(true))
                if (buttons.TryGetValue(button, out var callback)) { button.onClick.RemoveListener(callback); buttons.Remove(button); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel(); Reset();
            Application.quitting -= Dispose;
            SceneManager.sceneLoaded -= Loaded; SceneManager.sceneUnloaded -= Unloaded;
            BindExecution(null); MirrorFinished(false);
            if (story != null) { story.OnMirrorCrossingStarted -= MirrorStarted; story.OnMirrorCrossingFinished -= MirrorFinished; }
            if (step != null) { step.OnAdvanced -= Advanced; step.OnPresentationReset -= Reset; step.Taming.OnControlChanged -= ControlChanged; }
            if (player != null) { player.OnDamaged -= Damaged; player.OnDied -= Died; }
            if (identity != null) { identity.OnEntered -= Entered; identity.OnExited -= Exited; identity.OnExposed -= Exposed; identity.OnRefused -= Refused; }
            if (ui != null) { ui.OnViewOpened -= Opened; ui.OnViewClosed -= Closed; }
            foreach (var pair in buttons) if (pair.Key != null) pair.Key.onClick.RemoveListener(pair.Value);
            buttons.Clear();
            foreach (IDisposable subscription in subscriptions) subscription.Dispose();
            foreach (var handle in clips.Values) handle.Dispose();
            clips.Clear(); lifetime.Dispose();
        }
    }
}
