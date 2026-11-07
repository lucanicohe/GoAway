using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class MovementController : MonoBehaviour
{
    [SerializeField] private float playerSpeed = 3f;
    [SerializeField] private float Gravity = -9.81f;
    [SerializeField] private float JumpHeight = 1.5f;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;

    public float PlayerSpeed
    {
        get => playerSpeed;
        set => playerSpeed = value; // still works for PixelationController
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        // New Input System – keyboard
        Vector2 moveInput = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)   moveInput.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveInput.x += 1f;
        }

        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (move.magnitude > 1f) move.Normalize();

        controller.Move(move * playerSpeed * Time.deltaTime);

        // Gravity
        velocity.y += Gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Jump
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded)
            velocity.y = Mathf.Sqrt(JumpHeight * -2f * Gravity);
    }
}