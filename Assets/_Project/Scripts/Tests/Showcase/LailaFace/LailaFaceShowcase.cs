using UnityEngine;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using Game.LailaFace;
using Game.LailaFaceRecognition;

namespace Game.Tests.Showcase.LailaFace
{
    // 职责：在唯一Laila试玩场景验证当前31形态、控制区、重置和四版模型切换。
    [Category("Showcase")]
    public sealed class LailaFaceShowcase : ShowcaseScenario
    {
        protected override string Module => "LailaFace";
        protected override string ScenePath => "Assets/_Project/Scenes/LailaRecognitionPlaytest.unity";
        protected override bool LoadBootScene => false;

        [UnityTest]
        public IEnumerator HeadTopo_ControlsBlendShapes_AndResets()
        {
            var recognizer = FindRequired<LailaExpressionRecognizer>("LailaExpressionRecognition");
            var controls = FindRequired<LailaExpressionPlaytest>("PlaytestControls");
            FaceBlendShapeController face = recognizer.Face;
            Assert.That(face, Is.Not.Null);
            Assert.That(face.isActiveAndEnabled, Is.True);

            yield return Step("检查当前脸部31个形态及17个控制区");
            yield return Check("31形态满足严格17轴输入契约",
                () => face.BlendShapeCount == 31 && HasValidInput(face));
            yield return Check("17个控制区已启用", () =>
            {
                int count = 0;
                foreach (var handle in face.GetComponentsInChildren<FaceDragHandle>())
                    if (handle.isActiveAndEnabled) count++;
                return count == 17;
            });
            yield return Check("当前脸部已创建抓点反馈",
                () => face.GetComponent<Game.Gameplay.FacePointerFeedback>() != null);

            yield return Step("向上拖动左嘴角控制区",
                () => face.SetSignedPair("Mouth_L_Up", "Mouth_L_Down", 0.7f));
            yield return Check("左嘴角 Up=70，Down=0",
                () => Mathf.Approximately(face.GetWeight("Mouth_L_Up"), 70f)
                    && Mathf.Approximately(face.GetWeight("Mouth_L_Down"), 0f));
            yield return Snapshot("左嘴角上拉");

            yield return Step("向下拖动右上眼皮控制区",
                () => face.SetSignedPair("Eye_R_UpperLid_Up", "Eye_R_UpperLid_Down", -0.55f));
            yield return Check("右上眼皮 Up=0，Down=55",
                () => Mathf.Approximately(face.GetWeight("Eye_R_UpperLid_Up"), 0f)
                    && Mathf.Approximately(face.GetWeight("Eye_R_UpperLid_Down"), 55f));
            yield return Snapshot("右上眼皮下拉");

            yield return Step("点击 Reset Face", controls.ResetFace);
            yield return Check("全部31形态恢复为0", () => AllWeightsAreZero(face));
            yield return Snapshot("重置后");

            string[] hashes = {
                "11f828902e4cd26eb6488a1f744a9bad71d9a06ab76108a50dca547b43bf3b3b",
                "9d0999f9164dcbb5b00a93a6d5b7082912abf93a5c07f4478966bc631f8733b2",
                "d9e70e5c422be33298e0df35a526aeff26f931f63cc7a8870bc327569697a101",
                "7e63c6fc1f2b4da857fbe4a3ba00885bf43f39b6483e3b572d65c0ad6b93396f"
            };
            yield return Check("默认定向修复模型已就绪",
                () => recognizer.IsReady && recognizer.ActiveModelHash == hashes[0]);
            for (int i = 1; i <= hashes.Length; i++)
            {
                string expected = hashes[i % hashes.Length];
                yield return Step("切换模型 " + i, controls.ToggleModel);
                yield return Check("切换后的模型与元数据可实际推理",
                    () => recognizer.IsReady && recognizer.ActiveModelHash == expected && recognizer.Recognize());
            }
        }

        private static bool HasValidInput(FaceBlendShapeController face)
        {
            string error;
            return LailaExpressionSampler.TrySample(face, new float[17], out error);
        }

        private static bool AllWeightsAreZero(FaceBlendShapeController face)
        {
            var values = new float[17];
            string error;
            if (!LailaExpressionSampler.TrySample(face, values, out error)) return false;
            foreach (float value in values)
                if (!Mathf.Approximately(value, 0f)) return false;
            return true;
        }
    }
}
