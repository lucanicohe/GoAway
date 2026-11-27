using UnityEngine;

[DisallowMultipleComponent]
public class UploadZoneVisual : MonoBehaviour
{
    public enum ZoneState { Hidden, Uploading, Paused, Complete }
    [SerializeField] private Shader zoneShader;
    [Min(.01f)] public float radius = 3f;
    public Transform zoneCenter;
    public float floorOffset = .025f;
    [ColorUsage(false, true)] public Color uploadingColor = new Color(0f, 2f, 3f);
    [ColorUsage(false, true)] public Color pausedColor = new Color(3f, .1f, .1f);
    [ColorUsage(false, true)] public Color completeColor = new Color(.1f, 3f, .4f);
    [Range(0,.3f)] public float interiorOpacity = .05f;
    [Min(0)] public float pulseSpeed = 1f;
    [Min(0)] public float completeDuration = 1.5f;
    public ZoneState state = ZoneState.Hidden;
    private GameObject surface;
    private Material material;
    private float hideAt;

    private void Awake()
    {
        if (!zoneShader) zoneShader = Shader.Find("Custom/UploadZone");
        if (!zoneShader) { Debug.LogError("Assign UploadZone.shader in the Inspector.", this); enabled = false; return; }
        surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
        surface.name = "Upload Zone (Runtime)";
        Destroy(surface.GetComponent<Collider>());
        material = new Material(zoneShader);
        var renderer = surface.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Apply();
    }
    private void LateUpdate()
    {
        if (state == ZoneState.Complete && Time.time >= hideAt) state = ZoneState.Hidden;
        Apply();
    }
    private void Apply()
    {
        if (!surface) return;
        surface.SetActive(isActiveAndEnabled && state != ZoneState.Hidden);
        var center = zoneCenter ? zoneCenter.position : transform.position;
        surface.transform.SetPositionAndRotation(center + Vector3.up * floorOffset, Quaternion.Euler(90,0,0));
        surface.transform.localScale = new Vector3(radius*2, radius*2, 1);
        material.SetColor("_Tint", state == ZoneState.Paused ? pausedColor : state == ZoneState.Complete ? completeColor : uploadingColor);
        material.SetFloat("_Fill", interiorOpacity);
        material.SetFloat("_Pulse", pulseSpeed);
    }
    public void SetRadius(float value) { radius = Mathf.Max(.01f,value); }
    public void ShowUploading() { state = ZoneState.Uploading; Apply(); }
    public void ShowPaused() { state = ZoneState.Paused; Apply(); }
    public void ShowComplete() { if (state == ZoneState.Complete) return; state = ZoneState.Complete; hideAt = Time.time + completeDuration; Apply(); }
    public void Hide() { state = ZoneState.Hidden; Apply(); }
    private void OnDisable() { if (surface) surface.SetActive(false); }
    private void OnDestroy() { if (surface) Destroy(surface); if (material) Destroy(material); }
}
