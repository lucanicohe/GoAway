using UnityEngine;

/// <summary>
/// Low-lying "Alien egg chamber" ground fog. Cheap: a few dozen large soft smoke sprites (one 512x512 texture,
/// one material, no render textures, no volumetrics) in two layers:
///   Ground Mist - flat sheets of smoke crawling along the floor
///   Wisps       - small camera-facing puffs that give the fog some height
/// The shader fades the smoke by height, tints it, lets nearby lights make it glow, and fades it near the camera.
/// The fog sits at this object's Y position (that is the floor). Change values in the Inspector, they apply live.
/// Works in the Built-in pipeline and URP (not HDRP - HDRP has its own volumetric fog).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Alien Fog/Low Fog")]
public class AlienLowFog : MonoBehaviour
{
    public enum FogQuality { Low, Medium, High }

    [Header("Required (set automatically by the menu item)")]
    public Material material;
    [Tooltip("The smoke sheet. If empty, a smoke texture is generated in code so you never get plain squares.")]
    public Texture2D smokeTexture;

    [Header("Area - the fog sits at this object's Y (the floor)")]
    public Vector2 size = new Vector2(24f, 24f);
    [Range(0.3f, 4f)] public float height = 1.4f;

    [Header("Look")]
    [Range(0f, 1.5f)] public float density = 0.7f;
    public Color colorLow = new Color(0.50f, 0.60f, 0.64f, 1f);
    public Color colorHigh = new Color(0.82f, 0.92f, 0.95f, 1f);
    [Range(0.5f, 2f)] public float puffSize = 1f;

    [Header("Motion")]
    [Range(0f, 1f)] public float swirl = 0.5f;
    [Range(0f, 360f)] public float windAngle = 35f;
    [Range(0f, 1f)] public float windSpeed = 0.12f;

    [Header("Light glow (fog near lights picks up their colour)")]
    public bool lightGlow = true;
    [Range(0f, 3f)] public float glowStrength = 1f;
    [Tooltip("Leave empty to automatically use the 4 closest point/spot lights.")]
    public UnityEngine.Light[] lights;

    [Header("Performance / quality")]
    public FogQuality quality = FogQuality.Medium;
    [Tooltip("Fade the smoke where it touches walls and props. Needs the camera depth texture (Tools > Alien Fog > Enable URP Depth Texture).")]
    public bool softParticles = false;
    [Range(0f, 5f)] public float nearCameraFade = 1.5f;

    ParticleSystem ground, wisps;
    MaterialPropertyBlock block;
    readonly Vector4[] lightPos = new Vector4[4];
    readonly Vector4[] lightCol = new Vector4[4];
    float nextLightScan;
    UnityEngine.Light[] autoLights = new UnityEngine.Light[0];

    static readonly int idMainTex = Shader.PropertyToID("_MainTex");
    static Texture2D proceduralSheet;
    static readonly int idColorLow = Shader.PropertyToID("_ColorLow");
    static readonly int idColorHigh = Shader.PropertyToID("_ColorHigh");
    static readonly int idOpacity = Shader.PropertyToID("_Opacity");
    static readonly int idFloor = Shader.PropertyToID("_FogFloor");
    static readonly int idTop = Shader.PropertyToID("_FogTop");
    static readonly int idFadeBottom = Shader.PropertyToID("_FadeBottom");
    static readonly int idFadeTop = Shader.PropertyToID("_FadeTop");
    static readonly int idNear = Shader.PropertyToID("_NearFade");
    static readonly int idSoft = Shader.PropertyToID("_Soft");
    static readonly int idLightPos = Shader.PropertyToID("_FogLightPos");
    static readonly int idLightCol = Shader.PropertyToID("_FogLightCol");

    void OnEnable() { Apply(); }

