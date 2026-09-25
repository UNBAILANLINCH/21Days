// 职责：场景里的任务地点标记——给 ReachLocation 目标判定抵达、给指引定位目标点。
// 为什么新建：任务系统首次落地（PRP/quest-system）；DialogueInteractable 表示可对话物体，地点没有对白职责，不能复用。
using UnityEngine;

namespace Game.Quest
{
    /// <summary>
    /// 任务地点。由 <see cref="QuestSceneBinder"/> 在场景加载时登记（含未激活的），运行时 Instantiate 的不登记。
    /// </summary>
    public sealed class QuestLocation : MonoBehaviour
    {
        [Tooltip("地点键：任务表 ReachLocation 目标的 key，或目标指引用的 location 键。同一时刻已加载场景里应唯一。")]
        [SerializeField] private string locationKey;

        [Tooltip("抵达判定半径（米）：玩家锚点进入该半径即算抵达。")]
        [SerializeField, Min(0)] private float radius = 1.5f;

        public string Key => locationKey;
        public float Radius => radius;
        public Vector3 Position => transform.position;

#if UNITY_EDITOR
        // 只画调试范围，不放业务。
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}
