// 职责：跨槽位已读档案；不能实现 ISaveData，否则加载旧槽位会倒退已读集合。
using System;
using System.Collections.Generic;

namespace Game.Dialogue
{
    public sealed class DialogueReadData
    {
        public int Version { get; set; } = 1;
        public HashSet<string> Keys { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        public void Validate()
        {
            if (Version != 1 || Keys == null) throw new ArgumentException("已读档案损坏");
            foreach (string key in Keys)
                if (string.IsNullOrEmpty(key)) throw new ArgumentException("已读档案键损坏");
        }
        public static string Key(string conversation, string node, int revision)
        {
            // 长度前缀避免内容 ID 自身含分隔符时发生碰撞。
            return conversation.Length + ":" + conversation + node.Length + ":" + node + ":" + revision;
        }
    }
}
