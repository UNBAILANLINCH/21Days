// 职责：一步建齐一段新演出——时间轴资产（字幕 / 表情 / 动作 / 音效四条轨）、舞台预制体（PerformanceStage + PlayableDirector
//   + 舞台相机 + 占位演员，轨道绑定已接好）、整棵树设到 Performance 图层、登记 Addressables Performance 组（地址 = id）。
//
// 副作用说明（project-guide 硬规则 3）：
//   · 图层 Performance 不存在时，会用 TagManager 的 SerializedObject 在第一个空槽（≥ 8）建它——这是写
//     ProjectSettings/TagManager.asset，只在层缺失时写一次，写了会 Log；层已存在则不碰。
//   · RegisterAddressable 时 Performance 组不存在会新建（schema 照抄 UI 组），并写 AddressableAssetsData。
//   · 只保存本工厂建出 / 改过的资产（SaveAssetIfDirty），不调 AssetDatabase.SaveAssets，免得顺手保存别人未保存的改动。
//
// 为什么新建（复用 → 扩展 → 新建）：ProjectStructureMenu 只建目录与脚本骨架，不会建时间轴与带绑定的预制体；
//   手工搭一段演出要十几步（建轨、绑定、层、相机类型、登记地址），漏一步就播不出来，需要一个专门的模板工厂。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Game.Core.Logging;
using Game.Performance;
using Game.Performance.Timeline;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Game.Editor.Performance
{
    /// <summary>演出模板工厂。id 即 Addressables 地址、预制体名与时间轴名（小写字母 / 数字 / 下划线）。</summary>
    public static class PerformanceTemplateFactory
    {
        public const string AddressableGroupName = "Performance";
        public const string SubtitleTrackName = "字幕";
        public const string ExpressionTrackName = "表情";
        public const string AnimationTrackName = "动作";
        public const string AudioTrackName = "音效";
        public const double DefaultDurationSeconds = 8d;

        private const string SchemaTemplateGroupName = "UI";
        private const string TagManagerPath = "ProjectSettings/TagManager.asset";
        private const int FirstUserLayer = 8;
        private const float StageCameraOrthoSize = 5f;
        private const float StageCameraDepth = 10f;
        private const float StageCameraZ = -10f;

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_]+$");

        /// <summary>id 是否合法（非空、只含小写字母 / 数字 / 下划线）。</summary>
        public static bool IsValidId(string id) => !string.IsNullOrEmpty(id) && IdPattern.IsMatch(id);

        /// <summary>
        /// 新建一段演出。
        /// </summary>
        /// <exception cref="ArgumentException">id 非法，或同名预制体 / 时间轴已存在。</exception>
        /// <exception cref="InvalidOperationException">图层表已满建不了 Performance 层，或预制体保存失败。</exception>
        public static PerformanceTemplateResult Create(string id, PerformanceTemplateOptions options)
        {
            if (!IsValidId(id))
                throw new ArgumentException($"演出 id「{id}」不合法：只能用小写字母、数字和下划线。", nameof(id));

            options = options ?? new PerformanceTemplateOptions();
            string prefabFolder = TrimFolder(options.PrefabFolder, PerformanceTemplateOptions.DefaultPrefabFolder);
            string timelineFolder = TrimFolder(options.TimelineFolder, PerformanceTemplateOptions.DefaultTimelineFolder);
            string prefabPath = $"{prefabFolder}/{id}.prefab";
            string timelinePath = $"{timelineFolder}/{id}.playable";

            if (AssetDatabase.LoadAssetAtPath<Object>(prefabPath) != null || File.Exists(prefabPath))
                throw new ArgumentException($"已存在同名演出预制体：{prefabPath}", nameof(id));
            if (AssetDatabase.LoadAssetAtPath<Object>(timelinePath) != null || File.Exists(timelinePath))
                throw new ArgumentException($"已存在同名时间轴：{timelinePath}", nameof(id));

            int layer = EnsurePerformanceLayer();
            EnsureFolder(prefabFolder);
            EnsureFolder(timelineFolder);

            TimelineAsset timeline = CreateTimeline(id, timelinePath, out TrackAsset expressionTrack, out TrackAsset animationTrack);
            BuildPrefab(id, prefabPath, layer, timeline, expressionTrack, animationTrack, options.ActorSprites);
            ClearTimelineUndo(timeline);

            bool registered = options.RegisterAddressable && RegisterAddressable(prefabPath, id);
            Log.Info($"已新建演出「{id}」：{prefabPath}、{timelinePath}" + (registered ? "，已登记 Addressables。" : "。"));
            return new PerformanceTemplateResult(prefabPath, timelinePath, registered);
        }

        private static TimelineAsset CreateTimeline(
            string id, string timelinePath, out TrackAsset expressionTrack, out TrackAsset animationTrack)
        {
            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = id;
            AssetDatabase.CreateAsset(timeline, timelinePath);

            // 资产落盘后再建轨：CreateTrack 会把轨道作为子资产存进时间轴文件。
            timeline.CreateTrack<SubtitleTrack>(null, SubtitleTrackName);
            expressionTrack = timeline.CreateTrack<ExpressionTrack>(null, ExpressionTrackName);
            animationTrack = timeline.CreateTrack<AnimationTrack>(null, AnimationTrackName);
            timeline.CreateTrack<AudioTrack>(null, AudioTrackName);
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = DefaultDurationSeconds;

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
            return timeline;
        }

        private static void BuildPrefab(
            string id,
            string prefabPath,
            int layer,
            TimelineAsset timeline,
            TrackAsset expressionTrack,
            TrackAsset animationTrack,
            IReadOnlyList<SpritePerformanceActor.ExpressionEntry> actorSprites)
        {
            // 在预览场景里搭临时物体：不往用户当前打开的场景里塞东西，也就不会把它标脏。
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(id);
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var director = root.AddComponent<PlayableDirector>();
                director.playableAsset = timeline;
                director.playOnAwake = false;
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                director.extrapolationMode = DirectorWrapMode.None;
                var stage = root.AddComponent<PerformanceStage>();

                Camera camera = CreateStageCamera(root.transform, layer);

                var actorGo = new GameObject("Actor");
                actorGo.transform.SetParent(root.transform, false);
                var spriteRenderer = actorGo.AddComponent<SpriteRenderer>();
                var actor = actorGo.AddComponent<SpritePerformanceActor>();
                var animator = actorGo.AddComponent<Animator>();
                AssignActor(actor, spriteRenderer, actorSprites);

                using (var so = new SerializedObject(stage))
                {
                    so.FindProperty("director").objectReferenceValue = director;
                    so.FindProperty("stageCamera").objectReferenceValue = camera;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                director.SetGenericBinding(expressionTrack, actor);
                director.SetGenericBinding(animationTrack, animator);

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = layer;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success) throw new InvalidOperationException($"保存演出预制体失败：{prefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        /// <summary>
        /// 清掉时间轴资产及其各轨道的 Undo 记录。
        /// 实测：CreateTrack 等 Timeline API 建轨时会把资产推进 Undo 栈；回放阶段发现刚建好的 .playable
        /// 在别的测试运行器收尾回滚 Undo 组时被打回 m_Tracks: [] 的空壳并写盘，故在此清空以免被跨路径的回滚污染。
        /// </summary>
        private static void ClearTimelineUndo(TimelineAsset timeline)
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
                Undo.ClearUndo(track);
            Undo.ClearUndo(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
        }

        private static Camera CreateStageCamera(Transform parent, int layer)
        {
            var cameraGo = new GameObject("StageCamera");
            cameraGo.transform.SetParent(parent, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0f, StageCameraZ);

            // 刻意不加 AudioListener：舞台相机是叠加相机，场景里主相机已有监听器，两个会报警告。
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = StageCameraOrthoSize;
            camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.Depth;
            camera.depth = StageCameraDepth;

            Type dataType = Type.GetType(PerformanceValidator.UrpCameraDataTypeName);
            if (dataType == null)
            {
                Log.Warn("找不到 URP 的 UniversalAdditionalCameraData，舞台相机没能设成 Overlay；请手动把 Render Type 改为 Overlay。", cameraGo);
                return camera;
            }

            Component data = cameraGo.GetComponent(dataType);
            if (data == null) data = cameraGo.AddComponent(dataType);
            using (var so = new SerializedObject(data))
            {
                SerializedProperty prop = so.FindProperty(PerformanceValidator.UrpCameraTypeProperty);
                if (prop != null)
                {
                    prop.intValue = PerformanceValidator.UrpOverlayValue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            return camera;
        }

        private static void AssignActor(
            SpritePerformanceActor actor,
            SpriteRenderer spriteRenderer,
            IReadOnlyList<SpritePerformanceActor.ExpressionEntry> actorSprites)
        {
            int count = actorSprites == null ? 0 : actorSprites.Count;
            using (var so = new SerializedObject(actor))
            {
                so.FindProperty("target").objectReferenceValue = spriteRenderer;
                SerializedProperty list = so.FindProperty("expressions");
                list.arraySize = count;
                for (int i = 0; i < count; i++)
                {
                    SerializedProperty element = list.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("name").stringValue = actorSprites[i].Name ?? string.Empty;
                    element.FindPropertyRelative("sprite").objectReferenceValue = actorSprites[i].Sprite;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // 默认显示第一个表情，排演出时 Scene 视图里就能看见人。
            if (count > 0) spriteRenderer.sprite = actorSprites[0].Sprite;
        }

        /// <summary>把预制体登记进 Addressables Performance 组（地址 = id）；组不存在时照抄 UI 组 schema 新建。示例 builder 共用。</summary>
        internal static bool RegisterAddressable(string prefabPath, string id)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Log.Warn($"工程没有 Addressables 设置，演出「{id}」未登记地址。");
                return false;
            }

            AddressableAssetGroup group = settings.FindGroup(AddressableGroupName);
            if (group == null)
            {
                AddressableAssetGroup template = settings.FindGroup(SchemaTemplateGroupName);
                if (template == null) template = settings.DefaultGroup;
                group = settings.CreateGroup(AddressableGroupName, false, false, true, template.Schemas);
                Log.Info($"已新建 Addressables 组 {AddressableGroupName}（schema 照抄 {template.Name} 组）。");
            }

            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = id;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, true);
            AssetDatabase.SaveAssetIfDirty(group);
            AssetDatabase.SaveAssetIfDirty(settings);
            return true;
        }

        /// <summary>取 Performance 层；不存在就在第一个空的用户层槽（≥ 8）建它。表满抛异常。</summary>
        private static int EnsurePerformanceLayer()
        {
            int existing = LayerMask.NameToLayer(PerformanceValidator.PerformanceLayerName);
            if (existing >= 0) return existing;

            Object[] loaded = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
            if (loaded == null || loaded.Length == 0)
                throw new InvalidOperationException("读不到 TagManager，建不了 Performance 图层。");

            using (var so = new SerializedObject(loaded[0]))
            {
                SerializedProperty layers = so.FindProperty("layers");
                for (int i = FirstUserLayer; i < layers.arraySize; i++)
                {
                    SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                    slot.stringValue = PerformanceValidator.PerformanceLayerName;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Log.Info($"图层 {PerformanceValidator.PerformanceLayerName} 不存在，已建在第 {i} 层（写入 ProjectSettings/TagManager.asset）。");
                    return i;
                }
            }

            throw new InvalidOperationException("图层表已满，建不了 Performance 图层，请手动腾出一个用户层。");
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

        private static string TrimFolder(string folder, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(folder) ? fallback : folder.Replace('\\', '/').TrimEnd('/');
            if (!value.StartsWith("Assets", StringComparison.Ordinal))
                throw new ArgumentException($"目录必须在 Assets/ 下：{value}");
            return value;
        }
    }
}
