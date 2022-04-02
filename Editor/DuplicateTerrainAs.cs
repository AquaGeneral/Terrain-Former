using UnityEngine;
using UnityEditor;

/**
* NOTE:
* This script is still in a beta stage. It so far has one known issue (see below), it's possible there's more issues.
*
* Known Issue:
*  - Alphamaps are not being duplicated correctly (not sure why)
*/

namespace JesseStiller.TerrainFormerExtension {
    public class DuplicateTerrainAs : Editor {
        [MenuItem("Assets/&Duplicate Terrain…", true)]
        private static bool IsDuplicateTerrainValid() {
            if(Selection.activeGameObject == null) return false;

            Terrain terrain = Selection.activeGameObject.GetComponent<Terrain>();

            return terrain != null && terrain.terrainData != null;
        }

        [MenuItem("Assets/&Duplicate Terrain…", false)]
        private static void DuplicateTerrain() {
            Terrain sourceTerrain = Selection.activeGameObject.GetComponent<Terrain>();
            TerrainData sourceTerrainData = sourceTerrain.terrainData;

            string savePath = EditorUtility.SaveFilePanelInProject("Duplicate Terrain", sourceTerrainData.name, "asset", null);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(savePath);

            if(string.IsNullOrEmpty(savePath)) return;

            TerrainData destinationTerrainData = Utilities.DuplicateTerrainGameObject(fileName, sourceTerrain, sourceTerrainData).GetComponent<Terrain>().terrainData;

            AssetDatabase.CreateAsset(destinationTerrainData, savePath);
        }
    }
}