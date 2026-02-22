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

    [Header("Audio")]
    [Tooltip("Sound played every time the player jumps")]
    [SerializeField] private AudioClip jumpSFX;
    [Tooltip("AudioSource used to play SFX (auto-fetched if left empty)")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("Pitch when jumping at minimum force (large positive number)")]
    [SerializeField] private float jumpPitchMin = 0.85f;
    [Tooltip("Pitch when jumping at maximum force (large negative number)")]
    [SerializeField] private float jumpPitchMax = 1.25f;
    [Tooltip("Extra random pitch jitter applied on top of the force-based pitch (±half this value)")]
    [SerializeField] private float jumpPitchJitter = 0.07f;
    [Tooltip("Volume when jumping at minimum force")]
    [SerializeField] private float jumpVolumeMin = 0.55f;
    [Tooltip("Volume when jumping at maximum force")]
    [SerializeField] private float jumpVolumeMax = 1.0f;
    [Tooltip("Minimum seconds between SFX plays — prevents rapid-fire stacking")]
    [SerializeField] private float jumpSFXCooldown = 0.08f;

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

    // Audio
    private float sfxCooldownCounter = 0f;

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
        if (rb2d != null)
        {
            rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        boxCollider = GetComponent<BoxCollider2D>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (boxCollider != null)
        {
            initialColliderSize = boxCollider.size;
            // Squeeze tighter on Y than X for a better fit
            boxCollider.size = new Vector2(initialColliderSize.x * 0.48f, initialColliderSize.y * 0.48f);
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
        sfxCooldownCounter -= Time.deltaTime;
        coyoteTimeCounter  -= Time.deltaTime;
        jumpBufferCounter -= Time.deltaTime;

        // --- Ground ---
        wasGrounded = isGrounded;
        CheckGroundContact();

        if (isGrounded)
        {
            coyoteTimeCounter = coyoteTime;
            isJumping = false;
        }

        // Squash on landing — intensity scales down for bigger players
        if (isGrounded && !wasGrounded)
        {
            float squashIntensity = GetDeformIntensity();
            float squashX = Mathf.Lerp(1f, 1.3f,   squashIntensity);
            float squashY = Mathf.Lerp(1f, landSquashY, squashIntensity);
            squashStretchScale = new Vector3(squashX, squashY, 1f);
        }

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

            // Play jump SFX with force-based pitch/volume variation
            PlayJumpSFX();

            // Stretch upward on jump — intensity scales down for bigger players
            float stretchIntensity = GetDeformIntensity();
            float stretchX = Mathf.Lerp(1f, 0.8f,       stretchIntensity);
            float stretchY = Mathf.Lerp(1f, jumpStretchY, stretchIntensity);
            squashStretchScale = new Vector3(stretchX, stretchY, 1f);
        }
    }

    // ============================================================================
    // AUDIO
    // ============================================================================

    /// <summary>
    /// Plays the jump SFX with pitch and volume adjusted to match jump strength.
    /// - Stronger jumps (large negative numbers) → higher pitch, louder
    /// - Weaker jumps (large positive numbers)   → lower pitch, softer
    /// - A small random jitter keeps consecutive jumps from sounding identical.
    /// - A cooldown prevents clip stacking when jump-buffering fires rapidly.
    /// </summary>
    void PlayJumpSFX()
    {
        if (audioSource == null || jumpSFX == null) return;
        if (sfxCooldownCounter > 0f) return;

        // Normalise jump force: 0 = weakest possible, 1 = strongest possible
        // adjustedJumpForce is clamped to >= 1, theoretical max ≈ baseJumpForce + (9-1)*jumpBonus
        float maxForce = baseJumpForce + 8f * 1.1f;   // matches the formula in UpdatePlayerStats
        float forceT   = Mathf.InverseLerp(1f, maxForce, adjustedJumpForce);

        // Pitch: stronger jump → higher pitch
        float basePitch = Mathf.Lerp(jumpPitchMin, jumpPitchMax, forceT);
        float jitter    = Random.Range(-jumpPitchJitter * 0.5f, jumpPitchJitter * 0.5f);
        audioSource.pitch = Mathf.Clamp(basePitch + jitter, 0.5f, 3f);

        // Volume: stronger jump → louder
        float volume = Mathf.Lerp(jumpVolumeMin, jumpVolumeMax, forceT);

        audioSource.PlayOneShot(jumpSFX, volume);
        sfxCooldownCounter = jumpSFXCooldown;
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
        // Calculate the bottom Y position BEFORE scale changes
        float bottomOffsetLocal = 0f;
        if (boxCollider != null)
        {
            bottomOffsetLocal = boxCollider.offset.y - (boxCollider.size.y / 2f);
        }
        float previousBottomWorldY = transform.position.y + bottomOffsetLocal * transform.localScale.y;

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

        // Shift position so that the bottom edge of the collider remains firmly anchored.
        // This completely prevents the player from expanding into the floor and falling through!
        if (boxCollider != null)
        {
            float newBottomWorldY = transform.position.y + bottomOffsetLocal * transform.localScale.y;
            float shiftUpAmount = previousBottomWorldY - newBottomWorldY;
            transform.position += new Vector3(0, shiftUpAmount, 0);
        }
    }

    // ============================================================================
    // SQUASH & STRETCH HELPERS
    // ============================================================================

    /// <summary>
    /// Returns a 0–1 intensity for squash/stretch deformation.
    /// At number=1 (smallest/default) intensity is 1 (full effect).
    /// At number=9 (biggest) intensity is reduced so the deformation
    /// looks proportional to the player's actual jump height.
    /// </summary>
    float GetDeformIntensity()
    {
        // numberScale goes from ~0.1 (number=-9) up to ~2.6 (number=9)
        float scaleIncrement = 0.2f;
        float numberScale;
        if (currentNumber >= 0)
            numberScale = 1f + (currentNumber - 1) * scaleIncrement;
        else
            numberScale = Mathf.Max(1f - (Mathf.Abs(currentNumber) - 1) * scaleIncrement, 0.1f);

        // Clamp intensity so it never fully disappears (min 30% of the effect)
        return Mathf.Clamp(1f / numberScale, 0.3f, 1f);
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
