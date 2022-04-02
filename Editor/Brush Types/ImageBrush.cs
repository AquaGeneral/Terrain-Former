using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class ImageBrush : Brush {
        private const string prettyTypeName = "Image";
        private const int typeSortOrder = 20;
        private static Texture2D typeIcon;
        public Texture2D sourceTexture;
        
        public ImageBrush(string name, string id, Texture2D sourceTexture) {
            this.name = name;
            this.id = id;
            this.sourceTexture = sourceTexture;
        }
        
        internal override Texture2D GetTypeIcon() {
            if(typeIcon == null) {
                typeIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/CustomBrushIcon.psd");
            }
            return typeIcon;
        }

        internal override float[,] GenerateTextureSamples(int size) {
            // When adding and deleting brushes at once, the add event is called first and as such it might try to update a destroyed texture
            if(sourceTexture == null) return null;

            bool invertBrush = TerrainFormerEditor.GetCurrentToolSettings().invertBrushTexture || Settings.cached.invertBrushTexturesGlobally;
            bool useFalloffForCustomBrushes = TerrainFormerEditor.GetCurrentToolSettings().useFalloffForCustomBrushes;
            
            float[,] samples;
            if(useFalloffForCustomBrushes) {
                samples = GenerateFalloff(size);
            } else {
                samples = new float[size, size];
            }

            Vector2 point;
            float sample;
            bool invertAlphaFalloff = TerrainFormerEditor.GetCurrentToolSettings().invertFalloff;
            PointRotator pointRotator = new PointRotator(new Vector2(size * 0.5f, size * 0.5f));
            
            for(int x = 0; x < size; x++) {
                for(int y = 0; y < size; y++) {
                    point = pointRotator.Rotate(new Vector2(x, y));

                    if(useFalloffForCustomBrushes) {
                        if(invertAlphaFalloff) {
                            sample = 1f - samples[x, y];
                        } else {
                            sample = samples[x, y];
                        }
                    } else {
                        sample = 1f;
                    }

                    if(invertBrush) {
                        samples[x, y] = sourceTexture.GetPixelBilinear(point.x / size, point.y / size).grayscale * sample;
                    } else {
                        samples[x, y] = (1f - sourceTexture.GetPixelBilinear(point.x / size, point.y / size).grayscale) * sample;
                    }
                }
            }
            
            return samples;
        }
    }
}