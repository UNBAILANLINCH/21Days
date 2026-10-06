// 职责：场景里的传送点组件——持目标场景键与目标出生点 id，按触发方式判定「现在能不能走」，
//   判定通过时发一个事件就结束；**不做转场、不读输入**。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：工程里没有任何「场景出口」组件。QuestLocation 是任务判定点（到达即算），
//      DialogueInteractable 是拉对白，两者的判定语义都不是「切场景」。
//   2. 扩展不行：SceneGameState 是流程层基类，挂在容器里、按状态类加载场景，
//      它不认场景里的物体；把出口语义塞进它会耦合「场景内容」与「流程」。
//   3. 所以新建一个挂在场景物体上的组件，只留一个事件出口。
// 输入从哪来：交互键**不在这里读**。进入范围由外部（触发体 / 将来的 Trigger 回调）调 NotifyEntered，
//   交互键由外部输入层调 TryInteract。这样组件在 EditMode 里能直接测，不依赖 Physics。
// 转场由谁做：本组件不认识 GameFlow，拿到的东西已经够用了——调用方读 TargetSceneKey / TargetSpawnId，
//   再经 Game.World.WorldRules.Resolve 算落点、走既有流程（加载黑幕 E4 已由 GameFlow 统一落 / 揭）。
using System;
using UnityEngine;
using UnityEngine.Events;

namespace Game.World
{
    /// <summary>
    /// 传送点（一个出口）。挂在场景里的出口物体上。
    /// <para>
    /// 两种触发方式（对应 <c>TbPortal.trigger_kind</c>）：
    /// <list type="bullet">
    /// <item><see cref="PortalTriggerKind.EnterRange"/>：调用方报告目标进出范围，进入那一刻触发一次；</item>
    /// <item><see cref="PortalTriggerKind.Interact"/>：先进入范围，再由外部输入层调 <see cref="TryInteract"/>。</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>本组件不做转场。</b>它只回答「现在能不能走、走去哪」。转场要跨两个流程状态、要压加载黑幕，
    /// 那是 GameFlow 的事（roadmap E4 已做，别在这里再落一层黑幕）。
    /// </para>
    /// <para>
    /// <b>范围不靠 Physics</b>：本组件不声明 OnTriggerEnter，范围由调用方（触发体、将来的触发器层）喂进来，
    /// 所以 EditMode 里 <c>AddComponent</c> 出来就能测。
    /// </para>
    /// </summary>
    [AddComponentMenu("21Days/World/PortalAnchor")]
    public sealed class PortalAnchor : MonoBehaviour
    {
        [Header("去哪")]
        [Tooltip("目标场景键（TbScene.scene_key）。转场由调用方做，本组件只把它交出去。")]
        [SerializeField] private string targetSceneKey = string.Empty;

        [Tooltip("目标出生点 id；留空表示由 Game.World.WorldRules 回退到该场景的 default_spawn_id。")]
        [SerializeField] private string targetSpawnId = string.Empty;

        [Header("怎么触发")]
        [Tooltip("触发方式：进入范围即触发 / 需要交互键（对应 TbPortal.trigger_kind）。")]
        [SerializeField] private PortalTriggerKind triggerKind = PortalTriggerKind.EnterRange;

        [Tooltip("需要交互键时，玩家必须在范围内才能触发；进入范围之外的方式触发不了。")]
        [SerializeField] private bool requirePlayerInRange = true;

        [Tooltip("本出口的触发半径（米）。**范围判定仍由调用方做**：本组件只是把「我有多大」这个场地信息交出去，"
                 + "自己不查物理、不逐帧测距（见类注释「范围不靠 Physics」）。")]
        [SerializeField, Min(0.1f)] private float triggerRadius = 1.2f;

        [Header("场景内的锚点")]
        [Tooltip("本出口的锚点 id，与 TbPortal.anchor_id 对应；空 = 不校验（只在 Inspector 里提示）。")]
        [SerializeField] private string anchorId = string.Empty;

        [Tooltip("出口触发后要通知谁。本组件不订阅任何东西，转场接线交给场景侧。")]
        [SerializeField] private UnityEvent onTriggered = new UnityEvent();

        private bool playerInRange;
        private bool triggered;

        /// <summary>触发时发一次（同一个出口最多一次，除非 <see cref="ResetTriggered"/>）。</summary>
        public event Action<PortalAnchor> OnTriggered;

