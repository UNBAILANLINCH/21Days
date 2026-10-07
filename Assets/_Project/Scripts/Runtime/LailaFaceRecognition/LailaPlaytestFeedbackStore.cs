using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Game.LailaFaceRecognition
{
    // 职责：仅发布独立现场反馈的不可变截图／记录，不合并旧标注或提供训练输入。
    // 新建原因：原采集写unlabeled，原标注器固定139图，两者均不承担现场预测可见反馈的原子保存。
    public static class LailaPlaytestFeedbackStore
    {
        private static readonly string[] Labels = { "neutral", "happy", "sad", "surprise_fear", "angry", "uncertain", "hard_to_express" };
        public static string[] LabelKeys => (string[])Labels.Clone();

        public static bool IsAllowedLabel(string label) => Array.IndexOf(Labels, label) >= 0;

        public static string Digest(JObject snapshot)
        {
            var copy = (JObject)snapshot.DeepClone();
            copy.Remove("created_at"); copy.Remove("id"); copy.Remove("image");
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(copy.ToString(Newtonsoft.Json.Formatting.None))))
                    .Replace("-", "").ToLowerInvariant();
        }

        public static string Save(string batchDirectory, JObject snapshot, byte[] png)
        {
            if (snapshot == null || !IsAllowedLabel((string)snapshot["user_label"]))
                throw new InvalidOperationException("请主动选择类别或不确定");
            if (png == null || png.Length < 8 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71)
                throw new InvalidOperationException("截图不是有效PNG，未保存反馈");
            var raw = snapshot["raw17"] as JArray;
            var weights = snapshot["weights31"] as JObject;
            if (raw == null || raw.Count != 17 || weights == null || weights.Count != 31
                || string.IsNullOrEmpty((string)snapshot["model"]?["onnx_sha256"]))
                throw new InvalidOperationException("反馈快照或模型版本不完整");
            for (int i = 0; i < raw.Count; i++)
            {
                double value = (double)raw[i];
                if (double.IsNaN(value) || double.IsInfinity(value) || value < (i < 14 ? -1 : 0) || value > 1)
                    throw new InvalidOperationException("反馈输入无效");
            }
            Directory.CreateDirectory(batchDirectory);
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(batchDirectory, id);
            Directory.CreateDirectory(directory);
            string imagePath = Path.Combine(directory, "face.png");
            string recordPath = Path.Combine(directory, "record.json");
            string temporaryRecordPath = Path.Combine(directory, "record.partial");
            var record = (JObject)snapshot.DeepClone();
            record["schema_version"] = 1; record["id"] = id;
            record["purpose"] = "single-user-playtest-development-feedback-not-golden";
            record["source"] = "single-user-development-feedback";
            record["data_role"] = "playtest-feedback"; record["training_eligible"] = false;
            record["created_at"] = DateTime.UtcNow.ToString("O");
            using (var sha = SHA256.Create())
                record["image"] = new JObject { ["file"] = "face.png", ["sha256"] = BitConverter.ToString(sha.ComputeHash(png)).Replace("-", "").ToLowerInvariant() };
            bool wroteImage = false, wroteTemporaryRecord = false;
            try
            {
                using (var file = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { wroteImage = true; file.Write(png, 0, png.Length); file.Flush(true); }
                byte[] bytes = Encoding.UTF8.GetBytes(record.ToString());
                // record.json最后发布；没有完整JSON的目录不得当作有效反馈。
                using (var file = new FileStream(temporaryRecordPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { wroteTemporaryRecord = true; file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                File.Move(temporaryRecordPath, recordPath);
                return recordPath;
            }
            catch
            {
                if (wroteTemporaryRecord && File.Exists(temporaryRecordPath)) File.Delete(temporaryRecordPath);
                if (wroteImage && File.Exists(imagePath)) File.Delete(imagePath);
                if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
                throw;
            }
        }
    }
}
