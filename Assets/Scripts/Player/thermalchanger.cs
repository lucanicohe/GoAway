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

    [Header("Lamp")]
    [SerializeField] private GameObject lamp;
    [SerializeField] private float lampTemperature = 32f;

    [Header("Room")]
    [SerializeField] private GameObject room;
    [SerializeField] private float roomTemperature = 32f;

    [Header("Lamp Room2")]
    [SerializeField] private GameObject lampRoom2;
    [SerializeField] private float lampRoom2Temperature = 32f;

    [Header("Lamp Hall")]
    [SerializeField] private GameObject lampHall;
    [SerializeField] private float lampHallTemperature = 32f;

    [Header("Pipes")]
    [SerializeField] private GameObject pipes;
    [SerializeField] private float pipesTemperature = 32f;

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

            float temperature = GetTemperature(rend);

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

    private float GetTemperature(Renderer rend)
    {
        if (IsInside(rend, monster))
            return monsterTemperature;

        if (IsInside(rend, gun))
            return gunTemperature;

        if (IsInside(rend, hand))
            return handTemperature;

        if (IsInside(rend, lamp))
            return lampTemperature;

        if (IsInside(rend, room))
            return roomTemperature;

        if (IsInside(rend, lampRoom2))
            return lampRoom2Temperature;

        if (IsInside(rend, lampHall))
            return lampHallTemperature;

        if (IsInside(rend, pipes))
            return pipesTemperature;

        return Random.Range(0f, randomTemperatureAmount);
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