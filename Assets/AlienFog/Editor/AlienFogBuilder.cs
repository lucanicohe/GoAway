#if UNITY_EDITOR
#pragma warning disable 618
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlienFogTools
{
    /// <summary>Texture import settings for the smoke sheet.</summary>
    public class AlienFogAssetProcessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/AlienFog/Textures/")) return;
            TextureImporter ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.maxTextureSize = 512;
            ti.textureCompression = TextureImporterCompression.Compressed;
        }
    }

    public static class AlienFogBuilder
    {
        const string Root = "Assets/AlienFog";
        const string MatPath = Root + "/Materials/AlienLowFog.mat";
        const string TexPath = Root + "/Textures/AlienFog_Smoke.png";
        const string ScenePath = Root + "/Scenes/AlienFog_Demo.unity";
        const string PrefKey = "AlienFog_Offered";

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static Material EnsureMaterial()
        {
            EnsureFolder(Root + "/Materials");
            Shader sh = Shader.Find("AlienFog/LowFog");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            if (tex == null) { AssetDatabase.ImportAsset(TexPath, ImportAssetOptions.ForceSynchronousImport); tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath); }
            Material m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, MatPath); }
            else m.shader = sh;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Vector3 SpawnPoint()
        {
            GameObject sel = Selection.activeGameObject;
            if (sel != null) return sel.transform.position;
            SceneView sv = SceneView.lastActiveSceneView;
            if (sv != null) return new Vector3(sv.pivot.x, 0f, sv.pivot.z);
            return Vector3.zero;
        }

        [MenuItem("GameObject/Effects/Alien Low Fog", false, 10)]
        static void MenuCreateFogGameObject() { CreateFog(); }

        [MenuItem("Tools/Alien Fog/Add Low Fog To Scene")]
        static void MenuCreateFogTools() { CreateFog(); }

        public static GameObject CreateFog()
        {
            Material m = EnsureMaterial();
            GameObject go = new GameObject("Alien Low Fog");
            Undo.RegisterCreatedObjectUndo(go, "Create Alien Low Fog");
            go.transform.position = SpawnPoint();
            AlienLowFog f = go.AddComponent<AlienLowFog>();
            f.material = m;
            f.smokeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            f.Apply();
            Selection.activeGameObject = go;
            return go;
        }

        [MenuItem("Tools/Alien Fog/Repair Fog In Open Scene (re-assign texture + material)")]
        static void RepairFog()
        {
            Material m = EnsureMaterial();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            AlienLowFog[] all = UnityEngine.Object.FindObjectsOfType<AlienLowFog>();
            foreach (AlienLowFog f in all)
            {
                Undo.RecordObject(f, "Repair Alien Fog");
                f.material = m;
                f.smokeTexture = tex;
                f.Apply();
                EditorUtility.SetDirty(f);
            }
            EditorUtility.DisplayDialog("Alien Fog", "Repaired " + all.Length + " fog object(s).", "OK");
        }

        [MenuItem("Tools/Alien Fog/Enable URP Depth Texture (for soft particles)")]
        static void EnableDepthTexture()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null)
            {
                EditorUtility.DisplayDialog("Alien Fog", "You are using the Built-in pipeline: nothing to enable. Just tick 'Soft Particles' on the fog.", "OK");
                return;
            }
            SerializedObject so = new SerializedObject(rp);
            SerializedProperty p = so.FindProperty("m_RequireDepthTexture");
            if (p != null)
            {
                p.boolValue = true;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(rp);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Alien Fog", "Depth Texture enabled on your URP asset. Now tick 'Soft Particles' on the fog.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Alien Fog", "Couldn't find the setting automatically.\nSelect your URP asset and tick 'Depth Texture' under General.", "OK");
            }
        }

        static bool IsURP()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            return rp != null && !rp.GetType().Name.Contains("HD");
        }

        static Material SimpleLit(string path, Color c, float metallic, float smooth)
        {
            Shader sh = IsURP() ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Standard");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else m.shader = sh;
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void AddPointLight(string name, Vector3 pos, Color col, float intensity, float range)
        {
            GameObject g = new GameObject(name);
            g.transform.position = pos;
            UnityEngine.Light l = g.AddComponent<UnityEngine.Light>();
            l.type = UnityEngine.LightType.Point;
            l.color = col; l.intensity = intensity; l.range = range;
        }

        [MenuItem("Tools/Alien Fog/Build Demo Scene")]
        public static void BuildDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Materials");
            Material fogMat = EnsureMaterial();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.02f, 0.03f, 0.035f);

            UnityEngine.Light[] existing = UnityEngine.Object.FindObjectsOfType<UnityEngine.Light>();
            foreach (UnityEngine.Light l in existing) { l.intensity = 0.15f; l.color = new Color(0.6f, 0.75f, 0.9f); }

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            floor.GetComponent<Renderer>().sharedMaterial = SimpleLit(Root + "/Materials/Demo_Floor.mat", new Color(0.06f, 0.07f, 0.075f), 0.6f, 0.45f);

            Material pillarMat = SimpleLit(Root + "/Materials/Demo_Pillar.mat", new Color(0.10f, 0.11f, 0.12f), 0.4f, 0.35f);
            for (int i = 0; i < 6; i++)
            {
                GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.name = "Pillar " + i;
                float side = (i % 2 == 0) ? -1f : 1f;
                p.transform.position = new Vector3(side * 5f, 2f, 3f + (i / 2) * 6f);
                p.transform.localScale = new Vector3(0.9f, 2f, 0.9f);
                p.GetComponent<Renderer>().sharedMaterial = pillarMat;
            }

            AddPointLight("Teal Light", new Vector3(0f, 1.4f, 9f), new Color(0.2f, 0.9f, 0.8f), 4f, 16f);
            AddPointLight("Amber Light", new Vector3(-6f, 0.9f, -2f), new Color(1f, 0.55f, 0.2f), 3f, 12f);
            AddPointLight("Green Light", new Vector3(6f, 1.1f, 6f), new Color(0.3f, 1f, 0.4f), 3f, 12f);

            GameObject fogGo = new GameObject("Alien Low Fog");
            fogGo.transform.position = Vector3.zero;
            AlienLowFog f = fogGo.AddComponent<AlienLowFog>();
            f.material = fogMat;
            f.smokeTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            f.size = new Vector2(44f, 44f);
            f.density = 0.75f; f.height = 1.2f; f.swirl = 0.45f; f.windSpeed = 0.08f; f.puffSize = 1.1f;
            f.colorLow = new Color(0.38f, 0.50f, 0.52f, 1f); f.colorHigh = new Color(0.70f, 0.86f, 0.84f, 1f);
            f.glowStrength = 1.3f;
            f.Apply();

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.01f, 0.015f, 0.02f);
                cam.transform.position = new Vector3(0f, 1.5f, -10f);
                cam.transform.LookAt(new Vector3(0f, 1.0f, 6f));
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = fogGo;
            EditorUtility.DisplayDialog("Alien Fog demo ready",
                "Press PLAY to see the fog.\n\nClick 'Alien Low Fog' in the Hierarchy to tweak it live (density, height, colour, wind, quality).\n" +
                "To add fog to YOUR scene: menu GameObject > Effects > Alien Low Fog.", "OK");
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
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath) == null) return;
            if (Shader.Find("AlienFog/LowFog") == null) return;
            EditorApplication.update -= Poll;
            if (File.Exists(ScenePath)) return;
            EditorPrefs.SetBool(PrefKey + Application.dataPath.GetHashCode(), true);
            EnsureMaterial();
            int choice = EditorUtility.DisplayDialogComplex("Alien Low Fog",
                "Alien Low Fog imported.\n\nWhat would you like to do?",
                "Add fog to current scene", "Not now", "Build demo scene");
            if (choice == 0) CreateFog();
            else if (choice == 2) BuildDemo();
        }
    }
}
#endif
