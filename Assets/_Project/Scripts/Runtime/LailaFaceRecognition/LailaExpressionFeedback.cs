using System;

namespace Game.LailaFaceRecognition
{
    // 职责：展示五类的近零覆盖与迟滞；不改模型概率，也不判关卡成功。
    // 新建原因：已有Decide是历史拒识；扩展面板或推理方法会把可测试的时序规则与UI/Worker耦合。
    public sealed class LailaExpressionFeedback
    {
        private readonly LailaExpressionFeedbackConfig config;
        private readonly float[] previous = new float[17];
        private int candidate = -1;
        private double candidateSince;
        private double lastTime;
        public int DisplayIndex { get; private set; } = -1;
        public bool NeutralOverride { get; private set; }
        public float NormalizedAmplitude { get; private set; }
        public LailaExpressionFeedback(LailaExpressionFeedbackConfig settings)
        { if (settings == null) throw new ArgumentNullException(nameof(settings)); settings.Validate(); config = settings; }
        public void Reset()
        { DisplayIndex = candidate = -1; NeutralOverride = false; NormalizedAmplitude = 0; candidateSince = lastTime = 0; Array.Clear(previous, 0, previous.Length); }
        public bool Observe(float[] axes, float[] probabilities, double time)
        {
            if (axes == null || axes.Length != 17 || double.IsNaN(time) || double.IsInfinity(time) || time < lastTime)
                throw new InvalidOperationException("反馈输入或时间无效");
            int top = LailaExpressionRecognizer.HighestClass(probabilities, 0);
            float amplitude = 0, change = 0;
            for (int i = 0; i < 17; i++)
            {
                float value = axes[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || value < (i < 14 ? -1 : 0) || value > 1)
                    throw new InvalidOperationException("反馈17轴无效");
                amplitude = Math.Max(amplitude, Math.Abs(value) / config.Tolerance(i)); // lint-ok: 渲染权重的展示中性区，不参与确定性玩法或回放
                change = Math.Max(change, Math.Abs(value - previous[i])); // lint-ok: 仅展示切换响应，不判关卡
            }
            NormalizedAmplitude = amplitude;
            NeutralOverride = NeutralOverride ? amplitude < config.NeutralExitScale : amplitude <= 1;
            int desired = NeutralOverride ? 0 : top;
            float second = 0;
            for (int i = 0; i < 5; i++) if (i != top) second = Math.Max(second, probabilities[i]); // lint-ok: 模型展示迟滞，不修改模型或确定性状态
            if (candidate != desired) { candidate = desired; candidateSince = time; }
            Array.Copy(axes, previous, 17); lastTime = time;
            int old = DisplayIndex;
            if (DisplayIndex < 0 || NeutralOverride || change >= config.ImmediateInputChange
                || probabilities[top] - second >= config.DecisiveMargin || time - candidateSince >= config.SwitchDelay)
                DisplayIndex = desired;
            return old != DisplayIndex;
        }
        // 静止脸不重复推理，仍须按经过时间完成已冻结候选；输入等待/错误时调用方不得推进。
        public bool Advance(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < lastTime) throw new InvalidOperationException("反馈时间无效");
            lastTime = time;
            if (candidate < 0 || candidate == DisplayIndex || time - candidateSince < config.SwitchDelay) return false;
            DisplayIndex = candidate; return true;
        }
    }
}
