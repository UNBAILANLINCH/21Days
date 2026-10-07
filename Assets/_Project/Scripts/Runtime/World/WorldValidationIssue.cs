// 职责：世界表的一条校验问题——哪一行、哪个字段、错在哪、去哪改。
// 为什么新建：一个文件一个类（csharp-code.md「一个文件一个类，文件名等于类名」）。
namespace Game.World
{
    /// <summary>世界表的一条校验问题。消息里必须带「行、原值、去改哪张表」，否则排查要回去读表。</summary>
    public sealed class WorldValidationIssue
    {
        public WorldValidationIssue(string row, string message)
        {
            Row = row;
            Message = message;
        }

        /// <summary>出问题的行（如 <c>TbScene.human_jingyang</c>）。</summary>
        public string Row { get; }

        /// <summary>问题说明（人话，带原值与修法）。</summary>
        public string Message { get; }

        public override string ToString() => Row + "：" + Message;
    }
}
