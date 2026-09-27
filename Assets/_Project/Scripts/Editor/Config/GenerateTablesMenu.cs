// 职责：编辑器菜单「21Days/配置表/生成」——在编辑器里一键跑 scripts/gen-tables.ps1 并刷新资产。
// 为什么新建：策划与不熟悉命令行的同事改完 Excel 需要一个点得到的入口；
// BuildScript.cs 的职责是打包（命令行参数、平台切换、BuildReport），把配置表生成塞进去名实不符。
//
// 为什么扩展（PRP/quest-editor 3.4）：生成是所有表数据进包体的唯一闸口，任务表的编辑期校验挂在这里，
//   才对手改 JSON、不开任务编辑器的人也生效——有 Error 就不跑 Luban，Console 给中文错误而不是 Luban 的英文。
//   为此 Run 改成 public 的 TryGenerate（返回是否成功），任务编辑器的「保存并生成」直接调它。
//   菜单「21Days/策划/校验任务表」也放在这里：它就是「只过闸、不生成」，与生成前校验共用同一段
//   ValidateQuestTables（读表 + 校验 + 按等级打 Console），放进任务编辑器窗口反而要跨文件共享这段逻辑。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.Editor.Quest;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Game.Editor
{
    /// <summary>
    /// 配置表生成菜单。真正的活全在 <c>scripts/gen-tables.ps1</c> 里——
    /// 这里只负责起进程、把输出转发到 Console、跑完刷新资产，保证命令行与菜单两条路跑的是同一套逻辑。
    /// </summary>
    public static class GenerateTablesMenu
    {
        private const string LogPrefix = "[配置表]";
        private const string ScriptRelativePath = "scripts/gen-tables.ps1";

        [MenuItem("21Days/配置表/生成", false, 100)]
        public static void Generate()
        {
            TryGenerate(false);
        }

        [MenuItem("21Days/配置表/重新下载 Luban 后生成", false, 101)]
        public static void GenerateWithForcedDownload()
        {
            TryGenerate(true);
        }

        /// <summary>
        /// 只校验任务表、不生成：读 Tables/Data/quest/*.json，跑 <see cref="QuestTableValidator"/>，结果按等级打到 Console。
        /// </summary>
        [MenuItem("21Days/策划/校验任务表", false, 401)]
        public static void ValidateQuestTablesMenu()
        {
            int errors = ValidateQuestTables(out int questCount, out int warnings);
            if (errors > 0)
            {
                Debug.LogError($"{LogPrefix} 任务表校验未通过：{errors} 处错误、{warnings} 条警告（{questCount} 条任务）。打开 21Days/策划/任务编辑器 修正。");
            }
            else if (warnings > 0)
            {
                Debug.Log($"{LogPrefix} 任务表校验通过，{warnings} 条警告（{questCount} 条任务）。");
            }
            else
            {
                Debug.Log($"{LogPrefix} 任务表校验通过（{questCount} 条任务）");
            }
        }

        /// <summary>
        /// 读全部任务表文件并校验，逐条打 Console：读文件失败与 Error 用 LogError，Warning 用 LogWarning。
        /// 生成前闸口与「校验任务表」菜单共用这一段，保证两处看到的是同一份结果、同一种文案。
        /// </summary>
        /// <returns>错误数（读文件失败也算错误：那条任务进不了包）。</returns>
        private static int ValidateQuestTables(out int questCount, out int warningCount)
        {
            var problems = new List<string>();
            List<QuestDraft> drafts = QuestTableFile.LoadAll(problems);
            List<QuestIssue> issues = QuestTableValidator.Validate(drafts, QuestTableValidator.CollectContext());

            questCount = drafts.Count;
            warningCount = 0;
            int errorCount = problems.Count;

            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogError($"{LogPrefix} 任务表：[错误] 读文件失败：{problems[i]}");
            }

            for (int i = 0; i < issues.Count; i++)
            {
                QuestIssue issue = issues[i];
                if (issue.Severity == QuestIssueSeverity.Error)
                {
                    errorCount++;
                    Debug.LogError($"{LogPrefix} 任务表：{issue}");
                }
                else
                {
                    warningCount++;
                    Debug.LogWarning($"{LogPrefix} 任务表：{issue}");
                }
            }

            return errorCount;
        }

        [MenuItem("21Days/配置表/打开 Tables 目录", false, 120)]
        public static void OpenTablesFolder()
        {
            string tables = Path.Combine(GetProjectRoot(), "Tables");
            if (!Directory.Exists(tables))
            {
                Debug.LogError($"{LogPrefix} 找不到 Tables 目录：{tables}");
                return;
            }

            EditorUtility.RevealInFinder(tables);
        }

        /// <summary>
        /// 先过任务表校验（有 Error 不生成），再跑 gen-tables.ps1 并刷新资产。
        /// 任务编辑器的「保存并生成」与两个生成菜单都走这里。
        /// </summary>
        /// <param name="forceDownload">true 时给脚本加 -Force，删掉本地 Luban 重新下载。</param>
        /// <returns>生成成功返回 true；校验拦下、脚本缺失、进程出错、退出码非 0 都返回 false（原因已打到 Console）。</returns>
        public static bool TryGenerate(bool forceDownload)
        {
            int questErrors = ValidateQuestTables(out _, out _);
            if (questErrors > 0)
            {
                Debug.LogError($"{LogPrefix} 任务表有 {questErrors} 处错误，未生成。打开 21Days/策划/任务编辑器 修正后重试。");
                return false;
            }

            string projectRoot = GetProjectRoot();
            string script = Path.Combine(projectRoot, ScriptRelativePath);
            if (!File.Exists(script))
            {
                Debug.LogError($"{LogPrefix} 找不到生成脚本：{ScriptRelativePath}。确认仓库完整。");
                return false;
            }

            // -NoProfile：不加载用户的 PowerShell 配置，避免别人机器上的 profile 改了编码或路径把生成跑歪。
            // -ExecutionPolicy Bypass：仓库里的脚本没签名，默认策略会直接拒绝执行。
            string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"";
            if (forceDownload)
            {
                arguments += " -Force";
            }

            ProcessStartInfo startInfo = new ProcessStartInfo("powershell", arguments)
            {
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };

            int exitCode;
            string output;
            string error;

            try
            {
                EditorUtility.DisplayProgressBar("配置表", "正在跑 gen-tables.ps1（首次会下载 Luban，约 30 MB）…", 0.5f);

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        Debug.LogError($"{LogPrefix} 起不来 powershell 进程。");
                        return false;
                    }

                    // 同步读完再 WaitForExit：管道缓冲区满了会让子进程卡死，先读干净最省事。
                    output = process.StandardOutput.ReadToEnd();
                    error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogPrefix} 执行 {ScriptRelativePath} 出错：{e}");
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                Debug.Log($"{LogPrefix} 脚本输出：\n{output.TrimEnd()}");
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogWarning($"{LogPrefix} 脚本 stderr：\n{error.TrimEnd()}");
            }

            if (exitCode != 0)
            {
                Debug.LogError($"{LogPrefix} 生成失败，退出码 {exitCode}。按上面的 Luban 输出定位是哪张表的问题。");
                return false;
            }

            AssetDatabase.Refresh();
            Debug.Log($"{LogPrefix} 生成完成，资产已刷新。别忘了生成物（Generated/ 与 Data/Config/）要一起提交。");
            return true;
        }

        /// <summary>工程根 = Application.dataPath 的上一级。不写死任何本机路径。</summary>
        private static string GetProjectRoot()
        {
            return Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        }
    }
}
