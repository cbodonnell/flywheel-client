using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
	public float moveSpeed = 10f;
	public float jumpForce = 50f;
	public float gravityMultiplier = 5f;
	public float groundCheckRadius = 0.55f;
	public bool isLocalPlayer = false;
	public LayerMask groundLayer;

	private Rigidbody2D rb;
	private bool isGrounded;

	// Unity lifecycle method that is called once before the first frame update but after Awake() method:
	void Start()
	{
		rb = GetComponent<Rigidbody2D>();
		groundLayer = LayerMask.GetMask("Ground");
	}

	// Unity lifecycle method that is called once per frame, frequency varies depending on client
	// Commonly used for handling input and animations, where responsiveness to frame changes is important
	void Update()
	{
		if (isLocalPlayer)
		{
			HandleInput();
		}
	}

	// Unity lifecycle method that is called at fixed intervals, frequency set in Unity settings
	// Commonly used for physics calculations and anything that needs to be independent of frame rate fluctuations
	void FixedUpdate()
	{
		if (isLocalPlayer)
		{
			CheckGround();
			Move();
			ApplyGravity();
			NetworkManager.Instance.SendClientPlayerUpdate(transform.position);
		}
	}

	void HandleInput()
	{
		if (Input.GetButtonDown("Jump") && isGrounded)
		{
			Jump();
		}
	}

	void CheckGround()
	{
		isGrounded = Physics2D.OverlapCircle(transform.position, groundCheckRadius, groundLayer);
	}

	void Move()
	{
		float horizontalInput = Input.GetAxis("Horizontal");
		Vector2 movement = new Vector2(horizontalInput, 0f);
		rb.velocity = new Vector2(movement.x * moveSpeed, rb.velocity.y);
	}

	void Jump()
	{
		rb.velocity = new Vector2(rb.velocity.x, jumpForce);
	}

	void ApplyGravity()
	{
		rb.velocity -= Vector2.down * Physics2D.gravity.y * gravityMultiplier * Time.fixedDeltaTime;
	}
}
