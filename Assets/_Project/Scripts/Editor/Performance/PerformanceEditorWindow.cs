// 职责：菜单「21Days/演出/演出编辑器」——给动画师的演出总览：左栏列出全部演出（id / 时长 / 轨道摘要 / 校验状态），
//   右栏看选中演出的校验结果，并提供「打开时间轴」「定位资产」「校验」「试播（Play 模式）」与新建演出。
//
// 本窗口不含业务判定：建模板走 PerformanceTemplateFactory，校验走 PerformanceValidator，试播走运行中的 IPerformanceService，
//   窗口只负责列出、画结果、把点击转成对它们的调用（同 ReplayWindow 的分工）。
//
// 为什么新建（复用 → 扩展 → 新建）：AssetAuditWindow 是通用资产体检、ReplayWindow 是回放控制，名字和职责都装不下
//   「演出列表 + 新建 + 打开时间轴 + 试播」；也没有别的窗口认识演出预制体，只能新建。
using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using Game.Core.Boot;
using Game.Core.Logging;
using Game.Performance;
using Game.Performance.Timeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using VContainer;

namespace Game.Editor.Performance
{
    /// <summary>演出编辑器窗口（IMGUI 两栏）。</summary>
    public sealed class PerformanceEditorWindow : EditorWindow
    {
        private const double ResolveIntervalSeconds = 0.5d;
        private const float ListWidth = 300f;
        private const float DotSize = 10f;

        private const string HelpText =
            "一段演出 = 预制体 + 时间轴；动作用 Animation 轨绑演员的 Animator";

        /// <summary>左栏一行的缓存数据。</summary>
        private sealed class Row
        {
            public string Id;
            public string Path;
            public GameObject Prefab;
            public double Duration;
            public int SubtitleCount;
            public int HoldCount;
            public List<PerformanceIssue> Issues;
        }

        private readonly List<Row> rows = new List<Row>();
        private int selected = -1;
        private string newId = string.Empty;
        private string createMessage = string.Empty;
        private bool needsRefresh = true;
        private Vector2 listScroll;
        private Vector2 detailScroll;

        private IPerformanceService service;
        private string serviceHint = string.Empty;
        private string playMessage = string.Empty;
        private double nextResolveTime;

        [MenuItem("21Days/演出/演出编辑器", false, 400)]
        public static void Open()
        {
            PerformanceEditorWindow window = GetWindow<PerformanceEditorWindow>(false, "演出编辑器", true);
            window.minSize = new Vector2(640f, 360f);
            window.Show();
        }

        /// <summary>
        /// 打开演出预制体的时间轴：进预制体模式 → 选中舞台的 PlayableDirector → Timeline 窗口显示它（拖时间线即预览）。
        /// </summary>
        internal static void OpenTimeline(GameObject prefabAsset)
        {
            if (prefabAsset == null) return;
            string path = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(path))
            {
                // 已是场景物体或预制体模式里的内容，直接对它开时间轴。
                ShowTimelineFor(prefabAsset);
                return;
            }

