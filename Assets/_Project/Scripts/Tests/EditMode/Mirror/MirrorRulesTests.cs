// 职责：钉住 MirrorRules 的照镜判定——扇形内外、距离边界、最近者优先、人 / 物 / 妖线索齐 / 妖线索缺、无目标、自照恒空白、
//   followsMonster 取模型位置、线索是否齐、辨认记录的写入与去重（PRD V1 V2 V3 V4 V5 规则侧）。
// 为什么新建：Mirror 模块首次落地，一个被测类一个测试类；纯规则不经容器与场景。
using System;
using System.Collections.Generic;
using Game.Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Mirror
{
    /// <summary><see cref="MirrorRules"/> 的 EditMode 测试。玩家在原点、朝 +X、作用距离 5、半角 35°。</summary>
    public sealed class MirrorRulesTests
    {
        private const float Range = 5f;
        private const float HalfAngle = 35f;
        private static readonly Func<int, bool> AllClues = _ => true;
        private static readonly Func<int, bool> NoClues = _ => false;

        [Test]
        public void Resolve_HumanInsideFan_ReturnsHuman()
        {
            MirrorResult result = Resolve(AllClues, Candidate(3f, 0f, MirrorSubjectKind.Human));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Human));
            Assert.That(result.CandidateIndex, Is.EqualTo(0));
            Assert.That(result.YaoId, Is.EqualTo(0));
            Assert.That(result.Distance, Is.EqualTo(3f).Within(1e-4f));
        }

        [Test]
        public void Resolve_ObjectInsideFan_ReturnsObject()
        {
            MirrorResult result = Resolve(AllClues, Candidate(2f, 0.5f, MirrorSubjectKind.Object));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Object));
        }

        [Test]
        public void Resolve_YaoWithCluesHeld_ReturnsTrueForm()
        {
            MirrorResult result = Resolve(AllClues, Candidate(2f, 0f, MirrorSubjectKind.Yao, 2));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.TrueForm));
            Assert.That(result.YaoId, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_YaoWithCluesMissing_ReturnsBlurry()
        {
            MirrorResult result = Resolve(NoClues, Candidate(2f, 0f, MirrorSubjectKind.Yao, 2));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Blurry));
            Assert.That(result.YaoId, Is.EqualTo(2), "模糊轮廓也要带妖 id，服务才能记「见过轮廓」");
        }

        [Test]
        public void Resolve_YaoWhenClueDelegateNull_ReturnsBlurry()
        {
            MirrorResult result = Resolve(null, Candidate(2f, 0f, MirrorSubjectKind.Yao, 1));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Blurry));
        }

        [Test]
        public void Resolve_HasCluesAskedWithCandidateYaoId()
        {
            int asked = -1;
            Resolve(id => { asked = id; return true; }, Candidate(2f, 0f, MirrorSubjectKind.Yao, 7));

            Assert.That(asked, Is.EqualTo(7));
        }

        [Test]
        public void Resolve_TargetBehindPlayer_ReturnsNothing()
        {
            MirrorResult result = Resolve(AllClues, Candidate(-2f, 0f, MirrorSubjectKind.Human));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Nothing));
            Assert.That(result.HasTarget, Is.False);
        }

        [Test]
        public void Resolve_TargetJustInsideHalfAngle_IsHit()
        {
            MirrorResult result = Resolve(AllClues, AtAngle(3f, 30f, MirrorSubjectKind.Human));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Human));
        }

        [Test]
        public void Resolve_TargetJustOutsideHalfAngle_ReturnsNothing()
        {
            MirrorResult result = Resolve(AllClues, AtAngle(3f, 40f, MirrorSubjectKind.Human));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Nothing));
        }

        [Test]
        public void Resolve_TargetExactlyAtRange_IsHit()
        {
            MirrorResult result = Resolve(AllClues, Candidate(Range, 0f, MirrorSubjectKind.Human));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Human), "作用距离含边界");
        }

        [Test]
        public void Resolve_TargetBeyondRange_ReturnsNothing()
        {
            MirrorResult result = Resolve(AllClues, Candidate(Range + 0.01f, 0f, MirrorSubjectKind.Human));

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Nothing));
        }

        [Test]
        public void Resolve_ZeroRange_ReturnsNothingEvenAtFeet()
        {
            var query = new MirrorQuery(Vector2.zero, Vector2.right, 0f, HalfAngle,
                new[] { Candidate(0f, 0f, MirrorSubjectKind.Human) }, AllClues);

            Assert.That(MirrorRules.Resolve(query).Kind, Is.EqualTo(MirrorResultKind.Nothing));
        }

        [Test]
        public void Resolve_SeveralInFan_PicksNearest()
        {
            MirrorResult result = Resolve(AllClues,
                Candidate(4f, 0f, MirrorSubjectKind.Human),
                Candidate(1.5f, 0.3f, MirrorSubjectKind.Yao, 1),
                Candidate(3f, 0f, MirrorSubjectKind.Object));

            Assert.That(result.CandidateIndex, Is.EqualTo(1));
            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.TrueForm));
        }

        [Test]
        public void Resolve_NearerButOutsideFan_IsSkipped()
        {
            MirrorResult result = Resolve(AllClues,
                Candidate(0f, 1f, MirrorSubjectKind.Human),
                Candidate(4f, 0f, MirrorSubjectKind.Object));

            Assert.That(result.CandidateIndex, Is.EqualTo(1), "正侧方更近的对象不在扇形内，不抢目标");
        }

        [Test]
        public void Resolve_EqualDistance_PicksEarlierInList()
        {
            MirrorResult result = Resolve(AllClues,
                Candidate(3f, 0f, MirrorSubjectKind.Object),
                Candidate(3f, 0f, MirrorSubjectKind.Human));

            Assert.That(result.CandidateIndex, Is.EqualTo(0));
        }

        [Test]
        public void Resolve_NoCandidates_ReturnsNothing()
        {
            Assert.That(Resolve(AllClues).Kind, Is.EqualTo(MirrorResultKind.Nothing));
            var nullList = new MirrorQuery(Vector2.zero, Vector2.right, Range, HalfAngle, null, AllClues);
            Assert.That(MirrorRules.Resolve(nullList).Kind, Is.EqualTo(MirrorResultKind.Nothing));
        }

        [Test]
        public void Resolve_FacingNotNormalized_StillUsesDirectionOnly()
        {
            var query = new MirrorQuery(Vector2.zero, new Vector2(0f, 10f), Range, HalfAngle,
                new[] { Candidate(0f, 3f, MirrorSubjectKind.Human) }, AllClues);

            Assert.That(MirrorRules.Resolve(query).Kind, Is.EqualTo(MirrorResultKind.Human));
        }

        [Test]
        public void Resolve_ZeroFacing_OnlyTargetAtFeetIsHit()
        {
            var ahead = new MirrorQuery(Vector2.zero, Vector2.zero, Range, HalfAngle,
                new[] { Candidate(2f, 0f, MirrorSubjectKind.Human) }, AllClues);
            var atFeet = new MirrorQuery(Vector2.zero, Vector2.zero, Range, HalfAngle,
                new[] { Candidate(0f, 0f, MirrorSubjectKind.Human) }, AllClues);

            Assert.That(MirrorRules.Resolve(ahead).Kind, Is.EqualTo(MirrorResultKind.Nothing));
            Assert.That(MirrorRules.Resolve(atFeet).Kind, Is.EqualTo(MirrorResultKind.Human));
        }

        [Test]
        public void Resolve_OriginOffset_UsesRelativePosition()
        {
            var query = new MirrorQuery(new Vector2(10f, 10f), Vector2.up, Range, HalfAngle,
                new[] { Candidate(10f, 13f, MirrorSubjectKind.Human), Candidate(3f, 0f, MirrorSubjectKind.Object) },
                AllClues);

            MirrorResult result = MirrorRules.Resolve(query);

            Assert.That(result.CandidateIndex, Is.EqualTo(0));
        }

        [Test]
        public void ResolveSelf_AlwaysBlank()
        {
            MirrorResult result = MirrorRules.ResolveSelf();

            Assert.That(result.Kind, Is.EqualTo(MirrorResultKind.Self));
            Assert.That(result.HasTarget, Is.False);
            Assert.That(result.YaoId, Is.EqualTo(0));
        }

        [Test]
        public void CandidatePosition_FollowsMonster_UsesModelPosition()
        {
            var projected = new Vector2(3f, 4f);
            var model = new Vector2(-1f, 8f);

            Assert.That(MirrorRules.CandidatePosition(true, projected, model), Is.EqualTo(model));
            Assert.That(MirrorRules.CandidatePosition(false, projected, model), Is.EqualTo(projected));
        }

        [Test]
        public void CluesSatisfied_EmptyRequirement_IsTrue()
        {
            Assert.That(MirrorRules.CluesSatisfied(new List<int>(), null), Is.True);
            Assert.That(MirrorRules.CluesSatisfied(null, new Dictionary<int, int>()), Is.True);
        }

        [Test]
        public void CluesSatisfied_AllHeld_IsTrue()
        {
            var items = new Dictionary<int, int> { { 1005, 1 }, { 1001, 3 } };

            Assert.That(MirrorRules.CluesSatisfied(new List<int> { 1005, 1001 }, items), Is.True);
        }

        [Test]
        public void CluesSatisfied_MissingOrZeroCount_IsFalse()
        {
            var items = new Dictionary<int, int> { { 1005, 0 }, { 1001, 1 } };

            Assert.That(MirrorRules.CluesSatisfied(new List<int> { 1005 }, items), Is.False, "数量 0 不算持有");
            Assert.That(MirrorRules.CluesSatisfied(new List<int> { 1001, 1006 }, items), Is.False, "缺一件不算齐");
            Assert.That(MirrorRules.CluesSatisfied(new List<int> { 1001 }, null), Is.False);
        }

        [Test]
        public void Record_TrueForm_FirstTimeOnly()
        {
            var data = new MirrorSaveData();
            var result = new MirrorResult(MirrorResultKind.TrueForm, 0, 2, 1f);

            Assert.That(MirrorRules.Record(data, result), Is.True);
            Assert.That(MirrorRules.Record(data, result), Is.False, "再次照见不算首次");
            Assert.That(data.Identified, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void Record_Blurry_AddsGlimpseOnceAndIsNotIdentified()
        {
            var data = new MirrorSaveData();
            var result = new MirrorResult(MirrorResultKind.Blurry, 0, 2, 1f);

            Assert.That(MirrorRules.Record(data, result), Is.False);
            MirrorRules.Record(data, result);

            Assert.That(data.GlimpsedBlurry, Is.EqualTo(new[] { 2 }));
            Assert.That(data.Identified, Is.Empty);
        }

        [Test]
        public void Record_SelfAndHuman_OnlySelfCounts()
        {
            var data = new MirrorSaveData();

            MirrorRules.Record(data, MirrorResult.Self);
            MirrorRules.Record(data, new MirrorResult(MirrorResultKind.Human, 0, 0, 1f));
            MirrorRules.Record(data, MirrorResult.Nothing);

            Assert.That(data.SelfLooks, Is.EqualTo(1));
            Assert.That(data.Identified, Is.Empty);
            Assert.That(data.GlimpsedBlurry, Is.Empty);
        }

        [Test]
        public void Record_NullListsOrNullData_DoesNotThrow()
        {
            var data = new MirrorSaveData { Identified = null, GlimpsedBlurry = null };

            Assert.That(MirrorRules.Record(data, new MirrorResult(MirrorResultKind.TrueForm, 0, 1, 1f)), Is.True);
            MirrorRules.Record(data, new MirrorResult(MirrorResultKind.Blurry, 0, 2, 1f));
            Assert.That(MirrorRules.Record(null, MirrorResult.Self), Is.False);
            Assert.That(data.Identified, Is.EqualTo(new[] { 1 }));
            Assert.That(data.GlimpsedBlurry, Is.EqualTo(new[] { 2 }));
        }

        private static MirrorResult Resolve(Func<int, bool> hasClues, params MirrorCandidate[] candidates)
        {
            var query = new MirrorQuery(Vector2.zero, Vector2.right, Range, HalfAngle, candidates, hasClues);
            return MirrorRules.Resolve(query);
        }

        private static MirrorCandidate Candidate(float x, float y, MirrorSubjectKind kind, int yaoId = 0) =>
            new MirrorCandidate(new Vector2(x, y), kind, yaoId);

        // 以 +X 为 0°、逆时针为正，放在给定距离与角度上。
        private static MirrorCandidate AtAngle(float distance, float degrees, MirrorSubjectKind kind)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new MirrorCandidate(new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance, kind, 0);
        }
    }
}
