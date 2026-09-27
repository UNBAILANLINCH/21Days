// 职责：锁定序列帧小人生成规则——文件名解析、状态分组与帧排序、缺态报错（run 可选）、meta 解析、PPU / pivot / fps / 剪辑地速 / 画布尺寸校验。
// 新建原因：FramePuppetRules 是新增的纯规则类，按「被测类 + Tests」单独成文件。
using System.Collections.Generic;
using Game.Editor.CharacterPuppet;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.CharacterPuppet
{
    public sealed class FramePuppetRulesTests
    {
        [Test]
        public void TryParseFrameName_WhenWellFormed_ReturnsStateAndIndex()
        {
            string state;
            int index;
            Assert.That(FramePuppetRules.TryParseFrameName("chr_amiya_walk_07.png", "amiya", out state, out index), Is.True);
            Assert.That(state, Is.EqualTo("walk"));
            Assert.That(index, Is.EqualTo(7));
        }

        [Test]
        public void TryParseFrameName_WhenNameAndStateContainUnderscores_SplitsAtLastUnderscore()
        {
            string state;
            int index;
            Assert.That(FramePuppetRules.TryParseFrameName("chr_old_man_relax_idle_12.png", "old_man", out state, out index),
                Is.True);
            Assert.That(state, Is.EqualTo("relax_idle"));
            Assert.That(index, Is.EqualTo(12));
        }

        [TestCase("chr_amiya_walk_7.png")] // 序号只有一位
        [TestCase("chr_amiya_walk_00.png")] // 序号从 01 起
        [TestCase("chr_amiya_Walk_01.png")] // 大写
        [TestCase("chr_texas_walk_01.png")] // 别的角色
        [TestCase("chr_amiya_walk_01.jpg")] // 扩展名
        [TestCase("chr_amiya_01.png")] // 没有状态
        [TestCase("chr_amiya__01.png")] // 状态为空
        [TestCase("meta.json")]
        public void TryParseFrameName_WhenMalformed_ReturnsFalse(string fileName)
        {
            string state;
            int index;
            Assert.That(FramePuppetRules.TryParseFrameName(fileName, "amiya", out state, out index), Is.False);
        }

        [Test]
        public void GroupFrames_SortsByNumericIndexWithinEachState()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var files = new[]
            {
                "chr_a_walk_10.png", "chr_a_idle_02.png", "chr_a_walk_02.png", "chr_a_walk_01.png", "chr_a_idle_01.png",
                "chr_a_walk_03.png", "chr_a_walk_04.png", "chr_a_walk_05.png", "chr_a_walk_06.png", "chr_a_walk_07.png",
                "chr_a_walk_08.png", "chr_a_walk_09.png", "meta.json",
            };

            SortedDictionary<string, List<string>> groups = FramePuppetRules.GroupFrames("a", files, errors, warnings);

            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Is.Empty, "meta.json 不是 png，不该报忽略");
            Assert.That(groups.Keys, Is.EqualTo(new[] { "idle", "walk" }));
            Assert.That(groups["idle"], Is.EqualTo(new[] { "chr_a_idle_01.png", "chr_a_idle_02.png" }));
            Assert.That(groups["walk"][8], Is.EqualTo("chr_a_walk_09.png"));
            Assert.That(groups["walk"][9], Is.EqualTo("chr_a_walk_10.png"));
        }

        [Test]
        public void GroupFrames_WhenIndexDuplicated_ReportsError()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            FramePuppetRules.GroupFrames("a", new[] { "chr_a_idle_01.png", "chr_a_idle_001.png" }, errors, warnings);
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(errors[0], Does.Contain("重复"));
        }

        [Test]
        public void GroupFrames_WhenIndexHasGapOrUnknownPng_WarnsButKeepsFrames()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            SortedDictionary<string, List<string>> groups = FramePuppetRules.GroupFrames("a",
                new[] { "chr_a_idle_01.png", "chr_a_idle_03.png", "Idle Final.png" }, errors, warnings);
            Assert.That(errors, Is.Empty);
            Assert.That(groups["idle"].Count, Is.EqualTo(2));
            Assert.That(warnings.Count, Is.EqualTo(2));
            Assert.That(warnings.Exists(w => w.Contains("缺第 2 帧")), Is.True);
            Assert.That(warnings.Exists(w => w.Contains("Idle Final.png")), Is.True);
        }

        [Test]
        public void MissingStatesError_WhenIdleAndWalkPresent_ReturnsNull()
        {
            Assert.That(FramePuppetRules.MissingStatesError("a", new[] { "idle", "walk", "sit" }), Is.Null);
        }

        [Test]
        public void MissingStatesError_WithOrWithoutRun_OnlyRequiresIdleAndWalk()
        {
            Assert.That(FramePuppetRules.MissingStatesError("a", new[] { "idle", "walk" }), Is.Null);
            Assert.That(FramePuppetRules.MissingStatesError("a", new[] { "idle", "walk", "run" }), Is.Null);
            string error = FramePuppetRules.MissingStatesError("a", new[] { "idle" });
            Assert.That(error, Does.Not.Contain("run"), "缺 walk 的报错不该连带要求 run");
        }

        [Test]
        public void GroupFrames_WhenRunFramesPresent_NoWarnings()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            SortedDictionary<string, List<string>> groups = FramePuppetRules.GroupFrames("a",
                new[] { "chr_a_idle_01.png", "chr_a_walk_01.png", "chr_a_run_01.png", "chr_a_run_02.png" }, errors, warnings);
            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Is.Empty);
            Assert.That(FramePuppetRules.HasRunState(groups.Keys), Is.True);
        }

        [Test]
        public void HasRunState_WhenRunGroupPresent_ReturnsTrue()
        {
            Assert.That(FramePuppetRules.HasRunState(new[] { "idle", "walk" }), Is.False);
            Assert.That(FramePuppetRules.HasRunState(new[] { "idle", "walk", "run" }), Is.True);
            Assert.That(FramePuppetRules.HasRunState(null), Is.False);
        }

        [Test]
        public void ResolveGroundSpeed_WhenMetaHasGroundSpeed_ReadsIt()
        {
            FramePuppetMeta meta = FramePuppetRules.ParseMeta(
                "{\"animations\":{\"idle\":{\"frames\":12},\"walk\":{\"source\":\"Move\",\"groundSpeed\":2.5},"
                + "\"run\":{\"groundSpeed\":6}}}");
            Assert.That(FramePuppetRules.ResolveWalkGroundSpeed(meta), Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(FramePuppetRules.ResolveRunGroundSpeed(meta), Is.EqualTo(6f).Within(1e-5f));
        }

        [Test]
        public void ResolveGroundSpeed_WhenMissing_DefaultsToThreeAndFive()
        {
            // animations 有 walk 条目但没写 groundSpeed（现有方舟 meta 就是这样）、没有 run 条目、没有 animations、没有 meta。
            FramePuppetMeta noSpeed = FramePuppetRules.ParseMeta(
                "{\"fps\":12,\"animations\":{\"walk\":{\"source\":\"Move\",\"frames\":14}}}");
            Assert.That(FramePuppetRules.ResolveWalkGroundSpeed(noSpeed), Is.EqualTo(3f));
            Assert.That(FramePuppetRules.ResolveRunGroundSpeed(noSpeed), Is.EqualTo(5f));
            FramePuppetMeta empty = FramePuppetRules.ParseMeta("{}");
            Assert.That(FramePuppetRules.ResolveWalkGroundSpeed(empty), Is.EqualTo(3f));
            Assert.That(FramePuppetRules.ResolveRunGroundSpeed(empty), Is.EqualTo(5f));
            Assert.That(FramePuppetRules.ResolveWalkGroundSpeed(null), Is.EqualTo(3f));
            Assert.That(FramePuppetRules.ResolveRunGroundSpeed(null), Is.EqualTo(5f));
        }

        [Test]
        public void ResolveGroundSpeed_WhenNotPositive_FallsBackToDefault()
        {
            FramePuppetMeta meta = FramePuppetRules.ParseMeta(
                "{\"animations\":{\"walk\":{\"groundSpeed\":0},\"run\":{\"groundSpeed\":-2}}}");
            Assert.That(FramePuppetRules.ResolveWalkGroundSpeed(meta), Is.EqualTo(3f));
            Assert.That(FramePuppetRules.ResolveRunGroundSpeed(meta), Is.EqualTo(5f));
        }

        [Test]
        public void MissingStatesError_WhenWalkMissing_NamesStateAndExampleFile()
        {
            string error = FramePuppetRules.MissingStatesError("amiya", new[] { "idle" });
            Assert.That(error, Does.Contain("walk"));
            Assert.That(error, Does.Contain("chr_amiya_walk_01.png"));
            Assert.That(error, Does.Not.Contain("chr_amiya_idle_01.png"));
        }

        [Test]
        public void PixelsPerUnit_IsCanvasHeightOverTargetHeight()
        {
            Assert.That(FramePuppetRules.PixelsPerUnit(384, 1.6f), Is.EqualTo(240f).Within(1e-3f));
        }

        [Test]
        public void PixelsPerUnit_WhenTargetNotPositive_Throws()
        {
            Assert.That(() => FramePuppetRules.PixelsPerUnit(384, 0f), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void ResolvePivot_PrefersMetaPivot()
        {
            FramePuppetMeta meta = FramePuppetRules.ParseMeta(
                "{\"fps\":12,\"pivot\":{\"x\":0.5,\"y\":0.033854},\"pivotPx\":{\"x\":172,\"y\":13}}");
            Vector2 pivot = FramePuppetRules.ResolvePivot(meta, 344, 384);
            Assert.That(pivot.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(pivot.y, Is.EqualTo(0.033854f).Within(1e-5f));
        }

        [Test]
        public void ResolvePivot_FallsBackToPivotPxThenBottomCenter()
        {
            FramePuppetMeta pxOnly = FramePuppetRules.ParseMeta("{\"pivotPx\":{\"x\":100,\"y\":20}}");
            Vector2 fromPx = FramePuppetRules.ResolvePivot(pxOnly, 200, 400);
            Assert.That(fromPx.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(fromPx.y, Is.EqualTo(0.05f).Within(1e-5f));

            Vector2 none = FramePuppetRules.ResolvePivot(null, 200, 400);
            Assert.That(none, Is.EqualTo(new Vector2(0.5f, 0f)));
        }

        [Test]
        public void ResolveFps_RequestedThenMetaThenTwentyFour()
        {
            FramePuppetMeta meta = FramePuppetRules.ParseMeta("{\"fps\":12}");
            Assert.That(FramePuppetRules.ResolveFps(10f, meta), Is.EqualTo(10f));
            Assert.That(FramePuppetRules.ResolveFps(0f, meta), Is.EqualTo(12f));
            Assert.That(FramePuppetRules.ResolveFps(0f, FramePuppetRules.ParseMeta("{}")), Is.EqualTo(24f));
            Assert.That(FramePuppetRules.ResolveFps(0f, null), Is.EqualTo(24f));
        }

        [Test]
        public void ParseMeta_WhenEmptyOrInvalid_ReturnsNull()
        {
            Assert.That(FramePuppetRules.ParseMeta(""), Is.Null);
            Assert.That(FramePuppetRules.ParseMeta("not json"), Is.Null);
        }

        [Test]
        public void CanvasSizeError_WhenSizesDiffer_NamesBothFiles()
        {
            var names = new[] { "chr_a_idle_01.png", "chr_a_walk_01.png" };
            Assert.That(FramePuppetRules.CanvasSizeError(names, new[] { new Vector2Int(4, 8), new Vector2Int(4, 8) }),
                Is.Null);
            string error = FramePuppetRules.CanvasSizeError(names, new[] { new Vector2Int(4, 8), new Vector2Int(8, 8) });
            Assert.That(error, Does.Contain("chr_a_walk_01.png"));
        }

        [Test]
        public void AnimatorStateName_MapsIdleWalkRunAndKeepsOthers()
        {
            Assert.That(FramePuppetRules.AnimatorStateName("idle"), Is.EqualTo("Idle"));
            Assert.That(FramePuppetRules.AnimatorStateName("walk"), Is.EqualTo("Walk"));
            Assert.That(FramePuppetRules.AnimatorStateName("run"), Is.EqualTo("Run"));
            Assert.That(FramePuppetRules.AnimatorStateName("sit"), Is.EqualTo("sit"));
        }
    }
}
