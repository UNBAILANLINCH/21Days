using System;
using Game.Gameplay;
using Newtonsoft.Json.Linq;
using Unity.Sentis;
using UnityEngine;

namespace Game.LailaFaceRecognition
{
    // 职责：加载配对的候选ONNX，严格采样、CPU推理与拒识；不改变脸部。
    // 新建原因：项目现有捏脸组件没有模型加载或识别实现。
    [DisallowMultipleComponent]
    public sealed class LailaExpressionRecognizer : MonoBehaviour
    {
        [SerializeField] private FaceBlendShapeController face;
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private TextAsset metadata;
        [SerializeField] private string approvedModelHash;
        [SerializeField] private string approvedRigHash;
        [SerializeField] private ModelAsset researchModelAsset;
        [SerializeField] private TextAsset researchMetadata;
        [SerializeField] private string approvedResearchModelHash;
        [SerializeField] private bool useResearchCandidate;
        [SerializeField] private bool useStableFeedback;
        [SerializeField] private LailaExpressionFeedbackConfig feedbackConfig;
        private LailaExpressionFeedback feedback;
        private string diagnosticDetails;
        private Worker worker;
        private Tensor<float> input;
        private readonly float[] sliders = new float[17];
        private readonly float[] scratch = new float[17];
        private readonly float[] observed = new float[17];
        private readonly float[] scores = new float[5];
        private Func<string, bool> hasShape;
        private Func<string, float> getWeight;
        private bool hasObserved;
        private bool pending;
        private double nextInferenceTime;
        // UI刷新节流策略，不是模型阈值或玩法参数；最多每100ms推理一次。
        public const double RecognitionInterval = 0.1;
        public int InferenceCount { get; private set; }
        public double LastInferenceTime { get; private set; }
        public double LastInferenceMilliseconds { get; private set; }
        private string[] labels;
        private float energyThreshold;
        private float minConfidence;
        private bool started;

        public string Status { get; private set; } = "模型未就绪";
        public string Result { get; private set; } = "尚未识别";
        public string Details { get; private set; } = "候选模型；未通过人工盲标验收";
        public bool IsReady => worker != null;
        public event Action OnChanged;
        public FaceBlendShapeController Face => face;
        public bool UsesResearchCandidate => useResearchCandidate;
        public bool UsesStableFeedback => useStableFeedback;
        public string DisplayClassification => feedback != null && feedback.DisplayIndex >= 0 ? labels[feedback.DisplayIndex] : "无有效结果";
        public bool NeutralOverride => feedback != null && feedback.NeutralOverride;
        public string FeedbackReason => NeutralOverride ? "近原始零基线中性区" : "完整五类预测＋展示迟滞";
        public TextAsset ActiveMetadata => useResearchCandidate ? researchMetadata : metadata;
        public string ActiveModelHash => useResearchCandidate ? approvedResearchModelHash : approvedModelHash;
        public static string DisplayLabel(string key, string original) => key == "surprise_fear" ? "惊恐" : original;
        public string Classification { get; private set; } = "等待识别";
        public string Rejection { get; private set; } = "等待识别";

        public void SetResearchMode(bool research)
        {
            useResearchCandidate = research;
            if (!research) useStableFeedback = false;
            Initialize();
        }

        public void SetResearchCandidate(ModelAsset candidate, TextAsset candidateMetadata, string approvedHash, bool stableFeedback = false)
        {
            if (candidate == null || candidateMetadata == null || string.IsNullOrEmpty(approvedHash))
                throw new ArgumentException("研究候选引用不完整");
            // 仍由Initialize执行严格配对／契约校验；失败清空结果，不借用上一版输出。
            researchModelAsset = candidate;
            researchMetadata = candidateMetadata;
            approvedResearchModelHash = approvedHash;
            useStableFeedback = stableFeedback;
            SetResearchMode(true);
        }

