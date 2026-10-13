// 职责：官证额度、工件材料与点击顺序的独立试玩配置。
using UnityEngine;
namespace Game.Forging
{
    [CreateAssetMenu(menuName = "21Days/Forging/Config")]
    public sealed class ForgingConfig : ScriptableObject
    {
        [SerializeField] private int initialQuota = 2;
        [SerializeField] private int maximumQuota = 8;
        [SerializeField] private int keyCost = 4;
        [SerializeField] private int ornamentCost = 2;
        [SerializeField] private int requiredOrnaments = 1;
        [SerializeField] private int[] pointOrder = { 0, 1, 2, 3 };
        public int MaximumQuota => maximumQuota;
        public ForgingRules CreateRules() => new ForgingRules(initialQuota, maximumQuota, keyCost, ornamentCost, requiredOrnaments, pointOrder);
    }
}
