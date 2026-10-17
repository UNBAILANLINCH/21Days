// 职责：序章至会面的节点说明、教程文字和距离参数；运行时只读。
// 剧情表已有推进图，不能重复建图；只补场景接线和提示配置。
using System;
using UnityEngine;
namespace Game.Narrative
{
    [CreateAssetMenu(menuName = "21Days/Narrative/Spotlight Story")]
    public sealed class SpotlightStoryConfig : ScriptableObject
    {
        [Serializable]
        public sealed class Node
        {
            [SerializeField] private string stageId;
            [SerializeField] private string displayName;
            [SerializeField] private string verb = "交谈";
            [SerializeField, TextArea] private string hint;
            public string StageId => stageId;
            public string DisplayName => displayName;
            public string Verb => verb;
            public string Hint => hint;
        }
        [Serializable]
        public sealed class Guide
        {
            [SerializeField] private StoryGuideRules.Action action;
            [SerializeField, TextArea] private string text;
            public StoryGuideRules.Action Action => action;
            public string Text => text;
        }
        [SerializeField] private Node[] nodes = System.Array.Empty<Node>();
        [SerializeField] private Guide[] guides = System.Array.Empty<Guide>();
        [SerializeField, Min(.1f)] private float interactionRadius = 2.5f;
        [SerializeField, Min(.1f)] private float guideRadius = 7f;
        [SerializeField, Min(0f)] private float exitPadding = .5f;
        [SerializeField] private int questId = 900;
        public float InteractionRadius => interactionRadius;
        public float GuideRadius => guideRadius;
        public float ExitPadding => exitPadding;
        public int QuestId => questId;
        public Node Find(string stage) { foreach (var node in nodes) if (node != null && node.StageId == stage) return node; return null; }
        public string HintFor(StoryGuideRules.Action action) { foreach (var guide in guides) if (guide != null && guide.Action == action) return guide.Text; return ""; }
    }
}
