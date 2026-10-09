// 职责：把编辑器里所有已加载场景中的 3D 网格渲染器换成「21Days/ToonLit」（三渲二）材质。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：现有编辑器工具里 (AssetAuditWindow 扫资产 / BuildScript 打包 / ProjectStructureMenu 建目录 /
//      MainSceneShortcut 切场景 / BattleWhiteboxBuilder 建白盒) 没有一个管材质替换。
//   2. 扩展不行：这是「按对象类型批量换材质 + 生成配套材质资产」的独立工序，塞进上面任何一个都名实不符。
//
// 为什么做成工具而不是一次性脚本：换材质要能反复跑。调了着色器参数、加了新环境资产、想退回 PBR 对照，
//   都得再来一次；写成菜单项，参数就固定留在仓库里，别人也能复现同一套画法（一次性脚本会随会话消失）。
//
// 干什么：
//   · 遍历**所有已加载场景**（SceneManager 里 isLoaded 的每一个）的 MeshRenderer / SkinnedMeshRenderer
//     （跳过 SpriteRenderer、UI、Trail 等）。不只看激活场景：Play 流程里 Boot 是激活场景、SampleScene 是 Additive 加载的，
//     只看激活场景会漏掉整个玩法场景。
//   · 每个材质按「源材质」生成一份 ToonLit 材质资产，落在 Assets/_Project/Art/Materials/Toon/，
//     把 _BaseMap / _BaseColor / 抠图阈值搬过去，并按源材质是否抠图决定开不开 _ALPHATEST_ON。
//   · 同一份源材质只生成一次，多个物体共用同一份 Toon 材质（否则 34 个 glb 材质会炸成几百份）。
//   · 材质资产按名字复用，但属性每次都按「源材质 + 转换规则」重写（理由见 BuildToonMaterial）。
//   · 植物材质（IsFoliageMaterial）设成双面（_Cull = Off）：草卡、叶片卡是单面开放网格，单面剔除会丢掉背对相机的一半。
//   · 临时调色旋钮（_GradeHueShift / _GradeSaturation / _GradeValue）每次都从 ToonMaterialMap 的调色表写进材质，
//     表是权威来源，两个转换菜单都不清它。材质上拖好的值用「三渲二：材质上的调色旋钮存回对应表」存回表里；
//     转换时发现材质上的值和表不一致会按表覆盖并逐个点名提醒。这是美术新贴图到位前的过渡方案，新贴图到了就把表里的条目归零或删掉。
//
// 退了怎么办：「删掉重新生成」菜单会先把场景里的槽换回源材质；也可以手工把材质换成 glb / Materials 里的原件。
//   glb 内部的子材质一个都没动，随时能换回去对照。
//
// 不碰什么：Assets/_Project/Art/scene/env_well.glb 里的材质与预制体本身。换的是场景里渲染器指向哪个材质（prefab override），
//   所以美术重导 glb 不会把这套 Toon 材质冲掉，也不会污染模型资产。

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 探索场景的三渲二材质转换。菜单 <c>21Days/表现/…</c>，作用于编辑器里**所有已加载的场景**，不扫全工程资产。
    /// </summary>
    public static class ToonMaterialConverter
    {
        /// <summary>着色器资产路径。用 AssetDatabase 按路径取，不靠 Shader.Find——后者在打包后不可靠，且这里的路径是仓库内的固定位置。</summary>
        private const string ToonShaderPath = "Assets/_Project/Art/Shaders/ToonLit.shader";

        /// <summary>生成材质落在这里。与 Art/Materials 下其它风格目录平级，便于整体替换与回退。</summary>
        private const string OutputFolder = "Assets/_Project/Art/Materials/Toon";

        /// <summary>源材质 ↔ Toon 材质的对应表。重建与回退都靠它，不能删。</summary>
        private const string MapPath = OutputFolder + "/ToonMaterialMap.asset";

        /// <summary>
        /// 生成材质上打的标签前缀，值是来源材质的 <see cref="GlobalObjectId"/>。
        /// **为什么用标签 + GlobalObjectId，而不是资产 guid**：
        ///   · 用标签：对应表是中间文件，会被误删、被写坏（实测踩到过，一次空跑把它清空）；
        ///     标签长在材质资产自己身上，跟着资产走。
        ///   · 用 GlobalObjectId 而不是 guid：glb 里的材质是**子资产**，它们共用宿主文件的 guid，
        ///     `AssetDatabase.GetAssetPath` 对 34 个子材质返回的是同一个 glb 路径、guid 也全一样。
        ///     拿 guid 记录来源必定串味——实测踩到过：`M_Toon_pierre` 的来源被记成了
        ///     `Eevee Grass B Gold.001`，于是「重写属性」把错的材质当来源，怎么跑都不对。
        ///     GlobalObjectId 能把「宿主文件 + 子资产」唯一标识出来，正好治这个。
        /// </summary>
        private const string SourceGuidLabelPrefix = "ToonSource:";

        /// <summary>
        /// 暗部色：冷偏紫，但**整体很浅**。参考风格（明日方舟探索地图）的环境是「手绘贴图 + 轻度明暗」：
        /// 暗部只比亮部冷一点、暗一点，靠贴图自己的手绘明暗出体积。压深了会吃掉贴图细节——
        /// 实测把暗部压到 0.46 亮度时，深色树冠直接糊成黑块，和参考图差得最远。
        /// </summary>
        private static readonly Color ToonShadowColor = new Color(0.80f, 0.83f, 0.92f, 1f);

        /// <summary>
        /// 基色亮度下限（着色器 <c>_AlbedoFloor</c>，线性空间亮度）。着色器按贴图局部平均亮度算增益、rgb 等比提亮，
        /// 色相与贴图内部的明暗层次都不变（见 ToonLit.shader 的 ApplyAlbedoFloor）。
        /// 环境（石、木、泥地、灰盒）给低值，只防「乘一次明暗就纯黑」；植物给高一点，植物该是看得清叶形的颜色。
        /// 实测贴图亮度（线性，取不透明像素均值）：树冠 Shiny_Stylized_Leaves 0.113（青绿 #0A6B5E，不是暗贴图）、
        /// 高草 grass_basecolor 0.19、蕨 cao1 0.03、泥地 0.02、树干 wood 0.02。
        /// </summary>
        private const float EnvironmentAlbedoFloor = 0.12f;

        /// <summary>植物的基色亮度下限，含义同 <see cref="EnvironmentAlbedoFloor"/>。</summary>
        private const float FoliageAlbedoFloor = 0.22f;

        /// <summary>
        /// 非植物材质的背光面 / 投影里的最低受光量（着色器 <c>_LitMin</c>）。**别往上调**，实测过：
        /// 参考图（明日方舟探索地图）地面投影与亮部的明度比约 0.54（投影 #6F6154 / 亮部 #CFBDA0），墙下深影约 0.37；
        /// 本场景玩法机位下 0.35 时这个比值是 0.67，调到 0.5 变成 0.76——投影更浅、离参考更远。
        /// 参考图的投影偏暖褐（色相 19°～29°），本着色器的 _ShadowColor 偏冷，「发闷」来自色相不对，不是太深。
        /// 植物单独用 0.45（见 BuildToonMaterial）：植物不接收投影、开了半兰伯特，要的是看清叶形。
        /// </summary>
        private const float EnvironmentLitMin = 0.35f;

        /// <summary>
        /// 源材质的贴图属性名，按优先级排。场景里有两族材质，键名不一样，实测确认过：
        /// URP/Lit 是 <c>_BaseMap</c>，GLTFUtility 的 <c>Standard (Metallic)</c> 是 <c>_MainTex</c>。
        /// </summary>
        private static readonly string[] SourceTextureProperties = { "_BaseMap", "_MainTex", "_BaseColorMap" };

        /// <summary>源材质的颜色属性名，同样两族：URP/Lit <c>_BaseColor</c>、GLTFUtility <c>_Color</c>。</summary>
        private static readonly string[] SourceColorProperties = { "_BaseColor", "_Color" };

        /// <summary>
        /// 允许转换的源着色器。**白名单而不是黑名单**：场景里凡是 MeshRenderer 都会被遍历到，
        /// 实测混进来过 TextMeshPro 的 SDF 字体材质（3D 文字用的是 MeshRenderer），它靠一堆专属属性工作，
        /// 换成 ToonLit 会直接渲染不出来。只认这两族受光影的着色器，其余一律跳过。
        /// </summary>
        private static readonly string[] AllowedSourceShaders =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "GLTFUtility/URP/Standard (Metallic)",
        };

        private const string ConvertMenuPath = "21Days/表现/三渲二：环境转 Toon 材质";
        private const string RebuildMenuPath = "21Days/表现/三渲二：删掉重新生成（改了转换规则后用）";
        private const string RelabelMenuPath = "21Days/表现/三渲二：重建来源标签（标签写坏时用）";
        private const string SaveGradesMenuPath = "21Days/表现/三渲二：材质上的调色旋钮存回对应表";

        /// <summary>着色器里三个临时调色旋钮的属性名与中性值（色相 0、饱和度 1、明度 1）。</summary>
        private const string GradeHueProperty = "_GradeHueShift";
        private const string GradeSaturationProperty = "_GradeSaturation";
        private const string GradeValueProperty = "_GradeValue";

        [MenuItem(ConvertMenuPath, false, 400)]
        public static void ConvertScene()
        {
            // 逐槽诊断写文件。MCP 读控制台会截断（几十行以上就看不全），而排查转换问题恰恰要看全量配对，
            // 所以不走日志。文件在工程根的 Temp/ 下（生成物目录，不进仓库），排查完可以删。
            var diagnostics = new List<string>();
            ConvertSceneCore(diagnostics);

            if (diagnostics.Count > 0)
            {
                string path = System.IO.Path.Combine(
                    System.IO.Directory.GetParent(Application.dataPath).FullName,
                    "Temp", "toon-conversion-diagnostics.txt");
                System.IO.File.WriteAllLines(path, diagnostics.ToArray());
            }
        }

        private static void ConvertSceneCore(List<string> diagnostics)
        {
            Shader toonShader = AssetDatabase.LoadAssetAtPath<Shader>(ToonShaderPath);
            if (toonShader == null)
            {
                Debug.LogError("找不到三渲二着色器：" + ToonShaderPath + "，转换中止。");
                return;
            }

            EnsureOutputFolder();

            ToonMaterialMap map = LoadOrCreateMap();
            // 只清配对表。调色表是人写的权威数据，转换从不清它（见 ToonMaterialMap 文件头）。
            map.ClearPairs();
            var gradeOverwrites = new List<string>();

            // 源材质 → 生成出来的 Toon 材质。同一份源材质被多个渲染器用时只生成一次。
            var generated = new Dictionary<Material, Material>();
            var problems = new List<string>();
            var skippedShaders = new Dictionary<string, int>();
            var changedScenes = new HashSet<UnityEngine.SceneManagement.Scene>();
            int skippedCount = 0;
            int rendererCount = 0;
            int slotCount = 0;
            int reusedCount = 0;
            int refreshedCount = 0;

            foreach (Renderer renderer in EnumerateMeshRenderers())
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                rendererCount++;
                bool changed = false;

                // 预制体实例：源材质直接问预制体要，不靠名字。glb 的材质是**子资产**，
                // AssetDatabase.FindAssets 根本索引不到它们（实测：搜 pierre 找不到，
                // 但 LoadAllAssetsAtPath(glb) 里有 34 个材质），所以名字回溯对 glb 是死路。
                Material[] prefabSourceMaterials = ReadPrefabSourceMaterials(renderer);

                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    slotCount++;

                    if (source == null)
                    {
                        problems.Add(renderer.name + "[" + i + "]：材质槽为空");
                        continue;
                    }

                    if (source.shader == toonShader)
                    {
                        // 已经换过的槽：解析出它的源材质，按当前规则把属性重写一遍。
                        // 不重写的话，改了着色器默认值 / 抠图判据 / 调色板后重复跑转换，场景里一点变化都没有
                        // （实测踩到过：跑完显示「新生成 0 份」，参数压根没落地）。
                        Material knownSource = ResolveSourceMaterial(source, prefabSourceMaterials, i);
                        if (knownSource == null)
                        {
                            // 查不到来源就原样留着并点名——猜一个源材质塞进去比留着更糟。
                            problems.Add(renderer.name + "[" + i + "]：" + source.name
                                         + " 已是 Toon 材质但查不到源材质，属性未重写");
                            diagnostics.Add("MISS " + renderer.name + "[" + i + "] toon=" + source.name
                                            + " label=" + ReadSourceGuidLabel(source)
                                            + " labelResolvesTo=" + DescribeMaterial(ReadSourceMaterialLabel(source))
                                            + " prefabSlot=" + DescribeSlot(prefabSourceMaterials, i)
                                            + " nameLookup=" + DescribeMaterial(FindSourceMaterialByName(SourceNameOf(source.name))));
                            continue;
                        }

                        bool ignored;
                        Material rewritten = BuildToonMaterial(knownSource, toonShader, map, gradeOverwrites, out ignored);
                        refreshedCount++;
                        diagnostics.Add("REWRITE " + renderer.name + "[" + i + "] toon=" + source.name
                                        + " source=" + knownSource.name
                                        + " " + DescribeToonState(rewritten)
                                        + " gid=" + GlobalIdOf(knownSource));
                        map.Set(GlobalIdOf(knownSource), GlobalIdOf(source));
                        continue;
                    }

                    if (!IsAllowedSourceShader(source.shader))
                    {
                        // 不是「受光影的 3D 材质」就原样留着：字体、特效、贴花各有着色器，换了会直接坏掉。
                        skippedCount++;
                        int seen;
                        skippedShaders.TryGetValue(source.shader.name, out seen);
                        skippedShaders[source.shader.name] = seen + 1;
                        continue;
                    }

                    Material toon;
                    if (!generated.TryGetValue(source, out toon))
                    {
                        bool alreadyExisted;
                        toon = BuildToonMaterial(source, toonShader, map, gradeOverwrites, out alreadyExisted);
                        if (toon == null)
                        {
                            problems.Add(renderer.name + "[" + i + "]：" + source.name + " 转换失败");
                            continue;
                        }

                        generated[source] = toon;
                        if (alreadyExisted)
                        {
                            reusedCount++;
                        }
                    }

                    materials[i] = toon;
                    changed = true;
                    diagnostics.Add("CONVERT " + renderer.name + "[" + i + "] toon=" + toon.name
                                    + " source=" + source.name
                                    + " " + DescribeToonState(toon));

                    // 落账：源材质 → Toon 材质的 GlobalObjectId。表只是可读索引，权威来源是材质上的标签。
                    map.Set(GlobalIdOf(source), GlobalIdOf(toon));
                }

                if (changed)
                {
                    // 记进 Undo，用户在编辑器里 Ctrl+Z 就能整体退回这一批。
                    Undo.RecordObject(renderer, "环境转 Toon 材质");
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                    changedScenes.Add(renderer.gameObject.scene);
                }
            }

            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(map);
            // 改过槽的场景要标记为脏，不然用户切场景时会被「要不要保存」问懵。
            // 按渲染器所在的场景逐个标，不只标激活场景（多场景叠加加载时改的往往是非激活的那个）。
            foreach (UnityEngine.SceneManagement.Scene scene in changedScenes)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            }

            var report = new StringBuilder();
            report.Append("[三渲二] 转换完成：")
                  .Append(rendererCount).Append(" 个网格渲染器、")
                  .Append(slotCount).Append(" 个材质槽；新生成 ")
                  .Append(generated.Count - reusedCount).Append(" 份，复用已有 ")
                  .Append(reusedCount).Append(" 份，重写已转换 ")
                  .Append(refreshedCount).Append(" 份；跳过 ")
                  .Append(skippedCount).Append(" 个非环境材质槽（");
            foreach (KeyValuePair<string, int> pair in skippedShaders)
            {
                report.Append(pair.Key).Append(" ×").Append(pair.Value).Append("、");
            }

            report.Append("）。");
            if (problems.Count > 0)
            {
                report.Append(" 有 ").Append(problems.Count).Append(" 处跳过，见下方警告。");
            }

            Debug.Log(report.ToString());
            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogWarning("[三渲二] 跳过 " + problems[i]);
            }

            for (int i = 0; i < gradeOverwrites.Count; i++)
            {
                Debug.LogWarning("[三渲二] " + gradeOverwrites[i] + " 上的调色旋钮和调色表不一致，已按调色表覆盖。"
                                 + "要保留材质上拖的值，先跑「" + SaveGradesMenuPath + "」再转换。");
            }
        }

        /// <summary>
        /// 删掉生成目录里的全部 Toon 材质后重跑转换。
        /// 存在的理由：**改转换规则本身**（换属性键名、改抠图判据、改剔除）时要从零重来，
        /// 确保产物里不残留任何旧规则留下的键（比如已经从着色器里删掉的属性）。
        /// <para>
        /// 顺序是硬性的：**先把场景里的槽换回源材质，再删旧资产**。反过来就会把槽删成 None
        /// （实测踩到过：glb 那批槽全变 None，只能把场景从 git 重新加载才救回来）。
        /// </para>
        /// </summary>
        [MenuItem(RebuildMenuPath, false, 402)]
        public static void RebuildAll()
        {
            Shader toonShader = AssetDatabase.LoadAssetAtPath<Shader>(ToonShaderPath);
            if (toonShader == null)
            {
                Debug.LogError("找不到三渲二着色器：" + ToonShaderPath + "，重建中止。");
                return;
            }

            int restored = RestoreSourceMaterials(toonShader);
            if (restored == 0 && HasToonMaterialInScene(toonShader))
            {
                Debug.LogError("[三渲二] 场景里有 Toon 材质，却一个槽都没能换回源材质。"
                               + "为避免把场景删坏，重建中止——先手动确认这些材质上的来源标签还在不在。");
                return;
            }

            ToonMaterialMap map = LoadOrCreateMap();
            // 删材质之前先查一遍：材质上拖过、但没存回调色表的旋钮值，删了就找不回来，点名提醒。
            // 不中止——调色表才是权威来源，按表重建是这个菜单的本意。
            List<string> unsaved = FindGradesNotInMap(map);
            for (int i = 0; i < unsaved.Count; i++)
            {
                Debug.LogWarning("[三渲二] 材质上的调色旋钮与调色表不一致，重建后按调色表的值：" + unsaved[i]
                                 + "。要保留材质上的值，先跑「" + SaveGradesMenuPath + "」再重建。");
            }

            // 只清配对表；调色表（人写的权威数据）不动，这份资产也不在下面的 t:Material 删除范围内。
            map.ClearPairs();
            EditorUtility.SetDirty(map);

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { OutputFolder });
            int removed = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                if (AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guids[i])))
                {
                    removed++;
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("[三渲二] 重建：把 " + restored + " 个槽换回源材质，删掉 " + removed + " 份旧材质，接着重新生成。");
            ConvertScene();
        }

        /// <summary>已加载场景里还有没有 ToonLit 材质的槽（重建前的安全检查用）。</summary>
        private static bool HasToonMaterialInScene(Shader toonShader)
        {
            foreach (Renderer renderer in EnumerateMeshRenderers())
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials == null)
                {
                    continue;
                }

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].shader == toonShader)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 按生成材质上的来源标签，把场景里指向 Toon 材质的槽换回它的源材质。返回换回去的槽数。
        /// 查不到的槽**不动**并点名——这种情况多半是标签丢了（比如手工换过材质），
        /// 宁可留着 Toon 材质让人看见，也不能猜一个源材质塞进去。
        /// </summary>
        private static int RestoreSourceMaterials(Shader toonShader)
        {
            int restored = 0;
            var unmapped = new List<string>();
            var changedScenes = new HashSet<UnityEngine.SceneManagement.Scene>();

            foreach (Renderer renderer in EnumerateMeshRenderers())
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material current = materials[i];
                    if (current == null || current.shader != toonShader)
                    {
                        continue;
                    }

                    Material source = ReadSourceMaterialLabel(current);

                    if (source == null)
                    {
                        unmapped.Add(current.name);
                        continue;
                    }

                    materials[i] = source;
                    changed = true;
                    restored++;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                    changedScenes.Add(renderer.gameObject.scene);
                }
            }

            AssetDatabase.SaveAssets();
            foreach (UnityEngine.SceneManagement.Scene scene in changedScenes)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            }

            for (int i = 0; i < unmapped.Count; i++)
            {
                Debug.LogWarning("[三渲二] 生成材质上没有来源标签，这个槽保持原样：" + unmapped[i]);
            }

            return restored;
        }

        /// <summary>
        /// 按源材质生成一份 ToonLit 材质资产（或复用同名的已有资产）。
        /// </summary>
        /// <param name="map">账本：从它的调色表读这份源材质的临时调色值。</param>
        /// <param name="gradeOverwrites">材质上原有的调色值和调色表不一致、被表覆盖时，把材质名记进来（转换结束时统一提醒）。</param>
        /// <param name="alreadyExisted">产物是复用已有资产（true）还是这次新建的（false）。</param>
        private static Material BuildToonMaterial(Material source, Shader toonShader, ToonMaterialMap map,
                                                  List<string> gradeOverwrites, out bool alreadyExisted)
        {
            string assetPath = OutputFolder + "/M_Toon_" + Sanitize(source.name) + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            bool reuseExisting = existing != null;
            Material material;
            if (reuseExisting)
            {
                // 已经有同名资产就复用，但**属性照样按源材质重写一遍**：产物必须只由「源材质 + 转换规则」决定。
                // 只复用不重写的话，改了转换规则后旧产物会一直沿用旧规则（实测踩到：上一轮误开的抠图留在材质上，
                // 重跑也带不掉），而且两份材质会悄悄长得不一样。
                material = existing;
            }
            else
            {
                material = new Material(toonShader);
                material.name = "M_Toon_" + Sanitize(source.name);
            }

            // 贴图与颜色照搬。键名在两族材质里不同（见上面的属性名表），所以逐个试；搬不到就留 Toon 材质的默认值。
            Texture sourceTexture = null;
            string sourceTextureProperty = null;
            for (int i = 0; i < SourceTextureProperties.Length; i++)
            {
                string property = SourceTextureProperties[i];
                if (source.HasProperty(property) && source.GetTexture(property) != null)
                {
                    sourceTexture = source.GetTexture(property);
                    sourceTextureProperty = property;
                    break;
                }
            }

            if (sourceTexture != null)
            {
                material.SetTexture("_BaseMap", sourceTexture);
                // 平铺 / 偏移一起搬。不搬的话 UV 缩放在 1 以外或偏移不为 0 的物体会跳图——
                // 地面那张网格砖就是靠它铺满的。
                Vector2 scale = source.GetTextureScale(sourceTextureProperty);
                Vector2 offset = source.GetTextureOffset(sourceTextureProperty);
                material.SetTextureScale("_BaseMap", scale);
                material.SetTextureOffset("_BaseMap", offset);
            }

            Color baseColor = Color.white;
            for (int i = 0; i < SourceColorProperties.Length; i++)
            {
                if (source.HasProperty(SourceColorProperties[i]))
                {
                    baseColor = source.GetColor(SourceColorProperties[i]);
                    break;
                }
            }

            material.SetColor("_BaseColor", baseColor);

            float cutoff;
            bool useAlphaClip = ReadSourceAlphaClip(source, sourceTexture != null, out cutoff);
            if (useAlphaClip)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", cutoff);
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetFloat("_AlphaClip", 0f);
            }

            // 三渲二自己的调色板**每次都由这里写死**，不沿用产物上的旧值。
            // 理由：这些参数只由转换规则决定，不该随上一次的调试残留漂移——实测踩到过：
            // 调试时把 _ShadowColor 设成纯红、_AmbientStrength 设成 0，重跑转换后红色照样粘在产物上，
            // 因为「搬源材质属性」这套逻辑根本不碰这些键。要调色就改这里的常量（或事后手工调，但下次转换会覆盖）。
            //
            // 这一版的口径按参考风格（明日方舟探索地图）定：**手绘贴图 + 轻度明暗**，不是硬边平涂。
            // 所以暗部很浅、过渡偏软、环境不加高光/边缘光，也不描边（着色器里已没有描边），立体感交给贴图本身与接触阴影。
            material.SetColor("_ShadowColor", ToonShadowColor);
            material.SetFloat("_ShadowThreshold", 0f);
            material.SetFloat("_RampSmooth", 0.08f);
            material.SetFloat("_AmbientStrength", 0.15f);
            material.SetColor("_SpecColor2", Color.white);
            material.SetFloat("_SpecThreshold", 0.9f);
            material.SetFloat("_SpecSmooth", 0.02f);
            material.SetFloat("_SpecIntensity", 0f);
            material.SetColor("_RimColor", Color.white);
            material.SetFloat("_RimPower", 6f);
            material.SetFloat("_RimIntensity", 0f);

            // 叶片类材质：双面 + 关实时阴影接收 + 开半兰伯特。
            // 判据用材质名（这些是 glb 里带过来的名字）——几条原因都实测过：
            //   · 草卡、叶片卡是单面开放网格。单面剔除时背对相机的那一半直接不画；
            //     之前还有一个没开关的反壳描边 Pass 把这些背面涂成近黑，就是截图里那一大片深紫黑色块。
            //     双面后背面法线在着色器里按 VFACE 翻过来，正反两面受光一致。
            //   · 开着阴影接收时，每张叶片卡都给后面的卡投一层自阴影，整棵树压成近黑；
            //   · 叶片法线五花八门，大量与光接近垂直（NdotL≈0），普通兰伯特会全落进暗部，同样黑成一坨。
            // 将来正式低模进来，把这条判据换成材质上的勾选框即可。
            if (IsFoliageMaterial(source.name))
            {
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                material.EnableKeyword("_IGNORE_SHADOWS_ON");
                material.EnableKeyword("_HALF_LAMBERT_ON");
                material.SetFloat("_IgnoreShadows", 1f);
                material.SetFloat("_HalfLambert", 1f);
                // 叶片的暗部下限给高一点：植物本来就该是能看清叶形的颜色，不该压到近黑。
                material.SetFloat("_LitMin", 0.45f);
                material.SetFloat("_AlbedoFloor", FoliageAlbedoFloor);
            }
            else
            {
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
                material.DisableKeyword("_IGNORE_SHADOWS_ON");
                material.DisableKeyword("_HALF_LAMBERT_ON");
                material.SetFloat("_IgnoreShadows", 0f);
                material.SetFloat("_HalfLambert", 0f);
                material.SetFloat("_LitMin", EnvironmentLitMin);
                material.SetFloat("_AlbedoFloor", EnvironmentAlbedoFloor);
            }

            // 临时调色旋钮：值只从调色表来（表里没有这份源材质就是中性值）。
            // 复用已有资产时先比一下：材质上被人拖过、和表不一致的，按表覆盖并记下来提醒——不静默吃掉别人的调整。
            ToonGradeEntry grade = map.FindGrade(GlobalIdOf(source), source.name);
            float hue = grade == null ? 0f : grade.HueShift;
            float saturation = grade == null ? 1f : grade.Saturation;
            float value = grade == null ? 1f : grade.Value;
            if (reuseExisting && !GradeEquals(material, hue, saturation, value))
            {
                gradeOverwrites.Add(material.name);
            }

            material.SetFloat(GradeHueProperty, hue);
            material.SetFloat(GradeSaturationProperty, saturation);
            material.SetFloat(GradeValueProperty, value);

            if (!reuseExisting)
            {
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                // 复用同名资产时也要把来源标签刷成这次的源材质：名字撞车或旧标签过期时，标签以本次为准。
                EditorUtility.SetDirty(material);
            }

            WriteSourceGuidLabel(material, source);

            alreadyExisted = reuseExisting;
            return material;
        }

        /// <summary>
        /// 源材质开没开抠图、阈值多少。两族材质的判据不一样，**不能混用**：
        /// <list type="bullet">
        /// <item>URP/Lit 族：看 <c>_AlphaClip</c> 开关。<c>_Cutoff</c> 在 URP/Lit 上**永远存在且默认 0.5**，它只是阈值，
        /// 不代表开了抠图——实测踩到过：只看 <c>_Cutoff &gt; 0</c> 时，地砖 M_GroundTile 这种带贴图的不透明材质被一律判成抠图。</item>
        /// <item>GLTFUtility 族：没有 <c>_AlphaClip</c> / <c>_Cutoff</c>，只有 <c>_AlphaCutoff</c>；glTF 的 <c>alphaMode: MASK</c>
        /// 导进来是阈值 &gt; 0，<c>OPAQUE</c> 是 0。</item>
        /// </list>
        /// 前提是源材质有贴图：灰盒的 M_Wall / M_Dark 是纯色材质，没有 alpha 可抠。
        /// 不开的后果也踩过：草卡的透明区会渲染成实心方片。
        /// </summary>
        private static bool ReadSourceAlphaClip(Material source, bool hasTexture, out float cutoff)
        {
            cutoff = 0.5f;
            if (!hasTexture)
            {
                return false;
            }

            if (source.HasProperty("_AlphaClip"))
            {
                if (source.GetFloat("_AlphaClip") < 0.5f)
                {
                    return false;
                }

                if (source.HasProperty("_Cutoff"))
                {
                    cutoff = source.GetFloat("_Cutoff");
                }

                return true;
            }

            if (source.HasProperty("_AlphaCutoff"))
            {
                float value = source.GetFloat("_AlphaCutoff");
                if (value > 0f)
                {
                    cutoff = value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>诊断用：一份 Toon 材质的关键开关（剔除、抠图、基色下限），写进 Temp 下的诊断文件。</summary>
        private static string DescribeToonState(Material toon)
        {
            if (toon == null)
            {
                return "<null>";
            }

            return "cull=" + (UnityEngine.Rendering.CullMode)Mathf.RoundToInt(toon.GetFloat("_Cull"))
                   + " alphaTest=" + toon.IsKeywordEnabled("_ALPHATEST_ON")
                   + " albedoFloor=" + toon.GetFloat("_AlbedoFloor");
        }

        /// <summary>
        /// 资产或子资产的稳定标识。**不要用 AssetDatabase.AssetPathToGUID 代替**：
        /// glb 里的材质是子资产，它们共用宿主 guid，拿 guid 区分不开（详见 SourceGuidLabelPrefix 的注释）。
        /// </summary>
        private static string GlobalIdOf(UnityEngine.Object target)
        {
            return target == null ? string.Empty : UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(target).ToString();
        }

        /// <summary>诊断用：把材质数组里某一槽描述成一行文本。</summary>
        private static string DescribeSlot(Material[] materials, int index)
        {
            if (materials == null)
            {
                return "<no prefab source>";
            }

            if (index >= materials.Length)
            {
                return "<index out of range, len=" + materials.Length + ">";
            }

            return DescribeMaterial(materials[index]);
        }

        /// <summary>诊断用：把一份材质描述成「名字/着色器/guid」。</summary>
        private static string DescribeMaterial(Material material)
        {
            if (material == null)
            {
                return "<null>";
            }

            string path = AssetDatabase.GetAssetPath(material);
            return material.name + "/" + material.shader.name + "/" + AssetDatabase.AssetPathToGUID(path);
        }

        /// <summary>
        /// 是不是叶片类材质（双面 + 关实时阴影接收）。按 glb 带过来的材质名判，只覆盖这份资产的既有命名。
        /// </summary>
        private static bool IsFoliageMaterial(string sourceMaterialName)
        {
            if (string.IsNullOrEmpty(sourceMaterialName))
            {
                return false;
            }

            string[] foliageKeywords = { "Grass", "fern", "Leaf", "叶子", "草", "Leaves", "Plant" };
            for (int i = 0; i < foliageKeywords.Length; i++)
            {
                if (sourceMaterialName.IndexOf(foliageKeywords[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 解析一个「已经是 Toon 材质」的槽原本对应哪份源材质。三级回退，命中即返回：
        /// ① 生成材质上的来源标签（新产物都有）→ ② 预制体原本的材质 → ③ 按名字回溯（第一版灰盒产物）。
        /// 解析出来的来源会补写成标签，下一轮就不用再回溯。
        /// </summary>
        private static Material ResolveSourceMaterial(Material toonMaterial, Material[] prefabSourceMaterials, int slotIndex)
        {
            // 逐级判空用 == null，不用 ??：Unity 对象的伪空（已销毁）只有重载的 == 认得出（csharp-code.md）。
            Material source = ReadSourceMaterialLabel(toonMaterial);
            if (source == null && prefabSourceMaterials != null && slotIndex < prefabSourceMaterials.Length)
            {
                source = prefabSourceMaterials[slotIndex];
            }

            if (source == null)
            {
                source = FindSourceMaterialByName(SourceNameOf(toonMaterial.name));
            }

            if (source != null && string.IsNullOrEmpty(ReadSourceGuidLabel(toonMaterial)))
            {
                WriteSourceGuidLabel(toonMaterial, source);
            }

            return source;
        }

        /// <summary>
        /// 读生成材质上的来源标签并还原成材质对象。**读不到就返回 null，绝不猜**——
        /// 猜错的后果比没有来源更糟（实测：错的来源会让「按新规则重写」把错的材质当基准）。
        /// </summary>
        private static Material ReadSourceMaterialLabel(Material toonMaterial)
        {
            string label = ReadSourceGuidLabel(toonMaterial);
            if (string.IsNullOrEmpty(label))
            {
                return null;
            }

            if (!UnityEditor.GlobalObjectId.TryParse(label, out UnityEditor.GlobalObjectId id))
            {
                return null;
            }

            return UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as Material;
        }

        /// <summary>
        /// 取预制体实例「原本」的材质槽。非预制体实例返回 null。
        /// 用途：反查一个已经被换成 Toon 的槽，它换之前是什么——问预制体比猜名字可靠。
        /// </summary>
        private static Material[] ReadPrefabSourceMaterials(Renderer renderer)
        {
            if (UnityEditor.PrefabUtility.GetPrefabInstanceStatus(renderer) != UnityEditor.PrefabInstanceStatus.Connected)
            {
                return null;
            }

            var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(renderer) as Renderer;
            return source == null ? null : source.sharedMaterials;
        }

        /// <summary>
        /// 把生成材质上当前的临时调色旋钮值存回调色表（权威来源）。
        /// 用法：在材质 Inspector 里拖滑块看效果，满意后跑一次这个菜单，之后两个转换菜单都会按存下的值写回。
        /// 中性值且表里本来就没有的材质不记，免得表里堆满无意义的条目。查不到来源标签的材质点名跳过——不知道它属于谁就不能记。
        /// </summary>
        [MenuItem(SaveGradesMenuPath, false, 404)]
        public static void SaveGradesFromMaterials()
        {
            ToonMaterialMap map = LoadOrCreateMap();
            int saved = 0;
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { OutputFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                Material toon = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (toon == null || !toon.HasProperty(GradeHueProperty))
                {
                    continue;
                }

                Material source = ReadSourceMaterialLabel(toon);
                if (source == null)
                {
                    Debug.LogWarning("[三渲二] " + toon.name + " 没有来源标签，调色值没法存回。");
                    continue;
                }

                float hue = toon.GetFloat(GradeHueProperty);
                float saturation = toon.GetFloat(GradeSaturationProperty);
                float value = toon.GetFloat(GradeValueProperty);
                ToonGradeEntry existing = map.FindGrade(GlobalIdOf(source), source.name);
                bool neutral = GradeEquals(toon, 0f, 1f, 1f);
                if (existing == null && neutral)
                {
                    continue;
                }

                map.SetGrade(GlobalIdOf(source), source.name, hue, saturation, value);
                saved++;
            }

            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            Debug.Log("[三渲二] 已把 " + saved + " 份材质的调色旋钮存回调色表（" + MapPath + "）。");
        }

        /// <summary>材质上的三个调色旋钮是否等于给定值（容差 0.001，滑块拖动的浮点抖动不算改动）。</summary>
        private static bool GradeEquals(Material material, float hue, float saturation, float value)
        {
            if (!material.HasProperty(GradeHueProperty))
            {
                return true;
            }

            return Mathf.Abs(material.GetFloat(GradeHueProperty) - hue) < 0.001f
                   && Mathf.Abs(material.GetFloat(GradeSaturationProperty) - saturation) < 0.001f
                   && Mathf.Abs(material.GetFloat(GradeValueProperty) - value) < 0.001f;
        }

        /// <summary>生成目录里，材质上的调色值与调色表不一致的材质名（「删掉重新生成」删材质前用来提醒）。</summary>
        private static List<string> FindGradesNotInMap(ToonMaterialMap map)
        {
            var result = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { OutputFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                Material toon = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[i]));
                Material source = toon == null ? null : ReadSourceMaterialLabel(toon);
                if (source == null)
                {
                    continue;
                }

                ToonGradeEntry grade = map.FindGrade(GlobalIdOf(source), source.name);
                float hue = grade == null ? 0f : grade.HueShift;
                float saturation = grade == null ? 1f : grade.Saturation;
                float value = grade == null ? 1f : grade.Value;
                if (!GradeEquals(toon, hue, saturation, value))
                {
                    result.Add(toon.name);
                }
            }

            return result;
        }

        /// <summary>
        /// 清掉生成材质上的全部来源标签，然后重跑转换重新建立。
        /// 用在标签被写坏、或转换规则改到「认不出来」的时候：标签是可靠来源，一旦写错，
        /// 后面的解析会一直沿着错的走（实测踩到过：pierre 的材质挂着别的材质的来源标签）。
        /// 清掉之后来源改由「预制体原本的材质 / 按名字回溯」重新判定，也就是回到没标签时的路径。
        /// </summary>
        [MenuItem(RelabelMenuPath, false, 403)]
        public static void ClearLabelsAndRelabel()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { OutputFolder });
            int cleared = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (material == null)
                {
                    continue;
                }

                var kept = new List<string>();
                string[] labels = AssetDatabase.GetLabels(material);
                bool hadSourceLabel = false;
                for (int l = 0; l < labels.Length; l++)
                {
                    if (labels[l].StartsWith(SourceGuidLabelPrefix, System.StringComparison.Ordinal))
                    {
                        hadSourceLabel = true;
                    }
                    else
                    {
                        kept.Add(labels[l]);
                    }
                }

                if (hadSourceLabel)
                {
                    AssetDatabase.SetLabels(material, kept.ToArray());
                    cleared++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[三渲二] 已清掉 " + cleared + " 份材质上的来源标签，接着重新判定来源。");
            ConvertScene();
        }

        /// <summary>读生成材质上的来源标签，返回 GlobalObjectId 字符串；没有标签返回空串。</summary>
        private static string ReadSourceGuidLabel(Material toonMaterial)
        {
            string[] labels = AssetDatabase.GetLabels(toonMaterial);
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i].StartsWith(SourceGuidLabelPrefix, System.StringComparison.Ordinal))
                {
                    return labels[i].Substring(SourceGuidLabelPrefix.Length);
                }
            }

            return string.Empty;
        }

        /// <summary>把来源材质的 GlobalObjectId 写成标签，覆盖旧的同名标签。子资产（glb 里的材质）也唯一。</summary>
        private static void WriteSourceGuidLabel(Material toonMaterial, Material source)
        {
            if (source == null)
            {
                return;
            }

            var labels = new List<string>(AssetDatabase.GetLabels(toonMaterial));
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                if (labels[i].StartsWith(SourceGuidLabelPrefix, System.StringComparison.Ordinal))
                {
                    labels.RemoveAt(i);
                }
            }

            labels.Add(SourceGuidLabelPrefix + UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(source));
            AssetDatabase.SetLabels(toonMaterial, labels.ToArray());
        }

        /// <summary><c>M_Toon_pierre</c> → <c>pierre</c>。名字不合规则时原样返回。</summary>
        private static string SourceNameOf(string toonMaterialName)
        {
            const string prefix = "M_Toon_";
            return toonMaterialName != null && toonMaterialName.StartsWith(prefix, System.StringComparison.Ordinal)
                ? toonMaterialName.Substring(prefix.Length)
                : toonMaterialName;
        }

        /// <summary>
        /// 按名字回溯源材质。**只用于给老产物补标签**（第一版转换没打标签）。
        /// 找不到就返回 null，调用方按「没有来源」处理，不要猜。
        /// </summary>
        private static Material FindSourceMaterialByName(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
            {
                return null;
            }

            string[] guids = AssetDatabase.FindAssets(sourceName + " t:Material");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Material candidate = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (candidate != null && candidate.name == sourceName && IsAllowedSourceShader(candidate.shader))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>源着色器在不在白名单里。不在就原样留着——字体、特效、贴花各有专属着色器，换掉会直接坏。</summary>
        private static bool IsAllowedSourceShader(Shader shader)
        {
            for (int i = 0; i < AllowedSourceShaders.Length; i++)
            {
                if (shader.name == AllowedSourceShaders[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>材质名进文件名前把路径分隔符与非法字符换掉，避免生成出不存在的目录。</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Unnamed";
            }

            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = c == '/' || c == '\\' || c == '.';
                for (int j = 0; j < invalid.Length && !bad; j++)
                {
                    bad = c == invalid[j];
                }

                builder.Append(bad ? '_' : c);
            }

            return builder.ToString();
        }

        /// <summary>
        /// 所有已加载场景里的 3D 网格渲染器（含未激活物体）。SpriteRenderer / UI / 线渲染器不在内。
        /// 遍历 SceneManager 的每一个已加载场景，而不只是激活场景：Play 流程里 Boot 是激活场景、
        /// SampleScene 是 Additive 加载的，只看激活场景会漏掉整个玩法场景。
        /// </summary>
        private static IEnumerable<Renderer> EnumerateMeshRenderers()
        {
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int s = 0; s < sceneCount; s++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    Renderer[] renderers = roots[i].GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        if (renderers[r] is MeshRenderer || renderers[r] is SkinnedMeshRenderer)
                        {
                            yield return renderers[r];
                        }
                    }
                }
            }
        }

        private static void EnsureOutputFolder()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "Toon");
            }
        }

        /// <summary>取对应表资产，没有就建一个。表跟着产物一起放在生成目录里，整体进退。</summary>
        private static ToonMaterialMap LoadOrCreateMap()
        {
            var map = AssetDatabase.LoadAssetAtPath<ToonMaterialMap>(MapPath);
            if (map != null)
            {
                return map;
            }

            map = ScriptableObject.CreateInstance<ToonMaterialMap>();
            AssetDatabase.CreateAsset(map, MapPath);
            return map;
        }
    }
}
