using System;
using System.Collections.Generic;
using UnityEngine;

public class TowerGeneratorScript : MonoBehaviour
{
    [System.Serializable]
    public class BlockPatchData
    {
        public string blockName; // e.g., "Stone", "Dirt", "Nether Quartz Ore", "Blackstone"
        public GameObject blockPrefab;
        [Range(0.01f, 1f)]
        [Tooltip("How much of the floor this block patch covers (higher = larger/more frequent spots)")]
        public float patchCoverage = 0.25f;
        [Range(0.05f, 0.5f)]
        [Tooltip("Noise scale for this vein/spot (lower = big broad patches, higher = small scattered spots)")]
        public float veinScale = 0.18f;
    }

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
        public string structureName; // e.g., "Ore Cluster", "Cactus", "Oak Tree"
        public GameObject structurePrefab;
        [Range(1, 100)] public int spawnWeight = 10;
        [Tooltip("Vertical offset from the top surface of the block it spawns on (default 1.0 rests right on top)")]
        public float yOffset = 1.0f;
        [Tooltip("Allow random 90-degree Y rotations for visual variety")]
        public bool randomizeRotation = true;
    }

    [System.Serializable]
    public class BiomeTheme
    {
        public string themeName; // e.g., "Overworld", "Nether", "Desert"
        [Range(1, 100)] public int themeSelectionWeight = 10;

        [Header("Base & Sub-Surface Blocks")]
        [Tooltip("Main top surface block (e.g., Grass Block, Netherrack, Sand)")]
        public GameObject primarySurfaceBlock;
        [Tooltip("Block placed underneath raised terrain steps (e.g., Dirt under Grass, Netherrack, Sandstone). Falls back to primary if empty.")]
        public GameObject subSurfaceBlock;
        public GameObject wallPrefab;
        public GameObject hazardPrefab;

        [Header("Organic Block Spots / Veins")]
        [Tooltip("Different block spots mixed naturally into the floor (e.g., Stone, Dirt, Gold/Quartz Ore, Blackstone)")]
        public List<BlockPatchData> blockPatches = new List<BlockPatchData>();

        [Header("Terrain Elevation (Stepped Voxel Hills)")]
        [Range(0, 4)]
        [Tooltip("Max extra block layers stacked above the base floor (0 = flat, 2-3 = stepped hills like Minecraft)")]
        public int maxElevationHeight = 2;
        [Range(0.03f, 0.25f)]
        [Tooltip("Controls the size and shape of elevated terrain plateaus (lower = larger hills, higher = smaller mounds)")]
        public float terrainNoiseScale = 0.09f;
        [Range(0.2f, 0.8f)]
        [Tooltip("Threshold above which terrain rises (lower = more raised terrain, higher = mostly flat with islands)")]
        public float elevationThreshold = 0.48f;

        [Header("Thematic Structures (3 - 5 per floor)")]
        public List<StructureSpawnData> structurePool = new List<StructureSpawnData>();
        [Min(0)] public int minStructures = 3;
        [Min(0)] public int maxStructures = 5;

        [Header("Thematic Mob Pool")]
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
    // Stores the top block Y-level for every coordinate (0 = base floor, 1+ = elevated voxel steps)
    private int[,] heightMap;
    // Stores which prefab goes on the surface of each tile
    private GameObject[,] surfaceBlockMap;

    private Vector2Int spawnPos;
    private Vector2Int exitPos;
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

        // Clear previous layer objects safely in both Edit Mode and Play Mode
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
                Destroy(child);
            else
                DestroyImmediate(child);
        }

        // Select Biome Theme
        currentTheme = SelectBiomeTheme();
        if (currentTheme == null)
        {
            Debug.LogError("[PCG System] No Biome Themes configured in inspector!");
            return;
        }

        Debug.Log($"[PCG System] Floor {currentFloor} Theme: {currentTheme.themeName} (Seed: {floorSeed})");

        arenaGrid = new int[gridWidth, gridLength];
        heightMap = new int[gridWidth, gridLength];
        surfaceBlockMap = new GameObject[gridWidth, gridLength];

        // Random noise offsets per floor so every floor's hills and block patches have unique shapes
        Vector2 elevationOffset = new Vector2(UnityEngine.Random.Range(0f, 10000f), UnityEngine.Random.Range(0f, 10000f));
        Vector2[] patchOffsets = new Vector2[currentTheme.blockPatches.Count];
        for (int i = 0; i < patchOffsets.Length; i++)
        {
            patchOffsets[i] = new Vector2(UnityEngine.Random.Range(0f, 10000f), UnityEngine.Random.Range(0f, 10000f));
        }

        spawnPos = new Vector2Int(gridWidth / 2, 1);
        exitPos = new Vector2Int(gridWidth / 2, gridLength - 2);

        // 2. Organic Voxel Terrain & Block Patch Pass
        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                if (x == 0 || x == gridWidth - 1 || z == 0 || z == gridLength - 1)
                {
                    arenaGrid[x, z] = 2; // Perimeter wall
                    heightMap[x, z] = 0;
                }
                else
                {
                    arenaGrid[x, z] = 1; // Playable terrain

                    // Calculate stepped voxel elevation using Perlin noise
                    heightMap[x, z] = CalculateTerrainHeight(x, z, elevationOffset);

                    // Determine surface block (primary block vs organic spots of secondary blocks)
                    surfaceBlockMap[x, z] = DetermineSurfaceBlock(x, z, patchOffsets);
                }
            }
        }

        // Flatten a small 3x3 pad around Spawn and Exit so the elevator & gate sit cleanly
        FlattenAreaAround(spawnPos, 1, heightMap[spawnPos.x, spawnPos.y]);
        FlattenAreaAround(exitPos, 1, heightMap[exitPos.x, exitPos.y]);

        arenaGrid[spawnPos.x, spawnPos.y] = 3;
        arenaGrid[exitPos.x, exitPos.y] = 4;

        // 3. Hazards & Thematic Structures Placement
        ApplyHazardPass();
        ApplyStructurePass();

        // 4. Navigable Path Validation (ensures max 1-block step-ups and unblocked route)
        if (!IsCriticalPathValid(spawnPos, exitPos))
        {
            Debug.LogWarning("[PCG System] Terrain cliffs or structures blocked path! Regenerating...");
            towerSeed++; // Shift seed slightly to guarantee a fresh layout
            GenerateFloor();
            return;
        }

        // 5. Instantiate Voxel Blocks, Elevated Layers, Structures & Mobs
        InstantiateVisuals();
        ApplyMobSpawningPass();
    }

    private int CalculateTerrainHeight(int x, int z, Vector2 noiseOffset)
    {
        if (currentTheme.maxElevationHeight <= 0) return 0;

        float sampleX = (x * currentTheme.terrainNoiseScale) + noiseOffset.x;
        float sampleZ = (z * currentTheme.terrainNoiseScale) + noiseOffset.y;

        // Combine primary shape noise with subtle detail noise for natural irregular edges
        float primaryNoise = Mathf.PerlinNoise(sampleX, sampleZ);
        float detailNoise = Mathf.PerlinNoise(sampleX * 2.2f + 31.7f, sampleZ * 2.2f + 17.3f) * 0.2f;
        float combined = Mathf.Clamp01(primaryNoise * 0.85f + detailNoise);

        if (combined < currentTheme.elevationThreshold)
            return 0;

        // Map remaining noise range [elevationThreshold .. 1.0] into discrete integer block heights [1 .. maxElevationHeight]
        float normalizedHeight = Mathf.InverseLerp(currentTheme.elevationThreshold, 0.92f, combined);
        int blockSteps = Mathf.CeilToInt(normalizedHeight * currentTheme.maxElevationHeight);
        return Mathf.Clamp(blockSteps, 0, currentTheme.maxElevationHeight);
    }

    private GameObject DetermineSurfaceBlock(int x, int z, Vector2[] patchOffsets)
    {
        if (currentTheme.blockPatches != null)
        {
            for (int i = 0; i < currentTheme.blockPatches.Count; i++)
            {
                BlockPatchData patch = currentTheme.blockPatches[i];
                if (patch.blockPrefab == null) continue;

                float nx = (x * patch.veinScale) + patchOffsets[i].x;
                float nz = (z * patch.veinScale) + patchOffsets[i].y;
                float noiseVal = Mathf.PerlinNoise(nx, nz);

                // Higher patchCoverage lowers the threshold so larger/more frequent spots appear
                float threshold = 1f - patch.patchCoverage;
                if (noiseVal >= threshold)
                {
                    return patch.blockPrefab;
                }
            }
        }

        return currentTheme.primarySurfaceBlock;
    }

    private void FlattenAreaAround(Vector2Int center, int radius, int targetHeight)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                int nx = center.x + dx;
                int nz = center.y + dz;
                if (nx > 0 && nx < gridWidth - 1 && nz > 0 && nz < gridLength - 1)
                {
                    heightMap[nx, nz] = targetHeight;
                }
            }
        }
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
                return theme;
        }

        return biomeThemes[0];
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

            int currentHeight = heightMap[current.x, current.y];

            foreach (var dir in directions)
            {
                Vector2Int neighbor = current + dir;
                if (neighbor.x >= 0 && neighbor.x < gridWidth && neighbor.y >= 0 && neighbor.y < gridLength)
                {
                    int tile = arenaGrid[neighbor.x, neighbor.y];
                    int neighborHeight = heightMap[neighbor.x, neighbor.y];

                    // Walkable if not a wall (2) or structure (6) AND elevation step is <= 1 block high
                    if (!visited.Contains(neighbor) &&
                        tile != 2 &&
                        tile != 6 &&
                        Mathf.Abs(neighborHeight - currentHeight) <= 1)
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
                totalStructureWeight += Mathf.Max(1, s.spawnWeight);
        }

        if (totalStructureWeight == 0) return;

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

        int minCount = Mathf.Min(currentTheme.minStructures, currentTheme.maxStructures);
        int maxCount = Mathf.Max(currentTheme.minStructures, currentTheme.maxStructures);
        int targetStructures = UnityEngine.Random.Range(minCount, maxCount + 1);

        List<Vector2Int> placedPositions = new List<Vector2Int>();

        while (totalStructuresSpawned < targetStructures && validSpots.Count > 0)
        {
            int randIndex = UnityEngine.Random.Range(0, validSpots.Count);
            Vector2Int candidate = validSpots[randIndex];
            validSpots.RemoveAt(randIndex);

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
                arenaGrid[candidate.x, candidate.y] = 6;
                placedPositions.Add(candidate);

                // Spawn on top of the terrain's exact elevation at (candidate.x, candidate.y)
                int surfaceY = heightMap[candidate.x, candidate.y];
                Vector3 spawnWorldPos = new Vector3(candidate.x, surfaceY + selectedStructure.yOffset, candidate.y);
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
                return structure;
        }

        return null;
    }

    private void InstantiateVisuals()
    {
        GameObject fillerBlock = currentTheme.subSurfaceBlock != null
            ? currentTheme.subSurfaceBlock
            : currentTheme.primarySurfaceBlock;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int z = 0; z < gridLength; z++)
            {
                int surfaceY = heightMap[x, z];

                switch (arenaGrid[x, z])
                {
                    case 1: // Playable Voxel Terrain
                    case 6: // Terrain underneath a Structure
                        SpawnTerrainColumn(x, z, surfaceY, surfaceBlockMap[x, z], fillerBlock);
                        break;

                    case 2: // Perimeter Wall Boundary
                        if (currentTheme.wallPrefab != null)
                        {
                            for (int y = 0; y < wallHeight; y++)
                            {
                                Instantiate(currentTheme.wallPrefab, new Vector3(x, y, z), Quaternion.identity, transform);
                            }
                        }
                        break;

                    case 3: // Entrance Elevator
                        SpawnTerrainColumn(x, z, surfaceY, surfaceBlockMap[x, z], fillerBlock);
                        if (spawnElevatorPrefab != null)
                        {
                            Vector3 elevatorPos = new Vector3(x, surfaceY + 1.0f, z);
                            Quaternion elevatorRot = Quaternion.Euler(spawnElevatorRotationOffset);
                            Instantiate(spawnElevatorPrefab, elevatorPos, elevatorRot, transform);
                        }
                        break;

                    case 4: // Exit Gateway
                        SpawnTerrainColumn(x, z, surfaceY, surfaceBlockMap[x, z], fillerBlock);
                        if (exitGatePrefab != null)
                        {
                            Vector3 gatePos = new Vector3(x, surfaceY + 1.0f, z);
                            Quaternion gateRot = Quaternion.Euler(exitGateRotationOffset);
                            Instantiate(exitGatePrefab, gatePos, gateRot, transform);
                        }
                        break;

                    case 5: // Thematic Hazard (replaces top block at current terrain height)
                        GameObject hazardTop = currentTheme.hazardPrefab != null
                            ? currentTheme.hazardPrefab
                            : surfaceBlockMap[x, z];
                        SpawnTerrainColumn(x, z, surfaceY, hazardTop, fillerBlock);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Stacks filler blocks from Y=0 up to surfaceY-1, then places the top surface block at Y=surfaceY.
    /// Ensures raised terrain plateaus are solid 1x1x1 voxel columns with no gaps.
    /// </summary>
    private void SpawnTerrainColumn(int x, int z, int surfaceY, GameObject topBlock, GameObject underBlock)
    {
        if (underBlock != null)
        {
            for (int y = 0; y < surfaceY; y++)
            {
                Instantiate(underBlock, new Vector3(x, y, z), Quaternion.identity, transform);
            }
        }

        if (topBlock != null)
        {
            Instantiate(topBlock, new Vector3(x, surfaceY, z), Quaternion.identity, transform);
        }
    }

    private void ApplyMobSpawningPass()
    {
        if (currentTheme.mobPool == null || currentTheme.mobPool.Count == 0) return;

        int totalMobWeight = 0;
        foreach (var mob in currentTheme.mobPool)
        {
            if (mob.mobPrefab != null)
                totalMobWeight += Mathf.Max(1, mob.spawnWeight);
        }

        if (totalMobWeight == 0) return;

        for (int x = 1; x < gridWidth - 1; x++)
        {
            for (int z = 1; z < gridLength - 1; z++)
            {
                if (arenaGrid[x, z] == 1 && Vector2Int.Distance(new Vector2Int(x, z), spawnPos) > 4f)
                {
                    if (UnityEngine.Random.value < mobSpawnRate)
                    {
                        GameObject selectedMobPrefab = PickWeightedMob(totalMobWeight);
                        if (selectedMobPrefab != null)
                        {
                            int surfaceY = heightMap[x, z];
                            Vector3 spawnWorldPos = new Vector3(x, surfaceY + 1.0f, z);
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
                return mob.mobPrefab;
        }

        return null;
    }

    public void GoToNextFloor()
    {
        currentFloor++;
        GenerateFloor();
    }
}