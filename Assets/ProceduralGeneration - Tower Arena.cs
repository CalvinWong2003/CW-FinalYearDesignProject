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
    public class StructureSpawnData
    {
        public string structureName; // e.g., "Ore Cluster", "Cactus", "Ruined Pillar"
        public GameObject structurePrefab;
        [Range(1, 100)] public int spawnWeight = 10;
        [Tooltip("Vertical offset from the top of the floor block (default 1.0 rests on top of floor)")]
        public float yOffset = 1.0f;
        [Tooltip("Allow random 90-degree Y rotations for visual variety")]
        public bool randomizeRotation = true;
    }

    [System.Serializable]
    public class BiomeTheme
    {
        public string themeName; // e.g., "Desert", "Overworld", "Nether", "Deep Dark"
        [Range(1, 100)] public int themeSelectionWeight = 10;

        [Header("Block & Visual Palette")]
        [Tooltip("Add thematic blocks here (e.g. Desert: Sand, Sandstone, Smooth Sandstone)")]
        public GameObject[] floorBlocks;
        public GameObject wallPrefab;
        public GameObject hazardPrefab; // e.g., Magma Block for Nether, Quicksand/Trap for Desert

        [Header("Thematic Structures (3 - 5 per floor)")]
        [Tooltip("Prefabs such as ore clusters, cacti, pillars, or crystal formations")]
        public List<StructureSpawnData> structurePool = new List<StructureSpawnData>();
        [Min(0)] public int minStructures = 3;
        [Min(0)] public int maxStructures = 5;

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
    [Tooltip("Define multiple biomes with their own block palettes, walls, structures, and mob rosters")]
    public List<BiomeTheme> biomeThemes = new List<BiomeTheme>();

    [Header("Elevator & Exit Gateways")]
    public GameObject spawnElevatorPrefab;
    public Vector3 spawnElevatorRotationOffset = new Vector3(0, 180f, 0);
    public GameObject exitGatePrefab;
    public Vector3 exitGateRotationOffset = Vector3.zero;

    [Header("Mob & Hazard Density")]
    [Range(0f, 0.2f)] public float hazardSpawnRate = 0.04f;
    [Range(0f, 0.2f)] public float mobSpawnRate = 0.05f;
    public int totalStructuresSpawned;
    public int totalMobsSpawned;

    // Internal Grid: 0 = Empty, 1 = Floor, 2 = Wall, 3 = Spawn, 4 = Exit, 5 = Hazard, 6 = Structure
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
        totalStructuresSpawned = 0;

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

        // 3. Hazards & Thematic Structures Placement
        ApplyHazardPass();
        ApplyStructurePass();

        // 4. Path Validation (ensures structures don't wall off the exit)
        if (!IsCriticalPathValid(spawnPos, exitPos))
        {
            Debug.LogWarning("[PCG System] Critical path blocked by structures! Regenerating...");
            GenerateFloor();
            return;
        }

        // 5. Geometry & Entity Instantiation
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
                    int tile = arenaGrid[neighbor.x, neighbor.y];
                    // Walls (2) and Structures (6) act as solid obstacles
                    if (!visited.Contains(neighbor) && tile != 2 && tile != 6)
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

    private void ApplyStructurePass()
    {
        if (currentTheme.structurePool == null || currentTheme.structurePool.Count == 0) return;

        int totalStructureWeight = 0;
        foreach (var s in currentTheme.structurePool)
        {
            if (s.structurePrefab != null)
            {
                totalStructureWeight += Mathf.Max(1, s.spawnWeight);
            }
        }

        if (totalStructureWeight == 0) return;

        // Gather all valid interior floor coordinates (1 tile buffer from outer walls, away from spawn/exit)
        List<Vector2Int> validSpots = new List<Vector2Int>();
        for (int x = 2; x < gridWidth - 2; x++)
        {
            for (int z = 2; z < gridLength - 2; z++)
            {
                if (arenaGrid[x, z] == 1 &&
                    Vector2Int.Distance(new Vector2Int(x, z), spawnPos) > 3.5f &&
                    Vector2Int.Distance(new Vector2Int(x, z), exitPos) > 3.5f)
                {
                    validSpots.Add(new Vector2Int(x, z));
                }
            }
        }

        // Determine target count (between 3 and 5 by default)
        int minCount = Mathf.Min(currentTheme.minStructures, currentTheme.maxStructures);
        int maxCount = Mathf.Max(currentTheme.minStructures, currentTheme.maxStructures);
        int targetStructures = UnityEngine.Random.Range(minCount, maxCount + 1);

        List<Vector2Int> placedPositions = new List<Vector2Int>();

        while (totalStructuresSpawned < targetStructures && validSpots.Count > 0)
        {
            int randIndex = UnityEngine.Random.Range(0, validSpots.Count);
            Vector2Int candidate = validSpots[randIndex];
            validSpots.RemoveAt(randIndex);

            // Enforce minimum 3-block spacing between structures so they don't clump awkwardly
            bool tooClose = false;
            foreach (var placed in placedPositions)
            {
                if (Vector2Int.Distance(candidate, placed) < 3f)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose) continue;

            StructureSpawnData selectedStructure = PickWeightedStructure(totalStructureWeight);
            if (selectedStructure != null && selectedStructure.structurePrefab != null)
            {
                arenaGrid[candidate.x, candidate.y] = 6; // Mark cell as occupied by a structure
                placedPositions.Add(candidate);

                Vector3 spawnWorldPos = new Vector3(candidate.x, selectedStructure.yOffset, candidate.y);
                Quaternion rot = selectedStructure.randomizeRotation
                    ? Quaternion.Euler(0f, UnityEngine.Random.Range(0, 4) * 90f, 0f)
                    : Quaternion.identity;

                GameObject structObj = Instantiate(selectedStructure.structurePrefab, spawnWorldPos, rot, transform);
                structObj.name = $"{selectedStructure.structurePrefab.name}_{currentTheme.themeName}_{totalStructuresSpawned}";
                totalStructuresSpawned++;
            }
        }
    }

    private StructureSpawnData PickWeightedStructure(int totalWeight)
    {
        int roll = UnityEngine.Random.Range(0, totalWeight);
        int running = 0;

        foreach (var structure in currentTheme.structurePool)
        {
            if (structure.structurePrefab == null) continue;

            running += Mathf.Max(1, structure.spawnWeight);
            if (roll < running)
            {
                return structure;
            }
        }

        return null;
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
                    case 6: // Floor Block underneath a Structure
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
                            Vector3 elevatorPos = targetPos + Vector3.up * 1.0f;
                            Quaternion elevatorRot = Quaternion.Euler(spawnElevatorRotationOffset);
                            Instantiate(spawnElevatorPrefab, elevatorPos, elevatorRot, transform);
                        }
                        break;

                    case 4: // Exit Gateway
                        SpawnFloorTile(x, z, targetPos);
                        if (exitGatePrefab != null)
                        {
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
                // Only spawn mobs on open playable floor tiles (arenaGrid == 1), avoiding structures (6) and hazards (5)
                if (arenaGrid[x, z] == 1 && Vector2Int.Distance(new Vector2Int(x, z), spawnPos) > 4f)
                {
                    if (UnityEngine.Random.value < mobSpawnRate)
                    {
                        GameObject selectedMobPrefab = PickWeightedMob(totalMobWeight);
                        if (selectedMobPrefab != null)
                        {
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