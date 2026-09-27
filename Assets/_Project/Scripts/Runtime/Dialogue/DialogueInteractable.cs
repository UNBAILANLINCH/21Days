// 职责：场景里可交互的对白物体——有对话树时按范围与占用判定后调用 DialogueService.PlayAsync；
//   没有对话树（dialogueId == 0）但配了常驻台词时，按顺序抛出一句给头顶气泡（不暂停世界、不切输入图、不开面板）。
// 为什么新建：DialogueService 是纯 C# 服务，场景物体需要一个 MonoBehaviour 承载「对白 id / 交互半径 / 点击入口」；
//   现有 Dialogue 目录里没有挂在场景物体上的组件可扩展（DialogueView 是 UI 面板，职责不同）。
//   常驻台词加在这里而不是另起组件：它和对话树共用同一个交互入口（点击 / 焦点交互键 / 范围判定），拆开会有两套入口。
// 点击路径依赖：场景相机上挂 PhysicsRaycaster（3D 碰撞体）或 Physics2DRaycaster（Collider2D），本物体带对应碰撞体；
//   EventSystem 由 UIService 创建（UI 动作图已显式绑定），DialogueService 由 Boot 场景的 DialogueSceneBinder 注入。
//   直接 Play 玩法场景（不经 Boot）时二者都不存在：点击不会被派发，有对话树的物体在 Start 时记一条 Warn 提示。
//   缺任一项 OnPointerClick 都不会被调用，且不报错——此时仍可由玩法代码直接调 Interact()。
using System;
using Cysharp.Threading.Tasks;
using Game.Core.Logging;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Dialogue
{
    /// <summary>
    /// 可交互对白物体。服务由 <see cref="DialogueSceneBinder"/> 在场景加载时注入；运行时实例化的物体须自行 <see cref="Bind"/>。
    /// </summary>
    public sealed class DialogueInteractable : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("对白表里的对话树 id。0 表示没有对话树（只说常驻台词）。")]
        [SerializeField, Min(0)] private int dialogueId;
        [Tooltip("头顶名字与气泡里显示的名字。")]
        [SerializeField] private string displayName;
        [Tooltip("常驻台词：没有对话树时，每次交互按顺序取下一句（循环）显示在头顶气泡里。")]
        [SerializeField] private string[] bubbleLines;
        [Tooltip("交互半径（世界单位，按三维距离；2D 场景 z 相同即等价于 XY 平面距离）。小于等于 0 表示不限距离。")]
        [SerializeField, Min(0f)] private float interactRadius;
        [Tooltip("用来测距的角色（通常是玩家）。为空时用场景里的 DialogueInteractionActor（由 DialogueSceneBinder 注入）；两者都空表示不限距离。")]
        [SerializeField] private Transform actor;
        private DialogueService service;
        private Transform sceneActor;
        private int nextBubbleIndex;

        public int DialogueId => dialogueId;
        public string DisplayName => displayName;
        public bool IsBound => service != null;

        /// <summary>是否配了对话树（dialogueId &gt; 0）。</summary>
        public bool HasTree => dialogueId > 0;

        /// <summary>是否配了常驻台词。</summary>
        public bool HasBubble => bubbleLines != null && bubbleLines.Length > 0;

        /// <summary>指针当前是否悬停在本物体上（由 EventSystem 的 Enter / Exit 维护，禁用时清零）。</summary>
        public bool Hovered { get; private set; }

        /// <summary>
        /// 是否是当前交互焦点。**setter 只供 <see cref="DialogueInteractionFocus"/> 调用**（程序集内可见），
        /// 其他代码只读；表现组件（头顶标记）据此切换「!」与名字。
        /// </summary>
        public bool Focused { get; internal set; }

        /// <summary>
        /// 是否被沉浸模式隐藏（由 <see cref="DialogueSceneBinder"/> 按 HudVisibilityChangedEvent 统一设置）。
        /// 为 true 时头顶标记、名字与台词气泡都隐藏，点击本物体不响应。
        /// </summary>
        public bool HiddenByHud { get; private set; }

        /// <summary>
        /// 头顶图标是否被外部世界标记接管（如任务目标标记摆到本 NPC 头顶）。为 true 时头顶标记组件隐藏自己的「…/!」图标，
        /// 名字与台词气泡不受影响。setter（<see cref="SetMarkerOverridden"/>）只供接管方调用；
        /// 本组件不在 OnDisable 里清它——接管方只在目标引用变化时写一次，这里擅自清掉会与接管方失同步。
        /// </summary>
        public bool MarkerOverridden { get; private set; }

        /// <summary>
        /// 现在能否交互：在范围内、没有对白在进行，且「有树已绑定」或「无树但有台词」。
        /// </summary>
        public bool CanInteract => InRange && !(service != null && service.IsRunning) && (HasTree ? IsBound : HasBubble);

        /// <summary>测距角色：Inspector 配的 actor 优先，其次场景里的 DialogueInteractionActor。</summary>
        private Transform RangeActor => actor != null ? actor : sceneActor;

        /// <summary>测距角色为空或半径小于等于 0 恒为 true；否则按本物体与测距角色的三维距离判定。</summary>
        public bool InRange
        {
            get
            {
                Transform target = RangeActor;
                if (target == null || interactRadius <= 0f) return true;
                Vector3 offset = target.position - transform.position;
                return offset.sqrMagnitude <= interactRadius * interactRadius;
            }
        }

        /// <summary>对白正常结束（取消、失败不触发）。</summary>
        public event Action<DialogueResult> OnCompleted;

        /// <summary>无对话树时的一次交互：载荷是这次要显示的台词。</summary>
        public event Action<string> OnBubbleRequested;

        public void Bind(DialogueService dialogueService)
        {
            service = dialogueService ?? throw new ArgumentNullException(nameof(dialogueService));
        }

        /// <summary>
        /// 由 <see cref="DialogueSceneBinder"/> 注入场景里的 DialogueInteractionActor，作为 Inspector 未配 actor 时的测距角色；
        /// 场景卸载时传 null 清掉。
        /// </summary>
        internal void SetSceneActor(Transform sceneActorTransform)
        {
            sceneActor = sceneActorTransform;
        }

        /// <summary>沉浸模式显隐。只供 <see cref="DialogueSceneBinder"/> 调用；表现组件每帧读 <see cref="HiddenByHud"/>。</summary>
        internal void SetHiddenByHud(bool hidden)
        {
            HiddenByHud = hidden;
        }

        /// <summary>
        /// 头顶图标接管开关：只供接管方（程序集内驱动外部世界标记的代码，如任务 HUD）调用，接管传 true、让出传 false；
        /// 表现组件每帧读 <see cref="MarkerOverridden"/>。本模块不认识接管方是谁。
        /// </summary>
        internal void SetMarkerOverridden(bool overridden)
        {
            MarkerOverridden = overridden;
        }

        // 场景加载时 DialogueSceneBinder 已在 sceneLoaded 里绑定，早于 Start；到这里还没绑定多半是直接 Play 了玩法场景。
        // 无对话树的物体不需要服务，不提示。
        private void Start()
        {
            if (HasTree && service == null)
            {
                Log.Warn($"{name} 的 DialogueInteractable 未绑定 DialogueService：点击不会有反应。请从 Boot 场景进入游戏（直接 Play 玩法场景没有对白服务与 EventSystem）", this);
            }
        }

        private void OnDisable()
        {
            Hovered = false;
            Focused = false;
        }

        /// <summary>
        /// 交互：无树有台词 → 抛出下一句台词；有树 → 拉起对白。
        /// 未绑定 / 已有对白进行中 / 不在范围内 / 既无树也无台词时记 Warn 并忽略。
        /// </summary>
        public void Interact()
        {
            if (service != null && service.IsRunning)
            {
                Log.Warn($"{name} 交互被忽略：已有对白在进行", this);
                return;
            }
            if (!InRange)
            {
                Log.Warn($"{name} 交互被忽略：超出交互半径 {interactRadius}", this);
                return;
            }
            if (!HasTree)
            {
                if (!HasBubble)
                {
                    Log.Warn($"{name} 交互被忽略：既没有对话树（dialogueId=0）也没有常驻台词", this);
                    return;
                }
                string line = bubbleLines[nextBubbleIndex % bubbleLines.Length];
                nextBubbleIndex = (nextBubbleIndex + 1) % bubbleLines.Length;
                OnBubbleRequested?.Invoke(line);
                return;
            }
            if (service == null)
            {
                Log.Warn($"{name} 的 DialogueInteractable 未绑定 DialogueService，忽略交互（运行时实例化的物体须自行 Bind）", this);
                return;
            }
            PlayAsync().Forget();
        }

        // 沉浸模式下世界里的交互标记都已隐藏，点击 NPC 不再拉起对白；代码直接调 Interact() 不受影响。
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!HiddenByHud) Interact();
        }

        public void OnPointerEnter(PointerEventData eventData) => Hovered = true;

        public void OnPointerExit(PointerEventData eventData) => Hovered = false;

        // async UniTaskVoid 内部接住全部异常：取消静默、其它记 Error，不留下未观察的 UniTask 异常。
        private async UniTaskVoid PlayAsync()
        {
            try
            {
                DialogueResult result = await service.PlayAsync(dialogueId, destroyCancellationToken);
                OnCompleted?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                // 物体销毁或对白被中断：静默。
            }
            catch (Exception e)
            {
                Log.Error($"{name} 拉起对白 {dialogueId} 失败：{e}", this);
            }
        }
    }
}
