using UnityEngine;

/// <summary>
/// The sparks (and a short flash + light) that fly off whatever the blaster hits.
/// A pool of these is built by BlasterWeapon at start. Play() restarts one at a hit point.
/// The sparks are tiny stretched cubes moved by this script, no particle system is used.
/// </summary>
public class BlasterHitSparks : MonoBehaviour
{
    private Transform[] sparks;
    private Vector3[] velocity;
    private float[] life;
    private float[] maxLife;

    private Transform flash;
    private Light hitLight;

    private float gravity;
    private float width;
    private float length;
    private float flashSize;
    private float flashTime = 0.09f;
    private float flashTimer;
    private float lightIntensity;
    private float speedMin, speedMax, lifeMin, lifeMax;
    private bool thermalLook;

    public void Build(int count, Material materialA, Material materialB, Material flashMaterial,
                      float sparkWidth, float sparkLength, float impactFlashSize,
                      float minSpeed, float maxSpeed, float minLife, float maxLife_, float gravityValue,
                      bool useLight, Color lightColor, float intensity, float range)
    {
        width = sparkWidth;
        length = sparkLength;
        flashSize = impactFlashSize;
        speedMin = minSpeed; speedMax = maxSpeed;
        lifeMin = minLife; lifeMax = maxLife_;
        gravity = gravityValue;
        lightIntensity = intensity;

        sparks = new Transform[count];
        velocity = new Vector3[count];
        life = new float[count];
        maxLife = new float[count];

        for (int i = 0; i < count; i++)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "Spark " + i;
            RemoveCollider(s);
            s.transform.SetParent(transform, false);
            MeshRenderer mr = s.GetComponent<MeshRenderer>();
            mr.sharedMaterial = (i % 3 == 0) ? materialB : materialA;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            s.SetActive(false);
            sparks[i] = s.transform;
        }

        GameObject f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        f.name = "Impact Flash";
        RemoveCollider(f);
        f.transform.SetParent(transform, false);
        MeshRenderer fr = f.GetComponent<MeshRenderer>();
        fr.sharedMaterial = flashMaterial;
        fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fr.receiveShadows = false;
        f.SetActive(false);
        flash = f.transform;

        if (useLight)
        {
            GameObject l = new GameObject("Impact Light");
            l.transform.SetParent(transform, false);
            hitLight = l.AddComponent<Light>();
            hitLight.type = LightType.Point;
            hitLight.color = lightColor;
            hitLight.range = range;
            hitLight.intensity = 0f;
            hitLight.shadows = LightShadows.None;
            hitLight.enabled = false;
        }

        gameObject.SetActive(false);
    }

    private static void RemoveCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null)
            Destroy(c);
    }

    public void SetThermalLook(bool on)
    {
        thermalLook = on;
        if (hitLight != null && on)
            hitLight.enabled = false;
    }

    /// <summary>Restart this burst at a hit point. Sparks fly out along the surface normal.</summary>
    public void Play(Vector3 point, Vector3 normal)
    {
        gameObject.SetActive(true);
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        for (int i = 0; i < sparks.Length; i++)
        {
            Vector3 dir = normal + Random.insideUnitSphere * 1.1f;
            if (Vector3.Dot(dir, normal) < 0.1f)
                dir = Vector3.Reflect(dir, normal) + normal * 0.2f;
            dir.Normalize();

            velocity[i] = dir * Random.Range(speedMin, speedMax);
            maxLife[i] = Random.Range(lifeMin, lifeMax);
            life[i] = maxLife[i];

            sparks[i].position = point + normal * 0.02f;
            sparks[i].rotation = Quaternion.LookRotation(dir);
            sparks[i].localScale = new Vector3(width, width, length);
            sparks[i].gameObject.SetActive(true);
        }

        flash.position = point + normal * 0.03f;
        flash.localScale = Vector3.one * flashSize;
        flash.gameObject.SetActive(true);
        flashTimer = flashTime;

        if (hitLight != null)
        {
            hitLight.transform.position = point + normal * 0.25f;
            hitLight.intensity = lightIntensity;
            hitLight.enabled = !thermalLook;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool anyAlive = false;

        for (int i = 0; i < sparks.Length; i++)
        {
            if (life[i] <= 0f)
                continue;

            life[i] -= dt;
            if (life[i] <= 0f)
            {
                sparks[i].gameObject.SetActive(false);
                continue;
            }

            anyAlive = true;
            velocity[i] += Vector3.down * gravity * dt;
            Vector3 p = sparks[i].position + velocity[i] * dt;
            sparks[i].position = p;

            if (velocity[i].sqrMagnitude > 0.0001f)
                sparks[i].rotation = Quaternion.LookRotation(velocity[i]);

            float k = life[i] / maxLife[i];          // 1 -> 0
            sparks[i].localScale = new Vector3(width * k, width * k, length * Mathf.Lerp(0.35f, 1f, k));
        }

        if (flashTimer > 0f)
        {
            flashTimer -= dt;
            float t = Mathf.Clamp01(flashTimer / flashTime);   // 1 -> 0
            flash.localScale = Vector3.one * flashSize * (0.4f + 0.6f * t);
            if (hitLight != null)
                hitLight.intensity = lightIntensity * t;
            if (flashTimer <= 0f)
            {
                flash.gameObject.SetActive(false);
                if (hitLight != null)
                    hitLight.enabled = false;
            }
            else
            {
                anyAlive = true;
            }
        }

        if (!anyAlive)
            gameObject.SetActive(false);
    }
}
