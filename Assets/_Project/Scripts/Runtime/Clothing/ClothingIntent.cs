// 职责：独立衣肆试玩的调查与取衣操作；不读输入设备，不携带主线进度。
namespace Game.Clothing
{
    public readonly struct ClothingIntent
    {
        public ClothingIntent(int customer, bool claim, bool hasIdentity)
        { Customer = customer; Claim = claim; HasIdentity = hasIdentity; }
        public int Customer { get; }
        public bool Claim { get; }
        public bool HasIdentity { get; }
    }
}
