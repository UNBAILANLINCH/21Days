// 职责：验证切人只改变交互测距锚点，原本体不移动且读方收到变化。
using Game.Interaction;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests.EditMode.Interaction
{
    public sealed class InteractionActorAnchorTests
    {
        [Test]
        public void SetAnchor_ChangesRegistryNotificationWithoutMovingBody_AndDisposeUnsubscribes()
        {
            var body = new GameObject("body"); var borrowed = new GameObject("borrowed");
            var actor = body.AddComponent<InteractionActor>(); var registry = new InteractionRegistry(() => null);
            try
            {
                registry.SetActor(actor); int changed = 0; registry.OnActorChanged += _ => changed++;
                borrowed.transform.position = Vector3.right * 20;
                actor.SetAnchor(borrowed.transform);
                Assert.That(actor.Anchor, Is.SameAs(borrowed.transform));
                Assert.That(body.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(changed, Is.EqualTo(1));
                actor.SetAnchor(borrowed.transform); Assert.That(changed, Is.EqualTo(1));
                actor.SetAnchor(null); Assert.That(actor.Anchor, Is.SameAs(body.transform));
                registry.Dispose(); actor.SetAnchor(borrowed.transform); Assert.That(changed, Is.EqualTo(2));
            }
            finally { registry.Dispose(); Object.DestroyImmediate(body); Object.DestroyImmediate(borrowed); }
        }
    }
}
