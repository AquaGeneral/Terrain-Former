using System;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    public static class Utilities {
        internal static readonly int ignoreRaycastLayerMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        // Cached vectors, using Vector3.down/up alone allocates a new struct every time it's used in older versions of Unity!
        internal static readonly Vector3 downDirection = Vector3.down; 
        internal static readonly Vector3 upDirection = Vector3.up;

        internal static GameObject DuplicateTerrainGameObject(string name, Terrain sourceTerrain, TerrainData sourceTerrainData) {
            GameObject destinationTerrainGameObject = Terrain.CreateTerrainGameObject(null);
            destinationTerrainGameObject.name = name;

            if(name == sourceTerrain.name) destinationTerrainGameObject.name += " (Copy)";

            Terrain destinationTerrain = destinationTerrainGameObject.GetComponent<Terrain>();
            TerrainCollider terrainCollider = destinationTerrainGameObject.GetComponent<TerrainCollider>();
            TerrainData duplicatedTerrainData = DuplicateTerrainData(sourceTerrainData);

            // Base Terrain
            destinationTerrain.drawHeightmap = sourceTerrain.drawHeightmap;
            destinationTerrain.heightmapPixelError = sourceTerrain.heightmapPixelError;
            destinationTerrain.basemapDistance = sourceTerrain.basemapDistance;
#if UNITY_2019_1_OR_NEWER
            destinationTerrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
#else
            destinationTerrain.castShadows = sourceTerrain.castShadows;
#endif
#if !UNITY_2019_2_OR_NEWER
            destinationTerrain.materialType = sourceTerrain.materialType;
#endif
            destinationTerrain.reflectionProbeUsage = sourceTerrain.reflectionProbeUsage;
            
            // Tree & Detail Objects
            destinationTerrain.drawTreesAndFoliage = sourceTerrain.drawTreesAndFoliage;
            destinationTerrain.bakeLightProbesForTrees = sourceTerrain.bakeLightProbesForTrees;
            destinationTerrain.detailObjectDistance = sourceTerrain.detailObjectDistance;
            destinationTerrain.collectDetailPatches = sourceTerrain.collectDetailPatches;
            destinationTerrain.detailObjectDensity = sourceTerrain.detailObjectDensity;
            destinationTerrain.treeDistance = sourceTerrain.treeDistance;
            destinationTerrain.treeBillboardDistance = sourceTerrain.treeBillboardDistance;
            destinationTerrain.treeCrossFadeLength = sourceTerrain.treeCrossFadeLength;
            destinationTerrain.treeMaximumFullLODCount = sourceTerrain.treeMaximumFullLODCount;
            
            destinationTerrain.terrainData = duplicatedTerrainData;
            terrainCollider.terrainData = duplicatedTerrainData;

            return destinationTerrainGameObject;
        }

        internal static TerrainData DuplicateTerrainData(TerrainData sourceTerrainData) {
            TerrainData duplicatedTerrainData = new TerrainData();
            
            // Paint Texture
            duplicatedTerrainData.alphamapResolution = sourceTerrainData.alphamapResolution;
#if UNITY_2018_3_OR_NEWER
            duplicatedTerrainData.terrainLayers = sourceTerrainData.terrainLayers;
#else
            duplicatedTerrainData.splatPrototypes = sourceTerrainData.splatPrototypes;
#endif
            duplicatedTerrainData.SetAlphamaps(0, 0, sourceTerrainData.GetAlphamaps(0, 0, sourceTerrainData.alphamapWidth, sourceTerrainData.alphamapHeight));

            // Trees
            duplicatedTerrainData.treePrototypes = sourceTerrainData.treePrototypes;
            duplicatedTerrainData.treeInstances = sourceTerrainData.treeInstances;

            // Details
            int detailResolutionPerPatch = TerrainSettings.GetDetailResolutionPerPatch(sourceTerrainData);
            duplicatedTerrainData.SetDetailResolution(sourceTerrainData.detailResolution, detailResolutionPerPatch);
            duplicatedTerrainData.detailPrototypes = sourceTerrainData.detailPrototypes;
            for(int d = 0; d < sourceTerrainData.detailPrototypes.Length; d++) {
                duplicatedTerrainData.SetDetailLayer(0, 0, d, sourceTerrainData.GetDetailLayer(0, 0, sourceTerrainData.detailWidth, sourceTerrainData.detailHeight, d));
            }

            #if !UNITY_2019_3_OR_NEWER
            duplicatedTerrainData.thickness = sourceTerrainData.thickness;
            #endif

            // Wind Settings for Grass
            duplicatedTerrainData.wavingGrassStrength = sourceTerrainData.wavingGrassStrength;
            duplicatedTerrainData.wavingGrassSpeed = sourceTerrainData.wavingGrassSpeed;
            duplicatedTerrainData.wavingGrassAmount = sourceTerrainData.wavingGrassAmount;
            duplicatedTerrainData.wavingGrassTint = sourceTerrainData.wavingGrassTint;

            // Resolution
            duplicatedTerrainData.heightmapResolution = sourceTerrainData.heightmapResolution;
            duplicatedTerrainData.baseMapResolution = sourceTerrainData.baseMapResolution;
            duplicatedTerrainData.size = sourceTerrainData.size;
            int sourceheightmapResolution = sourceTerrainData.heightmapResolution;
            duplicatedTerrainData.SetHeights(0, 0, sourceTerrainData.GetHeights(0, 0, sourceheightmapResolution, sourceheightmapResolution));
            
            return duplicatedTerrainData;
        }

        /// <summary>
        /// A copy of the Utilities.LerpUnclamped that is only in Unity 5.2 and later.
        /// </summary>
        internal static float LerpUnclamped(float a, float b, float t) {
            return a + (b - a) * t;
        }

        internal static int RoundToNearestAndClamp(int currentNumber, int desiredNearestNumber, int minimum, int maximum) {
            int roundedNumber = Mathf.RoundToInt((float)currentNumber / desiredNearestNumber) * desiredNearestNumber;
            return Math.Min(Math.Max(roundedNumber, minimum), maximum);
        }

        internal static float FloorToSignificantDigits(float number, int numberOfDigits) {
            if(number == 0f) return 0f;
            
            float scale = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Abs(number))) + 1f);
            return scale * (float)Math.Round(number / scale, numberOfDigits);
        }

        /// <summary>
        /// Get the absolute path from a local path.
        /// </summary>
        /// <param name="localPath">The path contained within the "Assets" direction. Eg: "Models/Model.fbx"</param>
        /// <example>Passing "Models/Model.fbx" will return "C:/Users/John/MyProject/Assets/Models/Model.fbx"</example>
        /// <returns>Returns the absolute/full system path from the local "Assets" inclusive path.</returns>
        internal static string GetAbsolutePathFromLocalPath(string localPath) {
            return Application.dataPath.Remove(Application.dataPath.Length - 6, 6) + localPath;
        }

        /// <summary>
        /// Get the local path from an absolute path. 
        /// </summary>
        /// <example>Passing "C:/Users/John/MyProject/Assets/Models/Model.fbx" will return "Models/Model.fbx"</example>
        /// <param name="absolutePath"></param>
        /// <returns></returns>
        internal static string GetLocalPathFromAbsolutePath(string absolutePath) {
            int indexOfAssets = absolutePath.IndexOf("Assets", StringComparison.OrdinalIgnoreCase);

            if(indexOfAssets == -1) {
                throw new ArgumentException("The 'assetsPath' parameter must contain 'Assets/'");
            }
            return absolutePath.Remove(0, indexOfAssets);
        }
    }
}
