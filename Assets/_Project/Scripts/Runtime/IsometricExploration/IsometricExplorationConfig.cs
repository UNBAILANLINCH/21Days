// 职责：保存 2.5D 探索原型中需要现场调节的移动、排序与相机参数。
// 为什么新建：现有 PlayerConfig 面向战斗规则，不能承载独立场景原型的表现参数。
using UnityEngine;

namespace Game.IsometricExploration
{
    [CreateAssetMenu(menuName = "21Days/IsometricExploration/Config")]
    public sealed class IsometricExplorationConfig : ScriptableObject
    {
        [SerializeField, Min(0.1f), Tooltip("Showcase 角色每秒移动的世界单位数")]
        private float moveSpeed = 3f;
        [SerializeField, Min(1), Tooltip("世界 Y 坐标转换为整数排序值时使用的缩放")]
        private int sortingScale = 100;
        [SerializeField, Min(0.01f), Tooltip("摄像机跟随目标时的缓动时间")]
        private float cameraSmoothTime = 0.2f;

        public float MoveSpeed => moveSpeed;
        public int SortingScale => sortingScale;
        public float CameraSmoothTime => cameraSmoothTime;
    }
}
