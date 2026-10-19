// 职责：作者预摆的剧情交互点、提示范围和稳定目标，不生成地形或推进玩家。
// 现有五店区域不涵盖序章和会面，扩展它会混入章节内容；沿用同一交互登记接口。
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Interaction;
using Game.IsometricExploration;
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
            [SerializeField] private GameObject marker;
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
            public GameObject Marker => marker;
            public void ShowMarker(bool visible) { if (marker != null) marker.SetActive(visible); }
            public void Bind(SpotlightStoryConfig source, Func<Point, bool> canInteract, Action<Point> onInteract, bool human = false)
            {
                config = source; allowed = canInteract; interact = onInteract;
                var node = config.Find(stageId);
                prompt = new InteractionPrompt(node == null ? "交谈" : node.Verb, node == null ? stageId : node.DisplayName);
                if (stageId == "enter_r0171") prompt = human ? new InteractionPrompt("入镜", "妖界") : new InteractionPrompt("问询", "轮回堂值守");
            }
            public void Interact() { if (CanInteract) interact?.Invoke(this); }
            public void OnFocusChanged(bool focused) { }
        }
        [SerializeField] private SpotlightStoryConfig config;
        [SerializeField] private bool humanStory;
        [SerializeField] private NarrativeTrigger mainTarget;
        [SerializeField] private NarrativeTrigger mentorTarget;
        [SerializeField] private Point[] points = System.Array.Empty<Point>();
        [SerializeField] private GameObject[] legacyExamples = System.Array.Empty<GameObject>();
        [SerializeField] private GameObject windowFigure;
        public SpotlightStoryConfig Config => config;
        public bool IsHumanStory => humanStory;
        public NarrativeTrigger MainTarget => mainTarget;
        public NarrativeTrigger MentorTarget => mentorTarget;
        public Point[] Points => points;
        public GameObject WindowFigure => windowFigure;
        private void Awake()
        {
            // 章节绑定前先收起，避免沿用作者预览状态而提前闪现。
            if (windowFigure != null) windowFigure.SetActive(false);
            foreach (var point in points) if (point != null) point.ShowMarker(false);
        }
        public async UniTask PlayWindowShotAsync(Camera camera, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (camera == null || config == null || windowFigure == null)
                throw new InvalidOperationException("窗边特写的相机、配置或何素引用缺失");
            var follow = camera.GetComponent<SmoothCameraFollow>();
            bool following = follow != null && follow.enabled;
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            float originalFov = camera.fieldOfView;
            Vector3 focus = windowFigure.transform.position + Vector3.up * config.WindowLookHeight;
            Vector3 position = focus + config.WindowCameraOffset;
            Quaternion rotation = Quaternion.LookRotation(focus - position);
            if (follow != null) follow.enabled = false;
            try
            {
                await BlendWindowCameraAsync(camera, position, rotation, config.WindowFieldOfView, token);
                await UniTask.Delay(TimeSpan.FromSeconds(config.WindowHoldSeconds), ignoreTimeScale: true, cancellationToken: token);
                await BlendWindowCameraAsync(camera, originalPosition, originalRotation, originalFov, token);
            }
            finally
            {
                if (camera != null)
                {
                    camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                    camera.fieldOfView = originalFov;
                }
                if (follow != null)
                {
                    follow.SetOffset(follow.Offset); // 清掉接管前的跟随惯性，避免回镜后抖动。
                    follow.enabled = following;
                }
            }
        }
        private async UniTask BlendWindowCameraAsync(Camera camera, Vector3 position, Quaternion rotation, float fov, CancellationToken token)
        {
            Vector3 from = camera.transform.position;
            Quaternion fromRotation = camera.transform.rotation;
            float fromFov = camera.fieldOfView;
            float seconds = Mathf.Max(.1f, config.WindowBlendSeconds); // lint-ok: 仅镜头表现，不参与确定性模拟。
            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
            {
                token.ThrowIfCancellationRequested();
                if (camera == null) throw new OperationCanceledException(token);
                float t = Mathf.SmoothStep(0f, 1f, elapsed / seconds); // lint-ok: unscaled 运镜插值，不参与玩法计算。
                camera.transform.SetPositionAndRotation(Vector3.Lerp(from, position, t), Quaternion.Slerp(fromRotation, rotation, t));
                camera.fieldOfView = Mathf.Lerp(fromFov, fov, t); // lint-ok: 仅镜头视野插值。
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, token);
            }
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov;
        }
        public Point Find(string stage) { foreach (var point in points) if (point != null && point.StageId == stage) return point; return null; }
        public void Display(string stage, bool formal, bool mentorBattle)
        {
            if (windowFigure != null) windowFigure.SetActive(formal && !humanStory &&
                (stage == "enter_r0188" || stage == "talk_21009" || stage == "action_r0197"));
            foreach (var example in legacyExamples)
            {
                if (example == null) continue;
                if (example.TryGetComponent<NarrativeFlagVisibility>(out var visibility)) visibility.SetSuppressed(formal);
                else example.SetActive(!formal);
            }
            foreach (var point in points)
            {
                if (point == null || point.Anchor == null) continue;
                bool visible = humanStory && point.StageId == "enter_r0026" && stage != "enter_r0171";
                // 值守没有离场剧情；只收交互许可，人物随妖界根分组显隐。
                if (!humanStory && formal && point.StageId == "enter_r0171") visible = true;
                if (!humanStory && formal && point.StageId == "enter_r0215" && stage == "talk_21011") visible = true;
                // 赵十四没有离场剧情，留在戏台旁；交互许可仍由当前节点决定。
                if (humanStory && formal && point.StageId == "enter_r0083") visible = true;
                foreach (var candidate in points)
                    if (candidate != null && candidate.Anchor == point.Anchor && !StoryGuideRules.IsAutomaticEntry(candidate.StageId) && (candidate.StageId == stage || mentorBattle && candidate.StageId == "action_r0070")) visible = formal;
                point.Anchor.gameObject.SetActive(visible);
                point.ShowMarker(formal && stage == point.StageId);
            }
        }
    }
}
