using System.Collections.Generic;
using UnityEngine;

public class TowerGeneratorScript : MonoBehaviour
{
    public enum FloorPatternStyle { RandomScatter, Checkered, ConcentricRings }

    [Header("Infrastructure & Seeds")]
    [Tooltip("Leave at 0 for a completely random seed every time.")]
    public int towerSeed = 0;
    public int currentFloor = 1;

    [Header("Arena Dimensions")]
    [Min(3)] public int gridWidth = 30;   // X-axis dimension
    [Min(3)] public int gridLength = 30;  // Z-axis dimension
    
    [Header("Wall Settings")]
    [Range(1, 10)] public int wallHeight = 10; // Stacks walls up to 10 blocks tall

    [Header("Voxel Palette (Minecraft Aesthetic)")]
    [Tooltip("Assign multiple floor blocks here (e.g., Stone Bricks, Mossy Cobble, Basalt, etc.)")]
    public GameObject[] floorBlockPalette;
    public GameObject wallPrefab;
    public GameObject spawnElevatorPrefab;
    public GameObject exitGatePrefab;

    [Header("Hazard & Entity Prefabs")]
    public GameObject magmaTrapPrefab;
    public GameObject entityPrefab;
    public int spawned;

    // Internal Grid: 0 = Empty, 1 = Floor, 2 = Wall, 3 = Spawn, 4 = Exit, 5 = Hazard
    private int[,] arenaGrid;
    private Vector2Int spawnPos;
    private Vector2Int exitPos;

    // Tracks which specific block from the palette goes onto which grid coordinate
    private int[,] floorVisualMatrix;

    private void Start()
    {
        GenerateFloor();
    }