            AssetDatabase.OpenAsset(prefabAsset);
            PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage == null || prefabStage.assetPath != path)
            {
                Log.Warn($"没能进入预制体模式：{path}");
                return;
            }
            ShowTimelineFor(prefabStage.prefabContentsRoot);
        }

        /// <summary>选中物体上的 PlayableDirector 并让 Timeline 窗口显示它（TimelineEditor.GetOrCreateWindow + SetTimeline）。</summary>
        internal static void ShowTimelineFor(GameObject root)
        {
            if (root == null) return;
            var stage = root.GetComponent<PerformanceStage>();
            PlayableDirector director = stage != null && stage.Director != null ? stage.Director : root.GetComponentInChildren<PlayableDirector>(true);
            if (director == null)
            {
                Log.Warn($"「{root.name}」上找不到 PlayableDirector，打不开时间轴。", root);
                return;
            }

            Selection.activeObject = director.gameObject;
            TimelineEditorWindow timelineWindow = TimelineEditor.GetOrCreateWindow();
            timelineWindow.SetTimeline(director);
            timelineWindow.Show();
            timelineWindow.Focus();
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += MarkDirty;
            needsRefresh = true;
            nextResolveTime = 0d;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= MarkDirty;
            service = null;
        }

        private void MarkDirty()
        {
            needsRefresh = true;
            Repaint();
        }

        private void Update()
        {
            if (!EditorApplication.isPlaying)
            {
                if (service != null)
                {
                    // 退出 Play 后容器已销毁，缓存的服务是死对象。
                    service = null;
                    Repaint();
                }
                return;
            }

            if (service != null || EditorApplication.timeSinceStartup < nextResolveTime) return;
            nextResolveTime = EditorApplication.timeSinceStartup + ResolveIntervalSeconds;
            ResolveService();
            Repaint();
        }

        private void OnGUI()
        {
            if (needsRefresh) RefreshRows();

            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawDetail();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(HelpText, MessageType.Info);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(48f))) RefreshRows();
            GUILayout.Label("新建演出…", GUILayout.Width(64f));
            newId = EditorGUILayout.TextField(newId, EditorStyles.toolbarTextField, GUILayout.Width(200f));

            string hint = ValidateNewId(newId);
            using (new EditorGUI.DisabledScope(hint != null))
            {
                if (GUILayout.Button("创建", EditorStyles.toolbarButton, GUILayout.Width(48f))) CreatePerformance(newId);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(newId) && hint != null)
                EditorGUILayout.HelpBox(hint, MessageType.Warning);
            if (!string.IsNullOrEmpty(createMessage))
                EditorGUILayout.HelpBox(createMessage, MessageType.Info);
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            if (rows.Count == 0)
                EditorGUILayout.HelpBox($"{PerformanceTemplateOptions.DefaultPrefabFolder} 下还没有演出。在上方输入 id 新建一个。", MessageType.None);

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                Rect rect = EditorGUILayout.BeginHorizontal(i == selected ? EditorStyles.helpBox : GUIStyle.none);
                Rect dot = GUILayoutUtility.GetRect(DotSize, DotSize, GUILayout.Width(DotSize + 4f), GUILayout.Height(DotSize * 3f));
                EditorGUI.DrawRect(new Rect(dot.x + 2f, dot.center.y - DotSize * 0.5f, DotSize, DotSize), StatusColor(row.Issues));
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(row.Id, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"{row.Duration:0.##} 秒 · 字幕 {row.SubtitleCount} 条 / 停顿 {row.HoldCount} 个",
                    EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    selected = i;
                    playMessage = string.Empty;
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical();
            if (selected < 0 || selected >= rows.Count)
            {
                EditorGUILayout.HelpBox("左边选一段演出。", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            Row row = rows[selected];
            EditorGUILayout.LabelField(row.Id, EditorStyles.largeLabel);
            EditorGUILayout.LabelField(row.Path, EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开时间轴")) OpenTimeline(row.Prefab);
            if (GUILayout.Button("定位资产")) EditorGUIUtility.PingObject(row.Prefab);
            if (GUILayout.Button("校验"))
            {
                row.Issues = PerformanceValidator.Validate(row.Prefab, row.Id);
            }
            EditorGUILayout.EndHorizontal();

            DrawPlayControls(row);

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            DrawIssues(row.Issues);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPlayControls(Row row)
        {
            bool canPlay = EditorApplication.isPlaying && service != null;
            using (new EditorGUI.DisabledScope(!canPlay || service.IsRunning))
            {
                if (GUILayout.Button("试播")) PlayAsync(row.Id).Forget();
            }

            if (!EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("试播要在 Play 模式下用：从 Boot 进游戏后才有演出服务。", MessageType.None);
            else if (service == null)
                EditorGUILayout.HelpBox($"从 Boot 进游戏后才有演出服务。{serviceHint}", MessageType.Warning);
            if (!string.IsNullOrEmpty(playMessage))
                EditorGUILayout.HelpBox(playMessage, MessageType.Info);
        }

        /// <summary>画校验结果；点一条定位到它的对象。</summary>
        internal static void DrawIssues(List<PerformanceIssue> issues)
        {
            if (issues == null) return;
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("校验通过。", MessageType.Info);
                return;
            }

            foreach (PerformanceIssue issue in issues)
            {
                MessageType type = issue.IsError ? MessageType.Error : issue.IsInfo ? MessageType.Info : MessageType.Warning;
                EditorGUILayout.HelpBox($"{issue.Message}（{issue.Code}）", type);
                Rect rect = GUILayoutUtility.GetLastRect();
                if (issue.Context != null && Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    EditorGUIUtility.PingObject(issue.Context);
                    Selection.activeObject = issue.Context;
                    Event.current.Use();
                }
            }
        }

        private async UniTaskVoid PlayAsync(string id)
        {
            playMessage = $"正在试播「{id}」…";
            try
            {
                PerformanceResult result = await service.PlayAsync(id);
                playMessage = $"试播结束：{result.Outcome}，{result.DurationSeconds:0.##} 秒。";
            }
            catch (Exception e)
            {
                playMessage = $"试播失败：{e.GetType().Name}：{e.Message}";
                Log.Warn($"演出编辑器试播「{id}」失败：{e}");
            }
            Repaint();
        }

        private void CreatePerformance(string id)
        {
            try
            {
                PerformanceTemplateResult result = PerformanceTemplateFactory.Create(id, new PerformanceTemplateOptions());
                createMessage = $"已新建：{result.PrefabPath}" + (result.Registered ? "（已登记 Addressables）" : "（未登记 Addressables）");
                newId = string.Empty;
                RefreshRows();
                selected = rows.FindIndex(r => r.Path == result.PrefabPath);
                if (selected >= 0) OpenTimeline(rows[selected].Prefab);
            }
            catch (Exception e)
            {
                createMessage = $"新建失败：{e.Message}";
                Log.Warn($"新建演出「{id}」失败：{e}");
            }
            GUIUtility.ExitGUI();
        }

        private static string ValidateNewId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "输入演出 id（小写字母、数字、下划线，如 perf_intro）。";
            if (!PerformanceTemplateFactory.IsValidId(id)) return "id 只能用小写字母、数字和下划线。";
            string path = $"{PerformanceTemplateOptions.DefaultPrefabFolder}/{id}.prefab";
            if (File.Exists(path)) return $"已存在同名演出：{path}";
            return null;
        }

        private void RefreshRows()
        {
            needsRefresh = false;
            string selectedPath = selected >= 0 && selected < rows.Count ? rows[selected].Path : null;
            rows.Clear();
            selected = -1;

            string folder = PerformanceTemplateOptions.DefaultPrefabFolder;
            if (!AssetDatabase.IsValidFolder(folder)) return;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var stage = prefab.GetComponent<PerformanceStage>();
                if (stage == null) continue;

                string id = Path.GetFileNameWithoutExtension(path);
                var row = new Row { Id = id, Path = path, Prefab = prefab };
                FillSummary(row, stage);
                row.Issues = PerformanceValidator.Validate(prefab, id);
                rows.Add(row);
                if (path == selectedPath) selected = rows.Count - 1;
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (selectedPath != null) selected = rows.FindIndex(r => r.Path == selectedPath);
        }

        private static void FillSummary(Row row, PerformanceStage stage)
        {
            PlayableDirector director = stage.Director;
            var timeline = director == null ? null : director.playableAsset as TimelineAsset;
            if (timeline == null) return;
            row.Duration = timeline.duration;

            var visited = new HashSet<TrackAsset>();
            if (timeline.markerTrack != null)
            {
                visited.Add(timeline.markerTrack);
                row.HoldCount += CountHolds(timeline.markerTrack);
            }
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track == null || !visited.Add(track)) continue;
                row.HoldCount += CountHolds(track);
                if (track is SubtitleTrack) row.SubtitleCount += CountClips(track);
            }
        }

        private static int CountClips(TrackAsset track)
        {
            int count = 0;
            foreach (TimelineClip unused in track.GetClips()) count++;
            return count;
        }

        private static int CountHolds(TrackAsset track)
        {
            int count = 0;
            foreach (IMarker marker in track.GetMarkers())
            {
                if (marker is HoldMarker) count++;
            }
            return count;
        }

        private static Color StatusColor(List<PerformanceIssue> issues)
        {
            if (issues == null) return Color.gray;
            bool warning = false;
            foreach (PerformanceIssue issue in issues)
            {
                if (issue.IsError) return new Color(0.9f, 0.3f, 0.3f);
                if (!issue.IsInfo) warning = true;
            }
            return warning ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.35f, 0.8f, 0.4f);
        }

        private void ResolveService()
        {
            try
            {
                // 容器挂在 GameBootstrap 物体上（DontDestroyOnLoad），播放期间一直找得到（同 ReplayWindow）。
                GameLifetimeScope scope = FindObjectOfType<GameLifetimeScope>();
                if (scope == null)
                {
                    serviceHint = "当前场景里没有 GameLifetimeScope。";
                    return;
                }
                if (scope.Container == null)
                {
                    serviceHint = "容器还没建好，启动流程可能还在跑。";
                    return;
                }
                if (!scope.Container.TryResolve(out IPerformanceService resolved) || resolved == null)
                {
                    serviceHint = "容器里没有 IPerformanceService（Boot 没挂 PerformanceInstaller？）。";
                    return;
                }
                service = resolved;
                serviceHint = string.Empty;
            }
            catch (Exception e)
            {
                // 退出 Play 那一下容器正在销毁，解析会抛，按「暂时连不上」处理。
                serviceHint = $"取演出服务时出错：{e.GetType().Name}：{e.Message}";
            }
        }
    }
}
