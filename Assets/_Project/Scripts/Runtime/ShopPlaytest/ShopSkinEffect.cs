// 职责：把通用道具效果口转接五店换皮；Inventory 不反向依赖五店，身份规则也不持有 UI。
using Game.Inventory;
namespace Game.ShopPlaytest
{
    public sealed class ShopSkinEffect : IItemEffect
    {
        public const string Id = "identity.shop_skin";
        private readonly ShopPlaytestState state;
        public ShopSkinEffect(ShopPlaytestState state) { this.state = state; }
        public string EffectId => Id;
        public ItemEffectOutcome Apply(ItemEffectContext context) => state.TryWearSkin(context.ItemId, out string reason)
            ? ItemEffectOutcome.Applied(0) : ItemEffectOutcome.Rejected(reason);
    }
}
