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

        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartCurrentLevel();
        }

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
            int previousSectionIndex = currentPlayerSectionIndex;
            currentPlayerSectionIndex = calculatedSectionIndex;
            
            // The center point of the new section is its index * width
            float newCenterX = currentPlayerSectionIndex * levelWidth;

            // Pass y=0 so CameraController applies its yOffset only once
            // (using camera's current Y would stack the offset each time)
            Vector3 newCenterPoint = new Vector3(newCenterX, 0f, 0f);

            if (cameraController != null)
            {
                cameraController.SlideToNewSection(newCenterPoint);
                Debug.Log($"[LevelManager] Player entered section {currentPlayerSectionIndex}. Camera sliding to {newCenterX}.");
            }

            // Disable the BgMusic component on the old level
            DisableBgMusicOnSection(previousSectionIndex);
        }
    }

    /// <summary>
    /// Restarts the current level by moving the player back to its PlayerSpawn.
    /// </summary>
    public void RestartCurrentLevel()
    {
        if (player == null) return;

        float sectionCenterX = currentPlayerSectionIndex * levelWidth;

        foreach (GameObject level in activeLevels)
        {
            if (level == null) continue;

            // Check if this level belongs to the calculated section
            if (Mathf.Abs(level.transform.position.x - sectionCenterX) < levelWidth * 0.5f)
            {
                Transform spawnPoint = FindChildRecursive(level.transform, "PlayerSpawn");
                if (spawnPoint != null)
                {
                    player.position = spawnPoint.position;
                    // Reset velocity to prevent carrying momentum
                    Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
                    if (rb != null)
                    {
                        rb.velocity = Vector2.zero;
                    }
                    Debug.Log($"[LevelManager] Player respawned at PlayerSpawn for section {currentPlayerSectionIndex}.");
                }
                else
                {
                    Debug.LogWarning($"[LevelManager] No 'PlayerSpawn' found in section {currentPlayerSectionIndex}.");
                }
                break;
            }
        }
    }

    /// <summary>
    /// Disables the "BgMusic" component on the level in the given section.
    /// </summary>
    private void DisableBgMusicOnSection(int sectionIndex)
    {
        float sectionCenterX = sectionIndex * levelWidth;

        foreach (GameObject level in activeLevels)
        {
            if (level == null) continue;

            // Check if this level belongs to the given section
            if (Mathf.Abs(level.transform.position.x - sectionCenterX) < levelWidth * 0.5f)
            {
                // Search recursively for the BgMusic component (MonoBehaviour or AudioSource)
                Transform bgMusicTransform = FindChildRecursive(level.transform, "BgMusic");
                if (bgMusicTransform != null)
                {
                    // Disable all Behaviours on the BgMusic object
                    foreach (var behaviour in bgMusicTransform.GetComponents<Behaviour>())
                    {
                        behaviour.enabled = false;
                    }
                    Debug.Log($"[LevelManager] Disabled BgMusic on section {sectionIndex}.");
                }
                break;
            }
        }
    }

    /// <summary>
    /// Recursively searches for a child transform by name.
    /// </summary>
    private Transform FindChildRecursive(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName)
                return child;

            Transform found = FindChildRecursive(child, childName);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Spawns a new random level chunk from the prefabs array and manages cleanup of old ones.
    /// </summary>
    private void SpawnNextLevel()
    {
        if (levelPrefabs.Length == 0) return;

        bool isFirstLevel = (activeLevels.Count == 0);

        // Pick a random level prefab
        int randomIndex = Random.Range(0, levelPrefabs.Length);
        GameObject prefabToSpawn = levelPrefabs[randomIndex];

        // Instantiate it at the nextSpawnX position
        Vector3 spawnPos = new Vector3(nextSpawnX, 0f, 0f);
        GameObject newLevel = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity, this.transform);

        // Track it
        activeLevels.Enqueue(newLevel);

        if (isFirstLevel && player != null)
        {
            Transform spawnPoint = FindChildRecursive(newLevel.transform, "PlayerSpawn");
            if (spawnPoint != null)
            {
                player.position = spawnPoint.position;
                Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
                if (rb != null) rb.velocity = Vector2.zero;
                Debug.Log($"[LevelManager] Player spawned at first level's PlayerSpawn.");
            }
        }

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
