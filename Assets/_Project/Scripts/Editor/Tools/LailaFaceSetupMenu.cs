using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Game.LailaFace;

namespace Game.Editor
{
    /// <summary>
    /// 在当前 laila 场景为 Head-topo 创建或复用 8 个 BlendShape 控制区。
    /// 仅通过菜单运行，不参与运行时构建。
    /// </summary>
    public static class LailaFaceSetupMenu
    {
        private const string MenuPath = "21Days/LailaFace/一键创建 Head-topo 控制区";
        private const string ScenePath = "Assets/_Project/Scenes/laila.unity";
        private const string HeadAssetPath = "Assets/_Project/Art/fbx/Head-topo.fbx";
        private const string InputActionsPath = "Assets/_Project/Data/Input/GameInput.inputactions";
        private const string MuralShaderPath = "Assets/_Project/Art/Shaders/MuralFace.shader";
        private const string MuralMaterialPath = "Assets/_Project/Art/Materials/Laila/M_LailaFace_Mural.mat";

        private static readonly ControlDefinition[] Definitions =
        {
            new("Control_Mouth_L", FaceDragHandle.FaceControl.MouthLeft, -0.28f, -0.16f),
            new("Control_Mouth_R", FaceDragHandle.FaceControl.MouthRight, 0.28f, -0.16f),
            new("Control_Brow_L", FaceDragHandle.FaceControl.BrowLeft, -0.24f, 0.25f),
            new("Control_Brow_R", FaceDragHandle.FaceControl.BrowRight, 0.24f, 0.25f),
            new("Control_Eye_L_Upper", FaceDragHandle.FaceControl.EyeLeftUpperLid, -0.22f, 0.08f),
            new("Control_Eye_L_Lower", FaceDragHandle.FaceControl.EyeLeftLowerLid, -0.22f, -0.02f),
            new("Control_Eye_R_Upper", FaceDragHandle.FaceControl.EyeRightUpperLid, 0.22f, 0.08f),
            new("Control_Eye_R_Lower", FaceDragHandle.FaceControl.EyeRightLowerLid, 0.22f, -0.02f)
        };

        [MenuItem(MenuPath, false, 240)]
        private static void CreateControls()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                EditorUtility.DisplayDialog(
                    "LailaFace",
                    "请先打开 Assets/_Project/Scenes/laila.unity。此工具不会修改其他场景。",
                    "确定");
                return;
            }

            GameObject head = FindOrCreateHead(scene);
            if (head == null)
            {
                EditorUtility.DisplayDialog(
                    "LailaFace",
                    $"当前场景找不到 Head-topo，且无法加载 {HeadAssetPath}。",
                    "确定");
                return;
            }

