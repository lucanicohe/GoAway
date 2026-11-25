using UnityEngine;

public class IntensityFlicker : MonoBehaviour
{
    [SerializeField] private float minIntensity = 1f;
    [SerializeField] private float maxIntensity = 4f;
    [SerializeField] private float flickerSpeed = 0.2f;

    private Light pointLight;
    private float timer;

    void Start()
    {
        pointLight = GetComponent<Light>();
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= flickerSpeed)
        {
            pointLight.intensity = Random.Range(minIntensity, maxIntensity);
            timer = 0f;
        }
    }
}