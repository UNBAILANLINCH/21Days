// 职责：人工菜单生成划拳视图并登记地址，不改场景和已有美术。
// 其它 UI 生成器的布局是模块私有；复用原有字体、UIView 与 Addressables 接法。
// 载体：人工菜单；锚点：TavernFingerGuessView.prefab；正式美术接管后停止重建。
using System;
using Game.Tavern;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace Game.Editor.ShopPlaytest
{
    public static class TavernFingerGuessBuilder
    {
        private const string Path = "Assets/_Project/Prefabs/UI/TavernFingerGuessView.prefab";
        [MenuItem("21Days/酒肆/生成划拳界面")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("退出 Play 后生成划拳界面");
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            if (existing != null && existing.transform.Find("Paper") != null)
                throw new InvalidOperationException("划拳已接入项目美术，停止白盒覆盖；直接编辑现有预制体");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings == null ? null : settings.FindGroup("UI");
            if (group == null) throw new InvalidOperationException("缺少 UI 地址组");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/ForgingView.prefab");
            var text = source == null ? null : source.GetComponentInChildren<TMP_Text>(true);
            if (text == null || text.font == null) throw new InvalidOperationException("缺少中文字体来源");
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = Node("TavernFingerGuessView", null, Vector2.zero, Vector2.zero);
                SceneManager.MoveGameObjectToScene(root, preview);
                var rect = (RectTransform)root.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                root.AddComponent<CanvasGroup>();
                root.AddComponent<Image>().color = new Color(.025f, .035f, .04f, .93f);
                var view = root.AddComponent<TavernFingerGuessView>();
                var title = Label("Title", root.transform, "划拳行酒令", new Vector2(0, 350), new Vector2(1100, 70), text.font, 38);
                var status = Label("Status", root.transform, "", new Vector2(0, 285), new Vector2(1100, 45), text.font, 23);
                Label("FingersTitle", root.transform, "出几指", new Vector2(0, 205), new Vector2(1100, 40), text.font, 24);
                var fingers = new Button[6]; var calls = new Button[6]; var labels = new TMP_Text[6];
                for (int i = 0; i < 6; i++)
                {
                    fingers[i] = Button("Finger" + i, root.transform, i + " 指", new Vector2(-450 + i * 180, 130), new Vector2(155, 70), text.font);
                    calls[i] = Button("Call" + i, root.transform, "", new Vector2(-450 + i * 180, -20), new Vector2(155, 90), text.font);
                    labels[i] = calls[i].GetComponentInChildren<TMP_Text>();
                }
                Label("CallTitle", root.transform, "喊双方手指的总数", new Vector2(0, 60), new Vector2(1100, 40), text.font, 24);
                var result = Label("Result", root.transform, "", new Vector2(0, -190), new Vector2(1220, 225), text.font, 26);
                var confirm = Button("Confirm", root.transform, "出拳揭晓", new Vector2(-270, -370), new Vector2(235, 70), text.font);
                var next = Button("Next", root.transform, "再来一拳", new Vector2(0, -370), new Vector2(235, 70), text.font);
                var back = Button("Back", root.transform, "离席", new Vector2(270, -370), new Vector2(235, 70), text.font);
                using (var serialized = new SerializedObject(view))
                {
                    serialized.FindProperty("title").objectReferenceValue = title; serialized.FindProperty("status").objectReferenceValue = status;
                    serialized.FindProperty("resultLabel").objectReferenceValue = result;
                    Array(serialized, "fingerButtons", fingers); Array(serialized, "callButtons", calls); Array(serialized, "callLabels", labels);
                    serialized.FindProperty("confirmButton").objectReferenceValue = confirm; serialized.FindProperty("nextButton").objectReferenceValue = next;
                    serialized.FindProperty("backButton").objectReferenceValue = back; serialized.FindProperty("defaultSelected").objectReferenceValue = fingers[1];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, Path, out bool saved);
                if (!saved) throw new InvalidOperationException("划拳视图保存失败");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(Path), group, false, false);
            entry.address = nameof(TavernFingerGuessView);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, false);
            AssetDatabase.SaveAssetIfDirty(group);
        }
        private static void Array(SerializedObject serialized, string name, UnityEngine.Object[] values)
        {
            var array = serialized.FindProperty(name); array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        internal static GameObject Node(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform));
            if (parent != null) node.transform.SetParent(parent, false);
            var rect = (RectTransform)node.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; return node;
        }
        internal static TMP_Text Label(string name, Transform parent, string content, Vector2 position, Vector2 size, TMP_FontAsset font, int fontSize)
        {
            var label = Node(name, parent, position, size).AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = fontSize; label.text = content; label.color = new Color(.94f, .88f, .73f);
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false; return label;
        }
        private static Button Button(string name, Transform parent, string content, Vector2 position, Vector2 size, TMP_FontAsset font)
        {
            var node = Node(name, parent, position, size); var image = node.AddComponent<Image>(); image.color = new Color(.2f, .25f, .26f);
            var button = node.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            Label("Label", node.transform, content, Vector2.zero, size - new Vector2(8, 4), font, 24); return button;
        }
    }
}
