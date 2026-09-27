// 职责：钉住对白键位映射 DialogueKeyboardInput.Map——哪种状态下哪些键有效、选项期 Advance 被忽略、
//   弹窗期只放行弹窗键位、Choice N 越界与不可用行被忽略、主面板上 Esc 不关对白。
// 为什么新建：DialogueKeyboardInput 是新类；映射是纯函数，与 PlaybackPolicy（点击节奏）/ Rules（推进语义）的测试职责不同，
//   按「一个被测类一个测试类」新建。
using Game.Dialogue;
using NUnit.Framework;
using Key = Game.Dialogue.DialogueKeyboardInput.Key;
using Kind = Game.Dialogue.DialogueKeyboardInput.CommandKind;

namespace Game.Tests.EditMode.Dialogue
{
    public sealed class DialogueKeyboardInputTests
    {
        private static readonly bool[] NoRows = new bool[0];

        private static DialogueKeyboardInput.State Line(bool ready = true) =>
            new DialogueKeyboardInput.State(true, ready, false, false, false, NoRows);

        private static DialogueKeyboardInput.State Choosing(params bool[] rows) =>
            new DialogueKeyboardInput.State(true, true, true, false, false, rows);

        private static Kind MapKind(Key key, DialogueKeyboardInput.State state) =>
            DialogueKeyboardInput.Map(key, state).Kind;

        [TestCase(Key.Advance, Kind.Tap)]
        [TestCase(Key.Auto, Kind.ToggleAuto)]
        [TestCase(Key.Speed, Kind.CycleSpeed)]
        [TestCase(Key.Skip, Kind.RequestSkip)]
        [TestCase(Key.History, Kind.OpenHistory)]
        [TestCase(Key.Cancel, Kind.None)]
        [TestCase(Key.Choice1, Kind.None)]
        public void Map_OnLine_KeysMatchButtons(Key key, Kind expected)
        {
            Assert.That(MapKind(key, Line()), Is.EqualTo(expected));
        }

        [TestCase(Key.Advance)]
        [TestCase(Key.Auto)]
        [TestCase(Key.History)]
        [TestCase(Key.Choice1)]
        public void Map_WhenInactive_IgnoresEverything(Key key)
        {
            var inactive = new DialogueKeyboardInput.State(false, true, true, false, false, new[] { true });

            Assert.That(MapKind(key, inactive), Is.EqualTo(Kind.None));
        }

        [Test]
        public void Map_AdvanceBeforeReady_IsIgnored()
        {
            Assert.That(MapKind(Key.Advance, Line(ready: false)), Is.EqualTo(Kind.None));
        }

        [Test]
        public void Map_AdvanceWhileChoicesShown_IsIgnored()
        {
            // Enter 同时是 UI Submit：选项期若再推进，会与「点中选中的选项」叠在一起。
            Assert.That(MapKind(Key.Advance, Choosing(true, true)), Is.EqualTo(Kind.None));
        }

        [Test]
        public void Map_ChoiceKeys_PickShownRowByIndex()
        {
            DialogueKeyboardInput.Command second = DialogueKeyboardInput.Map(Key.Choice2, Choosing(true, true, true));

            Assert.That(second.Kind, Is.EqualTo(Kind.Choose));
            Assert.That(second.Row, Is.EqualTo(1));
            Assert.That(DialogueKeyboardInput.Map(Key.Choice4, Choosing(true, true, true, true)).Row, Is.EqualTo(3));
        }

        [Test]
        public void Map_ChoiceKeyBeyondShownRows_IsIgnored()
        {
            Assert.That(MapKind(Key.Choice3, Choosing(true, true)), Is.EqualTo(Kind.None));
            Assert.That(MapKind(Key.Choice1, Choosing()), Is.EqualTo(Kind.None));
        }

        [Test]
        public void Map_ChoiceKeyOnUnavailableRow_IsIgnored()
        {
            Assert.That(MapKind(Key.Choice2, Choosing(true, false, true)), Is.EqualTo(Kind.None));
            Assert.That(MapKind(Key.Choice3, Choosing(true, false, true)), Is.EqualTo(Kind.Choose));
        }

        [Test]
        public void Map_ChoiceKeyWithNullRows_IsIgnored()
        {
            var state = new DialogueKeyboardInput.State(true, true, true, false, false, null);

            Assert.That(MapKind(Key.Choice1, state), Is.EqualTo(Kind.None));
        }

        [TestCase(Key.History, Kind.CloseHistory)]
        [TestCase(Key.Cancel, Kind.CloseHistory)]
        [TestCase(Key.Advance, Kind.None)]
        [TestCase(Key.Choice1, Kind.None)]
        [TestCase(Key.Auto, Kind.None)]
        [TestCase(Key.Speed, Kind.None)]
        [TestCase(Key.Skip, Kind.None)]
        public void Map_WhileHistoryOpen_OnlyHistoryKeysWork(Key key, Kind expected)
        {
            var state = new DialogueKeyboardInput.State(true, true, true, true, false, new[] { true });

            Assert.That(MapKind(key, state), Is.EqualTo(expected));
        }

        [TestCase(Key.Cancel, Kind.CancelSkip)]
        [TestCase(Key.Advance, Kind.None)]
        [TestCase(Key.Choice1, Kind.None)]
        [TestCase(Key.Skip, Kind.None)]
        [TestCase(Key.History, Kind.None)]
        [TestCase(Key.Auto, Kind.None)]
        public void Map_WhileSkipConfirmOpen_OnlyCancelWorks(Key key, Kind expected)
        {
            var state = new DialogueKeyboardInput.State(true, true, true, false, true, new[] { true });

            Assert.That(MapKind(key, state), Is.EqualTo(expected));
        }
    }
}
