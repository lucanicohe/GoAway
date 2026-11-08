using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class ThermalChanger : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Camera thermalCamera;

    [Header("Post Processing Volumes")]
    [SerializeField] private Volume normalVolume;
    [SerializeField] private Volume thermalVolume;

    [Header("Monster Materials")]
    [SerializeField] private Renderer monsterRenderer;
    [SerializeField] private Material thermalMaterial;
    [SerializeField] private Material normalMaterial;

    private bool thermalMode = true;

    private float originalFogDensity;
    private float originalReflectionIntensity;

    private void Start()
    {
        originalFogDensity = RenderSettings.fogDensity;
        originalReflectionIntensity = RenderSettings.reflectionIntensity;

        // Start in thermal mode
        ApplyThermalMode();
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            (Keyboard.current.digit1Key.wasPressedThisFrame ||
             Keyboard.current.numpad1Key.wasPressedThisFrame))
        {
            thermalMode = !thermalMode;

            if (thermalMode)
                ApplyThermalMode();
            else
                ApplyNormalMode();
        }
    }

    private void ApplyThermalMode()
    {
        // Monster material
        monsterRenderer.sharedMaterial = thermalMaterial;

        // Post processing
        thermalVolume.enabled = true;
        normalVolume.enabled = false;

        // Fog ON
        RenderSettings.fog = true;
        RenderSettings.fogDensity = originalFogDensity;

        // Full reflections
        RenderSettings.reflectionIntensity = originalReflectionIntensity;

        // Thermal camera ON
        thermalCamera.gameObject.SetActive(true);

        Debug.Log("THERMAL MODE");
    }

    private void ApplyNormalMode()
    {
        // Monster material
        monsterRenderer.sharedMaterial = normalMaterial;

        // Post processing
        normalVolume.enabled = true;
        thermalVolume.enabled = false;

        // Fog OFF
        RenderSettings.fog = false;

        // Reflections OFF
        RenderSettings.reflectionIntensity = 0f;

        // Thermal camera OFF
        thermalCamera.gameObject.SetActive(false);

        Debug.Log("NORMAL MODE");
    }
}