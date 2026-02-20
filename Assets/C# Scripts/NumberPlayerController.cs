using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// NumberPlayerController: A dynamic 2D player controller for the "NumShift" game.
/// 
/// Core Mechanic: The player's abilities (speed, jump force, size, mass) shift based on a 
/// currentNumber value that changes during gameplay. Higher numbers make the player larger 
/// and heavier (slower jump, faster movement), while lower numbers make them smaller and lighter.
///
/// Movement Improvements:
/// - Acceleration / deceleration for smooth, responsive movement
/// - Coyote time: brief window to jump after walking off a ledge
/// - Jump buffering: queued jump if Space is pressed slightly before landing
/// - Variable jump height: hold Space to jump higher, release early to cut
/// - Extra fall gravity: snappier, less floaty jump arc
///
/// Squash & Stretch:
/// - Stretch upward when jumping
/// - Squash on landing
/// - Slight horizontal stretch when running at speed
/// - Brief pop-out scale when the number shifts
/// </summary>
public class NumberPlayerController : MonoBehaviour
{
    // ============================================================================
    // SERIALIZED FIELDS - Configurable in the Inspector
    // ============================================================================

    [Header("Number Settings")]
    public int currentNumber = 1;

    [Header("Movement")]
    [SerializeField] private float baseSpeed = 5f;
    [Tooltip("How fast the player accelerates to max speed (higher = snappier)")]
    [SerializeField] private float acceleration = 60f;
    [Tooltip("How fast the player decelerates when no input (higher = snappier stop)")]
    [SerializeField] private float deceleration = 80f;

    [Header("Jumping")]
    [SerializeField] private float baseJumpForce = 5f;
    [Tooltip("Multiply gravity when falling for a snappier arc")]
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [Tooltip("Multiply gravity when the jump button is released early (low jump)")]
    [SerializeField] private float lowJumpGravityMultiplier = 2.0f;
    [Tooltip("Seconds after leaving a ledge that the player can still jump")]
    [SerializeField] private float coyoteTime = 0.12f;
    [Tooltip("Seconds before landing that a jump press is remembered")]
    [SerializeField] private float jumpBufferTime = 0.15f;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Transform groundCheckTransform;
    [SerializeField] private float groundCheckRadius = 0.2f;

    [Header("Squash & Stretch")]
    [Tooltip("How much to stretch vertically on jump")]
    [SerializeField] private float jumpStretchY = 1.25f;
    [Tooltip("How much to squash vertically on land")]
    [SerializeField] private float landSquashY = 0.65f;
    [Tooltip("How fast squash/stretch lerps back to normal")]
    [SerializeField] private float squashStretchSpeed = 14f;
    [Tooltip("Max horizontal stretch multiplier when running at full speed")]
    [SerializeField] private float runStretchX = 1.1f;
    [Tooltip("How much the scale pops on a number shift")]
    [SerializeField] private float shiftPopScale = 1.3f;
    [Tooltip("How fast the shift pop lerps back")]
    [SerializeField] private float shiftPopSpeed = 10f;

    [Header("Display")]
    [SerializeField] private TextMeshPro numberDisplay;
    [SerializeField] private float baseFontSize = 6f;

    // ============================================================================
    // PRIVATE FIELDS
    // ============================================================================

    private Rigidbody2D rb2d;

    /// <summary>
    /// Cached reference to the BoxCollider2D component.
    /// Used to adjust the collider size when the player shifts numbers.
    /// </summary>
    private BoxCollider2D boxCollider;
    private Vector2 initialColliderSize;
    private bool isGrounded;
    private bool wasGrounded;
    private float moveInput;
    private float adjustedSpeed;
    private float adjustedJumpForce;
    private Vector3 initialScale;
    private float initialMass;
    private int previousNumber = -1;
    private float defaultGravityScale;

    // Coyote time & jump buffer
    private float coyoteTimeCounter;
    private float jumpBufferCounter;
    private bool isJumping;

    // Squash & stretch
    private Vector3 squashStretchScale = Vector3.one;   // Relative deformation (multiplied on top of base scale)
    private float shiftPopMultiplier = 1f;              // Extra pop on number shift

    // ============================================================================
    // CONSTANTS
    // ============================================================================

    private const int MAX_NUMBER = 9;
    private const int MIN_NUMBER = -9;

    // ============================================================================
    // INITIALIZATION
    // ============================================================================

    void Start()
    {
        rb2d = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();

        if (boxCollider != null)
        {
            initialColliderSize = boxCollider.size;
            // Squeeze tighter on Y than X for a better fit
            boxCollider.size = new Vector2(initialColliderSize.x * 0.7f, initialColliderSize.y * 0.7f);
        }

        if (numberDisplay == null)
            numberDisplay = GetComponentInChildren<TextMeshPro>();

        if (numberDisplay != null)
            numberDisplay.richText = true;

        initialScale = transform.localScale;
        initialMass  = rb2d.mass;
        defaultGravityScale = rb2d.gravityScale;

        squashStretchScale = Vector3.one;

        UpdatePlayerStats();
        DebugLogStats("Player initialized");
    }

