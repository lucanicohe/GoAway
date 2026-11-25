using UnityEngine;

public class PlanetSpin : MonoBehaviour
{
    [Header("Rotation Speed (degrees per second)")]
    public float speedX = 0f;
    public float speedY = 10f;
    public float speedZ = 0f;

    void Update()
    {
        transform.Rotate(speedX * Time.deltaTime, speedY * Time.deltaTime, speedZ * Time.deltaTime, Space.Self);
    }
}