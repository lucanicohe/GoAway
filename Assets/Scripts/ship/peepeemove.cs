using UnityEngine;

public class peepeemove : MonoBehaviour
{
    [SerializeField] private float startX = -21.2f;
    [SerializeField] private float endX = -27.8f;
    [SerializeField] private float duration = 3f;

    private float timer;
    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
        startPosition.x = startX;
        transform.position = startPosition;
    }

    void Update()
    {
        timer += Time.deltaTime;

        float t = Mathf.Clamp01(timer / duration);
        float easedT = 1f - Mathf.Pow(1f - t, 3f);

        Vector3 position = transform.position;
        position.x = Mathf.Lerp(startX, endX, easedT);

        transform.position = position;
    }
}