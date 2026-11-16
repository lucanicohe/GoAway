using UnityEngine;

/// <summary>
/// Example of how an enemy takes damage from the blaster. Used by the test scene.
/// It has health, flashes white when hit, "dies" at 0 and comes back after a few seconds.
/// Copy the idea into your real enemy: implement IBlasterDamageable and use hit.damage.
/// </summary>
[AddComponentMenu("Blaster/Test Target")]
public class BlasterTestTarget : MonoBehaviour, IBlasterDamageable
{
    [SerializeField] private float maxHealth = 100f;
    [Tooltip("Seconds until it comes back after dying. 0 = never.")]
    [SerializeField] private float respawnDelay = 2.5f;
    [SerializeField] private float hitFlashTime = 0.12f;

    private float health;
    private float flashTimer;
    private float respawnTimer;
    private bool dead;
    private Renderer[] renderers;
    private Collider[] colliders;
    private MaterialPropertyBlock block;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public float Health { get { return health; } }

    private void Awake()
    {
        health = maxHealth;
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        block = new MaterialPropertyBlock();
    }

    public void TakeBlasterDamage(BlasterHitInfo hit)
    {
        if (dead)
            return;

        health -= hit.damage;
        Debug.Log("[Blaster] target \"" + name + "\" took " + hit.damage.ToString("F0") + " damage, health " +
                  Mathf.Max(0f, health).ToString("F0") + "/" + maxHealth.ToString("F0"), this);

        flashTimer = hitFlashTime;
        SetFlash(true);

        if (health <= 0f)
            Die();
    }

    private void Die()
    {
        dead = true;
        respawnTimer = respawnDelay;
        SetVisible(false);
        Debug.Log("[Blaster] target \"" + name + "\" destroyed", this);
    }

    private void Update()
    {
        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0f)
                SetFlash(false);
        }

        if (dead && respawnDelay > 0f)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f)
            {
                dead = false;
                health = maxHealth;
                SetFlash(false);
                SetVisible(true);
            }
        }
    }

    private void SetVisible(bool visible)
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = visible;
    }

    private void SetFlash(bool on)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            if (on)
            {
                block.SetColor(BaseColorId, Color.white);
                block.SetColor(EmissionColorId, new Color(1.5f, 1.5f, 1.5f, 1f));
                r.SetPropertyBlock(block);
            }
            else
            {
                r.SetPropertyBlock(null);
            }
        }
    }
}
