using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ship goes wrong the moment the virus upload is complete:
///  - every LIGHT you drag in starts to flicker, like a power outage or a short circuit,
///  - every emission MATERIAL you drag in flickers its emission intensity the same way (drag the Material assets, not renderers),
///  - every ALARM Audio Source you drag in starts playing (looping by default).
/// Put this on any object in the scene (for example the one that has the MissionManager). Nothing else has to be set up:
/// it watches the MissionManager by itself. Lists have no size limit, drag in as many as you like.
///
/// Notes:
///  - Lights must be Realtime or Mixed, a Baked light can't change while playing.
///  - A material asset is changed while the flicker runs and put back to its normal emission when the flicker ends,
///    when this script is switched off, or when you stop Play mode. (If Unity crashes in the middle of it, re-check the emission colour of those materials.)
///  - Every renderer in the scene that uses a dragged-in material flickers with it.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Mission/Upload Alarm Effects")]
public class UploadAlarmEffects : MonoBehaviour
{
    [Header("Lights (drag in as many as you like)")]
    [Tooltip("Every light here flickers after the upload. Their normal intensity is remembered and restored at the end.")]
    public Light[] lights;

    [Header("Emission materials (drag in MATERIAL assets from the Project window, not renderers)")]
    [Tooltip("Every material here flickers its emission intensity after the upload. The material needs an emission colour that is not black (and Emission ticked on URP / Standard materials).")]
    public Material[] emissionMaterials;

    [Header("Alarm sounds (drag in as many Audio Sources as you like)")]
    [Tooltip("All of these start playing the moment the upload is complete. They stay silent before that.")]
    public AudioSource[] alarms;
    [Tooltip("On = the alarms repeat until the mission is complete. Off = each alarm plays its clip once.")]
    public bool loopAlarms = true;

    [Header("Flicker")]
    [Tooltip("Off = every light and material flickers on its own (faulty wiring). On = they all flicker at the same moment (a power outage).")]
    public bool syncAll = false;

    [Tooltip("0 = subtle / slow  →  1 = aggressive / fast + intense")]
    [Range(0f, 1f)] public float flickerAmount = 0.55f;

    [Header("End")]
    [Tooltip("On = when the mission is complete (the player reached the exit) the alarms stop and the lights and materials go back to normal. Off = it all keeps going.")]
    public bool endWhenMissionCompletes = true;

    // ------------------------------------------------------------------ internals

    // one flicker pattern: switches between lit and dark at random times
    class Flicker
    {
        public bool lit = true;
        public float timer;
        public float level = 1f;
    }

    class LightEntry
    {
        public Light light;
        public float baseIntensity;
        public float applied = -1f;      // last factor written (the value is only written when it changes)
        public Flicker flicker = new Flicker();
    }

    class MaterialEntry
    {
        public Material material;
        public int colorId;
        public Color baseColor;
        public float applied = -1f;
        public Flicker flicker = new Flicker();
    }

    static readonly string[] EmissionColorNames = { "_EmissionColor", "_EmissiveColor", "_Emission", "_EmissionTint" };

    readonly List<LightEntry> lightEntries = new List<LightEntry>();
    readonly List<MaterialEntry> materialEntries = new List<MaterialEntry>();
    readonly Flicker shared = new Flicker();   // used when Sync All is on

    bool active;    // the outage is running
    bool ended;     // the mission is complete: the outage is over for good
    bool touched;   // lights / materials have been changed (and need to be restored)

    // derived from the single slider
    float onTimeMin, onTimeMax, offTimeMin, offTimeMax, litBrightnessMin, darkLevel;

    void OnEnable()
    {
        Capture();
    }

    void OnDisable()
    {
        Restore();
    }

    void Start()
    {
        // the alarms stay silent until the upload is complete
        if (alarms == null) return;
        for (int i = 0; i < alarms.Length; i++)
        {
            AudioSource a = alarms[i];
            if (a == null) continue;
            a.playOnAwake = false;
            a.Stop();
        }
    }

    void Update()
    {
        MissionManager mission = MissionManager.Instance;
        if (mission == null || ended) return;

        if (!active)
        {
            if (!mission.UploadCompleted) return;
            Begin();
        }

        if (endWhenMissionCompletes && mission.State == MissionManager.MissionState.Completed)
        {
            End();
            return;
        }

        // keep the derived values fresh in case you tweak the slider at runtime
        DeriveFlickerParams();

        float dt = Time.deltaTime;
        float sharedLevel = syncAll ? Step(shared, dt) : 1f;

        for (int i = 0; i < lightEntries.Count; i++)
        {
            LightEntry e = lightEntries[i];
            if (e.light == null) continue;
            float f = syncAll ? sharedLevel : Step(e.flicker, dt);
            if (Mathf.Approximately(f, e.applied)) continue;
            e.applied = f;
            e.light.intensity = e.baseIntensity * f;
        }

        for (int i = 0; i < materialEntries.Count; i++)
        {
            MaterialEntry e = materialEntries[i];
            if (e.material == null) continue;
            float f = syncAll ? sharedLevel : Step(e.flicker, dt);
            if (Mathf.Approximately(f, e.applied)) continue;
            e.applied = f;
            Color b = e.baseColor;
            e.material.SetColor(e.colorId, new Color(b.r * f, b.g * f, b.b * f, b.a));   // HDR strength is kept, only scaled
        }
    }

    // ------------------------------------------------------------------ start / end

