#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace IG88Tools
{
    /// <summary>Import settings so the FBX and textures work without any manual tweaking.</summary>
    public class IG88AssetProcessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath != IG88SceneBuilder.FbxPath) return;
            ModelImporter mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = false;          // file is authored in metres -> no 100x / 0.01x scale surprises
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/IG88/Textures/")) return;
            TextureImporter ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 2048;
            if (n.Contains("normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (n.Contains("metal") || n.Contains("mask"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;               // data texture, must NOT be sRGB
                ti.alphaSource = TextureImporterAlphaSource.FromInput; // alpha = smoothness
                ti.alphaIsTransparency = false;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.None;
            }
        }
    }

    public static class IG88SceneBuilder
    {
        public const string Root = "Assets/IG88";
        public const string FbxPath = Root + "/Model/IG88.fbx";
        const string ScenePath = Root + "/Scenes/IG88_Demo.unity";
        const string PrefabPath = Root + "/Prefabs/IG88_Droid.prefab";
        const string PrefKey = "IG88_DemoOffered";

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

        static Texture2D Tex(string name)
        {
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + name + ".png");
            if (t == null) Debug.LogWarning("IG88: texture not found: " + name);
            return t;
        }

        static Material GetMat(string path, Shader sh)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            return m;
        }

        static void SetTex(Material m, string prop, Texture t) { if (m.HasProperty(prop)) m.SetTexture(prop, t); }
        static void SetF(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void SetC(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

        static Material MakeMetal(RP rp)
        {
            Material m = GetMat(Root + "/Materials/IG88_Metal.mat", LitShader(rp));
            Texture2D alb = Tex("IG88_Albedo"), nrm = Tex("IG88_Normal"), ms = Tex("IG88_MetalSmooth"), mask = Tex("IG88_MaskMap");
            SetC(m, "_Color", Color.white); SetC(m, "_BaseColor", Color.white);
            if (rp == RP.URP)
            {
                SetTex(m, "_BaseMap", alb); SetTex(m, "_BumpMap", nrm); SetTex(m, "_MetallicGlossMap", ms);
                m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                SetF(m, "_WorkflowMode", 1f); SetF(m, "_Metallic", 1f); SetF(m, "_Smoothness", 1f);
                SetF(m, "_SmoothnessTextureChannel", 0f); SetF(m, "_BumpScale", 1f);
            }
            else if (rp == RP.HDRP)
            {
                SetTex(m, "_BaseColorMap", alb); SetTex(m, "_NormalMap", nrm); SetTex(m, "_MaskMap", mask);
                m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_MASKMAP");
                SetF(m, "_Metallic", 1f); SetF(m, "_Smoothness", 1f);
                SetF(m, "_SmoothnessRemapMin", 0f); SetF(m, "_SmoothnessRemapMax", 1f);
            }
            else
            {
                SetTex(m, "_MainTex", alb); SetTex(m, "_BumpMap", nrm); SetTex(m, "_MetallicGlossMap", ms);
                m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_METALLICGLOSSMAP");
                SetF(m, "_Metallic", 1f); SetF(m, "_GlossMapScale", 1f); SetF(m, "_Glossiness", 1f);
                SetF(m, "_SmoothnessTextureChannel", 0f); SetF(m, "_BumpScale", 1f);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material MakeGlow(RP rp)
        {
            Material m = GetMat(Root + "/Materials/IG88_Glow.mat", LitShader(rp));
            Color baseCol = new Color(0.25f, 0f, 0f, 1f);
            Color emis = new Color(1f, 0.06f, 0.02f, 1f) * 3.5f; // HDR red glow
            SetC(m, "_Color", baseCol); SetC(m, "_BaseColor", baseCol);
            SetF(m, "_Metallic", 0f); SetF(m, "_Glossiness", 0.85f); SetF(m, "_Smoothness", 0.85f);
            m.EnableKeyword("_EMISSION");
            SetC(m, "_EmissionColor", emis);
            SetC(m, "_EmissiveColor", emis);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material MakeSimple(string path, RP rp, Color c, bool emissive)
        {
            Material m = GetMat(path, LitShader(rp));
            SetC(m, "_Color", c); SetC(m, "_BaseColor", c);
            SetF(m, "_Metallic", 0f); SetF(m, "_Glossiness", 0.2f); SetF(m, "_Smoothness", 0.2f);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                SetC(m, "_EmissionColor", c * 2.5f);
                SetC(m, "_EmissiveColor", c * 2.5f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Tools/IG88/Build Demo Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath) == null)
            {
                EditorUtility.DisplayDialog("IG88", "Could not find " + FbxPath + ".\nImport the whole IG88 package first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");

            RP rp = DetectPipeline();
            Material metal = MakeMetal(rp);
            Material glow = MakeGlow(rp);
            AssetDatabase.SaveAssets();

            // Make the FBX itself use the two materials (so even dragging the raw FBX in looks right).
            ModelImporter mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (mi != null)
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "IG88_Metal"), metal);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "IG88_Glow"), glow);
                mi.SaveAndReimport();
            }
            GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // --- droid (prefab with controller)
            GameObject droid = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            droid.name = "IG88";
            droid.transform.position = Vector3.zero;
            IG88Controller ctrl = droid.AddComponent<IG88Controller>();
            foreach (SkinnedMeshRenderer smr in droid.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.sharedMaterials = new Material[] { metal, glow };
                smr.updateWhenOffscreen = true;
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(droid, PrefabPath, InteractionMode.AutomatedAction);
            ctrl = droid.GetComponent<IG88Controller>();
            ctrl.DetectRig();
            Vector3 fwd = ctrl.RestForward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // --- ground
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            ground.GetComponent<Renderer>().sharedMaterial = MakeSimple(Root + "/Materials/IG88_Ground.mat", rp, new Color(0.30f, 0.32f, 0.36f), false);

            // --- target
            GameObject tgt = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tgt.name = "AimTarget";
            UnityEngine.Object.DestroyImmediate(tgt.GetComponent<Collider>());
            tgt.transform.localScale = Vector3.one * 0.3f;
            tgt.transform.position = fwd * 4f + right * 1.2f + Vector3.up * 1.3f;
            tgt.GetComponent<Renderer>().sharedMaterial = MakeSimple(Root + "/Materials/IG88_Target.mat", rp, new Color(1f, 0.85f, 0.1f), true);
            ctrl.target = tgt.transform;
            ctrl.aimAtTarget = true;
            ctrl.walking = false;

            // --- camera + fill light
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                cam.transform.position = fwd * 5.5f - right * 2.5f + Vector3.up * 1.8f;
                cam.transform.LookAt(new Vector3(0f, 1.1f, 0f) + fwd * 1.5f);
            }
            GameObject fill = new GameObject("Fill Light");
            UnityEngine.Light l = fill.AddComponent<UnityEngine.Light>();
            l.type = UnityEngine.LightType.Directional;
            l.intensity = 0.7f;
            l.color = new Color(0.8f, 0.85f, 1f);
            fill.transform.rotation = Quaternion.Euler(25f, -140f, 0f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = droid;
            EditorGUIUtility.PingObject(droid);

            EditorUtility.DisplayDialog("IG88 demo scene ready",
                "Render pipeline detected: " + rp + "\n\n" +
                "1. Press PLAY.\n" +
                "2. Click 'IG88' in the Hierarchy -> in the Inspector tick  Walking  and  Aim At Target.\n" +
                "3. Click 'AimTarget' in the Hierarchy and move it with the Move tool (W) in the Scene view.\n\n" +
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
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/IG88_Albedo.png") == null) return;
            EditorApplication.update -= Poll;
            if (File.Exists(ScenePath)) return;
            EditorPrefs.SetBool(PrefKey + Application.dataPath.GetHashCode(), true);
            if (EditorUtility.DisplayDialog("IG88 Droid",
                "IG88 imported.\n\nBuild the ready-to-play demo scene now?\n(You can also do it later: Tools > IG88 > Build Demo Scene)",
                "Build Demo Scene", "Not now"))
                Build();
        }
    }
}
#endif
