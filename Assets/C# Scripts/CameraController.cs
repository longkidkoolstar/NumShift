using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    [Tooltip("Vertical offset applied to the camera target position.")]
    public float yOffset = 0f;
    [Tooltip("How fast the camera slides to the new position.")]
    public float smoothTime = 0.3f;
    
    [Header("Aspect Ratio Settings")]
    [Tooltip("Target aspect ratio. E.g., 16 / 9 for standard widescreen.")]
    public float targetAspectRatio = 16f / 9f;
    [Tooltip("Keep this checked if you want the game window to adjust black bars dynamically when resized.")]
    public bool updateRatioContinuously = true;

    // The specific position the camera is trying to look at right now
    private Vector3 targetPosition;
    
    // Used by Unity's SmoothDamp function for calculating momentum
    private Vector3 currentVelocity = Vector3.zero;

    private int lastScreenWidth;
    private int lastScreenHeight;
    private Camera cam;

    void Start()
    {
        // When the game starts, lock onto wherever the camera already is, applying the yOffset
        targetPosition = new Vector3(transform.position.x, transform.position.y + yOffset, transform.position.z);
        transform.position = targetPosition;
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main; // Fallback in case this script isn't on the Camera itself!

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        EnforceAspectRatio();
    }

    void Update()
    {
        if (updateRatioContinuously && (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight))
        {
            EnforceAspectRatio();
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
        }
    }

    void LateUpdate()
    {
        // Smoothly glide the camera towards our target position every frame
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);
    }

    /// <summary>
    /// Call this function from your Level Generator or a Trigger Volume 
    /// whenever the player enters a new puzzle section!
    /// </summary>
    public void SlideToNewSection(Vector3 newCenterPoint)
    {
        // Keep the camera's original Z depth so we don't accidentally zoom inside the 2D plane
        targetPosition = new Vector3(newCenterPoint.x, newCenterPoint.y + yOffset, transform.position.z);
    }

    private void EnforceAspectRatio()
    {
        if (cam == null) return;

        // Current window aspect ratio
        float windowAspect = (float)Screen.width / (float)Screen.height;
        // Ratio of the actual window size to the desired aspect ratio
        float scaleHeight = windowAspect / targetAspectRatio;

        // If the scaled height is smaller than our current height, it means the screen is too tall
        // We'll have to add letter-boxing (black bars on top and bottom)
        if (scaleHeight < 1.0f)
        {
            Rect rect = cam.rect;
            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0;
            rect.y = (1.0f - scaleHeight) / 2.0f;
            cam.rect = rect;
        }
        else // Otherwise the screen is too wide. Add pillar-boxing (black bars on left and right)
        {
            float scaleWidth = 1.0f / scaleHeight;
            Rect rect = cam.rect;
            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) / 2.0f;
            rect.y = 0;
            cam.rect = rect;
        }
    }
}
