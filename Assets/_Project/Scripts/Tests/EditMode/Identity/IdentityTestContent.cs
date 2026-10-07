// 职责：身份模块测试共用的身份定义与数值块——id 口径与 `ai-docs/docs/story-facts.md` §4.1 的例子一致
//   （小写、下划线、不含点号），内容照 `01_换皮与附身.md:137-147` 的 R15 能力表挑四个代表。
// 为什么新建：四个测试文件都要同一张表；各写一份会出现「改了一处、另一处还是旧 id」的漂移。

using Game.Identity;

namespace Game.Tests.EditMode.Identity
{
    /// <summary>测试用的身份表与数值块构造。所有数值都是占位值，测试只依赖自己显式改过的那几项。</summary>
    internal static class IdentityTestContent
    {
        /// <summary>乐正：戏班，音乐解谜区受限（`01_换皮与附身.md:140`、`02_身份暴露与怀疑.md:96` R4）。</summary>
        public const string Musician = "yuezheng";

        /// <summary>都知：戏班，可以出入所有位置（`01_换皮与附身.md:139`、`02:98` R6）。</summary>
        public const string Steward = "duzhi";

        /// <summary>学徒：没有特殊能力，只能对话（`01_换皮与附身.md:141`）。</summary>
        public const string Apprentice = "xuetu";

        /// <summary>官证：可以修改拨料文书（`01_换皮与附身.md:145`、`02:107` R8）。</summary>
        public const string Clerk = "guanzheng";

        /// <summary>执事皮：皮类来源，有「生效中」时段（`01_换皮与附身.md:82`、`:166` R27）。</summary>
        public const string StewardPelt = "zhishi_pi";

        /// <summary>查勘使面具：面具类来源，持有型（`01_换皮与附身.md:117` R19）。</summary>
        public const string InspectorMask = "chakanshi_mianju";

        /// <summary>能力键：音乐解谜（只有乐正看得到正式表现形式）。</summary>
        public const string MusicPuzzleAbility = "music_puzzle";

        /// <summary>通行权限键：戏班全区。</summary>
        public const string StageAccess = "stage_all";

        /// <summary>六个身份的定义表。</summary>
        public static IdentityCatalog NewCatalog() => IdentityCatalog.From(new[]
        {
            IdentityDefinition.Create(Musician, "乐正", IdentityOrigin.Possession, "yuezheng_npc",
                new[] { MusicPuzzleAbility }, new[] { StageAccess }),
            IdentityDefinition.Create(Steward, "都知", IdentityOrigin.Possession, "duzhi_npc",
                new[] { "assign_patrol" }, new[] { StageAccess }),
            IdentityDefinition.Create(Apprentice, "学徒", IdentityOrigin.Possession, "xuetu_npc",
                null, new[] { StageAccess }),
            IdentityDefinition.Create(Clerk, "官证", IdentityOrigin.Possession, "guanzheng_npc",
                new[] { "edit_document" }, null),
            IdentityDefinition.Create(StewardPelt, "执事皮", IdentityOrigin.Pelt, "zhishi_npc",
                null, new[] { "fenfu_shrine" }),
            IdentityDefinition.Create(InspectorMask, "查勘使面具", IdentityOrigin.Mask, "chakanshi_npc",
                null, new[] { "patrol_mixed" }),
        });

        /// <summary>占位数值块（与默认资产同值；测试里显式改自己需要的那几项）。</summary>
        public static IdentitySettings NewSettings() => new IdentitySettings();

        // 强类型 id 常量：规则层的入参是 IdentityId，传裸字符串编译器会拦住
        // （格式不合法的文本连 IdentityId 都构造不出来，这正是「无效 id 明确行为」的第一道闸）。
        public static readonly IdentityId MusicianId = IdentityId.From(Musician);
        public static readonly IdentityId StewardId = IdentityId.From(Steward);
        public static readonly IdentityId ApprenticeId = IdentityId.From(Apprentice);
        public static readonly IdentityId ClerkId = IdentityId.From(Clerk);
        public static readonly IdentityId StewardPeltId = IdentityId.From(StewardPelt);
        public static readonly IdentityId InspectorMaskId = IdentityId.From(InspectorMask);
    }
}
