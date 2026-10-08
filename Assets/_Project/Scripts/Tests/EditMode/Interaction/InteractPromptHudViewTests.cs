// 职责：钉住统一交互提示 HUD 的拼字符串纯函数（PRP/interaction §5 第 6 条、D10）：键位显示串为空回退「E」、
//   「动词 · 名字」拼接（名字为空只写动词、动词为空回退「交互」），BOSS 的「挑战」与物资箱的「打开 · 物资箱」；
//   胶囊宽度按文字在 [最小, 最大] 里伸缩（第二波，修「挑战 · 阶段一 BOSS（占位）」被截断）。
// 为什么新建：这组用例原在 DialogueInteractableTests 里测对白 HUD；HUD 随统一交互迁入 Interaction 并改名，按「一个被测类一个测试类」挪过来。
using Game.Interaction;
using NUnit.Framework;

namespace Game.Tests.EditMode.Interaction
{
    public sealed class InteractPromptHudViewTests
    {
        [TestCase(null, "E")]
        [TestCase("", "E")]
        [TestCase("   ", "E")]
        [TestCase("F", "F")]
        [TestCase(" Q ", "Q")]
        public void FormatKeyText_FallsBackToE_WhenBindingDisplayEmpty(string display, string expected)
        {
            Assert.That(InteractPromptHudView.FormatKeyText(display), Is.EqualTo(expected));
        }

        [TestCase("对话", "长老", "对话 · 长老")]
        [TestCase("对话", " 旅人 ", "对话 · 旅人")]
        [TestCase("对话", "", "对话")]
        [TestCase("对话", null, "对话")]
        [TestCase("挑战", "BOSS", "挑战 · BOSS")]
        [TestCase("打开", "物资箱", "打开 · 物资箱")]
        [TestCase(" 打开 ", "物资箱", "打开 · 物资箱")]
        [TestCase(null, "物资箱", "交互 · 物资箱")]
        [TestCase("", null, "交互")]
        public void FormatLabel_VerbAndName(string verb, string name, string expected)
        {
            Assert.That(InteractPromptHudView.FormatLabel(new InteractionPrompt(verb, name)), Is.EqualTo(expected));
        }

        // 胶囊宽度随文字自适应（第二波）：短提示保持最小 320，长提示伸长，超过最大 640 才省略。
        [TestCase(130f, 320f, 640f, 320f, TestName = "ResolveWidth_ShortText_KeepsMinimum")]
        [TestCase(400f, 320f, 640f, 400f, TestName = "ResolveWidth_BossChallenge_GrowsWithText")]
        [TestCase(640f, 320f, 640f, 640f, TestName = "ResolveWidth_ExactlyMaximum_NotClamped")]
        [TestCase(900f, 320f, 640f, 640f, TestName = "ResolveWidth_TooLong_StopsAtMaximum")]
        [TestCase(500f, 320f, 100f, 320f, TestName = "ResolveWidth_MaxBelowMin_UsesMinimum")]
        public void ResolveWidth_ClampsContentWidth(float content, float min, float max, float expected)
        {
            Assert.That(InteractPromptHudView.ResolveWidth(content, min, max), Is.EqualTo(expected));
        }
    }
}
