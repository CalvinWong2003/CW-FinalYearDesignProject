using System;
using System.Collections.Generic;
using UnityEngine;

public class TowerGeneratorScript : MonoBehaviour
{
    public enum FloorPatternStyle { RandomScatter, Checkered, ConcentricRings }

    [System.Serializable]
    public class MobSpawnData
    {
        public string mobName;
        public GameObject mobPrefab;
        [Range(1, 100)] public int spawnWeight = 10;
    }

    [System.Serializable]
    public class BiomeTheme
    {
        public string themeName; // e.g., "Desert", "Overworld", "Nether", "End"
        [Range(1, 100)] public int themeSelectionWeight = 10;

        [Header("Block & Visual Palette")]
        [Tooltip("Add the thematic blocks here (e.g. Desert: Sand, Sandstone, Smooth Sandstone)")]
        public GameObject[] floorBlocks;
        public GameObject wallPrefab;
        public GameObject hazardPrefab; // e.g., Magma Block for Nether, Cactus/Spike for Desert

        [Header("Thematic Mob Pool")]
        [Tooltip("The mobs eligible to spawn on this specific biome floor")]
        public List<MobSpawnData> mobPool = new List<MobSpawnData>();
    }

    [Header("Infrastructure & Seeds")]
    [Tooltip("Leave at 0 for a completely random seed every time.")]
    public int towerSeed = 0;
    public int currentFloor = 1;

    [Header("Arena Dimensions")]
    [Min(3)] public int gridWidth = 30;
    [Min(3)] public int gridLength = 30;

    [Header("Wall Settings")]
    [Range(1, 10)] public int wallHeight = 10;

    [Header("Biome Theming System")]
    [Tooltip("Define multiple biomes with their own block palettes, walls, and mob rosters")]
    public List<BiomeTheme> biomeThemes = new List<BiomeTheme>();

    [Header("Elevator & Exit Gateways")]
    public GameObject spawnElevatorPrefab;
    public Vector3 spawnElevatorRotationOffset = new Vector3(0, 180f, 0); // Corrects prefab orientation
    public GameObject exitGatePrefab;
    public Vector3 exitGateRotationOffset = Vector3.zero;

    [Header("Mob & Hazard Density")]
    [Range(0f, 0.2f)] public float hazardSpawnRate = 0.04f;
    [Range(0f, 0.2f)] public float mobSpawnRate = 0.05f;
    public int totalMobsSpawned;

    // Internal Grid: 0 = Empty, 1 = Floor, 2 = Wall, 3 = Spawn, 4 = Exit, 5 = Hazard
    private int[,] arenaGrid;
    private Vector2Int spawnPos;
    private Vector2Int exitPos;

    // Tracks which specific block from the active theme's palette goes onto each grid cell
    private int[,] floorVisualMatrix;
    private BiomeTheme currentTheme;

    private void Start()
    {
        GenerateFloor();
    }

