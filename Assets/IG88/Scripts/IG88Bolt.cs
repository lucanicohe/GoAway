using UnityEngine;

/// <summary>
/// Red droid blaster bolt. Built and pooled by the GameManager (UnityEngine.Pool.ObjectPool, nothing to set up by hand).
/// Flies at the speed set in the GameManager, sphere-casts along its path like the player's bolt,
/// ignores droid / Flathead bodies (no friendly fire) and reports the first hit back to the GameManager.
/// When it has hit something or flown out of range it goes back into the pool (ReleaseDroidBolt).
///
/// OPTIMIZATION: the "is this a droid collider?" answer comes from GameManager.IsEnemyCollider, which remembers it
/// per collider (tag walk / component walk happens once per collider, never every frame).
/// </summary>
public class IG88Bolt : MonoBehaviour
{
    GameManager gm;
    float castRadius = 0.03f;
    float speed, range, travelled, damage;
    Vector3 dir = Vector3.forward;
    bool inFlight;
    readonly RaycastHit[] hits = new RaycastHit[16];
    Light[] lights;
    AudioSource flightAudio;

    public bool InFlight { get { return inFlight; } }

    /// <summary>True for a Flathead bolt (it goes back into the Flathead pool).</summary>
    public bool IsFlathead { get; private set; }

    public void Setup(GameManager manager, float radius, bool flathead = false)
    {
        gm = manager;
        castRadius = radius;
        IsFlathead = flathead;

        flightAudio = gameObject.AddComponent<AudioSource>();
        if (gm != null)
            gm.ConfigureEnemyBoltFlightAudio(flightAudio);
    }

    public void Launch(Vector3 origin, Vector3 direction, float boltSpeed, float maxRange, float boltDamage,
                       float lightIntensity, float lightRange, Color lightColor)
    {
        dir = direction.normalized;
        speed = boltSpeed;
        range = maxRange;
        damage = boltDamage;
        travelled = 0f;
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir));
        ApplyLights(lightIntensity, lightRange, lightColor);
        inFlight = true;
        gameObject.SetActive(true);
        if (flightAudio != null && flightAudio.clip != null)
            flightAudio.Play();
    }

    // Every light on the bolt: shadows always off, intensity / range / colour from the GameManager (so the Inspector values work live).
    // The lights are looked up on the first launch (the GameManager adds the light after Setup).
    void ApplyLights(float intensity, float lightRange, Color lightColor)
    {
        if (lights == null)
        {
            lights = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
                lights[i].shadows = LightShadows.None;
        }
        for (int i = 0; i < lights.Length; i++)
        {
            lights[i].intensity = intensity;
            lights[i].range = lightRange;
            lights[i].color = lightColor;
        }
    }

    // Back into the pool (only once per flight).
    void Finish()
    {
        if (!inFlight) return;
        inFlight = false;
        if (flightAudio != null)
            flightAudio.Stop();
        if (gm != null) gm.ReleaseDroidBolt(this);
        else gameObject.SetActive(false);
    }

    void Update()
    {
        if (!inFlight) return;

        float step = speed * Time.deltaTime;
        Vector3 pos = transform.position;

        int n = Physics.SphereCastNonAlloc(pos, castRadius, dir, hits, step, ~0, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            if (GameManager.IsEnemyCollider(c)) continue;   // droids never shoot each other
            if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = i; }
        }

        if (best >= 0)
        {
            if (gm != null) gm.DroidBoltHit(hits[best], dir, damage);
            Finish();
            return;
        }

        transform.position = pos + dir * step;
        travelled += step;
        if (travelled >= range) Finish();
    }
}
