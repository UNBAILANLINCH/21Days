// 职责：经编辑器API预摆和维护既有3D灰盒五店；只修改本玩法资产，不创建新场景。
// 载体：人工/MCP调用本菜单；锚点：SampleScene/FiveShops与Boot注册器；重复执行只核对，退出不后台运行。
using System;
using System.Collections.Generic;
using Game.CharacterPuppet;
using Game.Clothing;
using Game.Core.Boot;
using Game.Forging;
using Game.Identity;
using Game.Monster;
using Game.Narrative;
using Game.Rhythm;
using Game.ShopPlaytest;
using Game.Taming;
using Game.Tavern;
using Game.Theater;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace Game.Editor.ShopPlaytest
{
    public static class ShopPlaytestBuilder
    {
        private const string MainPath = "Assets/_Project/Scenes/SampleScene.unity";
        private const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private const string Prefabs = "Assets/_Project/Prefabs/ShopPlaytest";
        private const string Data = "Assets/_Project/Data/ShopPlaytest";
        private static readonly string[] Names = { "戏班", "衣肆", "金银行", "客栈", "酒肆" };
        private static readonly string[][] Roles = {
            new[] { "apprentice", "director", "musician" }, new[] { "customer0", "customer1", "customer2" },
            new[] { "officer", "smith" }, new string[0], new[] { "guest" }
        };
        private static readonly string[][] People = {
            new[] { "学徒", "都知", "乐正" }, new[] { "顾客甲", "顾客乙", "顾客丙" },
            new[] { "官证", "金银匠" }, new string[0], new[] { "酒客" }
        };
        private static TMP_FontAsset font;
        private static Material buildingMaterial;
        [MenuItem("21Days/五店/创建金银行打造界面")]
        public static void BuildForgingView()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出播放");
            const string path = "Assets/_Project/Prefabs/UI/ForgingView.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) throw new InvalidOperationException("打造界面已存在，请直接编辑预制体，禁止覆盖美术调整");
            font = Require<TMP_FontAsset>("Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings == null ? null : settings.FindGroup("UI");
            if (group == null) throw new InvalidOperationException("缺少UI资源组");
            var root = new GameObject("ForgingView", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                var rect = root.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
                ForgingView view = root.AddComponent<ForgingView>();
                var shade = ForgingBox("Backdrop", root.transform, Vector2.zero, Vector2.zero, new Color(.025f, .04f, .055f, .94f));
                shade.rectTransform.anchorMin = Vector2.zero; shade.rectTransform.anchorMax = Vector2.one;
                var panel = ForgingBox("WorkbenchPanel", root.transform, Vector2.zero, new Vector2(1460, 840), new Color(.13f, .18f, .21f));
                ForgingLabel("Title", panel.transform, new Vector2(0, 350), new Vector2(1000, 70), "金银行 · 打造台", 42);
                Ref(view, "quotaLabel", ForgingLabel("Quota", panel.transform, new Vector2(-560, 245), new Vector2(260, 100), "金属份额\n20/24", 34));
                Ref(view, "materialLabel", ForgingLabel("Materials", panel.transform, new Vector2(-180, 270), new Vector2(660, 55), "库存 / 累计消耗 / 预留", 25));
                ForgingBox("Divider", panel.transform, new Vector2(360, -15), new Vector2(2, 640), new Color(.5f, .58f, .61f));
                var disk = ForgingBox("FourPointDisk", panel.transform, new Vector2(-150, 30), new Vector2(460, 460), new Color(.22f, .3f, .34f));
                Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                disk.sprite = circle;
                var points = new List<Object>();
                Vector2[] positions = { new Vector2(0, 155), new Vector2(-155, -15), new Vector2(155, 35), new Vector2(65, -150) };
                for (int i = 0; i < 4; i++)
                {
                    Button point = ForgingButton("Point" + i, disk.transform, positions[i], new Vector2(78, 78), (i + 1).ToString());
                    point.image.sprite = circle; points.Add(point);
                }
                var products = new List<Object>(); var labels = new List<Object>();
                for (int i = 0; i < 3; i++)
                {
                    Button product = ForgingButton("Product" + i, panel.transform, new Vector2(535, 190 - i * 185), new Vector2(280, 140),
                        "物品" + (char)('A' + i) + "\n0/" + (i + 1));
                    products.Add(product); labels.Add(product.GetComponentInChildren<TMP_Text>());
                }
                var data = new SerializedObject(view);
                Array(data, "pointButtons", points); Array(data, "productButtons", products); Array(data, "productLabels", labels);
                data.ApplyModifiedPropertiesWithoutUndo();
                Ref(view, "progressLabel", ForgingLabel("Progress", panel.transform, new Vector2(-150, -230), new Vector2(700, 55), "选择工件后开始打造", 28));
                Button craft = ForgingButton("CraftButton", panel.transform, new Vector2(-530, -320), new Vector2(200, 65), "打造");
                Ref(view, "craftButton", craft); Ref(view, "defaultSelected", craft);
                Ref(view, "costLabel", ForgingLabel("Cost", panel.transform, new Vector2(-530, -375), new Vector2(200, 35), "5 金属", 24));
                Ref(view, "cancelButton", ForgingButton("CancelWorkpieceButton", panel.transform, new Vector2(-280, -320), new Vector2(220, 65), "取消工件"));
                Ref(view, "submitButton", ForgingButton("SubmitButton", panel.transform, new Vector2(-20, -320), new Vector2(200, 65), "提交"));
                Ref(view, "backButton", ForgingButton("BackButton", panel.transform, new Vector2(535, -355), new Vector2(280, 65), "返回街面"));
                Ref(view, "messageLabel", ForgingLabel("Message", root.transform, new Vector2(0, -470), new Vector2(1420, 60), "选择右侧工件，按高亮顺序点击圆盘点位。", 27));
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group);
                entry.address = nameof(ForgingView); Save(group); Save(settings);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Image ForgingBox(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false); image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = position; image.color = color; return image;
        }
        private static TMP_Text ForgingLabel(string name, Transform parent, Vector2 position, Vector2 size, string text, float fontSize)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(parent, false); label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size; label.font = font; label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center; label.color = new Color(.94f, .9f, .8f);
            label.text = text; label.raycastTarget = false; return label;
        }
        private static Button ForgingButton(string name, Transform parent, Vector2 position, Vector2 size, string text)
        {
            Image image = ForgingBox(name, parent, position, size, new Color(.4f, .49f, .53f));
            Button button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            ForgingLabel("Label", image.transform, Vector2.zero, size - new Vector2(12, 8), text, 30);
            return button;
        }
        [MenuItem("21Days/五店/接入现有3D主场景")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出播放");
            Scene previous = SceneManager.GetActiveScene();
            Scene main = SceneManager.GetSceneByPath(MainPath); bool openedMain = !main.isLoaded;
            Scene boot = SceneManager.GetSceneByPath(BootPath); bool openedBoot = !boot.isLoaded;
            if (openedMain) main = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Additive);
            if (openedBoot) boot = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(main);
                EncounterSceneView view = Find<EncounterSceneView>(main);
                if (view == null || view.PlayerIdentity == null) throw new InvalidOperationException("现有主地图缺切人接线");
                if (Find<ShopPlaytestWorld>(main) != null) throw new InvalidOperationException("五店已接入，禁止重复生成覆盖布局");
                font = Require<TMP_FontAsset>("Assets/_Project/Art/Fonts/Font_NotoSansSC_Regular SDF.asset");
                GameObject graybox = null;
                foreach (GameObject root in main.GetRootGameObjects()) if (root.name == "Environment_Graybox") graybox = root;
                if (graybox == null) throw new InvalidOperationException("目标不是既有3D灰盒地图");
                BoxCollider street = null; float largest = 0;
                foreach (BoxCollider box in graybox.GetComponentsInChildren<BoxCollider>())
                {
                    float area = box.bounds.size.x * box.bounds.size.z;
                    if (box.bounds.size.y < 1 && area > largest) { largest = area; street = box; }
                }
                if (street == null) throw new InvalidOperationException("灰盒街面缺少地面碰撞");
                buildingMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                buildingMaterial.SetColor("_BaseColor", new Color(.48f, .36f, .23f));
                string materialPath = Data + "/ShopBuilding.mat";
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (existing != null) { Object.DestroyImmediate(buildingMaterial); buildingMaterial = existing; }
                else AssetDatabase.CreateAsset(buildingMaterial, materialPath);
                Bounds bounds = street.bounds;
                Transform leftWall = graybox.transform.Find("Wall_Left");
                Transform backWall = graybox.transform.Find("Wall_Back");
                if (leftWall == null || backWall == null) throw new InvalidOperationException("现有街面墙体引用缺失");
                float firstX = Mathf.Max(bounds.min.x + 3, leftWall.GetComponent<BoxCollider>().bounds.max.x + 1.8f);
                float lastX = bounds.max.x - 3;
                float spacing = (lastX - firstX) / 4;
                if (spacing < 4.5f) throw new InvalidOperationException("当前地面不足五店街面布局，需调整作者点位");
                float rowZ = backWall.position.z - 2.6f;
                float groundY = bounds.max.y;
                var rootShops = new GameObject("FiveShops");
                SceneManager.MoveGameObjectToScene(rootShops, main);
                ShopPlaytestWorld world = rootShops.AddComponent<ShopPlaytestWorld>();
                var areas = new List<Object>(); var stations = new List<Object>();
                var actors = new List<Object>(); var targets = new List<Object>(view.PatrolActors);
                Transform[] original = null, redirected = null;
                for (int shop = 0; shop < 5; shop++)
                {
                    var template = new GameObject(Names[shop]);
                    GameObject building = Cube("BuildingVisual_Cube", template.transform, new Vector3(0, 1.8f, 4),
                        new Vector3(spacing - 1.4f, 3.6f, 3), buildingMaterial);
                    building.layer = LayerMask.NameToLayer("Obstacle");
                    Label(Names[shop], building.transform, new Vector3(0, .65f, -.52f), .34f);
                    var area = new GameObject("ActivityArea", typeof(BoxCollider)).GetComponent<BoxCollider>();
                    area.transform.SetParent(template.transform, false); area.isTrigger = true;
                    area.center = new Vector3(0, 1, -.1f); area.size = new Vector3(spacing - .8f, 6, 6.8f);
                    var actorsRoot = new GameObject("Actors").transform; actorsRoot.SetParent(template.transform, false);
                    for (int i = 0; i < Roles[shop].Length; i++)
                        Actor(actorsRoot, "shop_" + Roles[shop][i], Roles[shop][i], People[shop][i], shop,
                            shop == 4 ? new Vector3(-.8f, 0, .6f) : new Vector3((i - (Roles[shop].Length - 1) * .5f) * 1.6f, 0, -1f), true, true, i);
                    string stationName = shop == 0 ? "戏台" : shop == 1 ? "裁衣匠 / 领衣柜台" :
                        shop == 2 ? "拨料文书 / 打造台" : shop == 3 ? "客栈掌柜 / 线索" : "酒桌 / 酒令";
                    var station = new GameObject("InteractionPoint");
                    station.transform.SetParent(template.transform, false); station.transform.localPosition = new Vector3(0, 0, -.2f);
                    if (shop != 1)
                    {
                        ShopPlaytestActor stationActor = station.AddComponent<ShopPlaytestActor>();
                        ConfigureActor(stationActor, "shop_station_" + shop, "", stationName, shop);
                    }
                    Cube("LocalProp_Cube", station.transform, new Vector3(1.25f, .175f, .4f), new Vector3(1.6f, .35f, .4f), buildingMaterial);
                    if (shop != 1) Label(stationName, station.transform, new Vector3(0, 1.5f, .4f), 3.4f);
                    if (shop == 1 || shop == 3)
                        Actor(actorsRoot, shop == 1 ? "shop_tailor" : "shop_innkeeper", shop == 1 ? "tailor" : "innkeeper", shop == 1 ? "裁衣匠" : "客栈掌柜",
                            shop, new Vector3(2.6f, 0, .8f), true, true, 4);
                    if (shop == 0)
                    {
                        Actor(actorsRoot, "shop_patrol", "patrol", "巡逻学徒", shop, new Vector3(-2, 0, 1.8f), true, true, 2);
                        Transform routes = new GameObject("AuthoredRoutes").transform; routes.SetParent(template.transform, false);
                        original = Points(routes, "原巡逻", new[] { new Vector3(-2, 0, 1.8f), new Vector3(2, 0, 1.8f) });
                        redirected = Points(routes, "调开后巡逻", new[] { new Vector3(-2, 0, -2.5f), new Vector3(2, 0, -2.5f) });
                        TamingActor patrol = FindById(template, "shop_patrol");
                        Refs(patrol, "patrolPoints", original);
                    }
                    if (shop == 4)
                    {
                        Actor(actorsRoot, "shop_boss", "boss", "酒肆BOSS", shop, new Vector3(-1.6f, 0, .8f), true, false, 3);
                        Actor(actorsRoot, "shop_customer_a", "customer_a", "普通客人甲", shop, new Vector3(-.2f, 0, .6f), true, false, 1);
                        Actor(actorsRoot, "shop_customer_b", "customer_b", "普通客人乙", shop, new Vector3(-.2f, 0, 1.5f), true, false, 4);
                        TamingActor boss = FindById(template, "shop_boss");
                        var trigger = boss.gameObject.AddComponent<NarrativeTrigger>();
                        Text(trigger, "targetId", "shop_boss"); Text(trigger, "targetKind", "ShopMainBoss");
                    }
                    string prefabPath = Prefabs + "/Main_" + shop + ".prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) throw new InvalidOperationException("五店预制体已存在，不能覆盖：" + prefabPath);
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(template, prefabPath); Object.DestroyImmediate(template);
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(saved, main);
                    instance.transform.SetParent(rootShops.transform, false);
                    instance.transform.position = new Vector3(firstX + spacing * shop, groundY, rowZ);
                    areas.Add(instance.transform.Find("ActivityArea").GetComponent<BoxCollider>());
                    stations.Add(instance.transform.Find("InteractionPoint"));
                    actors.AddRange(instance.GetComponentsInChildren<ShopPlaytestActor>());
                    targets.AddRange(instance.GetComponentsInChildren<TamingActor>());
                    if (shop == 0)
                    {
                        var paths = instance.transform.Find("AuthoredRoutes");
                        original = new[] { paths.GetChild(0), paths.GetChild(1) };
                        redirected = new[] { paths.GetChild(2), paths.GetChild(3) };
                    }
                }
                var w = new SerializedObject(world);
                Array(w, "activityAreas", areas); Array(w, "stations", stations); Array(w, "actors", actors);
                Array(w, "originalRoute", new List<Object>(original)); Array(w, "redirectedRoute", new List<Object>(redirected));
                w.ApplyModifiedPropertiesWithoutUndo();
                var v = new SerializedObject(view); Array(v, "patrolActors", targets); v.ApplyModifiedPropertiesWithoutUndo();
                ConfigureBoot(boot);
                AppendIdentities();
                ResizeLocalView();
                EditorSceneManager.SaveScene(main); EditorSceneManager.SaveScene(boot);
                Debug.Log("五店接线完成：既有SampleScene，5个3D建筑Cube，现成小人，Boot正常入口。");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedBoot) EditorSceneManager.CloseScene(boot, true);
                if (openedMain) EditorSceneManager.CloseScene(main, true);
            }
        }
        [MenuItem("21Days/五店/整理主场景显示")]
        public static void RepairPresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出播放");
            Scene previous = SceneManager.GetActiveScene();
            Scene main = SceneManager.GetSceneByPath(MainPath); bool opened = !main.isLoaded;
            if (opened) main = EditorSceneManager.OpenScene(MainPath, OpenSceneMode.Additive);
            try
            {
                SpriteRenderer sourceRing = Find<EncounterSceneView>(main).PlayerIdentity.StateIndicator;
                for (int shop = 0; shop < 5; shop++)
                {
                    string path = Prefabs + "/Main_" + shop + ".prefab";
                    GameObject content = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (TMP_Text label in content.GetComponentsInChildren<TMP_Text>(true))
                        {
                            label.fontSize = 1.3f; label.rectTransform.sizeDelta = new Vector2(4.2f, .65f);
                            if (label.transform.parent.name == "BuildingVisual_Cube")
                            {
                                Vector3 scale = label.transform.parent.localScale;
                                label.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
                                label.transform.localPosition = new Vector3(0, .45f, -.52f);
                            }
                        }
                        content.transform.Find("InteractionPoint").localPosition = new Vector3(0, 0, -.2f);
                        Transform prop = content.transform.Find("InteractionPoint/LocalProp_Cube");
                        prop.localPosition = new Vector3(1.25f, .175f, .4f);
                        prop.localScale = new Vector3(1.6f, .35f, .4f);
                        foreach (ShopPlaytestActor actor in content.GetComponentsInChildren<ShopPlaytestActor>(true))
                        {
                            if (actor.StableId == "shop_guest") actor.transform.localPosition = new Vector3(-.8f, 0, .6f);
                            if (actor.StableId == "shop_boss") actor.transform.localPosition = new Vector3(-1.6f, 0, .8f);
                            if (actor.StableId == "shop_customer_a") actor.transform.localPosition = new Vector3(-.2f, 0, .6f);
                            if (actor.StableId == "shop_customer_b") actor.transform.localPosition = new Vector3(-.2f, 0, 1.5f);
                            TamingActor tame = actor.GetComponent<TamingActor>();
                            if (tame == null) continue;
                            Bool(tame, "observationOnly", actor.Shop == 4 && actor.Role != "guest");
                            if (actor.Shop == 4)
                            {
                                var data = new SerializedObject(tame);
                                var point = (Transform)data.FindProperty("patrolPoints").GetArrayElementAtIndex(0).objectReferenceValue;
                                point.localPosition = actor.transform.localPosition;
                            }
                            if (tame.StateIndicator == null && sourceRing != null)
                            {
                                var ring = new GameObject("SelectRing", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
                                ring.transform.SetParent(actor.transform, false);
                                ring.transform.localPosition = sourceRing.transform.localPosition;
                                ring.transform.localRotation = sourceRing.transform.localRotation;
                                ring.transform.localScale = sourceRing.transform.localScale;
                                ring.sprite = sourceRing.sprite; ring.sharedMaterial = sourceRing.sharedMaterial;
                                ring.sortingLayerID = sourceRing.sortingLayerID; ring.sortingOrder = sourceRing.sortingOrder;
                                Ref(tame, "stateIndicator", ring);
                            }
                            tame.RefreshLabel();
                        }
                        PrefabUtility.SaveAsPrefabAsset(content, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(content); }
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(main, true);
            }
        }
        [MenuItem("21Days/五店/归档旧独立试玩入口")]
        public static void DisableLegacyEntries()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出播放");
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            string[] paths = { "Assets/_Project/Scenes/ShopPlaytest.unity", "Assets/_Project/Scenes/ShopPlaytestWorld.unity", "Assets/_Project/Scenes/ShopPlaytestBattle.unity" };
            foreach (string path in paths)
            {
                var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
                if (entry != null && !entry.address.StartsWith("Legacy/", StringComparison.Ordinal))
                    entry.address = "Legacy/" + entry.address;
            }
            Save(settings.FindGroup("Scenes")); Save(settings);
        }
        private static void ConfigureBoot(Scene boot)
        {
            GameBootstrap bootstrap = Find<GameBootstrap>(boot);
            var shops = bootstrap.GetComponent<ShopPlaytestInstaller>();
            if (shops == null) shops = bootstrap.gameObject.AddComponent<ShopPlaytestInstaller>();
            Ref(shops, "clothing", Require<ClothingConfig>(Data + "/Clothing.asset"));
            Ref(shops, "forging", Require<ForgingConfig>(Data + "/Forging.asset"));
            Ref(shops, "tavern", Require<TavernConfig>(Data + "/Tavern.asset"));
            Ref(shops, "theater", Require<TheaterConfig>(Data + "/Theater.asset"));
            var rhythm = bootstrap.GetComponent<RhythmInstaller>();
            if (rhythm == null) rhythm = bootstrap.gameObject.AddComponent<RhythmInstaller>();
            Ref(rhythm, "config", Require<RhythmConfig>("Assets/_Project/Data/Rhythm/ChongErFei.asset"));
            Bool(rhythm, "autoEnterDemo", false);
        }
        private static void AppendIdentities()
        {
            var config = Require<IdentityConfig>("Assets/_Project/Data/Identity/IdentityConfig.asset");
            var data = new SerializedObject(config); SerializedProperty rows = data.FindProperty("definitions");
            for (int shop = 0; shop < Roles.Length; shop++)
            foreach (ShopPlaytestActor actor in Require<GameObject>("Assets/_Project/Prefabs/ShopPlaytest/Main_" + shop + ".prefab").GetComponentsInChildren<ShopPlaytestActor>(true))
            {
                string id = actor.Role;
                if (string.IsNullOrEmpty(id)) continue;
                bool found = false;
                for (int n = 0; n < rows.arraySize; n++) if (rows.GetArrayElementAtIndex(n).FindPropertyRelative("id").stringValue == id) found = true;
                if (found) continue;
                rows.InsertArrayElementAtIndex(rows.arraySize);
                var row = rows.GetArrayElementAtIndex(rows.arraySize - 1);
                row.FindPropertyRelative("id").stringValue = id;
                row.FindPropertyRelative("displayName").stringValue = actor.DisplayName;
                row.FindPropertyRelative("sourceCharacterId").stringValue = actor.StableId;
                row.FindPropertyRelative("origin").enumValueIndex = 1;
                row.FindPropertyRelative("abilities").arraySize = 0;
                row.FindPropertyRelative("accessRights").arraySize = 0;
            }
            data.ApplyModifiedPropertiesWithoutUndo(); Save(config);
        }
        private static void ResizeLocalView()
        {
            string path = "Assets/_Project/Prefabs/UI/ShopPlaytestView.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var panel = root.transform.Find("控制面板");
                panel.GetComponent<RectTransform>().anchoredPosition = new Vector2(640, -40);
                panel.GetComponent<RectTransform>().sizeDelta = new Vector2(600, 820);
                foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect == root.transform || rect == panel) continue;
                    Vector2 p = rect.anchoredPosition;
                    if (rect.parent == root.transform) { p.x = 640 + (p.x - 550) * .7f; p.y *= .75f; rect.anchoredPosition = p; rect.sizeDelta *= .7f; }
                }
                foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true)) label.fontSize *= .8f;
                foreach (Transform child in root.transform)
                    if (child.name.StartsWith("Shop", StringComparison.Ordinal) || child.name == "重试本店" || child.name == "返回标题") child.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void Actor(Transform parent, string id, string role, string name, int shop, Vector3 position, bool controllable, bool passive, int appearance)
        {
            var root = new GameObject(id); root.transform.SetParent(parent, false); root.transform.localPosition = position;
            var actor = root.AddComponent<ShopPlaytestActor>(); ConfigureActor(actor, id, role, name, shop);
            string[] looks = { "Chibi_amiya", "Chibi_skadi", "Chibi_nian", "Chibi_chen", "Chibi_texas" };
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>("Assets/_Project/Prefabs/Characters/" + looks[appearance % looks.Length] + ".prefab"));
            model.transform.SetParent(root.transform, false);
            var proxy = root.AddComponent<SpriteRenderer>(); proxy.enabled = false;
            foreach (ChibiPuppetMotion motion in model.GetComponentsInChildren<ChibiPuppetMotion>(true))
            { Ref(motion, "trackedRoot", root.transform); Ref(motion, "facingSource", proxy); }
            TMP_Text label = Label(name, root.transform, new Vector3(0, 2.2f, 0), 3.6f);
            if (controllable)
            {
                var tame = root.AddComponent<TamingActor>(); Text(tame, "stableId", id); Text(tame, "displayName", name);
                Ref(tame, "visual", proxy); Ref(tame, "nameLabel", label); Bool(tame, "passive", passive); Bool(tame, "addedForShops", true);
                Bool(tame, "observationOnly", shop == 4 && role != "guest");
                Transform point = new GameObject("初始待机点").transform; point.SetParent(parent.parent, false); point.localPosition = position;
                Refs(tame, "patrolPoints", new[] { point });
            }
        }
        private static void ConfigureActor(ShopPlaytestActor actor, string id, string role, string name, int shop)
        { Text(actor, "stableId", id); Text(actor, "role", role); Text(actor, "displayName", name); Int(actor, "shop", shop); }
        private static TamingActor FindById(GameObject root, string id)
        { foreach (TamingActor actor in root.GetComponentsInChildren<TamingActor>()) if (actor.StableId == id) return actor; throw new InvalidOperationException(id); }
        private static Transform[] Points(Transform parent, string name, Vector3[] positions)
        {
            var result = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++) { result[i] = new GameObject(name + i).transform; result[i].SetParent(parent, false); result[i].localPosition = positions[i]; }
            return result;
        }
        private static TMP_Text Label(string text, Transform parent, Vector3 position, float size)
        {
            var label = new GameObject("TMP_" + text, typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            label.transform.SetParent(parent, false); label.transform.localPosition = position; label.transform.localRotation = Quaternion.Euler(38, 0, 0);
            label.font = font; label.fontSize = size; label.text = text; label.alignment = TextAlignmentOptions.Center; label.rectTransform.sizeDelta = new Vector2(8, 1.2f); return label;
        }
        private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material; return go; }
        private static T Find<T>(Scene scene) where T : Component
        { foreach (GameObject root in scene.GetRootGameObjects()) { T value = root.GetComponentInChildren<T>(true); if (value != null) return value; } return null; }
        private static T Require<T>(string path) where T : Object
        { T value = AssetDatabase.LoadAssetAtPath<T>(path); if (value == null) throw new InvalidOperationException("资源缺失：" + path); return value; }
        private static void Ref(Object target, string field, Object value) { var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Text(Object target, string field, string value) { var so = new SerializedObject(target); so.FindProperty(field).stringValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Bool(Object target, string field, bool value) { var so = new SerializedObject(target); so.FindProperty(field).boolValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Int(Object target, string field, int value) { var so = new SerializedObject(target); so.FindProperty(field).intValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Refs(Object target, string field, Transform[] values) { var so = new SerializedObject(target); Array(so, field, new List<Object>(values)); so.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Array(SerializedObject data, string field, List<Object> values)
        { var p = data.FindProperty(field); p.arraySize = values.Count; for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
        private static void Save(Object value) { EditorUtility.SetDirty(value); AssetDatabase.SaveAssetIfDirty(value); }
    }
}
