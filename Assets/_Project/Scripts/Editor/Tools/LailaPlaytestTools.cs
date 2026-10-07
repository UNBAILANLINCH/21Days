using System;
using Game.LailaFaceRecognition;
using TMPro;
using Unity.Sentis;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Editor.Tools
{
    // 职责：仅打开已接线的独立研究试玩场景，复制／安装只由显式开发操作调用。
    // 新建原因：正式laila安装工具限制原场景，不能承担独立研究场景的开关与配方接线。
    // 恢复载体：菜单发起Play及跨域重载的playModeStateChanged；SessionState键与回调可即时核对。
    // 退场：恢复场景或启动失败后清键并退订；移除菜单时同时移除本恢复机制。
    public static class LailaPlaytestTools
    {
        public const string ScenePath = "Assets/_Project/Scenes/LailaRecognitionPlaytest.unity";
        private const string ResearchPath = "Assets/_Project/Data/LailaFaceRecognition/Research/laila_research59_r779";

        private const string FeedbackPath = "Assets/_Project/Data/LailaFaceRecognition/Research/FeedbackRepair20261005/laila_research59_feedback_20261005";
        private const string FeedbackConfigPath = "Assets/_Project/Data/LailaFaceRecognition/Research/FeedbackRepair20261005/StableFeedback.asset";
        private const string BaselineHash = "d9e70e5c422be33298e0df35a526aeff26f931f63cc7a8870bc327569697a101";
        private const string FeedbackHash = "9d0999f9164dcbb5b00a93a6d5b7082912abf93a5c07f4478966bc631f8733b2";
        private const string StrengthPath = "Assets/_Project/Data/LailaFaceRecognition/Research/StrengthRepair20261006/laila_research59_strength_20261006";
        private const string StrengthHash = "11f828902e4cd26eb6488a1f744a9bad71d9a06ab76108a50dca547b43bf3b3b";
        private const string PreviousScenesKey = "21Days.LailaPlaytest.PreviousScenes";

        [InitializeOnLoadMethod]
        private static void ObservePlaytestExit()
        {
            EditorApplication.playModeStateChanged -= RestorePreviousScenes;
            if (!string.IsNullOrEmpty(SessionState.GetString(PreviousScenesKey, "")))
                EditorApplication.playModeStateChanged += RestorePreviousScenes;
        }

        private static void RestorePreviousScenes(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= RestorePreviousScenes;
            string saved = SessionState.GetString(PreviousScenesKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            SessionState.EraseString(PreviousScenesKey);
            var rows = Newtonsoft.Json.Linq.JArray.Parse(saved);
            var setup = new SceneSetup[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                setup[i] = new SceneSetup { path = (string)rows[i]["path"], isLoaded = (bool)rows[i]["loaded"], isActive = (bool)rows[i]["active"] };
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        [MenuItem("21Days/Laila/候选试玩（含旧版对照）")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先结束当前Play，再打开独立试玩；不会自动停止你的运行");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("当前场景有未保存改动，请先保存或自行处理，再打开试玩");
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("当前有尚未保存的场景，请先自行保存或关闭，再打开试玩");
            }
            // Awake之前隔离其他场景，避免音游Bootstrap生成第二套常驻调试UI。
            // SessionState跨Play域重载保存现场；退出后恢复加载配置，不保存其他场景资产。
            var previous = new Newtonsoft.Json.Linq.JArray();
            foreach (var item in EditorSceneManager.GetSceneManagerSetup())
                previous.Add(new Newtonsoft.Json.Linq.JObject { ["path"] = item.path, ["loaded"] = item.isLoaded, ["active"] = item.isActive });
            SessionState.SetString(PreviousScenesKey, previous.ToString());
            ObservePlaytestExit();
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SceneManager.SetActiveScene(scene);
                Upgrade(scene);
                EditorApplication.isPlaying = true;
            }
            catch
            {
                RestorePreviousScenes(PlayModeStateChange.EnteredEditMode);
                throw;
            }
        }

        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("仅编辑态准备试玩");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                throw new InvalidOperationException("研究场景已存在，拒绝覆盖");
            if (!AssetDatabase.CopyAsset("Assets/_Project/Scenes/laila.unity", ScenePath))
                throw new InvalidOperationException("复制独立研究场景失败");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            LailaExpressionRecognizer recognizer = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var item in root.GetComponentsInChildren<LailaExpressionRecognizer>(true)) recognizer = item;
            if (recognizer == null) throw new InvalidOperationException("研究场景未包含原识别接线");
            var metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(ResearchPath + ".json");
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ResearchPath + ".onnx");
            if (metadata == null || model == null) throw new InvalidOperationException("研究模型尚未导入");
            var meta = Newtonsoft.Json.Linq.JObject.Parse(metadata.text);
            var state = new SerializedObject(recognizer);
            state.FindProperty("researchModelAsset").objectReferenceValue = model;
            state.FindProperty("researchMetadata").objectReferenceValue = metadata;
            state.FindProperty("approvedResearchModelHash").stringValue = (string)meta["onnx"]["sha256"];
            state.FindProperty("useResearchCandidate").boolValue = true;
            state.ApplyModifiedPropertiesWithoutUndo();
            var canvas = recognizer.GetComponentInChildren<Canvas>(true);
            var panel = new GameObject("PlaytestControls", typeof(RectTransform), typeof(Image), typeof(LailaExpressionPlaytest));
            panel.transform.SetParent(canvas.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(28, -28); rect.sizeDelta = new Vector2(410, 430);
            panel.GetComponent<Image>().color = new Color(.09f, .07f, .065f, .93f);
            panel.GetComponent<Image>().raycastTarget = false;
            var controls = panel.GetComponent<LailaExpressionPlaytest>();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset");
            Label(panel.transform, font, "直接拖脸，或点典型脸试玩", -18, 23, 40);
            var mode = Label(panel.transform, font, "研究59D · 拒识未校准", -60, 18, 36);
            Label(panel.transform, font, "配方是设计意图，不是自动标签", -98, 16, 32);
            var strengthLabel = Label(panel.transform, font, "强度：中", -132, 18, 30);
            Button(panel.transform, font, "中性", 18, -170, controls.Neutral);
            Button(panel.transform, font, "高兴", 212, -170, controls.Happy);
            Button(panel.transform, font, "悲伤", 18, -222, controls.Sad);
            Button(panel.transform, font, "惊恐", 212, -222, controls.SurpriseFear);
            Button(panel.transform, font, "愤怒", 18, -274, controls.Angry);
            Button(panel.transform, font, "切换轻／中／强", 212, -274, controls.CycleStrength);
            Button(panel.transform, font, "重置脸", 18, -326, controls.ResetFace);
            Button(panel.transform, font, "切换旧／新模型", 212, -326, controls.ToggleModel);
            Button(panel.transform, font, "恢复进入时的脸", 18, -378, controls.RestoreFace);
            state = new SerializedObject(controls);
            state.FindProperty("recognizer").objectReferenceValue = recognizer;
            state.FindProperty("recipes").objectReferenceValue = metadata;
            state.FindProperty("modeText").objectReferenceValue = mode;
            state.FindProperty("strengthText").objectReferenceValue = strengthLabel;
            state.ApplyModifiedPropertiesWithoutUndo();
            var resultPanel = recognizer.GetComponentInChildren<LailaExpressionPanel>(true);
            var resultState = new SerializedObject(resultPanel);
            var result = resultState.FindProperty("resultText").objectReferenceValue as TMP_Text;
            result.fontSize = 21;
            result.rectTransform.sizeDelta = new Vector2(394, 66);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("研究场景保存失败");
        }

        private static TextMeshProUGUI Label(Transform parent, TMP_FontAsset font, string value, float y, float size, float height)
        {
            var obj = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI));
            obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size;
            text.text = value; text.color = new Color(.94f, .86f, .7f); text.raycastTarget = false;
            var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(18, y); rect.sizeDelta = new Vector2(374, height);
            return text;
        }

        public static void Upgrade(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("仅升级干净的独立Laila研究场景");
            LailaExpressionRecognizer recognizer = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var item in root.GetComponentsInChildren<LailaExpressionRecognizer>(true)) recognizer = item;
            if (recognizer == null) throw new InvalidOperationException("试玩识别引用缺失");
            var canvas = recognizer.GetComponentInChildren<Canvas>(true);
            bool changed = false;
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.text == "惊讶／恐惧" || text.text == "惊讶/恐惧") { text.text = "惊恐"; changed = true; }
                if (text.text == "直接拖脸，或点典型脸试玩") { text.text = "Laila候选试玩 · 五类开发"; changed = true; }
                if (text.text == "配方是设计意图，不是自动标签") { text.text = "五类外可能误接收；配方不是标签"; changed = true; }
                if (text.text == "五类外可能误接收；配方不是标签") { text.text = "五类反馈；配方不是语义真值"; changed = true; }
            }
            var controls = canvas.GetComponentInChildren<LailaExpressionPlaytest>(true);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset");
            if (controls.transform.Find("旧怒眉对照") == null)
            { Button(controls.transform, font, "旧怒眉对照", 274, -378, controls.OldAngry); changed = true; }
            if (controls.transform.Find("原下睑对照") == null)
            { Button(controls.transform, font, "原下睑对照", 146, -378, controls.PreviousAngry); changed = true; }
            foreach (var button in controls.GetComponentsInChildren<Button>(true))
            {
                string method = button.onClick.GetPersistentMethodName(0);
                if (method == "RestoreFace" || method == "PreviousAngry" || method == "OldAngry")
                {
                    var rect = button.GetComponent<RectTransform>();
                    var position = new Vector2(method == "RestoreFace" ? 18 : method == "PreviousAngry" ? 146 : 274, -378);
                    var size = new Vector2(118, 42);
                    if (rect.anchoredPosition != position || rect.sizeDelta != size)
                    { rect.anchoredPosition = position; rect.sizeDelta = size; changed = true; }
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    if (method == "RestoreFace" && label.text != "恢复开始脸") { label.text = "恢复开始脸"; changed = true; }
                }
                if (method == "ToggleModel")
                {
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    if (label.text != "切换四版模型") { label.text = "切换四版模型"; changed = true; }
                }
            }
            changed |= BindResearchCandidates(recognizer, controls);
            if (canvas.GetComponentInChildren<LailaPlaytestFeedback>(true) == null)
            {
                var panel = new GameObject("LiveFeedback", typeof(RectTransform), typeof(Image), typeof(LailaPlaytestFeedback));
                panel.transform.SetParent(canvas.transform, false);
                var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(28, -478); rect.sizeDelta = new Vector2(410, 554);
                panel.GetComponent<Image>().color = new Color(.09f, .07f, .065f, .93f); panel.GetComponent<Image>().raycastTarget = false;
                var feedback = panel.GetComponent<LailaPlaytestFeedback>();
                Label(panel.transform, font, "现场反馈 · 先选再保存", -16, 20, 34);
                var selection = Label(panel.transform, font, "请选择你看到的类别（不自动选模型标签）", -58, 15, 34);
                Button(panel.transform, font, "中性", 18, -100, feedback.Neutral);
                Button(panel.transform, font, "高兴", 212, -100, feedback.Happy);
                Button(panel.transform, font, "悲伤", 18, -152, feedback.Sad);
                Button(panel.transform, font, "惊恐", 212, -152, feedback.SurpriseFear);
                Button(panel.transform, font, "愤怒", 18, -204, feedback.Angry);
                Button(panel.transform, font, "不确定", 212, -204, feedback.Uncertain);
                Button(panel.transform, font, "难以表达", 18, -256, feedback.HardToExpress);
                Button(panel.transform, font, "隐藏／显示预测", 212, -256, feedback.TogglePrediction);
                var input = NoteInput(panel.transform, font);
                var save = Button(panel.transform, font, "保存这张反馈", 18, -374, feedback.Save);
                Button(panel.transform, font, "打开保存目录", 212, -374, feedback.OpenFolder);
                Button(panel.transform, font, "开始10张试点", 18, -426, feedback.BeginPilot);
                Button(panel.transform, font, "下一张", 212, -426, feedback.NextPose);
                var status = Label(panel.transform, font, "自由发现只是补充；10张试点可改天做\n没有异常不等于可靠，不用无限试", -480, 14, 65);
                var resultPanel = recognizer.GetComponentInChildren<LailaExpressionPanel>(true);
                var group = resultPanel.GetComponent<CanvasGroup>(); if (group == null) group = resultPanel.gameObject.AddComponent<CanvasGroup>();
                var fields = new SerializedObject(feedback);
                fields.FindProperty("recognizer").objectReferenceValue = recognizer;
                fields.FindProperty("note").objectReferenceValue = input;
                fields.FindProperty("selectionText").objectReferenceValue = selection;
                fields.FindProperty("statusText").objectReferenceValue = status;
                fields.FindProperty("saveButton").objectReferenceValue = save;
                fields.FindProperty("predictionPanel").objectReferenceValue = group;
                fields.ApplyModifiedPropertiesWithoutUndo(); changed = true;
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("现场反馈接线保存失败");
            }
        }

        private static bool BindResearchCandidates(LailaExpressionRecognizer recognizer, LailaExpressionPlaytest controls)
        {
            var oldModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(ResearchPath + ".onnx");
            var oldMetadata = AssetDatabase.LoadAssetAtPath<TextAsset>(ResearchPath + ".json");
            var candidate = AssetDatabase.LoadAssetAtPath<ModelAsset>(FeedbackPath + ".onnx");
            var candidateMetadata = AssetDatabase.LoadAssetAtPath<TextAsset>(FeedbackPath + ".json");
            var strengthModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(StrengthPath + ".onnx");
            var strengthMetadata = AssetDatabase.LoadAssetAtPath<TextAsset>(StrengthPath + ".json");
            if (oldModel == null || oldMetadata == null || candidate == null || candidateMetadata == null || strengthModel == null || strengthMetadata == null)
                throw new InvalidOperationException("研究旧版或反馈修复资产未就绪");
            foreach (var pair in new[] { new[] { ResearchPath, BaselineHash }, new[] { FeedbackPath, FeedbackHash }, new[] { StrengthPath, StrengthHash } })
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(pair[0] + ".onnx"))).Replace("-", "").ToLowerInvariant();
                    if (actual != pair[1]) throw new InvalidOperationException("研究源模型hash与批准版本不符");
                }
            }
            var settings = new SerializedObject(controls);
            settings.FindProperty("baselineResearchModel").objectReferenceValue = oldModel;
            settings.FindProperty("baselineResearchMetadata").objectReferenceValue = oldMetadata;
            settings.FindProperty("baselineResearchHash").stringValue = BaselineHash;
            settings.FindProperty("feedbackResearchModel").objectReferenceValue = candidate;
            settings.FindProperty("feedbackResearchMetadata").objectReferenceValue = candidateMetadata;
            settings.FindProperty("feedbackResearchHash").stringValue = FeedbackHash;
            settings.FindProperty("strengthResearchModel").objectReferenceValue = strengthModel;
            settings.FindProperty("strengthResearchMetadata").objectReferenceValue = strengthMetadata;
            settings.FindProperty("strengthResearchHash").stringValue = StrengthHash;
            bool changed = settings.ApplyModifiedPropertiesWithoutUndo();
            settings = new SerializedObject(recognizer);
            LailaExpressionRecognizer.ValidateMetadata(Newtonsoft.Json.Linq.JObject.Parse(strengthMetadata.text),
                StrengthHash, settings.FindProperty("approvedRigHash").stringValue);
            settings.FindProperty("researchModelAsset").objectReferenceValue = strengthModel;
            settings.FindProperty("researchMetadata").objectReferenceValue = strengthMetadata;
            settings.FindProperty("approvedResearchModelHash").stringValue = StrengthHash;
            settings.FindProperty("useResearchCandidate").boolValue = true;
            var feedbackSettings = AssetDatabase.LoadAssetAtPath<LailaExpressionFeedbackConfig>(FeedbackConfigPath);
            if (feedbackSettings == null)
            {
                feedbackSettings = ScriptableObject.CreateInstance<LailaExpressionFeedbackConfig>();
                feedbackSettings.Validate();
                AssetDatabase.CreateAsset(feedbackSettings, FeedbackConfigPath);
            }
            feedbackSettings.Validate();
            settings.FindProperty("feedbackConfig").objectReferenceValue = feedbackSettings;
            settings.FindProperty("useStableFeedback").boolValue = true;
            return settings.ApplyModifiedPropertiesWithoutUndo() || changed;
        }

        private static TMP_InputField NoteInput(Transform parent, TMP_FontAsset font)
        {
            var obj = new GameObject("OptionalNote", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(18, -310); rect.sizeDelta = new Vector2(374, 54);
            var image = obj.GetComponent<Image>(); image.color = new Color(.25f, .21f, .17f);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)); viewport.transform.SetParent(obj.transform, false);
            var viewRect = viewport.GetComponent<RectTransform>(); viewRect.anchorMin = Vector2.zero; viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = new Vector2(8, 4); viewRect.offsetMax = new Vector2(-8, -4);
            var text = Label(viewport.transform, font, "", 0, 15, 46);
            var placeholder = Label(viewport.transform, font, "可选备注", 0, 15, 46);
            foreach (var label in new[] { text, placeholder })
            { label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero; }
            var field = obj.GetComponent<TMP_InputField>(); field.targetGraphic = image; field.textViewport = viewRect;
            field.textComponent = text; field.placeholder = placeholder; field.lineType = TMP_InputField.LineType.MultiLineNewline; field.characterLimit = 4000;
            return field;
        }

        private static Button Button(Transform parent, TMP_FontAsset font, string value, float x, float y, UnityEngine.Events.UnityAction action)
        {
            var obj = new GameObject(value, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(180, 42);
            obj.GetComponent<Image>().color = new Color(.28f, .2f, .13f);
            var text = Label(obj.transform, font, value, 0, 17, 42);
            text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            UnityEventTools.AddPersistentListener(obj.GetComponent<Button>().onClick, action);
            return obj.GetComponent<Button>();
        }
    }
}
