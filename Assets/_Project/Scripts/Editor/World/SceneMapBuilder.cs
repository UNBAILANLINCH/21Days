// 职责：旧 Build 生成地图并调整任务栏；新 ApplyPaperStyle 只美化既有地图，不保存任务栏或场景。
// 为什么新建：现有 UI 生成器的建图函数都是模块私有，不能复用；扩展战斗生成器会混入世界地图职责。
// 载体：人工执行菜单；锚点：SceneMapView.prefab 与 UI 组同名地址；正式美术接管后停用本生成器。
using System;
using System.IO;
using System.Security.Cryptography;
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
        private const string PaperFrameName = "MapPaperFrame";
        private const string QuestPrefabPath = "Assets/_Project/Prefabs/UI/QuestHudView.prefab";
        private const string SerifPath = "Assets/_Project/Art/Fonts/SourceHanSerifCN/SourceHanSerifCN-Bold SDF.asset";
        private const string SansPath = "Assets/_Project/Art/Fonts/SourceHanSansCN/SourceHanSansCN-Regular SDF.asset";
        private const string DividerPath = "Assets/_Project/Art/Sprites/UI/Quest/Delivery20261019/Slices/04-divider.png";

        [MenuItem("21Days/世界/生成最小场景地图")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("退出 Play 后才能生成地图资产。");
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (HasPaperStyle(existing))
                throw new InvalidOperationException("小地图已有纸页样式；旧生成器会覆盖布局和任务栏，已拒绝。请用「仅美化现有小地图」菜单。");
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

        // 手动菜单：只保存现有 SceneMapView，不调用 Build 或 PositionQuestBar。
        // 状态锚点：Contents/MapPaperFrame；重复执行复用节点；没有常驻编辑器回调。
        [MenuItem("21Days/世界/仅美化现有小地图（不动任务栏）")]
        public static void ApplyPaperStyle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("请先退出 Play 并等待编译完成。");
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == PrefabPath)
                throw new InvalidOperationException("请先保存并退出小地图 Prefab 编辑模式，避免覆盖未保存内容。");
            if (!File.Exists(PrefabPath) || !File.Exists(QuestPrefabPath))
                throw new InvalidOperationException("缺少现有地图或任务 HUD；此菜单不会从零重建它们。");
            if (!AssetDatabase.IsOpenForEdit(PrefabPath))
                throw new InvalidOperationException("小地图预制体当前不可编辑，请先解除该资产的只读或版本控制锁定。");
            TMP_FontAsset serif = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SerifPath);
            TMP_FontAsset sans = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SansPath);
            Sprite divider = AssetDatabase.LoadAssetAtPath<Sprite>(DividerPath);
            if (serif == null || sans == null || divider == null)
                throw new InvalidOperationException("缺少项目既有宋体、黑体或任务分隔切片；未修改任何资产。");
            if (!EditorUtility.DisplayDialog("仅美化小地图",
                "只保存 SceneMapView.prefab，保留地图入口和当前外框位置／尺寸，不移动任务栏。\n" +
                "将先备份小地图到 outputs/minimap-style-backup。继续？", "应用", "取消")) return;
            byte[] questBefore = File.ReadAllBytes(QuestPrefabPath);
            string backupDirectory = Path.Combine("outputs", "minimap-style-backup",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(backupDirectory);
            File.Copy(PrefabPath, Path.Combine(backupDirectory, "SceneMapView.prefab"));
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                StyleLoadedMap(root, serif, sans, divider);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("小地图保存失败；原文件备份已保留。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (!SameBytes(questBefore, File.ReadAllBytes(QuestPrefabPath)))
                throw new InvalidOperationException("任务 HUD 在操作期间发生变化；本工具未写它。请检查其它会话，不要自动回滚别人的改动。");
            Game.Core.Logging.Log.Info("小地图纸页样式已保存；任务 HUD 字节未变。备份：" + backupDirectory +
                "。仍需从 Boot 检查实际显示、换界和沉浸显隐。");
        }

        /// <summary>只修改传入的预制体内容，供菜单和隔离 EditMode 用例复用；不保存或读取其它资产。</summary>
        public static void StyleLoadedMap(GameObject root, TMP_FontAsset serif, TMP_FontAsset sans, Sprite divider)
        {
            if (root == null || serif == null || sans == null || divider == null)
                throw new ArgumentException("小地图根、两种字体与分隔切片不能为空。");
            SceneMapView view = root.GetComponent<SceneMapView>();
            if (view == null) throw new InvalidOperationException("根对象没有 SceneMapView；未尝试重建。");
            using (var serialized = new SerializedObject(view))
            {
                var map = serialized.FindProperty("map").objectReferenceValue as SceneMapGraphic;
                var title = serialized.FindProperty("title").objectReferenceValue as TMP_Text;
                var panel = serialized.FindProperty("contentRoot").objectReferenceValue as GameObject;
                var player = serialized.FindProperty("playerMarker").objectReferenceValue as RectTransform;
                var template = serialized.FindProperty("markerTemplate").objectReferenceValue as Image;
                if (map == null || title == null || panel == null || player == null || template == null)
                    throw new InvalidOperationException("现有小地图引用不完整；未尝试猜测或重建接线。");
                Transform contents = panel.transform.Find("Contents");
                TMP_Text legend = contents == null ? null : FindLabel(contents, "Legend");
                TMP_Text north = FindLabel(map.transform, "North");
                if (north == null && contents != null) north = FindLabel(contents, "North");
                Image paper = panel.GetComponent<Image>();
                Image playerImage = player.GetComponent<Image>();
                if (contents == null || legend == null || north == null || paper == null || playerImage == null ||
                    !panel.transform.IsChildOf(root.transform) || !map.transform.IsChildOf(contents) ||
                    !title.transform.IsChildOf(contents) || !player.IsChildOf(map.transform) ||
                    !template.transform.IsChildOf(map.transform))
                    throw new InvalidOperationException("小地图层级与已核对版本不同；请保留现状并人工合并。");
                if (((RectTransform)panel.transform).rect.width < 264f ||
                    ((RectTransform)panel.transform).rect.height < 294f)
                    throw new InvalidOperationException("现有小地图外框小于 264×294；不扩张外框挤占任务栏，请先核对布局。");
                // 标题／图例读取现有业务文字；外框锚点、位置、尺寸、父级全部保持。
                paper.sprite = null;
                paper.color = new Color(0.94f, 0.96f, 0.94f, 0.96f);
                paper.raycastTarget = false;
                title.font = serif;
                title.fontSize = 18f;
                title.fontSizeMin = 14f;
                title.fontSizeMax = 18f;
                title.enableAutoSizing = true;
                title.enableWordWrapping = false;
                title.overflowMode = TextOverflowModes.Ellipsis;
                title.alignment = TextAlignmentOptions.MidlineLeft;
                title.color = new Color32(33, 106, 121, 255);
                Place(title.rectTransform, new Vector2(-19f, 122f), new Vector2(190f, 28f));
                Place(map.rectTransform, new Vector2(0f, -2f), new Vector2(212f, 212f));
                north.transform.SetParent(contents, false);
                Place(north.rectTransform, new Vector2(96f, 122f), new Vector2(36f, 22f));
                north.font = sans;
                north.fontSize = 14f;
                north.enableAutoSizing = false;
                north.enableWordWrapping = false;
                north.overflowMode = TextOverflowModes.Overflow;
                north.color = title.color;
                legend.font = sans;
                legend.fontSize = 14f;
                legend.enableAutoSizing = false;
                legend.color = new Color32(43, 75, 79, 255);
                legend.text = "<color=#13788B>● 位置</color>  <color=#A25D16>● 入口</color>  <color=#785E47>● 楼梯</color>";
                Place(legend.rectTransform, new Vector2(0f, -134f), new Vector2(244f, 22f));
                serialized.FindProperty("groundColor").colorValue = new Color32(173, 204, 201, 255);
                serialized.FindProperty("buildingColor").colorValue = new Color32(91, 139, 143, 255);
                serialized.FindProperty("portalColor").colorValue = new Color32(162, 93, 22, 255);
                serialized.FindProperty("stairColor").colorValue = new Color32(120, 94, 71, 255);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                playerImage.color = new Color32(19, 120, 139, 255);
                OutlineMarker(playerImage);
                OutlineMarker(template);
                player.sizeDelta = new Vector2(10f, 10f);
                template.rectTransform.sizeDelta = new Vector2(7f, 7f);
                Image frame = Decoration(contents, PaperFrameName, new Vector2(0f, -2f), new Vector2(216f, 216f));
                frame.color = new Color32(135, 180, 187, 255);
                frame.transform.SetAsFirstSibling();
                Image field = Decoration(frame.transform, "Field", Vector2.zero, new Vector2(213f, 213f));
                field.color = new Color32(223, 235, 231, 255);
                Image line = Decoration(contents, "MapPaperDivider", new Vector2(0f, -117f), new Vector2(228f, 13.23f));
                line.sprite = divider;
                line.preserveAspect = true;
                line.color = Color.white;
                foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            }
        }

        private static TMP_Text FindLabel(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            return child == null ? null : child.GetComponent<TMP_Text>();
        }

        private static bool HasPaperStyle(GameObject root)
        {
            if (root == null) return false;
            SceneMapView view = root.GetComponent<SceneMapView>();
            if (view == null) return false;
            using (var serialized = new SerializedObject(view))
            {
                GameObject panel = serialized.FindProperty("contentRoot").objectReferenceValue as GameObject;
                return panel != null && panel.transform.Find("Contents/" + PaperFrameName) != null;
            }
        }

        private static void OutlineMarker(Image image)
        {
            Outline outline = image.GetComponent<Outline>();
            if (outline == null) outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            outline.useGraphicAlpha = true;
        }

        private static Image Decoration(Transform parent, string name, Vector2 position, Vector2 size)
        {
            Transform found = parent.Find(name);
            GameObject node = found == null ? Node(name, parent, position, size) : found.gameObject;
            Image image = node.GetComponent<Image>();
            if (image == null) image = node.AddComponent<Image>();
            Place(image.rectTransform, position, size);
            image.raycastTarget = false;
            return image;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(left)) == Convert.ToBase64String(hash.ComputeHash(right));
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
