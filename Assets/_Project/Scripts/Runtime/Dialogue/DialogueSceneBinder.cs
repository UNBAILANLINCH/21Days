// 职责：把 DialogueService 注入场景里的每个 DialogueInteractable——启动时扫已加载场景，之后每加载一个场景扫一次；
//   同时登记场景里的全部可交互物体（Bound）与玩家标记（Actor），供交互焦点系统逐帧挑选，免得它每帧 Find。
// 为什么新建：DialogueInteractable 是场景物体，不在根容器里，不能构造注入；Core 的 GameplayInstaller 只注册类型、不扫场景；
//   VContainer 的场景自动注入要求每个玩法场景各挂一个子作用域，而对白服务在根作用域，没必要为此给每个场景加作用域。
//   登记表放这里而不是焦点系统：扫场景只该有一处，焦点系统只读结果。
//   沉浸模式（HudVisibilityChangedEvent）也在这里统一下发到已登记物体：只有 Binder 手里有全量登记表，免得每个 NPC 各自订阅或每帧 Find。
using System;
using System.Collections.Generic;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.UI;
using MessagePipe;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Dialogue
{
    /// <summary>
    /// 场景绑定入口点。只处理场景里摆好的物体（含未激活的）；**运行时 Instantiate 出来的 DialogueInteractable 要自行调 Bind**，
    /// 且不会进 <see cref="Bound"/>（不参与范围焦点）。DontDestroyOnLoad 场景不在扫描范围内。
    /// </summary>
    public sealed class DialogueSceneBinder : IStartable, IDisposable
    {
        private readonly DialogueService service;
        private readonly IHudVisibility hudVisibility;
        private readonly ISubscriber<HudVisibilityChangedEvent> hudChanged;
        private readonly List<DialogueInteractable> bound = new List<DialogueInteractable>();
        private IDisposable hudSubscription;
        private bool subscribed;

        public DialogueSceneBinder(DialogueService service, IHudVisibility hudVisibility,
            ISubscriber<HudVisibilityChangedEvent> hudChanged)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.hudVisibility = hudVisibility ?? throw new ArgumentNullException(nameof(hudVisibility));
            this.hudChanged = hudChanged ?? throw new ArgumentNullException(nameof(hudChanged));
        }

        /// <summary>已加载场景里登记的全部可交互物体（含无对话树、只说常驻台词的）。场景卸载时移除已销毁项。</summary>
        public IReadOnlyList<DialogueInteractable> Bound => bound;

        /// <summary>场景里的玩家标记；没有或所在场景已卸载时为 null。</summary>
        public DialogueInteractionActor Actor { get; private set; }

        public void Start()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) BindScene(scene);
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            hudChanged.Subscribe(e => ApplyHudHidden(e.Hidden)).AddTo(bag);
            hudSubscription = bag.Build();
            subscribed = true;
        }

        public void Dispose()
        {
            if (!subscribed) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (hudSubscription != null)
            {
                hudSubscription.Dispose();
                hudSubscription = null;
            }
            subscribed = false;
            bound.Clear();
            Actor = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene(scene);

        // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
        private void OnSceneUnloaded(Scene scene)
        {
            for (int i = bound.Count - 1; i >= 0; i--)
            {
                if (bound[i] == null) bound.RemoveAt(i);
            }
            if (Actor == null)
            {
                // 玩家所在场景卸载后 Actor 是伪空，显式置空并清掉各物体上的测距角色。
                Actor = null;
                ApplyActor();
            }
        }

        // 只在场景加载时跑一次，不在每帧路径上。
        private void BindScene(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (DialogueInteractable interactable in root.GetComponentsInChildren<DialogueInteractable>(true))
                {
                    // 无对话树的物体不需要服务，只登记不绑定。
                    if (interactable.HasTree) interactable.Bind(service);
                    if (!bound.Contains(interactable)) bound.Add(interactable);
                    count++;
                }
                if (Actor == null)
                {
                    DialogueInteractionActor found = root.GetComponentInChildren<DialogueInteractionActor>(true);
                    if (found != null) Actor = found;
                }
            }
            ApplyActor();
            // 沉浸中加载的新场景：新登记的物体同样隐藏。
            ApplyHudHidden(hudVisibility.IsHudHidden);
            if (count > 0) Log.Debug($"DialogueSceneBinder：场景 {scene.name} 登记了 {count} 个可交互对白物体，玩家标记 {(Actor == null ? "无" : Actor.name)}");
        }

        // 事件驱动 + 场景加载时各一次，不在每帧路径上。
        private void ApplyHudHidden(bool hidden)
        {
            for (int i = 0; i < bound.Count; i++)
            {
                if (bound[i] != null) bound[i].SetHiddenByHud(hidden);
            }
        }

        private void ApplyActor()
        {
            Transform anchor = Actor == null ? null : Actor.Anchor;
            for (int i = 0; i < bound.Count; i++)
            {
                if (bound[i] != null) bound[i].SetSceneActor(anchor);
            }
        }
    }
}