        /// <summary>目标场景键（TbScene.scene_key）。</summary>
        public string TargetSceneKey => targetSceneKey;

        /// <summary>目标出生点 id；空串表示用目标场景的默认出生点。</summary>
        public string TargetSpawnId => targetSpawnId;

        /// <summary>触发方式。</summary>
        public PortalTriggerKind TriggerKind => triggerKind;

        /// <summary>
        /// 本出口的触发半径（米）。判定仍由调用方做：调用方拿它当「玩家离多近算进范围」的场地参数，
        /// 本组件不自己测距、不查物理。
        /// </summary>
        public float TriggerRadius => triggerRadius;

        /// <summary>本出口的锚点 id（与 TbPortal.anchor_id 对应）。</summary>
        public string AnchorId => anchorId;

        /// <summary>玩家当前是否在范围内（由 <see cref="NotifyEntered"/> / <see cref="NotifyExited"/> 维护）。</summary>
        public bool PlayerInRange => playerInRange;

        /// <summary>本出口是否已经触发过。</summary>
        public bool HasTriggered => triggered;

        /// <summary>进入范围。外部（触发体）调用；同一状态重复调用不重复触发。</summary>
        public void NotifyEntered()
        {
            if (playerInRange)
            {
                return;
            }

            playerInRange = true;

            // 进入范围即触发的那一种：进门那一下就走，不等交互键。
            if (triggerKind == PortalTriggerKind.EnterRange)
            {
                Trigger();
            }
        }

        /// <summary>离开范围。会清掉「在范围内」，但不解除「已触发」。</summary>
        public void NotifyExited()
        {
            playerInRange = false;
        }

        /// <summary>
        /// 外部输入层按下交互键时调。只有需要交互键的出口、且玩家在范围内、且还没触发过才返回 true。
        /// </summary>
        public bool TryInteract()
        {
            if (!CanTrigger())
            {
                return false;
            }

            Trigger();
            return true;
        }

        /// <summary>
        /// 现在能不能触发。纯判定，不改状态、不发事件，测试与调用方都用它。
        /// <para>
        /// 判据：① 目标场景键非空（没填目标就是没接线，不该悄悄传送到某处）；
        /// ② 还没触发过；③ 触发方式是交互键时，必须在范围内（进入范围那一种不看这个标志位，
        /// 因为它是在 <see cref="NotifyEntered"/> 里当场触发掉的）。
        /// </para>
        /// </summary>
        public bool CanTrigger()
        {
            if (string.IsNullOrEmpty(targetSceneKey) || triggered)
            {
                return false;
            }

            if (triggerKind == PortalTriggerKind.Interact && requirePlayerInRange && !playerInRange)
            {
                return false;
            }

            return true;
        }

        /// <summary>清掉「已触发」，让出口能再走一次（重置关卡、回放、测试用）。</summary>
        public void ResetTriggered()
        {
            triggered = false;
        }

        private void Awake()
        {
            // 目标场景键是场景侧接线，漏填要在加载时就看见，而不是等玩家踩上去什么都没发生。
            if (string.IsNullOrEmpty(targetSceneKey))
            {
                Debug.LogWarning($"传送点 {name} 没有填目标场景键，它将永远触发不了（见 Tables/Defines/world.xml 的 TbPortal.target_scene）。", this);
            }
        }

#if UNITY_EDITOR
        // 只在编辑器里提示「锚点没填」，运行期不做任何校验（运行期数据以表为准）。
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(anchorId))
            {
                return;
            }

            if (!string.IsNullOrEmpty(targetSceneKey) && string.IsNullOrEmpty(targetSpawnId))
            {
                // 留空是合法配置（回退到目标场景的默认出生点），不是错误，只在 Inspector 里留个痕。
                Debug.Log($"传送点 {name}：目标出生点留空，到达时将用「{targetSceneKey}」的默认出生点。", this);
            }
        }
#endif

        // 一次性闸门：触发过一次就不再触发，避免玩家在出口上反复踩导致连续转场。
        private void Trigger()
        {
            if (!CanTrigger())
            {
                return;
            }

            triggered = true;
            OnTriggered?.Invoke(this);

            // UnityEvent 是 UnityEngine.Object 字段（已销毁时会伪空），判空只用 == null。
            if (onTriggered != null)
            {
                onTriggered.Invoke();
            }
        }
    }
}
