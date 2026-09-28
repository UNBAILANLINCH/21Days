// 职责：钉住 MirrorCrackTracker——遭遇外不判定、进入遭遇取基线不发裂痕、击中裂痕增加才算新裂、剧情裂痕只刷新、
//   镜碎每次遭遇只触发一次、重进遭遇后清零、读到三裂存档进场即碎（PRD V8 V10 的防重入与时机）。
// 为什么新建：一个被测类一个测试类；跟踪器是纯 C#，不经容器与场景。
using Game.Mirror;
using NUnit.Framework;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorCrackTracker"/> 的 EditMode 测试。</summary>
    public sealed class MirrorCrackTrackerTests
    {
        [Test]
        public void Update_Inactive_DoesNothing()
        {
            var tracker = new MirrorCrackTracker();

            bool changed = tracker.Update(false, 3, 0, out bool cracked, out bool shattered);

            Assert.That(changed, Is.False);
            Assert.That(cracked, Is.False);
            Assert.That(shattered, Is.False, "遭遇开始前 Health 为 0 会被算成三裂，不能据此镜碎");
            Assert.That(tracker.Tracking, Is.False);
        }

        [Test]
        public void Update_EnterEncounter_TakesBaselineWithoutCrackEvent()
        {
            var tracker = new MirrorCrackTracker();

            bool changed = tracker.Update(true, 1, 2, out bool cracked, out bool shattered);

            Assert.That(changed, Is.True, "进入遭遇要刷新一次显示");
            Assert.That(cracked, Is.False, "基线不算新裂");
            Assert.That(shattered, Is.False);
            Assert.That(tracker.HitCracks, Is.EqualTo(1));
            Assert.That(tracker.StoryCracks, Is.EqualTo(2));
        }

        [Test]
        public void Update_HitIncrease_Cracked()
        {
            var tracker = new MirrorCrackTracker();
            tracker.Update(true, 0, 0, out _, out _);

            bool changed = tracker.Update(true, 1, 0, out bool cracked, out bool shattered);

            Assert.That(changed, Is.True);
            Assert.That(cracked, Is.True);
            Assert.That(shattered, Is.False);
        }

        [Test]
        public void Update_NoChange_NothingToDo()
        {
            var tracker = new MirrorCrackTracker();
            tracker.Update(true, 1, 0, out _, out _);

            bool changed = tracker.Update(true, 1, 0, out bool cracked, out bool shattered);

            Assert.That(changed, Is.False);
            Assert.That(cracked, Is.False);
            Assert.That(shattered, Is.False);
        }

        [Test]
        public void Update_StoryCrackOnly_ChangedButNotCracked()
        {
            var tracker = new MirrorCrackTracker();
            tracker.Update(true, 0, 0, out _, out _);

            bool changed = tracker.Update(true, 0, 1, out bool cracked, out bool shattered);

            Assert.That(changed, Is.True, "剧情裂痕要刷新可见范围");
            Assert.That(cracked, Is.False, "剧情裂痕不发击中裂痕事件");
            Assert.That(shattered, Is.False);
        }

        [Test]
        public void Update_HitDecrease_ChangedButNotCracked()
        {
            var tracker = new MirrorCrackTracker();
            tracker.Update(true, 2, 0, out _, out _);

            bool changed = tracker.Update(true, 1, 0, out bool cracked, out _);

            Assert.That(changed, Is.True);
            Assert.That(cracked, Is.False);
        }

        [Test]
        public void Update_ThirdHit_ShattersOnce()
        {
            var tracker = new MirrorCrackTracker();
            tracker.Update(true, 2, 0, out _, out _);

            tracker.Update(true, 3, 0, out bool cracked, out bool shattered);
            tracker.Update(true, 3, 0, out _, out bool again);

            Assert.That(cracked, Is.True);
            Assert.That(shattered, Is.True);
            Assert.That(again, Is.False, "同一场遭遇只碎一次");
            Assert.That(tracker.ShatterRaised, Is.True);
        }

        [Test]
        public void Update_Reentered_ResetsShatter()
        {
            var tracker = new MirrorCrackTracker();
            tracker.Update(true, 3, 0, out _, out bool first);
            tracker.Update(false, 3, 0, out _, out _);

            tracker.Update(true, 0, 0, out bool cracked, out bool shattered);
            tracker.Update(true, 3, 0, out _, out bool second);

            Assert.That(first, Is.True, "进场即三裂（读到战败存档）也要碎");
            Assert.That(cracked, Is.False);
            Assert.That(shattered, Is.False);
            Assert.That(second, Is.True, "重开后再次三裂要能再碎");
        }

        [Test]
        public void Update_ManyStoryCracks_NeverShatter()
        {
            var tracker = new MirrorCrackTracker();
            tracker.Update(true, 0, 0, out _, out _);

            tracker.Update(true, 0, 9, out _, out bool shattered);

            Assert.That(shattered, Is.False, "剧情裂痕不计入三裂");
        }
    }
}