    // ============================================================================
    // MAIN UPDATE LOOP
    // ============================================================================

    void Update()
    {
        // --- Read the number from the TMP text so other scripts can drive it ---
        ReadNumberFromDisplay();

        // --- Number shift detection ---
        if (currentNumber != previousNumber)
        {
            currentNumber = Mathf.Clamp(currentNumber, MIN_NUMBER, MAX_NUMBER);
            UpdatePlayerStats();
            previousNumber = currentNumber;
            shiftPopMultiplier = shiftPopScale;   // Trigger pop animation
            DebugLogStats($"Player shifted to number {currentNumber}");
        }

        // --- Timers ---
        coyoteTimeCounter -= Time.deltaTime;
        jumpBufferCounter -= Time.deltaTime;

        // --- Ground ---
        wasGrounded = isGrounded;
        CheckGroundContact();

        if (isGrounded)
        {
            coyoteTimeCounter = coyoteTime;
            isJumping = false;
        }

        // Squash on landing
        if (isGrounded && !wasGrounded)
            squashStretchScale = new Vector3(1.3f, landSquashY, 1f);

        // --- Input ---
        HandleHorizontalMovement();

        // Buffer the jump press
        if (Input.GetKeyDown(KeyCode.Space))
            jumpBufferCounter = jumpBufferTime;

        HandleJump();
        HandleGravity();
        FreezeRotation();

        // --- Squash & stretch animation ---
        UpdateSquashStretch();
    }

    // ============================================================================
    // MOVEMENT
    // ============================================================================

    void HandleHorizontalMovement()
    {
        moveInput = Input.GetAxisRaw("Horizontal");   // Raw for crisper feel

        float targetVelocityX = moveInput * adjustedSpeed;
        float currentVelocityX = rb2d.velocity.x;

        // Choose acceleration or deceleration rate
        float rate = (Mathf.Abs(moveInput) > 0.01f) ? acceleration : deceleration;

        // Smoothly move current velocity toward target
        float newVelocityX = Mathf.MoveTowards(currentVelocityX, targetVelocityX, rate * Time.deltaTime);

        rb2d.velocity = new Vector2(newVelocityX, rb2d.velocity.y);
    }

    // ============================================================================
    // GROUND DETECTION
    // ============================================================================

    void CheckGroundContact()
    {
        isGrounded = Physics2D.OverlapCircle(
            groundCheckTransform.position,
            groundCheckRadius,
            groundLayer
        ) != null;
    }

    // ============================================================================
    // JUMPING
    // ============================================================================

    void HandleJump()
    {
        // Consume the jump buffer if we can jump (grounded or coyote window)
        if (jumpBufferCounter > 0f && coyoteTimeCounter > 0f)
        {
            rb2d.velocity = new Vector2(rb2d.velocity.x, 0f);
            rb2d.AddForce(Vector2.up * adjustedJumpForce, ForceMode2D.Impulse);

            jumpBufferCounter  = 0f;
            coyoteTimeCounter  = 0f;
            isJumping          = true;

            // Stretch upward on jump
            squashStretchScale = new Vector3(0.8f, jumpStretchY, 1f);
        }
    }

    void HandleGravity()
    {
        if (rb2d.velocity.y < 0f)
        {
            // Falling — apply extra gravity for a snappier arc
            rb2d.gravityScale = defaultGravityScale * fallGravityMultiplier;
        }
        else if (rb2d.velocity.y > 0f && isJumping && !Input.GetKey(KeyCode.Space))
        {
            // Rising but jump button released — cut the jump short
            rb2d.gravityScale = defaultGravityScale * lowJumpGravityMultiplier;
        }
        else
        {
            rb2d.gravityScale = defaultGravityScale;
        }
    }

    // ============================================================================
    // ROTATION CONTROL
    // ============================================================================

    void FreezeRotation()
    {
        Vector3 rot = transform.eulerAngles;
        transform.eulerAngles = new Vector3(rot.x, rot.y, 0f);
    }

    // ============================================================================
    // SQUASH & STRETCH
    // ============================================================================