    [ContextMenu("Generate Floor")]
    public void GenerateFloor()
    {
        // 1. Core Infrastructure Pass: Initialize Deterministic Randomness
        if (towerSeed == 0) towerSeed = Random.Range(100000, 999999);

        int floorSeed = towerSeed + currentFloor;
        Random.InitState(floorSeed);

        Debug.Log($"[PCG System] Generating Floor {currentFloor} with Seed: {floorSeed}");

        // Clear previous layer visuals (handles both Editor and Play mode safely)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }

        arenaGrid = new int[gridWidth, gridLength];
        floorVisualMatrix = new int[gridWidth, gridLength];

        // Pick a floor architectural style based on floor seed
        FloorPatternStyle activePattern = (FloorPatternStyle)Random.Range(0, System.Enum.GetValues(typeof(FloorPatternStyle)).Length);
        Debug.Log($"[PCG System] Selected Aesthetic Pattern for Floor {currentFloor}: {activePattern}");

        // Select primary and secondary accent blocks from the palette
        int primaryBlockIndex = floorBlockPalette.Length > 0 ? Random.Range(0, floorBlockPalette.Length) : 0;
        int secondaryBlockIndex = floorBlockPalette.Length > 0 ? (primaryBlockIndex + 1) % floorBlockPalette.Length : 0;

        Vector2Int center = new Vector2Int(gridWidth / 2, gridLength / 2);

        // 2. Structural & Aesthetic Pattern Pass using Rectangular Grid Bounds
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                // Perimeter boundary forms the surrounding walls
                if (x == 0 || x == gridWidth - 1 || z == 0 || z == gridLength - 1)
                {
                    arenaGrid[x, z] = 2; // Wall boundary constraint
                }
                else
                {
                    arenaGrid[x, z] = 1; // Playable floor

                    float distanceFromCenter = Vector2.Distance(new Vector2(x, z), new Vector2(center.x, center.y));
                    floorVisualMatrix[x, z] = DeterminePatternBlock(x, z, distanceFromCenter, activePattern, primaryBlockIndex, secondaryBlockIndex);
                }
            }
        }

        // Place Entrance & Exit safely inside the inner playable arena bounds
        spawnPos = new Vector2Int(center.x, 1);
        exitPos = new Vector2Int(center.x, gridLength - 2);

        arenaGrid[spawnPos.x, spawnPos.y] = 3;
        arenaGrid[exitPos.x, exitPos.y] = 4;

        // 3. Path Validation Pass
        if (!IsCriticalPathValid(spawnPos, exitPos))
        {
            Debug.LogWarning("[PCG System] Layout Unreachable! Discarding & Regenerating...");
            GenerateFloor();
            return;
        }

        // 4. Hazard & Entity Placement Passes
        ApplyHazardPass(center);
        InstantiateVisuals();
    }

    private int DeterminePatternBlock(int x, int z, float dist, FloorPatternStyle style, int primary, int secondary)
    {
        if (floorBlockPalette == null || floorBlockPalette.Length <= 1) return 0;

        switch (style)
        {
            case FloorPatternStyle.Checkered:
                return ((x + z) % 2 == 0) ? primary : secondary;

            case FloorPatternStyle.ConcentricRings:
                return (Mathf.FloorToInt(dist) % 2 == 0) ? primary : secondary;

            case FloorPatternStyle.RandomScatter:
            default:
                return (Random.value < 0.25f) ? secondary : primary;
        }
    }

    private bool IsCriticalPathValid(Vector2Int start, Vector2Int end)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(start);
        visited.Add(start);

        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current == end) return true;

            foreach (var dir in directions)
            {
                Vector2Int neighbor = current + dir;
                if (neighbor.x >= 0 && neighbor.x < gridWidth && neighbor.y >= 0 && neighbor.y < gridLength)
                {
                    if (!visited.Contains(neighbor) && arenaGrid[neighbor.x, neighbor.y] != 2)
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
        return false;
    }

    private void ApplyHazardPass(Vector2Int center)
    {
        for (int x = 1; x < gridWidth - 1; x++)
        {
            for (int z = 1; z < gridLength - 1; z++)
            {
                if (arenaGrid[x, z] == 1 && Vector2Int.Distance(new Vector2Int(x, z), spawnPos) > 3f)
                {
                    // Random 5% chance to place a hazard block
                    if (Random.value < 0.05f)
                    {
                        arenaGrid[x, z] = 5;
                    }
                }
            }
        }
    }

    private void InstantiateVisuals()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                Vector3 targetPos = new Vector3(x, 0, z);

                switch (arenaGrid[x, z])
                {
                    case 1: // Open Floor
                        if (floorBlockPalette.Length > 0)
                        {
                            int blockIdx = floorVisualMatrix[x, z];
                            Instantiate(floorBlockPalette[blockIdx], targetPos, Quaternion.identity, transform);
                        }
                        break;

                    case 2: // Wall Boundary: stacks up to wallHeight
                        if (wallPrefab != null)
                        {
                            for (int y = 0; y < wallHeight; y++)
                            {
                                Instantiate(wallPrefab, targetPos + Vector3.up * y, Quaternion.identity, transform);
                            }
                        }
                        break;

                    case 3: // Entrance Elevator
                        if (floorBlockPalette.Length > 0)
                        {
                            int spawnFloorIdx = floorVisualMatrix[x, z];
                            Instantiate(floorBlockPalette[spawnFloorIdx], targetPos, Quaternion.identity, transform);
                        }
                        if (spawnElevatorPrefab != null)
                        {
                            Instantiate(spawnElevatorPrefab, targetPos + Vector3.up * 0.1f, Quaternion.identity, transform);
                        }
                        break;

                    case 4: // Exit Gate
                        if (floorBlockPalette.Length > 0)
                        {
                            int exitFloorIdx = floorVisualMatrix[x, z];
                            Instantiate(floorBlockPalette[exitFloorIdx], targetPos, Quaternion.identity, transform);
                        }
                        if (exitGatePrefab != null)
                        {
                            Instantiate(exitGatePrefab, targetPos + Vector3.up * 0.1f, Quaternion.identity, transform);
                        }
                        break;

                    case 5: // Hazard Block
                        if (magmaTrapPrefab != null)
                        {
                            Instantiate(magmaTrapPrefab, targetPos, Quaternion.identity, transform);
                        }
                        break;
                }
            }
        }
    }

    public void GoToNextFloor()
    {
        currentFloor++;
        GenerateFloor();
    }
}