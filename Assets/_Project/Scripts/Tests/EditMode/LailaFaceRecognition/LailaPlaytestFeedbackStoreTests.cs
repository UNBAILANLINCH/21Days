using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Tests.EditMode.LailaFaceRecognition
{
    public sealed class LailaPlaytestFeedbackStoreTests
    {
        private string directory;
        private static byte[] Image => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1UAAAAASUVORK5CYII=");

        [SetUp]
        public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "laila-feedback-store-test-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, true);
        }

        private static JObject Snapshot()
        {
            var weights = new JObject(); for (int i = 0; i < 31; i++) weights["shape" + i] = 0;
            return new JObject { ["raw17"] = JArray.FromObject(new float[17]), ["weights31"] = weights,
                ["user_label"] = "neutral", ["note"] = "原始意见", ["model"] = new JObject { ["onnx_sha256"] = new string('a', 64) } };
        }

        [Test]
        public void DisplayLabel_MergesOnlyPresentationAndKeepsInternalKey()
        {
            const string key = "surprise_fear";
            Assert.That(Game.LailaFaceRecognition.LailaExpressionRecognizer.DisplayLabel(key, "惊讶/恐惧"), Is.EqualTo("惊恐"));
            Assert.That(key, Is.EqualTo("surprise_fear"));
            Assert.That(Game.LailaFaceRecognition.LailaExpressionRecognizer.DisplayLabel("fear", "恐惧"), Is.EqualTo("恐惧"));
        }

        [TestCase(null)]
        [TestCase("surprise")]
        [TestCase("fear")]
        [TestCase("model_auto")]
        public void Save_NoExplicitFeedbackLabelPublishesNothing(string label)
        {
            var snapshot = Snapshot(); snapshot["user_label"] = label;
            Assert.Throws<InvalidOperationException>(() => Game.LailaFaceRecognition.LailaPlaytestFeedbackStore.Save(directory, snapshot, Image));
            Assert.That(Directory.Exists(directory), Is.False);
        }

        [Test]
        public void Save_TwoRecordsKeepSourceAndFirstRecordImmutable()
        {
            var snapshot = Snapshot(); string source = snapshot.ToString();
            string first = Game.LailaFaceRecognition.LailaPlaytestFeedbackStore.Save(directory, snapshot, Image);
            byte[] original = File.ReadAllBytes(first);
            Assert.That(snapshot.ToString(), Is.EqualTo(source));
            snapshot["user_label"] = "hard_to_express";
            string second = Game.LailaFaceRecognition.LailaPlaytestFeedbackStore.Save(directory, snapshot, Image);
            Assert.That(first, Is.Not.EqualTo(second)); Assert.That(File.ReadAllBytes(first), Is.EqualTo(original));
            var record = JObject.Parse(File.ReadAllText(second));
            Assert.That((bool)record["training_eligible"], Is.False);
            Assert.That((string)record["user_label"], Is.EqualTo("hard_to_express"));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(second), "face.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(second), "record.partial")), Is.False);
        }

        [Test]
        public void Save_LateInvalidInputOrMissingImagePublishesNothing()
        {
            var snapshot = Snapshot(); snapshot["raw17"][16] = double.NaN;
            Assert.Throws<InvalidOperationException>(() => Game.LailaFaceRecognition.LailaPlaytestFeedbackStore.Save(directory, snapshot, Image));
            Assert.That(Directory.Exists(directory), Is.False);
            Assert.Throws<InvalidOperationException>(() => Game.LailaFaceRecognition.LailaPlaytestFeedbackStore.Save(directory, Snapshot(), new byte[0]));
        }

        [Test]
        public void Save_UnwritableDestinationPreservesExistingFile()
        {
            Directory.CreateDirectory(directory); string blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "keep");
            Assert.Throws<IOException>(() => Game.LailaFaceRecognition.LailaPlaytestFeedbackStore.Save(blocked, Snapshot(), Image));
            Assert.That(File.ReadAllText(blocked), Is.EqualTo("keep"));
        }
    }
}
