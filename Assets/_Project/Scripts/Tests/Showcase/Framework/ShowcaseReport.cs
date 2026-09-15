// ShowcaseReport —— 回放过程的结构化记录 + markdown 写盘。
//
// 做什么：攒下每一步、每个检查点、每张截图和捕获到的运行时异常，回放结束后写成
//         Logs/verify/<模块小写>/<yyyyMMdd-HHmmss>/report.md，并复制一份到同模块目录下的 latest.md
//         （latest 里截图路径带上 run 目录名，这样两份都能点开图）。Claude 读 latest.md 汇报。
//
// 为什么新建（project-root.md「加能力的顺序」）：
//   复用 —— Unity Test Framework 只产出 NUnit XML（用例通过/失败），没有「第几步看到了什么、截图在哪」这层语义，
//           而这层恰恰是给人看回放用的；拿 XML 硬凑等于自己再写一遍解析。
//   扩展 —— 唯一职责相符的候选是 ShowcaseScenario，但它已经在管场景加载与节奏控制；
//           报告的数据结构和写盘格式会持续演进，塞进去会让那个类变成什么都干的大杂烩。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Tests.Showcase
{
    /// <summary>
    /// 一个模块在「本次 Play 会话」里的完整回放报告。同一测试类的多条 [UnityTest] 共用一份，
    /// 因此 run 目录只在第一次打开时生成一次（静态字段记住），多条用例的表并排写进同一个 report.md。
    /// </summary>
    public sealed class ShowcaseReport
    {
        /// <summary>报告条目的类型，对应 markdown 表里「类型」那一列。</summary>
        public enum EntryKind
        {
            Step,
            Check,
            Snapshot,
            Wait,
        }

        /// <summary>条目结果：没有判定（步骤、截图）、通过、失败。</summary>
        public enum EntryResult
        {
            None,
            Pass,
            Fail,
        }

        private const string ReportFileName = "report.md";
        private const string LatestFileName = "latest.md";

        /// <summary>本次 Play 会话的 run 目录名（时间戳），第一次打开报告时生成，之后所有模块共用同一个。</summary>
        private static string sessionStamp;

        /// <summary>本次 Play 会话已打开的报告，按模块名索引；一次跑多个模块时各自一份。</summary>
        private static readonly Dictionary<string, ShowcaseReport> OpenReports =
            new Dictionary<string, ShowcaseReport>(StringComparer.Ordinal);

        private readonly List<TestRecord> tests = new List<TestRecord>();
        private readonly string module;
        private readonly string stamp;
        private TestRecord currentTest;
        private int snapshotCount;

        private ShowcaseReport(string module, string stamp)
        {
            this.module = module;
            this.stamp = stamp;
        }

        /// <summary>run 目录的绝对路径，截图直接往这里落。</summary>
        public string RunDirectory
        {
            get { return Path.Combine(ModuleDirectory, stamp); }
        }

        /// <summary>当前这条用例里失败的检查点数量，TearDown 拿它决定要不要 Assert.Fail。</summary>
        public int CurrentTestFailureCount
        {
            get { return currentTest == null ? 0 : currentTest.FailureCount; }
        }

        /// <summary>当前这条用例期间捕获到的运行时异常条数。</summary>
        public int CurrentTestExceptionCount
        {
            get { return currentTest == null ? 0 : currentTest.Exceptions.Count; }
        }

        private string ModuleDirectory
        {
            get { return Path.Combine(ShowcaseOptions.ReportRoot, module.ToLowerInvariant()); }
        }

        /// <summary>
        /// 取本次 Play 会话里该模块的报告，没有就新建。run 目录名全会话只生成一次。
        /// </summary>
        public static ShowcaseReport Open(string module)
        {
            string key = string.IsNullOrEmpty(module) ? "unknown" : module;
            if (string.IsNullOrEmpty(sessionStamp))
            {
                sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            }

            ShowcaseReport report;
            if (!OpenReports.TryGetValue(key, out report))
            {
                report = new ShowcaseReport(key, sessionStamp);
                OpenReports[key] = report;
            }

            return report;
        }

        /// <summary>
        /// 清掉会话级静态状态。由 ShowcaseScenario 在每次进入 Play 时调用：
        /// 关了域重载（Enter Play Mode Options）时静态字段不会自动清，不清就会把新一轮的报告
        /// 追加进上一轮的 run 目录里。
        /// </summary>
        public static void ResetSession()
        {
            sessionStamp = null;
            OpenReports.Clear();
        }

        /// <summary>开一条用例记录；同名重跑也各记一条，不覆盖。</summary>
        public void BeginTest(string testName)
        {
            currentTest = new TestRecord(string.IsNullOrEmpty(testName) ? "(未命名用例)" : testName);
            tests.Add(currentTest);
        }

        /// <summary>记一条步骤 / 检查 / 等待条目。seconds 是相对该用例开始的秒数。</summary>
        public void Add(EntryKind kind, string content, EntryResult result, float seconds)
        {
            if (currentTest == null)
            {
                return;
            }

            currentTest.Entries.Add(new Entry(kind, content, result, seconds, null));
        }

        /// <summary>
        /// 记一条截图条目。fileName 为空表示这次没截成（批处理无图形），此时 content 里已经写了原因。
        /// </summary>
        public void AddSnapshot(string content, string fileName, float seconds)
        {
            if (currentTest == null)
            {
                return;
            }

            currentTest.Entries.Add(new Entry(EntryKind.Snapshot, content, EntryResult.None, seconds, fileName));
        }

        /// <summary>截图序号在整个 run 目录内连续，多条用例的图混在一个目录里也不会撞名。</summary>
        public int NextSnapshotIndex()
        {
            snapshotCount++;
            return snapshotCount;
        }

        /// <summary>记一条捕获到的运行时异常，归到当前用例名下。</summary>
        public void AddException(string message, string stackTrace)
        {
            if (currentTest == null)
            {
                return;
            }

            currentTest.Exceptions.Add(new ExceptionRecord(message, stackTrace));
        }

        /// <summary>
        /// 写 run 目录下的 report.md 和模块目录下的 latest.md，返回 report.md 的工程相对路径
        /// （相对而不是绝对：这串会进日志和 Assert 消息，不能带本机路径）。
        /// 写盘失败不抛异常 —— 报告写不出来是次要问题，不该把回放结果本身盖掉。
        /// </summary>
        public string Write()
        {
            string relative = string.Format(
                CultureInfo.InvariantCulture,
                "{0}/{1}/{2}/{3}",
                ShowcaseOptions.ReportRootRelative,
                module.ToLowerInvariant(),
                stamp,
                ReportFileName);

            try
            {
                string runDirectory = RunDirectory;
                Directory.CreateDirectory(runDirectory);
                UTF8Encoding encoding = new UTF8Encoding(false);
                File.WriteAllText(Path.Combine(runDirectory, ReportFileName), BuildMarkdown(false), encoding);
                File.WriteAllText(Path.Combine(ModuleDirectory, LatestFileName), BuildMarkdown(true), encoding);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{ShowcaseOptions.Prefix}[{module}] 报告写盘失败（{e.GetType().Name}：{e.Message}），"
                                 + $"回放结果只在控制台里，按 {ShowcaseOptions.Prefix} 过滤查看。");
            }

            return relative;
        }

        /// <summary>
        /// 拼 markdown。forLatest 为真时截图路径前面补上 run 目录名，
        /// 因为 latest.md 躺在模块目录（run 目录的上一级）。
        /// </summary>
        private string BuildMarkdown(bool forLatest)
        {
            int failures = 0;
            int exceptions = 0;
            for (int i = 0; i < tests.Count; i++)
            {
                failures += tests[i].FailureCount;
                exceptions += tests[i].Exceptions.Count;
            }

            bool pass = failures == 0 && exceptions == 0;
            StringBuilder sb = new StringBuilder();

            sb.Append("# ").Append(module).AppendLine(" 模块验证回放报告");
            sb.AppendLine();
            sb.Append("- 生成时间：")
              .AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.Append("- 回放批次：").AppendLine(stamp);
            sb.Append("- 节奏倍率：x")
              .AppendLine(ShowcaseOptions.HoldScale.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append("- 结论：**").Append(pass ? "PASS" : "FAIL").Append("** —— 检查点失败 ")
              .Append(failures.ToString(CultureInfo.InvariantCulture)).Append(" 个，运行时异常 ")
              .Append(exceptions.ToString(CultureInfo.InvariantCulture)).AppendLine(" 条");
            sb.AppendLine();

            for (int i = 0; i < tests.Count; i++)
            {
                AppendTest(sb, tests[i], forLatest);
            }

            AppendExceptions(sb);
            return sb.ToString();
        }

        private void AppendTest(StringBuilder sb, TestRecord test, bool forLatest)
        {
            sb.Append("## ").AppendLine(test.Name);
            sb.AppendLine();
            sb.AppendLine("| # | 类型 | 内容 | 结果 | 秒 |");
            sb.AppendLine("| ---: | --- | --- | :---: | ---: |");

            for (int i = 0; i < test.Entries.Count; i++)
            {
                Entry entry = test.Entries[i];
                string content = Escape(entry.Content);
                if (!string.IsNullOrEmpty(entry.FileName))
                {
                    string path = forLatest ? stamp + "/" + entry.FileName : entry.FileName;
                    content = $"{content}（`{path}`）";
                }

                sb.Append("| ").Append((i + 1).ToString(CultureInfo.InvariantCulture))
                  .Append(" | ").Append(KindText(entry.Kind))
                  .Append(" | ").Append(content)
                  .Append(" | ").Append(ResultText(entry.Result))
                  .Append(" | ").Append(entry.Seconds.ToString("0.0", CultureInfo.InvariantCulture))
                  .AppendLine(" |");
            }

            sb.AppendLine();
        }

        private void AppendExceptions(StringBuilder sb)
        {
            sb.AppendLine("## 运行时异常");
            sb.AppendLine();

            bool any = false;
            for (int i = 0; i < tests.Count; i++)
            {
                TestRecord test = tests[i];
                for (int j = 0; j < test.Exceptions.Count; j++)
                {
                    ExceptionRecord record = test.Exceptions[j];
                    sb.Append("- **").Append(test.Name).Append("**：").AppendLine(Escape(record.Message));
                    if (!string.IsNullOrEmpty(record.FirstFrame))
                    {
                        sb.Append("  - ").AppendLine(Escape(record.FirstFrame));
                    }

                    any = true;
                }
            }

            if (!any)
            {
                sb.AppendLine("无");
            }

            sb.AppendLine();
        }

        private static string KindText(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Step:
                    return "步骤";
                case EntryKind.Check:
                    return "检查";
                case EntryKind.Snapshot:
                    return "截图";
                default:
                    return "等待";
            }
        }

        private static string ResultText(EntryResult result)
        {
            switch (result)
            {
                case EntryResult.Pass:
                    return "✓";
                case EntryResult.Fail:
                    return "✗";
                default:
                    return "·";
            }
        }

        /// <summary>表格单元格里的竖线和换行会把 markdown 表冲散，转义掉。</summary>
        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>一条 [UnityTest] 的记录：条目表 + 这条用例期间捕到的异常。</summary>
        private sealed class TestRecord
        {
            public TestRecord(string name)
            {
                Name = name;
                Entries = new List<Entry>();
                Exceptions = new List<ExceptionRecord>();
            }

            public string Name { get; private set; }

            public List<Entry> Entries { get; private set; }

            public List<ExceptionRecord> Exceptions { get; private set; }

            public int FailureCount
            {
                get
                {
                    int count = 0;
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        if (Entries[i].Result == EntryResult.Fail)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
        }

        /// <summary>报告表里的一行。</summary>
        private sealed class Entry
        {
            public Entry(EntryKind kind, string content, EntryResult result, float seconds, string fileName)
            {
                Kind = kind;
                Content = content;
                Result = result;
                Seconds = seconds;
                FileName = fileName;
            }

            public EntryKind Kind { get; private set; }

            public string Content { get; private set; }

            public EntryResult Result { get; private set; }

            public float Seconds { get; private set; }

            /// <summary>截图条目的文件名（不含目录）；其它条目为空。</summary>
            public string FileName { get; private set; }
        }

        /// <summary>捕获到的一条 Error / Exception / Assert 日志。</summary>
        private sealed class ExceptionRecord
        {
            public ExceptionRecord(string message, string stackTrace)
            {
                Message = message;
                FirstFrame = FirstLine(stackTrace);
            }

            public string Message { get; private set; }

            /// <summary>调用栈第一行，够定位到文件:行；完整栈去控制台看。</summary>
            public string FirstFrame { get; private set; }

            private static string FirstLine(string stackTrace)
            {
                if (string.IsNullOrEmpty(stackTrace))
                {
                    return string.Empty;
                }

                int index = stackTrace.IndexOf('\n');
                string line = index < 0 ? stackTrace : stackTrace.Substring(0, index);
                return line.Trim();
            }
        }
    }
}
