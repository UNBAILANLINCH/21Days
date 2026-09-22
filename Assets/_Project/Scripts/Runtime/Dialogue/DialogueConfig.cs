// 职责：对白表现参数；通用 UIConfig 不包含阅读速度，内容仍由内容表提供。
using UnityEngine;

namespace Game.Dialogue
{
    [CreateAssetMenu(menuName = "21Days/Dialogue/Config")]
    public sealed class DialogueConfig : ScriptableObject
    {
        [SerializeField, Min(1)] private float charactersPerSecond = 35f;
        [SerializeField, Min(0.05f)] private float skipInterval = 0.15f;
        [SerializeField, Min(1)] private int historyLimit = 500;
        [SerializeField, Range(1, 30)] private int slotCount = 10;
        public float CharactersPerSecond => charactersPerSecond;
        public float SkipInterval => skipInterval;
        public int HistoryLimit => historyLimit;
        public int SlotCount => slotCount;
    }
}
