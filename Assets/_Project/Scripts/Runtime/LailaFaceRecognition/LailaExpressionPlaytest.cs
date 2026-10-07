using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using Unity.Sentis;
using UnityEngine;

namespace Game.LailaFaceRecognition
{
    // 职责：独立研究试玩的典型脸、强度、复位和旧／新模式按钮；不采集或标注。
    // 新建原因：现有结果面板只展示推理，没有可操作的试玩配方／回退职责。
    public sealed class LailaExpressionPlaytest : MonoBehaviour
    {
        [SerializeField] private LailaExpressionRecognizer recognizer;
        [SerializeField] private TextAsset recipes;
        [SerializeField] private TMP_Text modeText;
        [SerializeField] private TMP_Text strengthText;
        [SerializeField] private ModelAsset baselineResearchModel;
        [SerializeField] private TextAsset baselineResearchMetadata;
        [SerializeField] private string baselineResearchHash;
        [SerializeField] private ModelAsset feedbackResearchModel;
        [SerializeField] private TextAsset feedbackResearchMetadata;
        [SerializeField] private string feedbackResearchHash;
        [SerializeField] private ModelAsset strengthResearchModel;
        [SerializeField] private TextAsset strengthResearchMetadata;
        [SerializeField] private string strengthResearchHash;
        private float[][] previousAngry;
        private int angryVariant;
        private float[][] poses;
        private float[][] oldAngry;

        private readonly float[] original = new float[17];
        private bool captured;
        private int strength = 1;
        private int selected;
        private readonly List<GameObject> suspendedRoots = new List<GameObject>();

        private void Start() => EnsureInitialized();

        private void OnEnable()
        {
            if (poses != null) SuspendOtherSceneRoots();
        }

        private void EnsureInitialized()
        {
            if (poses != null) return;
            if (recognizer == null || recognizer.Face == null || recipes == null)
            { enabled = false; return; }
            string error;
            captured = LailaExpressionSampler.TrySample(recognizer.Face, original, out error);
            var rows = JObject.Parse(recipes.text)["playtest_presets"] as JArray;
            if (rows == null || rows.Count != 15) throw new InvalidOperationException("试玩配方须为五类各三种强度");
            poses = new float[rows.Count][];
            for (int i = 0; i < rows.Count; i++) poses[i] = rows[i]["values"].ToObject<float[]>();
            oldAngry = new float[3][];
            previousAngry = new float[3][];
            float[] strengths = { .5f, .75f, 1f };
            for (int i = 0; i < 3; i++)
            {
                oldAngry[i] = (float[])poses[12 + i].Clone();
                // 只调设计配方：眉头压低、眉尾相对抬高，不按模型输出自动选配方。
                poses[12 + i][0] = poses[12 + i][3] = -.85f * strengths[i];
                poses[12 + i][1] = poses[12 + i][4] = -.25f * strengths[i];
                poses[12 + i][2] = poses[12 + i][5] = .65f * strengths[i];
                previousAngry[i] = (float[])poses[12 + i].Clone();
                // 四图受控视觉对照仅支持保留眉角并放开眼裂；原下睑与旧眉配方均另存，不是语义金标。
                poses[12 + i][7] = poses[12 + i][9] = 0;
            }
            SuspendOtherSceneRoots();
            Refresh();
        }

