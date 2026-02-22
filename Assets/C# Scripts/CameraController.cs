using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    [Tooltip("How fast the camera slides to the new position.")]
    public float smoothTime = 0.3f;
    
    // The specific position the camera is trying to look at right now
    private Vector3 targetPosition;
    
    // Used by Unity's SmoothDamp function for calculating momentum
    private Vector3 currentVelocity = Vector3.zero;

    void Start()
    {
        // When the game starts, lock onto wherever the camera already is
        targetPosition = transform.position;
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
        targetPosition = new Vector3(newCenterPoint.x, newCenterPoint.y, transform.position.z);
    }
}
