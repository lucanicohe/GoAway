using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Only for the test scene: a crosshair, a short help text, the last hit on screen, and a bloom volume
/// (added only if the scene has no global volume with Bloom) so the green glow shows.
/// Delete this object in your own scenes.
/// </summary>
[AddComponentMenu("Blaster/Test Scene Helper")]
public class BlasterTestScene : MonoBehaviour
{
    [SerializeField] private bool addBloomIfMissing = true;
    [SerializeField] private bool showCrosshair = true;
    [SerializeField] private bool showHelp = true;

    private string lastHit = "";
    private float lastHitTime = -100f;
    private int hits;
    private GUIStyle labelStyle;
    private Texture2D dot;

    private void OnEnable()
    {
        BlasterWeapon.AnyHit += OnAnyHit;
    }

    private void OnDisable()
    {
        BlasterWeapon.AnyHit -= OnAnyHit;
    }

    private void Start()
    {
        if (addBloomIfMissing)
            AddBloom();
    }

    private void OnAnyHit(BlasterHitInfo info)
    {
        hits++;
        lastHitTime = Time.time;
        lastHit = "HIT  " + info.gameObject.name + "   " + info.distance.ToString("F1") + " m" +
                  (info.damageableFound ? "   damage " + info.damage.ToString("F0") : "   (not damageable)");
    }

    private void AddBloom()
    {
        Volume[] volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume v = volumes[i];
            if (v.isGlobal && v.sharedProfile != null && v.sharedProfile.Has<Bloom>())
                return;
        }

        GameObject go = new GameObject("Test Scene Bloom Volume");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        Bloom bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(1.4f);
        bloom.scatter.Override(0.65f);
        volume.sharedProfile = profile;
    }

    private void OnGUI()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 15;
            labelStyle.normal.textColor = new Color(0.75f, 1f, 0.75f, 1f);
        }
        if (dot == null)
        {
            dot = new Texture2D(1, 1);
            dot.SetPixel(0, 0, Color.white);
            dot.Apply();
        }

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;

        if (showCrosshair)
        {
            Color old = GUI.color;
            GUI.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 1f, 3f, 3f), dot);
            GUI.DrawTexture(new Rect(cx - 12f, cy - 0.5f, 7f, 1.5f), dot);
            GUI.DrawTexture(new Rect(cx + 6f, cy - 0.5f, 7f, 1.5f), dot);
            GUI.DrawTexture(new Rect(cx - 0.5f, cy - 12f, 1.5f, 7f), dot);
            GUI.DrawTexture(new Rect(cx - 0.5f, cy + 6f, 1.5f, 7f), dot);
            GUI.color = old;
        }

        if (showHelp)
        {
            GUI.Label(new Rect(14f, 10f, 700f, 26f), "Left mouse: fire (hold for auto)    WASD: move    Shift: sprint    Space: jump    Esc: release mouse", labelStyle);
            GUI.Label(new Rect(14f, 32f, 700f, 26f), "Hits: " + hits + "   (the Console shows what every bolt hit)", labelStyle);
        }

        if (Time.time - lastHitTime < 2.5f)
            GUI.Label(new Rect(cx - 250f, cy + 40f, 500f, 26f), lastHit, labelStyle);
    }
}