        private void Start() { started = true; Initialize(); }
        private void OnEnable() { if (started) Initialize(); }
        private void OnDisable() { Release(); Result = "尚未识别"; Status = "模型已停用"; Details = "此次没有有效识别结果"; OnChanged?.Invoke(); }
        private void OnDestroy() => Release();

        private void LateUpdate()
        {
            if (!IsReady) return;
            string error;
            if (!TrySample(out error))
            {
                hasObserved = false; pending = false;
                if (Status != "输入无效" || Result != error) InvalidateInput(error);
                return;
            }
            bool changed = !hasObserved;
            for (int i = 0; i < 17 && !changed; i++) changed = sliders[i] != observed[i];
            if (changed)
            {
                Array.Copy(sliders, observed, 17); hasObserved = true;
                if (!pending)
                {
                    pending = true;
                    if (!useStableFeedback || feedback == null || feedback.DisplayIndex < 0)
                    { Status = "表情已变化 · 等待识别"; Result = "识别中…"; }
                    Classification = Rejection = "等待当前输入";
                    Details = "正在等待当前输入；原始预测已失效，展示标签待更新"; OnChanged?.Invoke();
                }
            }
            if (pending && Time.unscaledTimeAsDouble >= nextInferenceTime) RecognizeSample(); // lint-ok: 只节流实验UI，不参与确定性玩法
            if (!pending && feedback != null && feedback.Advance(Time.unscaledTimeAsDouble)) PublishFeedback(); // lint-ok: 只推进展示迟滞，不参与关卡判定
        }

        private void InvalidateInput(string error)
        {
            if (feedback != null) feedback.Reset();
            hasObserved = false; pending = false;
            Classification = Rejection = "无有效结果"; Status = "输入无效"; Result = error;
            diagnosticDetails = null; Details = "此次没有有效识别结果"; OnChanged?.Invoke();
        }

        private bool TrySample(out string error)
        {
            error = null;
            if (face == null || !face.isActiveAndEnabled) { error = "脸部引用或输入缓冲无效"; return false; }
            return LailaExpressionSampler.TryRead(hasShape, getWeight, sliders, scratch, out error);
        }

        public void Initialize()
        {
            Release();
            Details = "候选模型；未通过人工盲标验收";
            try
            {
                var selectedModel = useResearchCandidate ? researchModelAsset : modelAsset;
                var selectedMetadata = useResearchCandidate ? researchMetadata : metadata;
                if (selectedModel == null || selectedMetadata == null || face == null) throw new InvalidOperationException("模型、元数据或脸部未绑定");
                var meta = JObject.Parse(selectedMetadata.text);
                ValidateMetadata(meta, useResearchCandidate ? approvedResearchModelHash : approvedModelHash, approvedRigHash);
                if (useResearchCandidate && ((int?)meta["research"]?["feature_dimension"] != 59
                    || (bool?)meta["research"]?["rejection_calibrated"] != false
                    || (string)meta["research"]?["purpose"] != "single-user-development-playtest-not-deployment"))
                    throw new InvalidOperationException("须使用明确标记的未校准59D研究候选");
                labels = new string[5];
                for (int i = 0; i < 5; i++) labels[i] = DisplayLabel((string)meta["labels"][i]["key"], (string)meta["labels"][i]["zh"]);
                energyThreshold = (float)meta["energy_threshold"];
                minConfidence = (float)meta["min_confidence"];
                if (useStableFeedback)
                {
                    if (!useResearchCandidate || feedbackConfig == null) throw new InvalidOperationException("五类反馈配置未绑定研究候选");
                    feedback = new LailaExpressionFeedback(feedbackConfig);
                }
                face.RebuildCache();
                hasShape = face.HasShape; getWeight = face.GetWeight;
                worker = new Worker(ModelLoader.Load(selectedModel), BackendType.CPU);
                input = new Tensor<float>(new TensorShape(1, 17), sliders);
                Status = useStableFeedback ? "五类反馈已就绪" : useResearchCandidate ? "研究59D已就绪 · 拒识未校准" : "实验模型已就绪";
                Result = "等待实时识别";
                Details = useResearchCandidate ? "单人开发意见候选；旧拒识门槛仅研究参照，未完成校准" : "只用合成数据训练；分数不代表人工准确率";
            }
            catch (Exception e) { Release(); Status = "识别不可用"; Result = e.Message; }
            OnChanged?.Invoke();
        }

