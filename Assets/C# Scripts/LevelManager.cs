using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LevelManager dynamically spawns level chunks as the player progresses,
/// despawns old ones to save memory, and tells the CameraController to 
/// slide over when the player crosses into a new level.
/// </summary>
public class LevelManager : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("List of level chunk prefabs to spawn randomly or sequentially.")]
    public GameObject[] levelPrefabs;

    [Header("Settings")]
    [Tooltip("The width of each level chunk in Unity units (e.g., 36 for a 16:9 screen at size 10).")]
    public float levelWidth = 35.5f;

    [Tooltip("How many level chunks to keep active at maximum? Older ones get destroyed.")]
    public int maxActiveLevels = 3;

    [Tooltip("Distance from the player to the right edge of the *current* spawned sequence that triggers a new spawn.")]
    public float spawnTriggerDistance = 20f;

    [Header("References")]
    [Tooltip("Reference to the player object to track progress. Auto-finds tagged 'Player' if left null.")]
    public Transform player;

    [Tooltip("Reference to the CameraController. Auto-finds Camera.main if left null.")]
    public CameraController cameraController;

    // Track active level chunks
    private Queue<GameObject> activeLevels = new Queue<GameObject>();

    // The X position where the NEXT level chunk should spawn
    private float nextSpawnX = 0f;

    // The index of the level section the player is currently inside
    private int currentPlayerSectionIndex = 0;

    void Start()
    {
        // Auto-find references if not assigned
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
                player = playerObj.transform;
            else
                Debug.LogWarning("[LevelManager] Could not find object tagged 'Player'.");
        }

        if (cameraController == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
                cameraController = mainCam.GetComponent<CameraController>();
            
            if (cameraController == null)
                Debug.LogWarning("[LevelManager] Could not find CameraController on Camera.main.");
        }

        if (levelPrefabs == null || levelPrefabs.Length == 0)
        {
            Debug.LogError("[LevelManager] No level prefabs assigned! Cannot spawn levels.");
            return;
        }

        // Spawn the first few levels to get started.
        // E.g., spawn one at X=0 (start), and one ahead.
        SpawnNextLevel(); 
        SpawnNextLevel();
    }

    void Update()
    {
        if (player == null) return;

        // 1. Check if we need to spawn a new level chunk ahead of the player.
        // We spawn if the player is getting close to the nextSpawnX limit.
        if (player.position.x > (nextSpawnX - spawnTriggerDistance))
        {
            SpawnNextLevel();
        }

        // 2. Check if the player crossed into a completely new level section 
        // to tell the camera to slide over.
        // We figure out the player's 'section index' based on X position and level width.
        // Using Mathf.FloorToInt handles negative positions too, though we typically move right.
        int calculatedSectionIndex = Mathf.FloorToInt((player.position.x + (levelWidth / 2f)) / levelWidth);

        if (calculatedSectionIndex > currentPlayerSectionIndex)
        {
            currentPlayerSectionIndex = calculatedSectionIndex;
            
            // The center point of the new section is its index * width
            float newCenterX = currentPlayerSectionIndex * levelWidth;

            // Optional: You could maintain the current Y, or use a specific Y if your levels change height
            Vector3 newCenterPoint = new Vector3(newCenterX, cameraController.transform.position.y, 0f);

            if (cameraController != null)
            {
                cameraController.SlideToNewSection(newCenterPoint);
                Debug.Log($"[LevelManager] Player entered section {currentPlayerSectionIndex}. Camera sliding to {newCenterX}.");
            }
        }
    }

    /// <summary>
    /// Spawns a new random level chunk from the prefabs array and manages cleanup of old ones.
    /// </summary>
    private void SpawnNextLevel()
    {
        if (levelPrefabs.Length == 0) return;

        // Pick a random level prefab
        int randomIndex = Random.Range(0, levelPrefabs.Length);
        GameObject prefabToSpawn = levelPrefabs[randomIndex];

        // Instantiate it at the nextSpawnX position
        Vector3 spawnPos = new Vector3(nextSpawnX, 0f, 0f);
        GameObject newLevel = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity, this.transform);

        // Track it
        activeLevels.Enqueue(newLevel);

        // Advance the spawn point for the next one
        nextSpawnX += levelWidth;

        // Destroy oldest level if we exceed the max active limit to save memory
        if (activeLevels.Count > maxActiveLevels)
        {
            GameObject levelToRemove = activeLevels.Dequeue();
            Destroy(levelToRemove);
        }
    }
}
