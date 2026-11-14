#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace FPCam
{
    /// <summary>Feeds WASD / arrows / gamepad stick, Shift, Space (and G = test shake) from the NEW Input System into the motor.</summary>
    [RequireComponent(typeof(FirstPersonMotor))]
    public class FPInputSystemProvider : MonoBehaviour
    {
        FirstPersonMotor motor;
        InputAction move, jump, sprint, shake;

        void Awake()
        {
            motor = GetComponent<FirstPersonMotor>();

            move = new InputAction("Move", InputActionType.Value, null, null, null, "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            move.AddBinding("<Gamepad>/leftStick");

            jump = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            jump.AddBinding("<Gamepad>/buttonSouth");

            sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            sprint.AddBinding("<Gamepad>/leftStickPress");

            shake = new InputAction("Shake", InputActionType.Button, "<Keyboard>/g");
        }

        void OnEnable() { move.Enable(); jump.Enable(); sprint.Enable(); shake.Enable(); }
        void OnDisable() { move.Disable(); jump.Disable(); sprint.Disable(); shake.Disable(); }

        void OnDestroy()
        {
            if (move != null) move.Dispose();
            if (jump != null) jump.Dispose();
            if (sprint != null) sprint.Dispose();
            if (shake != null) shake.Dispose();
        }

        void Update()
        {
            motor.moveInput = move.ReadValue<Vector2>();
            motor.sprintHeld = sprint.ReadValue<float>() > 0.5f;
            if (jump.triggered) motor.jumpRequested = true;
            if (shake.triggered && FirstPersonShake.Instance != null) FirstPersonShake.Instance.Shake(1f);
        }
    }
}
#endif