    void Begin()
    {
        active = true;
        touched = true;

        DeriveFlickerParams();

        // every light / material starts at another point of its pattern, so they don't all blink in step
        float spread = Mathf.Max(onTimeMin, onTimeMax);
        for (int i = 0; i < lightEntries.Count; i++) lightEntries[i].flicker.timer = Random.Range(0f, spread);
        for (int i = 0; i < materialEntries.Count; i++) materialEntries[i].flicker.timer = Random.Range(0f, spread);
        shared.timer = Random.Range(0f, spread);

        if (alarms != null)
        {
            for (int i = 0; i < alarms.Length; i++)
            {
                AudioSource a = alarms[i];
                if (a == null) continue;
                a.loop = loopAlarms;
                a.Play();
            }
        }

        Debug.Log("[Alarm] Upload complete: flickering " + lightEntries.Count + " light(s) and " + materialEntries.Count +
                  " material(s), playing " + (alarms != null ? alarms.Length : 0) + " alarm(s).", this);
    }

    void End()
    {
        ended = true;
        active = false;

        if (alarms != null)
        {
            for (int i = 0; i < alarms.Length; i++)
            {
                if (alarms[i] != null) alarms[i].Stop();
            }
        }

        Restore();
    }

    // ------------------------------------------------------------------ single-slider mapping

    void DeriveFlickerParams()
    {
        // 0 = calm / slow   →   1 = chaotic / fast + deep
        float t = Mathf.Clamp01(flickerAmount);

        // speed (smaller times = faster)
        onTimeMin  = Mathf.Lerp(0.25f, 0.03f, t);
        onTimeMax  = Mathf.Lerp(0.80f, 0.18f, t);
        offTimeMin = Mathf.Lerp(0.15f, 0.02f, t);
        offTimeMax = Mathf.Lerp(0.50f, 0.12f, t);

        // intensity
        litBrightnessMin = Mathf.Lerp(0.85f, 0.35f, t);   // how much it can drop when it comes back on
        darkLevel        = Mathf.Lerp(0.25f, 0.02f, t);    // how dark the “off” moments get
    }

    // ------------------------------------------------------------------ flicker pattern

    // advances one pattern and returns the brightness factor (1 = normal)
    float Step(Flicker f, float dt)
    {
        f.timer -= dt;
        for (int guard = 0; f.timer <= 0f && guard < 8; guard++)
        {
            f.lit = !f.lit;
            if (f.lit)
            {
                f.level = Random.Range(Mathf.Min(litBrightnessMin, 1f), 1f);
                f.timer += Random.Range(onTimeMin, Mathf.Max(onTimeMin, onTimeMax));
            }
            else
            {
                f.timer += Random.Range(offTimeMin, Mathf.Max(offTimeMin, offTimeMax));
            }
        }
        return f.lit ? f.level : darkLevel;
    }

    // ------------------------------------------------------------------ remember / restore the normal values

    void Capture()
    {
        lightEntries.Clear();
        materialEntries.Clear();

        if (lights != null)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l == null || HasLight(l)) continue;   // empty slot, or the same light dragged in twice

                LightEntry e = new LightEntry();
                e.light = l;
                e.baseIntensity = l.intensity;
                lightEntries.Add(e);
            }
        }

        if (emissionMaterials != null)
        {
            for (int i = 0; i < emissionMaterials.Length; i++)
            {
                Material m = emissionMaterials[i];
                if (m == null || HasMaterial(m)) continue;

                int id = FindEmissionColorId(m);
                if (id < 0)
                {
                    Debug.LogWarning("[Alarm] Material '" + m.name + "' has no emission colour property, so it can't flicker.", this);
                    continue;
                }

                Color c = m.GetColor(id);
                if (c.maxColorComponent < 0.001f)
                    Debug.LogWarning("[Alarm] Material '" + m.name + "' has a black emission colour: there is nothing to flicker. Give it an emission colour.", this);
                else if (m.HasProperty("_EmissionColor") && !m.IsKeywordEnabled("_EMISSION"))
                    Debug.LogWarning("[Alarm] Emission is switched off on material '" + m.name + "': tick 'Emission' in its Inspector.", this);

                MaterialEntry e = new MaterialEntry();
                e.material = m;
                e.colorId = id;
                e.baseColor = c;
                materialEntries.Add(e);
            }
        }
    }

    // puts every light and material back to the value it had before the outage
    void Restore()
    {
        if (!touched) return;
        touched = false;

        for (int i = 0; i < lightEntries.Count; i++)
        {
            LightEntry e = lightEntries[i];
            e.applied = -1f;
            if (e.light != null) e.light.intensity = e.baseIntensity;
        }

        for (int i = 0; i < materialEntries.Count; i++)
        {
            MaterialEntry e = materialEntries[i];
            e.applied = -1f;
            if (e.material != null) e.material.SetColor(e.colorId, e.baseColor);
        }
    }

    bool HasLight(Light l)
    {
        for (int i = 0; i < lightEntries.Count; i++)
            if (lightEntries[i].light == l) return true;
        return false;
    }

    bool HasMaterial(Material m)
    {
        for (int i = 0; i < materialEntries.Count; i++)
            if (materialEntries[i].material == m) return true;
        return false;
    }

    // the emission colour property: the usual names first, then any colour property with "emi" or "glow" in its name (Shader Graph)
    static int FindEmissionColorId(Material m)
    {
        for (int i = 0; i < EmissionColorNames.Length; i++)
            if (m.HasProperty(EmissionColorNames[i])) return Shader.PropertyToID(EmissionColorNames[i]);

        Shader sh = m.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
            string n = sh.GetPropertyName(i);
            string low = n.ToLowerInvariant();
            if (low.Contains("emi") || low.Contains("glow")) return Shader.PropertyToID(n);
        }
        return -1;
    }
}