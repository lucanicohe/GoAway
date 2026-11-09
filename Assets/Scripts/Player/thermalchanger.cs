using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ThermalChanger : MonoBehaviour
{
    [Header("Thermal Material")]
    [SerializeField] private Material thermalMaterial;

    [Header("Random Temperature")]
    [SerializeField] private float randomTemperatureAmount = 0.6f;

    [Header("Monster")]
    [SerializeField] private GameObject monster;
    [SerializeField] private float monsterTemperature = 32f;

    [Header("Gun")]
    [SerializeField] private GameObject gun;
    [SerializeField] private float gunTemperature = 32f;

    [Header("Hand")]
    [SerializeField] private GameObject hand;
    [SerializeField] private float handTemperature = 32f;

    private bool thermalMode = false;

    private readonly Dictionary<Renderer, Material[]> originalMaterials = new();
    private readonly List<Material> thermalInstances = new();

    // Shader Graph Reference property
    private static readonly int TempProperty = Shader.PropertyToID("_temp");

    private void Start()
    {
        thermalMode = false;
        Debug.Log("NORMAL MODE");
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            (Keyboard.current.digit1Key.wasPressedThisFrame ||
             Keyboard.current.numpad1Key.wasPressedThisFrame))
        {
            if (thermalMode)
                ApplyNormalMode();
            else
                ApplyThermalMode();
        }
    }

    private void ApplyThermalMode()
    {
        if (thermalMaterial == null)
        {
            Debug.LogError("Thermal Material is not assigned!");
            return;
        }

        thermalMode = true;

        Renderer[] renderers = FindObjectsByType<Renderer>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        originalMaterials.Clear();
        thermalInstances.Clear();

        foreach (Renderer rend in renderers)
        {
            if (rend == null)
                continue;

            Material[] original = rend.sharedMaterials;
            originalMaterials[rend] = original;

            float temperature;

            if (IsInside(rend, monster))
                temperature = monsterTemperature;
            else if (IsInside(rend, gun))
                temperature = gunTemperature;
            else if (IsInside(rend, hand))
                temperature = handTemperature;
            else
                temperature = Random.Range(0f, randomTemperatureAmount);

            Material[] newMaterials = new Material[original.Length];

            for (int i = 0; i < original.Length; i++)
            {
                Material instance = new Material(thermalMaterial);

                if (instance.HasProperty(TempProperty))
                {
                    instance.SetFloat(TempProperty, temperature);
                }
                else
                {
                    Debug.LogError(
                        $"Thermal material missing '_temp' property: {rend.name}"
                    );
                }

                newMaterials[i] = instance;
                thermalInstances.Add(instance);
            }

            rend.sharedMaterials = newMaterials;
        }

        Debug.Log($"THERMAL MODE: {originalMaterials.Count} renderers updated.");
    }

    private bool IsInside(Renderer rend, GameObject target)
    {
        return target != null &&
               (rend.transform == target.transform ||
                rend.transform.IsChildOf(target.transform));
    }

    private void ApplyNormalMode()
    {
        thermalMode = false;

        foreach (var entry in originalMaterials)
        {
            if (entry.Key != null)
                entry.Key.sharedMaterials = entry.Value;
        }

        foreach (Material instance in thermalInstances)
        {
            if (instance != null)
                Destroy(instance);
        }

        originalMaterials.Clear();
        thermalInstances.Clear();

        Debug.Log("NORMAL MODE: Original materials restored.");
    }

    private void OnDestroy()
    {
        if (thermalMode)
            ApplyNormalMode();
    }
}