// 职责：生成独立四轨试玩场景及其资产。现有模块生成器不含音游内容，扩展通用生成器会混入曲目专用布局。
using System;
using System.IO;
using System.Linq;
using Game.Core.Boot;
using Game.Rhythm;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Game.Editor.Rhythm
{
    public static class RhythmDemoBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/RhythmDemo.unity";
        private const string DataRoot = "Assets/_Project/Data/Rhythm";
        private const string ConfigPath = DataRoot + "/ChongErFei.asset";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/RhythmView.prefab";
        private const string PromptPath = "Assets/_Project/Prefabs/UI/Rhythm/RhythmQtePrompt.prefab";
        private static TMP_FontAsset font;

        [MenuItem("21Days/音游/应用场景QTE与锅碗瓢盆")]
        public static void ApplySceneQteDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出播放模式");
            const string path = DataRoot + "/GuoWanPiaoPen.asset";
            var config = AssetDatabase.LoadAssetAtPath<RhythmConfig>(path);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<RhythmConfig>();
                JsonUtility.FromJsonOverwrite(File.ReadAllText("PRP/rhythm-guowanpiaopen-20261020/chart.json"), config);
                var data = new SerializedObject(config);
                data.FindProperty("song").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Rhythm/GuoWanPiaoPen.mp3");
                data.FindProperty("input").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DataRoot + "/RhythmInput.asset");
                data.ApplyModifiedPropertiesWithoutUndo();
                var validation = config.CreateQtePilotCopy();
                try { validation.CreateRules(0, null); }
                finally { Object.DestroyImmediate(validation); }
                AssetDatabase.CreateAsset(config, path);
            }
            ApplyQtePromptArt();
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var installer = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RhythmInstaller>(true)).Single();
                var data = new SerializedObject(installer);
                data.FindProperty("config").objectReferenceValue = config;
                data.FindProperty("qtePilot").boolValue = true;
                data.FindProperty("catalog").objectReferenceValue = null;
                data.ApplyModifiedPropertiesWithoutUndo();
                if (!scene.GetRootGameObjects().Any(r => r.name == "QteInnBackdrop")) CreateInnBackdrop(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("音游 Demo 保存失败");
                AssetDatabase.SaveAssetIfDirty(config);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreateInnBackdrop(Scene scene)
        {
            const string sourcePath = "Assets/_Project/Scenes/SampleScene.unity";
            var source = SceneManager.GetSceneByPath(sourcePath);
            bool opened = !source.isLoaded;
            if (opened) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
            try
            {
                var demon = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).First(t => t.name == "DemonWorld");
                var backdrop = new GameObject("QteInnBackdrop"); SceneManager.MoveGameObjectToScene(backdrop, scene);
                var area = new Bounds(new Vector3(18, 5, 2), new Vector3(28, 14, 24));
                foreach (var renderer in demon.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (!renderer.enabled || filter == null || filter.sharedMesh == null || renderer.GetComponent<TMP_Text>() != null || !renderer.bounds.Intersects(area)) continue;
                    bool visible = true;
                    for (var ancestor = renderer.transform; ancestor != demon; ancestor = ancestor.parent)
                        if (!ancestor.gameObject.activeSelf) { visible = false; break; }
                    if (!visible) continue;
                    // 仅复用局部可见网格与材质，不复制 NPC、剧情、碰撞或第二套世界逻辑。
                    var node = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer));
                    node.layer = renderer.gameObject.layer;
                    node.transform.SetParent(backdrop.transform, false);
                    node.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                    node.transform.localScale = renderer.transform.lossyScale;
                    node.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var copy = node.GetComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                    copy.shadowCastingMode = renderer.shadowCastingMode; copy.receiveShadows = renderer.receiveShadows;
                }
                foreach (var actor in new[] { new { Name = "Chibi_player", X = 16.7f }, new { Name = "Chibi_yuezheng", X = 19.1f } })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Characters/" + actor.Name + ".prefab");
                    if (prefab == null) throw new InvalidOperationException("缺少已接入人物：" + actor.Name);
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, backdrop.transform);
                    visual.transform.position = new Vector3(actor.X, 4.9f, -2.6f);
                }
                var stageCamera = demon.Find("InnBattleStage").GetComponentInChildren<Camera>(true);
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                camera.transform.SetPositionAndRotation(stageCamera.transform.position, stageCamera.transform.rotation);
                camera.orthographic = false; camera.fieldOfView = stageCamera.fieldOfView;
                camera.GetUniversalAdditionalCameraData().SetRenderer(1);
            }
            finally { if (opened) EditorSceneManager.CloseScene(source, true); }
        }


        [MenuItem("21Days/音游/虫儿飞试水/启用节拍提示")]
        public static void EnableQtePilot() => SetQtePilot(true);

        [MenuItem("21Days/音游/虫儿飞试水/恢复原版入口")]
        public static void DisableQtePilot() => SetQtePilot(false);

        private static void SetQtePilot(bool enabled)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出播放模式");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("请先打开独立 RhythmDemo 场景，不改其他场景入口");
            RhythmInstaller target = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var installer in root.GetComponentsInChildren<RhythmInstaller>(true))
                {
                    if (target != null) throw new InvalidOperationException("场景中存在多个音游入口，请先检查接线");
                    target = installer;
                }
            if (target == null) throw new InvalidOperationException("场景中没有 RhythmInstaller");
            var data = new SerializedObject(target);
            var chart = data.FindProperty("config").objectReferenceValue as RhythmConfig;
            if (enabled && (chart == null || !chart.SupportsQtePilot))
                throw new InvalidOperationException("试水入口必须引用已接入的 QTE 曲目");
            if (data.FindProperty("qtePilot").boolValue == enabled) return;
            Undo.RecordObject(target, enabled ? "启用虫儿飞节拍提示" : "恢复音游原版入口");
            data.FindProperty("qtePilot").boolValue = enabled;
            data.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            // 不自动保存：保留开发者原有的场景未保存内容和撤销能力。
        }

        [MenuItem("21Days/音游/迁移选中谱面到毫秒记录 v2")]
        public static void MigrateSelectedChart()
        {
            var config = Selection.activeObject as RhythmConfig;
            if (config == null) throw new InvalidOperationException("请先选择 RhythmConfig 谱面资产");
            MigrateChart(config);
        }
        public static void MigrateChart(RhythmConfig config)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("播放期间不迁移谱面");
            if (config.SchemaVersion == 2) return;
            var rules = config.CreateRules(0, null);
            Undo.RecordObject(config, "迁移音游谱面 v2");
            var data = new SerializedObject(config);
            var records = data.FindProperty("notes"); records.arraySize = rules.Count;
            for (int i = 0; i < rules.Count; i++)
            {
                var record = records.GetArrayElementAtIndex(i);
                record.FindPropertyRelative("id").stringValue = rules.NoteId(i);
                record.FindPropertyRelative("lane").intValue = rules.NoteLane(i);
                record.FindPropertyRelative("timeMs").doubleValue = (rules.NoteTime(i) * 1000) - config.ChartOffsetMs;
                record.FindPropertyRelative("type").enumValueIndex = 0;
                record.FindPropertyRelative("durationMs").doubleValue = 0;
            }
            data.FindProperty("noteTimes").arraySize = 0;
            data.FindProperty("noteLanes").arraySize = 0;
            data.FindProperty("schemaVersion").intValue = 2;
            data.ApplyModifiedProperties();
            config.CreateRules(0, null);
            AssetDatabase.SaveAssetIfDirty(config);
        }

        [MenuItem("21Days/音游/创建四轨试玩场景")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("先退出播放模式");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("试玩场景已存在，直接打开；不覆盖现有内容");
            EnsureFolder(DataRoot);
            EnsureFolder("Assets/_Project/Prefabs/UI/Rhythm");
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset");
            var config = AssetDatabase.LoadAssetAtPath<RhythmConfig>(ConfigPath);
            bool newConfig = config == null;
            if (config == null) { config = ScriptableObject.CreateInstance<RhythmConfig>(); AssetDatabase.CreateAsset(config, ConfigPath); }
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DataRoot + "/RhythmInput.asset");
            if (input == null)
            {
                input = ScriptableObject.CreateInstance<InputActionAsset>();
                var map = new InputActionMap("Rhythm");
                string[] keys = { "d", "f", "j", "k" };
                for (int i = 0; i < keys.Length; i++) map.AddAction("Lane" + i, InputActionType.Button, "<Keyboard>/" + keys[i]);
                input.AddActionMap(map);
                AssetDatabase.CreateAsset(input, DataRoot + "/RhythmInput.asset");
            }
            if (newConfig)
            {
            var serialized = new SerializedObject(config);
            serialized.FindProperty("song").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/Rhythm/ChongErFei.mp3");
            serialized.FindProperty("input").objectReferenceValue = input;
            // 仅首次创建的种子；已有谱面资产是唯一来源，重建场景不覆盖其 v2 记录。
            double[] times = { 0.87,1.62,2.29,2.79,3.37,3.95,5.11,5.70,6.28,6.94,7.44,8.02,8.61,9.48,
                11.52,12.10,13.27,13.85,14.43,15.02,15.60,16.18,16.77,17.93,18.51,19.09,19.68,20.26,
                20.84,21.43,22.58,23.76,24.34,24.92,25.50,26.09,26.67,27.25,27.83,28.42,29.00,29.58,
                30.16,30.74,31.36,31.91,32.49,33.47,34.24,34.82,35.41,36.30,36.75,37.45,38.03,38.73 };
            int[] pattern = { 0,1,2,3,2,1,0,2,1,3,0,3,1,2,3,0 };
            var noteTimes = serialized.FindProperty("noteTimes");
            var noteLanes = serialized.FindProperty("noteLanes");
            noteTimes.arraySize = noteLanes.arraySize = times.Length;
            for (int i = 0; i < times.Length; i++)
            {
                noteTimes.GetArrayElementAtIndex(i).doubleValue = times[i];
                noteLanes.GetArrayElementAtIndex(i).intValue = pattern[i % pattern.Length];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            MigrateChart(config);
            }
            ValidateReusableChart(config);
            CreateView();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(PrefabPath), settings.FindGroup("UI"));
            entry.address = "RhythmView";
            EditorUtility.SetDirty(settings);

            Scene previous = SceneManager.GetActiveScene();
            Scene boot = SceneManager.GetSceneByPath("Assets/_Project/Scenes/Boot.unity");
            bool openedBoot = !boot.isLoaded;
            if (openedBoot) boot = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Boot.unity", OpenSceneMode.Additive);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            foreach (var original in boot.GetRootGameObjects())
            {
                if (original.GetComponent<GameBootstrap>() == null && original.GetComponent<Camera>() == null) continue;
                var copy = Object.Instantiate(original);
                copy.name = original.name;
                SceneManager.MoveGameObjectToScene(copy, scene);
                if (copy.GetComponent<GameBootstrap>() != null)
                {
                    foreach (var oldInstaller in copy.GetComponents<GameplayInstaller>()) Object.DestroyImmediate(oldInstaller);
                    var installer = copy.AddComponent<RhythmInstaller>();
                    var installerData = new SerializedObject(installer);
                    installerData.FindProperty("config").objectReferenceValue = config;
                    installerData.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.5f;
            EditorSceneManager.SaveScene(scene, ScenePath);
            entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(ScenePath), settings.FindGroup("Scenes"));
            entry.address = "RhythmDemoScene";
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(config);
            AssetDatabase.SaveAssetIfDirty(input);
            AssetDatabase.SaveAssetIfDirty(settings.FindGroup("UI"));
            AssetDatabase.SaveAssetIfDirty(settings.FindGroup("Scenes"));
            AssetDatabase.SaveAssetIfDirty(settings);
            SceneManager.SetActiveScene(previous);
            if (openedBoot) EditorSceneManager.CloseScene(boot, true);
            EditorSceneManager.CloseScene(scene, true);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        public static void ValidateReusableChart(RhythmConfig config)
        {
            if (config == null || config.Song == null || config.Input == null) throw new InvalidOperationException("已有谱面或资源引用不完整");
            config.CreateRules(0, null);
            config.CreatePracticeRules(0, null);
        }

        private static void CreateView()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) { ApplyQtePromptArt(); return; }
            var root = new GameObject("RhythmView", typeof(RectTransform), typeof(CanvasGroup));
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var view = root.AddComponent<RhythmView>();
            var data = new SerializedObject(view);
            var background = Box("Background", root.transform, Vector2.zero, new Vector2(1920,1080), new Color(0.035f,0.045f,0.09f));
            Stretch(background.rectTransform);
            Label("Heading", root.transform, new Vector2(-570,345), new Vector2(460,100), "虫儿飞", 60, Color.white);
            Label("Subtitle", root.transform, new Vector2(-570,275), new Vector2(460,60), "四轨试奏 / 40 秒", 27, new Color(0.5f,0.7f,0.9f));
            Label("Instructions", root.transform, new Vector2(-570,80), new Vector2(420,240), "让音符落到横线\n按下对应的键\n\nD    F    J    K\n\n单点 · 无长按", 28, new Color(0.7f,0.78f,0.88f));
            var area = new GameObject("NoteArea", typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(root.transform, false);
            area.anchorMin = area.anchorMax = new Vector2(0.5f,0.5f);
            area.pivot = new Vector2(0.5f,0);
            area.anchoredPosition = new Vector2(0,-330);
            area.sizeDelta = new Vector2(600,650);
            data.FindProperty("noteArea").objectReferenceValue = area;
            var lights = data.FindProperty("laneLights"); lights.arraySize = 4;
            string[] keys = { "D","F","J","K" };
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * 150;
                var color = RhythmView.LaneColor(i); color.a = 0.08f;
                Box("Lane" + i, root.transform, new Vector2(x,-5), new Vector2(140,650), color);
                var glow = Box("Key" + i, root.transform, new Vector2(x,-370), new Vector2(132,56), color);
                lights.GetArrayElementAtIndex(i).objectReferenceValue = glow;
                Label("KeyLabel" + i, root.transform, new Vector2(x,-370), new Vector2(100,50), keys[i], 30, RhythmView.LaneColor(i));
            }
            Box("JudgementLine", root.transform, new Vector2(0,-330), new Vector2(600,4), new Color(0.8f,0.9f,1f));
            Set(data, "scoreLabel", Label("Score", root.transform, new Vector2(570,275), new Vector2(480,140), "000000\n连击 0　最高 0", 38, Color.white));
            Set(data, "feedbackLabel", Label("Feedback", root.transform, new Vector2(570,75), new Vector2(500,140), "D / F / J / K", 32, RhythmView.LaneColor(0)));
            Set(data, "statusLabel", Label("Status", root.transform, new Vector2(0,390), new Vector2(900,60), "点击开始", 28, Color.white));
            var progress = SliderWidget("Progress", root.transform, new Vector2(0,350), new Vector2(600,6), 0, 1, false);
            progress.interactable = false;
            Set(data, "progressSlider", progress);
            Set(data, "offsetLabel", Label("OffsetLabel", root.transform, new Vector2(-570,-245), new Vector2(460,90), "延迟补偿 0 ms", 23, new Color(0.65f,0.76f,0.9f)));
            Set(data, "offsetSlider", SliderWidget("Offset", root.transform, new Vector2(-570,-330), new Vector2(400,26), -300, 300, true));
            Label("OffsetHint", root.transform, new Vector2(-570,-390), new Vector2(430,55), "调整后点击开始或返回保存", 20, new Color(0.5f,0.6f,0.75f));
            Button start = ButtonWidget("StartButton", root.transform, new Vector2(570,-180), "开始演奏", RhythmView.LaneColor(0));
            Set(data, "startButton", start);
            Set(data, "startLabel", start.GetComponentInChildren<TMP_Text>());
            Set(data, "backButton", ButtonWidget("BackButton", root.transform, new Vector2(570,-280), "返回标题", new Color(0.2f,0.27f,0.4f)));
            var result = Box("ResultPanel", root.transform, Vector2.zero, new Vector2(570,400), new Color(0.06f,0.08f,0.16f,0.98f));
            Set(data, "resultPanel", result.gameObject);
            Set(data, "resultLabel", Label("Result", result.transform, Vector2.zero, new Vector2(540,370), "演奏完成", 30, Color.white));
            result.gameObject.SetActive(false);
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            ApplyQtePromptArt();
        }
        [MenuItem("21Days/音游/应用八角提示美术")]
        public static void ApplyQtePromptArt()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出播放模式");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/UI/Rhythm/Delivery20261020/qte-keycap.png");
            var keyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/SourceHanSansCN/SourceHanSansCN-Regular SDF.asset");
            if (sprite == null || keyFont == null) throw new InvalidOperationException("八角键帽或思源黑体字体未导入");
            EnsureFolder("Assets/_Project/Prefabs/UI/Rhythm");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PromptPath) == null)
            {
                var root = new GameObject("RhythmQtePrompt", typeof(RectTransform), typeof(CanvasGroup));
                try
                {
                    var group = root.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
                    var face = new GameObject("Face", typeof(RectTransform), typeof(AspectRatioFitter));
                    face.transform.SetParent(root.transform, false);
                    var fitter = face.GetComponent<AspectRatioFitter>(); fitter.aspectRatio = 1; fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    var cap = Box("Keycap", face.transform, Vector2.zero, Vector2.one, Color.white);
                    cap.sprite = sprite; cap.preserveAspect = true;
                    cap.rectTransform.anchorMin = new Vector2(.07f, .07f); cap.rectTransform.anchorMax = new Vector2(.93f, .93f);
                    cap.rectTransform.offsetMin = cap.rectTransform.offsetMax = Vector2.zero;
                    var ring = new GameObject("TimingRing", typeof(RectTransform), typeof(CanvasRenderer), typeof(RhythmTrackGraphic));
                    ring.transform.SetParent(face.transform, false); Stretch(ring.GetComponent<RectTransform>());
                    ring.GetComponent<RhythmTrackGraphic>().Configure(false);
                    var key = new GameObject("PromptKey", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                    key.transform.SetParent(face.transform, false);
                    key.rectTransform.anchorMin = new Vector2(.3f, .25f); key.rectTransform.anchorMax = new Vector2(.7f, .75f);
                    key.rectTransform.offsetMin = key.rectTransform.offsetMax = Vector2.zero;
                    key.font = keyFont; key.fontStyle = FontStyles.Bold; key.color = new Color32(151, 226, 248, 255);
                    key.alignment = TextAlignmentOptions.MidlineGeoAligned; key.margin = Vector4.zero;
                    key.enableAutoSizing = true; key.fontSizeMin = 8; key.fontSizeMax = 100;
                    key.enableWordWrapping = false; key.raycastTarget = false; key.text = "F";
                    var hint = Object.Instantiate(key, face.transform); hint.name = "HoldHint"; hint.text = "按住";
                    hint.fontStyle = FontStyles.Normal; hint.fontSizeMax = 22; hint.fontSizeMin = 8;
                    hint.rectTransform.anchorMin = new Vector2(.05f, -.18f); hint.rectTransform.anchorMax = new Vector2(.95f, -.02f);
                    hint.rectTransform.offsetMin = hint.rectTransform.offsetMax = Vector2.zero;
                    PrefabUtility.SaveAsPrefabAsset(root, PromptPath);
                }
                finally { Object.DestroyImmediate(root); }
            }
            var viewRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var data = new SerializedObject(viewRoot.GetComponent<RhythmView>());
                data.FindProperty("qtePromptPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PromptPath).GetComponent<RectTransform>();
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(viewRoot, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(viewRoot); }
        }
        private static void Set(SerializedObject data, string property, Object value) => data.FindProperty(property).objectReferenceValue = value;
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static Image Box(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent,false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f,0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
        }
        private static TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, string text, float fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent,false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f,0.5f); rect.anchoredPosition = position; rect.sizeDelta = size;
            var label = go.GetComponent<TextMeshProUGUI>(); label.font = font; label.text = text; label.fontSize = fontSize;
            label.color = color; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false; return label;
        }
        private static Button ButtonWidget(string name, Transform parent, Vector2 position, string text, Color color)
        {
            var image = Box(name,parent,position,new Vector2(330,72),color); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            Label("Label",image.transform,Vector2.zero,new Vector2(320,70),text,28,Color.white); return button;
        }
        private static Slider SliderWidget(string name, Transform parent, Vector2 position, Vector2 size, float min, float max, bool handle)
        {
            var background = Box(name,parent,position,size,new Color(0.12f,0.17f,0.25f)); background.raycastTarget = handle;
            var slider = background.gameObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = handle;
            var fill = Box("Fill",background.transform,Vector2.zero,size,RhythmView.LaneColor(0)); Stretch(fill.rectTransform); slider.fillRect = fill.rectTransform;
            if (handle) { var knob = Box("Handle",background.transform,Vector2.zero,new Vector2(20,38),Color.white); knob.raycastTarget = true; slider.handleRect = knob.rectTransform; slider.targetGraphic = knob; }
            slider.SetValueWithoutNotify(0); return slider;
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); EnsureFolder(path.Substring(0,slash)); AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));
        }
    }
}
