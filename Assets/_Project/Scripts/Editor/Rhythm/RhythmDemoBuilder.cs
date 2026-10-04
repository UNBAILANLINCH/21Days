// 职责：生成独立四轨试玩场景及其资产。现有模块生成器不含音游内容，扩展通用生成器会混入曲目专用布局。
using System;
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
using Object = UnityEngine.Object;

namespace Game.Editor.Rhythm
{
    public static class RhythmDemoBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/RhythmDemo.unity";
        private const string DataRoot = "Assets/_Project/Data/Rhythm";
        private const string ConfigPath = DataRoot + "/ChongErFei.asset";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/RhythmView.prefab";
        private static TMP_FontAsset font;

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
            EnsureFolder("Assets/_Project/Prefabs/UI");
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
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
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
