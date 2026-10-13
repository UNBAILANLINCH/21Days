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
        public int Count => names.Length;
        public string NameAt(int index) => names[index];
        public string ClueAt(int index) => clues[index];
        public string TailorClue => tailorClue;
        public ClothingRules CreateRules()
        {
            if (names == null || clues == null || names.Length != clues.Length)
                throw new InvalidOperationException("衣肆姓名和证词数量不一致");
            return new ClothingRules(names.Length, collectorIndex);
        }
    }
}
