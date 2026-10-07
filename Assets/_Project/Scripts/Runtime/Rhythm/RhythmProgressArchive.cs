// 职责：迁移前保存原始档案并拒绝无法安全读取的版本；框架滚动.bak会被后续保存替换，进度DTO不能承担磁盘保护。
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Game.Rhythm
{
    public static class RhythmProgressArchive
    {
        public static void EnsureSupportedAndBackup(string root, string profile, int currentVersion)
        {
            if (string.IsNullOrEmpty(root)) return; // 内存假存档没有磁盘；真实State使用平台/JsonSaveService根目录。
            if (string.IsNullOrWhiteSpace(profile) || profile.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new ArgumentException("档案名称无效");
            string path = Path.Combine(root, "profile-" + profile + ".json");
            if (!File.Exists(path)) return;
            byte[] bytes = File.ReadAllBytes(path);
            var data = JObject.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
            bool envelope = data["version"] != null || data["data"] != null;
            if (envelope && (data["version"]?.Type != JTokenType.Integer || data["data"]?.Type != JTokenType.Object))
                throw new InvalidDataException("进度档案结构无效；原档保留，禁止覆盖");
            int version = envelope ? (int)data["version"] : 1;
            if (version < 1 || version > currentVersion) throw new InvalidDataException("进度版本不支持；原档保留，禁止覆盖");
            var progress = (envelope ? data["data"] : data).ToObject<RhythmProgressData>();
            if (progress == null) throw new InvalidDataException("进度档案内容无效");
            RhythmProgressRules.Normalize(progress);
            if (version >= currentVersion) return;
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            string backup = path + ".migration-v" + version + "-" + hash + ".bak";
            if (File.Exists(backup)) return;
            using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) stream.Write(bytes, 0, bytes.Length);
        }
    }
}
