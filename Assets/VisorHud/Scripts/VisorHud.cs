using UnityEngine;
using UnityEngine.UI;

/// <summary>Colour + opacity of one HUD element (shown as a small foldout in the Inspector).</summary>
[System.Serializable]
public class VisorHudPart
{
    [UnityEngine.ColorUsage(false)] public Color color = Color.white;
    [Range(0f, 1f)] public float opacity = 1f;

    public VisorHudPart() { }
    public VisorHudPart(float opacity) { this.opacity = opacity; }

    public Color Rgba { get { return new Color(color.r, color.g, color.b, Mathf.Clamp01(opacity)); } }
}

/// <summary>
/// Visor HUD controller (Republic-Commando style). Sits on the root of the "Visor HUD" canvas prefab.
/// Drag the sliders in the Inspector (works live, also while the game is running):
///   Health = left green striped bar        Ammo = right red striped bar
/// Each bar empties along an angle you pick in the Inspector (Health leans one way, Ammo the mirror of it).
/// The stripes are never scaled or squished - the bar is just cut off along a straight line (see VisorHudBarWipe.cs).
/// From your own code:   hud.SetHealth(0.5f);   hud.SetAmmo(0.25f);     (values 0 - 1)
/// Every visor / HUD element has its own colour and its own opacity slider.
/// The top line is a compass (N E S W) that follows the Main Camera (or any Transform you assign).
/// The canvas is Screen Space - Overlay, so it works with any camera (incl. the Cinemachine first-person rig).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("Visor HUD/Visor HUD")]
public class VisorHud : MonoBehaviour
{
    [Header("BARS  (drag the sliders)")]
    [Range(0f, 1f)] public float health = 1f;
    [Range(0f, 1f)] public float ammo = 1f;

    [Header("Bar look")]
    public Color healthColor = new Color(0.25f, 1f, 0.35f, 1f);
    public Color ammoColor = new Color(1f, 0.22f, 0.22f, 1f);
    [Range(0f, 1f)] public float healthBarOpacity = 1f;
    [Range(0f, 1f)] public float ammoBarOpacity = 1f;
    [Tooltip("How visible the empty part of a bar is.")]
    [Range(0f, 1f)] public float emptyBarOpacity = 0.35f;
    [Tooltip("Seconds the bars take to follow a change while playing. 0 = instant.")]
    [Range(0f, 1f)] public float smoothTime = 0.12f;

    [Header("Bar empty angle  (degrees - the way each bar goes empty, as seen on screen)")]
    [Tooltip("Direction the health bar empties in. 0 = toward the right, 180 = toward the left (right end empties first), 90 = up, -90 = down. In between it tilts, e.g. 147 = toward the left and tilted 33 degrees up. If it tilts the wrong way use -147.")]
    [Range(-180f, 180f)] public float healthEmptyAngle = 147f;
    [Tooltip("Direction the ammo bar empties in. 0 = toward the right (left end empties first), 180 = toward the left, 90 = up, -90 = down. In between it tilts, e.g. 33 = toward the right and tilted 33 degrees up. If it tilts the wrong way use -33.")]
    [Range(-180f, 180f)] public float ammoEmptyAngle = 33f;

    [Header("HUD parts (tick to show / hide)")]
    public bool showTopVisor = true;
    public bool showCompass = true;
    public bool showSideArcs = true;
    public bool showBottomPanels = true;
    public bool showBars = true;
    public bool showCrosshair = true;
    [Tooltip("Master opacity of the whole HUD (on top of the individual opacities below).")]
    [Range(0f, 1f)] public float hudOpacity = 1f;

    [Header("Visor metal  (colour tints the metal, opacity 1 = solid)")]
    public VisorHudPart topVisorMetal = new VisorHudPart(1f);
    public VisorHudPart topVisorEdgeLine = new VisorHudPart(1f);
    public VisorHudPart leftPanelMetal = new VisorHudPart(1f);
    public VisorHudPart leftPanelEdgeLine = new VisorHudPart(1f);
    public VisorHudPart rightPanelMetal = new VisorHudPart(1f);
    public VisorHudPart rightPanelEdgeLine = new VisorHudPart(1f);

