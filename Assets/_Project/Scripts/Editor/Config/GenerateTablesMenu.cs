// 职责：编辑器菜单「21Days/配置表/生成」——在编辑器里一键跑 scripts/gen-tables.ps1 并刷新资产。
// 为什么新建：策划与不熟悉命令行的同事改完 Excel 需要一个点得到的入口；
// BuildScript.cs 的职责是打包（命令行参数、平台切换、BuildReport），把配置表生成塞进去名实不符。

using System;
using System.Diagnostics;
using System.IO;
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
            Run(false);
        }

        [MenuItem("21Days/配置表/重新下载 Luban 后生成", false, 101)]
        public static void GenerateWithForcedDownload()
        {
            Run(true);
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

        /// <param name="forceDownload">true 时给脚本加 -Force，删掉本地 Luban 重新下载。</param>
        private static void Run(bool forceDownload)
        {
            string projectRoot = GetProjectRoot();
            string script = Path.Combine(projectRoot, ScriptRelativePath);
            if (!File.Exists(script))
            {
                Debug.LogError($"{LogPrefix} 找不到生成脚本：{ScriptRelativePath}。确认仓库完整。");
                return;
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
                        return;
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
                return;
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
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log($"{LogPrefix} 生成完成，资产已刷新。别忘了生成物（Generated/ 与 Data/Config/）要一起提交。");
        }

        /// <summary>工程根 = Application.dataPath 的上一级。不写死任何本机路径。</summary>
        private static string GetProjectRoot()
        {
            return Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        }
    }
}
