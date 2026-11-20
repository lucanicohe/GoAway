using UnityEngine;

/// <summary>
/// Red droid blaster bolt. Built and pooled by the GameManager (nothing to set up by hand).
/// Flies at the speed set in the GameManager, sphere-casts along its path like the player's bolt,
/// ignores droid bodies (no friendly fire) and reports the first hit back to the GameManager.
/// </summary>
public class IG88Bolt : MonoBehaviour
{
    GameManager gm;
    float castRadius = 0.03f;
    float speed, range, travelled, damage;
    Vector3 dir = Vector3.forward;
    readonly RaycastHit[] hits = new RaycastHit[16];

    public void Setup(GameManager manager, float radius)
    {
        gm = manager;
        castRadius = radius;
    }

    public void Launch(Vector3 origin, Vector3 direction, float boltSpeed, float maxRange, float boltDamage)
    {
        dir = direction.normalized;
        speed = boltSpeed;
        range = maxRange;
        damage = boltDamage;
        travelled = 0f;
        transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir));
        gameObject.SetActive(true);
    }

    void Update()
    {
        float step = speed * Time.deltaTime;
        Vector3 pos = transform.position;

        int n = Physics.SphereCastNonAlloc(pos, castRadius, dir, hits, step, ~0, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            if (c.GetComponentInParent<IG88Controller>() != null) continue; // droids never shoot each other
            if (hits[i].distance < bestDist) { bestDist = hits[i].distance; best = i; }
        }

        if (best >= 0)
        {
            if (gm != null) gm.DroidBoltHit(hits[best], dir, damage);
            gameObject.SetActive(false);
            return;
        }

        transform.position = pos + dir * step;
        travelled += step;
        if (travelled >= range) gameObject.SetActive(false);
    }
}
