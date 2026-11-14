
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class MovementController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float playerSpeed = 3f;
    [SerializeField] private float Gravity = -9.81f;
    [SerializeField] private float JumpHeight = 1.5f;

    [Header("Gun")]
    [SerializeField] private MuzzleFlash muzzleFlash;
    [SerializeField] private float fireRate = 0.15f;
    [SerializeField] private bool automaticFire = false;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private float nextFireTime;

    public float PlayerSpeed
    {
        get => playerSpeed;
        set => playerSpeed = value;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Update()
    {
        // Ground check
        isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        // Movement input
        Vector2 moveInput = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed ||
                Keyboard.current.upArrowKey.isPressed)
                moveInput.y += 1f;

            if (Keyboard.current.sKey.isPressed ||
                Keyboard.current.downArrowKey.isPressed)
                moveInput.y -= 1f;

            if (Keyboard.current.aKey.isPressed ||
                Keyboard.current.leftArrowKey.isPressed)
                moveInput.x -= 1f;

            if (Keyboard.current.dKey.isPressed ||
                Keyboard.current.rightArrowKey.isPressed)
                moveInput.x += 1f;
        }

        Vector3 move =
            transform.right * moveInput.x +
            transform.forward * moveInput.y;

        if (move.magnitude > 1f)
            move.Normalize();

        controller.Move(move * playerSpeed * Time.deltaTime);

        // Gravity
        velocity.y += Gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // Jump
        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame &&
            isGrounded)
        {
            velocity.y = Mathf.Sqrt(
                JumpHeight * -2f * Gravity
            );
        }

        // Firing
        HandleFiring();
    }

    private void HandleFiring()
    {
        if (Mouse.current == null || muzzleFlash == null)
            return;

        bool fireInput = automaticFire
            ? Mouse.current.leftButton.isPressed
            : Mouse.current.leftButton.wasPressedThisFrame;

        if (fireInput && Time.time >= nextFireTime)
        {
            Fire();
        }
    }

    private void Fire()
    {
        nextFireTime = Time.time + fireRate;

        // Trigger muzzle flash
        muzzleFlash.Fire();

        // Future additions:
        // Raycast shooting
        // Damage
        // Gunshot sound
        // Recoil
        // Ammo consumption
    }
}
