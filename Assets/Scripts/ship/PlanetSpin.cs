using UnityEngine;

public class PlanetSpin : MonoBehaviour
{
    public float speed = 10f; // degrees per second

    void Update()
    {
        transform.Rotate(0f, -speed * Time.deltaTime, 0f);
    }
}