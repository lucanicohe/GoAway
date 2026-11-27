using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class AmmoSupply : MonoBehaviour
{


    [Header("References")]
    public GameManager gameManager;
    public Shader circleShader, iconShader;

    [Header("Battery Charge")]
    [SerializeField] private Renderer[] chargeCylinders;
    [SerializeField, Range(0f, 1f)]
    private float depletedGlow = 0.02f;

    [Header("Supply Zone")]
    [Min(.1f)] public float radius = 1f;
    public float floorOffset = .025f;

    [Header("Floating Icon")]
    public float iconHeight = .85f;
    public float iconSize = .45f;
    public float bobAmplitude = .08f;
    public float bobSpeed = 2f;

    [Header("Appearance")]
    [ColorUsage(false, true)]
    public Color tint = new Color(1.5f, 3f, .25f, 1f);

    [Range(0f, 1f)]
    public float circleOpacity = .5f;

    [Range(0f, 1f)]
    public float iconOpacity = 1f;

    [Header("HUD")]
    public UnityEngine.UI.Text promptText;
    public string prompt = "「 R 」弾薬を補充する";

    [Header("Audio")]
    public AudioClip reloadSound;

    [Header("Collection Animation")]
    [Min(.1f)] public float fadeDuration = .65f;
    public float expansionSpeed = 3f;

    private Transform circle, icon;
    private Material circleMat, iconMat;
    private bool consumed, ownsPrompt;
    private float consumedAt;

    void Start()
    {
        if (promptText)
            promptText.enabled = false;

        if (!gameManager)
            gameManager = FindFirstObjectByType<GameManager>();

        if (!circleShader)
            circleShader = Shader.Find("Custom/AmmoSupply");

        if (!iconShader)
            iconShader = Shader.Find("Custom/AmmoIcon");

        if (!gameManager || !circleShader || !iconShader)
        {
            Debug.LogError(
                "AmmoSupply: missing manager or shaders", this);
            enabled = false;
            return;
        }

        circle = MakeQuad(
            "Supply Circle", circleShader, out circleMat);

        icon = MakeQuad(
            "Ammo Icon", iconShader, out iconMat);
    }

    Transform MakeQuad(
        string label, Shader shader, out Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);

        go.name = label;
        go.transform.SetParent(transform, false);

        var collider = go.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);

        mat = new Material(shader);

        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        return go.transform;
    }

    void Update()
    {
        if (!circle || !gameManager)
            return;

        float elapsed = consumed
            ? Time.time - consumedAt
            : 0f;

        float fade = consumed
            ? 1f - Mathf.Clamp01(elapsed / fadeDuration)
            : 1f;

        if (consumed && fade <= 0f)
        {
            enabled = false;
            return;
        }

        // Floor circle
        float r = radius + elapsed * expansionSpeed;

        circle.SetPositionAndRotation(
            transform.position + Vector3.up * floorOffset,
            Quaternion.Euler(90, 0, 0));

        circle.localScale = new Vector3(r * 2, r * 2, 1);

        // Floating ammunition icon
        icon.position = transform.position + Vector3.up *
            (iconHeight
            + Mathf.Sin(Time.time * bobSpeed) * bobAmplitude
            + elapsed * .5f);

        var cam = UnityEngine.Camera.main;

        if (cam)
            icon.rotation = cam.transform.rotation;

        icon.localScale = Vector3.one * iconSize;

        // Color and independent opacity
        circleMat.SetColor("_Tint", tint);
        iconMat.SetColor("_Tint", tint);

        circleMat.SetFloat("_Fade", fade * circleOpacity);
        iconMat.SetFloat("_Fade", fade * iconOpacity);

        if (consumed)
            return;

        // Check whether player can reload
        var player = gameManager.Player;

        Vector3 delta = player
            ? player.position - transform.position
            : Vector3.one * 99999f;

        bool canReload =
            player &&
            gameManager.PlayerAlive &&
            Mathf.Abs(delta.y) <= 2f &&
            new Vector2(delta.x, delta.z).sqrMagnitude
                <= radius * radius &&
            gameManager.CurrentAmmo < gameManager.MaxAmmo;

        SetPrompt(canReload);

        // Reload only inside the supply zone
        if (canReload && Pressed())
        {
            gameManager.SetAmmo(gameManager.MaxAmmo);

            consumed = true;
            consumedAt = Time.time;

            DimBattery();

            SetPrompt(false);

            if (reloadSound)
                AudioSource.PlayClipAtPoint(
                    reloadSound, transform.position);
        }
    }

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null &&
            Keyboard.current.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.R);
#else
        return false;
#endif
    }

    void SetPrompt(bool show)
    {
        if (!promptText)
            return;

        if (show)
        {
            promptText.text = prompt;
            promptText.enabled = true;
            ownsPrompt = true;
        }
        else if (ownsPrompt)
        {
            if (promptText.text == prompt)
            {
                promptText.text = "";
                promptText.enabled = false;
            }

            ownsPrompt = false;
        }
    }

    void OnDisable()
    {
        SetPrompt(false);

        if (circle)
            circle.gameObject.SetActive(false);

        if (icon)
            icon.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (circle)
            circle.gameObject.SetActive(true);

        if (icon)
            icon.gameObject.SetActive(true);
    }

    void OnDestroy()
    {
        if (circleMat)
            Destroy(circleMat);

        if (iconMat)
            Destroy(iconMat);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    void DimBattery()
    {
        foreach (Renderer cylinder in chargeCylinders)
        {
            if (!cylinder)
                continue;

            Material mat = cylinder.material;

            if (mat.HasProperty("_EmissionColor"))
            {
                Color emission = mat.GetColor("_EmissionColor");

                mat.SetColor("_EmissionColor",
                    emission * depletedGlow);
            }
        }
    }
}