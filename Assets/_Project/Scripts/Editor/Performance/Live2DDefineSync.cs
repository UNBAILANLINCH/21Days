// 职责：Live2D Cubism SDK 出现 / 消失时，自动加 / 减编译符号 LIVE2D_CUBISM，
// 让 Assets/_Project/Scripts/Runtime/Live2D/Game.Live2D.asmdef 的 defineConstraints 生效。
//
// ① 执行载体与副作用：[InitializeOnLoad] 静态构造在每次域重载（编译完成 / 打开编辑器）时都跑一遍，
//   但只在「SDK 是否存在」这一位状态真的翻转时才写一次 PlayerSettings（= 写 ProjectSettings/ProjectSettings.asset），
//   对应 project-guide 硬规则 3（自动化写生成物只在状态变化时写，不许每次域重载都写盘/刷屏）。
// ② 状态锚点（5 秒可证伪）：菜单 21Days/演出/检查 Live2D 符号——点一下，Console 立刻打印
//   「SDK 是否存在」与「当前符号是否含 LIVE2D_CUBISM」两行，不用翻 ProjectSettings 文件。
// ③ 退场条件：Live2D Cubism SDK 改走 UPM 包分发后，改用 asmdef 自带的 versionDefines
//   （按包版本自动定义符号，不需要这份轮询脚本），届时删除本文件。
//
// 为什么新建（复用 → 扩展 → 新建）：工程里没有任何「按第三方 SDK 是否存在切编译符号」的既有机制，
// 也没有承担这个职责的现有 Editor 类可扩展。

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Game.Editor.Performance
{
    /// <summary>
    /// 检测工程里是否存在 Live2D.Cubism.asmdef（官方 SDK 唯一的运行时程序集，Core / Framework / Rendering 都在里面），据此增删编译符号 LIVE2D_CUBISM。
    /// 按文件名完全相等判断，不会误匹配 Live2D.Cubism.Editor.asmdef。
    /// 只在状态变化时写 PlayerSettings 与打日志，域重载时静默重复检查。
    /// </summary>
    [InitializeOnLoad]
    internal static class Live2DDefineSync
    {
        private const string DefineSymbol = "LIVE2D_CUBISM";
        private const string CubismAsmdefFileName = "Live2D.Cubism.asmdef";

        static Live2DDefineSync()
        {
            Sync(logWhenUnchanged: false);
        }

        [MenuItem("21Days/演出/检查 Live2D 符号", false, 420)]
        private static void CheckFromMenu()
        {
            Sync(logWhenUnchanged: true);
        }

        private static void Sync(bool logWhenUnchanged)
        {
            bool sdkPresent = FindCubismAsmdef();
            BuildTargetGroup group = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
            string[] symbols = defines.Split(';').Where(s => s.Length > 0).ToArray();
            bool hasSymbol = symbols.Contains(DefineSymbol);

            if (sdkPresent && !hasSymbol)
            {
                string updated = defines.Length > 0 ? defines + ";" + DefineSymbol : DefineSymbol;
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, updated);
                Debug.Log("[Game] 检测到 Live2D Cubism SDK，已加编译符号 LIVE2D_CUBISM");
                return;
            }

            if (!sdkPresent && hasSymbol)
            {
                string updated = string.Join(";", symbols.Where(s => s != DefineSymbol));
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, updated);
                Debug.Log("[Game] 未检测到 Live2D Cubism SDK，已移除编译符号 LIVE2D_CUBISM");
                return;
            }

            if (logWhenUnchanged)
            {
                string sdkText = sdkPresent ? "存在" : "不存在";
                string symbolText = hasSymbol ? "含 LIVE2D_CUBISM" : "未设置 LIVE2D_CUBISM";
                Debug.Log($"[Game] Live2D Cubism SDK{sdkText}；当前编译符号{symbolText}。");
            }
        }

        private static bool FindCubismAsmdef()
        {
            string[] guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path) == CubismAsmdefFileName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
