using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float jumpForce = 50f;
    public float gravityMultiplier = 5f;
    public float groundCheckRadius = 0.55f;
    public LayerMask groundLayer;

    private Rigidbody2D rb;
    private bool isGrounded;
    private bool jumpRequested = false;
    private NetworkManager networkManager;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        groundLayer = LayerMask.GetMask("Ground");
        networkManager = FindObjectOfType<NetworkManager>(); // Find and store a reference to the NetworkManager
    }

    void Update()
    {
        HandleInput();

        // Check for jump input, set to true for next FixedUpdate
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            jumpRequested = true;
        }
    }

    void FixedUpdate()
    {
        CheckGround();
        // Comment out Move and ApplyGravity for now, as they will be handled server-side
        // Move();
        // ApplyGravity();

        // ClientPlayerUpdate
        // NetworkManager.Instance.SendClientPlayerUpdate(transform.position);

        // ClientPlayerInput
        float horizontalInput = Input.GetAxis("Horizontal");
        NetworkManager.Instance.SendClientPlayerInput(horizontalInput, 0f, jumpRequested);
        // Reset jumpRequested flag
        if (jumpRequested)
        {
            jumpRequested = false;
        }
    }

    void HandleInput()
    {
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            // Temporarily apply jump for immediate responsiveness
            Jump();
        }
    }

    void CheckGround()
    {
        isGrounded = Physics2D.OverlapCircle(transform.position, groundCheckRadius, groundLayer);
    }

    void Move()
    {
        // This method can be modified or removed based on server-side implementation
        float horizontalInput = Input.GetAxis("Horizontal");
        Vector2 movement = new Vector2(horizontalInput, 0f);
        rb.velocity = new Vector2(movement.x * moveSpeed, rb.velocity.y);
    }

    void Jump()
    {
        // Temporarily apply jump force
        rb.velocity = new Vector2(rb.velocity.x, jumpForce);
    }

    void ApplyGravity()
    {
        // This method can be modified or removed based on server-side implementation
        rb.velocity -= Vector2.down * Physics2D.gravity.y * gravityMultiplier * Time.fixedDeltaTime;
    }
}