    void UpdateSquashStretch()
    {
        // Lerp squash/stretch back toward neutral
        squashStretchScale = Vector3.Lerp(squashStretchScale, Vector3.one, squashStretchSpeed * Time.deltaTime);

        // Horizontal run stretch (only when grounded and moving fast)
        float speedRatio = (adjustedSpeed > 0f)
            ? Mathf.Abs(rb2d.velocity.x) / adjustedSpeed
            : 0f;

        float runX = isGrounded ? Mathf.Lerp(1f, runStretchX, speedRatio) : 1f;
        // Compensate Y so volume stays roughly constant
        float runY = isGrounded ? Mathf.Lerp(1f, 1f / runStretchX, speedRatio) : 1f;

        // Combine deformation with run stretch
        Vector3 deform = new Vector3(
            squashStretchScale.x * runX,
            squashStretchScale.y * runY,
            1f
        );

        // Decay shift pop multiplier
        shiftPopMultiplier = Mathf.Lerp(shiftPopMultiplier, 1f, shiftPopSpeed * Time.deltaTime);

        // Get the "base" scale for this number (set by UpdatePlayerStats)
        // Positive numbers grow the player; negative numbers shrink it.
        float scaleIncrement = 0.2f;
        float numberScale;
        if (currentNumber >= 0)
            numberScale = 1f + (currentNumber - 1) * scaleIncrement;
        else
            numberScale = Mathf.Max(1f - (Mathf.Abs(currentNumber) - 1) * scaleIncrement, 0.1f);
        Vector3 baseScale = initialScale * numberScale * shiftPopMultiplier;

        // Apply deformation on top
        transform.localScale = new Vector3(
            baseScale.x * deform.x,
            baseScale.y * deform.y,
            baseScale.z
        );
    }

    // ============================================================================
    // READ NUMBER FROM DISPLAY
    // ============================================================================

    /// <summary>
    /// Reads the current number from the TextMeshPro text.
    /// This allows other scripts to change the displayed text and have the
    /// player stats automatically update to match.
    /// </summary>
    void ReadNumberFromDisplay()
    {
        if (numberDisplay != null && !string.IsNullOrEmpty(numberDisplay.text))
        {
            string trimmed = numberDisplay.text.Trim();
            if (int.TryParse(trimmed, out int parsedNumber))
            {
                currentNumber = Mathf.Clamp(parsedNumber, MIN_NUMBER, MAX_NUMBER);
            }
        }
    }

    // ============================================================================
    // DYNAMIC STAT CALCULATION
    // ============================================================================

    void UpdatePlayerStats()
    {
        // Use absolute value so negative numbers scale the same as positive
        int absNumber = Mathf.Abs(currentNumber);

        // Speed increases with higher absolute numbers
        float speedMultiplier = 0.5f;
        adjustedSpeed = baseSpeed + (absNumber - 1) * speedMultiplier;

        // Jump force:
        // - Positive numbers: Decrease (heavier = lower jump)
        // - Negative numbers: Increase at double the rate (much higher than any positive)
        float jumpPenalty = 0.3f;
        float jumpBonus   = 1.1f; // 2x the penalty rate so negatives clearly exceed positives
        if (currentNumber >= 1)
        {
            adjustedJumpForce = baseJumpForce - (currentNumber - 1) * jumpPenalty;
        }
        else
        {
            // E.g., at -9: baseJump + (9 - 1) * 0.6 = baseJump + 4.8
            adjustedJumpForce = baseJumpForce + (Mathf.Abs(currentNumber) - 1) * jumpBonus;
        }
        adjustedJumpForce = Mathf.Max(adjustedJumpForce, 1f);

        // Mass increases with higher absolute numbers
        float massMultiplier = 0.3f;
        float newMass        = initialMass * (1f + (absNumber - 1) * massMultiplier);
        rb2d.mass = newMass;

        // NOTE: BoxCollider2D is automatically scaled by transform.localScale,
        // so no explicit collider size adjustment is needed here.

        // Visual update
        UpdateColor();
    }

    // ============================================================================
    // VISUAL FEEDBACK
    // ============================================================================

    void UpdateColor()
    {
        Color textColor;

        if (currentNumber <= 3)
            textColor = Color.blue;
        else if (currentNumber <= 6)
            textColor = Color.green;
        else
            textColor = Color.red;

        if (numberDisplay != null)
        {
            // Don't overwrite the text — other scripts control what the text says.
            // We only update font size and color.
            float fontSizeMultiplier = 1.0f;
            numberDisplay.fontSize = baseFontSize + (Mathf.Abs(currentNumber) - 1) * fontSizeMultiplier;
            numberDisplay.color    = textColor;
        }
    }

    // ============================================================================
    // DEBUG HELPERS
    // ============================================================================

    void DebugLogStats(string context = "")
    {
        string logMessage = $"[NumShift] {context}\n" +
            $"  Number: {currentNumber}\n" +
            $"  Speed: {adjustedSpeed:F2}\n" +
            $"  Jump Force: {adjustedJumpForce:F2}\n" +
            $"  Scale: {transform.localScale.x:F2}\n" +
            $"  Mass: {rb2d.mass:F2}\n" +
            $"  Grounded: {isGrounded}";

        Debug.Log(logMessage);
    }

    // ============================================================================
    // GIZMOS
    // ============================================================================

    void OnDrawGizmosSelected()
    {
        if (groundCheckTransform != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheckTransform.position, groundCheckRadius);
        }
    }
}
