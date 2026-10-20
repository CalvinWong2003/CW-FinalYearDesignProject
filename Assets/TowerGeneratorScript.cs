using System.Collections.Generic;
using UnityEngine;

public class TowerGeneratorScript : MonoBehaviour
{
    // Define the visual thematic patterns available for the floor
    public enum FloorPatternStyle { RandomScatter, Checkered, ConcentricRings }

    [Header("Infrastructure & Seeds")]
    [Tooltip("Leave at 0 for a completely random seed every time.")]
    public int towerSeed = 0;
    public int currentFloor = 1;

    [Header("Arena Dimensions")]
    [Tooltip("Radius of the playable circular floor.")]
    public int towerRadius = 15;

    [Range(1, 10)]
    [Tooltip("Height of the surrounding perimeter walls in blocks (up to 10).")]
    public int wallHeight = 10;

    [Tooltip("Thickness of the outer boundary wall ring.")]
    [Range(1, 4)]
    public int wallThickness = 2;

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

    // Internal Grid: 0 = Void, 1 = Floor, 2 = Wall, 3 = Spawn, 4 = Exit, 5 = Hazard
    private int[,] arenaGrid;
    private int currentGridSize;
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

        // Clear previous layer visuals
        foreach (Transform child in transform) { Destroy(child.gameObject); }

        // Grid size is derived solely from tower radius + wall boundary padding
        currentGridSize = (towerRadius + wallThickness) * 2 + 1;
        arenaGrid = new int[currentGridSize, currentGridSize];
        floorVisualMatrix = new int[currentGridSize, currentGridSize];

        // Pick a floor architectural style based entirely on the floor seed
        FloorPatternStyle activePattern = (FloorPatternStyle)Random.Range(0, System.Enum.GetValues(typeof(FloorPatternStyle)).Length);
        Debug.Log($"[PCG System] Selected Aesthetic Pattern for Floor {currentFloor}: {activePattern}");

        // Select primary and secondary accent blocks from the palette for this floor run
        int primaryBlockIndex = Random.Range(0, floorBlockPalette.Length);
        int secondaryBlockIndex = (primaryBlockIndex + 1) % floorBlockPalette.Length;

        // Exact mathematical center of the grid
        Vector2Int center = new Vector2Int(towerRadius + wallThickness, towerRadius + wallThickness);

        // 2. Structural & Aesthetic Pattern Pass
        for (int x = 0; x < currentGridSize; x++)
        {
            for (int z = 0; z < currentGridSize; z++)
            {
                float distanceFromCenter = Vector2.Distance(new Vector2(x, z), new Vector2(center.x, center.y));

                if (distanceFromCenter <= towerRadius)
                {
                    arenaGrid[x, z] = 1; // Playable floor
                    floorVisualMatrix[x, z] = DeterminePatternBlock(x, z, distanceFromCenter, activePattern, primaryBlockIndex, secondaryBlockIndex);
                }
                else if (distanceFromCenter <= towerRadius + wallThickness)
                {
                    arenaGrid[x, z] = 2; // Outer wall perimeter ring
                }
                else
                {
                    arenaGrid[x, z] = 0; // Empty void outside the arena
                }
            }
        }

        // Place Entrance & Exit safely inside the circular floor boundary
        spawnPos = new Vector2Int(center.x, center.y - (towerRadius - 2));
        exitPos = new Vector2Int(center.x, center.y + (towerRadius - 2));
        arenaGrid[spawnPos.x, spawnPos.y] = 3;
        arenaGrid[exitPos.x, exitPos.y] = 4;

        // 3. Path Validation Pass
        if (!IsCriticalPathValid(spawnPos, exitPos))
        {
            Debug.LogWarning("[PCG System] Layout Unreachable! Discarding & Regenerating...");
            GenerateFloor();
            return;
        }

        // 4. Hazard & Visual Placement Passes
        ApplyHazardPass();
        InstantiateVisuals();
    }

    private int DeterminePatternBlock(int x, int z, float dist, FloorPatternStyle style, int primary, int secondary)
    {
        if (floorBlockPalette.Length <= 1) return 0;

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
                if (neighbor.x >= 0 && neighbor.x < currentGridSize && neighbor.y >= 0 && neighbor.y < currentGridSize)
                {
                    if (!visited.Contains(neighbor) && arenaGrid[neighbor.x, neighbor.y] != 2 && arenaGrid[neighbor.x, neighbor.y] != 0)
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
        return false;
    }

    private void ApplyHazardPass()
    {
        for (int x = 0; x < currentGridSize; x++)
        {
            for (int z = 0; z < currentGridSize; z++)
            {
                if (arenaGrid[x, z] == 1 && Vector2Int.Distance(new Vector2Int(x, z), spawnPos) > 3f)
                {
                    // 5% chance to place a hazard on open floor blocks away from spawn
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
        for (int x = 0; x < currentGridSize; x++)
        {
            for (int z = 0; z < currentGridSize; z++)
            {
                // Centers the generated circle directly over the GameObject's local origin (0, 0, 0)
                Vector3 targetPos = new Vector3(x - (currentGridSize / 2f), 0, z - (currentGridSize / 2f));

                switch (arenaGrid[x, z])
                {
                    case 1: // Open Floor
                        int blockIdx = floorVisualMatrix[x, z];
                        Instantiate(floorBlockPalette[blockIdx], targetPos, Quaternion.identity, transform);
                        break;

                    case 2: // Wall Boundary (Stacked vertically up to wallHeight)
                        for (int y = 0; y < wallHeight; y++)
                        {
                            Vector3 wallBlockPos = targetPos + (Vector3.up * y);
                            Instantiate(wallPrefab, wallBlockPos, Quaternion.identity, transform);
                        }
                        break;

                    case 3: // Entrance Elevator
                        int spawnFloorIdx = floorVisualMatrix[x, z];
                        Instantiate(floorBlockPalette[spawnFloorIdx], targetPos, Quaternion.identity, transform);
                        Instantiate(spawnElevatorPrefab, targetPos + Vector3.up * 0.1f, Quaternion.identity, transform);
                        break;

                    case 4: // Exit Gate
                        int exitFloorIdx = floorVisualMatrix[x, z];
                        Instantiate(floorBlockPalette[exitFloorIdx], targetPos, Quaternion.identity, transform);
                        Instantiate(exitGatePrefab, targetPos + Vector3.up * 0.1f, Quaternion.identity, transform);
                        break;

                    case 5: // Hazard Block
                        Instantiate(magmaTrapPrefab, targetPos, Quaternion.identity, transform);
                        break;

                    case 0: // Void / Empty space
                    default:
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