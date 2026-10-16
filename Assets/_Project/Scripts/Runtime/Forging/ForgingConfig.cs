// 职责：官证金属额度、A/B/C目标数量、每件成本与点击顺序配置。
using UnityEngine;
namespace Game.Forging
{
    [CreateAssetMenu(menuName = "21Days/Forging/Config")]
    public sealed class ForgingConfig : ScriptableObject
    {
        [SerializeField] private int initialQuota = 20;
        [SerializeField] private int maximumQuota = 24;
        [SerializeField] private int productCost = 5;
        [SerializeField] private int[] requiredProducts = { 1, 2, 3 };
        [SerializeField] private int[] pointOrder = { 0, 1, 2, 3 };
        public int MaximumQuota => maximumQuota;
        public ForgingRules CreateRules() => new ForgingRules(initialQuota, maximumQuota, productCost, requiredProducts, pointOrder);
    }
}