    [Header("White HUD elements  (colour + opacity of each)")]
    public VisorHudPart leftArcColor = new VisorHudPart(1f);
    public VisorHudPart rightArcColor = new VisorHudPart(1f);
    public VisorHudPart compassLineColor = new VisorHudPart(0.47f);
    public VisorHudPart compassTicksColor = new VisorHudPart(0.94f);
    public VisorHudPart compassLettersColor = new VisorHudPart(0.9f);

    [Header("Crosshair")]
    public Color crosshairColor = Color.white;
    [Range(0f, 1f)] public float crosshairOpacity = 1f;
    [Range(32f, 256f)] public float crosshairSize = 96f;

    [Header("Compass (top line)")]
    [Tooltip("What the compass follows. Leave empty to use the Main Camera.")]
    public Transform compassTarget;
    [Tooltip("World direction that counts as North, as a rotation around the Y axis in degrees. 0 = North is along the +Z axis.")]
    [Range(-180f, 180f)] public float northOffset = 0f;
    [Tooltip("How many degrees fit across the compass line. Smaller = letters move faster.")]
    [Range(60f, 360f)] public float compassVisibleDegrees = 180f;
    [Range(12f, 48f)] public float compassLetterSize = 24f;
    [Tooltip("Tick this to fake the heading and preview the letters without moving.")]
    public bool useTestHeading = false;
    [Range(0f, 360f)] public float testHeading = 0f;

    [Header("Labels (text under the bars)")]
    public bool showLabels = true;
    public Color labelColor = new Color(0.84f, 0.93f, 0.96f, 1f);
    [Range(0f, 1f)] public float healthLabelOpacity = 1f;
    [Range(0f, 1f)] public float ammoLabelOpacity = 1f;
    [Range(10f, 48f)] public float labelSize = 22f;

    [Header("Number texts  (live health 120 -> 0 and ammo count)")]
    [Tooltip("Optional. Leave empty and the label under the health bar shows the health number.")]
    public Text healthNumberText;
    [Tooltip("Optional. Leave empty and the label under the ammo bar shows the ammo number.")]
    public Text ammoNumberText;
    public Color numberColor = new Color(0.84f, 0.93f, 0.96f, 1f);
    [Range(0f, 1f)] public float healthNumberOpacity = 1f;
    [Range(0f, 1f)] public float ammoNumberOpacity = 1f;
    [Range(10f, 64f)] public float numberSize = 28f;

    [Header("Wiring (filled in automatically - leave alone)")]
    public Image healthFill;
    public Image healthBack;
    public Image ammoFill;
    public Image ammoBack;
    public Image crosshairImage;
    public Text healthLabel, ammoLabel;
    public GameObject topVisor, leftArc, rightArc, leftPanel, rightPanel, healthBar, ammoBar, crosshair;
    public CanvasGroup group;
    [UnityEngine.HideInInspector] public Image topBodyImage, topEdgeImage, compassLineImage, compassTicksImage;
    [UnityEngine.HideInInspector] public Image leftArcImage, rightArcImage;
    [UnityEngine.HideInInspector] public Image leftPanelBodyImage, leftPanelEdgeImage, rightPanelBodyImage, rightPanelEdgeImage;
    [UnityEngine.HideInInspector] public VisorHudBarWipe healthWipe, ammoWipe;   // cut-off effect on each Fill (added automatically)
    [UnityEngine.HideInInspector] public RectTransform compassArea;
    [UnityEngine.HideInInspector] public Text[] compassTexts;

    float shownHealth = 1f, shownAmmo = 1f;
    float prevShownHealth = -1f, prevShownAmmo = -1f;
    int lastHealthNumber = -9999, lastAmmoNumber = -9999;
    bool staticVisualsDirty = true;
    bool crosshairHidden;
    float lastYaw;

