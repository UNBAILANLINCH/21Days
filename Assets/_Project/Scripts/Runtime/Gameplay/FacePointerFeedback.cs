using System.Collections.Generic;
using Game.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // 职责：laila 的真实拾取抓点、原创右手软件光标及生命周期恢复。
    // 新建原因：FaceDragHandle 只负责变形输入；反馈不应改变键映射或场景资产。
    [DisallowMultipleComponent]
    public sealed class FacePointerFeedback : MonoBehaviour
    {
        [Tooltip("仅编辑器调试显示点位名称；正式发布始终关闭。")]
        [SerializeField]
        private bool showDebugLabels;

        private readonly Dictionary<GameObject, FaceDragHandle> handlesByObject = new();
        private readonly Dictionary<FaceDragHandle, SphereCollider> colliders = new();
        private readonly List<RaycastResult> hits = new();
        private readonly List<Vector3> vertices = new();
        private FaceDragHandle[] handles;
        private Camera viewCamera;
        private EventSystem eventSystem;
        private InputSystemUIInputModule inputModule;
        private PointerEventData pointer;
        private SkinnedMeshRenderer faceRenderer;
        private Mesh bakedFace;
        private int[] triangles;
        private Canvas canvas;
        private RectTransform canvasRect;
        private Image marker;
        private Image cursorImage;
        private Text label;
        private Sprite[] cursorSprites;
        private Sprite markerSprite;
        private FaceDragHandle current;
        private bool wasCursorVisible;
        private bool ownsCursor;
        private int bakedFrame = -1;
        private string currentState = "普通";

        public string CurrentState => currentState;
        public FaceDragHandle CurrentHandle => current;
        public bool MarkerVisible => marker != null && marker.enabled;
        public bool DebugLabelsVisible
        {
            get
            {
#if UNITY_EDITOR
                return showDebugLabels;
#else
                return false;
#endif
            }
            set => showDebugLabels = value;
        }

        private void Awake()
        {
            if (gameObject.scene.path != "Assets/_Project/Scenes/LailaRecognitionPlaytest.unity")
            {
                enabled = false;
                return;
            }

            viewCamera = Camera.main;
            eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
                pointer = new PointerEventData(eventSystem);
            }

            faceRenderer = GetComponent<SkinnedMeshRenderer>();
            if (faceRenderer == null) faceRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (faceRenderer != null)
            {
                bakedFace = new Mesh { name = "Laila feedback occlusion" };
            }

            handles = GetComponentsInChildren<FaceDragHandle>();
            foreach (FaceDragHandle handle in handles)
            {
                handlesByObject[handle.gameObject] = handle;
                colliders[handle] = handle.GetComponent<SphereCollider>();
            }

            CreateOverlay();
        }

        private void Update()
        {
            EnsureInputContext();
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() != gameObject.scene)
            {
                CancelCurrent();
                ReleaseFeedback();
                return;
            }
            if (inputModule == null || inputModule.point == null || inputModule.point.action == null
                || !Application.isFocused || viewCamera == null || eventSystem == null)
            {
                CancelCurrent();
                ReleaseFeedback();
                return;
            }

            Vector2 position = inputModule.point.action.ReadValue<Vector2>();
            if (position.x < 0 || position.y < 0 || position.x >= Screen.width || position.y >= Screen.height)
            {
                CancelCurrent();
                ReleaseFeedback();
                return;
            }

            if (inputModule.cancel != null && inputModule.cancel.action != null
                && inputModule.cancel.action.WasPressedThisFrame())
            {
                CancelCurrent();
                ReleaseFeedback();
                return;
            }

            RefreshFeedback(position);
        }

        // 与实际输入共用；公开供本场景回放验证，不提供虚构命中入口。
        public void RefreshFeedback(Vector2 position)
        {
            EnsureInputContext();
            if (!isActiveAndEnabled || pointer == null || viewCamera == null) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene() != gameObject.scene)
            {
                CancelCurrent();
                ReleaseFeedback();
                return;
            }
            if (!viewCamera.pixelRect.Contains(position))
            {
                CancelCurrent();
                ReleaseFeedback();
                return;
            }
            FaceDragHandle grabbed = null;
            foreach (FaceDragHandle handle in handles)
            {
                if (handle != null && handle.isActiveAndEnabled && handle.IsDragging)
                {
                    grabbed = handle;
                    break;
                }
            }

            pointer.position = position;
            hits.Clear();
            eventSystem.RaycastAll(pointer, hits);
            FaceDragHandle hovered = null;
            if (hits.Count > 0) handlesByObject.TryGetValue(hits[0].gameObject, out hovered);
            if (hovered != null && (!hovered.isActiveAndEnabled || !PointVisible(hovered))) hovered = null;
            current = grabbed != null ? grabbed : hovered;
            int state = grabbed != null ? 2 : hovered != null ? 1 : 0;
            currentState = state == 2 ? "抓取中" : state == 1 ? "可抓取" : "普通";

            if (!ownsCursor)
            {
                wasCursorVisible = Cursor.visible;
                ownsCursor = true;
                Cursor.visible = false;
            }

            cursorImage.enabled = true;
            cursorImage.sprite = cursorSprites[state];
            Place(cursorImage.rectTransform, position);
            bool show = current != null && PointVisible(current);
            marker.enabled = show;
            label.enabled = show && DebugLabelsVisible;
            if (!label.enabled) label.text = string.Empty;
            if (show)
            {
                Vector3 screen = viewCamera.WorldToScreenPoint(ControlCenter(current));
                Place(marker.rectTransform, screen);
                Place(label.rectTransform, (Vector2)screen + new Vector2(22, 12));
                marker.color = state == 2 ? new Color(1f, 0.68f, 0.30f) : new Color(0.86f, 0.96f, 0.88f);
                if (label.enabled)
                    label.text = current.name.Replace("Control_", "") + (state == 2 ? " · HOLD" : " · GRAB");
            }
        }

        private void EnsureInputContext()
        {
            EventSystem active = EventSystem.current;
            if (active == null) return;
            if (eventSystem == active && pointer != null && inputModule != null) return;
            eventSystem = active;
            inputModule = active.GetComponent<InputSystemUIInputModule>();
            pointer = new PointerEventData(active);
        }

        private Vector3 ControlCenter(FaceDragHandle handle)
        {
            SphereCollider area = colliders[handle];
            return area != null ? area.transform.TransformPoint(area.center) : handle.transform.position;
        }

        private bool PointVisible(FaceDragHandle handle)
        {
            Vector3 center = ControlCenter(handle);
            Vector3 screen = viewCamera.WorldToScreenPoint(center);
            if (screen.z <= 0 || !viewCamera.pixelRect.Contains(screen)) return false;
            if (faceRenderer == null || !faceRenderer.enabled || bakedFace == null) return true;
            if (bakedFrame != Time.frameCount)
            {
                faceRenderer.BakeMesh(bakedFace);
                bakedFace.GetVertices(vertices);
                // 导入网格可关闭 Read/Write；BakeMesh 的 CPU 副本仍可安全取拓扑。
                if (triangles == null) triangles = bakedFace.triangles;
                bakedFrame = Time.frameCount;
            }

            Ray ray = viewCamera.ScreenPointToRay(screen);
            // 以真实可拾取的球体前表面判断，不把贴在皮肤内的中心误当成背后点。
            SphereCollider area = colliders[handle];
            if (area != null && area.Raycast(ray, out RaycastHit hit, viewCamera.farClipPlane)) center = hit.point;
            return FirstFaceHit(ray.origin, center) >= 0.9999f;
        }

        private float FirstFaceHit(Vector3 worldOrigin, Vector3 worldEnd)
        {
            Vector3 origin = faceRenderer.transform.InverseTransformPoint(worldOrigin);
            Vector3 end = faceRenderer.transform.InverseTransformPoint(worldEnd);
            Vector3 segment = end - origin;
            float first = 1;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 edge1 = vertices[triangles[i + 1]] - a;
                Vector3 edge2 = vertices[triangles[i + 2]] - a;
                Vector3 cross = Vector3.Cross(segment, edge2);
                float determinant = Vector3.Dot(edge1, cross);
                if (Mathf.Abs(determinant) < 1e-15f) continue; // lint-ok: 米制细网格射线的双精度量级容差，不是玩法数值
                Vector3 offset = origin - a;
                float u = Vector3.Dot(offset, cross) / determinant;
                if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(offset, edge1);
                float v = Vector3.Dot(segment, q) / determinant;
                if (v < 0 || u + v > 1) continue;
                float t = Vector3.Dot(edge2, q) / determinant;
                if (t > 0 && t < first) first = t;
            }

            return first;
        }

        private void Place(RectTransform target, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, viewCamera, out Vector2 local);
            target.anchoredPosition = local;
        }

        private void CreateOverlay()
        {
            GameObject root = new("Laila pointer feedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            // 不继承模型的 100 倍导入缩放；生命周期仍由本组件负责。
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = viewCamera;
            canvas.planeDistance = viewCamera.nearClipPlane + 0.01f; // lint-ok: UI 平面位于近裁面前，不涉及玩法位置
            canvas.sortingOrder = 32000;
            canvasRect = root.GetComponent<RectTransform>();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // lint-ok: 本场景 UI 参考分辨率，CanvasScaler 适配实际窗口
            scaler.matchWidthOrHeight = 0.5f;

            marker = CreateImage("Grab point", new Vector2(24, 24));
            Texture2D ring = new(32, 32, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[32 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f));
                if ((distance > 11 && distance < 14) || distance < 2) pixels[y * 32 + x] = new Color32(255, 255, 255, 255);
            }
            ring.SetPixels32(pixels); ring.Apply();
            markerSprite = Sprite.Create(ring, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
            marker.sprite = markerSprite;

            GameObject textObject = new("Control label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(root.transform, false);
            label = textObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.color = new Color(0.98f, 0.94f, 0.80f);
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(300, 36);
            label.rectTransform.pivot = new Vector2(0, 0.5f);
            cursorImage = CreateImage("Right bone hand", new Vector2(32, 40));
            cursorImage.rectTransform.pivot = new Vector2(13f / 32f, 1f - 2f / 40f);
            cursorSprites = new Sprite[3];
            for (int i = 0; i < 3; i++) cursorSprites[i] = DrawRightHand(i);
            ReleaseFeedback();
        }

        private Image CreateImage(string title, Vector2 size)
        {
            GameObject child = new(title, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(canvas.transform, false);
            Image image = child.GetComponent<Image>();
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        // 原创像素骨架：掌心朝向观察者，拇指在画面左侧，因此是右手。
        private Sprite DrawRightHand(int state)
        {
            Color32[] pixels = new Color32[32 * 40];
            Color32 bone = state == 2 ? new Color32(235, 191, 122, 255) : new Color32(226, 235, 207, 255);
            System.Action<int, int, int, int> boneRect = (x, y, width, height) =>
            {
                for (int row = y; row < y + height; row++) for (int col = x; col < x + width; col++)
                    if (col >= 0 && col < 32 && row >= 0 && row < 40) pixels[(39 - row) * 32 + col] = bone;
            };
            // 手掌朝向观察者：拇指在左侧，为右手；握持态收拢手指。
            boneRect(10, 18, 16, 14);
            boneRect(12, 31, 12, 4);
            boneRect(15, 35, 7, 4);
            if (state == 2)
            {
                boneRect(10, 13, 4, 7); boneRect(15, 12, 4, 8);
                boneRect(20, 14, 4, 6); boneRect(25, 17, 3, 9);
                boneRect(5, 22, 8, 5); boneRect(8, 18, 5, 6);
            }
            else
            {
                boneRect(11, 2, 4, 19);
                boneRect(16, state == 1 ? 6 : 13, 4, state == 1 ? 16 : 9);
                boneRect(21, state == 1 ? 10 : 15, 4, state == 1 ? 13 : 8);
                boneRect(26, state == 1 ? 15 : 18, 3, 11);
                boneRect(3, 21, 4, 7); boneRect(6, 25, 6, 6);
            }
            Color32[] outlined = (Color32[])pixels.Clone();
            for (int y = 1; y < 39; y++) for (int x = 1; x < 31; x++)
            {
                int index = y * 32 + x;
                if (pixels[index].a != 0) continue;
                if (pixels[index - 1].a != 0 || pixels[index + 1].a != 0 || pixels[index - 32].a != 0 || pixels[index + 32].a != 0)
                    outlined[index] = new Color32(60, 36, 30, 255);
            }
            Texture2D texture = new(32, 40, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Original right bone hand " + state };
            texture.SetPixels32(outlined); texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 32, 40), new Vector2(0.5f, 0.5f));
        }

        public void CancelCurrent()
        {
            if (handles == null) return;
            foreach (FaceDragHandle handle in handles)
                if (handle != null && handle.IsDragging) handle.CancelDrag();
        }

        public void ReleaseFeedback()
        {
            current = null;
            currentState = "普通";
            if (marker != null) marker.enabled = false;
            if (label != null) label.enabled = false;
            if (cursorImage != null) cursorImage.enabled = false;
            if (ownsCursor)
            {
                Cursor.visible = wasCursorVisible;
                ownsCursor = false;
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { CancelCurrent(); ReleaseFeedback(); }
        }

        private void OnDisable()
        {
            CancelCurrent();
            ReleaseFeedback();
        }

        private void OnDestroy()
        {
            if (bakedFace != null) Destroy(bakedFace);
            if (markerSprite != null) { Destroy(markerSprite.texture); Destroy(markerSprite); }
            if (cursorSprites != null) foreach (Sprite sprite in cursorSprites)
                if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
            if (canvas != null) Destroy(canvas.gameObject);
        }
    }
}
