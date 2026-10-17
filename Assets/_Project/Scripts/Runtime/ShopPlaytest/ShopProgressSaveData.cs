// 职责：五店各自的槽位进度；既有遭遇只保存角色，背包不拥有证词和工件点位，故补此分区。
using System.Collections.Generic;
using Game.Core.Save;
using Game.Forging;
namespace Game.ShopPlaytest
{
    public sealed class ShopProgressSaveData : ISaveData
    {
        public int Version => 3;
        public bool Initialized { get; set; }
        public bool TheaterEntered { get; set; }
        public bool PatrolRedirected { get; set; }
        public bool TheaterCompleted { get; set; }
        public bool[] Investigated { get; set; }
        public bool ClothesReceived { get; set; }
        public ForgingProgressData Forging { get; set; }
        public ForgingProgressData LegacyForging { get; set; }
        public int PlayerDrunk { get; set; }
        public int BossDrunk { get; set; }
        public Dictionary<string, int> Customers { get; set; }
        public bool TavernCompleted { get; set; }
        public List<int> RewardedItems { get; set; } = new List<int>();
        public string EquippedSkinId { get; set; } = "";
        public void Migrate(int fromVersion)
        {
            // 旧钥匙/首饰与ABC配方没有确认的映射，留存原记录，仅新打造进度从配置初态开始。
            if (fromVersion < 2) { LegacyForging = Forging; Forging = null; }
            if (fromVersion < 3) RewardedItems = new List<int>();
        }
    }
}
