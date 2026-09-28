// 职责：生成 / 原地更新世界舞台示例演出 perf_sample_scene_talk——五个方舟小人站在 SampleScene 村口的灰盒里、
//   透视舞台相机近景微俯、底部对白面板带头像（对标《明日方舟》活动探索截图）。预制体与时间轴存在时就地改内容，GUID 不变。
//
// 为什么新建（复用 → 扩展 → 新建）：
//   复用不行：PerformanceTemplateFactory 只产空的世界舞台壳（空站位根 + 空演员名单 + 三条空轨），
//     且已存在同名资产时直接拒绝，做不到「可重跑、保 GUID」；
//   扩展不行：把「嵌套小人预制体 + 固定台词」塞进模板工厂会让通用工厂背上示例内容，职责说不通。
//   Addressables 登记与舞台相机（默认构图）复用工厂的 RegisterAddressable / CreateWorldStageCamera（internal），不复制。
//
// 副作用：只写本 builder 的两个资产与 Addressables Performance 组条目；不改 Chibi 预制体（只嵌套引用），不碰任何场景。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.CharacterPuppet;
using Game.Core.Logging;
using Game.IsometricExploration;
using Game.Performance;
using Game.Performance.Timeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Game.Editor.Performance.Samples
{
    /// <summary>世界舞台示例演出 builder。菜单 21Days/演出/生成示例·场景对白（世界舞台）。</summary>
    public static class SceneTalkSampleBuilder
    {
        public const string Id = "perf_sample_scene_talk";
        public const string PrefabPath = "Assets/_Project/Prefabs/Performance/" + Id + ".prefab";
        public const string TimelinePath = "Assets/_Project/Data/Performance/Timelines/" + Id + ".playable";

        private const string ChibiFolder = "Assets/_Project/Prefabs/Characters";
        private const string AvatarFolder = "Assets/_Project/Art/Sprites/Characters/Ark/Avatars";
        private const string PuppetVisualName = "PuppetVisual";
        private const string ChibiSpriteChild = "Sprite";

        // ── 构图：世界锚点由场景触发器的 StageAnchor 给（SampleScene (10.5, 4.888, 7.0)，旋转 0）；舞台相机用模板工厂的默认构图
        //    （PerformanceTemplateFactory.DefaultCamera*，就是在本示例上调出来的），改构图改那边。──

        // ── 字幕节奏 ──
        private const double ClipSeconds = 4d;
        private const double ClipGap = 0.3d;
        /// <summary>停顿标记落在片段末尾前多少秒（必须在片段内部，▼ 才会随字幕一起出现）。</summary>
        private const double HoldBeforeClipEnd = 0.1d;
        private const double TailSeconds = 0.5d;

        /// <summary>
        /// 站位（从左到右）：名字、小人 / 头像键、局部 x、局部 z、是否朝左。
        /// 阿米娅与德克萨斯之间留出 1.35 的空：场景锥筒 Cone_3 (9.5, 6.2) 在队列前 0.8 处，投影正落在这道空里，不挡人；
        /// 阿米娅不再往左，免得挨上物资箱 Crate_A (7.5, 5.8)；右边四人间距收到 0.9，让整排重心尽量靠回画面中线。
        /// </summary>
        private static readonly ActorSpec[] Actors =
        {
            new ActorSpec("阿米娅", "amiya", -1.75f, -0.1f, false),
            new ActorSpec("德克萨斯", "texas", -0.4f, 0.05f, false),
            new ActorSpec("能天使", "exusiai", 0.5f, -0.05f, false),
            new ActorSpec("陈", "chen", 1.4f, 0.1f, true),
            new ActorSpec("斯卡蒂", "skadi", 2.3f, 0f, true),
        };

        /// <summary>演员名单顺序（阿米娅、陈、斯卡蒂、德克萨斯、能天使）。</summary>
        private static readonly string[] CastOrder = { "amiya", "chen", "skadi", "texas", "exusiai" };

        private static readonly string[,] Lines =
        {
            { "阿米娅", "您好，请问这里的负责人在哪里？我们想问问能不能在村里借宿几天。" },
            { "陈", "借宿？你们一行人从哪来的，来这儿做什么？" },
            { "德克萨斯", "路过。车坏在坡下面了，修好就走。" },
            { "能天使", "哎呀陈队别这么凶嘛，我们是好人，真的！你看阿米娅的脸就知道了。" },
            { "斯卡蒂", "……先进来吧。夜里风大，坡下面不安全。" },
            { "阿米娅", "谢谢！我们不会给大家添麻烦的。" },
        };

        [MenuItem("21Days/演出/生成示例·场景对白（世界舞台）")]
        private static void BuildFromMenu()
        {
            List<PerformanceIssue> issues = Build();
            int blocking = issues.Count(i => i.Severity != PerformanceIssueSeverity.Info);
            if (blocking == 0) Log.Info($"示例演出 {Id} 已生成，校验无 Error / Warning。");
        }

        /// <summary>生成或原地更新示例演出，返回校验结果（同时逐条打日志）。可重复执行，资产 GUID 不变。</summary>
        public static List<PerformanceIssue> Build()
        {
            TimelineAsset timeline = WriteTimeline();
            WritePrefab(timeline);
            PerformanceTemplateFactory.RegisterAddressable(PrefabPath, Id);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            List<PerformanceIssue> issues = PerformanceValidator.Validate(prefab, Id);
            foreach (PerformanceIssue issue in issues)
            {
                string line = $"[{Id}] {issue.Severity} {issue.Code}：{issue.Message}";
                if (issue.Severity == PerformanceIssueSeverity.Info) Log.Info(line);
                else Log.Warn(line);
            }
            return issues;
        }

        private static TimelineAsset WriteTimeline()
        {
            EnsureFolder(Path.GetDirectoryName(TimelinePath).Replace('\\', '/'));
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                timeline.name = Id;
                AssetDatabase.CreateAsset(timeline, TimelinePath);
            }
            else
            {
                // 原地清空：删掉全部轨道与停顿标记再重建，资产本体（GUID）保留。
                foreach (TrackAsset track in timeline.GetRootTracks().ToList())
                    timeline.DeleteTrack(track);
                if (timeline.markerTrack != null)
                {
                    foreach (IMarker marker in timeline.markerTrack.GetMarkers().ToList())
                        timeline.markerTrack.DeleteMarker(marker);
                }
            }

            var subtitles = timeline.CreateTrack<SubtitleTrack>(null, PerformanceTemplateFactory.SubtitleTrackName);
            timeline.CreateMarkerTrack();
            double start = 0d;
            for (int i = 0; i < Lines.GetLength(0); i++)
            {
                TimelineClip clip = subtitles.CreateClip<SubtitleClip>();
                clip.start = start;
                clip.duration = ClipSeconds;
                clip.displayName = Lines[i, 0];
                using (var so = new SerializedObject(clip.asset))
                {
                    so.FindProperty("speaker").stringValue = Lines[i, 0];
                    so.FindProperty("text").stringValue = Lines[i, 1];
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                timeline.markerTrack.CreateMarker<HoldMarker>(start + ClipSeconds - HoldBeforeClipEnd);
                start += ClipSeconds + ClipGap;
            }

            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = start - ClipGap + TailSeconds;

            // 坑册「Timeline API 建好的时间轴被别的测试运行器回滚 Undo 打成空壳」：清 Undo 再落盘。
            foreach (TrackAsset track in timeline.GetOutputTracks()) Undo.ClearUndo(track);
            if (timeline.markerTrack != null) Undo.ClearUndo(timeline.markerTrack);
            Undo.ClearUndo(timeline);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
            return timeline;
        }

        private static void WritePrefab(TimelineAsset timeline)
        {
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) CreateEmptyPrefab();

            // 离屏副本改内容（坑册「MCP 预制体舞台改动可能不落盘」）：不经预制体舞台，也不在任何场景留实例。
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                root.layer = 0;
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                var director = GetOrAdd<PlayableDirector>(root);
                director.playableAsset = timeline;
                director.playOnAwake = false;
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                director.extrapolationMode = DirectorWrapMode.None;
                var stage = GetOrAdd<PerformanceStage>(root);

                Camera camera = PerformanceTemplateFactory.CreateWorldStageCamera(root.transform);
                CreateActors(root.transform, camera);

                using (var so = new SerializedObject(stage))
                {
                    so.FindProperty("director").objectReferenceValue = director;
                    so.FindProperty("stageCamera").objectReferenceValue = camera;
                    so.FindProperty("skippable").boolValue = true;
                    so.FindProperty("pauseWorld").boolValue = true;
                    so.FindProperty("hideHud").boolValue = true;
                    SerializedProperty cast = so.FindProperty("cast");
                    cast.arraySize = CastOrder.Length;
                    for (int i = 0; i < CastOrder.Length; i++)
                    {
                        ActorSpec spec = Actors.First(a => a.Key == CastOrder[i]);
                        SerializedProperty entry = cast.GetArrayElementAtIndex(i);
                        entry.FindPropertyRelative("speaker").stringValue = spec.Speaker;
                        entry.FindPropertyRelative("avatar").objectReferenceValue = LoadAvatar(spec.Key);
                        // 头像侧按站位：站在锚点左边（x < 0）的头像显示在面板左侧，其余在右侧。
                        entry.FindPropertyRelative("side").enumValueIndex =
                            (int)(spec.X < 0f ? PerformanceAvatarSide.Left : PerformanceAvatarSide.Right);
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("示例演出预制体保存失败：" + PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateActors(Transform parent, Camera camera)
        {
            var actorsGo = new GameObject(PerformanceTemplateFactory.ActorsName);
            actorsGo.transform.SetParent(parent, false);

            foreach (ActorSpec spec in Actors)
            {
                var actor = new GameObject("Actor_" + spec.Speaker);
                actor.transform.SetParent(actorsGo.transform, false);
                actor.transform.localPosition = new Vector3(spec.X, 0f, spec.Z);

                // 坑册「纯纸片 NPC 的 Visual 在半身高」：Billboard 挂在脚底原点的空物体上，小人挂其下。
                var visual = new GameObject(PuppetVisualName);
                visual.transform.SetParent(actor.transform, false);
                var billboard = visual.AddComponent<CameraBillboard>();

                string chibiPath = $"{ChibiFolder}/Chibi_{spec.Key}.prefab";
                var chibiAsset = AssetDatabase.LoadAssetAtPath<GameObject>(chibiPath);
                if (chibiAsset == null) throw new InvalidOperationException("缺小人预制体：" + chibiPath);
                var chibi = (GameObject)PrefabUtility.InstantiatePrefab(chibiAsset, visual.transform);
                chibi.transform.localPosition = Vector3.zero;
                chibi.transform.localRotation = Quaternion.identity;
                chibi.transform.localScale = new Vector3(spec.FaceLeft ? -1f : 1f, 1f, 1f);

                // 舞台小人不移动只播待机：停用位移驱动，免得实例化后被摆到锚点的那一跳被当成「在走」而改朝向 / 切走路。
                var motion = chibi.GetComponent<ChibiPuppetMotion>();
                if (motion != null) motion.enabled = false;

                Transform spriteChild = chibi.transform.Find(ChibiSpriteChild);
                var spriteRenderer = spriteChild == null ? null : spriteChild.GetComponent<SpriteRenderer>();
                using (var so = new SerializedObject(billboard))
                {
                    so.FindProperty("targetCamera").objectReferenceValue = camera;
                    so.FindProperty("visualRenderer").objectReferenceValue = spriteRenderer;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static Sprite LoadAvatar(string key)
        {
            string path = $"{AvatarFolder}/avatar_{key}.png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Log.Warn($"头像缺失或不是 Sprite：{path}");
            return sprite;
        }

        private static void CreateEmptyPrefab()
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(Id);
                SceneManager.MoveGameObjectToScene(root, preview);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("示例演出预制体创建失败：" + PrefabPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private readonly struct ActorSpec
        {
            public readonly string Speaker;
            public readonly string Key;
            public readonly float X;
            public readonly float Z;
            public readonly bool FaceLeft;

            public ActorSpec(string speaker, string key, float x, float z, bool faceLeft)
            {
                Speaker = speaker;
                Key = key;
                X = x;
                Z = z;
                FaceLeft = faceLeft;
            }
        }
    }
}
