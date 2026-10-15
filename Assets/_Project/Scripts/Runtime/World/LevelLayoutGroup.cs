// 职责：保存关卡整体摆位的关联对象；不在运行时同步位置、不改变任何玩法。
// 复用不足：Transform 层级无法同时包含导入模型节点和已有店铺预制体，强行重挂会改变模型接线。
// 扩展不足：SpawnAnchor/QuestLocation 是单点身份，不负责建筑及人员集合，故新增纯作者数据组件。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.World
{
    [DisallowMultipleComponent]
    [AddComponentMenu("21Days/World/LevelLayoutGroup")]
    public sealed class LevelLayoutGroup : MonoBehaviour
    {
        [SerializeField] private string displayName;
        [Tooltip("整组移动时一同平移的外部建筑、人员或点位；本物体的子物体会自动跟随，无需重复填写。")]
        [SerializeField] private Transform[] linkedMembers = Array.Empty<Transform>();

        public IReadOnlyList<Transform> LinkedMembers => linkedMembers;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }
}
