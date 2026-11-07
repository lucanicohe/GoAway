using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Reflection;

public class ThermalChanger : MonoBehaviour
{
    [Header("Cameras")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Camera thermalCamera;

    [Header("Monster Materials")]
    [SerializeField] private Renderer monsterRenderer;
    [SerializeField] private Material thermalMaterial;
    [SerializeField] private Material normalMaterial;

    private bool thermalMode = true;

    private float originalFogDensity;
    private float originalReflectionIntensity;

    private class PostProcessState
    {
        public Behaviour component;
        public bool originalEnabled;
        public PropertyInfo property;
        public bool originalPropertyValue;
    }

    private readonly List<PostProcessState> postProcessStates =
        new List<PostProcessState>();

    private void Start()
    {
        // Save original environment values
        originalFogDensity = RenderSettings.fogDensity;
        originalReflectionIntensity = RenderSettings.reflectionIntensity;

        // Find main camera post-processing automatically
        Behaviour[] components = mainCamera.GetComponents<Behaviour>();

        foreach (Behaviour component in components)
        {
            if (component == null)
                continue;

            string typeName = component.GetType().Name;

            PostProcessState state = new PostProcessState();
            state.component = component;
            state.originalEnabled = component.enabled;

            // Built-in Post Processing
            if (typeName == "PostProcessLayer")
            {
                postProcessStates.Add(state);
                continue;
            }

            // URP / HDRP camera post-processing
            PropertyInfo property = component.GetType().GetProperty(
                "renderPostProcessing",
                BindingFlags.Public | BindingFlags.Instance
            );

            if (property != null &&
                property.PropertyType == typeof(bool) &&
                property.CanRead &&
                property.CanWrite)
            {
                state.property = property;
                state.originalPropertyValue =
                    (bool)property.GetValue(component);

                postProcessStates.Add(state);
            }
        }

        // START IN THERMAL MODE
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
        // Monster thermal material
        monsterRenderer.sharedMaterial = thermalMaterial;

        // Fog ON
        RenderSettings.fog = true;
        RenderSettings.fogDensity = originalFogDensity;

        // Post Processing ON
        foreach (PostProcessState state in postProcessStates)
        {
            if (state.component == null)
                continue;

            state.component.enabled = state.originalEnabled;

            if (state.property != null)
                state.property.SetValue(
                    state.component,
                    state.originalPropertyValue
                );
        }

        // Full environment reflections
        RenderSettings.reflectionIntensity =
            originalReflectionIntensity;

        // Thermal camera ON
        thermalCamera.gameObject.SetActive(true);

        Debug.Log("THERMAL MODE ACTIVATED");
    }

    private void ApplyNormalMode()
    {
        // Monster normal material
        monsterRenderer.sharedMaterial = normalMaterial;

        // Fog OFF
        RenderSettings.fog = false;

        // Post Processing OFF
        foreach (PostProcessState state in postProcessStates)
        {
            if (state.component == null)
                continue;

            if (state.property != null)
                state.property.SetValue(state.component, false);
            else
                state.component.enabled = false;
        }

        // Reflections OFF
        RenderSettings.reflectionIntensity = 0f;

        // Thermal camera OFF
        thermalCamera.gameObject.SetActive(false);

        Debug.Log("NORMAL MODE ACTIVATED");
    }
}