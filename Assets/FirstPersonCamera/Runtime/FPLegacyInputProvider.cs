#if ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine;

namespace FPCam
{
    /// <summary>Feeds WASD / Shift / Space (and G = test shake) from the OLD Input Manager into the motor.</summary>
    [RequireComponent(typeof(FirstPersonMotor))]
    public class FPLegacyInputProvider : MonoBehaviour
    {
        FirstPersonMotor motor;

        void Awake() { motor = GetComponent<FirstPersonMotor>(); }

        void Update()
        {
            motor.moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            motor.sprintHeld = Input.GetKey(KeyCode.LeftShift);
            if (Input.GetKeyDown(KeyCode.Space)) motor.jumpRequested = true;
            if (Input.GetKeyDown(KeyCode.G) && FirstPersonShake.Instance != null) FirstPersonShake.Instance.Shake(1f);
        }
    }
}
#endif
