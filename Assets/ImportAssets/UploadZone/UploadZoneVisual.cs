
using UnityEngine;

[DisallowMultipleComponent]
public class UploadZoneVisual : MonoBehaviour
{
    public enum ZoneState
    {
        Hidden, Uploading, Paused, Complete
    }

    [SerializeField] private Shader zoneShader;

    [Min(.01f)] public float radius = 3f;
    public Transform zoneCenter;
    public float floorOffset = .025f;

    [ColorUsage(false, true)]
    public Color uploadingColor = new Color(.1f, 3f, .4f);

    [ColorUsage(false, true)]
    public Color pausedColor = new Color(3f, .1f, .1f);

    [ColorUsage(false, true)]
    public Color completeColor = new Color(.1f, 3f, .4f);

    [Range(0, .3f)]
    public float interiorOpacity = .05f;

    [Min(0)]
    public float pulseSpeed = 1f;

    [Header("Completion Shockwave")]

    [Min(.01f)]
    public float completeDuration = 3f;

    [Min(0f)]
    public float expansionSpeed = 12f;

    [Min(0f)]
    public float expansionAcceleration = 4f;

    [Min(0.1f)]
    public float fadePower = 1.5f;

    public ZoneState state = ZoneState.Hidden;

    private GameObject surface;
    private Material material;

    private float completeStartedAt;
    private float completeStartRadius;

    private void Awake()
    {
        if (!zoneShader)
            zoneShader = Shader.Find("Custom/UploadZone");

        if (!zoneShader)
        {
            Debug.LogError(
                "Assign UploadZone.shader in the Inspector.",
                this
            );

            enabled = false;
            return;
        }

        surface = GameObject.CreatePrimitive(
            PrimitiveType.Quad
        );

        surface.name = "Upload Zone (Runtime)";

        Collider col = surface.GetComponent<Collider>();

        if (col != null)
        {
            col.enabled = false;
            Destroy(col);
        }

        material = new Material(zoneShader);

        var renderer = surface.GetComponent<MeshRenderer>();

        renderer.sharedMaterial = material;
        renderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;

        renderer.receiveShadows = false;

        Apply();
    }

    private void LateUpdate()
    {
        if (state == ZoneState.Complete)
        {
            float elapsed =
                Time.unscaledTime - completeStartedAt;

            if (elapsed >= completeDuration)
                state = ZoneState.Hidden;
        }

        Apply();
    }

    private void Apply()
    {
        if (!surface)
            return;

        surface.SetActive(
            isActiveAndEnabled &&
            state != ZoneState.Hidden
        );

        if (state == ZoneState.Hidden)
            return;

        Vector3 center = zoneCenter
            ? zoneCenter.position
            : transform.position;

        float visualRadius = radius;
        float fill = interiorOpacity;

        Color tint = state == ZoneState.Paused
            ? pausedColor
            : uploadingColor;

        if (state == ZoneState.Complete)
        {
            float elapsed = Mathf.Max(
                0f,
                Time.unscaledTime - completeStartedAt
            );

            float progress = Mathf.Clamp01(
                elapsed / Mathf.Max(.01f, completeDuration)
            );

            // Expand continuously, with no radius cap.
            visualRadius =
                completeStartRadius +
                expansionSpeed * elapsed +
                .5f * expansionAcceleration *
                elapsed * elapsed;

            // Gradually fade the perimeter.
            float fade = Mathf.Pow(
                1f - progress,
                fadePower
            );

            tint = completeColor;

            tint.r *= fade;
            tint.g *= fade;
            tint.b *= fade;
            tint.a = fade;

            // Interior disappears faster than the ring.
            fill = interiorOpacity *
                Mathf.Pow(1f - progress, 4f);
        }

        surface.transform.SetPositionAndRotation(
            center + Vector3.up * floorOffset,
            Quaternion.Euler(90, 0, 0)
        );

        surface.transform.localScale = new Vector3(
            visualRadius * 2f,
            visualRadius * 2f,
            1f
        );

        material.SetColor("_Tint", tint);
        material.SetFloat("_Fill", fill);
        material.SetFloat("_Pulse", pulseSpeed);
    }

    public void SetRadius(float value)
    {
        radius = Mathf.Max(.01f, value);
    }

    public void ShowUploading()
    {
        state = ZoneState.Uploading;
        Apply();
    }

    public void ShowPaused()
    {
        state = ZoneState.Paused;
        Apply();
    }

    public void ShowComplete()
    {
        if (state == ZoneState.Complete)
            return;

        completeStartedAt = Time.unscaledTime;
        completeStartRadius = radius;

        state = ZoneState.Complete;

        Apply();
    }

    public void Hide()
    {
        state = ZoneState.Hidden;
        Apply();
    }

    private void OnDisable()
    {
        if (surface)
            surface.SetActive(false);
    }

    private void OnDestroy()
    {
        if (surface)
            Destroy(surface);

        if (material)
            Destroy(material);
    }
}
