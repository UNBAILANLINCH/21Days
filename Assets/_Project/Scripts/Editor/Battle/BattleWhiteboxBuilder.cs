// 职责：一键生成 / 重建回合制战斗的白盒资产（PRP/turnbased-battle W2a）——
//   闪白材质 M_SpriteFlash、BOSS 占位外观预制体 Prefabs/Battle/BattleBoss_Placeholder、战斗界面预制体 Prefabs/UI/BattleView、
//   战斗场景 Scenes/BattleArena.unity（舞台根 y = −1000、灰盒地面与背景、侧视战斗相机、补光、两个站位），
//   登记 Addressables（Scenes 组 BattleArena、UI 组 BattleView），并把 BossRosterConfig 里 sample_boss 的外观指到占位预制体。
// 为什么新建（复用 → 扩展 → 新建）：
//   1. 复用不行：PerformanceTemplateFactory 只建演出舞台壳（时间轴 + 舞台相机），不建 UI 与灰盒场景；
//      其余现有 UI 预制体是 MCP 一次性搭的，没有可重跑的生成器。
//   2. 扩展不行：战斗白盒的结构与演出模板无关，塞进去名实不符。
//   界面与场景有几十个节点、上百个引用，手搭一遍容易漏；写成可重跑的生成器，改布局改这里重跑即可。
//
// 重跑语义：预制体按路径覆盖保存（GUID 不变）；场景整份重建（根物体全删再建）。**手改过的预制体 / 场景会被覆盖**——
//   正式美术替换时停用本生成器、直接改资产。
// 副作用（project-guide 硬规则 3）：
//   · 只保存本生成器建出 / 改过的资产（SaveAssetIfDirty / SaveAsPrefabAsset / SaveScene），不调 AssetDatabase.SaveAssets。
//   · 场景在 Additive 新开 / 打开、建完保存后关闭，建造期间临时把它设为活动场景（新物体才落进它），结束把活动场景还回去；
//     不碰、不保存任何别的已打开场景。Play 中或别的已打开场景有未保存改动时拒绝执行。
using System;
using Game.Battle;
using Game.Core.Logging;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Editor.Battle
{
    /// <summary>战斗白盒生成器。</summary>
    public static class BattleWhiteboxBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/BattleArena.unity";
        public const string ViewPrefabPath = "Assets/_Project/Prefabs/UI/BattleView.prefab";
        public const string BossPrefabPath = "Assets/_Project/Prefabs/Battle/BattleBoss_Placeholder.prefab";
        public const string FlashMaterialPath = "Assets/_Project/Art/Materials/Battle/M_SpriteFlash.mat";
        public const string RosterPath = "Assets/_Project/Data/Battle/BossRosterConfig.asset";

        private const string FlashShaderName = "21Days/SpriteFlash";
        private const string PlayerPuppetPath = "Assets/_Project/Prefabs/Characters/Chibi_amiya.prefab";
        private const string BossPuppetPath = "Assets/_Project/Prefabs/Characters/Chibi_nian.prefab";
        private const string BlobShadowPath = "Assets/_Project/Art/Sprites/Fx/Fx_BlobShadow.png";
        private const string GroundMaterialPath = "Assets/_Project/Art/Materials/Graybox/M_GroundTile.mat";
        private const string WallMaterialPath = "Assets/_Project/Art/Materials/Graybox/M_Wall.mat";
        private const string DarkMaterialPath = "Assets/_Project/Art/Materials/Graybox/M_Dark.mat";
        private const string TowerMaterialPath = "Assets/_Project/Art/Materials/Graybox/M_Tower.mat";
        private const string AccentMaterialPath = "Assets/_Project/Art/Materials/Graybox/M_Accent.mat";

        /// <summary>URP 自带 Sprite-Unlit-Default 材质（SampleScene 的 BlobShadow 用的同一份）。</summary>
        private const string SpriteUnlitGuid = "9dfc825aed78fcd4ba02077103263b40";

        private const string SceneGroupName = "Scenes";
        private const string UiGroupName = "UI";
        private const string BossId = "sample_boss";

        /// <summary>舞台根离世界原点的偏移（PRP D4：远离世界，不与任何世界场景的物体重叠）。</summary>
        public static readonly Vector3 StageOrigin = new Vector3(0f, -1000f, 0f);

        // 侧视构图（站位 x ±2.6）：脚底在屏高约 29%，BOSS 头顶锚点约 73%，上方留出 BOSS 头顶条、下方留出招式栏。
        //   相机离角色 9.6 m，在 SampleScene 线性雾起点 18 m 之内，角色不吃雾；背景 15–23 m 处略带雾，有纵深。
        private static readonly Vector3 CameraRigLocal = new Vector3(0f, 1.9f, -9.6f);
        private const float CameraPitch = 4f;
        private const float CameraFieldOfView = 34f;

        // ── 白盒配色 ─────────────────────────────────────────────────────────
        private static readonly Color PanelColor = new Color(0.07f, 0.07f, 0.09f, 0.72f);
        private static readonly Color SlotDimColor = new Color(0.32f, 0.32f, 0.36f, 1f);
        private static readonly Color DimOverlay = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color TextColor = new Color(0.96f, 0.95f, 0.92f, 1f);
        private static readonly Color DarkText = new Color(0.12f, 0.1f, 0.08f, 1f);
        private static readonly Color CaptionColor = new Color(0.75f, 0.74f, 0.7f, 1f);
        private static readonly Color BarBack = new Color(0.08f, 0.08f, 0.09f, 0.85f);
        private static readonly Color BossHealthColor = new Color(0.86f, 0.13f, 0.13f, 1f);
        private static readonly Color DrunkColor = new Color(0f, 0.68f, 0.94f, 1f);
        private static readonly Color PlayerHealthColor = new Color(0.36f, 0.8f, 0.4f, 1f);
        private static readonly Color StatusTextColor = new Color(1f, 0.72f, 0.3f, 1f);
        private static readonly Color StunColor = new Color(1f, 0.86f, 0.25f, 1f);

        [MenuItem("21Days/战斗/重建战斗白盒（界面 + 战斗场景 + 占位 BOSS）", false, 440)]
        public static void BuildFromMenu()
        {
            string issue = BuildAll();
            if (issue != null) EditorUtility.DisplayDialog("战斗白盒", issue, "知道了");
        }

        /// <summary>全部生成；返回 null = 成功，否则是没做的原因（中文）。</summary>
        public static string BuildAll()
        {
            string blocked = CheckEditor();
            if (blocked != null) return blocked;

            Material flash = BuildFlashMaterial();
            GameObject bossPrefab = BuildBossPrefab(flash);
            BuildView();
            BuildArena(flash, bossPrefab);
            bool sceneRegistered = Register(ScenePath, BattleArena.SceneKey, SceneGroupName);
            bool viewRegistered = Register(ViewPrefabPath, nameof(BattleView), UiGroupName);
            AssignRoster(bossPrefab);
            Log.Info($"战斗白盒已生成：{ScenePath}、{ViewPrefabPath}、{BossPrefabPath}；Addressables 场景 {sceneRegistered}、界面 {viewRegistered}。");
            return null;
        }

        private static string CheckEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Play 中不能生成，先退出 Play。";
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (open.isDirty && open.path != ScenePath)
                    return $"已打开的场景 {open.name} 有未保存改动（可能是别的会话的），不生成，免得混进去。先处理它。";
            }

            return null;
        }

        // ── 材质 ─────────────────────────────────────────────────────────────

        private static Material BuildFlashMaterial()
        {
            Shader shader = Shader.Find(FlashShaderName);
            if (shader == null) throw new InvalidOperationException($"找不到着色器 {FlashShaderName}（Art/Shaders/SpriteFlash.shader）");
            EnsureFolder("Assets/_Project/Art/Materials/Battle");
            var material = AssetDatabase.LoadAssetAtPath<Material>(FlashMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_SpriteFlash" };
                AssetDatabase.CreateAsset(material, FlashMaterialPath);
            }

            material.shader = shader;
            material.SetColor("_FlashColor", Color.white);
            material.SetFloat("_Cutoff", 0.5f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        // ── BOSS 占位外观 ────────────────────────────────────────────────────

        private static GameObject BuildBossPrefab(Material flash)
        {
            EnsureFolder("Assets/_Project/Prefabs/Battle");
            var puppet = Require<GameObject>(BossPuppetPath);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("BattleBoss_Placeholder");
                SceneManager.MoveGameObjectToScene(root, preview);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(puppet, preview);
                instance.transform.SetParent(root.transform, false);
                // 占位 BOSS 比玩家大一圈（1.5 倍，约 2.4 m），稍染暗红以示敌方；正式外观等 §8.1 #1。
                instance.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                foreach (SpriteRenderer part in instance.GetComponentsInChildren<SpriteRenderer>(true))
                    part.color = new Color(1f, 0.82f, 0.82f, 1f);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath, out bool ok);
                if (!ok) throw new InvalidOperationException("BOSS 占位预制体保存失败：" + BossPrefabPath);
                return saved;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // ── 战斗界面预制体 ───────────────────────────────────────────────────

        private static void BuildView()
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = Node("BattleView", null);
                SceneManager.MoveGameObjectToScene(root, preview);
                Stretch(root);
                root.AddComponent<CanvasGroup>();
                var view = root.AddComponent<BattleView>();
                Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

                // 左上回合提示
                GameObject turnBox = Node("TurnBox", root.transform);
                Place(turnBox, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -24f), new Vector2(420f, 52f));
                AddImage(turnBox, PanelColor, rounded, false);
                TMP_Text turnLabel = Text(turnBox.transform, "Label", "第 1 回合 · 你的回合", 26f, TextColor, TextAlignmentOptions.Center, FontStyles.Bold);
                Stretch(turnLabel.gameObject, 12f);

                // 顶部可用道具
                GameObject itemBar = Node("ItemBar", root.transform);
                Place(itemBar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(640f, 128f));
                AddImage(itemBar, PanelColor, rounded, false);
                TMP_Text itemCaption = Text(itemBar.transform, "Caption", "可用道具", 18f, CaptionColor, TextAlignmentOptions.TopLeft, FontStyles.Normal);
                Place(itemCaption.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-28f, 26f));
                GameObject itemRowNode = Node("ItemRow", itemBar.transform);
                Place(itemRowNode, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(-24f, -40f));
                var itemLayout = itemRowNode.AddComponent<HorizontalLayoutGroup>();
                itemLayout.childAlignment = TextAnchor.MiddleCenter;
                itemLayout.spacing = 12f;
                itemLayout.childControlWidth = false;
                itemLayout.childControlHeight = false;
                itemLayout.childForceExpandWidth = false;
                itemLayout.childForceExpandHeight = false;
                BattleSlotWidget itemTemplate = Slot(itemRowNode.transform, "ItemTemplate", new Vector2(104f, 78f), rounded, true,
                    20f, 18f, false);
                TMP_Text itemEmpty = Text(itemBar.transform, "Empty", "（暂无可用道具）", 20f, CaptionColor, TextAlignmentOptions.Center, FontStyles.Normal);
                Place(itemEmpty.gameObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), new Vector2(-24f, -40f));

                // 底部招式栏
                GameObject skillBar = Node("SkillBar", root.transform);
                Place(skillBar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(680f, 168f));
                AddImage(skillBar, PanelColor, rounded, false);
                var skills = new BattleSlotWidget[3];
                for (int i = 0; i < skills.Length; i++)
                {
                    skills[i] = Slot(skillBar.transform, "Skill" + (i + 1), new Vector2(190f, 132f), rounded, true, 32f, 22f, true);
                    Place(skills[i].gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2((i - 1) * 214f, 0f), new Vector2(190f, 132f));
                }

                Button endTurn = TextButton(root.transform, "EndTurnButton", "结束回合", rounded);
                Place(endTurn.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(360f, 30f), new Vector2(170f, 56f));

                // 玩家状态（招式栏左上方），右下角剩余回合
                GameObject statusArea = Node("StatusArea", root.transform);
                Place(statusArea, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(-340f, 198f), new Vector2(300f, 70f));
                GameObject statusRowNode = Node("StatusRow", statusArea.transform);
                Place(statusRowNode, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(140f, 0f));
                var statusLayout = statusRowNode.AddComponent<HorizontalLayoutGroup>();
                statusLayout.childAlignment = TextAnchor.MiddleLeft;
                statusLayout.spacing = 10f;
                statusLayout.childControlWidth = false;
                statusLayout.childControlHeight = false;
                statusLayout.childForceExpandWidth = false;
                statusLayout.childForceExpandHeight = false;
                BattleSlotWidget statusTemplate = Slot(statusRowNode.transform, "StatusTemplate", new Vector2(60f, 60f), rounded, false, 30f, 16f, true);
                BattleSlotWidget rounds = Slot(statusArea.transform, "Rounds", new Vector2(150f, 60f), rounded, false, 16f, 24f, false);
                Place(rounds.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(150f, 60f));

                // 右侧怒气槽
                BattleSlotWidget rage = Slot(root.transform, "RageGauge", new Vector2(116f, 280f), rounded, false, 22f, 22f, false);
                Place(rage.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 90f), new Vector2(116f, 280f));
                GameObject pipRowNode = Node("PipRow", rage.transform);
                Place(pipRowNode, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(-20f, -90f));
                var pipLayout = pipRowNode.AddComponent<VerticalLayoutGroup>();
                pipLayout.childAlignment = TextAnchor.MiddleCenter;
                pipLayout.spacing = 10f;
                pipLayout.reverseArrangement = true; // 第 1 格在最下面，攒怒气从下往上亮
                pipLayout.childControlWidth = false;
                pipLayout.childControlHeight = false;
                pipLayout.childForceExpandWidth = false;
                pipLayout.childForceExpandHeight = false;
                GameObject pipNode = Node("PipTemplate", pipRowNode.transform);
                Size(pipNode, new Vector2(64f, 44f));
                Image pip = AddImage(pipNode, new Color(0.22f, 0.22f, 0.25f, 1f), knob, false);
                pip.type = Image.Type.Simple;
                // 怒气槽的字摆上下两端
                Place(Child<TMP_Text>(rage, "Title").gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(0f, 30f));
                Place(Child<TMP_Text>(rage, "Detail").gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(0f, 30f));

                // BOSS 头顶：名字 / 状态字 / 生命条 / 醉酒条（07「BOSS状态显示」）
                GameObject bossHud = Node("BossHud", root.transform);
                Place(bossHud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(360f, 120f));
                BattleBarWidget bossDrunk = Bar(bossHud.transform, "DrunkBar", new Vector2(0f, 0f), new Vector2(340f, 20f), DrunkColor, rounded, 15f);
                BattleBarWidget bossHealth = Bar(bossHud.transform, "HealthBar", new Vector2(0f, 24f), new Vector2(340f, 28f), BossHealthColor, rounded, 18f);
                TMP_Text bossStatus = Text(bossHud.transform, "Status", "微醺", 28f, StatusTextColor, TextAlignmentOptions.Bottom, FontStyles.Bold);
                Place(bossStatus.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 54f), new Vector2(340f, 36f));
                TMP_Text bossName = Text(bossHud.transform, "Name", "BOSS", 18f, CaptionColor, TextAlignmentOptions.Bottom, FontStyles.Normal);
                Place(bossName.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(340f, 26f));
                GameObject bossInfoNode = Node("Info", bossHud.transform);
                Place(bossInfoNode, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(340f, 92f));
                AddImage(bossInfoNode, new Color(0f, 0f, 0f, 0f), null, true); // 透明悬停区
                var bossInfo = bossInfoNode.AddComponent<BattleSlotWidget>();

                // 玩家头顶：生命条 + 晕眩标记
                GameObject playerHud = Node("PlayerHud", root.transform);
                Place(playerHud, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(260f, 76f));
                BattleBarWidget playerHealth = Bar(playerHud.transform, "HealthBar", Vector2.zero, new Vector2(240f, 24f), PlayerHealthColor, rounded, 16f);
                GameObject stun = Node("StunMark", playerHud.transform);
                Place(stun, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(96f, 36f));
                AddImage(stun, StunColor, rounded, false);
                TMP_Text stunText = Text(stun.transform, "Label", "晕眩", 22f, DarkText, TextAlignmentOptions.Center, FontStyles.Bold);
                Stretch(stunText.gameObject);

                // 飘字层
                GameObject floatRootNode = Node("FloatRoot", root.transform);
                Stretch(floatRootNode);
                TMP_Text floatTemplate = Text(floatRootNode.transform, "FloatTemplate", "-1", 40f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                Place(floatTemplate.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 60f));

                // 中央闪现提示
                GameObject hint = Node("Hint", root.transform);
                Place(hint, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(0f, 110f));
                AddImage(hint, new Color(0f, 0f, 0f, 0.62f), null, false);
                var hintGroup = hint.AddComponent<CanvasGroup>();
                hintGroup.blocksRaycasts = false;
                hintGroup.interactable = false;
                hintGroup.alpha = 0f;
                TMP_Text hintText = Text(hint.transform, "Text", "怪物处于微醺状态，本回合无法行动。", 40f, TextColor, TextAlignmentOptions.Center, FontStyles.Bold);
                Stretch(hintText.gameObject, 24f);

                // 悬停详情框（放最后，压在所有东西上面）
                GameObject tooltip = Node("Tooltip", root.transform);
                Place(tooltip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(420f, 100f));
                AddImage(tooltip, new Color(0.05f, 0.05f, 0.07f, 0.94f), rounded, false);
                var tooltipLayout = tooltip.AddComponent<VerticalLayoutGroup>();
                tooltipLayout.padding = new RectOffset(18, 18, 14, 14);
                tooltipLayout.childControlWidth = true;
                tooltipLayout.childControlHeight = true;
                tooltipLayout.childForceExpandWidth = true;
                tooltipLayout.childForceExpandHeight = false;
                var tooltipFitter = tooltip.AddComponent<ContentSizeFitter>();
                tooltipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var tooltipGroup = tooltip.AddComponent<CanvasGroup>();
                tooltipGroup.blocksRaycasts = false;
                tooltipGroup.interactable = false;
                tooltipGroup.alpha = 0f;
                TMP_Text tooltipText = Text(tooltip.transform, "Text", "详情", 20f, TextColor, TextAlignmentOptions.TopLeft, FontStyles.Normal);
                tooltipText.richText = true;
                tooltipText.lineSpacing = 6f;

                // 接线
                using (var so = new SerializedObject(view))
                {
                    Ref(so, "turnLabel", turnLabel);
                    Ref(so, "itemRow", itemRowNode.transform);
                    Ref(so, "itemTemplate", itemTemplate);
                    Ref(so, "itemEmpty", itemEmpty.gameObject);
                    SerializedProperty skillArray = so.FindProperty("skillSlots");
                    skillArray.arraySize = skills.Length;
                    for (int i = 0; i < skills.Length; i++) skillArray.GetArrayElementAtIndex(i).objectReferenceValue = skills[i];
                    Ref(so, "endTurnButton", endTurn);
                    Ref(so, "statusRow", statusRowNode.transform);
                    Ref(so, "statusTemplate", statusTemplate);
                    Ref(so, "roundsSlot", rounds);
                    Ref(so, "rageSlot", rage);
                    Ref(so, "ragePipRow", pipRowNode.transform);
                    Ref(so, "ragePipTemplate", pip);
                    Ref(so, "bossHud", bossHud.transform);
                    Ref(so, "bossName", bossName);
                    Ref(so, "bossStatus", bossStatus);
                    Ref(so, "bossHealth", bossHealth);
                    Ref(so, "bossDrunk", bossDrunk);
                    Ref(so, "bossInfo", bossInfo);
                    Ref(so, "playerHud", playerHud.transform);
                    Ref(so, "playerHealth", playerHealth);
                    Ref(so, "stunMark", stun);
                    Ref(so, "floatRoot", floatRootNode.transform);
                    Ref(so, "floatTemplate", floatTemplate);
                    Ref(so, "hintGroup", hintGroup);
                    Ref(so, "hintText", hintText);
                    Ref(so, "tooltipRoot", tooltip.transform);
                    Ref(so, "tooltipGroup", tooltipGroup);
                    Ref(so, "tooltipText", tooltipText);
                    // UIView 的默认选中（方向键导航起点）：招式 1。
                    Ref(so, "defaultSelected", skills[0].Button);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, ViewPrefabPath, out bool ok);
                if (!ok) throw new InvalidOperationException("BattleView 预制体保存失败：" + ViewPrefabPath);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // ── 战斗场景 ─────────────────────────────────────────────────────────

        private static void BuildArena(Material flash, GameObject bossPrefab)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen)
            {
                scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                    ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }

            try
            {
                SceneManager.SetActiveScene(scene); // 下面 new / CreatePrimitive 的物体落进战斗场景，不碰别的场景
                foreach (GameObject old in scene.GetRootGameObjects()) Object.DestroyImmediate(old);

                var root = new GameObject("BattleStage");
                root.transform.position = StageOrigin;
                var stage = root.AddComponent<BattleStage>();

                // 相机：侧视、透视、渲染器 1（UniversalRenderer，高低档同号）；默认关着、不打 MainCamera、不挂 AudioListener。
                var rig = new GameObject("CameraRig");
                rig.transform.SetParent(root.transform, false);
                rig.transform.localPosition = CameraRigLocal;
                var cameraNode = new GameObject("BattleCamera");
                cameraNode.transform.SetParent(rig.transform, false);
                cameraNode.transform.localRotation = Quaternion.Euler(CameraPitch, 0f, 0f);
                var camera = cameraNode.AddComponent<Camera>();
                camera.fieldOfView = CameraFieldOfView;
                camera.nearClipPlane = 0.3f;
                camera.farClipPlane = 80f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.8f, 0.76f, 0.68f, 1f);
                camera.enabled = false;
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.SetRenderer(1);
                cameraData.renderPostProcessing = true;

                // 补光：点光只照得到舞台（离世界 1000 m），不影响世界；世界的方向光 / 环境光照常叠上来。
                var fill = new GameObject("FillLight");
                fill.transform.SetParent(root.transform, false);
                fill.transform.localPosition = new Vector3(0f, 3.4f, -3.2f);
                var light = fill.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 16f;
                light.intensity = 1.3f;
                light.color = new Color(1f, 0.93f, 0.82f, 1f);
                light.shadows = LightShadows.None;

                // 灰盒：地面、矮墙、两侧屋舍、塔、酒桶、桌子（酒肆村口的意思，观感同探索灰盒）。
                var ground = Require<Material>(GroundMaterialPath);
                var wall = Require<Material>(WallMaterialPath);
                var dark = Require<Material>(DarkMaterialPath);
                var tower = Require<Material>(TowerMaterialPath);
                var accent = Require<Material>(AccentMaterialPath);
                var set = new GameObject("Set");
                set.transform.SetParent(root.transform, false);
                Block(set.transform, "Ground", PrimitiveType.Cube, new Vector3(0f, -0.1f, 4f), new Vector3(40f, 0.2f, 26f), ground);
                Block(set.transform, "WallLow", PrimitiveType.Cube, new Vector3(0f, 0.6f, 5.5f), new Vector3(26f, 1.2f, 0.5f), wall);
                Block(set.transform, "HouseLeft", PrimitiveType.Cube, new Vector3(-7.5f, 1.8f, 9f), new Vector3(5f, 3.6f, 3.5f), wall);
                Block(set.transform, "RoofLeft", PrimitiveType.Cube, new Vector3(-7.5f, 3.9f, 9f), new Vector3(5.6f, 0.6f, 4f), dark);
                Block(set.transform, "HouseRight", PrimitiveType.Cube, new Vector3(7f, 2.2f, 10f), new Vector3(6f, 4.4f, 4f), wall);
                Block(set.transform, "RoofRight", PrimitiveType.Cube, new Vector3(7f, 4.7f, 10f), new Vector3(6.6f, 0.6f, 4.6f), dark);
                Block(set.transform, "Tower", PrimitiveType.Cube, new Vector3(0.6f, 3.5f, 13f), new Vector3(2.2f, 7f, 2.2f), tower);
                Block(set.transform, "PostLeft", PrimitiveType.Cube, new Vector3(-4.2f, 1.2f, 3.6f), new Vector3(0.25f, 2.4f, 0.25f), accent);
                Block(set.transform, "PostRight", PrimitiveType.Cube, new Vector3(4.2f, 1.2f, 3.6f), new Vector3(0.25f, 2.4f, 0.25f), accent);
                Block(set.transform, "BarrelA", PrimitiveType.Cylinder, new Vector3(-5.4f, 0.45f, 2.4f), new Vector3(0.8f, 0.45f, 0.8f), dark);
                Block(set.transform, "BarrelB", PrimitiveType.Cylinder, new Vector3(-4.5f, 0.4f, 2.9f), new Vector3(0.7f, 0.4f, 0.7f), dark);
                Block(set.transform, "Table", PrimitiveType.Cube, new Vector3(5.3f, 0.45f, 2.6f), new Vector3(1.8f, 0.9f, 1f), accent);

                // 站位：玩家左（朝右）、BOSS 右（朝左）。
                BattleActor player = Actor(root.transform, "PlayerActor", new Vector3(-2.6f, 0f, 0f), false, 1.95f, 1.3f, flash);
                var playerPuppet = (GameObject)PrefabUtility.InstantiatePrefab(Require<GameObject>(PlayerPuppetPath), scene);
                playerPuppet.transform.SetParent(player.transform.Find("Visual"), false);
                BattleActor boss = Actor(root.transform, "BossActor", new Vector3(2.6f, 0f, 0f), true, 2.6f, 2.0f, flash);

                using (var so = new SerializedObject(stage))
                {
                    Ref(so, "stageCamera", camera);
                    Ref(so, "cameraRig", rig.transform);
                    Ref(so, "player", player);
                    Ref(so, "boss", boss);
                    Ref(so, "defaultBossPrefab", bossPrefab);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("战斗场景保存失败：" + ScenePath);
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded && previousActive != scene) SceneManager.SetActiveScene(previousActive);
                if (!wasOpen && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static BattleActor Actor(Transform parent, string name, Vector3 local, bool faceLeft, float headHeight, float shadowWidth, Material flash)
        {
            var node = new GameObject(name);
            node.transform.SetParent(parent, false);
            node.transform.localPosition = local;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(node.transform, false);
            var head = new GameObject("Head");
            head.transform.SetParent(node.transform, false);
            head.transform.localPosition = new Vector3(0f, headHeight, 0f);

            var shadow = new GameObject("Shadow");
            shadow.transform.SetParent(node.transform, false);
            shadow.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            shadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadow.transform.localScale = new Vector3(shadowWidth, shadowWidth * 0.5f, 1f);
            var shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = Require<Sprite>(BlobShadowPath);
            shadowRenderer.sharedMaterial = Require<Material>(AssetDatabase.GUIDToAssetPath(SpriteUnlitGuid));
            shadowRenderer.color = new Color(0f, 0f, 0f, 0.55f);

            var actor = node.AddComponent<BattleActor>();
            using (var so = new SerializedObject(actor))
            {
                Ref(so, "visualRoot", visual.transform);
                Ref(so, "head", head.transform);
                so.FindProperty("faceLeft").boolValue = faceLeft;
                Ref(so, "flashMaterial", flash);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return actor;
        }

        private static void Block(Transform parent, string name, PrimitiveType type, Vector3 local, Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(type);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = local;
            block.transform.localScale = scale;
            // 灰盒只看不碰：去掉碰撞体，免得世界里的物理查询（遮挡扫掠等）与它有任何瓜葛。
            Collider collider = block.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ── 登记 / 名册 ──────────────────────────────────────────────────────

        private static bool Register(string assetPath, string address, string groupName)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Log.Warn($"工程没有 Addressables 设置，{assetPath} 未登记。");
                return false;
            }

            AddressableAssetGroup group = settings.FindGroup(groupName);
            if (group == null)
            {
                Log.Warn($"Addressables 里没有 {groupName} 组，{assetPath} 未登记。");
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            if (entry != null && entry.parentGroup == group && entry.address == address) return true; // 已登记，不重写组文件
            entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entry.address = address;
            // 只落组文件（同 c72e3a0 的改动面）；settingsModified = false，不顺带改写 AddressableAssetSettings 的缓存哈希。
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true, false);
            AssetDatabase.SaveAssetIfDirty(group);
            return true;
        }

        private static void AssignRoster(GameObject bossPrefab)
        {
            var roster = AssetDatabase.LoadAssetAtPath<BossRosterConfig>(RosterPath);
            if (roster == null)
            {
                Log.Warn($"找不到 {RosterPath}，BOSS 外观没有指过去。");
                return;
            }

            using (var so = new SerializedObject(roster))
            {
                SerializedProperty bosses = so.FindProperty("bosses");
                for (int i = 0; i < bosses.arraySize; i++)
                {
                    SerializedProperty row = bosses.GetArrayElementAtIndex(i);
                    if (row.FindPropertyRelative("id").stringValue != BossId) continue;
                    row.FindPropertyRelative("stagePrefab").objectReferenceValue = bossPrefab;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssetIfDirty(roster);
        }

        // ── UI 小工具 ────────────────────────────────────────────────────────

        private static BattleSlotWidget Slot(Transform parent, string name, Vector2 size, Sprite sprite, bool clickable,
            float titleSize, float detailSize, bool withBadge)
        {
            GameObject node = Node(name, parent);
            Size(node, size);
            Image frame = AddImage(node, SlotDimColor, sprite, true);
            Button button = null;
            if (clickable)
            {
                button = node.AddComponent<Button>();
                button.targetGraphic = frame;
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 0.97f, 0.88f, 1f);
                colors.pressedColor = new Color(0.75f, 0.72f, 0.66f, 1f);
                colors.selectedColor = new Color(0.86f, 0.96f, 1f, 1f);
                colors.disabledColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                button.colors = colors;
            }

            TMP_Text title = Text(node.transform, "Title", name, titleSize, TextColor, TextAlignmentOptions.Center, FontStyles.Bold);
            Place(title.gameObject, new Vector2(0f, 0.45f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8f, -6f));
            TMP_Text detail = Text(node.transform, "Detail", string.Empty, detailSize, TextColor, TextAlignmentOptions.Center, FontStyles.Normal);
            Place(detail.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0.45f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-8f, -6f));

            TMP_Text badge = null;
            if (withBadge)
            {
                GameObject badgeBox = Node("Badge", node.transform);
                Place(badgeBox, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-4f, 4f), new Vector2(30f, 26f));
                AddImage(badgeBox, new Color(0f, 0f, 0f, 0.7f), sprite, false);
                badge = Text(badgeBox.transform, "Text", "1", 18f, TextColor, TextAlignmentOptions.Center, FontStyles.Bold);
                Stretch(badge.gameObject);
            }

            GameObject dim = Node("Dim", node.transform);
            Stretch(dim);
            AddImage(dim, DimOverlay, sprite, false);

            var widget = node.AddComponent<BattleSlotWidget>();
            using (var so = new SerializedObject(widget))
            {
                Ref(so, "button", button);
                Ref(so, "frame", frame);
                Ref(so, "title", title);
                Ref(so, "detail", detail);
                Ref(so, "badge", badge);
                Ref(so, "dim", dim);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return widget;
        }

        private static BattleBarWidget Bar(Transform parent, string name, Vector2 position, Vector2 size, Color fillColor, Sprite sprite, float fontSize)
        {
            GameObject node = Node(name, parent);
            Place(node, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), position, size);
            AddImage(node, BarBack, sprite, false);
            GameObject fillNode = Node("Fill", node.transform);
            var fill = fillNode.GetComponent<RectTransform>();
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);
            AddImage(fillNode, fillColor, sprite, false);
            TMP_Text label = Text(node.transform, "Label", string.Empty, fontSize, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(label.gameObject);
            var bar = node.AddComponent<BattleBarWidget>();
            using (var so = new SerializedObject(bar))
            {
                Ref(so, "fill", fill);
                Ref(so, "label", label);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return bar;
        }

        private static Button TextButton(Transform parent, string name, string label, Sprite sprite)
        {
            GameObject node = Node(name, parent);
            Image image = AddImage(node, new Color(0.85f, 0.55f, 0.25f, 1f), sprite, true);
            var button = node.AddComponent<Button>();
            button.targetGraphic = image;
            TMP_Text text = Text(node.transform, "Label", label, 24f, DarkText, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(text.gameObject);
            return button;
        }

        private static GameObject Node(string name, Transform parent)
        {
            var node = new GameObject(name, typeof(RectTransform));
            if (parent != null) node.transform.SetParent(parent, false);
            return node;
        }

        private static void Place(GameObject node, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = node.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Size(GameObject node, Vector2 size) => node.GetComponent<RectTransform>().sizeDelta = size;

        private static void Stretch(GameObject node, float inset = 0f)
        {
            var rect = node.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
        }

        private static Image AddImage(GameObject node, Color color, Sprite sprite, bool raycast)
        {
            var image = node.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        private static TMP_Text Text(Transform parent, string name, string content, float size, Color color, TextAlignmentOptions alignment, FontStyles style)
        {
            GameObject node = Node(name, parent);
            var text = node.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        private static T Child<T>(Component owner, string name) where T : Component
        {
            Transform child = owner.transform.Find(name);
            return child == null ? null : child.GetComponent<T>();
        }

        private static void Ref(SerializedObject so, string property, Object value)
        {
            SerializedProperty found = so.FindProperty(property);
            if (found == null) throw new InvalidOperationException($"{so.targetObject.GetType().Name} 没有序列化字段 {property}（生成器与脚本对不上）");
            found.objectReferenceValue = value;
        }

        private static T Require<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"找不到 {typeof(T).Name}：{path}");
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
