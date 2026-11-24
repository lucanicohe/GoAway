using UnityEngine;

/// <summary>
/// One blaster bolt. Created and recycled by BlasterWeapon (a small pool made at start, nothing is spawned later).
/// It flies in a straight line and checks the path it travelled every frame, so it can never skip through a thin wall.
/// </summary>
public class BlasterBolt : MonoBehaviour
{
    private BlasterWeapon owner;
    private Vector3 direction;
    private float speed;
    private float remaining;
    private float halfLength;
    private float castRadius;
    private Light[] lights;

    public bool InFlight { get { return gameObject.activeSelf; } }

    public void Setup(BlasterWeapon weapon, float length, float radius)
    {
        owner = weapon;
        halfLength = length * 0.5f;
        castRadius = radius;
    }

    /// <summary>Start the bolt with its tail at the muzzle.</summary>
    public void Launch(Vector3 muzzlePosition, Vector3 dir, float boltSpeed, float range)
    {
        direction = dir.normalized;
        speed = boltSpeed;
        remaining = range;
        transform.SetPositionAndRotation(muzzlePosition + direction * halfLength, Quaternion.LookRotation(direction));
        NoShadows();
        gameObject.SetActive(true);
    }

    // every light on the bolt gets shadows off (done once, on the first launch)
    private void NoShadows()
    {
        if (lights != null)
            return;
        lights = GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
            lights[i].shadows = LightShadows.None;
    }

    private void Update()
    {
        float step = speed * Time.deltaTime;
        if (step <= 0f)
            return;

        Vector3 head = transform.position + direction * halfLength;

        RaycastHit hit;
        if (owner != null && owner.CastIgnoringOwner(head, direction, step, castRadius, out hit))
        {
            transform.position = hit.point - direction * halfLength;
            owner.RegisterHit(hit, direction);
            gameObject.SetActive(false);
            return;
        }

        transform.position += direction * step;
        remaining -= step;
        if (remaining <= 0f)
            gameObject.SetActive(false);
    }
}