using System.Reflection;
using Game.LailaFace;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Tests.EditMode.LailaFace
{
    public sealed class FaceDragHandleTests
    {
        private GameObject root;
        private Mesh mesh;
        private FaceBlendShapeController face;
        private EventSystem events;
        private PhysicsRaycaster raycaster;

        private float RangePixels => Mathf.Max(40f, Mathf.Min(Screen.width, Screen.height) * 0.12f); // lint-ok: 匹配拖拽表现换算的测试输入

        [SetUp]
        public void SetUp()
        {
            root = new GameObject(nameof(FaceDragHandleTests));
            events = Child("Events").AddComponent<EventSystem>();
            GameObject camera = Child("Camera");
            camera.AddComponent<Camera>();
            raycaster = camera.AddComponent<PhysicsRaycaster>();
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.triangles = new[] { 0, 1, 2 };
            foreach (string name in new[]
            {
                "Mouth_L_Up", "Mouth_L_Down", "Mouth_R_Up", "Mouth_R_Down",
                "Mouth_L_Out", "Mouth_L_In", "Mouth_R_Out", "Mouth_R_In",
                "Mouth_UpperLip_Up.001", "Mouth_LowerLip_Down.001",
                "Brow_L_Mid_Up", "Brow_L_Mid_Down", "Mouth_UpperLipL_Up"
            })
            {
                mesh.AddBlendShapeFrame(name, 100f, new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector3[3], new Vector3[3]);
            }

            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            face = root.AddComponent<FaceBlendShapeController>();
            using (var serialized = new SerializedObject(face))
            {
                serialized.FindProperty("faceRenderer").objectReferenceValue = renderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            face.RebuildCache();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
        }

        [TestCase(FaceDragHandle.FaceControl.UpperLip, "Mouth_UpperLip_Up.001", 1f)]
        [TestCase(FaceDragHandle.FaceControl.LowerLip, "Mouth_LowerLip_Down.001", -1f)]
        public void Drag_SingleLip_ClampsAndReturnsToBasis(FaceDragHandle.FaceControl control, string shape, float direction)
        {
            FaceDragHandle handle = Handle(control);
            PointerEventData pointer = Pointer();
            handle.OnPointerDown(pointer);
            pointer.position = Vector2.up * (RangePixels * 2f * direction);
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight(shape), Is.EqualTo(100f).Within(0.001f));
            handle.OnPointerUp(pointer);

            handle.OnPointerDown(pointer);
            pointer.position -= Vector2.up * (RangePixels * 2f * direction);
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight(shape), Is.EqualTo(0f).Within(0.001f));
            Assert.That(handle.enabled, Is.True);
        }

        [TestCase(FaceDragHandle.FaceControl.MouthLeft, "Mouth_L", -1f)]
        [TestCase(FaceDragHandle.FaceControl.MouthRight, "Mouth_R", 1f)]
        public void Drag_MouthCorner_CombinesIndependentAxes(FaceDragHandle.FaceControl control, string prefix, float direction)
        {
            FaceDragHandle handle = Handle(control, horizontal: true, invertHorizontal: direction < 0f);
            PointerEventData pointer = Pointer();
            handle.OnPointerDown(pointer);
            pointer.position = new Vector2(direction * RangePixels * 0.5f, RangePixels * 0.5f);
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight(prefix + "_Up"), Is.EqualTo(50f).Within(0.001f));
            Assert.That(face.GetWeight(prefix + "_Out"), Is.EqualTo(50f).Within(0.001f));
            pointer.position = new Vector2(-direction * RangePixels * 2f, -RangePixels * 2f);
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight(prefix + "_Down"), Is.EqualTo(100f).Within(0.001f));
            Assert.That(face.GetWeight(prefix + "_In"), Is.EqualTo(100f).Within(0.001f));
            Assert.That(face.GetWeight(prefix + "_Up"), Is.Zero);
            Assert.That(face.GetWeight(prefix + "_Out"), Is.Zero);
        }

        [Test]
        public void Drag_ModelOverrides_UsesNewBrowAndLipWithoutChangingLegacyShapes()
        {
            FaceDragHandle brow = Handle(FaceDragHandle.FaceControl.BrowLeft,
                upOverride: "Brow_L_Mid_Up", downOverride: "Brow_L_Mid_Down");
            PointerEventData pointer = Pointer();
            brow.OnPointerDown(pointer);
            pointer.position = Vector2.up * RangePixels;
            brow.OnDrag(pointer);
            Assert.That(face.GetWeight("Brow_L_Mid_Up"), Is.EqualTo(100f));
            pointer.position = Vector2.down * RangePixels;
            brow.OnDrag(pointer);
            Assert.That(face.GetWeight("Brow_L_Mid_Up"), Is.Zero);
            Assert.That(face.GetWeight("Brow_L_Mid_Down"), Is.EqualTo(100f));
            brow.OnPointerUp(pointer);

            FaceDragHandle lip = Handle(FaceDragHandle.FaceControl.UpperLip,
                upOverride: "Mouth_UpperLipL_Up");
            pointer.position = Vector2.zero;
            lip.OnPointerDown(pointer);
            pointer.position = Vector2.up * RangePixels;
            lip.OnDrag(pointer);
            Assert.That(face.GetWeight("Mouth_UpperLipL_Up"), Is.EqualTo(100f));
            Assert.That(face.GetWeight("Mouth_UpperLip_Up.001"), Is.Zero);
            Assert.That(brow.enabled && lip.enabled, Is.True);
        }

        [Test]
        public void Drag_LegacyCorner_DoesNotChangeHorizontalShapes()
        {
            FaceDragHandle handle = Handle(FaceDragHandle.FaceControl.MouthLeft);
            face.SetWeight("Mouth_L_Out", 25f);
            PointerEventData pointer = Pointer();
            handle.OnPointerDown(pointer);
            pointer.position = new Vector2(RangePixels, RangePixels * 0.5f);
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight("Mouth_L_Out"), Is.EqualTo(25f));
            Assert.That(face.GetWeight("Mouth_L_Up"), Is.EqualTo(50f).Within(0.001f));
        }

        [Test]
        public void Drag_WrongPointerOrReleased_DoesNotWriteWeights()
        {
            FaceDragHandle handle = Handle(FaceDragHandle.FaceControl.UpperLip);
            PointerEventData pointer = Pointer();
            handle.OnPointerDown(pointer);
            PointerEventData other = Pointer();
            other.pointerId++;
            other.position = Vector2.up * RangePixels;
            handle.OnPointerDown(other);
            handle.OnDrag(other);
            handle.OnPointerUp(other);
            Assert.That(face.GetWeight("Mouth_UpperLip_Up.001"), Is.Zero);
            handle.OnPointerUp(pointer);
            pointer.position = Vector2.up * RangePixels;
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight("Mouth_UpperLip_Up.001"), Is.Zero);
        }

        [TestCase(FaceDragHandle.FaceControl.EyeLeftGaze)]
        [TestCase(FaceDragHandle.FaceControl.EyeRightGaze)]
        public void Drag_Gaze_RotatesAroundFixedCenterAndResets(FaceDragHandle.FaceControl control)
        {
            root.transform.rotation = Quaternion.Euler(270f, 180f, 0f);
            Transform pivot = Child("Pivot").transform;
            pivot.localPosition = new Vector3(0.04f, 0.03f, 0.07f);
            pivot.localRotation = Quaternion.Euler(270f, 0f, 0f);
            pivot.localScale = Vector3.one * 70f;
            GameObject eye = Child("Eye");
            eye.transform.SetParent(pivot, false);
            Vector3 center = pivot.position;
            Quaternion neutral = pivot.rotation;
            FaceDragHandle handle = Handle(control, pivot: pivot);
            PointerEventData pointer = Pointer();
            handle.OnPointerDown(pointer);
            pointer.position = Vector2.one * RangePixels * 2f;
            handle.OnDrag(pointer);
            Quaternion expected = Quaternion.AngleAxis(-20f, raycaster.eventCamera.transform.up)
                * Quaternion.AngleAxis(15f, raycaster.eventCamera.transform.right) * neutral;
            Assert.That(Quaternion.Angle(pivot.rotation, expected), Is.LessThan(0.01f));
            Assert.That(pivot.position, Is.EqualTo(center));
            Assert.That(eye.transform.position, Is.EqualTo(center));
            Assert.That(eye.transform.localPosition, Is.EqualTo(Vector3.zero));
            handle.OnPointerUp(pointer);
            handle.OnPointerDown(pointer);
            pointer.position -= Vector2.one * RangePixels;
            handle.OnDrag(pointer);
            Assert.That(Quaternion.Angle(pivot.rotation, neutral), Is.LessThan(0.01f));
            pointer.position += Vector2.right * RangePixels;
            handle.OnDrag(pointer);
            handle.ResetGaze();
            Assert.That(Quaternion.Angle(pivot.rotation, neutral), Is.LessThan(0.01f));
        }

        [Test]
        public void CancelDrag_PreservesWeightAndRejectsStaleDragUntilNewPress()
        {
            FaceDragHandle handle = Handle(FaceDragHandle.FaceControl.UpperLip);
            PointerEventData pointer = Pointer();
            handle.OnPointerDown(pointer);
            pointer.position = Vector2.up * RangePixels * 0.5f;
            handle.OnDrag(pointer);
            handle.CancelDrag();
            Assert.That(handle.IsDragging, Is.False);
            pointer.position = Vector2.up * RangePixels;
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight("Mouth_UpperLip_Up.001"), Is.EqualTo(50f).Within(0.001f));
            handle.OnPointerDown(pointer);
            pointer.position += Vector2.up * RangePixels * 0.5f;
            handle.OnDrag(pointer);
            Assert.That(face.GetWeight("Mouth_UpperLip_Up.001"), Is.EqualTo(100f).Within(0.001f));
            handle.OnPointerUp(pointer);
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private FaceDragHandle Handle(FaceDragHandle.FaceControl control, bool horizontal = false,
            bool invertHorizontal = false, Transform pivot = null,
            string upOverride = "", string downOverride = "")
        {
            FaceDragHandle handle = Child("Handle").AddComponent<FaceDragHandle>();
            using (var serialized = new SerializedObject(handle))
            {
                serialized.FindProperty("control").enumValueIndex = (int)control;
                serialized.FindProperty("face").objectReferenceValue = face;
                serialized.FindProperty("enableHorizontalDrag").boolValue = horizontal;
                serialized.FindProperty("invertHorizontal").boolValue = invertHorizontal;
                serialized.FindProperty("eyePivot").objectReferenceValue = pivot;
                serialized.FindProperty("upShapeOverride").stringValue = upOverride;
                serialized.FindProperty("downShapeOverride").stringValue = downOverride;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            typeof(FaceDragHandle).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handle, null);
            typeof(FaceDragHandle).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(handle, null);
            return handle;
        }

        private PointerEventData Pointer()
        {
            return new PointerEventData(events)
            {
                pointerId = 1,
                position = Vector2.zero,
                pointerPressRaycast = new RaycastResult { module = raycaster }
            };
        }
    }
}
