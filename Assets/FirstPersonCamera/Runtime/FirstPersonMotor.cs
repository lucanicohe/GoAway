using UnityEngine;

namespace FPCam
{
    /// <summary>
    /// Physics-based first-person movement. All movement happens in FixedUpdate on a Rigidbody with Interpolation ON,
    /// so the player's position is smoothed for rendering. The camera (Cinemachine) only READS the player's "Head" position
    /// and handles mouse look itself - which is what removes the jitter.
    /// Input is supplied by an input provider component (FPInputSystemProvider or FPLegacyInputProvider).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class FirstPersonMotor : MonoBehaviour
    {
        [Header("Camera")]
        [Tooltip("The Cinemachine camera (or any transform) whose horizontal direction defines 'forward'. Set automatically.")]
        public Transform lookReference;

        [Header("Movement")]
        public float walkSpeed = 4.5f;
        public float sprintSpeed = 7.5f;
        public float groundAcceleration = 60f;
        public float airAcceleration = 12f;
        public float jumpHeight = 1.1f;

        [Header("Ground check")]
        public float groundCheckExtra = 0.15f;

        // filled in by the input provider every frame
        [HideInInspector] public Vector2 moveInput;
        [HideInInspector] public bool sprintHeld;
        [HideInInspector] public bool jumpRequested;

        Rigidbody rb;
        CapsuleCollider col;
        public bool IsGrounded { get; private set; }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<CapsuleCollider>();
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;   // the key setting for smooth rendering of a physics body
        }

        Vector3 GetVelocity()
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        void SetVelocity(Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        bool CheckGround()
        {
            Vector3 origin = transform.TransformPoint(col.center);
            float dist = col.height * 0.5f * transform.lossyScale.y + groundCheckExtra;
            return Physics.Raycast(origin, Vector3.down, dist, ~0, QueryTriggerInteraction.Ignore);
        }

        void FixedUpdate()
        {
            Transform r = lookReference;
            if (r == null && UnityEngine.Camera.main != null) r = UnityEngine.Camera.main.transform;
            Quaternion yaw = r != null ? Quaternion.Euler(0f, r.eulerAngles.y, 0f) : transform.rotation;

            Vector2 inp = Vector2.ClampMagnitude(moveInput, 1f);
            Vector3 wish = yaw * new Vector3(inp.x, 0f, inp.y) * (sprintHeld ? sprintSpeed : walkSpeed);

            IsGrounded = CheckGround();
            Vector3 v = GetVelocity();
            Vector3 h = new Vector3(v.x, 0f, v.z);
            float acc = IsGrounded ? groundAcceleration : airAcceleration;
            h = Vector3.MoveTowards(h, wish, acc * Time.fixedDeltaTime);
            v.x = h.x;
            v.z = h.z;

            if (jumpRequested && IsGrounded)
                v.y = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
            jumpRequested = false;

            SetVelocity(v);
        }
    }
}
