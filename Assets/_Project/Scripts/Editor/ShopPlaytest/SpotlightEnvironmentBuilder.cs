// 职责：人工菜单为现有 SampleScene 首次接入局部环境预览，不造地图或改剧情。
// 原剧情生成器会写入故事、Boot 与场景，不能复用或扩展为安全的环境单独接线。
// 载体：人工菜单；锚点：场景根 SpotlightEnvironment；已存在视图即退出，不自动重建。
using System;
using System.IO;
using System.Linq;
using Game.Narrative;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game.Editor.ShopPlaytest
{
    public static class SpotlightEnvironmentBuilder
    {
        private const string MenuPath = "21Days/剧情/接入剧情光照与局部天气";
        private const string ScenePath = "Assets/_Project/Scenes/SampleScene.unity";
        private const string ConfigPath = "Assets/_Project/Data/Narrative/SpotlightEnvironmentConfig.asset";
        private const string MaterialFolder = "Assets/_Project/Art/Materials/Narrative";
        private const string MaterialPath = MaterialFolder + "/SpotlightSoftParticle.mat";
        private const string TexturePath = MaterialFolder + "/SpotlightSoftParticle.asset";
        private const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";
        private const string UndoLabel = "接入剧情光照与局部天气";

        [MenuItem(MenuPath)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play，再接入剧情环境。");
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
                throw new InvalidOperationException("请先打开并激活现有 SampleScene；不修改其他场景。");
            var roots = scene.GetRootGameObjects();
            var existing = roots.SelectMany(root => root.GetComponentsInChildren<SpotlightEnvironmentView>(true)).ToArray();
            if (existing.Length > 0)
            {
                Selection.activeObject = existing[0];
                Debug.LogWarning("已有剧情环境视图，已选中；拒绝覆盖或重复创建。请直接调整现有引用。", existing[0]);
                return;
            }
            var lights = roots.SelectMany(root => root.GetComponentsInChildren<Light>(true))
                .Where(light => light.type == LightType.Directional).ToArray();
            if (lights.Length != 1)
                throw new InvalidOperationException("SampleScene 必须恰好有一个方向光（含禁用对象）；请先消除缺失或歧义。");
            var bindings = roots.SelectMany(root => root.GetComponentsInChildren<SpotlightSceneBindings>(true)).ToArray();
            if (bindings.Length == 0)
                throw new InvalidOperationException("请先接入现有 SpotlightSceneBindings，再安装环境。");
            // 只读登记点，不按物体名称猜地点，也不借用另一界的同名节点。
            var mentor = FindAnchor(bindings, true, "enter_r0026", scene);
            var library = FindAnchor(bindings, true, "enter_r0129", scene);
            var water = FindAnchor(bindings, false, "enter_r0384", scene);
            var config = LoadOrCreateConfig();
            var material = LoadOrCreateMaterial();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            try
            {
                // 稳定根不属于人妖分组，也不挂在 Display 会关闭的交互点下。
                var root = NewObject("SpotlightEnvironment", null, scene);
                var view = Undo.AddComponent<SpotlightEnvironmentView>(root);
                ParticleSystem snow = null;
                ParticleSystem flame = null;
                Light altar = null;
                Light[] candles = Array.Empty<Light>();
                ParticleSystem mist = null;
                if (mentor != null)
                {
                    var site = Site("Human_导师雪与祭火_预览可调", root.transform, mentor.position);
                    snow = Particles("Snow", site, material, new Vector3(0f, 5f, 0f),
                        new Vector3(12f, .2f, 12f), 32f, 6f, .09f, new Color(.94f, .97f, 1f, .85f), new Vector3(.10f, -1f, .04f));
                    // 点位只是已知的局部预览参照，祭坛正式美术摆位仍由作者调整。
                    altar = LocalLight("AltarFire", site, new Vector3(1.2f, .6f, .6f), config, config.AltarIntensity, 4f);
                    flame = Particles("AltarFlame", site, material, altar.transform.localPosition,
                        new Vector3(.35f, .1f, .35f), 18f, .8f, .28f, config.FlameColor, new Vector3(0f, .7f, 0f));
                }
                else Missing(root.transform, "Human_雪与祭火", "enter_r0026");
                if (library != null)
                {
                    var site = Site("Human_书库烛光_预览可调", root.transform, library.position);
                    candles = new[]
                    {
                        LocalLight("Candle_Left", site, new Vector3(-.8f, .7f, .4f), config, config.CandleIntensity, 2.5f),
                        LocalLight("Candle_Right", site, new Vector3(.8f, .7f, .4f), config, config.CandleIntensity, 2.5f)
                    };
                }
                else Missing(root.transform, "Human_书库烛光", "enter_r0129");
                if (water != null)
                {
                    var site = Site("Demon_水边薄雾_预览可调", root.transform, water.position);
                    mist = Particles("WaterMist", site, material, new Vector3(0f, .25f, 0f),
                        new Vector3(5f, .1f, 2f), 3f, 5f, 1.4f, new Color(.72f, .82f, .95f, .12f), new Vector3(.12f, .02f, 0f));
                }
                else Missing(root.transform, "Demon_水边薄雾", "enter_r0384");
                using (var serialized = new SerializedObject(view))
                {
                    serialized.FindProperty("config").objectReferenceValue = config;
                    serialized.FindProperty("directionalLight").objectReferenceValue = lights[0];
                    serialized.FindProperty("snow").objectReferenceValue = snow;
                    serialized.FindProperty("altarFire").objectReferenceValue = altar;
                    serialized.FindProperty("altarFlame").objectReferenceValue = flame;
                    serialized.FindProperty("waterMist").objectReferenceValue = mist;
                    var list = serialized.FindProperty("candles");
                    list.arraySize = candles.Length;
                    for (int i = 0; i < candles.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = candles[i];
                    serialized.ApplyModifiedProperties();
                }
                EditorSceneManager.MarkSceneDirty(scene);
                Selection.activeObject = root;
                Undo.CollapseUndoOperations(group);
                Debug.Log("剧情环境已接线，场景尚未保存。检查预览位置后手动保存；缺少点位的禁用空标记不参与播放。撤销只回退场景，新建配置与材质保留供复用。", root);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static Transform FindAnchor(SpotlightSceneBindings[] bindings, bool human, string stage, Scene scene)
        {
            var matches = bindings.Where(binding => binding.IsHumanStory == human && binding.Points != null)
                .SelectMany(binding => binding.Points)
                .Where(point => point != null && point.StageId == stage && point.Anchor != null)
                .Select(point => point.Anchor).Distinct().ToArray();
            if (matches.Length > 1 || matches.Any(anchor => anchor.gameObject.scene != scene))
                throw new InvalidOperationException("点位 " + stage + " 有多个或跨场景锚点，请先检查绑定。");
            return matches.Length == 1 ? matches[0] : null;
        }

        [MenuItem("21Days/剧情/局部环境/在所选位置添加水雾")]
        public static void AddWaterMist()
        {
            var view = RequireSelectedSite(out var position);
            using (var serialized = new SerializedObject(view))
            {
                var reference = serialized.FindProperty("waterMist");
                if (reference.objectReferenceValue != null)
                {
                    Selection.activeObject = reference.objectReferenceValue;
                    Debug.LogWarning("水雾已有引用，已选中；请移动现有对象，不重复创建。", view);
                    return;
                }
                var material = LoadOrCreateMaterial();
                EditSite(view, "添加局部水雾", () =>
                {
                    var site = Site("WaterMist_作者所选位置_预览可调", view.transform, position);
                    var mist = Particles("WaterMist", site, material, new Vector3(0f, .25f, 0f),
                        new Vector3(5f, .1f, 2f), 3f, 5f, 1.4f, new Color(.72f, .82f, .95f, .12f), new Vector3(.12f, .02f, 0f));
                    reference.objectReferenceValue = mist;
                    serialized.ApplyModifiedProperties();
                    Selection.activeObject = site.gameObject;
                });
            }
        }

        [MenuItem("21Days/剧情/局部环境/在所选位置添加烛光")]
        public static void AddCandle()
        {
            var view = RequireSelectedSite(out var position);
            using (var serialized = new SerializedObject(view))
            {
                var config = serialized.FindProperty("config").objectReferenceValue as SpotlightEnvironmentConfig;
                if (config == null) throw new InvalidOperationException("请先为环境视图赋有效配置。");
                EditSite(view, "添加局部烛光", () =>
                {
                    var site = Site("Candle_作者所选位置_预览可调", view.transform, position);
                    var candle = LocalLight("Candle", site, Vector3.zero, config, config.CandleIntensity, 2.5f);
                    var list = serialized.FindProperty("candles");
                    int index = list.arraySize;
                    list.arraySize = index + 1;
                    list.GetArrayElementAtIndex(index).objectReferenceValue = candle;
                    serialized.ApplyModifiedProperties();
                    Selection.activeObject = site.gameObject;
                });
            }
        }

        [MenuItem("21Days/剧情/局部环境/在所选位置添加静态积水")]
        public static void AddStaticWater()
        {
            var view = RequireSelectedSite(out var position);
            const string name = "StaticWater_作者所选位置_美术可调预览";
            var existing = view.transform.Find(name);
            if (existing != null)
            {
                Selection.activeObject = existing.gameObject;
                Debug.LogWarning("静态积水预览已存在，已选中；请调整现有对象。", existing);
                return;
            }
            var material = LoadOrCreateWaterMaterial();
            EditSite(view, "添加静态积水预览", () =>
            {
                var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
                water.name = name;
                SceneManager.MoveGameObjectToScene(water, view.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(water, UndoLabel);
                Undo.SetTransformParent(water.transform, view.transform, UndoLabel);
                Undo.RecordObject(water.transform, UndoLabel);
                water.transform.position = position + Vector3.up * .015f;
                water.transform.localScale = new Vector3(.3f, 1f, .2f);
                Undo.DestroyObjectImmediate(water.GetComponent<Collider>());
                var renderer = water.GetComponent<MeshRenderer>();
                Undo.RecordObject(renderer, UndoLabel);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Selection.activeObject = water;
            });
        }

        private static SpotlightEnvironmentView RequireSelectedSite(out Vector3 position)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || !scene.isLoaded || scene.path != ScenePath)
                throw new InvalidOperationException("退出 Play，并打开、激活现有 SampleScene 后再摆位。");
            var selected = Selection.activeTransform;
            if (selected == null || EditorUtility.IsPersistent(selected) || selected.gameObject.scene != scene)
                throw new InvalidOperationException("请在 SampleScene 选择一个真实位置锚点；不接受 Project 资产或其他场景对象。");
            var views = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<SpotlightEnvironmentView>(true)).ToArray();
            if (views.Length != 1 || views[0].transform.parent != null)
                throw new InvalidOperationException("需要唯一、位于场景根的 SpotlightEnvironmentView；请先完成首次接线。");
            position = selected.position;
            return views[0];
        }

        private static void EditSite(SpotlightEnvironmentView view, string label, Action edit)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            try
            {
                edit();
                EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
                Undo.CollapseUndoOperations(group);
                Debug.Log(label + "完成，场景尚未保存；请检查位置、尺寸及可见性后保存。只复制所选位置，不跟随交互点显隐。", view);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static Material LoadOrCreateWaterMaterial()
        {
            const string path = MaterialFolder + "/SpotlightStaticWaterPreview.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                if (material.shader == null || !material.shader.isSupported)
                    throw new InvalidOperationException("已有积水材质的 Shader 不可用；保留资产，请先修复。");
                return material;
            }
            RequireUnusedPath(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("缺少可用 URP/Lit Shader；不修改渲染设置。");
            EnsureFolder(MaterialFolder);
            material = new Material(shader) { name = "SpotlightStaticWaterPreview" };
            material.SetColor("_BaseColor", new Color(.21f, .31f, .35f, 1f));
            material.SetFloat("_Smoothness", .35f);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject NewObject(string name, Transform parent, Scene scene)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            if (parent != null) Undo.SetTransformParent(go.transform, parent, UndoLabel);
            return go;
        }

        private static Transform Site(string name, Transform parent, Vector3 position)
        {
            var go = NewObject(name, parent, parent.gameObject.scene);
            Undo.RecordObject(go.transform, UndoLabel);
            go.transform.position = position;
            return go.transform;
        }

        private static void Missing(Transform parent, string label, string stage)
        {
            var marker = NewObject("待摆位_" + label + "_缺少" + stage, parent, parent.gameObject.scene);
            Undo.RecordObject(marker, UndoLabel);
            marker.SetActive(false);
            Debug.LogWarning(label + " 缺少真实 " + stage + " 锚点：只建立禁用空标记，没有创建原点特效。补齐真实地点后选中锚点，使用剧情/局部环境子菜单添加水雾或烛光；重新运行首次接入不会覆盖视图。", marker);
        }

        private static Light LocalLight(string name, Transform parent, Vector3 offset,
            SpotlightEnvironmentConfig config, float intensity, float range)
        {
            var go = NewObject(name, parent, parent.gameObject.scene);
            Undo.RecordObject(go.transform, UndoLabel);
            go.transform.localPosition = offset;
            var light = Undo.AddComponent<Light>(go);
            Undo.RecordObject(light, UndoLabel);
            light.type = LightType.Point;
            light.color = config.FlameColor;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        private static ParticleSystem Particles(string name, Transform parent, Material material, Vector3 offset,
            Vector3 box, float rate, float lifetime, float size, Color color, Vector3 velocity)
        {
            var go = NewObject(name, parent, parent.gameObject.scene);
            Undo.RecordObject(go.transform, UndoLabel);
            go.transform.localPosition = offset;
            var particles = Undo.AddComponent<ParticleSystem>(go);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Undo.RecordObject(particles, UndoLabel);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * .7f, size);
            main.startColor = color;
            main.maxParticles = 512;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = rate;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var motion = particles.velocityOverLifetime;
            motion.enabled = true;
            motion.space = ParticleSystemSimulationSpace.World;
            motion.x = velocity.x;
            motion.y = velocity.y;
            motion.z = velocity.z;
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Undo.RecordObject(renderer, UndoLabel);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        private static SpotlightEnvironmentConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<SpotlightEnvironmentConfig>(ConfigPath);
            if (config != null) return config;
            RequireUnusedPath(ConfigPath);
            EnsureFolder("Assets/_Project/Data/Narrative");
            config = ScriptableObject.CreateInstance<SpotlightEnvironmentConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        private static Material LoadOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
            {
                if (material.shader == null || !material.shader.isSupported)
                    throw new InvalidOperationException("已有环境粒子材质的 Shader 不可用；保留资产，请先修复。");
                return material;
            }
            RequireUnusedPath(MaterialPath);
            var shader = Shader.Find(ParticleShader);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("缺少可用 URP Particles/Unlit Shader；停止接入，不修改渲染管线。");
            EnsureFolder(MaterialFolder);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null)
            {
                RequireUnusedPath(TexturePath);
                // 编辑器 API 序列化程序生成的小纹理，不覆盖任何已有美术图像。
                texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "SpotlightSoftParticle", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        float distance = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                        float alpha = Mathf.Clamp01(1f - distance);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                    }
                texture.Apply();
                AssetDatabase.CreateAsset(texture, TexturePath);
            }
            material = new Material(shader) { name = "SpotlightSoftParticle", renderQueue = (int)RenderQueue.Transparent };
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static void RequireUnusedPath(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path))
                throw new InvalidOperationException("目标路径已被其他资产占用，拒绝覆盖：" + path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
