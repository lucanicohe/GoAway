using UnityEngine;

public class spacerod : MonoBehaviour
{
    [Header("Speed")]
    public float speed = 8f;

    [Header("Skybox")]
    public bool rotateSkybox = true;
    public string skyboxRotationProperty = "_RotationX";
    public float skyboxMultiplier = 1f;

    [Header("First Object")]
    public Transform objectToRotate;
    public Vector3 firstRotationAxis = new Vector3(1, 0, 0);
    public float firstRotationMultiplier = 1f;

    [Header("Second Object")]
    public Transform secondObjectToRotate;
    public Vector3 secondRotationAxis = new Vector3(1, 0, 0);
    public float secondRotationMultiplier = 1f;

    private float rotation;

    private Quaternion startRotation1;
    private Quaternion startRotation2;

    void Start()
    {
        if (objectToRotate != null)
            startRotation1 = objectToRotate.localRotation;

        if (secondObjectToRotate != null)
            startRotation2 = secondObjectToRotate.localRotation;
    }

    void Update()
    {
        rotation += speed * Time.deltaTime;

        if (rotateSkybox && RenderSettings.skybox != null)
        {
            float current = RenderSettings.skybox.GetFloat(skyboxRotationProperty);
            RenderSettings.skybox.SetFloat(
                skyboxRotationProperty,
                current + speed * skyboxMultiplier * Time.deltaTime
            );
        }

        if (objectToRotate != null)
        {
            objectToRotate.localRotation =
                startRotation1 *
                Quaternion.AngleAxis(rotation * firstRotationMultiplier, firstRotationAxis.normalized);
        }

        if (secondObjectToRotate != null)
        {
            secondObjectToRotate.localRotation =
                startRotation2 *
                Quaternion.AngleAxis(rotation * secondRotationMultiplier, secondRotationAxis.normalized);
        }
    }
}