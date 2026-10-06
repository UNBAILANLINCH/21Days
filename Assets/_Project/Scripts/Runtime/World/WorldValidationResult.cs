// 职责：世界表校验的结果——阻断问题列表 + 未实装场景列表（后者是状态不是错误）。
// 为什么新建：一个文件一个类（csharp-code.md「一个文件一个类，文件名等于类名」）；
//   「未实装的场景」要能和「表写坏了」分开报：前者是工程现状（Addressables 里还没有那些场景），
//   后者是必须当场修的错。合成一个 boolean 就分不出来了。
using System;
using System.Collections.Generic;
using System.Text;

namespace Game.World
{
    /// <summary>世界表校验结果。</summary>
    public sealed class WorldValidationResult
    {
        private static readonly IReadOnlyList<string> NoScenes = Array.Empty<string>();
        private static readonly IReadOnlyList<WorldValidationIssue> NoIssues = Array.Empty<WorldValidationIssue>();

        private WorldValidationResult(IReadOnlyList<WorldValidationIssue> problems, IReadOnlyList<string> notImplementedScenes)
        {
            Problems = problems;
            NotImplementedSceneKeys = notImplementedScenes;
        }

        /// <summary>阻断问题：引用对不上、白名单外的取值、未实装却写了地址。空列表才算过。</summary>
        public IReadOnlyList<WorldValidationIssue> Problems { get; }

        /// <summary>表里 <c>implemented=false</c> 的场景键——**不是错误**，是「还不能加载」的现状。</summary>
        public IReadOnlyList<string> NotImplementedSceneKeys { get; }

        /// <summary>校验是否通过（只看 <see cref="Problems"/>）。</summary>
        public bool Passed => Problems.Count == 0;

        /// <summary>拼一句人能读的汇总，给异常消息与测试断言用。</summary>
        public string Describe()
        {
            if (Problems.Count == 0)
            {
                return NotImplementedSceneKeys.Count == 0
                    ? "全部通过；没有未实装的场景。"
                    : $"全部通过；未实装的场景：{string.Join("、", NotImplementedSceneKeys)}。";
            }

            var text = new StringBuilder();
            for (int i = 0; i < Problems.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(' ');
                }

                text.Append(Problems[i]);
            }

            return text.ToString();
        }

        /// <summary>构造一个只有失败结果的结果（读表本身就失败时用）。</summary>
        public static WorldValidationResult Failed(IReadOnlyList<WorldValidationIssue> problems) =>
            new WorldValidationResult(problems ?? NoIssues, NoScenes);

        /// <summary>构造一个完整结果。</summary>
        public static WorldValidationResult Of(IReadOnlyList<WorldValidationIssue> problems, IReadOnlyList<string> notImplementedScenes) =>
            new WorldValidationResult(problems ?? NoIssues, notImplementedScenes ?? NoScenes);
    }
}
