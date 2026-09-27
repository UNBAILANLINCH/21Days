// 职责：已读记录独立档案 "dialogue-read" 的落盘 DTO（带版本信封）。
// 为什么新建：DialogueReadData 本身不能实现 ISaveData——实现了就可能被当成槽位分区，读旧槽位会让已读集合倒退；
//   它又是规则持有的运行时单例（HashSet 便于查重），不适合直接当落盘格式。所以档案用这个 DTO 包一层，
//   由 DialogueReadStore 在读入 / 写出时与单例互相拷贝。放不进 DialogueReadData 同文件：一文件一类。
using System.Collections.Generic;
using Game.Core.Save;

namespace Game.Dialogue
{
    /// <summary>
    /// 已读档案的落盘格式。只经 <c>ISaveService.ReadProfileAsync / WriteProfileAsync</c> 读写，
    /// **不要**用 <c>saves.Get&lt;DialogueReadProfile&gt;()</c> 把它放进槽位。
    /// </summary>
    public sealed class DialogueReadProfile : ISaveData
    {
        public int Version => 1;

        /// <summary>已读键（<see cref="DialogueReadData.Key"/> 生成的字符串），顺序无意义。</summary>
        public List<string> Keys { get; set; } = new List<string>();

        public void Migrate(int fromVersion)
        {
            // 版本 1 是首版，无需迁移。
        }
    }
}
