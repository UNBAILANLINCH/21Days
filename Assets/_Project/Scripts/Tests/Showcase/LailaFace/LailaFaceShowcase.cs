using UnityEngine;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Game.LailaFace;

namespace Game.Tests.Showcase.LailaFace
{
    // 职责：回放 laila 场景里的 Head-topo 控制器，验证 16 个形变、成对映射与重置。
    // 新建原因：LailaFace 是新玩法测试入口，需要独立的可读回放，不把检查塞进运行时脚本。
    [Category("Showcase")]
    public sealed class LailaFaceShowcase : ShowcaseScenario
    {
        private static readonly string[] ShapeNames =
        {
            "Mouth_L_Up", "Mouth_L_Down", "Mouth_R_Up", "Mouth_R_Down",
            "Brow_L_Up", "Brow_L_Down", "Brow_R_Up", "Brow_R_Down",
            "Eye_L_UpperLid_Up", "Eye_L_UpperLid_Down",
            "Eye_L_LowerLid_Up", "Eye_L_LowerLid_Down",
            "Eye_R_UpperLid_Up", "Eye_R_UpperLid_Down",
            "Eye_R_LowerLid_Up", "Eye_R_LowerLid_Down"
        };

        protected override string Module => "LailaFace";
        protected override string ScenePath => "Assets/_Project/Scenes/laila.unity";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator HeadTopo_ControlsBlendShapes_AndResets()
        {
            FaceBlendShapeController face = FindRequired<FaceBlendShapeController>("Head-topo");

            yield return Step("检查 Head-topo 已导入 16 个 BlendShape");
            yield return Check("16 个 BlendShape 名称全部存在", () => HasAllShapes(face));

            yield return Step("向上拖动左嘴角控制区", () => face.SetSignedPair("Mouth_L_Up", "Mouth_L_Down", 0.7f));
            yield return Check(
                "左嘴角 Up=70，Down=0",
                () => Mathf.Approximately(face.GetWeight("Mouth_L_Up"), 70f)
                    && Mathf.Approximately(face.GetWeight("Mouth_L_Down"), 0f));
            yield return Snapshot("左嘴角上拉");

            yield return Step("向下拖动右上眼皮控制区", () =>
                face.SetSignedPair("Eye_R_UpperLid_Up", "Eye_R_UpperLid_Down", -0.55f));
            yield return Check(
                "右上眼皮 Up=0，Down=55",
                () => Mathf.Approximately(face.GetWeight("Eye_R_UpperLid_Up"), 0f)
                    && Mathf.Approximately(face.GetWeight("Eye_R_UpperLid_Down"), 55f));
            yield return Snapshot("右上眼皮下拉");

            yield return Step("点击 Reset Face", face.ResetFace);
            yield return Check("16 个 BlendShape 全部恢复为 0", () => AllWeightsAreZero(face));
            yield return Snapshot("重置后");
        }

        private static bool HasAllShapes(FaceBlendShapeController face)
        {
            for (int i = 0; i < ShapeNames.Length; i++)
            {
                if (!face.HasShape(ShapeNames[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllWeightsAreZero(FaceBlendShapeController face)
        {
            for (int i = 0; i < ShapeNames.Length; i++)
            {
                if (!Mathf.Approximately(face.GetWeight(ShapeNames[i]), 0f))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
