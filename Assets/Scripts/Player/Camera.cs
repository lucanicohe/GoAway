
using UnityEngine;

public class Camera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float positionSmoothTime = 0.15f;
    [SerializeField] private float rotationSpeed = 10f;

    private Vector3 relativePosition;
    private Quaternion relativeRotation;
    private Vector3 velocity;

    void Start()
    {
        if (target == null) return;

        // Save the camera's initial position and rotation
        // relative to the player.
        relativePosition = target.InverseTransformPoint(transform.position);
        relativeRotation = Quaternion.Inverse(target.rotation) * transform.rotation;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Maintain the original relative position.
        Vector3 desiredPosition = target.TransformPoint(relativePosition);

        // Maintain the original relative rotation.
        Quaternion desiredRotation = target.rotation * relativeRotation;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref velocity,
            positionSmoothTime
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        velocity = Vector3.zero;

        if (target == null) return;

        relativePosition = target.InverseTransformPoint(transform.position);
        relativeRotation = Quaternion.Inverse(target.rotation) * transform.rotation;
    }
}