    void Reset()
    {
#if UNITY_EDITOR
        if (material == null)
            material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/AlienFog/Materials/AlienLowFog.mat");
        if (smokeTexture == null)
            smokeTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AlienFog/Textures/AlienFog_Smoke.png");
#endif
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        // Not allowed to create objects inside OnValidate, so do the work a moment later.
        UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Apply(); };
#endif
    }

    ParticleSystem GetOrCreate(string childName)
    {
        Transform t = transform.Find(childName);
        GameObject go;
        if (t == null)
        {
            go = new GameObject(childName);
            go.transform.SetParent(transform, false);
        }
        else go = t.gameObject;
        ParticleSystem ps = go.GetComponent<ParticleSystem>();
        if (ps == null) ps = go.AddComponent<ParticleSystem>();
        return ps;
    }

    static float Clamp(float v, float lo, float hi) { return Mathf.Max(lo, Mathf.Min(hi, v)); }

    /// <summary>(Re)builds / updates the particle layers from the Inspector values.</summary>
    public void Apply()
    {
        if (ground == null) ground = GetOrCreate("Ground Mist");
        if (wisps == null) wisps = GetOrCreate("Wisps");

        float area = Mathf.Max(1f, size.x * size.y);
        float perCount = quality == FogQuality.Low ? 40f : (quality == FogQuality.Medium ? 22f : 14f);
        int nGround = Mathf.RoundToInt(Clamp(area / perCount, 10f, 200f));
        int nWisps = Mathf.RoundToInt(Clamp(nGround * 0.6f, 6f, 130f));

        Vector2 wind = new Vector2(Mathf.Cos(windAngle * Mathf.Deg2Rad), Mathf.Sin(windAngle * Mathf.Deg2Rad)) * (windSpeed * 0.6f);

        // layer 1: flat sheets lying close to the floor
        ConfigureLayer(ground, ParticleSystemRenderMode.HorizontalBillboard, nGround,
            new Vector2(14f, 22f), new Vector2(3.5f, 6.5f) , 0.05f, Mathf.Min(0.35f, height * 0.3f),
            0.33f * density, 0.63f * density, wind, 0f, 0.04f);

        // layer 2: camera-facing puffs that add height / volume
        ConfigureLayer(wisps, ParticleSystemRenderMode.Billboard, nWisps,
            new Vector2(10f, 16f), new Vector2(2.2f, 4.2f), height * 0.12f, height * 0.7f,
            0.15f * density, 0.30f * density, wind * 1.3f, 0.025f, 0.08f);

        ground.Clear(); ground.Play();
        wisps.Clear(); wisps.Play();
        UpdateBlock();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // edit mode: pre-simulate so the fog is visible in the Scene view without pressing Play
            ground.Simulate(20f, true, true);
            wisps.Simulate(16f, true, true);
        }
