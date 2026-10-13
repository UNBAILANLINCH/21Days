// 职责：生成共用小地图 HUD，把任务栏摆到下方；只保存这两个预制体与 UI 组，不改场景。
// 为什么新建：现有 UI 生成器的建图函数都是模块私有，不能复用；扩展战斗生成器会混入世界地图职责。
// 载体：人工执行菜单；锚点：SceneMapView.prefab 与 UI 组同名地址；正式美术接管后停用本生成器。
using System;
using Game.Core.UI;
using Game.World;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Editor.World
{
    public static class SceneMapBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/UI/SceneMapView.prefab";

        [MenuItem("21Days/世界/生成最小场景地图")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("退出 Play 后才能生成地图资产。");
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetGroup group = settings == null ? null : settings.FindGroup("UI");
            if (group == null) throw new InvalidOperationException("找不到 Addressables UI 组。");
            GameObject inventory = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/InventoryPanelView.prefab");
            TMP_Text source = inventory == null ? null : inventory.GetComponentInChildren<TMP_Text>(true);
            if (source == null || source.font == null) throw new InvalidOperationException("找不到现有界面的中文字体。");
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = Node("SceneMapView", null, Vector2.zero, Vector2.zero);
                SceneManager.MoveGameObjectToScene(root, preview);
                Stretch(root);
                root.AddComponent<CanvasGroup>().blocksRaycasts = false;
                var view = root.AddComponent<SceneMapView>();
                GameObject panel = Node("MapPanel", root.transform, new Vector2(24f, -20f), new Vector2(264f, 294f));
                var panelRect = (RectTransform)panel.transform;
                panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0f, 1f);
                Image background = panel.AddComponent<Image>();
                background.color = new Color(0.07f, 0.10f, 0.09f, 0.92f);
                background.raycastTarget = false;
                GameObject contents = Node("Contents", panel.transform, Vector2.zero, Vector2.zero);
                Stretch(contents);
                TMP_Text title = Text("Title", contents.transform, "探索地图", new Vector2(0f, 122f), new Vector2(236f, 28f), source.font, 16f);
                title.alignment = TextAlignmentOptions.Left;
                title.enableAutoSizing = true;
                title.fontSizeMin = 11f;
                title.fontSizeMax = 16f;
                GameObject area = Node("Map", contents.transform, new Vector2(0f, -6f), new Vector2(224f, 224f));
                var graphic = area.AddComponent<SceneMapGraphic>();
                graphic.raycastTarget = false;
                area.AddComponent<RectMask2D>();
                TMP_Text north = Text("North", area.transform, "北 ↑", new Vector2(94f, 98f), new Vector2(36f, 20f), source.font, 12f);
                north.color = new Color(0.65f, 0.74f, 0.68f);
                Image template = Dot("MarkerTemplate", area.transform, new Color(1f, 0.78f, 0.35f), 6f);
                Image player = Dot("Player", area.transform, new Color(0.3f, 0.9f, 1f), 8f);
                template.gameObject.SetActive(false);
                Text("Legend", contents.transform,
                    "<color=#4DE6FF>● 位置</color>  <color=#FFC759>● 入口</color>  <color=#CCAB80>● 楼梯</color>",
                    new Vector2(0f, -135f), new Vector2(244f, 20f), source.font, 12f);
                using (var serialized = new SerializedObject(view))
                {
                    serialized.FindProperty("map").objectReferenceValue = graphic;
                    serialized.FindProperty("title").objectReferenceValue = title;
                    serialized.FindProperty("contentRoot").objectReferenceValue = panel;
                    serialized.FindProperty("playerMarker").objectReferenceValue = player.rectTransform;
                    serialized.FindProperty("markerTemplate").objectReferenceValue = template;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("地图预制体保存失败。");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            PositionQuestBar();
            string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = nameof(SceneMapView);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, false);
            AssetDatabase.SaveAssetIfDirty(group);
            Game.Core.Logging.Log.Info("共用小地图 HUD 已生成：左上角常驻，任务栏位于下方。");
        }

        private static void PositionQuestBar()
        {
            const string path = "Assets/_Project/Prefabs/UI/QuestHudView.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view = root.GetComponent<Game.Quest.QuestHudView>();
                if (view == null) throw new InvalidOperationException("任务 HUD 预制体缺少 QuestHudView。");
                using (var serialized = new SerializedObject(view))
                {
                    GameObject bar = serialized.FindProperty("root").objectReferenceValue as GameObject;
                    if (bar == null) throw new InvalidOperationException("任务 HUD 的任务栏未接线。");
                    var rect = (RectTransform)bar.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(24f, -332f);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new InvalidOperationException("任务 HUD 保存失败。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject Node(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var node = new GameObject(name, typeof(RectTransform));
            if (parent != null) node.transform.SetParent(parent, false);
            var rect = (RectTransform)node.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return node;
        }

        private static void Stretch(GameObject node)
        {
            var rect = (RectTransform)node.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text Text(string name, Transform parent, string content, Vector2 position, Vector2 size, TMP_FontAsset font, float fontSize)
        {
            var text = Node(name, parent, position, size).AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.text = content;
            text.color = new Color(0.92f, 0.94f, 0.91f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static Image Dot(string name, Transform parent, Color color, float size)
        {
            var image = Node(name, parent, Vector2.zero, new Vector2(size, size)).AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
    }
}
