// 职责：菜单「21Days/工程/创建模块骨架…」——输入模块名，按工程约定建出目录与一个占位规则类。
// 为什么新建（project-root.md「加能力的顺序」）：
//   1. 复用不行：工程里只有 BuildScript（打包）与 GenerateTablesMenu（跑配置表生成脚本），
//      都是「起一个外部进程」，和「按约定建目录、写文件」不是一回事。
//   2. 扩展不行：塞进 GenerateTablesMenu 会让那个类同时管配置表和工程结构，名实不符。
//   本类既是菜单项又是那个极简窗口（一个文件一个类，类名等于文件名）。
//
// 它只做机械的那部分（目录、命名空间、文件头），设计与实现仍然走 /new-feature。

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 创建模块骨架的小窗口。按 <c>CLAUDE.md</c> 的目录约定建三处：
    /// <code>
    /// Assets/_Project/Scripts/Runtime/&lt;模块&gt;/      命名空间 Game.&lt;模块&gt;，含一个占位规则类
    /// Assets/_Project/Scripts/Tests/EditMode/&lt;模块&gt;/  EditMode 测试
    /// Assets/_Project/Data/&lt;模块&gt;/                   ScriptableObject 配置资产
    /// </code>
    /// 已存在就整单拒绝，不做「有的建有的不建」——半建出来的骨架比没建更难收拾。
    /// </summary>
    public sealed class ProjectStructureMenu : EditorWindow
    {
        private const string RuntimeRoot = "Assets/_Project/Scripts/Runtime";
        private const string TestsRoot = "Assets/_Project/Scripts/Tests/EditMode";
        private const string DataRoot = "Assets/_Project/Data";

        /// <summary>模块名必须是 PascalCase 的合法标识符：它同时是目录名、命名空间后缀和类名前缀。</summary>
        private static readonly Regex ModuleNamePattern = new Regex("^[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

        private string moduleName = string.Empty;
        private string message = string.Empty;
        private MessageType messageType = MessageType.None;

        [MenuItem("21Days/工程/创建模块骨架…", false, 200)]
        public static void Open()
        {
            ProjectStructureMenu window = GetWindow<ProjectStructureMenu>(true, "创建模块骨架", true);
            window.minSize = new Vector2(460f, 220f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("按工程约定建目录与占位规则类", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "模块名用 PascalCase（Player、Inventory）。它同时是目录名、命名空间 Game.<模块> 和类名前缀。\n"
                + "建完还要跑 /new-feature <模块> 走设计与实现，跑 /generate-doc <模块> 生成文档三件套。",
                MessageType.Info);

            moduleName = EditorGUILayout.TextField("模块名", moduleName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("将创建", EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                string preview = string.IsNullOrWhiteSpace(moduleName) ? "<模块>" : moduleName.Trim();
                EditorGUILayout.LabelField($"{RuntimeRoot}/{preview}/{preview}Rules.cs", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"{TestsRoot}/{preview}/", EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"{DataRoot}/{preview}/", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("创建", GUILayout.Height(28f)))
            {
                Create();
            }

            if (!string.IsNullOrEmpty(message))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(message, messageType);
            }
        }

        private void Create()
        {
            string module = (moduleName ?? string.Empty).Trim();
            if (!ModuleNamePattern.IsMatch(module))
            {
                Report("模块名要用 PascalCase 的字母数字（首字母大写），例如 Player、Inventory。", MessageType.Error);
                return;
            }

            string runtimeDir = $"{RuntimeRoot}/{module}";
            string testsDir = $"{TestsRoot}/{module}";
            string dataDir = $"{DataRoot}/{module}";
            string projectRoot = GetProjectRoot();

            // 三处任何一处已存在就整单拒绝：这个工具只负责「从零建」，已有模块的改动走 /new-feature。
            foreach (string existing in new[] { runtimeDir, testsDir, dataDir })
            {
                if (Directory.Exists(Path.Combine(projectRoot, existing)))
                {
                    Report($"{existing} 已经存在，没有创建任何东西。\n"
                           + $"要改已有模块请直接编辑，或先读 ai-docs/docs/modules/{module.ToLowerInvariant()}/ 下的 guide。",
                        MessageType.Error);
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(projectRoot, runtimeDir));
                Directory.CreateDirectory(Path.Combine(projectRoot, testsDir));
                Directory.CreateDirectory(Path.Combine(projectRoot, dataDir));

                string rulesPath = Path.Combine(projectRoot, runtimeDir, $"{module}Rules.cs");

                // 不带 BOM：本仓库只有 .ps1 存 BOM，其它文本文件一律无 BOM（ai-docs/pitfalls.md）。
                File.WriteAllText(rulesPath, BuildRulesTemplate(module), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Report($"创建失败：{e.Message}", MessageType.Error);
                return;
            }

            AssetDatabase.Refresh();
            Report($"已创建 {module} 的骨架。接下来：\n"
                   + $"1. 跑 /new-feature {module} 定范围与设计要点，把 {module}Rules 写成真的规则类；\n"
                   + $"2. 在 {testsDir}/ 写 EditMode 测试（至少一条覆盖核心规则）；\n"
                   + $"3. 玩法要接进标题界面就照 Runtime/Sample/SampleInstaller.cs 写一个 Installer；\n"
                   + $"4. 跑 /generate-doc {module} 生成文档三件套，并在 ai-docs/docs/catalog.md 补一行。\n"
                   + "（Tests 与 Data 两个目录现在是空的，Unity 与 git 都要等里面有文件才认。）",
                MessageType.Info);

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(runtimeDir));
        }

        /// <summary>占位规则类：形状照着 Runtime/Sample/SampleRules.cs，注释直接写清下一步该干什么。</summary>
        private static string BuildRulesTemplate(string module)
        {
            return $@"// 职责：{module} 模块的玩法规则（占位，由「21Days/工程/创建模块骨架…」生成）。
// 为什么新建：{module} 是新模块，Runtime/ 下没有可复用或可扩展的文件。
//   —— 这两行请在动手实现时改成真实理由（project-root.md「加能力的顺序」要求写明前两步为何不行）。
//
// 规则类是**纯 C# 类**：不继承 MonoBehaviour、不碰 UnityEngine.Time、不碰单例，
// 输入输出都是普通值，这样 Tests/EditMode/{module}/ 里一条断言就能钉住核心规则
// （architecture.md 第 7 节；范例见 Runtime/Sample/SampleRules.cs）。

namespace Game.{module}
{{
    /// <summary>{module} 的玩法规则。表现（MonoBehaviour）不写在这里。</summary>
    public sealed class {module}Rules
    {{
        // TODO: 依赖走构造注入（IConfigService、IClock……），不要在这里 new 服务、不要读静态单例。
    }}
}}
";
        }

        private void Report(string text, MessageType type)
        {
            message = text;
            messageType = type;
        }

        /// <summary>工程根 = Application.dataPath 的上一级。不写死任何本机路径。</summary>
        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
