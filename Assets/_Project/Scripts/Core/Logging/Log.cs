// 职责：全框架统一的日志门面，带 [Game] 前缀，Debug 级别编译期剔除。
// 为什么新建：波 1 之前工程内没有任何日志封装，直接调 UnityEngine.Debug 无法统一前缀、
// 也无法把调试日志从发布包里剔除；没有可复用或可扩展的已有文件承担这个职责。

using System.Diagnostics;
using UnityEngine;

namespace Game.Core.Logging
{
    /// <summary>
    /// 静态日志门面。四个级别：Debug / Info / Warn / Error。
    /// Debug 只在编辑器与开发包里存在，正式包里整句调用（含参数求值）被编译器剔除。
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[Game] ";

        /// <summary>调试日志。仅 UNITY_EDITOR / DEVELOPMENT_BUILD 下存在，正式包里连调用都不会生成。</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string message, Object context = null)
        {
            UnityEngine.Debug.Log(Prefix + message, context);
        }

        /// <summary>常规信息，发布包里保留。</summary>
        public static void Info(string message, Object context = null)
        {
            UnityEngine.Debug.Log(Prefix + message, context);
        }

        /// <summary>可继续运行但需要留意的情况。</summary>
        public static void Warn(string message, Object context = null)
        {
            UnityEngine.Debug.LogWarning(Prefix + message, context);
        }

        /// <summary>出错，通常伴随流程中止。</summary>
        public static void Error(string message, Object context = null)
        {
            UnityEngine.Debug.LogError(Prefix + message, context);
        }
    }
}