        public static void ValidateMetadata(JObject meta, string modelHash, string rigHash)
        {
            if ((int?)meta["schema_version"] != 1 || (string)meta["rig"]?["name"] != "laila_v2_candidate")
                throw new InvalidOperationException("模型契约版本不匹配");
            if (string.IsNullOrEmpty(modelHash) || string.IsNullOrEmpty(rigHash)
                || (string)meta["onnx"]?["sha256"] != modelHash || (string)meta["rig"]?["config_hash"] != rigHash
                || (string)meta["train_rig_hash"] != rigHash || (bool?)meta["checks"]?["numeric"]?["ok"] != true
                || (bool?)meta["checks"]?["operators"]?["ok"] != true || (bool?)meta["checks"]?["size"]?["ok"] != true)
                throw new InvalidOperationException("模型配对、绑定hash或导出自检不匹配");
            var keys = LailaExpressionSampler.Keys;
            var rows = meta["sliders"] as JArray;
            if (rows == null || rows.Count != 17) throw new InvalidOperationException("模型必须使用17轴");
            for (int i = 0; i < 17; i++)
                if ((string)rows[i]["key"] != keys[i] || (float?)rows[i]["min"] != (i < 14 ? -1 : 0)
                    || (float?)rows[i]["max"] != 1 || (float?)rows[i]["default"] != 0)
                    throw new InvalidOperationException("输入顺序或范围不匹配：" + keys[i]);
            string[] classKeys = { "neutral", "happy", "sad", "surprise_fear", "angry" };
            var classes = meta["labels"] as JArray;
            if (classes == null || classes.Count != 5) throw new InvalidOperationException("类别必须是约定五类");
            for (int i = 0; i < 5; i++) if ((string)classes[i]["key"] != classKeys[i]) throw new InvalidOperationException("类别顺序不匹配");
            float threshold = (float)meta["energy_threshold"], confidence = (float)meta["min_confidence"];
            if (float.IsNaN(threshold) || float.IsInfinity(threshold) || float.IsNaN(confidence)
                || confidence < 0 || confidence > 1) throw new InvalidOperationException("拒识阈值无效");
        }

        public bool Recognize()
        {
            if (!useStableFeedback) Result = "尚未识别";
            Details = "此次没有有效识别结果";
            if (!IsReady) { InvalidateInput("模型未就绪"); Status = "识别不可用"; OnChanged?.Invoke(); return false; }
            string error;
            if (!TrySample(out error))
            { InvalidateInput(error); return false; }
            Array.Copy(sliders, observed, 17); hasObserved = true;
            return RecognizeSample();
        }

        private bool RecognizeSample()
        {
            pending = false;
            LastInferenceTime = Time.unscaledTimeAsDouble; // lint-ok: 实验UI节流时间，不参与确定性玩法
            nextInferenceTime = LastInferenceTime + RecognitionInterval;
            try
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                float energy;
                InferInto(sliders, scores, out energy);
                LastInferenceMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; InferenceCount++;
                var probabilities = scores;
                int best = Decide(probabilities, energy, energyThreshold, minConfidence);
                Status = best < 0 ? "当前识别 · 不确定" : "当前识别 · 实验模型";
                int top = 0;
                for (int i = 1; i < probabilities.Length; i++) if (probabilities[i] > probabilities[top]) top = i;
                Classification = labels[top];
                Rejection = best < 0 ? "不确定／未通过旧门槛" : "通过旧门槛";
                Result = best < 0 ? "不确定 / 认不出" : labels[best];
                if (useResearchCandidate)
                {
                    Status = "研究59D · 拒识未校准";
                    Result = "分类：" + Classification + "\n研究拒识：" + Rejection;
                }
                Details = "模型分数 " + probabilities[top].ToString("P1") + "（非人工准确率）\n";
                for (int i = 0; i < 5; i++) Details += labels[i] + " " + probabilities[i].ToString("P1") + "  ";
                Details += "\nenergy " + energy.ToString("F3") + " / 阈值 " + energyThreshold.ToString("F3");
                diagnosticDetails = Details;
                if (feedback != null)
                {
                    feedback.Observe(sliders, probabilities, LastInferenceTime);
                    PublishFeedback(); return true;
                }
                OnChanged?.Invoke(); return true;
            }
            catch (Exception e) { Release(); Status = "识别不可用"; Result = e.Message; OnChanged?.Invoke(); return false; }
        }

