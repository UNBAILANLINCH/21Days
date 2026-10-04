// 职责：迁移归档的磁盘保护回归；成绩内存测试不能证明原始字节备份和拒绝后的文件保留。
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Game.Rhythm;
using NUnit.Framework;

namespace Game.Tests.EditMode.Rhythm
{
    public sealed class RhythmArchiveTests
    {
        private const string Profile = "rhythm-test-progress";

        [TestCase(false)]
        [TestCase(true)]
        public void OldProfile_BackupPreservesExactBytesAndIsIdempotent(bool envelope)
        {
            WithTemporaryRoot(root =>
            {
                string content = "{\r\n  \"BestScores\": {\"song%3Aa:chart:old\": 1500},\r\n  \"ClearedCharts\": [\"song%3Aa:chart:old\"]\r\n}";
                if (envelope) content = "{\"version\":1,\"data\":" + content + "}";
                byte[] original = WithBom(content);
                string path = ProfilePath(root);
                File.WriteAllBytes(path, original);
                RhythmProgressArchive.EnsureSupportedAndBackup(root, Profile, 2);
                string[] backups = Directory.GetFiles(root, "*.migration-v1-*.bak");
                Assert.That(backups.Length, Is.EqualTo(1));
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(original)).Replace("-", "").ToLowerInvariant();
                Assert.That(backups[0], Is.EqualTo(path + ".migration-v1-" + hash + ".bak"));
                Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(original));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                RhythmProgressArchive.EnsureSupportedAndBackup(root, Profile, 2);
                Assert.That(Directory.GetFiles(root, "*.migration-v1-*.bak").Length, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(original));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            });
        }

        [TestCase("{\"version\":3,\"data\":{}}")]
        [TestCase("{\"version\":0,\"data\":{}}")]
        [TestCase("{\"version\":2}")]
        [TestCase("{\"data\":{}}")]
        [TestCase("{\"version\":\"2\",\"data\":{}}")]
        [TestCase("{\"version\":2,\"data\":[]}")]
        public void UnsupportedOrInvalidEnvelope_RejectsWithoutChangingOriginal(string content)
        {
            AssertRejectedAndUntouched(content, () => Assert.Throws<InvalidDataException>(CurrentAttempt));
        }

        [Test]
        public void InvalidCurrentRecord_RejectsWithoutChangingOriginal()
        {
            string key = RhythmProgressRules.RecordKey("song", "chart", "1", "four-lane-tap-hold", "1");
            var valid = new RhythmRunResult("run", null, RhythmPlayMode.FreePlay, RhythmRunCompletion.Completed,
                "finished", "song", "chart", "1", "four-lane-tap-hold", "1", 2, 2, 0, 0, 0, 2000, 2);
            var data = new RhythmProgressData();
            data.Records.Add(key, new RhythmRecordData { BestScoreRun = valid, LastCompletedRun = valid, Cleared = true });
            string content = "{\"version\":2,\"data\":" + Serialize(data).Replace("\"Score\":2000", "\"Score\":1") + "}";
            Assert.That(content, Does.Contain("\"Score\":1"), "fixture必须确实包含非法分数");
            AssertRejectedAndUntouched(content, () => Assert.Catch<Exception>(CurrentAttempt));
        }

        [Test]
        public void CurrentRecordWithWrongComparableKey_RejectsWithoutChangingOriginal()
        {
            var run = new RhythmRunResult("run", null, RhythmPlayMode.FreePlay, RhythmRunCompletion.Completed,
                "finished", "song", "chart", "1", "four-lane-tap-hold", "1", 2, 2, 0, 0, 0, 2000, 2);
            var data = new RhythmProgressData();
            data.Records.Add("wrong-key", new RhythmRecordData { BestScoreRun = run, LastCompletedRun = run });
            AssertRejectedAndUntouched("{\"version\":2,\"data\":" + Serialize(data) + "}",
                () => Assert.Throws<ArgumentException>(CurrentAttempt));
        }

        [Test]
        public void SupportedCurrentProfile_DoesNotCreateMigrationBackup()
        {
            WithTemporaryRoot(root =>
            {
                byte[] original = WithBom("{\"version\":2,\"data\":{}}");
                File.WriteAllBytes(ProfilePath(root), original);
                RhythmProgressArchive.EnsureSupportedAndBackup(root, Profile, 2);
                Assert.That(Directory.GetFiles(root).Length, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(ProfilePath(root)), Is.EqualTo(original));
            });
        }

        private Action currentAttempt;
        private void CurrentAttempt() => currentAttempt();
        private void AssertRejectedAndUntouched(string content, Action assertion)
        {
            WithTemporaryRoot(root =>
            {
                byte[] original = WithBom(content);
                string path = ProfilePath(root);
                File.WriteAllBytes(path, original);
                currentAttempt = () => RhythmProgressArchive.EnsureSupportedAndBackup(root, Profile, 2);
                try { assertion(); }
                finally { currentAttempt = null; }
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
                Assert.That(Directory.GetFiles(root).Length, Is.EqualTo(1), "拒绝读取不能产生迁移备份或替换文件");
            });
        }
        private static string Serialize(object value)
        {
            var json = Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true);
            return (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
        }
        private static string ProfilePath(string root) => Path.Combine(root, "profile-" + Profile + ".json");
        private static byte[] WithBom(string text)
        {
            byte[] content = Encoding.UTF8.GetBytes(text);
            byte[] bytes = new byte[content.Length + 3];
            bytes[0] = 0xef; bytes[1] = 0xbb; bytes[2] = 0xbf;
            Buffer.BlockCopy(content, 0, bytes, 3, content.Length);
            return bytes;
        }
        private static void WithTemporaryRoot(Action<string> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "21days-rhythm-archive-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { body(root); }
            finally { Directory.Delete(root, true); }
        }
    }
}