        private void SuspendOtherSceneRoots()
        {
            // 独立试玩只在Play期间暂停其他已加载场景的根对象，防止相机／射线重复；退出恢复。
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene == gameObject.scene) continue;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.activeSelf) { suspendedRoots.Add(root); root.SetActive(false); }
            }
            // 常驻选曲Canvas不在SceneManager枚举中，研究试玩也须暂停其射线遮挡；不暂停常驻时间等服务。
            foreach (var canvas in FindObjectsOfType<Canvas>())
                if (canvas.isRootCanvas && canvas.gameObject.scene.name == "DontDestroyOnLoad" && canvas.gameObject.activeSelf)
                { suspendedRoots.Add(canvas.gameObject); canvas.gameObject.SetActive(false); }
        }

        public void Neutral() => ApplyPreset(0);
        public void Happy() => ApplyPreset(1);
        public void Sad() => ApplyPreset(2);
        public void SurpriseFear() => ApplyPreset(3);
        public void Angry() => ApplyPreset(4);
        public void OldAngry() => ApplyAngryComparison(2);
        public void PreviousAngry() => ApplyAngryComparison(1);

        private void ApplyAngryComparison(int variant)
        {
            if (!isActiveAndEnabled) return;
            ApplyPreset(4);
            angryVariant = variant;
            string error;
            var values = variant == 1 ? previousAngry[strength] : oldAngry[strength];
            if (!LailaExpressionSampler.TryApply(recognizer.Face, values, out error)) throw new InvalidOperationException(error);
            Refresh();
        }

        public void ApplyPreset(int category)
        {
            if (!isActiveAndEnabled || category < 0 || category >= 5) return;
            EnsureInitialized();
            if (poses == null) throw new InvalidOperationException("试玩引用未就绪，不能应用配方");
            selected = category;
            angryVariant = 0;
            string error;
            if (!LailaExpressionSampler.TryApply(recognizer.Face, poses[category * 3 + strength], out error))
                throw new InvalidOperationException(error);
            Refresh();
        }

        public void CycleStrength()
        {
            strength = (strength + 1) % 3;
            if (selected == 4 && angryVariant != 0) ApplyAngryComparison(angryVariant);
            else ApplyPreset(selected);
            Refresh();
        }

        public void ResetFace()
        {
            if (recognizer != null && recognizer.Face != null) recognizer.Face.ResetFace();
        }

        public void ToggleModel()
        {
            if (recognizer == null) return;
            if (baselineResearchModel == null || feedbackResearchModel == null)
                recognizer.SetResearchMode(!recognizer.UsesResearchCandidate);
            else if (strengthResearchModel != null && !recognizer.UsesResearchCandidate)
                recognizer.SetResearchCandidate(strengthResearchModel, strengthResearchMetadata, strengthResearchHash, true);
            else if (strengthResearchModel != null && recognizer.ActiveModelHash == strengthResearchHash)
                recognizer.SetResearchCandidate(feedbackResearchModel, feedbackResearchMetadata, feedbackResearchHash, true);
            else if (!recognizer.UsesResearchCandidate)
                recognizer.SetResearchCandidate(feedbackResearchModel, feedbackResearchMetadata, feedbackResearchHash, true);
            else if (recognizer.ActiveModelHash == feedbackResearchHash)
                recognizer.SetResearchCandidate(baselineResearchModel, baselineResearchMetadata, baselineResearchHash);
            else recognizer.SetResearchMode(false);
            Refresh();
        }

        public void RestoreFace()
        {
            if (!captured || recognizer == null || recognizer.Face == null) return;
            string error;
            LailaExpressionSampler.TryApply(recognizer.Face, original, out error);
        }

        private void Refresh()
        {
            if (modeText != null) modeText.text = recognizer != null && recognizer.UsesResearchCandidate
                ? (recognizer.UsesStableFeedback
                    ? (recognizer.ActiveModelHash == strengthResearchHash ? "定向修复59D · 20261006 · 五类稳定反馈" : "反馈59D · 20261005 · 五类稳定反馈")
                    : "旧r779 59D · 历史拒识诊断")
                : "旧51D · 原拒识规则";
            if (strengthText != null) strengthText.text = "强度：" + new[] { "轻", "中", "强" }[strength]
                + (selected == 4 ? " · " + new[] { "下睑0对照", "原下睑", "旧眉" }[angryVariant] : "");
        }

        private void OnDisable()
        {
            RestoreFace();
            foreach (var root in suspendedRoots) if (root != null) root.SetActive(true);
            suspendedRoots.Clear();
        }
    }
}
