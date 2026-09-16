// 职责：菜单「21Days/工程/资产体检」——只读扫描 Assets/_Project/，列出四类会静默坑人的资产问题。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：project-lint 只看 .cs 的文本行，看不到资产；gc_scan 只看 harness 内部的引用。
//      资产层面的「缺 .meta / 丢脚本 / 贴图过大 / 漏进 Addressables」没有任何现成检查。
//   2. 扩展不行：ProjectStructureMenu 管「建骨架」，BuildScript 管打包，职责都对不上。
//
// 只报告，不修复：这四类问题的正确修法各不相同（有的要 git 补 .meta，有的要人判断该删还是该接），
// 自动修复很容易把「该有人看一眼」的事悄悄抹平。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 资产体检窗口。四项检查：
    /// <list type="number">
    /// <item>缺 <c>.meta</c> 的文件——提交后别人拉下来 GUID 重生成，引用静默全断。</item>
    /// <item>场景 / 预制体里的 <c>Missing (Mono Script)</c>——脚本删了或 GUID 变了，运行时才炸。</item>
    /// <item>分辨率大于 <see cref="MaxTextureSize"/> 的贴图——手机上显存与包体的主要来源。</item>
    /// <item><c>Prefabs/UI/*.prefab</c> 没进任何 Addressables 组——<c>OpenAsync</c> 运行时才报找不到地址。</item>
    /// </list>
    /// 每行**双击**定位到 Project 窗口。扫描是只读的，不改任何资产、不开场景。
    /// </summary>
    public sealed class AssetAuditWindow : EditorWindow
    {
        /// <summary>超过这个边长就报出来（像素）。2048 是移动端比较稳的单张上限。</summary>
        public const int MaxTextureSize = 2048;

        private const string ProjectFolder = "Assets/_Project";
        private const string UIPrefabFolder = "Assets/_Project/Prefabs/UI";

        /// <summary>
        /// 匹配序列化文件里的脚本引用。两种缺失形态：
        /// <c>{fileID: 0}</c>（组件的脚本引用是空的）和 guid 在工程里查不到（脚本被删或换了 GUID）。
        /// </summary>
        private static readonly Regex ScriptReferencePattern = new Regex(
            @"m_Script:\s*\{fileID:\s*(-?\d+)(?:\s*,\s*guid:\s*([0-9a-fA-F]{32}))?",
            RegexOptions.Compiled);

        private readonly List<Finding> missingMeta = new List<Finding>();
        private readonly List<Finding> missingScripts = new List<Finding>();
        private readonly List<Finding> oversizedTextures = new List<Finding>();
        private readonly List<Finding> unaddressedViews = new List<Finding>();

        private Vector2 scroll;
        private string summary = "还没扫描。";
        private bool scanned;

        [MenuItem("21Days/工程/资产体检", false, 210)]
        public static void Open()
        {
            AssetAuditWindow window = GetWindow<AssetAuditWindow>(false, "资产体检", true);
            window.minSize = new Vector2(560f, 320f);
            window.Show();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("扫描", GUILayout.Width(120f), GUILayout.Height(24f)))
                {
                    Scan();
                }

                GUILayout.Label(summary, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.HelpBox(
                "只读扫描，不会修改任何资产。每行双击可在 Project 窗口里定位。\n"
                + "贴图那一项看的是**导入后**的尺寸（受 Max Size 影响），不是源文件尺寸。\n"
                + "扫描全程跑在主线程：资产上千之后编辑器会卡住一阵，进度条不动也属正常，等它跑完。",
                MessageType.None);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawSection("缺 .meta 的文件", missingMeta,
                "资产和 .meta 必须成对提交。移动 / 删除 / 改名用 git mv / git rm 连 .meta 一起。");
            DrawSection("脚本引用丢失（Missing Mono Script）", missingScripts,
                "脚本被删或 GUID 变了。补回脚本，或在场景 / 预制体里把那个坏组件摘掉。");
            DrawSection($"分辨率大于 {MaxTextureSize} 的贴图", oversizedTextures,
                "在 Inspector 的 Max Size 里压下来，或把图拆小；手机包体和显存主要花在这儿。");
            DrawSection("没进 Addressables 的 UI 预制体", unaddressedViews,
                "面板预制体必须在 Addressables 里，且**地址等于面板类名**，否则 OpenAsync 运行时才报错。");

            EditorGUILayout.EndScrollView();
        }

        private void DrawSection(string title, List<Finding> findings, string fix)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"{title}（{findings.Count}）", EditorStyles.boldLabel);

            if (!scanned)
            {
                return;
            }

            if (findings.Count == 0)
            {
                EditorGUILayout.LabelField("    没有问题", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField($"    修法：{fix}", EditorStyles.miniLabel);
            for (int i = 0; i < findings.Count; i++)
            {
                DrawRow(findings[i]);
            }
        }

        private static void DrawRow(Finding finding)
        {
            var content = new GUIContent($"    {finding.AssetPath}    —— {finding.Detail}");
            Rect rect = GUILayoutUtility.GetRect(content, EditorStyles.label, GUILayout.ExpandWidth(true));
            EditorGUI.LabelField(rect, content);

            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.clickCount != 2 || !rect.Contains(current.mousePosition))
            {
                return;
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finding.AssetPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }

            current.Use();
        }

        private void Scan()
        {
            missingMeta.Clear();
            missingScripts.Clear();
            oversizedTextures.Clear();
            unaddressedViews.Clear();

            try
            {
                EditorUtility.DisplayProgressBar("资产体检", "检查 .meta…", 0.1f);
                ScanMissingMeta();

                EditorUtility.DisplayProgressBar("资产体检", "检查场景与预制体里的脚本引用…", 0.4f);
                ScanMissingScripts();

                EditorUtility.DisplayProgressBar("资产体检", "检查贴图尺寸…", 0.7f);
                ScanOversizedTextures();

                EditorUtility.DisplayProgressBar("资产体检", "检查 Addressables 覆盖…", 0.9f);
                ScanUnaddressedViews();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            scanned = true;
            int total = missingMeta.Count + missingScripts.Count + oversizedTextures.Count + unaddressedViews.Count;
            summary = total == 0
                ? "四项全过，没有发现问题。"
                : $"共 {total} 条：缺 meta {missingMeta.Count}，丢脚本 {missingScripts.Count}，"
                  + $"贴图过大 {oversizedTextures.Count}，漏进 Addressables {unaddressedViews.Count}。";
        }

        private void ScanMissingMeta()
        {
            string root = Path.Combine(Application.dataPath, "_Project");
            if (!Directory.Exists(root))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string normalized = file.Replace("\\", "/");
                if (normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || IsHidden(normalized))
                {
                    continue;
                }

                if (!File.Exists(file + ".meta"))
                {
                    missingMeta.Add(new Finding(ToAssetPath(normalized), "没有同名 .meta"));
                }
            }
        }

        private void ScanMissingScripts()
        {
            var paths = new List<string>();
            CollectAssetPaths("t:Prefab", paths);
            CollectAssetPaths("t:SceneAsset", paths);

            for (int i = 0; i < paths.Count; i++)
            {
                string assetPath = paths[i];
                string text;
                try
                {
                    text = File.ReadAllText(assetPath);
                }
                catch (IOException)
                {
                    continue;
                }

                // 二进制序列化的场景 / 预制体没法这样扫。本工程是 Force Text（.gitattributes 也按文本处理），
                // 真遇到二进制的就报出来让人去 Editor Settings 里改回文本。
                if (!text.StartsWith("%YAML", StringComparison.Ordinal))
                {
                    missingScripts.Add(new Finding(assetPath, "不是文本序列化，扫不了（Editor Settings → Asset Serialization 改成 Force Text）"));
                    continue;
                }

                int broken = CountBrokenScriptReferences(text);
                if (broken > 0)
                {
                    missingScripts.Add(new Finding(assetPath, $"{broken} 处脚本引用丢失"));
                }
            }
        }

        private static int CountBrokenScriptReferences(string text)
        {
            int broken = 0;
            foreach (Match match in ScriptReferencePattern.Matches(text))
            {
                string guid = match.Groups[2].Value;
                if (string.IsNullOrEmpty(guid))
                {
                    // 没有 guid 说明引用是空的（fileID: 0）。
                    broken++;
                    continue;
                }

                if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                {
                    broken++;
                }
            }

            return broken;
        }

        private void ScanOversizedTextures()
        {
            var paths = new List<string>();
            CollectAssetPaths("t:Texture2D", paths);

            for (int i = 0; i < paths.Count; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
                if (texture == null)
                {
                    continue;
                }

                if (texture.width > MaxTextureSize || texture.height > MaxTextureSize)
                {
                    oversizedTextures.Add(new Finding(paths[i], $"{texture.width}×{texture.height}"));
                }
            }
        }

        private void ScanUnaddressedViews()
        {
            if (!Directory.Exists(Path.Combine(GetProjectRoot(), UIPrefabFolder)))
            {
                return;
            }

            if (!AddressableAssetSettingsDefaultObject.SettingsExists)
            {
                unaddressedViews.Add(new Finding(UIPrefabFolder, "工程里没有 Addressables 设置，整项没法查"));
                return;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            var entries = new List<AddressableAssetEntry>();

            // includeSelf: true —— 文件夹条目会被展开成里面每个资源，所以「整个文件夹加进组」的资源也算覆盖到。
            settings.GetAllAssets(entries, true);

            var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].AssetPath))
                {
                    covered.Add(entries[i].AssetPath);
                }
            }

            var prefabs = new List<string>();
            CollectAssetPaths("t:Prefab", prefabs, UIPrefabFolder);
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (!covered.Contains(prefabs[i]))
                {
                    unaddressedViews.Add(new Finding(prefabs[i], "不在任何 Addressables 组里"));
                }
            }
        }

        private static void CollectAssetPaths(string filter, List<string> into, string folder = ProjectFolder)
        {
            string[] guids = AssetDatabase.FindAssets(filter, new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path))
                {
                    into.Add(path);
                }
            }
        }

        /// <summary>Unity 自己也会忽略这些：以 . 开头、以 ~ 结尾、.tmp 结尾的文件与目录。</summary>
        private static bool IsHidden(string normalizedPath)
        {
            string[] segments = normalizedPath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0)
                {
                    continue;
                }

                if (segment[0] == '.' || segment[segment.Length - 1] == '~'
                    || segment.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>绝对路径 → Assets/ 开头的资产路径。不写死本机路径。</summary>
        private static string ToAssetPath(string normalizedAbsolutePath)
        {
            string dataPath = Application.dataPath.Replace("\\", "/");
            return normalizedAbsolutePath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase)
                ? "Assets" + normalizedAbsolutePath.Substring(dataPath.Length)
                : normalizedAbsolutePath;
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        /// <summary>一条发现：资产路径 + 一句话说明。只是个数据壳，不值得单开文件。</summary>
        private readonly struct Finding
        {
            public Finding(string assetPath, string detail)
            {
                AssetPath = assetPath;
                Detail = detail;
            }

            public string AssetPath { get; }

            public string Detail { get; }
        }
    }
}