#endif
    }

    // Picks the smoke texture: the assigned one, else the material's, else one generated in code.
    Texture ResolveTexture()
    {
#if UNITY_EDITOR
        if (smokeTexture == null)
            smokeTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/AlienFog/Textures/AlienFog_Smoke.png");
#endif
        if (smokeTexture != null) return smokeTexture;
        if (material != null && material.HasProperty("_MainTex"))
        {
            Texture t = material.GetTexture("_MainTex");
            if (t != null) return t;
        }
        if (proceduralSheet == null) proceduralSheet = MakeProceduralSheet();
        return proceduralSheet;
    }

    // 256x256 sheet (2x2 puffs of 128px) made from Perlin noise - a safety net so the fog is never plain squares.
    static Texture2D MakeProceduralSheet()
    {
        const int S = 256, C = 128;
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, true);
        t.name = "AlienFog_ProceduralSmoke";
        t.wrapMode = TextureWrapMode.Clamp;
        t.hideFlags = HideFlags.HideAndDontSave;
        Color32[] px = new Color32[S * S];
        for (int cy = 0; cy < 2; cy++)
        for (int cx = 0; cx < 2; cx++)
        {
            float ox = 13.7f * (cx + 1) + 31.1f * cy, oy = 7.3f * (cy + 1) + 19.9f * cx;
            for (int y = 0; y < C; y++)
            for (int x = 0; x < C; x++)
            {
                float u = (x + 0.5f) / C, v = (y + 0.5f) / C;
                float wx = Mathf.PerlinNoise(ox + u * 3f, oy + v * 3f) - 0.5f;
                float wy = Mathf.PerlinNoise(oy + u * 3f + 50f, ox + v * 3f + 50f) - 0.5f;
                float uu = u + wx * 0.35f, vv = v + wy * 0.35f;
                float n = 0.5f * Mathf.PerlinNoise(ox + uu * 3f, oy + vv * 3f)
                        + 0.3f * Mathf.PerlinNoise(ox + uu * 6f + 9f, oy + vv * 6f + 9f)
                        + 0.2f * Mathf.PerlinNoise(ox + uu * 12f + 4f, oy + vv * 12f + 4f);
                float dx = u - 0.5f, dy = v - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                float win = Mathf.Clamp01(1f - r);
                win = win * win * (3f - 2f * win);
                float a = Mathf.Clamp01((n - 0.30f) / 0.5f);
                a = Mathf.Pow(a, 1.2f) * win * 0.95f;
                byte ab = (byte)(a * 255f);
                byte cb = (byte)(255f * (1f - 0.2f * a));
                px[(cy * C + y) * S + cx * C + x] = new Color32(cb, cb, cb, ab);
            }
        }
        t.SetPixels32(px);
        t.Apply(true, false);
        return t;
    }

    void ConfigureLayer(ParticleSystem ps, ParticleSystemRenderMode mode, int count, Vector2 life, Vector2 sz,
                        float yStart, float yThickness, float alphaMin, float alphaMax, Vector2 wind, float rise, float rotSpeed)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.duration = 10f;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count + 4;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sz.x * puffSize, sz.y * puffSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, alphaMin), new Color(1f, 1f, 1f, alphaMax));

        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = count / ((life.x + life.y) * 0.5f);

        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.position = new Vector3(0f, yStart + yThickness * 0.5f, 0f);
        sh.rotation = Vector3.zero;
        sh.scale = new Vector3(size.x, Mathf.Max(0.02f, yThickness), size.y);

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(wind.x);
        vel.y = new ParticleSystem.MinMaxCurve(rise);
        vel.z = new ParticleSystem.MinMaxCurve(wind.y);

        var noise = ps.noise;
        noise.enabled = swirl > 0.001f;
        noise.strength = 0.05f + swirl * 0.35f;
        noise.frequency = 0.12f;
        noise.scrollSpeed = 0.08f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-rotSpeed, rotSpeed);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.85f, 1f, 1.25f));

        var tsa = ps.textureSheetAnimation;
        tsa.enabled = true;
        tsa.mode = ParticleSystemAnimationMode.Grid;
        tsa.numTilesX = 2;
        tsa.numTilesY = 2;
        tsa.animation = ParticleSystemAnimationType.WholeSheet;
        tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 1f);     // random puff per particle

        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        r.sharedMaterial = material;
        r.sortMode = ParticleSystemSortMode.Distance;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        r.maxParticleSize = 5f;
        r.minParticleSize = 0f;
    }

    void ScanLights()
    {
        nextLightScan = Time.realtimeSinceStartup + 0.5f;
#pragma warning disable 618
        UnityEngine.Light[] all = UnityEngine.Object.FindObjectsOfType<UnityEngine.Light>();
#pragma warning restore 618
        System.Collections.Generic.List<UnityEngine.Light> list = new System.Collections.Generic.List<UnityEngine.Light>();
        for (int i = 0; i < all.Length; i++)
        {
            UnityEngine.Light l = all[i];
            if (l != null && l.enabled && l.gameObject.activeInHierarchy && l.type != LightType.Directional) list.Add(l);
        }
        Vector3 c = transform.position;
        list.Sort((a, b) => (a.transform.position - c).sqrMagnitude.CompareTo((b.transform.position - c).sqrMagnitude));
        if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
        autoLights = list.ToArray();
    }

    void UpdateBlock()
    {
        if (ground == null || wisps == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        block.Clear();

        float floorY = transform.position.y;
        block.SetTexture(idMainTex, ResolveTexture());
        block.SetColor(idColorLow, colorLow);
        block.SetColor(idColorHigh, colorHigh);
        block.SetFloat(idOpacity, 1f);
        block.SetFloat(idFloor, floorY);
        block.SetFloat(idTop, floorY + height);
        block.SetFloat(idFadeBottom, Mathf.Min(0.12f, height * 0.2f));
        block.SetFloat(idFadeTop, height * 0.5f);
        block.SetFloat(idNear, nearCameraFade);
        block.SetFloat(idSoft, softParticles ? 1f : 0f);
        if (softParticles)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;     // Built-in pipeline: ask the camera to render depth (URP ignores this)
            if (cam != null) cam.depthTextureMode |= UnityEngine.DepthTextureMode.Depth;
        }

        for (int i = 0; i < 4; i++) { lightPos[i] = Vector4.zero; lightCol[i] = Vector4.zero; }
        if (lightGlow)
        {
            UnityEngine.Light[] src = (lights != null && lights.Length > 0) ? lights : autoLights;
            int n = 0;
            for (int i = 0; i < src.Length && n < 4; i++)
            {
                UnityEngine.Light l = src[i];
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.type == LightType.Directional) continue;
                Vector3 p = l.transform.position;
                lightPos[n] = new Vector4(p.x, p.y, p.z, Mathf.Max(0.5f, l.range));
                Color lc = l.color * (Mathf.Clamp(l.intensity, 0f, 8f) * 0.25f * glowStrength);
                lightCol[n] = new Vector4(lc.r, lc.g, lc.b, 0f);
                n++;
            }
        }
        block.SetVectorArray(idLightPos, lightPos);
        block.SetVectorArray(idLightCol, lightCol);

        ground.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block);
        wisps.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(block);
    }

    void LateUpdate()
    {
        if (lightGlow && (lights == null || lights.Length == 0) && Time.realtimeSinceStartup > nextLightScan) ScanLights();
        UpdateBlock();
    }

    // ---- quick presets (right-click the component header) ----
    [ContextMenu("Preset: Alien Egg Chamber (dense, cold, slow)")]
    void PresetAlien()
    {
        density = 0.75f; height = 1.2f; swirl = 0.45f; windSpeed = 0.08f; puffSize = 1.1f;
        colorLow = new Color(0.38f, 0.50f, 0.52f, 1f); colorHigh = new Color(0.70f, 0.86f, 0.84f, 1f);
        glowStrength = 1.3f; Apply();
    }

    [ContextMenu("Preset: Light Mist")]
    void PresetMist()
    {
        density = 0.35f; height = 0.8f; swirl = 0.3f; windSpeed = 0.15f; puffSize = 1f;
        colorLow = new Color(0.65f, 0.70f, 0.72f, 1f); colorHigh = new Color(0.9f, 0.94f, 0.96f, 1f);
        glowStrength = 0.8f; Apply();
    }

    [ContextMenu("Preset: Heavy Smoke")]
    void PresetSmoke()
    {
        density = 1f; height = 2.2f; swirl = 0.7f; windSpeed = 0.2f; puffSize = 1.3f;
        colorLow = new Color(0.30f, 0.30f, 0.30f, 1f); colorHigh = new Color(0.55f, 0.55f, 0.55f, 1f);
        glowStrength = 1f; Apply();
    }
}
