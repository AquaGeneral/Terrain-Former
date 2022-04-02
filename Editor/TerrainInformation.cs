using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    public class TerrainInfo {
        public Transform transform;
        public Terrain terrain;
        public TerrainData terrainData;
        public TerrainCollider collider;
        public string terrainAssetPath;
        public CommandArea commandArea;
        public int gridRelativeOffsetX;
        public int gridRelativeOffsetY;
        public int gridCellX, gridCellY; // Co-ordinates of the terrain in respect to a terrain grid
        public int toolOffsetX, toolOffsetY; // The samples' offsets based on the current tool selected
        public bool ignoreOnAssetsImported = false;
        public HeightsCacheBlock heightsCache;
        public AlphamapsCacheBlock alphamapsCache;
        
        public TerrainInfo(Terrain terrain) {
            this.terrain = terrain;
            transform = terrain.transform;
            collider = transform.GetComponent<TerrainCollider>();
            terrainData = terrain.terrainData;

            terrainAssetPath = AssetDatabase.GetAssetPath(terrainData);
        }
    }
}
