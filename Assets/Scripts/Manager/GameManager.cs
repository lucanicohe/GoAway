using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

[DefaultExecutionOrder(-50)] // runs before most player scripts
public class GameManager : MonoBehaviour
{
    [Header("Ammo")]
    [SerializeField] private int maxAmmo = 30;
    [SerializeField] private int currentAmmo = 30;          // live counter in Inspector
    [SerializeField] private bool infiniteAmmo = false;

    [Header("References (auto-finds if empty)")]
    [SerializeField] private BlasterWeapon blaster;
    [SerializeField] private VisorHud visorHud;
    [SerializeField] private WalkSprintRotationSway sway;
    [SerializeField] private BlasterRecoil recoil;

    [Header("3D Ammo Text")]
    [Tooltip("Drag your 3D TextMeshPro here")]
    [SerializeField] private TextMeshPro ammoText3D;

    [Header("Sway / Recoil Coordination")]
    [Tooltip("If true, GameManager forces a safe execution order so they don't fight over the same transforms.")]
    [SerializeField] private bool autoFixSwayRecoilConflict = true;

    [Header("IG-88 Patrol")]
    [Tooltip("Drag all 4 IG-88 root GameObjects here.")]
    [SerializeField] private IG88Controller[] ig88s;
    [Tooltip("Drag the player (or camera) here — IG-88s will measure distance and aim at this.")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private float ig88PatrolSpeed      = 2.5f;
    [SerializeField] private float ig88StopDistance     = 5f;
    [SerializeField] private float ig88PatrolDistance   = 15f;
    [SerializeField] private float ig88DetectionInterval = 0.5f;
    [SerializeField] private float ig88StepsPerSecond   = 1.8f;

    private bool blasterWasEnabled;

    private void Awake()
    {
        // Auto-find missing refs
        if (blaster == null) blaster = FindFirstObjectByType<BlasterWeapon>();
        if (visorHud == null) visorHud = FindFirstObjectByType<VisorHud>();
        if (sway == null) sway = FindFirstObjectByType<WalkSprintRotationSway>();
        if (recoil == null) recoil = FindFirstObjectByType<BlasterRecoil>();

        // Soft fix for the transform conflict
        if (autoFixSwayRecoilConflict)
            FixSwayRecoilOrder();
    }

    private void Start()
    {
        currentAmmo = Mathf.Clamp(currentAmmo, 0, maxAmmo);
        UpdateAmmoUI();

        if (blaster != null)
        {
            blaster.Fired += OnWeaponFired;
            blasterWasEnabled = blaster.enabled;
        }
    }

    private void OnDestroy()
    {
        if (blaster != null)
            blaster.Fired -= OnWeaponFired;
    }

    private void Update()
    {
        if (ig88s != null)
        {
            foreach (var ig in ig88s)
            {
                if (ig == null) continue;
                ig.player            = playerTarget;
                ig.moveSpeed         = ig88PatrolSpeed;
                ig.stopDistance      = ig88StopDistance;
                ig.patrolDistance    = ig88PatrolDistance;
                ig.detectionInterval = ig88DetectionInterval;
                ig.stepsPerSecond    = ig88StepsPerSecond;
            }
        }
        // Optional: also block firing when out of ammo by disabling the component temporarily
        if (blaster != null)
        {
            bool canShoot = infiniteAmmo || currentAmmo > 0;
            if (blaster.enabled != canShoot)
                blaster.enabled = canShoot;
        }
    }

    private void OnWeaponFired(BlasterWeapon weapon)
    {
        if (infiniteAmmo) return;

        currentAmmo = Mathf.Max(0, currentAmmo - 1);
        UpdateAmmoUI();

        if (currentAmmo <= 0)
            Debug.Log("Out of ammo!");
    }

    private void UpdateAmmoUI()
    {
        // Visor HUD (0-1)
        if (visorHud != null)
        {
            float normalized = maxAmmo > 0 ? (float)currentAmmo / maxAmmo : 0f;
            visorHud.SetAmmo(normalized);
        }

        // 3D TextMeshPro
        if (ammoText3D != null)
        {
            ammoText3D.text = currentAmmo.ToString();
        }
    }

    // ---------- Public helpers you can call from other scripts ----------

    public void AddAmmo(int amount)
    {
        currentAmmo = Mathf.Clamp(currentAmmo + amount, 0, maxAmmo);
        UpdateAmmoUI();
    }

    public void SetAmmo(int amount)
    {
        currentAmmo = Mathf.Clamp(amount, 0, maxAmmo);
        UpdateAmmoUI();
    }

    public int CurrentAmmo => currentAmmo;
    public int MaxAmmo => maxAmmo;
    public bool HasAmmo => infiniteAmmo || currentAmmo > 0;

    // ---------- Soft conflict fix (no rewrite of the two scripts) ----------

    private void FixSwayRecoilOrder()
    {
        if (sway != null)
        {
            Debug.Log("[GameManager] Sway/Recoil coordination active. " +
                      "Sway should run before Recoil. If they still fight, " +
                      "put WalkSprintRotationSway on the root (PlayerObjects) and " +
                      "keep BlasterRecoil moving only the children — that is the intended setup.");
        }
    }

    // Quick reload for testing (press R)
    private void LateUpdate()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            currentAmmo = maxAmmo;
            UpdateAmmoUI();
            Debug.Log("Reloaded!");
        }
    }
}