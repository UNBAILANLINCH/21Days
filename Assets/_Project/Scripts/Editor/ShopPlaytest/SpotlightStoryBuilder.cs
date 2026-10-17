// 职责：人工菜单把正式节点、区域教程与教学战斗接入现有舞台，不生成第二个移动驱动。
// 旧五店生成器会覆盖店铺布局，不能复用；这里只追加章节对象并沿用 UI 接线助手。
// 载体：人工菜单；锚点：SpotlightStory 根与两个新视图地址；正式摆位后不重复重建。
using System;
using System.Linq;
using Game.Battle;
using Game.Monster;
using Game.Narrative;
using Game.Quest;
using Game.ShopPlaytest;
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
    public static class SpotlightStoryBuilder
    {
        private const string ConfigPath = "Assets/_Project/Data/Narrative/SpotlightStoryConfig.asset";
        private static readonly string[] Stages = { "enter_r0026", "action_r0070", "enter_r0083", "enter_r0129", "enter_r0171", "enter_r0188", "enter_r0198", "enter_r0215", "enter_r0293" };
        private static readonly string[] Names = { "李诏阳", "李诏阳 · 切磋", "西城委托", "朝廷书库", "轮回堂值守", "坊市入口", "酒肆楼梯口", "殷红", "轮回堂方向" };
        private static readonly string[] Locations = { "spotlight_courtyard", "spotlight_duel", "spotlight_west_city", "spotlight_library", "spotlight_mirror", "spotlight_market", "spotlight_tavern_stairs", "spotlight_alley", "spotlight_next" };
        [MenuItem("21Days/剧情/接入序章至酒肆会面")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("退出 Play 后接线");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/SampleScene.unity");
            if (!scene.isLoaded) throw new InvalidOperationException("先打开现有 SampleScene，不覆盖其它场景");
            if (scene.GetRootGameObjects().Any(root => root.GetComponent<SpotlightSceneBindings>() != null)) throw new InvalidOperationException("已接线，后续请直接调整点位与配置");
            var world = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ShopPlaytestWorld>(true)).Single();
            var view = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EncounterSceneView>(true)).Single();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/ForgingView.prefab");
            var font = source.GetComponentInChildren<TMP_Text>(true).font;
            Vector3 spawn = view.ToScenePosition(view.PlayerStart);
            var positions = new[] {
                Ground(spawn + new Vector3(2.8f, 0, 1.6f)), Ground(spawn + new Vector3(2.8f, 0, 1.6f)),
                Ground(spawn + new Vector3(6f, 0, 0)), Ground(spawn + new Vector3(7f, 0, 2.5f)),
                Ground(new Vector3(-7f, 5f, -4.5f)), Ground(new Vector3(-1f, 5f, -5f)),
                Ground(world.Station(4).position + new Vector3(2f, 0, -3f)), Ground(world.Station(4).position + new Vector3(-3.5f, 0, -3f)),
                Ground(new Vector3(0f, 8f, 10f)) };
            var config = BuildConfig(); BuildGuide(font); AddMentorBattle();
            var rootObject = new GameObject("SpotlightStory"); SceneManager.MoveGameObjectToScene(rootObject, scene);
            Undo.RegisterCreatedObjectUndo(rootObject, "接入正式剧情");
            var bindings = rootObject.AddComponent<SpotlightSceneBindings>();
            var main = rootObject.AddComponent<NarrativeTrigger>(); Target(main, "spotlight_main", "SpotlightMain");
            var anchors = new Transform[Stages.Length]; NarrativeTrigger mentor = null;
            for (int i = 0; i < Stages.Length; i++)
            {
                if (i == 1) { anchors[i] = anchors[0]; continue; }
                var anchor = new GameObject("StoryPoint_" + Stages[i]); anchor.transform.SetParent(rootObject.transform, false); anchor.transform.position = positions[i]; anchors[i] = anchor.transform;
                var location = anchor.AddComponent<QuestLocation>(); Text(location, "locationKey", Locations[i]);
                Label(Names[i], anchor.transform, font);
                if (i == 0 || i == 2 || i == 4 || i == 7) AddCharacter(anchor.transform, i == 7 ? "Chibi_nian" : i == 4 ? "Chibi_chen" : "Chibi_skadi");
                if (i == 0) { mentor = anchor.AddComponent<NarrativeTrigger>(); Target(mentor, "spotlight_mentor", "SpotlightMentor"); }
            }
            var duelLocation = new GameObject("DuelLocation"); duelLocation.transform.SetParent(rootObject.transform, false); duelLocation.transform.position = positions[1];
            Text(duelLocation.AddComponent<QuestLocation>(), "locationKey", Locations[1]);
            var boss = world.Actors.Single(actor => actor.StableId == "shop_boss");
            var bossTrigger = boss.GetComponent<NarrativeTrigger>(); if (bossTrigger == null) bossTrigger = Undo.AddComponent<NarrativeTrigger>(boss.gameObject);
            Target(bossTrigger, "shop_boss", "SpotlightShopBoss");
            using (var serialized = new SerializedObject(bindings))
            {
                serialized.FindProperty("config").objectReferenceValue = config; serialized.FindProperty("mainTarget").objectReferenceValue = main;
                serialized.FindProperty("mentorTarget").objectReferenceValue = mentor;
                var points = serialized.FindProperty("points"); points.arraySize = Stages.Length;
                for (int i = 0; i < Stages.Length; i++) { var point = points.GetArrayElementAtIndex(i); point.FindPropertyRelative("stageId").stringValue = Stages[i]; point.FindPropertyRelative("anchor").objectReferenceValue = anchors[i]; }
                var examples = scene.GetRootGameObjects().Where(item => item.name.StartsWith("Npc_", StringComparison.Ordinal)).ToArray();
                var legacy = serialized.FindProperty("legacyExamples"); legacy.arraySize = examples.Length;
                for (int i = 0; i < examples.Length; i++) legacy.GetArrayElementAtIndex(i).objectReferenceValue = examples[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var boot = SceneManager.GetSceneByPath("Assets/_Project/Scenes/Boot.unity"); bool alreadyOpen = boot.isLoaded;
            if (!alreadyOpen) boot = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Boot.unity", OpenSceneMode.Additive);
            try
            {
                var installer = boot.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<NarrativeInstaller>(true)).Single();
                using (var serialized = new SerializedObject(installer)) { serialized.FindProperty("spotlight").objectReferenceValue = config; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                EditorSceneManager.MarkSceneDirty(boot); EditorSceneManager.SaveScene(boot);
            }
            finally { if (!alreadyOpen) EditorSceneManager.CloseScene(boot, true); }
            SceneManager.SetActiveScene(scene);
        }
        private static SpotlightStoryConfig BuildConfig()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Narrative")) AssetDatabase.CreateFolder("Assets/_Project/Data", "Narrative");
            var config = AssetDatabase.LoadAssetAtPath<SpotlightStoryConfig>(ConfigPath);
            if (config == null) { config = ScriptableObject.CreateInstance<SpotlightStoryConfig>(); AssetDatabase.CreateAsset(config, ConfigPath); }
            string[] hints = { "移动靠近李诏阳，按 {Interact} 交谈。", "按 {Interact} 开始切磋；战斗中选择招式，留意双方血量。", "靠近接案点，按 {Interact} 听取委托。", "走近书库文书，按 {Interact} 查证旧案。", "靠近入镜与问询点，按 {Interact} 向轮回堂值守问询。", "前往坊市入口，按 {Interact} 查看酒肆窗边。", "主人已败，前往酒肆楼梯口，按 {Interact} 寻人。", "到巷口靠近殷红，按 {Interact} 交谈。", "本段已完成。殷红将带你前往轮回堂。" };
            using (var serialized = new SerializedObject(config))
            {
                var nodes = serialized.FindProperty("nodes"); nodes.arraySize = Stages.Length;
                for (int i = 0; i < Stages.Length; i++) { var node = nodes.GetArrayElementAtIndex(i); node.FindPropertyRelative("stageId").stringValue = Stages[i]; node.FindPropertyRelative("displayName").stringValue = Names[i]; node.FindPropertyRelative("verb").stringValue = i == 1 ? "切磋" : i == 3 ? "查阅" : "交谈"; node.FindPropertyRelative("hint").stringValue = hints[i]; }
                string[] texts = { "", "靠近人物后按 {Interact} 交谈。", "按住 {Sneak} 潜行，绕到未察觉目标背后，按 {Secondary} 取皮。", "皮已入包。再次靠近来源按 {Secondary} 换皮，或按 {Inventory} 打开背包使用。", "以学徒身份靠近巡逻学徒，按 {Interact} 核对名册。", "以都知身份靠近巡逻学徒，按 {Interact} 调整路线。", "以乐正身份靠近都知，按 {Interact} 进入演奏。", "向顾客与裁衣匠询问订衣情况，判断谁来取衣，再换皮领取。", "以官证身份与金银匠交谈，补足金属份额。", "以金银匠身份打开打造台；按高亮点位完成工件，金属不足可找官证补料。", "以酒客身份靠近甲、乙或主人，按 {Interact} 划拳。各出一手，喊双方总数，输者喝酒。", "主人已有醉意。靠近按 {Interact} 挑战，也可继续划拳增加醉意。", "靠近殷红，按 {Interact} 继续交谈。", "本段已完成：殷红将带你前往轮回堂。" };
                var guides = serialized.FindProperty("guides"); guides.arraySize = texts.Length;
                for (int i = 0; i < texts.Length; i++) { var guide = guides.GetArrayElementAtIndex(i); guide.FindPropertyRelative("action").enumValueIndex = i; guide.FindPropertyRelative("text").stringValue = texts[i]; }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config); return config;
        }
        private static void BuildGuide(TMP_FontAsset font)
        {
            const string path = "Assets/_Project/Prefabs/UI/StoryGuideHudView.prefab";
            var settings = AddressableAssetSettingsDefaultObject.Settings; var group = settings.FindGroup("UI");
            if (group == null) throw new InvalidOperationException("缺少 UI 地址组");
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = TavernFingerGuessBuilder.Node("StoryGuideHudView", null, Vector2.zero, Vector2.zero); SceneManager.MoveGameObjectToScene(root, preview);
                var rect = (RectTransform)root.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                root.AddComponent<CanvasGroup>().blocksRaycasts = false; var view = root.AddComponent<StoryGuideHudView>();
                var content = TavernFingerGuessBuilder.Node("Hint", root.transform, new Vector2(-30, 180), new Vector2(490, 160));
                rect = (RectTransform)content.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
                var background = content.AddComponent<Image>(); background.color = new Color(.03f, .05f, .06f, .9f); background.raycastTarget = false;
                var label = TavernFingerGuessBuilder.Label("Text", content.transform, "", Vector2.zero, new Vector2(445, 130), font, 25);
                label.alignment = TextAlignmentOptions.Left;
                using (var serialized = new SerializedObject(view)) { serialized.FindProperty("label").objectReferenceValue = label; serialized.FindProperty("content").objectReferenceValue = content; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved); if (!saved) throw new InvalidOperationException("教程视图保存失败");
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group, false, false); entry.address = nameof(StoryGuideHudView);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, false); AssetDatabase.SaveAssetIfDirty(group);
        }
        private static void AddMentorBattle()
        {
            var roster = AssetDatabase.LoadAssetAtPath<BossRosterConfig>("Assets/_Project/Data/Battle/BossRosterConfig.asset");
            if (roster.Bosses.Any(boss => boss.Id == "spotlight_mentor")) return;
            using (var serialized = new SerializedObject(roster))
            {
                var rows = serialized.FindProperty("bosses"); rows.arraySize++; var row = rows.GetArrayElementAtIndex(rows.arraySize - 1);
                row.FindPropertyRelative("id").stringValue = "spotlight_mentor"; row.FindPropertyRelative("displayName").stringValue = "李诏阳 · 切磋";
                row.FindPropertyRelative("maxHealth").intValue = 24; row.FindPropertyRelative("playerHealth").intValue = 120;
                row.FindPropertyRelative("outOfBattleDrunk").intValue = 0; row.FindPropertyRelative("stagePrefab").objectReferenceValue = null;
                row.FindPropertyRelative("monsterKindId").intValue = 0; row.FindPropertyRelative("aggroEnabled").boolValue = false;
                row.FindPropertyRelative("battleProfile").FindPropertyRelative("phases").arraySize = 0;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(roster); AssetDatabase.SaveAssetIfDirty(roster);
        }
        private static void Text(UnityEngine.Object target, string field, string value)
        { using (var serialized = new SerializedObject(target)) { serialized.FindProperty(field).stringValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); } }
        private static void Target(NarrativeTrigger target, string id, string kind) { Text(target, "targetId", id); Text(target, "targetKind", kind); }
        private static Vector3 Ground(Vector3 position)
        {
            if (!Physics.Raycast(position + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("剧情点未找到地面：" + position);
            return hit.point + Vector3.up * .025f;
        }
        private static void Label(string name, Transform parent, TMP_FontAsset font)
        {
            var label = new GameObject("Name", typeof(TextMeshPro)).GetComponent<TextMeshPro>(); label.transform.SetParent(parent, false); label.transform.localPosition = new Vector3(0, 2.1f, 0);
            label.transform.rotation = Quaternion.Euler(38, 0, 0); label.font = font; label.fontSize = 3.4f; label.text = name; label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(8, 1); label.color = new Color(1f, .85f, .5f);
        }
        private static void AddCharacter(Transform parent, string prefab)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Characters/" + prefab + ".prefab");
            if (source == null) throw new InvalidOperationException("缺少现成人物外观");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, parent); model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
        }
    }
}
