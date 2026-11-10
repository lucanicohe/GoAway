using UnityEngine;
using UnityEngine.Animations.Rigging;

public class ProceduralWalking : MonoBehaviour
{
    [Header("Walking")]
    [SerializeField] private bool walk = false;

    [SerializeField] private float walkingSpeed = 2f;
    [SerializeField] private float stepLength = 0.35f;
    [SerializeField] private float stepHeight = 0.2f;

    [Header("Left Leg")]
    [SerializeField] private Transform leftFootTarget;
    [SerializeField] private TwoBoneIKConstraint leftLegIK;

    [Header("Right Leg")]
    [SerializeField] private Transform rightFootTarget;
    [SerializeField] private TwoBoneIKConstraint rightLegIK;

    private Vector3 leftStartPosition;
    private Vector3 rightStartPosition;

    private float timer;

    private void Start()
    {
        if (leftFootTarget != null)
            leftStartPosition = leftFootTarget.localPosition;

        if (rightFootTarget != null)
            rightStartPosition = rightFootTarget.localPosition;
    }

    private void Update()
    {
        if (leftFootTarget == null || rightFootTarget == null)
            return;

        // Enable or disable IK
        float targetWeight = walk ? 1f : 0f;

        if (leftLegIK != null)
            leftLegIK.weight = Mathf.Lerp(
                leftLegIK.weight,
                targetWeight,
                Time.deltaTime * 5f
            );

        if (rightLegIK != null)
            rightLegIK.weight = Mathf.Lerp(
                rightLegIK.weight,
                targetWeight,
                Time.deltaTime * 5f
            );

        if (!walk)
        {
            leftFootTarget.localPosition = Vector3.Lerp(
                leftFootTarget.localPosition,
                leftStartPosition,
                Time.deltaTime * 5f
            );

            rightFootTarget.localPosition = Vector3.Lerp(
                rightFootTarget.localPosition,
                rightStartPosition,
                Time.deltaTime * 5f
            );

            return;
        }

        timer += Time.deltaTime * walkingSpeed * Mathf.PI * 2f;

        // Left leg
        UpdateFoot(
            leftFootTarget,
            leftStartPosition,
            Mathf.Sin(timer)
        );

        // Right leg: opposite phase
        UpdateFoot(
            rightFootTarget,
            rightStartPosition,
            Mathf.Sin(timer + Mathf.PI)
        );
    }

    private void UpdateFoot(
        Transform foot,
        Vector3 startPosition,
        float phase
    )
    {
        Vector3 position = startPosition;

        // Move forward and backward
        position.z += Mathf.Cos(
            Mathf.Asin(Mathf.Clamp(phase, -1f, 1f))
        ) * stepLength * 0.5f;

        // Lift foot during swing
        position.y += Mathf.Max(0f, phase) * stepHeight;

        foot.localPosition = position;
    }
}