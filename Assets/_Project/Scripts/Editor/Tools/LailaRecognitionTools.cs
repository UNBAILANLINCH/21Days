using System;
using System.IO;
using System.Security.Cryptography;
using Game.LailaFace;
using Game.LailaFaceRecognition;
using Newtonsoft.Json.Linq;
using TMPro;
using Unity.Sentis;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor.Tools
{
    // 职责：在指定laila场景接线局部识别UI，并采集未标注真实截图/参数。
    // 新建原因：现有模型控制菜单没有推理部署校验或独立标注采集入口。
    public static class LailaRecognitionTools
    {
        public static void Install(string modelPath, string metadataPath)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != "Assets/_Project/Scenes/laila.unity")
                throw new InvalidOperationException("仅在laila编辑态安装识别UI");
            var metaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(metadataPath);
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
            if (metaAsset == null || model == null) throw new InvalidOperationException("模型尚未导入");
            var meta = JObject.Parse(metaAsset.text);
            string digest;
            using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(modelPath))).Replace("-", "").ToLowerInvariant();
            string rig = (string)meta["rig"]["config_hash"];
            LailaExpressionRecognizer.ValidateMetadata(meta, digest, rig);
            foreach (var rootObject in scene.GetRootGameObjects())
                if (rootObject.name == "LailaExpressionRecognition") throw new InvalidOperationException("识别UI已存在，拒绝重复安装");
            FaceBlendShapeController face = null;
            foreach (var rootObject in scene.GetRootGameObjects())
                foreach (var candidate in rootObject.GetComponentsInChildren<FaceBlendShapeController>(false))
                    if (candidate.isActiveAndEnabled && candidate.BlendShapeCount == 31)
                    { if (face != null) throw new InvalidOperationException("当前脸部引用不唯一"); face = candidate; }
            if (face == null) throw new InvalidOperationException("未找到当前31形态脸部");
            var root = new GameObject("LailaExpressionRecognition");
            Undo.RegisterCreatedObjectUndo(root, "Install Laila recognition");
            var recognizer = root.AddComponent<LailaExpressionRecognizer>();
            var so = new SerializedObject(recognizer);
            so.FindProperty("face").objectReferenceValue = face;
            so.FindProperty("modelAsset").objectReferenceValue = model;
            so.FindProperty("metadata").objectReferenceValue = metaAsset;
            so.FindProperty("approvedModelHash").stringValue = digest;
            so.FindProperty("approvedRigHash").stringValue = rig;
            so.ApplyModifiedProperties();
            var canvasObject = new GameObject("ResultCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<Canvas>().sortingOrder = 50;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1;
            var panel = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-28, -28); rect.sizeDelta = new Vector2(430, 310);
            panel.GetComponent<Image>().color = new Color(0.09f, 0.07f, 0.065f, 0.93f);
            panel.GetComponent<Image>().raycastTarget = false;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset");
            var title = Text(panel.transform, font, "表情识别 · 实验", 24, -18, 38);
            var status = Text(panel.transform, font, "模型未就绪", 17, -58, 30);
            var result = Text(panel.transform, font, "尚未识别", 27, -92, 58);
            var details = Text(panel.transform, font, "合成候选；未通过人工验收", 15, -212, 90);
            details.gameObject.SetActive(false);
            var recognize = MakeButton(panel.transform, font, "识别当前表情", new Vector2(18, -160), new Vector2(245, 42));
            var expand = MakeButton(panel.transform, font, "详情", new Vector2(277, -160), new Vector2(130, 42));
            var view = panel.AddComponent<LailaExpressionPanel>();
            so = new SerializedObject(view);
            so.FindProperty("recognizer").objectReferenceValue = recognizer;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("resultText").objectReferenceValue = result;
            so.FindProperty("detailsText").objectReferenceValue = details;
            so.FindProperty("recognizeButton").objectReferenceValue = recognize;
            so.FindProperty("detailsButton").objectReferenceValue = expand;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font, string value, float size, float y, float height)
        {
            var obj = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(18, y); rect.sizeDelta = new Vector2(394, height);
            var text = obj.GetComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size; text.text = value;
            text.color = new Color(0.94f, 0.86f, 0.7f); text.raycastTarget = false;
            return text;
        }

        private static Button MakeButton(Transform parent, TMP_FontAsset font, string label, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            obj.GetComponent<Image>().color = new Color(0.28f, 0.2f, 0.13f);
            var text = Text(obj.transform, font, label, 18, 0, size.y);
            var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            return obj.GetComponent<Button>();
        }

        [MenuItem("21Days/Laila/采集识别样本（未标注）")]
        private static void CaptureSelected()
        {
            LailaRecognitionCaptureWindow.Open();
        }

        public static string Capture(LailaExpressionRecognizer recognizer, string split, string group)
        {
            if (!EditorApplication.isPlaying || recognizer == null || Camera.main == null)
                throw new InvalidOperationException("在运行态、当前相机下采集");
            if (split != "unassigned" && split != "train" && split != "dev" && split != "test")
                throw new InvalidOperationException("未知数据划分");
            if (string.IsNullOrWhiteSpace(group)) throw new InvalidOperationException("近邻组不能为空");
            var values = new float[17]; string error;
            if (!LailaExpressionSampler.TrySample(recognizer.Face, values, out error)) throw new InvalidOperationException(error);
            var dir = Path.Combine(Application.dataPath, "..", "ML", "expression-recognition", "data", "laila-captures-v2", split);
            var captureRoot = Path.GetDirectoryName(dir);
            if (Directory.Exists(captureRoot))
                foreach (var existing in Directory.GetFiles(captureRoot, "*.json", SearchOption.AllDirectories))
                {
                    var sample = JObject.Parse(File.ReadAllText(existing));
                    if ((string)sample["group"] == group && (string)sample["split"] != split)
                        throw new InvalidOperationException("该近邻组已在其他划分中；应整组迁移，不能跨集采集");
                }
            Directory.CreateDirectory(dir);
            string id = Guid.NewGuid().ToString("N");
            var metaField = new SerializedObject(recognizer).FindProperty("metadata");
            var meta = JObject.Parse(((TextAsset)metaField.objectReferenceValue).text);
            var sliders = new JObject(); var keys = LailaExpressionSampler.Keys;
            for (int i = 0; i < keys.Length; i++) sliders[keys[i]] = values[i];
            var record = new JObject { ["schema_version"] = 1, ["id"] = id, ["rig"] = "laila_v2_candidate",
                ["rig_hash"] = meta["rig"]["config_hash"].DeepClone(), ["group"] = group, ["split"] = split,
                ["status"] = "unlabeled", ["source"] = "unity-editor-play-capture", ["sliders"] = sliders };
            var renderer = CaptureRenderer(recognizer.Face);
            if (renderer == null || renderer.sharedMesh == null) throw new InvalidOperationException("当前脸部网格缺失");
            string assetPath = AssetDatabase.GetAssetPath(renderer.sharedMesh);
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) throw new InvalidOperationException("无法核对脸部源资产");
            using (var sha = SHA256.Create()) record["fbx_sha256"] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assetPath))).Replace("-", "").ToLowerInvariant();
            var weights = new JObject();
            for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++) weights[renderer.sharedMesh.GetBlendShapeName(i)] = renderer.GetBlendShapeWeight(i);
            record["weights"] = weights;
            var camera = Camera.main; var old = camera.targetTexture; var active = RenderTexture.active;
            var rt = new RenderTexture(1920, 1080, 24); var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(dir, id + ".png"), image.EncodeToPNG());
                File.WriteAllText(Path.Combine(dir, id + ".json"), record.ToString());
            }
            finally { camera.targetTexture = old; RenderTexture.active = active; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image); }
            return id;
        }

        private static SkinnedMeshRenderer CaptureRenderer(FaceBlendShapeController face)
            => new SerializedObject(face).FindProperty("faceRenderer").objectReferenceValue as SkinnedMeshRenderer;
    }
}
