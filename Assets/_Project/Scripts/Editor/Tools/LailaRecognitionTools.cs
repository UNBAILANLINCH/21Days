using System;
using System.IO;
using System.Security.Cryptography;
using Game.Gameplay;
using Game.LailaFaceRecognition;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor.Tools
{
    // 职责：采集当前试玩的未标注真实截图与参数；不安装识别UI。
    // 新建原因：运行时反馈保存不承担分组、划分与匿名样本采集。
    public static class LailaRecognitionTools
    {
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