    [ContextMenu("Generate Floor")]
    public void GenerateFloor()
    {
        // 1. Deterministic Seed Setup
        if (towerSeed == 0) towerSeed = UnityEngine.Random.Range(100000, 999999);
        int floorSeed = towerSeed + currentFloor;
        UnityEngine.Random.InitState(floorSeed);

        totalMobsSpawned = 0;

        // Clear previous layer objects (handles both Editor ContextMenu and runtime)
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

        // Select Biome Theme by weighted chance
        currentTheme = SelectBiomeTheme();
        if (currentTheme == null)
        {
            Debug.LogError("[PCG System] No Biome Themes configured in inspector!");
            return;
        }

        Debug.Log($"[PCG System] Floor {currentFloor} selected Theme: {currentTheme.themeName} (Seed: {floorSeed})");

        arenaGrid = new int[gridWidth, gridLength];
        floorVisualMatrix = new int[gridWidth, gridLength];

        FloorPatternStyle activePattern = (FloorPatternStyle)UnityEngine.Random.Range(0, Enum.GetValues(typeof(FloorPatternStyle)).Length);

        int primaryBlockIndex = (currentTheme.floorBlocks != null && currentTheme.floorBlocks.Length > 0)
            ? UnityEngine.Random.Range(0, currentTheme.floorBlocks.Length)
            : 0;

        int secondaryBlockIndex = (currentTheme.floorBlocks != null && currentTheme.floorBlocks.Length > 0)
            ? (primaryBlockIndex + 1) % currentTheme.floorBlocks.Length
            : 0;

        Vector2Int center = new Vector2Int(gridWidth / 2, gridLength / 2);

        // 2. Structural & Aesthetic Pass
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                if (x == 0 || x == gridWidth - 1 || z == 0 || z == gridLength - 1)
                {
                    arenaGrid[x, z] = 2; // Wall boundary
                }
                else
                {
                    arenaGrid[x, z] = 1; // Playable floor
                    float dist = Vector2.Distance(new Vector2(x, z), new Vector2(center.x, center.y));
                    floorVisualMatrix[x, z] = DeterminePatternBlock(x, z, dist, activePattern, primaryBlockIndex, secondaryBlockIndex);
                }
            }
        }

        // Spawn and Exit positions
        spawnPos = new Vector2Int(center.x, 1);
        exitPos = new Vector2Int(center.x, gridLength - 2);

        arenaGrid[spawnPos.x, spawnPos.y] = 3;
        arenaGrid[exitPos.x, exitPos.y] = 4;

        // 3. Path Validation
        if (!IsCriticalPathValid(spawnPos, exitPos))
        {
            Debug.LogWarning("[PCG System] Critical path invalid! Regenerating...");
            GenerateFloor();
            return;
        }

        // 4. Hazards, Spawns & Geometry Generation
        ApplyHazardPass();
        InstantiateVisuals();
        ApplyMobSpawningPass();
    }

    private BiomeTheme SelectBiomeTheme()
    {
        if (biomeThemes == null || biomeThemes.Count == 0) return null;

        int totalWeight = 0;
        foreach (var theme in biomeThemes) totalWeight += Mathf.Max(1, theme.themeSelectionWeight);

        int roll = UnityEngine.Random.Range(0, totalWeight);
        int runningWeight = 0;

        foreach (var theme in biomeThemes)
        {
            runningWeight += Mathf.Max(1, theme.themeSelectionWeight);
            if (roll < runningWeight)
            {
                return theme;
            }
        }

        return biomeThemes[0];
    }

    private int DeterminePatternBlock(int x, int z, float dist, FloorPatternStyle style, int primary, int secondary)
    {
        if (currentTheme.floorBlocks == null || currentTheme.floorBlocks.Length <= 1) return 0;

        switch (style)
        {
            case FloorPatternStyle.Checkered:
                return ((x + z) % 2 == 0) ? primary : secondary;

            case FloorPatternStyle.ConcentricRings:
                return (Mathf.FloorToInt(dist) % 2 == 0) ? primary : secondary;

            case FloorPatternStyle.RandomScatter:
            default:
                return (UnityEngine.Random.value < 0.25f) ? secondary : primary;
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

    private void ApplyHazardPass()
    {
        for (int x = 1; x < gridWidth - 1; x++)
        {
            for (int z = 1; z < gridLength - 1; z++)
            {
                if (arenaGrid[x, z] == 1 &&
                    Vector2Int.Distance(new Vector2Int(x, z), spawnPos) > 3f &&
                    Vector2Int.Distance(new Vector2Int(x, z), exitPos) > 3f)
                {
                    if (UnityEngine.Random.value < hazardSpawnRate)
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
                    case 1: // Base Thematic Floor Block
                        SpawnFloorTile(x, z, targetPos);
                        break;

                    case 2: // Wall Stacking (up to wallHeight)
                        GameObject wallPrefabToUse = currentTheme.wallPrefab != null ? currentTheme.wallPrefab : null;
                        if (wallPrefabToUse != null)
                        {
                            for (int y = 0; y < wallHeight; y++)
                            {
                                Instantiate(wallPrefabToUse, targetPos + (Vector3.up * y), Quaternion.identity, transform);
                            }
                        }
                        break;

                    case 3: // Entrance Elevator
                        SpawnFloorTile(x, z, targetPos);
                        if (spawnElevatorPrefab != null)
                        {
                            // Spawned 1 full block above floor with explicit rotation override
                            Vector3 elevatorPos = targetPos + Vector3.up * 1.0f;
                            Quaternion elevatorRot = Quaternion.Euler(spawnElevatorRotationOffset);
                            Instantiate(spawnElevatorPrefab, elevatorPos, elevatorRot, transform);
                        }
                        break;

                    case 4: // Exit Gateway
                        SpawnFloorTile(x, z, targetPos);
                        if (exitGatePrefab != null)
                        {
                            // Spawned 1 full block above floor
                            Vector3 gatePos = targetPos + Vector3.up * 1.0f;
                            Quaternion gateRot = Quaternion.Euler(exitGateRotationOffset);
                            Instantiate(exitGatePrefab, gatePos, gateRot, transform);
                        }
                        break;

                    case 5: // Thematic Hazard
                        GameObject hazardToUse = currentTheme.hazardPrefab != null ? currentTheme.hazardPrefab : null;
                        if (hazardToUse != null)
                        {
                            Instantiate(hazardToUse, targetPos, Quaternion.identity, transform);
                        }
                        else
                        {
                            SpawnFloorTile(x, z, targetPos);
                        }
                        break;
                }
            }
        }
    }

    private void SpawnFloorTile(int x, int z, Vector3 pos)
    {
        if (currentTheme.floorBlocks != null && currentTheme.floorBlocks.Length > 0)
        {
            int blockIdx = floorVisualMatrix[x, z];
            Instantiate(currentTheme.floorBlocks[blockIdx], pos, Quaternion.identity, transform);
        }
    }

    private void ApplyMobSpawningPass()
    {
        if (currentTheme.mobPool == null || currentTheme.mobPool.Count == 0) return;

        // Calculate total weight of all mobs in this theme's pool
        int totalMobWeight = 0;
        foreach (var mob in currentTheme.mobPool)
        {
            if (mob.mobPrefab != null)
            {
                totalMobWeight += Mathf.Max(1, mob.spawnWeight);
            }
        }

        if (totalMobWeight == 0) return;

        for (int x = 1; x < gridWidth - 1; x++)
        {
            for (int z = 1; z < gridLength - 1; z++)
            {
                // Only spawn mobs on playable floors away from the elevator
                if (arenaGrid[x, z] == 1 && Vector2Int.Distance(new Vector2Int(x, z), spawnPos) > 4f)
                {
                    if (UnityEngine.Random.value < mobSpawnRate)
                    {
                        GameObject selectedMobPrefab = PickWeightedMob(totalMobWeight);
                        if (selectedMobPrefab != null)
                        {
                            // Spawn entity resting on the floor (Y = 1.0)
                            Vector3 spawnWorldPos = new Vector3(x, 1.0f, z);
                            GameObject mobObj = Instantiate(selectedMobPrefab, spawnWorldPos, Quaternion.identity, transform);
                            mobObj.name = $"{selectedMobPrefab.name}_Floor{currentFloor}_{totalMobsSpawned}";
                            totalMobsSpawned++;
                        }
                    }
                }
            }
        }
    }

    private GameObject PickWeightedMob(int totalWeight)
    {
        int roll = UnityEngine.Random.Range(0, totalWeight);
        int running = 0;

        foreach (var mob in currentTheme.mobPool)
        {
            if (mob.mobPrefab == null) continue;

            running += Mathf.Max(1, mob.spawnWeight);
            if (roll < running)
            {
                return mob.mobPrefab;
            }
        }

        return null;
    }

    public void GoToNextFloor()
    {
        currentFloor++;
        GenerateFloor();
    }
}