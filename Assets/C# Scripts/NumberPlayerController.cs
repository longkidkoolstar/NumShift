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
/// Movement Feel:
/// - Acceleration / deceleration with separate air & ground rates
/// - Turn-around boost: extra deceleration when reversing direction (skid feel)
/// - Coyote time: brief window to jump after walking off a ledge
/// - Jump buffering: queued jump if Space is pressed slightly before landing
/// - Variable jump height: hold Space to jump higher, release early to cut
/// - Extra fall gravity: snappier, less floaty jump arc
/// - Apex hang: reduced gravity near the peak of the jump for satisfying hang-time
/// - Apex speed bonus: extra horizontal speed at the top of the arc for better air control
///
/// Juice & Feedback:
/// - Squash & stretch on jump, land, and run (volume-preserving)
/// - Velocity-based landing squash: harder landings = bigger squash
/// - Visual lean: subtle tilt in the direction of movement
/// - Landing SFX with impact-based pitch/volume variation
/// - Dust particle hooks for landing and running
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
    [Tooltip("Acceleration multiplier when in the air (lower = more commitment)")]
    [SerializeField] private float airAccelerationMultiplier = 0.65f;
    [Tooltip("Extra deceleration when actively turning around (skid feel)")]
    [SerializeField] private float turnAroundMultiplier = 2.5f;

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
    [Tooltip("Velocity threshold for apex hang (near the peak of the jump)")]
    [SerializeField] private float apexThreshold = 1.5f;
    [Tooltip("Gravity multiplier at the apex for a floaty hang-time feel")]
    [SerializeField] private float apexGravityMultiplier = 0.4f;
    [Tooltip("Extra horizontal speed bonus at the apex for better air control")]
    [SerializeField] private float apexSpeedBonus = 1.5f;

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
    [Tooltip("Max lean angle (degrees) when running at full speed")]
    [SerializeField] private float maxLeanAngle = 5f;
    [Tooltip("How fast the lean angle interpolates")]
    [SerializeField] private float leanSpeed = 12f;
    [Tooltip("Minimum fall speed to trigger a velocity-based landing squash")]
    [SerializeField] private float minLandVelocity = 2f;
    [Tooltip("Fall speed at which landing squash is at maximum intensity")]
    [SerializeField] private float maxLandVelocity = 15f;

    [Header("Display")]
    [SerializeField] private TextMeshPro numberDisplay;
    [SerializeField] private float baseFontSize = 6f;

    [Header("Audio")]
    [Tooltip("Sound played every time the player jumps")]
    [SerializeField] private AudioClip jumpSFX;
    [Tooltip("Sound played when the player lands")]
    [SerializeField] private AudioClip landSFX;
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
    [Tooltip("Minimum landing velocity to play the landing SFX")]
    [SerializeField] private float landSFXMinVelocity = 3f;

    [Header("Particles")]
    [Tooltip("Optional particle system spawned at feet on landing")]
    [SerializeField] private ParticleSystem landDustParticles;
    [Tooltip("Optional particle system spawned at feet when running")]
    [SerializeField] private ParticleSystem runDustParticles;

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
    private Vector2 initialColliderOffset;
    private float baseGroundCheckRadius;
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

    // Apex detection
    private bool isAtApex;

    // Squash & stretch
    private Vector3 squashStretchScale = Vector3.one;   // Relative deformation (multiplied on top of base scale)
    private float shiftPopMultiplier = 1f;              // Extra pop on number shift
    private float currentLeanAngle = 0f;                // Current visual tilt
    private float lastFallSpeed = 0f;                   // Track fall speed for landing impact

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
            initialColliderOffset = boxCollider.offset;
            // Squeeze tighter on Y than X for a better fit
            boxCollider.size = new Vector2(initialColliderSize.x * 0.48f, initialColliderSize.y * 0.48f);
        }

        baseGroundCheckRadius = groundCheckRadius;

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

        // --- Track fall speed (before ground check resets velocity) ---
        if (rb2d.velocity.y < 0f)
            lastFallSpeed = Mathf.Abs(rb2d.velocity.y);

        // --- Ground ---
        wasGrounded = isGrounded;
        CheckGroundContact();

        if (isGrounded)
        {
            coyoteTimeCounter = coyoteTime;
            isJumping = false;
        }

        // Velocity-based squash on landing — harder landings = bigger squash
        if (isGrounded && !wasGrounded)
        {
            float impactT = Mathf.InverseLerp(minLandVelocity, maxLandVelocity, lastFallSpeed);
            float squashIntensity = GetDeformIntensity() * Mathf.Lerp(0.4f, 1f, impactT);
            float squashX = Mathf.Lerp(1f, 1.3f,   squashIntensity);
            float squashY = Mathf.Lerp(1f, landSquashY, squashIntensity);
            squashStretchScale = new Vector3(squashX, squashY, 1f);

            // Landing SFX — louder and lower pitch for harder impacts
            PlayLandSFX(impactT);

            // Landing dust particles
            if (landDustParticles != null && impactT > 0.15f)
            {
                landDustParticles.transform.position = groundCheckTransform.position;
                var emission = landDustParticles.emission;
                var burst = emission.GetBurst(0);
                burst.count = Mathf.Lerp(3, 12, impactT);
                emission.SetBurst(0, burst);
                landDustParticles.Play();
            }

            lastFallSpeed = 0f;
        }

        // --- Input ---
        HandleHorizontalMovement();

        // Buffer the jump press
        if (Input.GetKeyDown(KeyCode.Space))
            jumpBufferCounter = jumpBufferTime;

        HandleJump();
        HandleGravity();

        // --- Run dust particles ---
        HandleRunDust();

        // --- Visual lean ---
        UpdateLean();

        // --- Squash & stretch animation ---
        UpdateSquashStretch();
    }

    // ============================================================================
    // MOVEMENT
    // ============================================================================

    void HandleHorizontalMovement()
    {
        moveInput = Input.GetAxisRaw("Horizontal");   // Raw for crisper feel

        // Apex speed bonus — extra horizontal control near the top of a jump
        float apexBonus = isAtApex ? apexSpeedBonus : 0f;
        float targetVelocityX = moveInput * (adjustedSpeed + apexBonus);
        float currentVelocityX = rb2d.velocity.x;

        // Detect if the player is actively turning around (input opposes velocity)
        bool isTurningAround = (Mathf.Abs(moveInput) > 0.01f) &&
                               (Mathf.Sign(moveInput) != Mathf.Sign(currentVelocityX)) &&
                               (Mathf.Abs(currentVelocityX) > 0.5f);

        // Choose acceleration or deceleration rate
        float rate;
        if (Mathf.Abs(moveInput) < 0.01f)
        {
            rate = deceleration;                         // No input → decelerate
        }
        else if (isTurningAround)
        {
            rate = deceleration * turnAroundMultiplier;  // Turning → fast skid-stop
        }
        else
        {
            rate = acceleration;                         // Normal acceleration
        }

        // Reduce air control so jumps feel more committed
        if (!isGrounded)
            rate *= airAccelerationMultiplier;

        // Smoothly move current velocity toward target
        float newVelocityX = Mathf.MoveTowards(currentVelocityX, targetVelocityX, rate * Time.deltaTime);

        rb2d.velocity = new Vector2(newVelocityX, rb2d.velocity.y);
    }

    // ============================================================================
    // GROUND DETECTION
    // ============================================================================

    void CheckGroundContact()
    {
        // Dynamically reposition the ground check to the actual bottom of the collider.
        // This ensures it always sits just below the player's feet, even when scaled up.
        if (groundCheckTransform != null && boxCollider != null)
        {
            float colliderBottomLocal = boxCollider.offset.y - (boxCollider.size.y / 2f);
            // Position the ground check slightly below the collider bottom edge
            groundCheckTransform.localPosition = new Vector3(
                groundCheckTransform.localPosition.x,
                colliderBottomLocal - 0.02f,
                groundCheckTransform.localPosition.z
            );
        }

        // Scale the ground check radius with the player so it works at all sizes
        float currentScaleY = Mathf.Abs(transform.localScale.y);
        float scaledRadius = baseGroundCheckRadius * Mathf.Max(currentScaleY, 0.5f);

        isGrounded = Physics2D.OverlapCircle(
            groundCheckTransform.position,
            scaledRadius,
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
            rb2d.AddForce(Vector2.up * (adjustedJumpForce * rb2d.mass), ForceMode2D.Impulse);

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

    /// <summary>
    /// Plays the landing SFX with intensity based on fall speed.
    /// Harder landings produce a louder, lower-pitched thud.
    /// </summary>
    void PlayLandSFX(float impactT)
    {
        if (audioSource == null || landSFX == null) return;
        if (lastFallSpeed < landSFXMinVelocity) return;
        if (sfxCooldownCounter > 0f) return;

        // Harder impact → lower pitch, louder volume
        float pitch = Mathf.Lerp(1.15f, 0.80f, impactT);
        float jitter = Random.Range(-0.04f, 0.04f);
        audioSource.pitch = Mathf.Clamp(pitch + jitter, 0.5f, 2f);

        float volume = Mathf.Lerp(0.3f, 0.9f, impactT);
        audioSource.PlayOneShot(landSFX, volume);
        sfxCooldownCounter = jumpSFXCooldown;
    }

    void HandleGravity()
    {
        // Detect apex: near the peak of the jump arc
        isAtApex = isJumping && Mathf.Abs(rb2d.velocity.y) < apexThreshold && !isGrounded;

        if (isAtApex)
        {
            // Apex hang — reduced gravity for satisfying hang-time
            rb2d.gravityScale = defaultGravityScale * apexGravityMultiplier;
        }
        else if (rb2d.velocity.y < 0f)
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
    // VISUAL LEAN & RUN DUST
    // ============================================================================

    /// <summary>
    /// Applies a subtle visual tilt in the direction of movement.
    /// Makes the character feel dynamic and alive when running.
    /// The lean is purely cosmetic — physics rotation is still locked.
    /// </summary>
    void UpdateLean()
    {
        if (!isGrounded)
        {
            // Smoothly return to upright in the air
            currentLeanAngle = Mathf.Lerp(currentLeanAngle, 0f, leanSpeed * Time.deltaTime);
        }
        else
        {
            // Lean into the direction of velocity
            float speedRatio = (adjustedSpeed > 0f) ? rb2d.velocity.x / adjustedSpeed : 0f;
            float targetLean = -speedRatio * maxLeanAngle; // Negative because Unity Z rotation is counter-clockwise
            currentLeanAngle = Mathf.Lerp(currentLeanAngle, targetLean, leanSpeed * Time.deltaTime);
        }

        // Apply lean — keep physics rotation locked but allow visual tilt
        rb2d.freezeRotation = true;
        transform.eulerAngles = new Vector3(0f, 0f, currentLeanAngle);
    }

    /// <summary>
    /// Emits run dust particles when the player is moving fast on the ground.
    /// </summary>
    void HandleRunDust()
    {
        if (runDustParticles == null) return;

        bool shouldEmit = isGrounded && Mathf.Abs(rb2d.velocity.x) > adjustedSpeed * 0.5f;

        if (shouldEmit && !runDustParticles.isPlaying)
            runDustParticles.Play();
        else if (!shouldEmit && runDustParticles.isPlaying)
            runDustParticles.Stop();
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

        // --- Anchor the collider so the player grows UPWARD from the feet ---
        // Compute the scale for the new number (same formula as UpdateSquashStretch)
        float scaleIncrement = 0.2f;
        float numberScale;
        if (currentNumber >= 0)
            numberScale = 1f + (currentNumber - 1) * scaleIncrement;
        else
            numberScale = Mathf.Max(1f - (Mathf.Abs(currentNumber) - 1) * scaleIncrement, 0.1f);

        if (boxCollider != null)
        {
            // Capture where the bottom of the collider is right now in world space
            float oldBottomLocal = boxCollider.offset.y - (boxCollider.size.y / 2f);
            float oldBottomWorld = transform.position.y + oldBottomLocal * transform.localScale.y;

            // Compute the new Y scale
            float newScaleY = initialScale.y * numberScale;

            // Adjust the collider offset so the bottom edge stays at the same local-space
            // relative position. The collider center shifts upward as the player grows.
            float halfHeight = boxCollider.size.y / 2f;
            float desiredBottomLocal = oldBottomLocal;  // Keep feet at the same local-space position
            float newOffsetY = desiredBottomLocal + halfHeight;
            boxCollider.offset = new Vector2(boxCollider.offset.x, newOffsetY);

            // Shift the player's world position upward so the feet stay on the ground
            float newBottomWorld = transform.position.y + desiredBottomLocal * newScaleY;
            float shiftUp = oldBottomWorld - newBottomWorld;
            if (Mathf.Abs(shiftUp) > 0.001f)
            {
                transform.position += new Vector3(0f, shiftUp, 0f);
            }
        }

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
            float currentScaleY = Mathf.Abs(transform.localScale.y);
            float scaledRadius = baseGroundCheckRadius * Mathf.Max(currentScaleY, 0.5f);
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheckTransform.position, scaledRadius);
        }
    }
}
