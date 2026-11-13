#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MonsterTools
{
    /// <summary>Import settings so the FBX works without any manual tweaking.</summary>
    public class MonsterAssetProcessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath != MonsterSceneBuilder.FbxPath) return;
            ModelImporter mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = false;          // file is authored in metres -> no 100x / 0.01x scale surprises
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
        }
    }

    public static class MonsterSceneBuilder
    {
        public const string Root = "Assets/Monster";
        public const string FbxPath = Root + "/Model/Monster.fbx";
        const string ScenePath = Root + "/Scenes/Monster_Demo.unity";
        const string PrefabPath = Root + "/Prefabs/Monster.prefab";
        const string PrefKey = "Monster_DemoOffered";

        enum RP { BuiltIn, URP, HDRP }

        static RP DetectPipeline()
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null) return RP.BuiltIn;
            return asset.GetType().Name.Contains("HD") ? RP.HDRP : RP.URP;
        }

        static Shader LitShader(RP rp)
        {
            Shader s = null;
            if (rp == RP.URP) s = Shader.Find("Universal Render Pipeline/Lit");
            else if (rp == RP.HDRP) s = Shader.Find("HDRP/Lit");
            if (s == null) s = Shader.Find("Standard");
            if (s == null) s = Shader.Find("Diffuse");
            return s;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Material GetMat(string path, Shader sh)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            return m;
        }

        static void SetF(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void SetC(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

        static Material MakeSimple(string path, RP rp, Color c, float metallic, float smooth)
        {
            Material m = GetMat(path, LitShader(rp));
            SetC(m, "_Color", c); SetC(m, "_BaseColor", c);
            SetF(m, "_Metallic", metallic);
            SetF(m, "_Glossiness", smooth); SetF(m, "_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Tools/Monster/Build Demo Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                EditorUtility.DisplayDialog("Monster", "Could not find " + FbxPath + ".\nImport the whole Monster package first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");

            RP rp = DetectPipeline();
            Material skin = MakeSimple(Root + "/Materials/Monster_Skin.mat", rp, new Color(0.07f, 0.08f, 0.09f, 1f), 0.25f, 0.6f);
            AssetDatabase.SaveAssets();

            // Make the FBX itself use this material (so even dragging the raw FBX in looks right).
            ModelImporter mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (mi != null)
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Monster_Skin"), skin);
                mi.SaveAndReimport();
            }
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject monster = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            monster.name = "Monster";
            monster.transform.position = Vector3.zero;
            MonsterController ctrl = monster.AddComponent<MonsterController>();
            foreach (SkinnedMeshRenderer smr in monster.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Material[] mats = new Material[Mathf.Max(1, smr.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = skin;
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(monster, PrefabPath, InteractionMode.AutomatedAction);
            ctrl = monster.GetComponent<MonsterController>();
            ctrl.DetectRig();
            Vector3 fwd = ctrl.Forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            ground.GetComponent<Renderer>().sharedMaterial = MakeSimple(Root + "/Materials/Monster_Ground.mat", rp, new Color(0.30f, 0.33f, 0.30f), 0f, 0.15f);

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                cam.transform.position = fwd * 13f - right * 7f + Vector3.up * 4f;
                cam.transform.LookAt(new Vector3(0f, 2.3f, 0f) + fwd * 0.5f);
            }
            GameObject fill = new GameObject("Fill Light");
            UnityEngine.Light l = fill.AddComponent<UnityEngine.Light>();
            l.type = UnityEngine.LightType.Directional;
            l.intensity = 0.7f;
            l.color = new Color(0.8f, 0.85f, 1f);
            fill.transform.rotation = Quaternion.Euler(25f, -140f, 0f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = monster;
            EditorGUIUtility.PingObject(monster);

            EditorUtility.DisplayDialog("Monster demo scene ready",
                "Render pipeline detected: " + rp + "\n\n" +
                "1. Press PLAY.\n" +
                "2. Click 'Monster' in the Hierarchy. In the Inspector tick:\n" +
                "     Run On All Fours   -> gallops like a bear\n" +
                "     Attack On Two Legs -> rears up and swipes with its claws\n" +
                "   Untick both -> idles on all fours.\n\n" +
                "Scene: " + ScenePath, "OK");
        }

        [InitializeOnLoadMethod]
        static void AutoOffer()
        {
            if (EditorPrefs.GetBool(PrefKey + Application.dataPath.GetHashCode(), false)) return;
            EditorApplication.update += Poll;
        }

        static int polls;
        static void Poll()
        {
            polls++;
            if (polls > 6000) { EditorApplication.update -= Poll; return; }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null) return;
            EditorApplication.update -= Poll;
            if (File.Exists(ScenePath)) return;
            EditorPrefs.SetBool(PrefKey + Application.dataPath.GetHashCode(), true);
            if (EditorUtility.DisplayDialog("Monster",
                "Monster imported.\n\nBuild the ready-to-play demo scene now?\n(You can also do it later: Tools > Monster > Build Demo Scene)",
                "Build Demo Scene", "Not now"))
                Build();
        }
    }
}
#endif
