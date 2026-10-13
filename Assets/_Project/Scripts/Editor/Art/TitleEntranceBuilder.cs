// 职责：按美术导出坐标组装正式 TitleView 的入场拆件与可调时间轴。
// 为什么新建：现有导入器只配置贴图；预制体内部引用与几十个遮罩需要由 Unity 编辑器序列化，不能手写 YAML。
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.UI;
using Game.Core.UI.Views;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor
{
    public static class TitleEntranceBuilder
    {
        private const string ArtPath = "Assets/_Project/Art/Sprites/UI/Title/Yang/Entrance/";
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/TitleView.prefab";
        private const string ConfigPath = "Assets/_Project/Data/UI/TitleEntranceConfig.asset";

        private sealed class Part
        {
            internal RectTransform Target;
            internal string Name;
            internal float Delay, Duration, Fade, Scale = 1f, Rotation, BreathScale, BreathSeconds = 4f;
            internal Vector2 Offset, Breath;
            internal AnimationCurve Curve;
            internal int Reveal;
        }

        [MenuItem("21Days/Art/Build Title Entrance")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出播放模式");
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing == null) throw new InvalidOperationException("正式标题预制体不存在");
            if (existing.GetComponent<TitleEntranceMotion>() != null || AssetDatabase.LoadAssetAtPath<TitleEntranceConfig>(ConfigPath) != null)
                throw new InvalidOperationException("标题入场已接线，直接调整配置，不重建覆盖");
            JObject manifest = JObject.Parse(File.ReadAllText(ArtPath + "parts.json"));
            var assets = new Dictionary<string, JObject>();
            foreach (JObject asset in manifest["assets"]) assets.Add((string)asset["file"], asset);
            foreach (string file in assets.Keys)
            {
                var importer = AssetImporter.GetAtPath(ArtPath + file) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("贴图尚未导入：" + file);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var parts = new List<Part>();
                RectTransform frame = Rect("EntranceScene", root.transform, Vector2.zero, Vector2.zero);
                frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
                frame.offsetMin = frame.offsetMax = Vector2.zero;
                frame.SetSiblingIndex(1);
                RectTransform content = Rect("Artwork1920", frame, Vector2.zero, new Vector2(1920f, 1080f));
                // 原 PSB 底层是白色；水面尚未升起时不能透出编辑器相机的灰底。
                var backing = Rect("PaperBackdrop", content, Vector2.zero, content.sizeDelta).gameObject.AddComponent<Image>();
                backing.color = Color.white; backing.raycastTarget = false;
                Add(parts, assets, content, "01_场景/天空_合成底色.png", 0f, 0.25f, Vector2.zero);
                Part lotus = Add(parts, assets, content, "01_场景/莲花纹.png", 1.25f, 2.05f, Vector2.zero, 0.28f);
                Vector2 lotusPivot = new Vector2(0.5f, 0.29f);
                lotus.Target.anchoredPosition += Vector2.Scale(lotusPivot - lotus.Target.pivot, lotus.Target.sizeDelta);
                lotus.Target.pivot = lotusPivot;
                lotus.BreathScale = 0.012f; lotus.BreathSeconds = 4.8f;
                Part leftCloud = Add(parts, assets, content, "01_场景/云_左.png", 1.55f, 1.8f, new Vector2(262.5f, 116f), 0.42f);
                leftCloud.Breath = new Vector2(8f, 3f); leftCloud.BreathSeconds = 4.5f;
                Part rightCloud = Add(parts, assets, content, "01_场景/云_右.png", 1.72f, 1.9f, new Vector2(-301.5f, 142.5f), 0.42f);
                rightCloud.Breath = new Vector2(-7f, 4f); rightCloud.BreathSeconds = 5.2f;
                Add(parts, assets, content, "01_场景/头顶圆光.png", 0.6f, 2.1f, new Vector2(0f, 35f), 0.9f);
                string[] mountains = { "远山_右内", "远山_右外", "远山_左内", "远山_左外" };
                for (int i = 0; i < mountains.Length; i++)
                    Add(parts, assets, content, "01_场景/" + mountains[i] + ".png", 0.85f + i * 0.12f, 2.2f,
                        new Vector2(i < 2 ? 100f : -100f, -24f), 0.93f);
                Ribbon(parts, assets, content, "丝带_左", 2.5f, 1);
                Ribbon(parts, assets, content, "丝带_右", 2.64f, -1);
                Add(parts, assets, content, "02_石头/石头_左远景.png", 1.5f, 1.8f, new Vector2(649.5f, 101f), 0.32f);
                Add(parts, assets, content, "04_水面与倒影/雾气_左.png", 1.2f, 2.3f, new Vector2(206.5f, 142.5f), 0.52f);
                Add(parts, assets, content, "04_水面与倒影/雾气_右.png", 1.4f, 2.4f, new Vector2(-727.5f, 194f), 0.52f);
                Part water = Add(parts, assets, content, "04_水面与倒影/水面.png", 0.08f, 2.5f, new Vector2(0f, -165f));
                water.Fade = 1.1f; water.Breath = new Vector2(0f, -2.5f); water.BreathSeconds = 5.8f;
                // 水纹的 SoftLight 已在原画布上烘焙进水面，普通 UI 透明叠加会把它变成亮白条。
                string[] reflections = { "铜镜倒影_水影", "铜镜倒影_蓝", "铜镜倒影_深" };
                for (int i = 0; i < reflections.Length; i++)
                    Add(parts, assets, content, "04_水面与倒影/" + reflections[i] + ".png", 0.85f + i * 0.08f, 2.1f, new Vector2(0f, -110f));
                Add(parts, assets, content, "02_石头/石头_左中景.png", 1.85f, 1.7f, new Vector2(144f, 169f), 0.38f);
                Part mirror = Add(parts, assets, content, "03_镜子与标题/铜镜_本体.png", 0.32f, 2.4f, new Vector2(0f, 420f), 0.88f);
                mirror.Rotation = -32f; mirror.Curve = SettleCurve(); mirror.Breath = new Vector2(0f, 2f); mirror.BreathSeconds = 5.4f;
                Part rightStone = Add(parts, assets, content, "02_石头/石头_右中景.png", 2.03f, 1.75f, new Vector2(-147f, 206f), 0.38f);
                rightStone.Target.SetSiblingIndex(mirror.Target.GetSiblingIndex());
                Add(parts, assets, content, "03_镜子与标题/鸟.png", 2.05f, 1.45f, new Vector2(55f, 35f), 0.85f);
                Part rightForeground = Add(parts, assets, content, "02_石头/石头_右前景.png", 1.72f, 1.9f, new Vector2(-829f, 157.5f), 0.32f);
                rightForeground.Target.SetSiblingIndex(mirror.Target.GetSiblingIndex());
                Part title = Add(parts, assets, content, "03_镜子与标题/标题_镜非台.png", 0.98f, 1.95f, new Vector2(0f, 255f), 0.97f);
                title.Rotation = 3f; title.Curve = SettleCurve();

                string[] names = { "设置", "选择存档", "继续", "退出游戏" };
                string[] buttons = { "SettingsButton", "LoadButton", "ContinueButton", "QuitButton" };
                var buttonRoots = new List<GameObject>();
                var images = new List<Image>();
                for (int i = 0; i < names.Length; i++)
                {
                    var button = (RectTransform)root.transform.Find("Buttons/" + buttons[i]);
                    Vector2 center = ButtonCenter(button);
                    var art = Rect("EntranceArtwork", button, Vector2.zero, button.sizeDelta);
                    buttonRoots.Add(art.gameObject); images.Add(button.GetComponent<Image>());
                    string prefix = "05_卷轴按钮/" + names[i] + "/未选中/";
                    float beat = 3.65f + i * 0.14f;
                    Part scroll = Add(parts, assets, art, prefix + "卷轴底.png", beat, 0.84f, new Vector2(0f, 480f), 1f, center);
                    scroll.Curve = DropCurve(); scroll.Fade = 0.16f;
                    Part cord = Add(parts, assets, art, prefix + "吊线与菱形.png", beat, 0.84f, new Vector2(0f, 480f), 1f, center);
                    cord.Curve = DropCurve(); cord.Fade = 0.16f;
                    Part gradient = Add(parts, assets, art, prefix + "底部渐变.png", beat, 0.84f, new Vector2(0f, 480f), 1f, center);
                    gradient.Curve = DropCurve(); gradient.Fade = 0.16f;
                    Add(parts, assets, art, prefix + "花纹.png", beat + 0.68f, 0.55f, new Vector2(0f, 18f), 0.88f, center);
                    Add(parts, assets, art, prefix + "文字.png", beat + 0.8f, 0.6f, new Vector2(0f, 12f), 0.97f, center);
                }
                var start = (RectTransform)root.transform.Find("Buttons/StartButton");
                Vector2 startCenter = ButtonCenter(start);
                var startArt = Rect("EntranceArtwork", start, Vector2.zero, start.sizeDelta);
                Part startPart = Track(startArt, "开始游戏按钮", 5.05f, 1.25f, new Vector2(0f, -18f), 0.96f);
                startPart.Fade = 1.15f; startPart.BreathScale = 0.012f; startPart.BreathSeconds = 2.8f; parts.Add(startPart);
                string[] startFiles = { "按键底_含山纹", "花纹_左长线", "花纹_左菱形", "花纹_右长线", "花纹_右菱形", "文字_开始游戏" };
                for (int i = 0; i < startFiles.Length; i++)
                    ImageAt(assets, startArt, "06_开始游戏按钮/" + startFiles[i] + ".png", startCenter);
                var startImage = startArt.gameObject.AddComponent<Image>();
                var originalStartImage = start.GetComponent<Image>();
                startImage.sprite = originalStartImage.sprite;
                startImage.preserveAspect = true;
                originalStartImage.enabled = false;
                start.GetComponent<Button>().targetGraphic = startImage;
                var config = ScriptableObject.CreateInstance<TitleEntranceConfig>();
                var configData = new SerializedObject(config);
                SerializedProperty tracks = configData.FindProperty("tracks"); tracks.arraySize = parts.Count;
                for (int i = 0; i < parts.Count; i++) Configure(tracks.GetArrayElementAtIndex(i), parts[i]);
                configData.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(config, ConfigPath);
                var motion = root.AddComponent<TitleEntranceMotion>();
                var data = new SerializedObject(motion);
                data.FindProperty("config").objectReferenceValue = config;
                SetObjects(data.FindProperty("targets"), parts.ConvertAll<UnityEngine.Object>(part => part.Target));
                SetObjects(data.FindProperty("buttonArtwork"), buttonRoots.ConvertAll<UnityEngine.Object>(item => item));
                SetObjects(data.FindProperty("buttonImages"), images.ConvertAll<UnityEngine.Object>(item => item));
                data.FindProperty("sceneArtwork").objectReferenceValue = frame.gameObject;
                data.FindProperty("sceneFrame").objectReferenceValue = frame;
                data.FindProperty("sceneContent").objectReferenceValue = content;
                data.FindProperty("referenceSize").vector2Value = new Vector2(1920f, 1080f);
                data.FindProperty("background").objectReferenceValue = root.transform.Find("Background").GetComponent<Image>();
                data.FindProperty("startArtwork").objectReferenceValue = startArt.gameObject;
                data.FindProperty("startImage").objectReferenceValue = startImage;
                data.ApplyModifiedPropertiesWithoutUndo();
                var viewData = new SerializedObject(root.GetComponent<TitleView>());
                viewData.FindProperty("entranceMotion").objectReferenceValue = motion; viewData.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("标题入场已接线：" + parts.Count + " 个轨道，末拍 6.3 秒；原按钮路由与主题素材保留");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.layer = parent.gameObject.layer; var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }

        private static Vector2 ButtonCenter(RectTransform rect) => rect.anchoredPosition
            + new Vector2((rect.anchorMin.x - 0.5f) * 1920f, (rect.anchorMin.y - 0.5f) * 1080f)
            + Vector2.Scale(Vector2.one * 0.5f - rect.pivot, rect.sizeDelta);

        private static RectTransform ImageAt(Dictionary<string, JObject> assets, RectTransform parent, string file, Vector2 origin)
        {
            JObject asset = assets[file];
            Vector2 center = new Vector2((float)asset["centerX"] - 960f, 540f - (float)asset["centerY"]) - origin;
            Vector2 size = new Vector2((float)asset["width"], (float)asset["height"]);
            RectTransform rect = Rect(Path.GetFileNameWithoutExtension(file), parent, center, size);
            var image = rect.gameObject.AddComponent<Image>(); image.raycastTarget = false;
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + file);
            if (image.sprite == null) throw new InvalidOperationException("缺少 Sprite：" + file);
            return rect;
        }

        private static Part Add(List<Part> parts, Dictionary<string, JObject> assets, RectTransform parent, string file,
            float delay, float duration, Vector2 offset, float scale = 1f, Vector2 origin = default)
        {
            Part part = Track(ImageAt(assets, parent, file, origin), Path.GetFileNameWithoutExtension(file), delay, duration, offset, scale);
            parts.Add(part); return part;
        }

        private static Part Track(RectTransform target, string name, float delay, float duration, Vector2 offset, float scale) => new Part
        {
            Target = target, Name = name, Delay = delay, Duration = duration, Fade = Mathf.Min(0.65f, duration),
            Offset = offset, Scale = scale, Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f)
        };

        private static void Ribbon(List<Part> parts, Dictionary<string, JObject> assets, RectTransform parent, string name, float delay, int direction)
        {
            RectTransform image = ImageAt(assets, parent, "01_场景/" + name + ".png", Vector2.zero);
            RectTransform mask = Rect(name + "_揭露遮罩", parent, image.anchoredPosition, image.sizeDelta);
            image.SetParent(mask, false); image.anchoredPosition = Vector2.zero;
            mask.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(18, 0);
            Part part = Track(mask, name, delay, 1.45f, Vector2.zero, 1f); part.Reveal = direction; parts.Add(part);
        }

        private static AnimationCurve SettleCurve() => new AnimationCurve(new Keyframe(0f, 0f, 0f, 0.15f),
            new Keyframe(0.62f, 0.91f, 1f, 1f), new Keyframe(0.86f, 1.012f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        private static AnimationCurve DropCurve() => new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.42f, 0.24f, 1.6f, 1.6f), new Keyframe(0.66f, 1.035f, 0f, 0f),
            new Keyframe(0.84f, 0.992f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        private static void Configure(SerializedProperty p, Part value)
        {
            p.FindPropertyRelative("part").stringValue = value.Name;
            p.FindPropertyRelative("delay").floatValue = value.Delay; p.FindPropertyRelative("duration").floatValue = value.Duration;
            p.FindPropertyRelative("fadeSeconds").floatValue = value.Fade; p.FindPropertyRelative("offset").vector2Value = value.Offset;
            p.FindPropertyRelative("scale").floatValue = value.Scale; p.FindPropertyRelative("rotation").floatValue = value.Rotation;
            p.FindPropertyRelative("curve").animationCurveValue = value.Curve; p.FindPropertyRelative("revealDirection").intValue = value.Reveal;
            p.FindPropertyRelative("breathOffset").vector2Value = value.Breath; p.FindPropertyRelative("breathScale").floatValue = value.BreathScale;
            p.FindPropertyRelative("breathSeconds").floatValue = value.BreathSeconds;
        }

        private static void SetObjects(SerializedProperty p, List<UnityEngine.Object> values)
        {
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
