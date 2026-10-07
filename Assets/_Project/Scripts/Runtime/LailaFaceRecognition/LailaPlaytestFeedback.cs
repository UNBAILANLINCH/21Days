using System;
using System.IO;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.LailaFaceRecognition
{
    // 职责：独立试玩中主动选类／备注并保存点击瞬间的脸、参数和实际预测。
    // 新建原因：原未标注采集菜单没有现场反馈UI，原139图盲标器不能接管当前脸。
    // 载体：保存按钮同步执行；5秒锚点：成功状态及对应face.png／record.json；退场：停用不留异步任务。
    public sealed class LailaPlaytestFeedback : MonoBehaviour
    {
        [SerializeField] private LailaExpressionRecognizer recognizer;
        [SerializeField] private TMP_InputField note;
        [SerializeField] private TMP_Text selectionText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button saveButton;
        [SerializeField] private CanvasGroup predictionPanel;
        private string selected;
        private string batchDirectory;
        private string lastDigest;
        private bool saving;
        private bool guided;
        private bool revealed;
        private bool predictionSeenAtSelection;
        private bool predictionExposed = true;
        private int savedCount;
        private const int PilotLimit = 10;
        public string LastSavedPath { get; private set; }
        public string LastStatus { get; private set; } = "尚未保存反馈";

        public void Neutral() => Select("neutral", "中性");
        public void Happy() => Select("happy", "高兴");
        public void Sad() => Select("sad", "悲伤");
        public void SurpriseFear() => Select("surprise_fear", "惊恐");
        public void Angry() => Select("angry", "愤怒");
        public void Uncertain() => Select("uncertain", "不确定");
        public void HardToExpress() => Select("hard_to_express", "难以表达");

        public void BeginPilot()
        {
            if (guided) { SetStatus("已开始本批试点；保存后点下一张，10张可停止"); return; }
            guided = true; savedCount = 0; lastDigest = null; batchDirectory = null; LastSavedPath = null;
            BeginNextPose();
        }

        public void NextPose()
        {
            if (!guided) { BeginPilot(); return; }
            if (savedCount >= PilotLimit) { SetStatus("本批10张已完成，可以停止；这是开发试点，不是准确率验收"); return; }
            if (string.IsNullOrEmpty(LastSavedPath)) { SetStatus("先保存当前这张；捏不出可选难以表达"); return; }
            BeginNextPose();
        }

        private void BeginNextPose()
        {
            selected = null; LastSavedPath = null;
            predictionExposed = false;
            if (recognizer != null && recognizer.Face != null) recognizer.Face.ResetFace();
            if (note != null) note.text = "";
            SetPredictionVisible(false);
            string[] targets = { "中性", "高兴", "悲伤", "惊恐", "愤怒" };
            if (selectionText != null) selectionText.text = "试做：" + targets[savedCount / 2] + " · " + (savedCount + 1) + "/10（尚未选标签）";
            SetStatus("从中性脸试做，再选你看到的类别\n捏不出可选难以表达；保存后再看模型");
        }

        public void TogglePrediction() => SetPredictionVisible(!revealed);

        private void SetPredictionVisible(bool visible)
        {
            revealed = visible;
            if (visible) predictionExposed = true;
            if (predictionPanel != null)
            { predictionPanel.alpha = visible ? 1 : 0; predictionPanel.interactable = visible; predictionPanel.blocksRaycasts = visible; }
        }

        private void Start() => RestorePredictionVisibility();
        private void OnEnable() => RestorePredictionVisibility();
        private void OnDisable() => RestorePredictionVisibility();

        private void RestorePredictionVisibility()
        {
            // 未保存试点在组件停用、重启时仍隐藏预测；此前主动揭晓的暴露记录不清零。
            SetPredictionVisible(!guided || !string.IsNullOrEmpty(LastSavedPath));
        }

        private void Select(string key, string display)
        {
            selected = key;
            predictionSeenAtSelection = predictionExposed;
            if (selectionText != null) selectionText.text = "你的选择：" + display;
        }

        public void Save()
        {
            if (saving) return;
            if (guided && savedCount >= PilotLimit) { SetStatus("本批10张已完成，可以停止"); return; }
            if (guided && !string.IsNullOrEmpty(LastSavedPath)) { SetStatus("本张已保存；点下一张从中性重新开始"); return; }
            if (!LailaPlaytestFeedbackStore.IsAllowedLabel(selected)) { SetStatus("请先选择你看到的类别或不确定"); return; }
            saving = true;
            if (saveButton != null) saveButton.interactable = false;
            try
            {
#if UNITY_EDITOR
                if (!Application.isPlaying || recognizer == null || !recognizer.IsReady || Camera.main == null)
                    throw new InvalidOperationException("当前脸或识别模型未就绪");
                var raw = new float[17]; string error;
                if (!LailaExpressionSampler.TrySample(recognizer.Face, raw, out error)) throw new InvalidOperationException(error);
                var renderer = new SerializedObject(recognizer.Face).FindProperty("faceRenderer").objectReferenceValue as SkinnedMeshRenderer;
                if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.blendShapeCount != 31)
                    throw new InvalidOperationException("当前31形态网格未就绪");
                var weights = new JObject();
                for (int i = 0; i < 31; i++) weights[renderer.sharedMesh.GetBlendShapeName(i)] = renderer.GetBlendShapeWeight(i);
                var metadata = JObject.Parse(recognizer.ActiveMetadata.text);
                if (!recognizer.Recognize()) throw new InvalidOperationException("当前脸识别失败，未保存反馈");
                float[] probabilities; float energy;
                // 同步推理同一份已冻结raw17，禁止把限频等待中的旧UI结果写入当前脸。
                recognizer.Infer(raw, out probabilities, out energy);
                int top = 0; for (int i = 1; i < 5; i++) if (probabilities[i] > probabilities[top]) top = i;
                int decision = LailaExpressionRecognizer.Decide(probabilities, energy, (float)metadata["energy_threshold"], (float)metadata["min_confidence"]);
                var snapshot = new JObject {
                    ["raw17"] = JArray.FromObject(raw), ["raw17_keys"] = JArray.FromObject(LailaExpressionSampler.Keys), ["weights31"] = weights,
                    ["user_label"] = selected, ["note"] = note != null ? note.text : "", ["prediction_visible_before_selection"] = predictionSeenAtSelection,
                    ["prediction_exposed_before_save"] = predictionExposed,
                    ["collection_mode"] = guided ? "bounded-development-pilot" : "free-discovery-supplement",
                    ["attempt_slot"] = guided ? savedCount + 1 : 0, ["pilot_limit"] = guided ? PilotLimit : 0,
                    ["target_hint"] = guided ? LailaPlaytestFeedbackStore.LabelKeys[savedCount / 2] : null,
                    ["model"] = new JObject { ["onnx_sha256"] = recognizer.ActiveModelHash, ["rig_hash"] = metadata["rig"]["config_hash"].DeepClone(),
                        ["research59"] = recognizer.UsesResearchCandidate, ["research"] = metadata["research"] != null ? metadata["research"].DeepClone() : null },
                    ["prediction"] = new JObject { ["class_key"] = metadata["labels"][top]["key"].DeepClone(), ["probabilities"] = JArray.FromObject(probabilities),
                        ["energy"] = energy, ["accepted_class_key"] = decision < 0 ? null : (string)metadata["labels"][decision]["key"],
                        ["energy_threshold"] = metadata["energy_threshold"].DeepClone(), ["min_confidence"] = metadata["min_confidence"].DeepClone(),
                        ["display_class"] = recognizer.UsesStableFeedback ? recognizer.DisplayClassification : recognizer.Classification,
                        ["display_policy"] = recognizer.UsesStableFeedback ? "five-class-neutral-zone-stable-feedback" : "historical-rejection-diagnostic",
                        ["neutral_override"] = recognizer.NeutralOverride, ["display_reason"] = recognizer.UsesStableFeedback ? recognizer.FeedbackReason : "历史拒识对照",
                        ["human_rejection_calibrated"] = false },
                    ["scene"] = gameObject.scene.path, ["capture"] = "synchronous-click-snapshot-baked-current-face" };
                string digest = LailaPlaytestFeedbackStore.Digest(snapshot);
                if (lastDigest == digest && !string.IsNullOrEmpty(LastSavedPath) && File.Exists(LastSavedPath))
                { SetStatus("这张反馈已保存；改脸、类别或备注后可再保存"); return; }
                byte[] png = CaptureFrozenFace(renderer, Camera.main);
                if (string.IsNullOrEmpty(batchDirectory))
                    batchDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ML", "expression-recognition", "artifacts", "laila_v2_candidate",
                        "playtest-feedback", "batch-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
                LastSavedPath = LailaPlaytestFeedbackStore.Save(batchDirectory, snapshot, png);
                lastDigest = digest;
                savedCount++;
                SetPredictionVisible(true);
                SetStatus(guided ? "已保存 " + savedCount + "/10；" + (savedCount >= PilotLimit ? "本批完成，可停止" : "先看模型，再点下一张")
                    : "补充反馈已保存；10张试点可点开始\n可点打开保存目录查看");
#else
                throw new InvalidOperationException("现场反馈仅用于本地Unity Editor试玩");
#endif
            }
            catch (Exception e) { SetStatus("保存失败：" + e.Message); }
            finally { saving = false; if (saveButton != null) saveButton.interactable = true; }
        }

        public void OpenFolder()
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(batchDirectory) && Directory.Exists(batchDirectory)) EditorUtility.RevealInFinder(batchDirectory);
            else SetStatus("先保存一张反馈，才会建立本地目录");
