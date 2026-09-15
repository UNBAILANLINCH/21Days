// ShowcaseOptions —— 回放框架的全局旋钮与路径常量：日志前缀、默认停顿、节奏倍率、报告根目录。
//
// 做什么：把「所有 Showcase 都要读的同一份配置」收在一处，避免每个模块的回放脚本各写一套魔法数字。
//         节奏倍率从 EditorPrefs 读，开发者能用菜单实时调快调慢，代码不用改；批处理下自动归零当回归跑。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— 工程里没有任何运行期配置载体。ScriptableObject 不合适：这些值要在编辑器里随时改、
//           还要在批处理里按 Application.isBatchMode 变，SO 资产读不到这两种上下文，改它还会写回资产。
//   扩展 —— 唯一的已有编辑器脚本 BuildScript.cs 职责是打包，把回放配置塞进去名字和职责都说不通。

using System.IO;
using UnityEngine;

namespace Game.Tests.Showcase
{
    /// <summary>
    /// 回放框架的静态配置。全部是只读常量或按上下文现算的属性，没有可写状态。
    /// </summary>
    public static class ShowcaseOptions
    {
        /// <summary>所有 Showcase 日志的统一前缀，方便 read_console(filter_text="[VERIFY]") 一把捞出来。</summary>
        public const string Prefix = "[VERIFY]";

        /// <summary>Step 不显式给 hold 时的默认停顿秒数——够开发者在 Game 视图里看清一步。</summary>
        public const float DefaultHold = 1.5f;

        /// <summary>检查点失败后的额外停顿：红字要停得久一点，别一闪而过。</summary>
        public const float FailHold = 2.5f;

        /// <summary>节奏倍率存在 EditorPrefs 里的键；ShowcaseMenu 写它，HoldScale 读它。</summary>
        public const string HoldScaleKey = "Game.Verify.HoldScale";

        /// <summary>框架落地后唯一的常驻场景；存在才加载，现在还没有也不影响回放。</summary>
        public const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";

        /// <summary>各模块验证场景所在目录，命名约定 &lt;Module&gt;.unity。</summary>
        public const string VerifySceneFolder = "Assets/_Project/Scenes/Verify";

        /// <summary>报告与截图的根目录名（相对工程根），已在 .gitignore 里，不进版本库。</summary>
        public const string ReportRootRelative = "Logs/verify";

        /// <summary>
        /// 回放节奏倍率：批处理（CI 回归）不停顿；编辑器里读开发者用菜单设的值；其余场合按原速。
        /// 每次读都现算，所以菜单一改立刻生效，不用重进 Play。
        /// </summary>
        public static float HoldScale
        {
            get
            {
                if (Application.isBatchMode)
                {
                    return 0f;
                }

#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetFloat(HoldScaleKey, 1f);
#else
                return 1f;
#endif
            }
        }

        /// <summary>
        /// 工程根目录的绝对路径。由 Application.dataPath（即 &lt;工程根&gt;/Assets）往上一级推出来，
        /// 不写死任何本机路径；判断 Assets 下某资产文件是否存在时要拿它拼绝对路径。
        /// </summary>
        public static string ProjectRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent == null ? Application.dataPath : parent.FullName;
            }
        }

        /// <summary>报告根目录的绝对路径：&lt;工程根&gt;/Logs/verify。</summary>
        public static string ReportRoot
        {
            get { return Path.Combine(ProjectRoot, "Logs", "verify"); }
        }
    }
}
