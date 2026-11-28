#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WebGLBakeHelper
{
    private static readonly string[] DynamicKeywords =
    {
        "player", "camera", "rig", "blaster", "weapon", "muzzle", "bolt",
        "enemy", "flathead", "ig88", "droideka", "door", "ammo", "upload",
        "terminal", "extraction", "visor", "fog", "spaceship", "planet"
    };

    [MenuItem("Tools/WebGL/1-Click Bake Lighting & Occlusion")]
    public static void PrepareAndBake()
    {
        if (!EditorUtility.DisplayDialog(
                "WebGL 1-Click Bake",
                "This will:\n" +
                "1. Set Player spotlights to Important (ForcePixel)\n" +
                "2. Mark static map meshes for Lighting & Occlusion Culling\n" +
                "3. Set non-flickering room lights to Baked\n" +
                "4. Bake Occlusion Culling & Lighting\n\n" +
                "Continue?",
                "Yes, Bake Now",
                "Cancel"))
        {
            return;
        }

        int staticMeshCount = 0;
        int bakedLightCount = 0;
        int importantLightCount = 0;

        // 1. Mark static map geometry for GI + Occlusion Culling
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        foreach (MeshRenderer mr in renderers)
        {
            if (mr == null || IsDynamicObject(mr.gameObject))
                continue;

            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
            flags |= StaticEditorFlags.ContributeGI
                   | StaticEditorFlags.OccluderStatic
                   | StaticEditorFlags.OccludeeStatic
                   | StaticEditorFlags.BatchingStatic;
            GameObjectUtility.SetStaticEditorFlags(mr.gameObject, flags);
            EditorUtility.SetDirty(mr.gameObject);
            staticMeshCount++;
        }

        // 2. Configure Lights: Player/Flicker/Enemy stay Realtime; static room lights -> Baked
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (Light l in lights)
        {
            if (l == null)
                continue;

            if (IsPlayerObject(l.gameObject))
            {
                l.lightmapBakeType = LightmapBakeType.Realtime;
                l.renderMode = LightRenderMode.ForcePixel;
                EditorUtility.SetDirty(l);
                importantLightCount++;
                continue;
            }

            if (l.GetComponent<IntensityFlicker>() != null || IsDynamicObject(l.gameObject))
            {
                l.lightmapBakeType = LightmapBakeType.Realtime;
                EditorUtility.SetDirty(l);
                continue;
            }

            l.lightmapBakeType = LightmapBakeType.Baked;
            EditorUtility.SetDirty(l);
            bakedLightCount++;
        }

        EditorSceneManager.MarkAllScenesDirty();
        EditorSceneManager.SaveOpenScenes();

        // 3. Bake Occlusion Culling (fast, synchronous)
        StaticOcclusionCulling.Compute();

        // 4. Start Lightmap Bake (async with Unity progress bar)
        Lightmapping.BakeAsync();

        Debug.Log($"[WebGLBakeHelper] Marked {staticMeshCount} static meshes, set {bakedLightCount} lights to Baked, " +
                  $"set {importantLightCount} player lights to Important. Occlusion baked & Lightmap bake started!");
    }

    private static bool IsPlayerObject(GameObject go)
    {
        Transform t = go.transform;
        while (t != null)
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("player") || n.Contains("camera") || n.Contains("rig") ||
                n.Contains("blaster") || n.Contains("flashlight") || n.Contains("spotlight"))
            {
                return true;
            }
            t = t.parent;
        }
        return false;
    }

    private static bool IsDynamicObject(GameObject go)
    {
        if (go.GetComponentInParent<Rigidbody>() != null ||
            go.GetComponentInParent<Animator>() != null ||
            go.GetComponentInParent<CharacterController>() != null ||
            go.GetComponent<ParticleSystem>() != null)
        {
            return true;
        }

        Transform t = go.transform;
        while (t != null)
        {
            string n = t.name.ToLowerInvariant();
            for (int i = 0; i < DynamicKeywords.Length; i++)
            {
                if (n.Contains(DynamicKeywords[i]))
                    return true;
            }
            t = t.parent;
        }
        return false;
    }
}
#endif
