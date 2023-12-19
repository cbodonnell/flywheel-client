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
    }

    void FixedUpdate()
    {
        CheckGround();
        // Comment out Move and ApplyGravity for now, as they will be handled server-side
        // Move();
        // ApplyGravity();
        SendInputToServer();
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

    void SendInputToServer()
    {
        // Capture and send input data to the server
        float horizontalInput = Input.GetAxis("Horizontal");
        bool jumpInput = Input.GetButtonDown("Jump");
        networkManager.SendPlayerInput(horizontalInput, 0f, jumpInput); // Vertical input is 0 for 2D
    }
}
