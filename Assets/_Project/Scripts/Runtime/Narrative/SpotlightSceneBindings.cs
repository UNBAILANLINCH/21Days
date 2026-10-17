// 职责：作者预摆的剧情交互点、提示范围和稳定目标，不生成地形或推进玩家。
// 现有五店区域不涵盖序章和会面，扩展它会混入章节内容；沿用同一交互登记接口。
using System;
using Game.Interaction;
using UnityEngine;
namespace Game.Narrative
{
    public sealed class SpotlightSceneBindings : MonoBehaviour
    {
        [Serializable]
        public sealed class Point : IInteractable
        {
            [SerializeField] private string stageId;
            [SerializeField] private Transform anchor;
            private SpotlightStoryConfig config;
            private Func<Point, bool> allowed;
            private Action<Point> interact;
            private InteractionPrompt prompt;
            public string StageId => stageId;
            public Vector3 Position => anchor == null ? Vector3.zero : anchor.position;
            public float InteractionRadius => config == null ? 0f : config.InteractionRadius;
            public bool CanInteract => anchor != null && anchor.gameObject.activeInHierarchy && allowed != null && allowed(this);
            public int InteractionPriority => 10;
            public InteractionPrompt Prompt => prompt;
            public Transform Anchor => anchor;
            public void Bind(SpotlightStoryConfig source, Func<Point, bool> canInteract, Action<Point> onInteract)
            {
                config = source; allowed = canInteract; interact = onInteract;
                var node = config.Find(stageId);
                prompt = new InteractionPrompt(node == null ? "交谈" : node.Verb, node == null ? stageId : node.DisplayName);
            }
            public void Interact() { if (CanInteract) interact?.Invoke(this); }
            public void OnFocusChanged(bool focused) { }
        }
        [SerializeField] private SpotlightStoryConfig config;
        [SerializeField] private NarrativeTrigger mainTarget;
        [SerializeField] private NarrativeTrigger mentorTarget;
        [SerializeField] private Point[] points = System.Array.Empty<Point>();
        [SerializeField] private GameObject[] legacyExamples = System.Array.Empty<GameObject>();
        public SpotlightStoryConfig Config => config;
        public NarrativeTrigger MainTarget => mainTarget;
        public NarrativeTrigger MentorTarget => mentorTarget;
        public Point[] Points => points;
        public Point Find(string stage) { foreach (var point in points) if (point != null && point.StageId == stage) return point; return null; }
        public void Display(string stage, bool formal, bool mentorBattle)
        {
            foreach (var example in legacyExamples) if (example != null) example.SetActive(!formal);
            foreach (var point in points)
            {
                if (point == null || point.Anchor == null) continue;
                bool visible = false;
                foreach (var candidate in points)
                    if (candidate != null && candidate.Anchor == point.Anchor && (candidate.StageId == stage || mentorBattle && candidate.StageId == "action_r0070")) visible = formal;
                point.Anchor.gameObject.SetActive(visible);
            }
        }
    }
}
