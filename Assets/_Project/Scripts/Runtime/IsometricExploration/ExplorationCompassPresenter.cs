// 职责：驱动探索 HUD 的万向标——场景加载时登记 ExplorationPointOfInterest，每帧把屏外 / 身后仍需指引的兴趣点
//   摆到屏幕边缘（对象池复用模板，不销毁只隐藏），沉浸时整体跳过。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：QuestHudPresenter 只指引「当前追踪任务」一个目标，且带头顶标记与距离节流，万向标要同时画多个兴趣点。
//   2. 扩展不行：ExplorationHudPresenter 管沉浸开关与输入订阅，塞进逐帧摆位会让它同时成为 ITickable 与场景扫描器，职责说不通。
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Core.Events;
using Game.Core.Logging;
using Game.Core.Telemetry;
using Game.Core.UI;
using Game.Quest;
using MessagePipe;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Game.IsometricExploration
{
    /// <summary>
    /// 万向标入口点。HUD 在 <see cref="BootCompletedEvent"/> 后才能拿到（与 <see cref="ExplorationHudPresenter"/> 并发打开返回同一实例）；
    /// 本类不关闭 HUD（HUD 的生命周期归 ExplorationHudPresenter）。
    /// 每帧无分配：标记池只在需要更多标记时扩容，标签字符串在登记时截好、只在槽位换了兴趣点时写 TMP。
    /// </summary>
    public sealed class ExplorationCompassPresenter : IStartable, ITickable, IDisposable
    {
        // 标签相对箭头朝画布中心方向的偏移（像素）。
        private const float LabelOffset = 44f;

        // 同边万向标最小间距（像素）。待并入 IsometricExplorationConfig（本波暂不改该 SO，先落地为常量）。
        private const float CompassMinSpacing = 56f;

        private readonly IUIService ui;
        private readonly QuestSceneBinder binder;
        private readonly IsometricExplorationConfig config;
        private readonly ISubscriber<BootCompletedEvent> bootCompleted;
        private readonly ISubscriber<HudVisibilityChangedEvent> hudChanged;
        private readonly ITelemetryScope telemetry;

        private readonly List<ExplorationPointOfInterest> points = new List<ExplorationPointOfInterest>();
        private readonly List<string> pointLabels = new List<string>();

        // 本帧待摆放的万向标：先收集全部，经 SpreadAlongEdges 同边错开后再写入标记池，避免文字互相压住。
        private readonly List<ExplorationCompassRules.CompassPlacement> framePlacements =
            new List<ExplorationCompassRules.CompassPlacement>();
        private readonly List<string> framePlacementLabels = new List<string>();

        // 标记池：并行列表，下标即槽位。
        private readonly List<RectTransform> markerRoots = new List<RectTransform>();
        private readonly List<RectTransform> markerArrows = new List<RectTransform>();
        private readonly List<TMP_Text> markerLabels = new List<TMP_Text>();
        private readonly List<string> markerLabelSources = new List<string>();

        private IDisposable subscription;
        private ExplorationHudView hud;
        private bool hudHidden;
        private bool sceneSubscribed;
        private bool templateMissingReported;
        private int shownCount;
        private bool disposed;

        public ExplorationCompassPresenter(IUIService ui, QuestSceneBinder binder, IsometricExplorationConfig config,
            ISubscriber<BootCompletedEvent> bootCompleted, ISubscriber<HudVisibilityChangedEvent> hudChanged,
            ITelemetryScope telemetry)
        {
            this.ui = ui ?? throw new ArgumentNullException(nameof(ui));
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            // IsometricExplorationConfig 是 ScriptableObject，判空只用 == null。
            if (config == null) throw new ArgumentNullException(nameof(config));
            this.config = config;
            this.bootCompleted = bootCompleted ?? throw new ArgumentNullException(nameof(bootCompleted));
            this.hudChanged = hudChanged ?? throw new ArgumentNullException(nameof(hudChanged));
            this.telemetry = telemetry ?? NullTelemetryScope.Instance;
            Enabled = config.ShowCompass;
        }

        /// <summary>已登记的兴趣点（含已销毁待清理项）。供回放 / 调试读取。</summary>
        public IReadOnlyList<ExplorationPointOfInterest> Points => points;

        /// <summary>本帧显示中的万向标个数。</summary>
        public int ShownCount => shownCount;

        /// <summary>
        /// 万向标总开关：初值取 <see cref="IsometricExplorationConfig.ShowCompass"/>（默认关——用户决定：
        /// 全部兴趣点都标识会显得屏幕乱，任务追踪指引已由 Quest 负责）。关闭时 <see cref="Tick"/> 跳过扫描与摆位，
        /// 只在刚关闭那一帧收起已显示的标记；调试或将来做附近提示时可运行时开。回放用它显式打开来验证摆位规则。
        /// </summary>
        public bool Enabled { get; set; }

        public void Start()
        {
            // 订阅句柄必须托管（EventConventions.cs 第 5 条）。
            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            bootCompleted.Subscribe(_ => OpenHudAsync().Forget()).AddTo(bag);
            hudChanged.Subscribe(e => HandleHudChanged(e.Hidden)).AddTo(bag);
            subscription = bag.Build();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) BindScene(scene);
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            sceneSubscribed = true;
        }

        public void Tick()
        {
            if (disposed || hud == null || hudHidden) return;

            if (!Enabled)
            {
                if (shownCount > 0) HideFrom(0);
                return;
            }

            Camera camera = binder.SceneCamera;
            if (camera == null)
            {
                HideFrom(0);
                return;
            }

            Vector2 canvasSize = hud.CanvasSize;
            float margin = config.CompassEdgeMargin;

            // 第一遍：只算摆位，不落到标记池（还没做同边错开，位置可能还会调整）。
            framePlacements.Clear();
            framePlacementLabels.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                ExplorationPointOfInterest point = points[i];
                if (point == null || !point.isActiveAndEnabled || !point.IsVisible) continue;

                Vector3 viewport = camera.WorldToViewportPoint(point.Position);
                if (!ExplorationCompassRules.TryPlace(viewport, canvasSize, margin, out Vector2 position, out float angle))
                {
                    continue;
                }

                framePlacements.Add(new ExplorationCompassRules.CompassPlacement(position, angle));
                framePlacementLabels.Add(pointLabels[i]);
            }

            ExplorationCompassRules.SpreadAlongEdges(framePlacements, canvasSize, margin, CompassMinSpacing);

            // 第二遍：错开后的最终位置落到标记池。
            int used = 0;
            for (int k = 0; k < framePlacements.Count; k++)
            {
                if (!EnsureMarker(used)) break;
                ExplorationCompassRules.CompassPlacement placement = framePlacements[k];
                PlaceMarker(used, placement.AnchoredPosition, placement.AngleDeg, framePlacementLabels[k]);
                used++;
            }

            HideFrom(used);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (subscription != null)
            {
                subscription.Dispose();
                subscription = null;
            }

            if (sceneSubscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                sceneSubscribed = false;
            }

            // 标记实例是 HUD 的子物体，随 HUD（UIService 持有）一起销毁，这里只清引用。
            points.Clear();
            pointLabels.Clear();
            framePlacements.Clear();
            framePlacementLabels.Clear();
            markerRoots.Clear();
            markerArrows.Clear();
            markerLabels.Clear();
            markerLabelSources.Clear();
            hud = null;
        }

        private async UniTaskVoid OpenHudAsync()
        {
            try
            {
                // 与 ExplorationHudPresenter 并发打开：UIService 保证返回同一实例。
                ExplorationHudView opened = await ui.OpenAsync<ExplorationHudView>();
                if (disposed) return;
                hud = opened;
                if (hud.CompassRoot == null || hud.CompassMarkerTemplate == null)
                {
                    ReportTemplateMissing();
                }
            }
            catch (ObjectDisposedException)
            {
                // 作用域销毁途中 UIService 已释放，静默。
            }
            catch (Exception e)
            {
                // 万向标只是提示，开不出来不影响玩法，记 Error 不崩。
                telemetry.TrackError("compass_open_failed", e);
                Log.Error($"ExplorationCompassPresenter：拿探索 HUD 失败：{e}");
            }
        }

        private void HandleHudChanged(bool hidden)
        {
            hudHidden = hidden;
            // HUD 的 ControlsRoot 已随沉浸整体隐藏；这里同步收起标记，退出沉浸后下一帧重摆。
            if (hidden) HideFrom(0);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene(scene);

        // 卸载回调时场景物体已销毁，UnityEngine.Object 的 == null 能认出来。
        private void OnSceneUnloaded(Scene scene)
        {
            for (int i = points.Count - 1; i >= 0; i--)
            {
                if (points[i] == null)
                {
                    points.RemoveAt(i);
                    pointLabels.RemoveAt(i);
                }
            }
        }

        // 只在场景加载时跑一次，不在每帧路径上；标签在这里截好，Tick 不再拼字符串。
        private void BindScene(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ExplorationPointOfInterest point in root.GetComponentsInChildren<ExplorationPointOfInterest>(true))
                {
                    if (points.Contains(point)) continue;
                    points.Add(point);
                    pointLabels.Add(TrimLabel(point.Label, config.CompassLabelMax));
                    count++;
                }
            }

            if (count > 0)
            {
                telemetry.Track("poi_bound", ("count", count));
                Log.Debug($"ExplorationCompassPresenter：场景 {scene.name} 登记了 {count} 个兴趣点");
            }
        }

        /// <summary>按最大字数截断标签；max ≤ 0 或标签为空返回空串（不显示标签）。</summary>
        public static string TrimLabel(string label, int max)
        {
            if (string.IsNullOrEmpty(label) || max <= 0) return string.Empty;
            return label.Length <= max ? label : label.Substring(0, max);
        }

        // 池里没有第 index 个就从模板复制一个；模板缺失返回 false（本帧不再画）。
        private bool EnsureMarker(int index)
        {
            if (index < markerRoots.Count) return true;
            RectTransform template = hud.CompassMarkerTemplate;
            RectTransform parent = hud.CompassRoot;
            if (template == null || parent == null)
            {
                ReportTemplateMissing();
                return false;
            }

            // 只在池扩容时实例化与取组件，不在稳定帧路径上。
            RectTransform instance = Object.Instantiate(template, parent, false);
            instance.name = template.name + "_" + index;
            Image arrow = instance.GetComponentInChildren<Image>(true);
            TMP_Text label = instance.GetComponentInChildren<TMP_Text>(true);
            markerRoots.Add(instance);
            markerArrows.Add(arrow != null ? arrow.rectTransform : instance);
            markerLabels.Add(label);
            markerLabelSources.Add(null);
            instance.gameObject.SetActive(false);
            return true;
        }

        private void PlaceMarker(int index, Vector2 position, float angle, string label)
        {
            RectTransform root = markerRoots[index];
            if (root == null) return;
            root.anchoredPosition = position;
            RectTransform arrow = markerArrows[index];
            if (arrow != null) arrow.localEulerAngles = new Vector3(0f, 0f, angle);

            TMP_Text text = markerLabels[index];
            if (text != null)
            {
                // 标签朝画布中心一侧偏移，贴边时不被裁掉；只在换了兴趣点时写 TMP。
                Vector2 inward = position.sqrMagnitude > 1e-6f ? -position.normalized : Vector2.up;
                text.rectTransform.anchoredPosition = inward * LabelOffset;
                if (!ReferenceEquals(markerLabelSources[index], label))
                {
                    markerLabelSources[index] = label;
                    text.text = label;
                    bool show = label.Length > 0;
                    if (text.gameObject.activeSelf != show) text.gameObject.SetActive(show);
                }
            }

            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        }

        private void HideFrom(int start)
        {
            for (int i = start; i < markerRoots.Count; i++)
            {
                RectTransform root = markerRoots[i];
                if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
            }

            shownCount = start < markerRoots.Count ? start : markerRoots.Count;
        }

        private void ReportTemplateMissing()
        {
            if (templateMissingReported) return;
            templateMissingReported = true;
            telemetry.TrackWarn("compass_template_missing");
            Log.Error("ExplorationCompassPresenter：ExplorationHudView 的 compassRoot / compassMarkerTemplate 未接线，万向标不会显示。");
        }
    }
}
