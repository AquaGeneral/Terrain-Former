using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class PerlinNoiseBrush : Brush {
        private const string prettyTypeName = "Perlin Noise";
        private const int typeSortOrder = 10;
        private static Texture2D typeIcon;
        
        public PerlinNoiseBrush(string name, string id) {
            this.name = name;
            this.id = id;
        }

        internal override Texture2D GetTypeIcon() {
            if(typeIcon == null) {
                typeIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/LinearProceduralBrushIcon.psd");
            }
            return typeIcon;
        }
        
        // TODO: Add Support for multiple layers
        internal override float[,] GenerateTextureSamples(int pixelsPerAxis) {
            float[,] samples = GenerateFalloff(pixelsPerAxis);

            float spanCoefficient = 1f / pixelsPerAxis * Settings.cached.perlinNoiseScale;
            PointRotator pointRotator = new PointRotator(new Vector2(pixelsPerAxis * 0.5f * spanCoefficient, pixelsPerAxis * 0.5f * spanCoefficient));
            Vector2 point;
            
            float minMaxDifferenceCoefficient = 1f / (Settings.cached.perlinNoiseMax - Settings.cached.perlinNoiseMin);
            for(int x = 0; x < pixelsPerAxis; x++) {
                for(int y = 0; y < pixelsPerAxis; y++) {
                    point = pointRotator.Rotate(new Vector2(x * spanCoefficient, y * spanCoefficient));
                    samples[x, y] = Mathf.Clamp01(samples[x, y] * (Mathf.PerlinNoise(point.x, point.y) - Settings.cached.perlinNoiseMin) * minMaxDifferenceCoefficient);
                }
            }

            if(TerrainFormerEditor.GetCurrentToolSettings().invertFalloff) {
                for(int x = 0; x < pixelsPerAxis; x++) {
                    for(int y = 0; y < pixelsPerAxis; y++) {
                        samples[x, y] = 1f - samples[x, y];
                    }
                }
            }
            return samples;
        }
    }
}