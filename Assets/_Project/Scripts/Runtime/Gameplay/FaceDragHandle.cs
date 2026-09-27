using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.LailaFace
{
    // 职责：把一个 3D 控制区的垂直拖拽转换为一个 BlendShape Up/Down 成对值。
    // 新建原因：FaceBlendShapeController 只负责 Renderer 写入，不能承担指针事件与控制区映射。
    public sealed class FaceDragHandle : MonoBehaviour,
        IInitializePotentialDragHandler,
        IPointerDownHandler,
        IDragHandler,
        IPointerUpHandler
    {
        public enum FaceControl
        {
            MouthLeft,
            MouthRight,
            BrowLeft,
            BrowRight,
            EyeLeftUpperLid,
            EyeLeftLowerLid,
            EyeRightUpperLid,
            EyeRightLowerLid
        }

        [SerializeField]
        private FaceBlendShapeController face;

        [SerializeField]
        private FaceControl control;

        [Tooltip("拖动屏幕短边的多少比例达到 100%。")]
        [Range(0.03f, 0.5f)]
        [SerializeField]
        private float fullRangeScreenFraction = 0.12f;

        [SerializeField]
        private bool invert;

        private string upShape;
        private string downShape;
        private Vector2 startPointerPosition;
        private float startValue;
        private int activePointerId = int.MinValue;

        public FaceControl Control => control;

        private void Awake()
        {
            if (face == null)
            {
                face = GetComponentInParent<FaceBlendShapeController>();
            }

            ResolveShapeNames();
        }

        private void Start()
        {
            if (face == null || !face.HasShape(upShape) || !face.HasShape(downShape))
            {
                Debug.LogError($"FaceDragHandle「{control}」没有找到对应的 BlendShape。", this);
                enabled = false;
            }
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!enabled || face == null || activePointerId != int.MinValue)
            {
                return;
            }

            activePointerId = eventData.pointerId;
            startPointerPosition = eventData.position;
            startValue = face.GetSignedPair(upShape, downShape);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId != activePointerId || face == null)
            {
                return;
            }

            float fullRangePixels = Mathf.Max( // lint-ok: 屏幕指针到表现值的换算，不参与逻辑回放
                40f,
                Mathf.Min(Screen.width, Screen.height) * fullRangeScreenFraction); // lint-ok: 屏幕指针到表现值的换算，不参与逻辑回放
            float deltaValue = (eventData.position.y - startPointerPosition.y) / fullRangePixels;

            if (invert)
            {
                deltaValue = -deltaValue;
            }

            face.TrySetSignedPair(upShape, downShape, startValue + deltaValue);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId == activePointerId)
            {
                activePointerId = int.MinValue;
            }
        }

        private void OnDisable()
        {
            activePointerId = int.MinValue;
        }

        private void ResolveShapeNames()
        {
            switch (control)
            {
                case FaceControl.MouthLeft:
                    upShape = "Mouth_L_Up";
                    downShape = "Mouth_L_Down";
                    break;
                case FaceControl.MouthRight:
                    upShape = "Mouth_R_Up";
                    downShape = "Mouth_R_Down";
                    break;
                case FaceControl.BrowLeft:
                    upShape = "Brow_L_Up";
                    downShape = "Brow_L_Down";
                    break;
                case FaceControl.BrowRight:
                    upShape = "Brow_R_Up";
                    downShape = "Brow_R_Down";
                    break;
                case FaceControl.EyeLeftUpperLid:
                    upShape = "Eye_L_UpperLid_Up";
                    downShape = "Eye_L_UpperLid_Down";
                    break;
                case FaceControl.EyeLeftLowerLid:
                    upShape = "Eye_L_LowerLid_Up";
                    downShape = "Eye_L_LowerLid_Down";
                    break;
                case FaceControl.EyeRightUpperLid:
                    upShape = "Eye_R_UpperLid_Up";
                    downShape = "Eye_R_UpperLid_Down";
                    break;
                case FaceControl.EyeRightLowerLid:
                    upShape = "Eye_R_LowerLid_Up";
                    downShape = "Eye_R_LowerLid_Down";
                    break;
            }
        }
    }
}
