#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FPCam.EditorTools
{
    /// <summary>
    /// Builds the jitter-free first-person camera exactly like the video:
    ///   1. Cinemachine Camera                       2. empty "Head" object as Tracking Target
    ///   3. Position Control = Hard Lock To Target   4. Rotation Control = Pan Tilt
    ///   5. Input Controller, Look axis Acceleration/Deceleration Time = 0
    ///   6. (extras) Basic Multi Channel Perlin noise, Impulse Listener (6D Shake) + Impulse Source
    /// plus a physics Player (Rigidbody, Interpolate) that moves in FixedUpdate.
    ///
    /// Cinemachine components are found BY NAME (reflection) instead of compiled against, so this menu always compiles,
    /// whatever Cinemachine 3 version you have.
    /// </summary>
    public static class FirstPersonRigBuilder
    {
        const string Root = "Assets/FirstPersonCamera";
        const string PrefabPath = Root + "/Prefabs/FirstPersonRig.prefab";
        const string DemoScene = Root + "/Scenes/FirstPerson_Demo.unity";
        const string PrefKey = "FPCam_Offered2";

        // ------------------------------------------------------------ reflection / serialization helpers

        static System.Type CMType(string name)
        {
            foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type t = a.GetType("Unity.Cinemachine." + name);
                if (t != null) return t;
            }
            return null;
        }

        static System.Type RuntimeType(string name)
        {
            foreach (System.Reflection.Assembly a in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type t = a.GetType("FPCam." + name);
                if (t != null) return t;
            }
            return null;
        }

        static bool HasCinemachine3() { return CMType("CinemachineCamera") != null && CMType("CinemachineBrain") != null; }

        static Component AddCM(GameObject go, string typeName)
        {
            System.Type t = CMType(typeName);
            if (t == null)
            {
                Debug.LogWarning("First Person Camera: Cinemachine type '" + typeName + "' not found (is Cinemachine 3 installed?).");
                return null;
            }
            return go.AddComponent(t);
        }

        static bool SetFloat(Object o, string path, float v)
        {
            if (o == null) return false;
            SerializedObject so = new SerializedObject(o);
            SerializedProperty p = so.FindProperty(path);
            if (p == null || p.propertyType != SerializedPropertyType.Float) return false;
            p.floatValue = v;
            so.ApplyModifiedProperties();
            return true;
        }

        static bool SetRef(Object o, string path, Object v)
        {
            if (o == null) return false;
            SerializedObject so = new SerializedObject(o);
            SerializedProperty p = so.FindProperty(path);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) return false;
            p.objectReferenceValue = v;
            so.ApplyModifiedProperties();
            return true;
        }

        // sets EVERY float property with this name anywhere inside the component (used for the nested Look-axis driver)
        static int SetFloatsByName(Object target, string propName, float value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty it = so.GetIterator();
            int count = 0;
            bool enter = true;
            while (it.Next(enter))
            {
                enter = true;
                if (it.name == propName && it.propertyType == SerializedPropertyType.Float) { it.floatValue = value; count++; }
            }
            so.ApplyModifiedProperties();
            return count;
        }

        static bool SetRefByName(Object target, string propName, Object value)
        {
            SerializedObject so = new SerializedObject(target);
            SerializedProperty it = so.GetIterator();
            bool enter = true, done = false;
            while (it.Next(enter))
            {
                enter = true;
                if (it.name == propName && it.propertyType == SerializedPropertyType.ObjectReference) { it.objectReferenceValue = value; done = true; }
            }
            so.ApplyModifiedProperties();
            return done;
        }

        static Object FindNoise(string contains)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:NoiseSettings"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssetDatabase.LoadAssetAtPath<Object>(p);
            }
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        // ------------------------------------------------------------ the rig

        /// <summary>Builds the whole rig. withMainCamera = also create a Main Camera (with the Cinemachine Brain) inside the rig.</summary>
        static GameObject BuildRig(Vector3 position, bool withMainCamera)
        {
            GameObject root = new GameObject("FirstPersonRig");
            root.transform.position = position;

            // Main Camera + Cinemachine Brain
            if (withMainCamera)
            {
                GameObject mc = new GameObject("Main Camera");
                mc.tag = "MainCamera";
                mc.transform.SetParent(root.transform, false);
                mc.AddComponent<UnityEngine.Camera>();
                mc.AddComponent<AudioListener>();
                AddCM(mc, "CinemachineBrain");
            }

            // Player: physics body (Rigidbody, Interpolate, moves in FixedUpdate)
            GameObject player = new GameObject("Player");
            player.transform.SetParent(root.transform, false);
            CapsuleCollider cap = player.AddComponent<CapsuleCollider>();
            cap.height = 1.8f; cap.radius = 0.35f; cap.center = new Vector3(0f, 0.9f, 0f);
            Rigidbody rb = player.AddComponent<Rigidbody>();
            rb.mass = 70f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            FPCam.FirstPersonMotor motor = player.AddComponent<FPCam.FirstPersonMotor>();
            player.AddComponent<FPCam.CursorLockOnPlay>();

            System.Type prov = RuntimeType("FPInputSystemProvider");
            if (prov == null) prov = RuntimeType("FPLegacyInputProvider");
            if (prov != null) player.AddComponent(prov);
            else Debug.LogWarning("First Person Camera: no input provider available (check Project Settings > Player > Active Input Handling).");

            // step 2: empty "Head" = the camera's Tracking Target
            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.62f, 0f);

            // step 1: Cinemachine Camera (NOT a child of the player: mouse look is independent of physics)
            GameObject camGo = new GameObject("CM FirstPerson Camera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = head.transform.position;
            Component cam = AddCM(camGo, "CinemachineCamera");
            if (cam != null)
            {
                bool ok = SetRef(cam, "Target.TrackingTarget", head.transform);
                if (!ok)
                {
                    System.Reflection.PropertyInfo follow = cam.GetType().GetProperty("Follow");
                    if (follow != null && follow.CanWrite) { follow.SetValue(cam, head.transform, null); ok = true; }
                }
                if (!ok) Debug.LogWarning("First Person Camera: could not auto-assign the Tracking Target. Drag 'Head' into CM FirstPerson Camera > Tracking Target.");
                SetFloat(cam, "Lens.FieldOfView", 75f);
                SetFloat(cam, "Lens.NearClipPlane", 0.05f);
            }

            // step 3 + 4: Position Control = Hard Lock To Target, Rotation Control = Pan Tilt
            AddCM(camGo, "CinemachineHardLockToTarget");
            AddCM(camGo, "CinemachinePanTilt");

            // extras: subtle handheld noise (Amplitude Gain 0 = off)
            Object handheld = FindNoise("Handheld_normal_mild");
            if (handheld == null) handheld = FindNoise("Handheld");
            Component noise = AddCM(camGo, "CinemachineBasicMultiChannelPerlin");
            if (noise != null)
            {
                if (handheld != null) SetRef(noise, "NoiseProfile", handheld);
                else Debug.LogWarning("First Person Camera: no Cinemachine noise profile found; pick one on Basic Multi Channel Perlin.");
                SetFloat(noise, "AmplitudeGain", 0.25f);
                SetFloat(noise, "FrequencyGain", 1f);
            }

            // extras: Impulse Listener with 6D Shake secondary noise (non-repetitive shakes)
            Component listener = AddCM(camGo, "CinemachineImpulseListener");
            Object shake6d = FindNoise("6D");
            if (listener != null && shake6d != null)
            {
                bool ok = SetRefByName(listener, "SecondaryNoise", shake6d) || SetRefByName(listener, "m_SecondaryNoise", shake6d);
                if (!ok) Debug.LogWarning("First Person Camera: set the Impulse Listener's Secondary Noise to '6D Shake' by hand.");
            }

            // step 5: Input Controller, Acceleration / Deceleration Time = 0 (no smoothing = no sluggish input)
            Component input = AddCM(camGo, "CinemachineInputAxisController");
            if (input != null)
            {
                System.Reflection.MethodInfo sync = input.GetType().GetMethod("SynchronizeControllers",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (sync != null && sync.GetParameters().Length == 0) { try { sync.Invoke(input, null); } catch (System.Exception) { } }
                int a = SetFloatsByName(input, "AccelTime", 0f);
                int d = SetFloatsByName(input, "DecelTime", 0f);
                if (a == 0 || d == 0)
                    Debug.LogWarning("First Person Camera: couldn't find the Look axis Acceleration/Deceleration Time. Select CM FirstPerson Camera > Input Axis Controller and set both to 0 by hand.");
            }

            // camera shake source
            GameObject shakeGo = new GameObject("Camera Shake Source");
            shakeGo.transform.SetParent(player.transform, false);
            Component src = AddCM(shakeGo, "CinemachineImpulseSource");
            FPCam.FirstPersonShake fps = shakeGo.AddComponent<FPCam.FirstPersonShake>();
            fps.source = src;

            motor.lookReference = camGo.transform;      // forward = where the camera looks
            return root;
        }

        static bool CheckCinemachine()
        {
            if (HasCinemachine3()) return true;
            EditorUtility.DisplayDialog("First Person Camera",
                "Cinemachine 3 was not found in this project.\n\nInstall it: Window > Package Manager > Unity Registry > Cinemachine (version 3.x), then run this menu again.", "OK");
            return false;
        }

        // ------------------------------------------------------------ prefab

        /// <summary>Creates (or re-creates) the self-contained prefab: Assets/FirstPersonCamera/Prefabs/FirstPersonRig.prefab</summary>
        static GameObject CreatePrefabAsset()
        {
            EnsureFolder(Root + "/Prefabs");
            GameObject temp = BuildRig(Vector3.zero, true);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        [MenuItem("Tools/First Person Camera/Create Prefab")]
        static void MenuCreatePrefab()
        {
            if (!CheckCinemachine()) return;
            GameObject prefab = CreatePrefabAsset();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            EditorUtility.DisplayDialog("First Person Camera",
                "Prefab created:\n" + PrefabPath + "\n\nDrag it into any scene. It contains its own Main Camera + Cinemachine Brain, so delete the scene's old Main Camera.", "OK");
        }

        // ------------------------------------------------------------ add to the open scene

        [MenuItem("Tools/First Person Camera/Add Rig To Open Scene")]
        static void MenuAddToScene()
        {
            if (!CheckCinemachine()) return;
            Vector3 pos = new Vector3(0f, 0.1f, 0f);
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null) pos = new Vector3(sv.pivot.x, 0.1f, sv.pivot.z);

            UnityEngine.Camera main = UnityEngine.Camera.main;
            bool needCamera = main == null;
            GameObject rig = BuildRig(pos, needCamera);
            Undo.RegisterCreatedObjectUndo(rig, "Add First Person Rig");

            if (!needCamera)
            {
                // use the scene's existing Main Camera: it only needs a Cinemachine Brain
                System.Type brain = CMType("CinemachineBrain");
                if (brain != null && main.GetComponent(brain) == null) Undo.AddComponent(main.gameObject, brain);
            }
            Selection.activeGameObject = rig;
            EditorSceneManager.MarkSceneDirty(rig.scene);
        }

        // ------------------------------------------------------------ demo scene

        [MenuItem("Tools/First Person Camera/Build Demo Scene")]
        public static void BuildDemo()
        {
            if (!CheckCinemachine()) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root + "/Scenes");
            GameObject prefab = CreatePrefabAsset();       // the prefab is created here too

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // the prefab brings its own Main Camera, so remove the default one
            UnityEngine.Camera defaultCam = UnityEngine.Camera.main;
            if (defaultCam != null) Object.DestroyImmediate(defaultCam.gameObject);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(6f, 1f, 6f);

            for (int i = 0; i < 8; i++)
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Box " + i;
                float ang = i * Mathf.PI * 2f / 8f;
                float h = 0.5f + (i % 3) * 0.6f;
                box.transform.position = new Vector3(Mathf.Cos(ang) * 8f, h * 0.5f, Mathf.Sin(ang) * 8f);
                box.transform.localScale = new Vector3(2f, h, 2f);
            }
            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Ramp";
            ramp.transform.position = new Vector3(0f, 0.6f, 5f);
            ramp.transform.rotation = Quaternion.Euler(-15f, 0f, 0f);
            ramp.transform.localScale = new Vector3(3f, 0.3f, 5f);

            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Pillar";
            pillar.transform.position = new Vector3(0f, 2f, 12f);
            pillar.transform.localScale = new Vector3(1.5f, 2f, 1.5f);

            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.transform.position = new Vector3(0f, 0.1f, 0f);

            EditorSceneManager.SaveScene(scene, DemoScene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = rig;
            EditorGUIUtility.PingObject(prefab);

            EditorUtility.DisplayDialog("First person camera ready",
                "Demo scene: " + DemoScene + "\nPrefab:     " + PrefabPath + "\n\n" +
                "Press PLAY.\nWASD = move, Shift = sprint, Space = jump, mouse = look.\nG = test camera shake. Esc releases the mouse.\n\n" +
                "Tweak: expand FirstPersonRig > select 'CM FirstPerson Camera' (noise amplitude, look speed, field of view).", "OK");
        }

        // ------------------------------------------------------------ test shake

        [MenuItem("Tools/First Person Camera/Test Camera Shake (Play mode)")]
        static void TestShake()
        {
            if (!Application.isPlaying || FPCam.FirstPersonShake.Instance == null)
            {
                EditorUtility.DisplayDialog("First Person Camera", "Enter Play mode with the first-person rig in the scene, then use this menu (or press G).", "OK");
                return;
            }
            FPCam.FirstPersonShake.Instance.Shake(1f);
        }

        // ------------------------------------------------------------ one-time welcome

        [InitializeOnLoadMethod]
        static void Welcome()
        {
            string key = PrefKey + Application.dataPath.GetHashCode();
            if (EditorPrefs.GetBool(key, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(key, false)) return;
                EditorPrefs.SetBool(key, true);
                int c = EditorUtility.DisplayDialogComplex("First Person Camera",
                    "First-person camera package imported.\n\nWhat would you like to do?\n(Everything is also under the menu Tools > First Person Camera)",
                    "Build demo scene + prefab", "Later", "Add to open scene");
                if (c == 0) BuildDemo();
                else if (c == 2) MenuAddToScene();
            };
        }
    }
}
#endif
