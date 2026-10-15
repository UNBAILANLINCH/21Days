// 职责：戏班固定谱面与通过门槛；音游配置不能拥有验身份与巡逻转向条件。
using Game.Rhythm;
using UnityEngine;
namespace Game.Theater
{
    [CreateAssetMenu(menuName = "21Days/Theater Config")]
    public sealed class TheaterConfig : ScriptableObject
    {
        [SerializeField] private RhythmConfig chart;
        [SerializeField, Range(1, 100)] private int passScorePercent = 60;
        public RhythmConfig Chart => chart;
        public int PassScorePercent => passScorePercent;
    }
}
