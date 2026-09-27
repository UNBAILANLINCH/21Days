// 职责：「序列帧目录 → AnimationClip / AnimatorController / 小人预制体」一键生成（美术按状态交序列帧后直接走它）。
//   菜单 21Days/角色/从序列帧生成小人…；脚本入口 FramePuppetGenerator.Generate(FramePuppetRequest)（MCP execute_code 可批量调用）。
//
// 生成物（全部可重跑：已有资产原地更新、保 GUID，不删了重建）：
//   · 帧贴图导入设置：Sprite / Single、PPU = 画布高 / 目标高度、pivot = meta.pivot（Custom）。只改这一个目录，
//     不碰 SpriteImportProcessor 的全局首次导入规则。
//   · Art/Animations/Characters/<名字>/chr_<名字>_<状态>.anim：Sprite 子物体 SpriteRenderer.m_Sprite 的关键帧序列，循环。
//   · 同目录 chr_<名字>.controller：参数 Moving(bool) / Running(bool) / Speed(float=1)；状态 Idle / Walk / Run，
//     过渡全部 0 时长、无退出时间（Idle→Walk/Run 按 Running 分流、Walk⇄Run 按 Running、Walk/Run→Idle 按 !Moving）；
//     Walk / Run 速度乘 Speed。有 run 帧时 Run 用 run 剪辑，没有时 Run 复用 walk 剪辑（控制器形状统一，驱动层不分支）。
//     其它状态作孤立状态加入。
//   · Prefabs/Characters/Chibi_<名字>.prefab：根 Animator(UnscaledTime) + ChibiPuppet（含 walkClipSpeed / runClipSpeed /
//     hasRunClip，地速取 meta.json animations.<walk|run>.groundSpeed，缺省 3 / 5）+ ChibiPuppetMotion，子物体 Sprite。
//   · 可选 Sprite Atlas（V2）：Art/Sprites/.../<名字>/<名字>.spriteatlasv2，包含帧目录；已存在不动；Sprite Packer 关着时跳过。
//
// 副作用说明：只写上面列出的资产，逐个 SaveAssetIfDirty，不调 AssetDatabase.SaveAssets（免得顺手保存别人未保存的改动）；
//   预制体走 PrefabUtility.LoadPrefabContents / SaveAsPrefabAsset（离屏副本，不经过预制体舞台，也不在任何场景留实例）。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：当时的 chr_chibi_*（分件小人，2026-09-28 已删）是分件骨架曲线动画，与「整帧换 Sprite」完全不同；没有从目录批量建动画的工具；
//   2. 扩展不行：PerformanceTemplateFactory 管演出时间轴，职责不同；规则部分已拆到 FramePuppetRules 供测试。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.CharacterPuppet;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Game.Editor.CharacterPuppet
{
    /// <summary>序列帧小人生成工具（窗口 + 静态入口）。</summary>
    public sealed class FramePuppetGenerator : EditorWindow
    {
        public const string AnimationRoot = "Assets/_Project/Art/Animations/Characters";
        public const string PrefabRoot = "Assets/_Project/Prefabs/Characters";
        public const string ConfigPath = "Assets/_Project/Data/CharacterPuppet/ChibiPuppetConfig.asset";
        public const string MaterialPath = "Assets/_Project/Art/Materials/Character/M_SpriteDepthClip.mat";
        public const string SpriteChildName = "Sprite";
        public const string MovingParameter = "Moving";
        public const string RunningParameter = "Running";
        public const string SpeedParameter = "Speed";

        private const string MenuPath = "21Days/角色/从序列帧生成小人…";

        // SpritePackerMode 数值：3/4 = 旧版 Sprite Atlas（V1），5/6 = Sprite Atlas V2；0~2 = 关或旧版 Legacy Packer。
        private const int PackerModeAtlasV1Min = 3;
        private const int PackerModeAtlasV2Min = 5;

        [SerializeField] private DefaultAsset frameFolder;
        [SerializeField] private string characterName = string.Empty;
        [SerializeField] private float targetHeight = FramePuppetRules.DefaultTargetHeight;
        [SerializeField] private float fps;
        [SerializeField] private bool defaultFacesLeft;
        [SerializeField] private bool createAtlas = true;
        [SerializeField] private string lastReport = string.Empty;
        [SerializeField] private Vector2 scroll;

        [MenuItem(MenuPath, false, 420)]
        private static void Open()
        {
            var window = GetWindow<FramePuppetGenerator>("序列帧小人");
            window.minSize = new Vector2(420f, 360f);
            if (Selection.activeObject is DefaultAsset folder
                && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(folder)))
            {
                window.frameFolder = folder;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "帧目录里放 chr_<名字>_<状态>_<NN>.png（至少 idle + walk，可选 run，同一画布尺寸）与可选 meta.json。\n" +
                "生成动画、控制器、Chibi_<名字>.prefab；可重跑，已有资产原地更新。规范见 docs/artist-guide.md「角色序列帧交付规范」。",
                MessageType.Info);
            frameFolder = (DefaultAsset)EditorGUILayout.ObjectField("帧目录", frameFolder, typeof(DefaultAsset), false);
            characterName = EditorGUILayout.TextField(new GUIContent("角色名（空 = 目录名）"), characterName);
            targetHeight = EditorGUILayout.FloatField(new GUIContent("目标高度（单位）", "整张画布在场景里的高度；PPU = 画布高像素 / 它"),
                targetHeight);
            fps = EditorGUILayout.FloatField(new GUIContent("帧率（0 = 取 meta，无则 24）"), fps);
            defaultFacesLeft = EditorGUILayout.Toggle(new GUIContent("美术默认朝左", "勾上则 Sprite 子物体 flipX，预制体仍按「默认朝右」工作"),
                defaultFacesLeft);
            createAtlas = EditorGUILayout.Toggle("建 Sprite Atlas", createAtlas);

            using (new EditorGUI.DisabledScope(frameFolder == null))
            {
                if (GUILayout.Button("生成 / 更新", GUILayout.Height(28f)))
                {
                    RunFromWindow();
                }
            }

            if (!string.IsNullOrEmpty(lastReport))
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                EditorGUILayout.TextArea(lastReport, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        private void RunFromWindow()
        {
            var request = new FramePuppetRequest
            {
                FrameDirectory = AssetDatabase.GetAssetPath(frameFolder),
                CharacterName = characterName,
                TargetHeight = targetHeight,
                Fps = fps,
                DefaultFacesLeft = defaultFacesLeft,
                CreateAtlas = createAtlas,
            };
            try
            {
                lastReport = Generate(request);
            }
            catch (InvalidOperationException e)
            {
                lastReport = "生成失败：" + e.Message;
                EditorUtility.DisplayDialog("序列帧小人", lastReport, "知道了");
            }
        }

        /// <summary>
        /// 生成 / 更新一个角色的全部资产，返回中文报告（含 PPU、pivot、各状态帧数与警告）。
        /// </summary>
        /// <exception cref="InvalidOperationException">目录不存在、没有合法帧、缺 idle / walk、帧尺寸不一致、预制体保存失败。</exception>
        public static string Generate(FramePuppetRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string directory = (request.FrameDirectory ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (!directory.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(directory))
            {
                throw new InvalidOperationException("帧目录必须是工程内 Assets/ 开头的已有目录：" + directory);
            }

            string name = string.IsNullOrEmpty(request.CharacterName) ? Path.GetFileName(directory) : request.CharacterName;
            if (!FramePuppetRules.IsValidCharacterName(name))
            {
                throw new InvalidOperationException("角色名只能用小写字母、数字与单个下划线：" + name);
            }

            if (request.TargetHeight <= 0f)
            {
                throw new InvalidOperationException("目标高度必须为正：" + request.TargetHeight);
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            var fileNames = new List<string>();
            foreach (string file in Directory.GetFiles(directory))
            {
                fileNames.Add(Path.GetFileName(file));
            }

            fileNames.Sort(StringComparer.Ordinal);
            SortedDictionary<string, List<string>> groups = FramePuppetRules.GroupFrames(name, fileNames, errors, warnings);
            string missing = FramePuppetRules.MissingStatesError(name, groups.Keys);
            if (missing != null)
            {
                errors.Add(missing);
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("\n", errors));
            }

            string metaPath = directory + "/" + FramePuppetRules.MetaFileName;
            FramePuppetMeta meta = File.Exists(metaPath) ? FramePuppetRules.ParseMeta(File.ReadAllText(metaPath)) : null;
            if (File.Exists(metaPath) && meta == null)
            {
                warnings.Add(string.Format("meta.json 解析失败，按默认值处理（pivot 底边正中、fps {0}、走 / 跑地速 {1} / {2}）",
                    FramePuppetRules.DefaultFps, FramePuppetRules.DefaultWalkGroundSpeed, FramePuppetRules.DefaultRunGroundSpeed));
            }

            Vector2Int canvas = ReadCanvasSize(directory, groups);
            float ppu = FramePuppetRules.PixelsPerUnit(canvas.y, request.TargetHeight);
            Vector2 pivot = FramePuppetRules.ResolvePivot(meta, canvas.x, canvas.y);
            float frameRate = FramePuppetRules.ResolveFps(request.Fps, meta);
            var clipMotion = new ClipMotion
            {
                WalkClipSpeed = FramePuppetRules.ResolveWalkGroundSpeed(meta),
                RunClipSpeed = FramePuppetRules.ResolveRunGroundSpeed(meta),
                HasRunClip = FramePuppetRules.HasRunState(groups.Keys),
            };

            ApplyImportSettings(directory, groups, ppu, pivot);

            string animationDir = AnimationRoot + "/" + name;
            EnsureFolder(animationDir);
            var clips = new SortedDictionary<string, AnimationClip>(StringComparer.Ordinal);
            var firstIdleSprite = (Sprite)null;
            var report = new StringBuilder();
            report.AppendLine(string.Format("角色 {0}：画布 {1}x{2}，PPU {3:0.###}，pivot ({4:0.####}, {5:0.####})，{6} fps，显示高 {7:0.###} 单位",
                name, canvas.x, canvas.y, ppu, pivot.x, pivot.y, frameRate, request.TargetHeight));
            foreach (KeyValuePair<string, List<string>> group in groups)
            {
                List<Sprite> sprites = LoadSprites(directory, group.Value);
                if (group.Key == FramePuppetRules.IdleState)
                {
                    firstIdleSprite = sprites[0];
                }

                string clipPath = string.Format("{0}/chr_{1}_{2}.anim", animationDir, name, group.Key);
                clips.Add(group.Key, WriteClip(clipPath, sprites, frameRate));
                report.AppendLine(string.Format("  {0}：{1} 帧 → {2}", group.Key, sprites.Count, clipPath));
            }

            string controllerPath = string.Format("{0}/chr_{1}.controller", animationDir, name);
            AnimatorController controller = WriteController(controllerPath, clips);
            report.AppendLine("  控制器 → " + controllerPath);
            report.AppendLine(string.Format("  Run 态：{0}；剪辑地速 走 {1:0.###} / 跑 {2:0.###} 单位/秒",
                clipMotion.HasRunClip ? "run 剪辑" : "无 run 帧，复用 walk 剪辑", clipMotion.WalkClipSpeed, clipMotion.RunClipSpeed));

            string prefabPath = string.Format("{0}/Chibi_{1}.prefab", PrefabRoot, name);
            WritePrefab(prefabPath, controller, firstIdleSprite, request.DefaultFacesLeft, clipMotion);
            report.AppendLine("  预制体 → " + prefabPath);

            if (request.CreateAtlas)
            {
                report.AppendLine("  " + WriteAtlas(directory, name));
            }

            foreach (string warning in warnings)
            {
                report.AppendLine("  [警告] " + warning);
            }

            string text = report.ToString();
            Debug.Log("[序列帧小人] " + text);
            return text;
        }

        private static Vector2Int ReadCanvasSize(string directory, SortedDictionary<string, List<string>> groups)
        {
            var names = new List<string>();
            var sizes = new List<Vector2Int>();
            foreach (List<string> files in groups.Values)
            {
                foreach (string file in files)
                {
                    TextureImporter importer = GetImporter(directory + "/" + file);
                    int width;
                    int height;
                    importer.GetSourceTextureWidthAndHeight(out width, out height);
                    names.Add(file);
                    sizes.Add(new Vector2Int(width, height));
                }
            }

            string error = FramePuppetRules.CanvasSizeError(names, sizes);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            return sizes[0];
        }

        private static void ApplyImportSettings(string directory, SortedDictionary<string, List<string>> groups, float ppu,
            Vector2 pivot)
        {
            var dirty = new List<TextureImporter>();
            foreach (List<string> files in groups.Values)
            {
                foreach (string file in files)
                {
                    TextureImporter importer = GetImporter(directory + "/" + file);
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    bool same = importer.textureType == TextureImporterType.Sprite
                                && importer.spriteImportMode == SpriteImportMode.Single
                                && settings.spriteAlignment == (int)SpriteAlignment.Custom
                                && Approximately(settings.spritePivot.x, pivot.x)
                                && Approximately(settings.spritePivot.y, pivot.y)
                                && Approximately(settings.spritePixelsPerUnit, ppu);
                    if (same)
                    {
                        continue;
                    }

                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = pivot;
                    settings.spritePixelsPerUnit = ppu;
                    importer.SetTextureSettings(settings);
                    dirty.Add(importer);
                }
            }

            if (dirty.Count == 0)
            {
                return;
            }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (TextureImporter importer in dirty)
                {
                    importer.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static List<Sprite> LoadSprites(string directory, List<string> files)
        {
            var sprites = new List<Sprite>(files.Count);
            foreach (string file in files)
            {
                string path = directory + "/" + file;
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    throw new InvalidOperationException("导入后取不到 Sprite：" + path);
                }

                sprites.Add(sprite);
            }

            return sprites;
        }

        private static AnimationClip WriteClip(string path, List<Sprite> sprites, float frameRate)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(clip, path);
            }

            clip.ClearCurves();
            foreach (EditorCurveBinding old in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                AnimationUtility.SetObjectReferenceCurve(clip, old, null);
            }

            clip.frameRate = frameRate;
            // 末尾补一帧与最后一帧相同的键：剪辑长度 = 帧数 / fps，循环时最后一帧也完整显示一帧的时长。
            var keys = new ObjectReferenceKeyframe[sprites.Count + 1];
            for (int i = 0; i < sprites.Count; i++)
            {
                keys[i] = new ObjectReferenceKeyframe { time = i / frameRate, value = sprites[i] };
            }

            keys[sprites.Count] = new ObjectReferenceKeyframe
            {
                time = sprites.Count / frameRate,
                value = sprites[sprites.Count - 1],
            };
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve(SpriteChildName, typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        private static AnimatorController WriteController(string path, SortedDictionary<string, AnimationClip> clips)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            EnsureParameters(controller);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            var existing = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            foreach (ChildAnimatorState child in machine.states)
            {
                existing[child.state.name] = child.state;
            }

            // 状态 → 剪辑：每组帧一个状态；没有 run 帧时补一个复用 walk 剪辑的 run 状态，控制器形状统一。
            var motions = new SortedDictionary<string, AnimationClip>(clips, StringComparer.Ordinal);
            if (!motions.ContainsKey(FramePuppetRules.RunState))
            {
                motions.Add(FramePuppetRules.RunState, clips[FramePuppetRules.WalkState]);
            }

            // 已有状态按名字复用（控制器 GUID 与状态对象不变），缺的才新建；旧过渡全部清掉后按约定重建，重跑不会重复。
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            var states = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            int row = 0;
            foreach (KeyValuePair<string, AnimationClip> pair in motions)
            {
                string stateName = FramePuppetRules.AnimatorStateName(pair.Key);
                wanted.Add(stateName);
                AnimatorState state;
                if (!existing.TryGetValue(stateName, out state))
                {
                    state = machine.AddState(stateName, new Vector3(300f, 60f * row, 0f));
                }

                row++;
                state.motion = pair.Value;
                state.speed = 1f;
                state.writeDefaultValues = true;
                bool locomotion = pair.Key == FramePuppetRules.WalkState || pair.Key == FramePuppetRules.RunState;
                state.speedParameterActive = locomotion;
                state.speedParameter = locomotion ? SpeedParameter : string.Empty;
                foreach (AnimatorStateTransition transition in state.transitions)
                {
                    state.RemoveTransition(transition);
                }

                states.Add(pair.Key, state);
            }

            foreach (KeyValuePair<string, AnimatorState> pair in existing)
            {
                if (!wanted.Contains(pair.Key))
                {
                    machine.RemoveState(pair.Value);
                }
            }

            AnimatorState idle = states[FramePuppetRules.IdleState];
            AnimatorState walk = states[FramePuppetRules.WalkState];
            AnimatorState run = states[FramePuppetRules.RunState];
            // 同一状态的过渡按添加顺序求值：回 Idle 放第一条，停步优先于走跑互切。
            AddInstantTransition(idle, walk, Cond(AnimatorConditionMode.If, MovingParameter),
                Cond(AnimatorConditionMode.IfNot, RunningParameter));
            AddInstantTransition(idle, run, Cond(AnimatorConditionMode.If, MovingParameter),
                Cond(AnimatorConditionMode.If, RunningParameter));
            AddInstantTransition(walk, idle, Cond(AnimatorConditionMode.IfNot, MovingParameter));
            AddInstantTransition(walk, run, Cond(AnimatorConditionMode.If, RunningParameter));
            AddInstantTransition(run, idle, Cond(AnimatorConditionMode.IfNot, MovingParameter));
            AddInstantTransition(run, walk, Cond(AnimatorConditionMode.IfNot, RunningParameter));
            machine.defaultState = idle;

            // AnimatorController 的编辑 API 会往 Undo 栈推记录；别的测试运行器收尾回滚 Undo 时会把刚建好的状态机打回去
            // （ai-docs/pitfalls.md「Timeline API 建好的时间轴被回滚」同类），所以清掉再存。
            Undo.ClearUndo(controller);
            Undo.ClearUndo(machine);
            foreach (AnimatorState state in states.Values)
            {
                Undo.ClearUndo(state);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        private static void EnsureParameters(AnimatorController controller)
        {
            var parameters = new List<AnimatorControllerParameter>(controller.parameters);
            UpsertParameter(parameters, MovingParameter, AnimatorControllerParameterType.Bool, 0f);
            UpsertParameter(parameters, RunningParameter, AnimatorControllerParameterType.Bool, 0f);
            UpsertParameter(parameters, SpeedParameter, AnimatorControllerParameterType.Float, 1f);
            controller.parameters = parameters.ToArray();
        }

        private static void UpsertParameter(List<AnimatorControllerParameter> parameters, string name,
            AnimatorControllerParameterType type, float defaultFloat)
        {
            AnimatorControllerParameter parameter = parameters.Find(p => p.name == name);
            if (parameter == null)
            {
                parameter = new AnimatorControllerParameter { name = name };
                parameters.Add(parameter);
            }

            parameter.type = type;
            parameter.defaultBool = false;
            parameter.defaultInt = 0;
            parameter.defaultFloat = defaultFloat;
        }

        private static AnimatorCondition Cond(AnimatorConditionMode mode, string parameter)
        {
            return new AnimatorCondition { mode = mode, parameter = parameter, threshold = 0f };
        }

        private static void AddInstantTransition(AnimatorState from, AnimatorState to, params AnimatorCondition[] conditions)
        {
            // 整帧换图没法混合，过渡给 0：切状态立刻换到目标剪辑的第一帧。
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.hasFixedDuration = true;
            transition.duration = 0f;
            transition.offset = 0f;
            foreach (AnimatorCondition condition in conditions)
            {
                transition.AddCondition(condition.mode, condition.threshold, condition.parameter);
            }

            Undo.ClearUndo(transition);
        }

        private static void WritePrefab(string path, AnimatorController controller, Sprite firstIdle, bool facesLeft,
            ClipMotion clipMotion)
        {
            var config = AssetDatabase.LoadAssetAtPath<ChibiPuppetConfig>(ConfigPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (config == null || material == null)
            {
                throw new InvalidOperationException("缺依赖资产：" + (config == null ? ConfigPath : MaterialPath));
            }

            EnsureFolder(PrefabRoot);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                CreateEmptyPrefab(path);
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.transform.localScale = Vector3.one;
                root.layer = 0;

                Animator animator = GetOrAdd<Animator>(root);
                animator.runtimeAnimatorController = controller;
                animator.updateMode = AnimatorUpdateMode.UnscaledTime; // 对话时停期间待机照播，与原分件小人预制体一致
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                ChibiPuppet puppet = GetOrAdd<ChibiPuppet>(root);
                ChibiPuppetMotion motion = GetOrAdd<ChibiPuppetMotion>(root);

                Transform spriteTransform = root.transform.Find(SpriteChildName);
                if (spriteTransform == null)
                {
                    var child = new GameObject(SpriteChildName);
                    SceneManager.MoveGameObjectToScene(child, root.scene);
                    spriteTransform = child.transform;
                    spriteTransform.SetParent(root.transform, false);
                }

                spriteTransform.localPosition = Vector3.zero;
                spriteTransform.localRotation = Quaternion.identity;
                spriteTransform.localScale = Vector3.one;
                SpriteRenderer renderer = GetOrAdd<SpriteRenderer>(spriteTransform.gameObject);
                renderer.sprite = firstIdle;
                renderer.sharedMaterial = material;
                renderer.sortingLayerID = 0;
                renderer.sortingOrder = 0;
                renderer.color = Color.white;
                renderer.flipX = facesLeft;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var puppetSo = new SerializedObject(puppet);
                puppetSo.FindProperty("animator").objectReferenceValue = animator;
                SerializedProperty parts = puppetSo.FindProperty("parts");
                parts.arraySize = 1;
                parts.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                puppetSo.FindProperty("walkClipSpeed").floatValue = clipMotion.WalkClipSpeed;
                puppetSo.FindProperty("runClipSpeed").floatValue = clipMotion.RunClipSpeed;
                puppetSo.FindProperty("hasRunClip").boolValue = clipMotion.HasRunClip;
                puppetSo.ApplyModifiedPropertiesWithoutUndo();

                var motionSo = new SerializedObject(motion);
                motionSo.FindProperty("puppet").objectReferenceValue = puppet;
                motionSo.FindProperty("config").objectReferenceValue = config;
                motionSo.FindProperty("trackedRoot").objectReferenceValue = null;
                motionSo.FindProperty("facingSource").objectReferenceValue = null;
                motionSo.ApplyModifiedPropertiesWithoutUndo();

                bool saved;
                PrefabUtility.SaveAsPrefabAsset(root, path, out saved);
                if (!saved)
                {
                    throw new InvalidOperationException("预制体保存失败：" + path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateEmptyPrefab(string path)
        {
            // 在预览场景里建根再存成预制体：不经过任何真实场景，不会在场景里留下散件。
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject(Path.GetFileNameWithoutExtension(path));
                SceneManager.MoveGameObjectToScene(root, preview);
                bool saved;
                PrefabUtility.SaveAsPrefabAsset(root, path, out saved);
                if (!saved)
                {
                    throw new InvalidOperationException("预制体创建失败：" + path);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static string WriteAtlas(string directory, string name)
        {
            int mode = (int)EditorSettings.spritePackerMode;
            if (mode < PackerModeAtlasV1Min)
            {
                return "图集：Sprite Packer 为 " + EditorSettings.spritePackerMode + "，跳过";
            }

            Object folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(directory);
            if (mode >= PackerModeAtlasV2Min)
            {
                string path = directory + "/" + name + ".spriteatlasv2";
                if (File.Exists(path))
                {
                    return "图集：已存在，保持不动 → " + path;
                }

                var atlas = new SpriteAtlasAsset();
                atlas.Add(new[] { folder });
                SpriteAtlasAsset.Save(atlas, path);
                AssetDatabase.ImportAsset(path);
                var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(path);
                importer.includeInBuild = true;
                importer.packingSettings = AtlasPacking();
                importer.textureSettings = AtlasTexture();
                importer.SaveAndReimport();
                return "图集（V2）→ " + path;
            }

            string v1Path = directory + "/" + name + ".spriteatlas";
            if (File.Exists(v1Path))
            {
                return "图集：已存在，保持不动 → " + v1Path;
            }

            var v1 = new SpriteAtlas();
            v1.Add(new[] { folder });
            v1.SetPackingSettings(AtlasPacking());
            v1.SetTextureSettings(AtlasTexture());
            v1.SetIncludeInBuild(true);
            AssetDatabase.CreateAsset(v1, v1Path);
            return "图集（V1）→ " + v1Path;
        }

        private static SpriteAtlasPackingSettings AtlasPacking()
        {
            return new SpriteAtlasPackingSettings
            {
                blockOffset = 1,
                enableRotation = false,
                enableTightPacking = true,
                padding = 4,
            };
        }

        private static SpriteAtlasTextureSettings AtlasTexture()
        {
            // 与 SpriteImportProcessor 的纸片默认一致：透视相机下远近缩放，要 mipmap + Bilinear。
            return new SpriteAtlasTextureSettings
            {
                readable = false,
                generateMipMaps = true,
                sRGB = true,
                filterMode = FilterMode.Bilinear,
            };
        }

        private static TextureImporter GetImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("不是贴图或还没导入（先让 Unity 刷新一次）：" + path);
            }

            return importer;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static bool Approximately(float a, float b)
        {
            return Math.Abs(a - b) < 1e-4f;
        }

        /// <summary>写进预制体 ChibiPuppet 的剪辑运动标定（仅生成过程内部传参）。</summary>
        private struct ClipMotion
        {
            public float WalkClipSpeed; // lint-ok: 私有嵌套结构体的内部传参字段，不序列化、不进 Inspector
            public float RunClipSpeed; // lint-ok: 同上
            public bool HasRunClip; // lint-ok: 同上
        }
    }
}
