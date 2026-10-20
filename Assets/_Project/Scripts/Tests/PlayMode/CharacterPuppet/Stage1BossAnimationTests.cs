// 纯规则不能验证真实Animator与暂停下的命中帧；在隔离舞台复用正式预制体和StrikeAsync。
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Battle;
using Game.CharacterPuppet;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests.PlayMode.CharacterPuppet
{
    public sealed class Stage1BossAnimationTests
    {
        [UnityTest]
        public IEnumerator PausedStage_AttacksTwiceHitsOnceEachAndCancelsBeforeImpact()
        {
            float timeScale = Time.timeScale;
            var root = new GameObject("BossAnimationTest");
            try
            {
                Time.timeScale = 0;
                var attacker = new GameObject("Attacker").AddComponent<BattleActor>();
                attacker.transform.SetParent(root.transform);
                var target = new GameObject("Target").AddComponent<BattleActor>();
                target.transform.SetParent(root.transform);
                target.transform.position = Vector3.left * 3;
                var stage = root.AddComponent<BattleStage>();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Characters/Chibi_stage1_boss.prefab");
                Assert.That(prefab, Is.Not.Null);
                attacker.Mount(prefab);
                var puppet = attacker.GetComponentInChildren<ChibiPuppet>();
                yield return null;
                float before = puppet.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(puppet.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(before));
                Assert.That(puppet.GetComponent<ChibiPuppetMotion>().enabled, Is.False);
                var hitFrame = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Characters/stage1_boss/chr_stage1_boss_attack_13.png");
                int impacts = 0;
                foreach (bool heavy in new[] { false, true })
                {
                    int previous = impacts;
                    yield return stage.StrikeAsync(attacker, target, heavy, () =>
                    {
                        impacts++;
                        Assert.That(puppet.Animator.GetCurrentAnimatorStateInfo(0).IsName("attack"), Is.True);
                        Assert.That(puppet.GetComponentInChildren<SpriteRenderer>().sprite, Is.SameAs(hitFrame), "命中定位到出手帧");
                    }, CancellationToken.None).ToCoroutine();
                    Assert.That(impacts, Is.EqualTo(previous + 1));
                    Assert.That(puppet.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
                    Assert.That(Vector3.Distance(attacker.transform.position, attacker.HomePosition), Is.LessThan(.001f));
                }
                using (var cancellation = new CancellationTokenSource())
                {
                    var pending = stage.StrikeAsync(attacker, target, false, () => impacts++, cancellation.Token);
                    yield return new WaitForSecondsRealtime(.05f);
                    cancellation.Cancel();
                    Exception error = null;
                    yield return pending.ToCoroutine(ex => error = ex);
                    Assert.That(error, Is.InstanceOf<OperationCanceledException>());
                    Assert.That(impacts, Is.EqualTo(2), "预备中取消没有提交命中");
                    Assert.That(puppet.Animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
                }
            }
            finally
            {
                Time.timeScale = timeScale;
                Object.Destroy(root);
            }
        }
    }
}
#endif
