#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlatheadRobotKit.EditorTools
{
    public class FlatheadAssetProcessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath != FlatheadBuilder.FbxPath) return;
            ModelImporter mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = false;           // file is in metres
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
        }
    }

    /// <summary>Tools > Flathead Robot : Build Prefab / Build Test Scene.</summary>
    public static class FlatheadBuilder
    {
        public const string Root = "Assets/FlatheadRobot";
        public const string FbxPath = Root + "/Model/Flathead.fbx";
        const string PrefabPath = Root + "/Prefabs/FlatheadRobot.prefab";
        const string ScenePath = Root + "/Scenes/Flathead_Test.unity";
        const string PrefKey = "Flathead_Offered";

        static readonly string[] MatNames = { "Flat_Dark", "Flat_Eye_Red", "Flat_Metal", "Flat_Mid", "Flat_Red" };
        static readonly Color[] MatColors =
        {
            new Color(0.05f, 0.055f, 0.065f), new Color(1f, 0.05f, 0.03f), new Color(0.55f, 0.57f, 0.60f), new Color(0.16f, 0.17f, 0.19f), new Color(0.75f, 0.04f, 0.03f)
        };
        static readonly float[] MatMetal = { 0.3f, 0f, 0.8f, 0.4f, 0.2f };
        static readonly float[] MatSmooth = { 0.4f, 0.5f, 0.55f, 0.4f, 0.4f };

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static Shader LitShader()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            Shader s = null;
            if (rp != null && !rp.GetType().Name.Contains("HD")) s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        static Material MakeMat(string name, Color c, float metal, float smooth, bool emissive)
        {
            string path = Root + "/Materials/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = LitShader();
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 2.5f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject BuildPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                EditorUtility.DisplayDialog("Flathead Robot", "Could not find " + FbxPath + ". Import the whole package first.", "OK");
                return null;
            }
            EnsureFolder(Root + "/Materials"); EnsureFolder(Root + "/Prefabs"); EnsureFolder(Root + "/Scenes");

            Material[] mats = new Material[MatNames.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = MakeMat(MatNames[i], MatColors[i], MatMetal[i], MatSmooth[i], i == 1);
            AssetDatabase.SaveAssets();

            ModelImporter mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (mi != null)
            {
                for (int i = 0; i < mats.Length; i++)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), MatNames[i]), mats[i]);
                mi.SaveAndReimport();
            }
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            inst.name = "FlatheadRobot";
            inst.transform.position = Vector3.zero;
            inst.AddComponent<FlatheadController>();
            foreach (SkinnedMeshRenderer smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Material[] cur = smr.sharedMaterials;
                Material[] set = new Material[cur.Length];
                for (int i = 0; i < set.Length; i++) set[i] = mats[Mathf.Min(i, mats.Length - 1)];
                smr.sharedMaterials = set;
                smr.updateWhenOffscreen = true;
            }
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(inst, PrefabPath);
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        [MenuItem("Tools/Flathead Robot/Build Prefab")]
        static void MenuPrefab()
        {
            GameObject p = BuildPrefab();
            if (p == null) return;
            Selection.activeObject = p; EditorGUIUtility.PingObject(p);
            EditorUtility.DisplayDialog("Flathead Robot", "Prefab created:\n" + PrefabPath + "\n\nDrag it into any scene. Tick Walking / Aim At Target / Jump on its Flathead Controller.", "OK");
        }

        [MenuItem("Tools/Flathead Robot/Build Test Scene")]
        public static void BuildTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            GameObject prefab = BuildPrefab();
            if (prefab == null) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(4f, 1f, 4f);
            Material fm = MakeMat("Test_Floor", new Color(0.32f, 0.34f, 0.36f), 0f, 0.15f, false);
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            for (int i = 0; i < 6; i++)
            {
                GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Crate " + i;
                float a = i * Mathf.PI * 2f / 6f;
                c.transform.position = new Vector3(Mathf.Cos(a) * 7f, 0.5f, Mathf.Sin(a) * 7f);
                c.GetComponent<Renderer>().sharedMaterial = fm;
            }

            GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            robot.transform.position = Vector3.zero;
            FlatheadController ctrl = robot.GetComponent<FlatheadController>();
            ctrl.DetectRig();
            Vector3 fwd = ctrl.Forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            GameObject tgt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tgt.name = "AimTarget";
            Object.DestroyImmediate(tgt.GetComponent<Collider>());
            tgt.transform.localScale = Vector3.one * 0.3f;
            tgt.transform.position = fwd * 3f + right * 1.5f + Vector3.up * 1.3f;
            tgt.GetComponent<Renderer>().sharedMaterial = MakeMat("Test_Target", new Color(1f, 0.85f, 0.1f), 0f, 0.3f, true);
            ctrl.target = tgt.transform;
            ctrl.aimAtTarget = true;

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                cam.transform.position = fwd * 5.5f - right * 3.2f + Vector3.up * 2.4f;
                cam.transform.LookAt(new Vector3(0f, 0.7f, 0f) + fwd * 0.6f);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = robot;
            EditorUtility.DisplayDialog("Flathead Robot test scene ready",
                "Press PLAY, click 'FlatheadRobot' in the Hierarchy and tick in the Inspector:\n" +
                "  Walking      (set Walk Direction 0-360, any direction)\n  Aim At Target (move 'AimTarget' in the Scene view, or tick Orbit Target)\n  Jump\n\n" +
                "Scene: " + ScenePath + "\nPrefab: " + PrefabPath, "OK");
        }

        [InitializeOnLoadMethod]
        static void Welcome()
        {
            string key = PrefKey + Application.dataPath.GetHashCode();
            if (EditorPrefs.GetBool(key, false)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(key, false)) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null) return;
                EditorPrefs.SetBool(key, true);
                int c = EditorUtility.DisplayDialogComplex("Flathead Robot", "Flathead robot imported.\n\nWhat would you like to do?\n(Also under Tools > Flathead Robot)", "Build test scene + prefab", "Later", "Only the prefab");
                if (c == 0) BuildTestScene(); else if (c == 2) MenuPrefab();
            };
        }
    }
}
#endif
