// 职责：保存未完成工件和已交付成果；既有材料快照不含点位/额度，所以单独记录玩法进度。
namespace Game.Forging
{
    public sealed class ForgingProgressData
    {
        public int Schema { get; set; }
        public int Quota { get; set; }
        public int Spent { get; set; }
        public int[] Products { get; set; }
        // 仅为v1存档留存原钥匙/首饰记录，新规则不读取这两个字段。
        public int Keys { get; set; }
        public int Ornaments { get; set; }
        public int Product { get; set; }
        public int Progress { get; set; }
        public bool Submitted { get; set; }
    }
}