            SkinnedMeshRenderer renderer = head.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                EditorUtility.DisplayDialog("LailaFace", "Head-topo 下找不到 SkinnedMeshRenderer。", "确定");
                return;
            }

            FaceBlendShapeController controller = head.GetComponent<FaceBlendShapeController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<FaceBlendShapeController>(head);
            }

            SetSerializedReference(controller, "faceRenderer", renderer);
            controller.RebuildCache();
            ApplyMuralMaterial(renderer);
            EnsureMuralController(head, renderer);

            Camera sceneCamera = FindSceneComponent<Camera>(scene);
            if (sceneCamera == null)
            {
                EditorUtility.DisplayDialog("LailaFace", "当前场景找不到 Camera。", "确定");
                return;
            }

            EnsureUniversalRenderer(sceneCamera);
            EnsurePhysicsRaycaster(sceneCamera);
            EnsureEventSystem(scene);
            CreateOrUpdateControls(head, renderer, sceneCamera, controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = head;

            Debug.Log("LailaFace：已创建或更新 8 个 Head-topo 控制区，并保存 laila 场景。", head);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateCreateControls()
        {
            return SceneManager.GetActiveScene().path == ScenePath && !EditorApplication.isPlaying;
        }

        [MenuItem("21Days/LailaFace/仅应用聊斋材质", false, 241)]
        private static void ApplyMuralMaterialOnly()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                EditorUtility.DisplayDialog("LailaFace", "请先打开 Assets/_Project/Scenes/laila.unity。", "确定");
                return;
            }

            GameObject head = FindSceneObject(scene, "Head-topo");
            SkinnedMeshRenderer renderer = head == null
                ? null
                : head.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer == null)
            {
                EditorUtility.DisplayDialog("LailaFace", "当前场景找不到 Head-topo 的 SkinnedMeshRenderer。", "确定");
                return;
            }

            Camera sceneCamera = FindSceneComponent<Camera>(scene);
            if (sceneCamera == null)
            {
                EditorUtility.DisplayDialog("LailaFace", "当前场景找不到 Camera。", "确定");
                return;
            }

            EnsureUniversalRenderer(sceneCamera);
            ApplyMuralMaterial(renderer);
            EnsureMuralController(head, renderer);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("LailaFace：已只更新 Head-topo 的材质，未修改控制区位置。", head);
        }

        [MenuItem("21Days/LailaFace/仅应用聊斋材质", true)]
        private static bool ValidateApplyMuralMaterialOnly()
        {
            return SceneManager.GetActiveScene().path == ScenePath && !EditorApplication.isPlaying;
        }

        private static void CreateOrUpdateControls(
            GameObject head,
            SkinnedMeshRenderer renderer,
            Camera sceneCamera,
            FaceBlendShapeController controller)
        {
            Bounds bounds = renderer.bounds;
            Vector3 toCamera = sceneCamera.transform.position - bounds.center;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                toCamera = -renderer.transform.forward;
            }

            toCamera.Normalize();
            float faceScale = bounds.extents.magnitude;
            float depth = faceScale * 0.55f;
            float radius = faceScale * 0.08f;
            float parentScale = Mathf.Max( // lint-ok: 编辑器控制区尺寸换算，不参与逻辑回放
                0.001f,
                Mathf.Max( // lint-ok: 编辑器控制区尺寸换算，不参与逻辑回放
                    Mathf.Abs(head.transform.lossyScale.x),
                    Mathf.Max( // lint-ok: 编辑器控制区尺寸换算，不参与逻辑回放
                        Mathf.Abs(head.transform.lossyScale.y),
                        Mathf.Abs(head.transform.lossyScale.z))));
            Vector3 right = sceneCamera.transform.right;
            Vector3 up = sceneCamera.transform.up;

            for (int i = 0; i < Definitions.Length; i++)
            {
                ControlDefinition definition = Definitions[i];
                GameObject controlObject = FindDirectChild(head.transform, definition.Name);
                bool created = false;
                if (controlObject == null)
                {
                    controlObject = new GameObject(definition.Name);
                    Undo.RegisterCreatedObjectUndo(controlObject, "创建 LailaFace 控制区");
                    controlObject.transform.SetParent(head.transform, true);
                    created = true;
                }

                if (created)
                {
                    Undo.RecordObject(controlObject.transform, "定位 LailaFace 控制区");
                    controlObject.transform.position = bounds.center
                        + right * (faceScale * definition.Horizontal)
                        + up * (faceScale * definition.Vertical)
                        + toCamera * depth;
                }

                SphereCollider collider = controlObject.GetComponent<SphereCollider>();
                if (collider == null)
                {
                    collider = Undo.AddComponent<SphereCollider>(controlObject);
                    collider.isTrigger = false;
                    collider.radius = radius / parentScale;
                }

                FaceDragHandle handle = controlObject.GetComponent<FaceDragHandle>();
                if (handle == null)
                {
                    handle = Undo.AddComponent<FaceDragHandle>(controlObject);
                    SetSerializedFloat(handle, "fullRangeScreenFraction", 0.12f);
                    SetSerializedBool(handle, "invert", false);
                }

                SetSerializedReference(handle, "face", controller);
                SetSerializedEnum(handle, "control", (int)definition.Control);
            }
        }

        private static void EnsurePhysicsRaycaster(Camera sceneCamera)
        {
            if (sceneCamera.GetComponent<PhysicsRaycaster>() == null)
            {
                Undo.AddComponent<PhysicsRaycaster>(sceneCamera.gameObject);
            }
        }

        private static void EnsureUniversalRenderer(Camera sceneCamera)
        {
            UniversalAdditionalCameraData cameraData =
                sceneCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                return;
            }

            // laila 使用当前 UniversalRP.asset 中的 3D UniversalRenderer（索引 1）。
            SerializedObject serialized = new(cameraData);
            SerializedProperty rendererIndex = serialized.FindProperty("m_RendererIndex");
            if (rendererIndex == null || rendererIndex.intValue == 1)
            {
                return;
            }

            Undo.RecordObject(cameraData, "切换 LailaFace 3D Renderer");
            rendererIndex.intValue = 1;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(cameraData);
        }

        private static void ApplyMuralMaterial(SkinnedMeshRenderer renderer)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(MuralShaderPath);
            if (shader == null)
            {
                Debug.LogWarning($"LailaFace：找不到聊斋古画 Shader：{MuralShaderPath}", renderer);
                return;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MuralMaterialPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "M_LailaFace_Mural"
                };
                AssetDatabase.CreateAsset(material, MuralMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials.Length > 0)
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
            }
            else
            {
                renderer.sharedMaterial = material;
            }

            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureMuralController(GameObject head, SkinnedMeshRenderer renderer)
        {
            MuralFaceController controller = head.GetComponent<MuralFaceController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<MuralFaceController>(head);
            }

            SetSerializedReference(controller, "faceRenderer", renderer);
            controller.Apply();
        }

        private static void EnsureEventSystem(Scene scene)
        {
            EventSystem eventSystem = FindSceneComponent<EventSystem>(scene);
            if (eventSystem == null)
            {
                GameObject eventSystemObject = new("EventSystem");
                Undo.RegisterCreatedObjectUndo(eventSystemObject, "创建 LailaFace EventSystem");
                SceneManager.MoveGameObjectToScene(eventSystemObject, scene);
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            StandaloneInputModule legacyModule = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyModule != null)
            {
                Undo.DestroyObjectImmediate(legacyModule);
            }

            InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                inputModule = Undo.AddComponent<InputSystemUIInputModule>(eventSystem.gameObject);
            }

            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions != null)
            {
                inputModule.actionsAsset = inputActions;
            }
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < transforms.Length; j++)
                {
                    if (transforms[j].name == objectName)
                    {
                        return transforms[j].gameObject;
                    }
                }
            }

            return null;
        }

        private static GameObject FindOrCreateHead(Scene scene)
        {
            GameObject existing = FindSceneObject(scene, "Head-topo");
            if (existing != null)
            {
                return existing;
            }

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(HeadAssetPath);
            if (asset == null)
            {
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            if (instance == null)
            {
                return null;
            }

            instance.name = "Head-topo";
            Undo.RegisterCreatedObjectUndo(instance, "实例化 Head-topo");
            return instance;
        }

        private static T FindSceneComponent<T>(Scene scene) where T : Component
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static GameObject FindDirectChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            return child == null ? null : child.gameObject;
        }

        private static void SetSerializedReference(Object target, string propertyName, Object value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetSerializedEnum(Object target, string propertyName, int value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetSerializedFloat(Object target, string propertyName, float value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetSerializedBool(Object target, string propertyName, bool value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(propertyName).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private readonly struct ControlDefinition
        {
            public ControlDefinition(
                string name,
                FaceDragHandle.FaceControl control,
                float horizontal,
                float vertical)
            {
                Name = name;
                Control = control;
                Horizontal = horizontal;
                Vertical = vertical;
            }

            public string Name { get; }
            public FaceDragHandle.FaceControl Control { get; }
            public float Horizontal { get; }
            public float Vertical { get; }
        }
    }
}
