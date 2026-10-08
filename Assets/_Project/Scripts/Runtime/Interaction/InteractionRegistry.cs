// 职责：统一交互登记表的实现——保存各模块登记进来的候选；启动时与每次 sceneLoaded 找出场景里的玩家标记（InteractionActor），
//   sceneUnloaded 时清掉已销毁的候选与玩家标记，玩家标记随场景没了就重扫一遍已加载场景。
// 为什么新建：原来「找玩家标记」在 DialogueSceneBinder 里，Loot / Narrative 借用它就得认识对白（PRP/interaction D1、D7）；
//   各模块的 SceneBinder 只扫自己模块的组件，塞进任何一个都会让别的模块反向依赖它。扫玩家标记只该有一处，放在 Interaction。
using System;
using System.Collections.Generic;
using Game.Core.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Game.Interaction
{
    /// <summary>
    /// 登记表入口点。只扫场景里摆好的玩家标记（含未激活的）；DontDestroyOnLoad 场景不在扫描范围内。
    /// 候选由各模块的场景登记器主动 <see cref="Register"/>，本类不认识任何具体的可交互类型。
    /// <para>
    /// 场景切换的时序不固定：流程层卸旧场景（Addressables 异步卸载，不等）后立刻加载新场景，新场景的 sceneLoaded
    /// 可能先于旧场景的 sceneUnloaded 到达——那时旧玩家标记还活着，新标记被跳过。所以旧标记随场景卸载变成伪空后，
    /// <see cref="HandleSceneUnloaded"/> 会重扫一遍已加载场景把新标记找回来（PRP/interaction 第二波 WARN 1）。
    /// </para>
    /// </summary>
    public sealed class InteractionRegistry : IInteractionRegistry, IStartable, IDisposable
    {
        private readonly List<IInteractable> candidates = new List<IInteractable>();
        private readonly Func<InteractionActor> scanLoadedScenes;
        private bool subscribed;

        /// <summary>生产用：玩家标记从 SceneManager 的已加载场景里扫。</summary>
        public InteractionRegistry() : this(null)
        {
        }

        /// <param name="loadedScenesScanner">
        /// 在全部已加载场景里找玩家标记的函数；为 null 时用 SceneManager 实扫。
        /// 测试注入假扫描：EditMode 下 SceneManager 扫到的是编辑器里开着的场景，结果不可控。
        /// </param>
        public InteractionRegistry(Func<InteractionActor> loadedScenesScanner)
        {
            scanLoadedScenes = loadedScenesScanner ?? ScanLoadedScenes;
        }

        public IReadOnlyList<IInteractable> Candidates => candidates;

        public InteractionActor Actor { get; private set; }

        public event Action<InteractionActor> OnActorChanged;

        public void Start()
        {
            InteractionActor found = scanLoadedScenes();
            if (found != null && Actor == null) SetActor(found);
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
        }

        public void Dispose()
        {
            if (subscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            candidates.Clear();
            Actor = null;
            OnActorChanged = null;
        }

        public void Register(IInteractable interactable)
        {
            if (interactable == null || IndexOf(interactable) >= 0) return;
            candidates.Add(interactable);
        }

        public void Unregister(IInteractable interactable)
        {
            int index = IndexOf(interactable);
            if (index >= 0) candidates.RemoveAt(index);
        }

        /// <summary>手动指定玩家标记（测试或运行时生成玩家时用）；与当前相同则不触发事件。</summary>
        public void SetActor(InteractionActor actor)
        {
            if (ReferenceEquals(Actor, actor)) return;
            Actor = actor;
            OnActorChanged?.Invoke(actor);
        }

        /// <summary>
        /// 场景卸载后的收尾（sceneUnloaded 回调调它）：移除已销毁的候选；玩家标记随场景销毁成伪空时，
        /// 重扫已加载场景——找到就换成新标记，找不到才置空；两种情况都只发一次 <see cref="OnActorChanged"/>。
        /// 公开是为了测试程序集可调（Game.Runtime 未对测试开 InternalsVisibleTo）。只在场景卸载时跑，不在每帧路径上。
        /// </summary>
        public void HandleSceneUnloaded()
        {
            // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                if (candidates[i] is UnityEngine.Object unityObject && unityObject == null) candidates.RemoveAt(i);
            }

            // 只处理「标记已销毁」（伪空）：ReferenceEquals 下伪空 != null，SetActor 会触发。真 null 说明本来就没有，不重扫。
            if (Actor != null || ReferenceEquals(Actor, null)) return;
            InteractionActor replacement = scanLoadedScenes();
            SetActor(replacement);
            if (replacement != null) Log.Debug($"InteractionRegistry：旧玩家标记随场景卸载，重扫找回 {replacement.name}");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => FindActor(scene);

        private void OnSceneUnloaded(Scene scene) => HandleSceneUnloaded();

        // 只在场景加载时跑一次。已有活着的玩家标记就不换（场景里应只有一个）。
        private void FindActor(Scene scene)
        {
            if (Actor != null) return;
            InteractionActor found = FindActorIn(scene);
            if (found == null) return;
            SetActor(found);
            Log.Debug($"InteractionRegistry：场景 {scene.name} 找到玩家标记 {found.name}");
        }

        // 生产用的全场景扫描：启动时与「玩家标记随场景卸载」后各跑一次。卸载回调里被卸的场景 isLoaded 已为 false，不会扫到它。
        private static InteractionActor ScanLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                InteractionActor found = FindActorIn(scene);
                if (found != null) return found;
            }
            return null;
        }

        private static InteractionActor FindActorIn(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                InteractionActor found = root.GetComponentInChildren<InteractionActor>(true);
                if (found != null) return found;
            }
            return null;
        }

        // 引用比较：Unity 对象的 Equals 会把两个已销毁对象判成相等，List.IndexOf 可能删错。
        private int IndexOf(IInteractable interactable)
        {
            if (interactable == null) return -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (ReferenceEquals(candidates[i], interactable)) return i;
            }
            return -1;
        }
    }
}