        private void PublishFeedback()
        {
            Status = "当前五类反馈"; Result = DisplayClassification;
            Details = "原始预测：" + Classification + "\n展示结果：" + DisplayClassification + "\n展示原因：" + FeedbackReason
                + "\n旧拒识诊断：" + Rejection + "（不阻挡五类反馈）\n" + diagnosticDetails;
            OnChanged?.Invoke();
        }

        public void Infer(float[] values, out float[] probabilities, out float energy)
        {
            probabilities = new float[5];
            InferInto(values, probabilities, out energy);
        }

        private void InferInto(float[] values, float[] probabilities, out float energy)
        {
            if (!IsReady || values == null || values.Length != 17) throw new InvalidOperationException("推理输入无效");
            for (int i = 0; i < 17; i++)
                if (float.IsNaN(values[i]) || float.IsInfinity(values[i]) || values[i] < (i < 14 ? -1 : 0) || values[i] > 1)
                    throw new InvalidOperationException("推理输入非有限或越界");
            input.Upload(values);
            worker.Schedule(input);
            var output = worker.PeekOutput("probs") as Tensor<float>;
            var energyOutput = worker.PeekOutput("energy") as Tensor<float>;
            if (output == null || energyOutput == null) throw new InvalidOperationException("模型输出缺失");
            if (output.shape.length != 5 || energyOutput.shape.length != 1) throw new InvalidOperationException("输出维度无效");
            // CPU Worker持有输出；等待作业后直接读，不克隆或释放Worker的张量。
            output.CompleteAllPendingOperations(); energyOutput.CompleteAllPendingOperations();
            for (int i = 0; i < 5; i++) probabilities[i] = output[i];
            energy = energyOutput[0];
            HighestClass(probabilities, energy);
        }

        public static int Decide(float[] probabilities, float energy, float threshold, float confidence)
        {
            int best = HighestClass(probabilities, energy);
            return energy > threshold || probabilities[best] < confidence ? -1 : best;
        }

        public static int HighestClass(float[] probabilities, float energy)
        {
            if (probabilities == null || probabilities.Length != 5 || float.IsNaN(energy) || float.IsInfinity(energy))
                throw new InvalidOperationException("输出维度或energy无效");
            int best = 0; float total = 0;
            for (int i = 0; i < 5; i++)
            {
                float p = probabilities[i];
                if (float.IsNaN(p) || float.IsInfinity(p) || p < 0 || p > 1) throw new InvalidOperationException("概率输出无效");
                total += p; if (p > probabilities[best]) best = i;
            }
            if (Math.Abs(total - 1) > 0.001) throw new InvalidOperationException("概率和无效"); // lint-ok: 神经网络浮点输出校验，结果仅供实验展示不参与确定性玩法
            return best;
        }

        private void Release()
        {
            if (feedback != null) feedback.Reset();
            feedback = null; diagnosticDetails = null;
            Array.Clear(scores, 0, scores.Length);
            Classification = Rejection = "等待当前输入";
            Result = "等待当前输入";
            hasObserved = false; pending = false; nextInferenceTime = 0;
            if (input != null) { input.Dispose(); input = null; }
            if (worker != null) { worker.Dispose(); worker = null; }
        }
    }
}
