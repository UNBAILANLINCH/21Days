using System;
using Game.LailaFace;

namespace Game.LailaFaceRecognition
{
    // 职责：只读采样当前31形态的17轴，拒绝缺键、越界和成对冲突。
    // 新建原因：旧控制器getter缺键返回零，无法提供严格识别输入契约。
    public static class LailaExpressionSampler
    {
        public static string[] Keys => new[] {
            "brow_L_inner_y", "brow_L_mid_y", "brow_L_outer_y",
            "brow_R_inner_y", "brow_R_mid_y", "brow_R_outer_y",
            "eye_L_upper_y", "eye_L_lower_y", "eye_R_upper_y", "eye_R_lower_y",
            "mouth_corner_L_y", "mouth_corner_R_y", "mouth_corner_L_x", "mouth_corner_R_x",
            "upper_lip_L", "upper_lip_R", "lower_lip" };

        private static readonly string[] Positive = {
            "Brow_L_Inner_Up", "Brow_L_Mid_Up", "Brow_L_Outer_Up",
            "Brow_R_Inner_Up", "Brow_R_Mid_Up", "Brow_R_Outer_Up",
            "Eye_L_UpperLid_Up", "Eye_L_LowerLid_Up", "Eye_R_UpperLid_Up", "Eye_R_LowerLid_Up",
            "Mouth_L_Up", "Mouth_R_Up", "Mouth_L_Out", "Mouth_R_Out",
            "Mouth_UpperLipL_Up", "Mouth_UpperLipR_Up", "Mouth_LowerLip_Down" };
        private static readonly string[] Negative = {
            "Brow_L_Inner_Down", "Brow_L_Mid_Down", "Brow_L_Outer_Down",
            "Brow_R_Inner_Down", "Brow_R_Mid_Down", "Brow_R_Outer_Down",
            "Eye_L_UpperLid_Down", "Eye_L_LowerLid_Down", "Eye_R_UpperLid_Down", "Eye_R_LowerLid_Down",
            "Mouth_L_Down", "Mouth_R_Down", "Mouth_L_In", "Mouth_R_In" };

        private static readonly string[] MissingErrors = CreateErrors("缺少形态：", false);
        private static readonly string[] WeightErrors = CreateErrors("形态权重非有限或越界：", false);
        private static readonly string[] PairErrors = CreateErrors("同对形态冲突：", true);

        private static string[] CreateErrors(string prefix, bool pair)
        {
            var messages = new string[17];
            for (int i = 0; i < 17; i++) messages[i] = prefix + Positive[i] + (pair && i < 14 ? " / " + Negative[i] : "");
            return messages;
        }

        public static bool TrySample(FaceBlendShapeController face, float[] destination, out string error)
        {
            error = null;
            if (face == null || !face.isActiveAndEnabled || destination == null || destination.Length != 17)
            { error = "脸部引用或输入缓冲无效"; return false; }
            return TryRead(face.HasShape, face.GetWeight, destination, out error);
        }

        public static bool TryRead(Func<string, bool> exists, Func<string, float> read, float[] destination, out string error)
            => TryRead(exists, read, destination, new float[17], out error);

        // 研究试玩配方复用采样器的形态表；完整校验后才赋权，不制造缺失控制器。
        public static bool TryApply(FaceBlendShapeController face, float[] values, out string error)
        {
            error = null;
            if (face == null || values == null || values.Length != 17)
            { error = "试玩输入无效"; return false; }
            for (int i = 0; i < 17; i++)
                if (!face.HasShape(Positive[i]) || (i < 14 && !face.HasShape(Negative[i]))
                    || float.IsNaN(values[i]) || float.IsInfinity(values[i]) || values[i] < (i < 14 ? -1 : 0) || values[i] > 1)
                { error = "试玩配方与当前脸部不符"; return false; }
            for (int i = 0; i < 17; i++)
            {
                face.SetWeight(Positive[i], Math.Max(values[i], 0) * 100); // lint-ok: 仅研究试玩的渲染形态赋权，不参与玩法回放
                if (i < 14) face.SetWeight(Negative[i], Math.Max(-values[i], 0) * 100); // lint-ok: 仅研究试玩的渲染形态赋权，不参与玩法回放
            }
            return true;
        }

        // 调用方持有独立暂存区和缓存委托，实时采样不产生每帧数组或委托。
        public static bool TryRead(Func<string, bool> exists, Func<string, float> read, float[] destination,
            float[] values, out string error)
        {
            error = null;
            if (exists == null || read == null || destination == null || destination.Length != 17
                || values == null || values.Length != 17 || ReferenceEquals(values, destination))
            { error = "输入缓冲无效"; return false; }
            // 完整验证后才写目标数组，失败不会留下半个有效快照。
            for (int i = 0; i < 17; i++)
            {
                if (!exists(Positive[i]) || (i < 14 && !exists(Negative[i])))
                { error = MissingErrors[i]; return false; }
                float p = read(Positive[i]), n = i < 14 ? read(Negative[i]) : 0;
                if (!Valid(p) || !Valid(n)) { error = WeightErrors[i]; return false; }
                if (i < 14 && p > 0.001f && n > 0.001f)
                { error = PairErrors[i]; return false; }
                values[i] = (Math.Min(100, Math.Max(0, p)) - Math.Min(100, Math.Max(0, n))) / 100; // lint-ok: 只读渲染形态采样，不参与确定性玩法或回放
            }
            Array.Copy(values, destination, 17);
            return true;
        }

        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value)
            && value >= -0.001f && value <= 100.001f;
    }
}
