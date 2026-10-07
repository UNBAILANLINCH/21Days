using System;
using UnityEngine;

namespace Game.LailaFaceRecognition
{
    // 职责：近零中性区与展示迟滞的可调初始参数，不代表可见形变或语义校准。
    // 新建原因：模型元数据只含历史拒识；把展示规则塞入模型或UI不能保持职责与历史字节。
    [CreateAssetMenu(menuName = "21Days/Laila/五类反馈配置")]
    public sealed class LailaExpressionFeedbackConfig : ScriptableObject
    {
        [SerializeField] private float[] neutralTolerances = {
            .03f, .03f, .03f, .03f, .03f, .03f,
            .02f, .02f, .02f, .02f,
            .03f, .03f, .03f, .03f, .02f, .02f, .02f };
        [SerializeField] private float neutralExitScale = 1.5f;
        [SerializeField] private double switchDelay = .18;
        [SerializeField] private float decisiveMargin = .25f;
        [SerializeField] private float immediateInputChange = .12f;
        public float Tolerance(int axis) => neutralTolerances[axis];
        public float NeutralExitScale => neutralExitScale;
        public double SwitchDelay => switchDelay;
        public float DecisiveMargin => decisiveMargin;
        public float ImmediateInputChange => immediateInputChange;
        public void Validate()
        {
            if (neutralTolerances == null || neutralTolerances.Length != 17)
                throw new InvalidOperationException("中性区配置必须覆盖17轴");
            foreach (float value in neutralTolerances)
                if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0 || value >= 1)
                    throw new InvalidOperationException("中性区容差无效");
            if (float.IsNaN(neutralExitScale) || float.IsInfinity(neutralExitScale) || neutralExitScale <= 1
                || double.IsNaN(switchDelay) || double.IsInfinity(switchDelay) || switchDelay < 0
                || float.IsNaN(decisiveMargin) || decisiveMargin <= 0 || decisiveMargin > 1
                || float.IsNaN(immediateInputChange) || immediateInputChange <= 0 || immediateInputChange > 1)
                throw new InvalidOperationException("反馈时序配置无效");
        }
    }
}
