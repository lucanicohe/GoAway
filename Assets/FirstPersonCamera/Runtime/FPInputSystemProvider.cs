
#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPCam
{
    /// <summary>
    /// Handles movement, jumping, sprinting, camera shake,
    /// and gun firing using Unity's New Input System.
    /// </summary>
    [RequireComponent(typeof(FirstPersonMotor))]
    public class FPInputSystemProvider : MonoBehaviour
    {
        [Header("Gun")]
        [SerializeField] private MuzzleFlash muzzleFlash;
        [SerializeField] private float fireInterval = 0.15f;
        [SerializeField] private bool automaticFire = false;

        private FirstPersonMotor motor;

        private InputAction move;
        private InputAction jump;
        private InputAction sprint;
        private InputAction shake;
        private InputAction fire;

        private float nextFireTime;

        void Awake()
        {
            motor = GetComponent<FirstPersonMotor>();

            // Movement
            move = new InputAction(
                "Move",
                InputActionType.Value,
                null, null, null, "Vector2"
            );

            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            move.AddBinding("<Gamepad>/leftStick");

            // Jump
            jump = new InputAction(
                "Jump",
                InputActionType.Button,
                "<Keyboard>/space"
            );
            jump.AddBinding("<Gamepad>/buttonSouth");

            // Sprint
            sprint = new InputAction(
                "Sprint",
                InputActionType.Button,
                "<Keyboard>/leftShift"
            );
            sprint.AddBinding("<Gamepad>/leftStickPress");

            // Camera shake
            shake = new InputAction(
                "Shake",
                InputActionType.Button,
                "<Keyboard>/g"
            );

            // Fire
            fire = new InputAction(
                "Fire",
                InputActionType.Button,
                "<Mouse>/leftButton"
            );
            fire.AddBinding("<Gamepad>/rightTrigger");
        }

        void OnEnable()
        {
            move?.Enable();
            jump?.Enable();
            sprint?.Enable();
            shake?.Enable();
            fire?.Enable();
        }

        void OnDisable()
        {
            move?.Disable();
            jump?.Disable();
            sprint?.Disable();
            shake?.Disable();
            fire?.Disable();
        }

        void OnDestroy()
        {
            move?.Dispose();
            jump?.Dispose();
            sprint?.Dispose();
            shake?.Dispose();
            fire?.Dispose();
        }

        void Update()
        {
            // Movement
            motor.moveInput = move.ReadValue<Vector2>();

            // Sprint
            motor.sprintHeld = sprint.ReadValue<float>() > 0.5f;

            // Jump
            if (jump.WasPressedThisFrame())
                motor.jumpRequested = true;

            // Camera shake
            if (shake.WasPressedThisFrame() &&
                FirstPersonShake.Instance != null)
            {
                FirstPersonShake.Instance.Shake(1f);
            }

            // Gun firing
            HandleFiring();
        }

        private void HandleFiring()
        {
            if (muzzleFlash == null)
                return;

            bool wantsToFire = automaticFire
                ? fire.IsPressed()
                : fire.WasPressedThisFrame();

            if (wantsToFire && Time.time >= nextFireTime)
            {
                nextFireTime = Time.time +
                    Mathf.Max(0.01f, fireInterval);

                muzzleFlash.Fire();
            }
        }
    }
}
#endif
