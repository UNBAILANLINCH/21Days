// 职责：镜模块的存档分区——辨认记录（已照见真形 / 见过模糊轮廓）、剧情裂痕数、自照次数。
// 为什么新建：Mirror 模块首次落地（PRP/mirror-core 2.3），框架与既有模块里没有辨认记录的分区；
//   塞进 LootSaveData / PlayerSaveData 会让拾取或玩家存档混入与它们无关的辨认数据。
//   击中裂痕不进存档：它由 PlayerModel.Health 推出（重开本场 Health 回满即清零）。
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Mirror
{
    public sealed class MirrorSaveData : ISaveData
    {
        public int Version => 1;

        /// <summary>已照见真形的妖（妖物表主键），按首次照见顺序，不重复。</summary>
        public List<int> Identified { get; set; } = new();

        /// <summary>见过模糊轮廓的妖（妖物表主键），按首次顺序，不重复。</summary>
        public List<int> GlimpsedBlurry { get; set; } = new();

        /// <summary>剧情裂痕数：只缩减作用距离与可见范围，不计入三裂、不致镜碎。</summary>
        public int StoryCracks { get; set; }

        /// <summary>自照次数（埋点与后续叙事用）。</summary>
        public int SelfLooks { get; set; }

        public void Migrate(int fromVersion)
        {
            // 第 1 版无迁移。
        }
    }
}
