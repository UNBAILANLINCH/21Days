// 职责：场景实例的稳定标识、显示名和既有巡逻/纸片接线，不负责推进玩法。
// 为什么新建：现有角色仅靠视图引用；模型与小人组件没有可序列化的实例身份，不能用名字或 InstanceID 替代。
using System;
using Game.Monster;
using TMPro;
using UnityEngine;

namespace Game.Taming
{
    [DisallowMultipleComponent]
    public sealed class TamingActor : MonoBehaviour
    {
        [SerializeField] private string stableId;
        [SerializeField] private string displayName;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private SpriteRenderer stateIndicator;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Transform[] patrolPoints;

        public string StableId => stableId;
        public string DisplayName => displayName;
        public SpriteRenderer Visual => visual;
        public SpriteRenderer StateIndicator => stateIndicator;
        public event Action<TamingActor, bool> OnAvailabilityChanged;

        // Reset 由编辑器添加组件时调用，保存后沿用；运行时和复制时都不偷偷重造标识。
        private void Reset() => stableId = Guid.NewGuid().ToString("N");
        private void Awake() => RefreshLabel();
        private void OnEnable() => OnAvailabilityChanged?.Invoke(this, true);
        private void OnDisable() => OnAvailabilityChanged?.Invoke(this, false);

        public void RefreshLabel()
        {
            if (nameLabel != null) nameLabel.text = displayName + " [" + stableId + "]";
        }

        public Vector2[] PatrolPositions(EncounterSceneView view)
        {
            if (patrolPoints == null || patrolPoints.Length == 0) throw new InvalidOperationException("巡逻者缺少路线：" + stableId);
            var result = new Vector2[patrolPoints.Length];
            for (int i = 0; i < result.Length; i++)
            {
                if (patrolPoints[i] == null) throw new InvalidOperationException("巡逻点引用为空：" + stableId);
                result[i] = view.ToLogicPosition(patrolPoints[i].position);
            }
            return result;
        }
    }
}
