// 职责：当前身份决定NPC回应和取衣选项，旧调查记录不能绕过身份。
using Game.Clothing;
using Game.Core.Telemetry;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests.EditMode.Clothing
{
    public sealed class ClothingControllerTests
    {
        [Test]
        public void Tailor_RecognizesCustomers_OffersOnlyUnclaimedOrder()
        {
            var config = ScriptableObject.CreateInstance<ClothingConfig>();
            try
            {
                var controller = new ClothingController(config, NullTelemetryScope.Instance);
                Assert.That(controller.TailorReply(""), Is.EqualTo(config.VisitorReply));
                for (int i = 0; i < 3; i++)
                {
                    string role = "customer" + i;
                    Assert.That(controller.TailorReply(role), Is.EqualTo(config.TailorReplyAt(i)));
                    Assert.That(controller.OffersClothes(role), Is.EqualTo(i == 1));
                }
                controller.Apply(1, false, "customer1");
                Assert.That(controller.Apply(1, false, "customer0"), Is.Not.EqualTo(config.TailorReplyAt(1)));
                Assert.That(controller.Apply(1, true, "customer0"), Is.Not.EqualTo(config.HandoverReply));
                Assert.That(controller.Rules.Received, Is.False);
                Assert.That(controller.Apply(1, true, "customer1"), Is.EqualTo(config.HandoverReply));
                Assert.That(controller.TailorReply("customer1"), Is.EqualTo(config.ReceivedReply));
                Assert.That(controller.OffersClothes("customer1"), Is.False);
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
