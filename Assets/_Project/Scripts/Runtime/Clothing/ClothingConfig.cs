// 职责：衣肆独立试玩的可替换证词与唯一答案；不是正式剧情数据。
using System;
using UnityEngine;
namespace Game.Clothing
{
    [CreateAssetMenu(menuName = "21Days/Clothing/Config")]
    public sealed class ClothingConfig : ScriptableObject
    {
        [SerializeField] private string[] names = { "顾客甲", "顾客乙", "顾客丙" };
        [SerializeField] private string[] clues = { "我今天才第一次来，还没订过衣服。", "昨天我订了蓝色长衫，也交了订金。", "我只是陪人来的，不取衣服。" };
        [SerializeField] private string tailorClue = "今天可以领取的，是昨日已交订金的蓝色长衫。";
        [SerializeField] private int collectorIndex = 1;
        [SerializeField] private string visitorReply = "客官，想看什么款式的衣裳？";
        [SerializeField] private string[] tailorReplies = {
            "您今天头一回来，还没订过衣服，先看看款式吧。",
            "您昨日订的蓝色长衫已经做好了，订金也收到了。现在拿给您吗？",
            "您是陪人来的吧？取衣的那位还没来。"
        };
        [SerializeField] private string receivedReply = "您订的长衫已经取走了，还想看看别的吗？";
        [SerializeField] private string handoverReply = "给，您的蓝色长衫。收好了。";
        public int Count => names.Length;
        public string NameAt(int index) => names[index];
        public string ClueAt(int index) => clues[index];
        public string TailorClue => tailorClue;
        public string VisitorReply => visitorReply;
        public string TailorReplyAt(int index) => tailorReplies[index];
        public string ReceivedReply => receivedReply;
        public string HandoverReply => handoverReply;
        public ClothingRules CreateRules()
        {
            if (names == null || clues == null || names.Length != clues.Length || tailorReplies == null || tailorReplies.Length != names.Length)
                throw new InvalidOperationException("衣肆姓名和证词数量不一致");
            return new ClothingRules(names.Length, collectorIndex);
        }
    }
}
