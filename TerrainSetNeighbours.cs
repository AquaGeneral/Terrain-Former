using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    public class TerrainSetNeighbours : MonoBehaviour {
        [SerializeField]
        private Terrain leftTerrain, topTerrain, rightTerrain, bottomTerrain;

        // Setting the neighbours must be done at runtime as this data is not saved into the asset.
        void Awake() {
            GetComponent<Terrain>().SetNeighbors(leftTerrain, topTerrain, rightTerrain, bottomTerrain);
            Destroy(this);
        }

        public void SetNeighbours(Terrain leftTerrain, Terrain topTerrain, Terrain rightTerrain, Terrain bottomTerrain) {
            this.leftTerrain = leftTerrain;
            this.topTerrain = topTerrain;
            this.rightTerrain = rightTerrain;
            this.bottomTerrain = bottomTerrain;
        }
    }
}