using System;
using System.IO;
using System.Security.Cryptography;
using Game.LailaFaceRecognition;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Game.Editor.Tools
{
    // 职责：进入Play与构建场景时核对源ONNX/JSON配对，不依赖Inspector内声称的hash。
    // 执行主体：编辑器Play切换与BuildPipeline场景处理；5秒检查：换错模型后Play被拒并报告配对错误。
    // 退出：domain卸载事件自然清理；删去本类即可移除守卫。现有项目没有模型配对守卫可复用。
    [InitializeOnLoad]
    public sealed class LailaRecognitionDeploymentCheck : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        static LailaRecognitionDeploymentCheck()
        {
            EditorApplication.playModeStateChanged -= OnPlayState;
            EditorApplication.playModeStateChanged += OnPlayState;
        }
        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++) CheckScene(SceneManager.GetSceneAt(i));
            }
            catch (Exception e) { EditorApplication.isPlaying = false; UnityEngine.Debug.LogError("识别部署校验失败：" + e.Message); }
        }
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report != null)
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var recognizer in root.GetComponentsInChildren<LailaExpressionRecognizer>(true))
                        if (recognizer.UsesResearchCandidate) throw new BuildFailedException("59D研究试玩不进入正式Player构建");
            CheckScene(scene);
        }
        private static void CheckScene(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var recognizer in root.GetComponentsInChildren<LailaExpressionRecognizer>(true)) Validate(recognizer);
        }
        public static void Validate(LailaExpressionRecognizer recognizer)
        {
            var so = new SerializedObject(recognizer);
            var model = so.FindProperty("modelAsset").objectReferenceValue;
            var metaAsset = so.FindProperty("metadata").objectReferenceValue as UnityEngine.TextAsset;
            string path = AssetDatabase.GetAssetPath(model);
            if (model == null || metaAsset == null || !File.Exists(path)) throw new BuildFailedException("模型或元数据缺失");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            if (hash != so.FindProperty("approvedModelHash").stringValue) throw new BuildFailedException("源ONNX与批准的模型hash不匹配");
            LailaExpressionRecognizer.ValidateMetadata(JObject.Parse(metaAsset.text), hash, so.FindProperty("approvedRigHash").stringValue);
            var researchModel = so.FindProperty("researchModelAsset").objectReferenceValue;
            var researchMeta = so.FindProperty("researchMetadata").objectReferenceValue as UnityEngine.TextAsset;
            if (researchModel != null || researchMeta != null || recognizer.UsesResearchCandidate)
            {
                string researchPath = AssetDatabase.GetAssetPath(researchModel);
                if (researchModel == null || researchMeta == null || !File.Exists(researchPath))
                    throw new BuildFailedException("研究模型或元数据缺失");
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(researchPath))).Replace("-", "").ToLowerInvariant();
                if (hash != so.FindProperty("approvedResearchModelHash").stringValue)
                    throw new BuildFailedException("研究ONNX源字节hash不匹配");
                LailaExpressionRecognizer.ValidateMetadata(JObject.Parse(researchMeta.text), hash, so.FindProperty("approvedRigHash").stringValue);
            }
        }
    }
}
