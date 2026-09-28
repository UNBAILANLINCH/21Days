// 职责：钉住通用记录面板 TranscriptView 的文字格式——与下沉前对白历史面板的输出逐字相同（普通行、旁白行、「选择」行、省略头），
//   Show 把同一段文字写进预制体的 content；面板在 Top 层、通用取消路由关不掉它；TranscriptLine 把 null 记为空串。
// 为什么新建：TranscriptView 从对白模块下沉到 Core，格式成了两个玩法模块共用的契约；Core 测试目录里没有 UI 面板格式的用例可扩展，
//   按「被测类 + Tests」单独成文件。Show 那条读真实预制体：它守的就是预制体接线（content / close）。
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Game.Core.UI;
using Game.Core.UI.Views;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Core
{
    public sealed class TranscriptViewTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/TranscriptView.prefab";

        /// <summary>下沉前一条对白历史的形状（IsChoice / Speaker / Text），只用来喂旧格式基准。</summary>
        private readonly struct LegacyEntry
        {
            public LegacyEntry(bool choice, string speaker, string text)
            {
                Choice = choice;
                Speaker = speaker;
                Text = text;
            }

            public bool Choice { get; }
            public string Speaker { get; }
            public string Text { get; }
        }

        // 下沉前 DialogueHistoryView.Show 的原实现（逐行照抄），作逐字比对的基准。
        private static string LegacyFormat(IEnumerable<LegacyEntry> entries, bool truncated)
        {
            var text = new StringBuilder();
            if (truncated) text.AppendLine("更早的记录已省略。\n");
            foreach (LegacyEntry entry in entries)
            {
                if (entry.Choice) text.Append("选择：");
                else if (!string.IsNullOrEmpty(entry.Speaker)) text.Append(entry.Speaker).Append("：");
                text.AppendLine(entry.Text).AppendLine();
            }
            return text.ToString();
        }

        [Test]
        public void Format_SpeakerLine_WritesSpeakerColonTextAndBlankLine()
        {
            string formatted = TranscriptView.Format(new[] { new TranscriptLine("阿米娅", "博士，前面就是村口了。") }, false);

            Assert.That(formatted, Is.EqualTo("阿米娅：博士，前面就是村口了。" + System.Environment.NewLine + System.Environment.NewLine));
            Assert.That(formatted, Is.EqualTo(LegacyFormat(new[] { new LegacyEntry(false, "阿米娅", "博士，前面就是村口了。") }, false)));
        }

        [Test]
        public void Format_NarrationLine_WritesTextOnly()
        {
            string formatted = TranscriptView.Format(new[] { new TranscriptLine(string.Empty, "风停了。") }, false);

            Assert.That(formatted, Is.EqualTo(LegacyFormat(new[] { new LegacyEntry(false, string.Empty, "风停了。") }, false)));
            Assert.That(formatted, Does.Not.Contain("："), "旁白不写说话者前缀");
        }

        [Test]
        public void Format_ChoiceSpeaker_MatchesLegacyChoiceLine()
        {
            // 旧格式里选择项固定写「选择：」（不看说话者）；新格式由调用方把「选择」放进 Speaker。
            string formatted = TranscriptView.Format(new[] { new TranscriptLine("选择", "接受") }, false);

            Assert.That(formatted, Is.EqualTo(LegacyFormat(new[] { new LegacyEntry(true, "旅人", "接受") }, false)));
        }

        [Test]
        public void Format_TruncatedMixedLines_MatchesLegacyExactly()
        {
            var lines = new[]
            {
                new TranscriptLine("老者", "人都跑光了，对谁负责去？！"),
                new TranscriptLine("选择", "我去看看"),
                new TranscriptLine(string.Empty, "<b>风</b>停了。"),
            };
            var legacy = new[]
            {
                new LegacyEntry(false, "老者", "人都跑光了，对谁负责去？！"),
                new LegacyEntry(true, "老者", "我去看看"),
                new LegacyEntry(false, string.Empty, "<b>风</b>停了。"),
            };

            string formatted = TranscriptView.Format(lines, true);

            Assert.That(formatted, Is.EqualTo(LegacyFormat(legacy, true)));
            Assert.That(formatted, Does.StartWith("更早的记录已省略。\n" + System.Environment.NewLine));
        }

        [Test]
        public void Format_NullOrEmptyLines_OnlyWritesTruncatedNotice()
        {
            Assert.That(TranscriptView.Format(null, false), Is.Empty);
            Assert.That(TranscriptView.Format(new TranscriptLine[0], false), Is.Empty);
            Assert.That(TranscriptView.Format(null, true), Is.EqualTo(LegacyFormat(new LegacyEntry[0], true)));
        }

        [Test]
        public void TranscriptLine_Null_BecomesEmpty()
        {
            var line = new TranscriptLine(null, null);

            Assert.That(line.Speaker, Is.Empty);
            Assert.That(line.Text, Is.Empty);
            Assert.That(TranscriptView.Format(new[] { line }, false),
                Is.EqualTo(LegacyFormat(new[] { new LegacyEntry(false, null, null) }, false)));
        }

        [Test]
        public void Show_OnPrefab_WritesFormattedTextIntoContent()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "找不到记录面板预制体：" + PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<TranscriptView>();
                Assert.That(view, Is.Not.Null, "预制体根上没有 TranscriptView（脚本 GUID 是否随改名保留？）");
                view.OnOpenAsync(null, CancellationToken.None).GetAwaiter().GetResult();
                var lines = new[] { new TranscriptLine("阿米娅", "走吧。"), new TranscriptLine("选择", "跟上") };

                view.Show(lines, false);

                TMP_Text content;
                using (var so = new SerializedObject(view)) content = (TMP_Text)so.FindProperty("content").objectReferenceValue;
                Assert.That(content, Is.Not.Null, "content 未接线");
                Assert.That(content.text, Is.EqualTo(TranscriptView.Format(lines, false)));
                view.OnCloseAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Layer_IsTop_AndCancelRouterCannotCloseIt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "找不到记录面板预制体：" + PrefabPath);
            var view = prefab.GetComponent<TranscriptView>();

            Assert.That(view.Layer, Is.EqualTo(UILayer.Top), "演出会整层藏 Popup，记录面板要在 Top 层才看得见");
            Assert.That(view.CloseOnCancel, Is.False, "开关与 Esc 由调用方负责");
        }
    }
}