    public float Health { get { return health; } set { health = Mathf.Clamp01(value); } }
    public float Ammo { get { return ammo; } set { ammo = Mathf.Clamp01(value); } }
    public void SetHealth(float value01) { health = Mathf.Clamp01(value01); }
    public void SetAmmo(float value01) { ammo = Mathf.Clamp01(value01); }

    /// <summary>Marks static HUD visuals dirty so they get refreshed on the next frame.</summary>
    public void MarkVisualsDirty() { staticVisualsDirty = true; }

    /// <summary>Hides the crosshair while something else (e.g. an interact prompt) takes its place. Pass false to bring it back.</summary>
    public void SetCrosshairHidden(bool hidden)
    {
        if (crosshairHidden == hidden) return;
        crosshairHidden = hidden;
        Show(crosshair, showCrosshair && !crosshairHidden);
    }

    /// <summary>Compass bearing in degrees (0 = North, 90 = East, 180 = South, 270 = West).</summary>
    public float Heading { get { return Mathf.Repeat(ReadYaw() - northOffset, 360f); } }

    void OnEnable()
    {
        AutoWire();
        shownHealth = health;
        shownAmmo = ammo;
        staticVisualsDirty = true;
        Apply();
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        // changing UI objects is not allowed inside OnValidate, so do it a moment later
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            AutoWire();
            shownHealth = health;
            shownAmmo = ammo;
            staticVisualsDirty = true;
            Apply();
        };
#endif
    }

    void Update()
    {
        // Static visor panels and settings are only repainted when dirty, not every frame!
        if (staticVisualsDirty || !Application.isPlaying)
        {
            ApplyStaticLook();
            staticVisualsDirty = false;
        }

        bool barsChanged = false;
        if (Application.isPlaying && smoothTime > 0.0001f)
        {
            // starts moving at full speed right away (no slow ease-in) and is practically there after smoothTime seconds
            float k = 1f - Mathf.Exp(-3f * Time.deltaTime / smoothTime);
            float nextH = Mathf.Abs(health - shownHealth) < 0.0005f ? health : Mathf.Lerp(shownHealth, health, k);
            float nextA = Mathf.Abs(ammo - shownAmmo) < 0.0005f ? ammo : Mathf.Lerp(shownAmmo, ammo, k);
            if (Mathf.Abs(nextH - shownHealth) > 0.0001f || Mathf.Abs(nextA - shownAmmo) > 0.0001f)
            {
                shownHealth = nextH;
                shownAmmo = nextA;
                barsChanged = true;
            }
        }
        else
        {
            if (shownHealth != health || shownAmmo != ammo)
            {
                shownHealth = health;
                shownAmmo = ammo;
                barsChanged = true;
            }
        }

        // Only redraw the wipes and bar fills when the values are actively animating!
        if (barsChanged || prevShownHealth != shownHealth || prevShownAmmo != shownAmmo)
        {
            prevShownHealth = shownHealth;
            prevShownAmmo = shownAmmo;
            ApplyBars();
        }

        if (showCompass)
        {
            UpdateCompass();
        }
    }

    Transform FindDeep(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    Image FindImage(string childName)
    {
        Transform t = FindDeep(transform, childName);
        return t != null ? t.GetComponent<Image>() : null;
    }

    void AutoWire()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (topVisor == null) { Transform t = FindDeep(transform, "Top Visor"); if (t != null) topVisor = t.gameObject; }
        if (leftArc == null) { Transform t = FindDeep(transform, "Left Arc"); if (t != null) leftArc = t.gameObject; }
        if (rightArc == null) { Transform t = FindDeep(transform, "Right Arc"); if (t != null) rightArc = t.gameObject; }
        if (leftPanel == null) { Transform t = FindDeep(transform, "Left Panel"); if (t != null) leftPanel = t.gameObject; }
        if (rightPanel == null) { Transform t = FindDeep(transform, "Right Panel"); if (t != null) rightPanel = t.gameObject; }
        if (healthBar == null) { Transform t = FindDeep(transform, "Health Bar"); if (t != null) healthBar = t.gameObject; }
        if (ammoBar == null) { Transform t = FindDeep(transform, "Ammo Bar"); if (t != null) ammoBar = t.gameObject; }
        if (crosshair == null) { Transform t = FindDeep(transform, "Crosshair"); if (t != null) crosshair = t.gameObject; }
        if (crosshairImage == null && crosshair != null) crosshairImage = crosshair.GetComponent<Image>();
        if (healthBar != null)
        {
            if (healthFill == null) { Transform t = healthBar.transform.Find("Fill"); if (t != null) healthFill = t.GetComponent<Image>(); }
            if (healthBack == null) { Transform t = healthBar.transform.Find("Back"); if (t != null) healthBack = t.GetComponent<Image>(); }
            if (healthLabel == null) { Transform t = healthBar.transform.Find("Label"); if (t != null) healthLabel = t.GetComponent<Text>(); }
        }
        if (ammoBar != null)
        {
            if (ammoFill == null) { Transform t = ammoBar.transform.Find("Fill"); if (t != null) ammoFill = t.GetComponent<Image>(); }
            if (ammoBack == null) { Transform t = ammoBar.transform.Find("Back"); if (t != null) ammoBack = t.GetComponent<Image>(); }
            if (ammoLabel == null) { Transform t = ammoBar.transform.Find("Label"); if (t != null) ammoLabel = t.GetComponent<Text>(); }
        }

        // visor layers (older prefabs do not have them - everything below is null-checked when used)
        if (topBodyImage == null) topBodyImage = FindImage("Top Body");
        if (topEdgeImage == null) topEdgeImage = FindImage("Top Edge");
        if (compassLineImage == null) compassLineImage = FindImage("Compass Line");
        if (compassTicksImage == null) compassTicksImage = FindImage("Compass Ticks");
        if (leftArcImage == null && leftArc != null) leftArcImage = leftArc.GetComponent<Image>();
        if (rightArcImage == null && rightArc != null) rightArcImage = rightArc.GetComponent<Image>();
        if (leftPanelBodyImage == null) leftPanelBodyImage = FindImage("Left Panel Body");
        if (leftPanelEdgeImage == null) leftPanelEdgeImage = FindImage("Left Panel Edge");
        if (rightPanelBodyImage == null) rightPanelBodyImage = FindImage("Right Panel Body");
        if (rightPanelEdgeImage == null) rightPanelEdgeImage = FindImage("Right Panel Edge");

        // compass letters
        if (compassArea == null)
        {
            Transform t = FindDeep(transform, "Compass Letters");
            if (t != null) compassArea = t as RectTransform;
        }
        if (compassTexts == null || compassTexts.Length != 4) compassTexts = new Text[4];
        string[] letterNames = { "Letter N", "Letter E", "Letter S", "Letter W" };
        for (int i = 0; i < 4; i++)
        {
            if (compassTexts[i] != null) continue;
            Transform t = FindDeep(transform, letterNames[i]);
            if (t != null) compassTexts[i] = t.GetComponent<Text>();
        }

        EnsureWipes();
        HideOldTopFills();
    }

    // An earlier version of this script added a "Fill Top" child to each Fill. It is not used any more,
    // so it gets switched off here (you can delete it by hand if you see it in the Hierarchy).
    void HideOldTopFills()
    {
        HideOldTop(healthFill);
        HideOldTop(ammoFill);
    }

    static void HideOldTop(Image fill)
    {
        if (fill == null) return;
        Transform t = fill.transform.Find("Fill Top");
        if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
    }

    // older versions of this script squished the bar with localScale - undo that if it was saved in the scene
    static void FixScale(Image img)
    {
        RectTransform rt = img.rectTransform;
        Vector3 s = rt.localScale;
        if (s.y >= 0f && s.y < 0.9999f) { s.y = 1f; rt.localScale = s; }
    }

    // ---------- bars that empty along an angle ----------
    // The Fill image is a plain (Simple) image and a small effect (VisorHudBarWipe) clips it along a straight line.
    // Nothing is scaled, so the stripes keep their size. The effect is added to each Fill automatically.

    void EnsureWipes()
    {
        bool need = (healthFill != null && healthWipe == null) || (ammoFill != null && ammoWipe == null);
        if (!need) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // adding components while a scene / prefab is loading is not allowed, so do it a moment later
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                BuildWipes();
                Apply();
            };
            return;
        }