#endif
        }

        private void SetStatus(string value)
        {
            LastStatus = value;
            if (statusText != null) statusText.text = value;
        }

#if UNITY_EDITOR
        private static byte[] CaptureFrozenFace(SkinnedMeshRenderer source, Camera camera)
        {
            var mesh = new Mesh();
            var clone = new GameObject("FeedbackFrozenFace"); clone.hideFlags = HideFlags.HideAndDontSave;
            clone.layer = source.gameObject.layer;
            var rt = new RenderTexture(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active; bool enabled = source.enabled;
            try
            {
                // BakeMesh在点击回调内冻结当前权重，不等待下一帧，也不改玩家脸的任何权重。
                source.BakeMesh(mesh);
                clone.transform.SetParent(source.transform, false);
                clone.AddComponent<MeshFilter>().sharedMesh = mesh;
                var rendered = clone.AddComponent<MeshRenderer>(); rendered.sharedMaterials = source.sharedMaterials;
                var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block); rendered.SetPropertyBlock(block);
                rendered.shadowCastingMode = source.shadowCastingMode; rendered.receiveShadows = source.receiveShadows;
                source.enabled = false; camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply(); return image.EncodeToPNG();
            }
            finally
            {
                source.enabled = enabled; camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                rt.Release(); DestroyImmediate(clone); DestroyImmediate(mesh); DestroyImmediate(rt); DestroyImmediate(image);
            }
        }
#endif
    }
}
