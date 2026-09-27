// 职责：回放小人（验证场景挂序列帧小人 Chibi_amiya）的待机 → 向右走 → 向左走 → 停下 → 走路 3 → 奔跑 5 → 停下，看动画切换、翻面与走跑步频差别。
//   Chibi_amiya 没有 run 帧：奔跑仍停在 Walk 态，只是 walk 剪辑提速到上限；换成有 run 帧的小人时奔跑一步应改断言 Run 态、Speed ≈ 1。
// 新建原因：CharacterPuppet 是新模块，按 module-verify.md 每模块一份 Showcase。
// 舞台是 SampleScene（不加载 Boot）：场景里现成的 amiya 挂在 player 下、由遭遇控制器每帧驱动，推它的根会和控制器打架；
//   所以回放自己从预制体实例化一只独立小人（根 → Visual（CameraBillboard）→ Chibi_amiya，与 player 同构），
//   放在玩家左前方的镜头内，Track() 交给收尾销毁，不动场景里的任何物体。
using System.Collections;
using Game.CharacterPuppet;
using Game.IsometricExploration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Showcase.CharacterPuppet
{
    [Category("Showcase")]
    public sealed class CharacterPuppetShowcase : ShowcaseScenario
    {
        private const float MoveSpeed = 1.5f;
        private const float MoveSeconds = 2f;
        private const float WalkSpeed = 3f;
        private const float RunSpeed = 5f;
        // 验证场景镜头固定，可见范围约起点左 0.5 到右 3.75：走路向右 3 单位、奔跑向左 3 单位回原点，不出画。
        private const float WalkSeconds = 1f;
        private const float RunSeconds = 0.6f;
        // 期望播放倍率 = clamp(速度 / 剪辑地速, rateMin 0.8, rateMax 1.6)；amiya 走路剪辑地速 3（meta 未写，取缺省）。
        // 走 3 → 1.0；跑 5 → 5/3 ≈ 1.67 夹到 1.6。随 ChibiPuppetConfig.asset 与预制体 walkClipSpeed 调参同步改。
        private const float ExpectedWalkRate = 1f;
        private const float ExpectedRunRate = 1.6f;
        private const float RateTolerance = 0.1f;
        private const string PuppetPrefabPath = "Assets/_Project/Prefabs/Characters/Chibi_amiya.prefab";

        /// <summary>
        /// 独立小人相对玩家的站位：左 1.5、靠镜头 1.5。各步累计位移最远向右 3 单位（走到玩家右前方）再回原点，
        /// 全程在跟随玩家的主相机画面里，也不和玩家纸片重叠。
        /// </summary>
        private static readonly Vector3 PuppetOffsetFromPlayer = new Vector3(-1.5f, 0f, -1.5f);
        private static readonly int SpeedParamHash = Animator.StringToHash("Speed");

        protected override string Module => "CharacterPuppet";
        protected override string ScenePath => ShowcaseOptions.DemoScenePath;
        protected override bool LoadBootScene => false;

        private bool moveDone;

        [UnityTest]
        public IEnumerator IdleWalkTurnStop_PlaysMatchingAnimation()
        {
            var player = FindRequired<Transform>("player");
            ChibiPuppet puppet = SpawnPuppet(player.position + PuppetOffsetFromPlayer);
            Transform root = puppet.transform.parent.parent;
            Coroutine move = null;
            try
            {
                yield return Step("原地站 2 秒", null, 2f);
                yield return Check("播放待机动画", () => IsState(puppet, "Idle"), 1f);
                yield return Snapshot("待机");

                yield return Step("以 1.5 单位/秒向右移动 2 秒", () =>
                    move = puppet.StartCoroutine(MoveRoot(root, Vector3.right * MoveSpeed, MoveSeconds)), 0f);
                yield return Check("切到走路动画且面朝右", () => IsState(puppet, "Walk") && puppet.transform.localScale.x > 0f, 1f);
                yield return Snapshot("向右走");
                yield return WaitUntil("向右移动结束", () => moveDone, MoveSeconds + 3f);

                yield return Step("以 1.5 单位/秒向左移动 2 秒", () =>
                    move = puppet.StartCoroutine(MoveRoot(root, Vector3.left * MoveSpeed, MoveSeconds)), 0f);
                yield return Check("翻面朝左并继续走路", () => IsState(puppet, "Walk") && puppet.transform.localScale.x < 0f, 1f);
                yield return Snapshot("向左走");
                yield return WaitUntil("向左移动结束", () => moveDone, MoveSeconds + 3f);

                yield return Step("停下", null, 1f);
                yield return Check("回到待机动画，保持朝左", () => IsState(puppet, "Idle") && puppet.transform.localScale.x < 0f, 1.5f);
                yield return Snapshot("停下待机");

                // 走跑步频：Animator 的 Speed 参数即 Walk / Run 状态的播放倍率，速度越快腿摆越快（有上限，不快放）。
                float walkRate = 0f;
                yield return Step("以 3 单位/秒向右移动 1 秒", () =>
                    move = puppet.StartCoroutine(MoveRoot(root, Vector3.right * WalkSpeed, WalkSeconds)), 0f);
                yield return Check("走路动画按原速播放，倍率约 1.0（误差 ±0.1）",
                    () => IsState(puppet, "Walk") && IsNear(GetWalkRate(puppet), ExpectedWalkRate), 1.5f);
                walkRate = GetWalkRate(puppet);
                yield return Snapshot("走路 3");
                yield return WaitUntil("走路移动结束", () => moveDone, WalkSeconds + 3f);

                yield return Step("以 5 单位/秒向左移动 0.6 秒（回到原点）", () =>
                    move = puppet.StartCoroutine(MoveRoot(root, Vector3.left * RunSpeed, RunSeconds)), 0f);
                yield return Check("无 run 帧：仍是 Walk 态，倍率夹到上限约 1.6（误差 ±0.1），且高于上一步走路倍率",
                    () => IsState(puppet, "Walk") && IsNear(GetWalkRate(puppet), ExpectedRunRate)
                        && GetWalkRate(puppet) > walkRate, 0.5f);
                yield return Snapshot("奔跑 5");
                yield return WaitUntil("奔跑移动结束", () => moveDone, RunSeconds + 3f);

                yield return Step("停下", null, 1f);
                yield return Check("回到待机动画", () => IsState(puppet, "Idle"), 1.5f);

                // 世界时停（对话期间 Time.timeScale = 0）：Animator 走 unscaled time，待机动画应继续播放而不是定格。
                yield return Step("触发世界时停（模拟对话）", () => Time.timeScale = 0f, 0f);
                try
                {
                    yield return Wait(0.5f);
                    float idleTimeBefore = GetIdleNormalizedTime(puppet);
                    for (int i = 0; i < 5; i++)
                    {
                        yield return null;
                    }

                    yield return Check("时停期间待机动画仍在播放（未定格）",
                        () => IsState(puppet, "Idle") && GetIdleNormalizedTime(puppet) > idleTimeBefore, 1f);
                    yield return Snapshot("时停待机");
                }
                finally
                {
                    // 无论检查是否通过都要恢复，避免这条用例把 timeScale 泄漏给同一批次的其他测试。
                    Time.timeScale = 1f;
                }
            }
            finally
            {
                if (move != null && puppet != null)
                {
                    puppet.StopCoroutine(move);
                }
            }
        }

        /// <summary>
        /// 造独立小人：根（被推的角色根）→ Visual（CameraBillboard 让纸片正对透视镜头）→ Chibi_amiya 预制体实例。
        /// ChibiPuppetMotion 沿父级跳过 Visual 取到根，读根的位移反推动画，与 player 下的结构一致。
        /// </summary>
        private ChibiPuppet SpawnPuppet(Vector3 position)
        {
            GameObject prefab = null;
#if UNITY_EDITOR
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PuppetPrefabPath);
#endif
            if (prefab == null)
            {
                Assert.Fail($"加载不到小人预制体 {PuppetPrefabPath}。");
            }

            var root = Track(new GameObject("ShowcasePuppet"));
            root.transform.position = position;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<CameraBillboard>();
            GameObject instance = Object.Instantiate(prefab, visual.transform, false);
            instance.name = "ShowcasePuppet_amiya";
            var puppet = instance.GetComponent<ChibiPuppet>();
            if (puppet == null)
            {
                Assert.Fail($"预制体 {PuppetPrefabPath} 根上没有 ChibiPuppet。");
            }

            return puppet;
        }

        private static bool IsState(ChibiPuppet puppet, string state) =>
            puppet.Animator != null && puppet.Animator.GetCurrentAnimatorStateInfo(0).IsName(state);

        // 读 ChibiPuppet 写进 Animator 的 Speed 参数（ChibiPuppet.SetMoving），即 Walk / Run 状态的实际播放倍率。
        private static float GetWalkRate(ChibiPuppet puppet) =>
            puppet.Animator == null ? 0f : puppet.Animator.GetFloat(SpeedParamHash);

        private static bool IsNear(float value, float expected) =>
            value >= expected - RateTolerance && value <= expected + RateTolerance;

        private static float GetIdleNormalizedTime(ChibiPuppet puppet) =>
            puppet.Animator == null ? 0f : puppet.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;

        // 逐帧推根节点，模拟角色移动；小人只从位移反推动画，不知道是谁在推。
        private IEnumerator MoveRoot(Transform root, Vector3 velocity, float seconds)
        {
            moveDone = false;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                root.position += velocity * Time.deltaTime;
            }

            moveDone = true;
        }
    }
}
