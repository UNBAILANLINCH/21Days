using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Game.Editor.Tools
{
    // 职责：打开已接线的研究试玩，退出Play后恢复之前加载的场景；不创建或升级资产。
    // 恢复载体：菜单及跨域重载的playModeStateChanged；SessionState键可即时核对。
    // 退场：恢复场景或启动失败后清键并退订；移除菜单时同时移除本恢复机制。
    public static class LailaPlaytestTools
    {
        public const string ScenePath = "Assets/_Project/Scenes/LailaRecognitionPlaytest.unity";
        private const string PreviousScenesKey = "21Days.LailaPlaytest.PreviousScenes";

        [InitializeOnLoadMethod]
        private static void ObservePlaytestExit()
        {
            EditorApplication.playModeStateChanged -= RestorePreviousScenes;
            if (!string.IsNullOrEmpty(SessionState.GetString(PreviousScenesKey, "")))
                EditorApplication.playModeStateChanged += RestorePreviousScenes;
        }

        private static void RestorePreviousScenes(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= RestorePreviousScenes;
            string saved = SessionState.GetString(PreviousScenesKey, "");
            if (string.IsNullOrEmpty(saved)) return;
            SessionState.EraseString(PreviousScenesKey);
            var rows = Newtonsoft.Json.Linq.JArray.Parse(saved);
            var setup = new SceneSetup[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                setup[i] = new SceneSetup { path = (string)rows[i]["path"], isLoaded = (bool)rows[i]["loaded"], isActive = (bool)rows[i]["active"] };
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        [MenuItem("21Days/Laila/候选试玩（含旧版对照）")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先结束当前Play，再打开试玩");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new InvalidOperationException("Laila试玩场景缺失，请恢复已提交的场景资产");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("当前场景有未保存改动，请先保存或自行处理");
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("当前有尚未保存的场景，请先自行保存或关闭");
            }
            var previous = new Newtonsoft.Json.Linq.JArray();
            foreach (var item in EditorSceneManager.GetSceneManagerSetup())
                previous.Add(new Newtonsoft.Json.Linq.JObject { ["path"] = item.path, ["loaded"] = item.isLoaded, ["active"] = item.isActive });
            SessionState.SetString(PreviousScenesKey, previous.ToString());
            ObservePlaytestExit();
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SceneManager.SetActiveScene(scene);
                EditorApplication.isPlaying = true;
            }
            catch
            {
                RestorePreviousScenes(PlayModeStateChange.EnteredEditMode);
                throw;
            }
        }
    }
}
