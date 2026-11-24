using UnityEngine;

namespace FPCam
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonMotor))]
    public class FirstPersonFootsteps : MonoBehaviour
    {
        [Header("Audio")]
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private AudioClip[] footstepClips;

        [Range(0f, 1f)]
        [SerializeField] private float walkVolume = 0.5f;

        [Range(0f, 1f)]
        [SerializeField] private float sprintVolume = 1f;

        [Header("Footstep Timing")]
        [SerializeField] private float walkInterval = 0.5f;
        [SerializeField] private float sprintInterval = 0.3f;
        [SerializeField] private float minSpeed = 0.5f;

        [Header("Variation")]
        [SerializeField] private float minPitch = 0.95f;
        [SerializeField] private float maxPitch = 1.05f;

        private FirstPersonMotor motor;
        private Rigidbody rb;
        private float stepProgress;

        void Awake()
        {
            motor = GetComponent<FirstPersonMotor>();
            rb = GetComponent<Rigidbody>();
        }

        void Update()
        {
#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = rb.linearVelocity;
#else
            Vector3 velocity = rb.velocity;
#endif

            velocity.y = 0f;

            bool isMoving =
                motor.moveInput.sqrMagnitude > 0.01f &&
                velocity.magnitude > minSpeed;

            if (!motor.IsGrounded || !isMoving)
            {
                stepProgress = 0f;
                return;
            }

            float interval = motor.sprintHeld
                ? sprintInterval
                : walkInterval;

            stepProgress += Time.deltaTime /
                Mathf.Max(interval, 0.01f);

            if (stepProgress >= 1f)
            {
                PlayFootstep();
                stepProgress -= 1f;
            }
        }

        void PlayFootstep()
        {
            if (footstepSource == null ||
                footstepClips == null ||
                footstepClips.Length == 0)
                return;

            footstepSource.pitch =
                Random.Range(minPitch, maxPitch);

            AudioClip clip = footstepClips[
                Random.Range(0, footstepClips.Length)
            ];

            float volume = motor.sprintHeld ? sprintVolume : walkVolume;

            footstepSource.PlayOneShot(clip, volume);
        }
    }
}