#endif
        BuildWipes();
    }

    void BuildWipes()
    {
        if (healthFill != null && healthWipe == null) healthWipe = GetOrAddWipe(healthFill);
        if (ammoFill != null && ammoWipe == null) ammoWipe = GetOrAddWipe(ammoFill);
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    static VisorHudBarWipe GetOrAddWipe(Image fill)
    {
        VisorHudBarWipe w = fill.GetComponent<VisorHudBarWipe>();
        if (w == null) w = fill.gameObject.AddComponent<VisorHudBarWipe>();
        return w;
    }

    // screen-space angle (0 = right, 90 = up) -> direction in the Fill's own space, so it still looks right if the bar is rotated or flipped
    Vector2 LocalWipeDir(RectTransform rt, float angleDeg)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        Vector3 world = transform.TransformDirection(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
        Vector3 local = rt.InverseTransformVector(world);
        Vector2 d = new Vector2(local.x, local.y);
        return d.sqrMagnitude > 0.000001f ? d.normalized : new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    void ApplyBar(Image fill, VisorHudBarWipe wipe, float value, Color color, float opacity, float angle)
    {
        if (fill == null) return;
        FixScale(fill);
        fill.color = WithAlpha(color, opacity);
        if (wipe == null) return;                                         // effect is added a moment after the first Apply

        if (fill.type != Image.Type.Simple) fill.type = Image.Type.Simple;   // the effect does the cutting, not the Image fill
        wipe.Set(Mathf.Clamp01(value), LocalWipeDir(fill.rectTransform, angle));
    }

    static Color Dim(Color c, float alpha) { return new Color(c.r * 0.25f, c.g * 0.25f, c.b * 0.25f, alpha); }

    static Color WithAlpha(Color c, float opacity) { return new Color(c.r, c.g, c.b, c.a * Mathf.Clamp01(opacity)); }

    static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }

    static void Paint(Image img, VisorHudPart part) { if (img != null && part != null) img.color = part.Rgba; }

    void ApplyLabel(Text t, float opacity)
    {
        if (t == null) return;
        Show(t.gameObject, showLabels);
        t.color = WithAlpha(labelColor, opacity);
        int size = Mathf.RoundToInt(labelSize);
        if (t.fontSize != size) t.fontSize = size;
    }

    void ApplyNumber(Text t, float opacity)
    {
        if (t == null) return;
        t.color = WithAlpha(numberColor, opacity);
        int size = Mathf.RoundToInt(numberSize);
        if (t.fontSize != size) t.fontSize = size;
    }

    /// <summary>Pushes the live health number (e.g. 120 -> 0) onto the number text, or onto the health label if none is assigned.</summary>
    public void SetHealthNumber(int value)
    {
        if (lastHealthNumber == value) return;
        lastHealthNumber = value;
        Text t = healthNumberText != null ? healthNumberText : healthLabel;
        if (t != null) t.text = value.ToString();
    }

    /// <summary>Pushes the live ammo number onto the number text, or onto the ammo label if none is assigned.</summary>
    public void SetAmmoNumber(int value)
    {
        if (lastAmmoNumber == value) return;
        lastAmmoNumber = value;
        Text t = ammoNumberText != null ? ammoNumberText : ammoLabel;
        if (t != null) t.text = value.ToString();
    }

    float ReadYaw()
    {
        if (useTestHeading) return testHeading;
        Transform tr = compassTarget;
        if (tr == null)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null) tr = cam.transform;
        }
        if (tr == null) return testHeading;          // nothing to follow: show the test heading
        Vector3 f = tr.forward;
        f.y = 0f;
        if (f.sqrMagnitude > 0.0001f) lastYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;   // keeps the last value when looking straight up / down
        return lastYaw;
    }

    void UpdateCompass()
    {
        if (compassTexts == null || compassTexts.Length < 4) return;
        float heading = Heading;
        float width = (compassArea != null && compassArea.rect.width > 1f) ? compassArea.rect.width : 1300f;
        float visible = Mathf.Max(60f, compassVisibleDegrees);
        float half = visible * 0.5f;
        float pxPerDegree = width / visible;
        float fadeRange = half * 0.35f;
        int size = Mathf.RoundToInt(compassLetterSize);
        for (int i = 0; i < 4; i++)
        {
            Text t = compassTexts[i];
            if (t == null) continue;
            float delta = Mathf.DeltaAngle(heading, i * 90f);          // N = 0, E = 90, S = 180, W = 270
            float k = Mathf.Clamp01((half - Mathf.Abs(delta)) / fadeRange);
            bool on = showCompass && k > 0.001f;
            Show(t.gameObject, on);
            if (!on) continue;
            t.rectTransform.anchoredPosition = new Vector2(delta * pxPerDegree, 0f);
            Color c = compassLettersColor.color;
            c.a = Mathf.Clamp01(compassLettersColor.opacity) * k;
            t.color = c;
            if (t.fontSize != size) t.fontSize = size;
        }
    }

    void ApplyBars()
    {
        ApplyBar(healthFill, healthWipe, shownHealth, healthColor, healthBarOpacity, healthEmptyAngle);
        ApplyBar(ammoFill, ammoWipe, shownAmmo, ammoColor, ammoBarOpacity, ammoEmptyAngle);
        if (healthBack != null) healthBack.color = Dim(healthColor, emptyBarOpacity * healthBarOpacity);
        if (ammoBack != null) ammoBack.color = Dim(ammoColor, emptyBarOpacity * ammoBarOpacity);
    }

    void ApplyStaticLook()
    {
        ApplyLabel(healthLabel, healthLabelOpacity);
        ApplyLabel(ammoLabel, ammoLabelOpacity);
        ApplyNumber(healthNumberText, healthNumberOpacity);
        ApplyNumber(ammoNumberText, ammoNumberOpacity);

        // top visor: with the new layered prefab only the metal is hidden, so the compass can stay on its own
        if (topBodyImage != null)
        {
            Show(topVisor, true);
            Show(topBodyImage.gameObject, showTopVisor);
            if (topEdgeImage != null) Show(topEdgeImage.gameObject, showTopVisor);
        }
        else
        {
            Show(topVisor, showTopVisor);
        }
        if (compassLineImage != null) Show(compassLineImage.gameObject, showCompass);
        if (compassTicksImage != null) Show(compassTicksImage.gameObject, showCompass);
        Show(leftArc, showSideArcs);
        Show(rightArc, showSideArcs);
        Show(leftPanel, showBottomPanels);
        Show(rightPanel, showBottomPanels);
        Show(healthBar, showBars);
        Show(ammoBar, showBars);
        Show(crosshair, showCrosshair && !crosshairHidden);

        Paint(topBodyImage, topVisorMetal);
        Paint(topEdgeImage, topVisorEdgeLine);
        Paint(leftPanelBodyImage, leftPanelMetal);
        Paint(leftPanelEdgeImage, leftPanelEdgeLine);
        Paint(rightPanelBodyImage, rightPanelMetal);
        Paint(rightPanelEdgeImage, rightPanelEdgeLine);
        Paint(leftArcImage, leftArcColor);
        Paint(rightArcImage, rightArcColor);
        Paint(compassLineImage, compassLineColor);
        Paint(compassTicksImage, compassTicksColor);

        if (group != null) group.alpha = hudOpacity;
        if (crosshairImage != null)
        {
            crosshairImage.color = WithAlpha(crosshairColor, crosshairOpacity);
            RectTransform rt = crosshairImage.rectTransform;
            if (Mathf.Abs(rt.sizeDelta.x - crosshairSize) > 0.01f) rt.sizeDelta = new Vector2(crosshairSize, crosshairSize);
        }
    }

    void Apply()
    {
        ApplyBars();
        ApplyStaticLook();
        UpdateCompass();
    }
}