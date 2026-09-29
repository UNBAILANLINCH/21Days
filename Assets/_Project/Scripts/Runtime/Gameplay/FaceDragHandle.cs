using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.LailaFace
{
    // 职责：把 3D 控制区拖拽映射为面部形态权重或眼球父节点旋转。
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
            EyeRightLowerLid,
            UpperLip,
            LowerLip,
            EyeLeftGaze,
            EyeRightGaze
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

        [Tooltip("仅嘴角：水平拖拽控制 Out / In；旧模型保持关闭。")]
        [SerializeField]
        private bool enableHorizontalDrag;

        [SerializeField]
        private bool invertHorizontal;

        [Tooltip("仅眼球：指向眼球与角膜共同的父节点，不移动其位置。")]
        [SerializeField]
        private Transform eyePivot;

        [Tooltip("眼球水平 / 垂直最大旋转角度。")]
        [SerializeField]
        private Vector2 maxGazeAngles = new(20f, 15f);

        private string upShape;
        private string downShape;
        private string outShape;
        private string inShape;
        private Vector2 startPointerPosition;
        private float startValue;
        private float startHorizontalValue;
        private Vector2 gazeValue;
        private Vector2 startGazeValue;
        private Quaternion neutralEyeRotation;
        private bool hasNeutralEyeRotation;
        private Vector3 gazeUpAxis;
        private Vector3 gazeRightAxis;
        private int activePointerId = int.MinValue;

        public FaceControl Control => control;
        private bool IsLip => control == FaceControl.UpperLip || control == FaceControl.LowerLip;
        private bool IsGaze => control == FaceControl.EyeLeftGaze || control == FaceControl.EyeRightGaze;

        private void Awake()
        {
            if (face == null)
            {
                face = GetComponentInParent<FaceBlendShapeController>();
            }

            ResolveShapeNames();
            if (eyePivot != null)
            {
                neutralEyeRotation = eyePivot.localRotation;
                hasNeutralEyeRotation = true;
            }
        }

        private void Start()
        {
            if (IsGaze)
            {
                if (eyePivot == null || maxGazeAngles.x <= 0f || maxGazeAngles.y <= 0f)
                {
                    Debug.LogError($"FaceDragHandle「{control}」需要眼球父节点与正数角度范围。", this);
                    enabled = false;
                }

                return;
            }

            if (face == null || !face.HasShape(upShape)
                || (!IsLip && !face.HasShape(downShape))
                || (enableHorizontalDrag && (!face.HasShape(outShape) || !face.HasShape(inShape))))
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
            if (!enabled || activePointerId != int.MinValue)
            {
                return;
            }

            if (IsGaze)
            {
                Camera eventCamera = eventData.pressEventCamera;
                if (eyePivot == null || eventCamera == null)
                {
                    return;
                }

                Quaternion parentRotation = eyePivot.parent == null ? Quaternion.identity : eyePivot.parent.rotation;
                gazeUpAxis = Quaternion.Inverse(parentRotation) * eventCamera.transform.up;
                gazeRightAxis = Quaternion.Inverse(parentRotation) * eventCamera.transform.right;
                startGazeValue = gazeValue;
            }
            else
            {
                if (face == null)
                {
                    return;
                }

                startValue = IsLip ? face.GetWeight(upShape) / 100f : face.GetSignedPair(upShape, downShape);
                if (enableHorizontalDrag)
                {
                    startHorizontalValue = face.GetSignedPair(outShape, inShape);
                }
            }

            activePointerId = eventData.pointerId;
            startPointerPosition = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!enabled || activePointerId == int.MinValue || eventData.pointerId != activePointerId)
            {
                return;
            }

            float fullRangePixels = Mathf.Max( // lint-ok: 屏幕指针到表现值的换算，不参与逻辑回放
                40f,
                Mathf.Min(Screen.width, Screen.height) * fullRangeScreenFraction); // lint-ok: 屏幕指针到表现值的换算，不参与逻辑回放
            Vector2 delta = (eventData.position - startPointerPosition) / fullRangePixels;

            if (IsGaze)
            {
                if (eyePivot == null)
                {
                    return;
                }

                gazeValue = startGazeValue + delta;
                gazeValue.x = Mathf.Clamp(gazeValue.x, -1f, 1f); // lint-ok: 眼球表现旋转范围
                gazeValue.y = Mathf.Clamp(gazeValue.y, -1f, 1f); // lint-ok: 眼球表现旋转范围
                eyePivot.localRotation = Quaternion.AngleAxis(-gazeValue.x * maxGazeAngles.x, gazeUpAxis)
                    * Quaternion.AngleAxis(gazeValue.y * maxGazeAngles.y, gazeRightAxis)
                    * neutralEyeRotation;
                return;
            }

            if (face == null)
            {
                return;
            }

            float deltaValue = delta.y;

            if (invert)
            {
                deltaValue = -deltaValue;
            }

            if (IsLip)
            {
                // 下唇向下张开；反向拖拽只回到 Basis，不要求不存在的反向 Key。
                float direction = control == FaceControl.LowerLip ? -1f : 1f;
                face.TrySetWeight(upShape, (startValue + deltaValue * direction) * 100f);
            }
            else
            {
                face.TrySetSignedPair(upShape, downShape, startValue + deltaValue);
                if (enableHorizontalDrag)
                {
                    float horizontalDelta = invertHorizontal ? -delta.x : delta.x;
                    face.TrySetSignedPair(outShape, inShape, startHorizontalValue + horizontalDelta);
                }
            }
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

        [ContextMenu("Reset Gaze")]
        public void ResetGaze()
        {
            if (!IsGaze || eyePivot == null || !hasNeutralEyeRotation)
            {
                return;
            }

            eyePivot.localRotation = neutralEyeRotation;
            gazeValue = Vector2.zero;
            activePointerId = int.MinValue;
        }

        private void ResolveShapeNames()
        {
            switch (control)
            {
                case FaceControl.MouthLeft:
                    upShape = "Mouth_L_Up";
                    downShape = "Mouth_L_Down";
                    outShape = "Mouth_L_Out";
                    inShape = "Mouth_L_In";
                    break;
                case FaceControl.MouthRight:
                    upShape = "Mouth_R_Up";
                    downShape = "Mouth_R_Down";
                    outShape = "Mouth_R_Out";
                    inShape = "Mouth_R_In";
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
                case FaceControl.UpperLip:
                    upShape = "Mouth_UpperLip_Up.001";
                    break;
                case FaceControl.LowerLip:
                    upShape = "Mouth_LowerLip_Down.001";
                    break;
            }
        }
    }
}
