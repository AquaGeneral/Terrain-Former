using System;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal abstract class Brush {
        internal const float GlobalBrushSpeedFactor = 0.001f;

        private const float Pi25Percent = Mathf.PI * 0.25f;
        private const float Pi75Percent = Mathf.PI * 0.75f;
        private const float Pi125Percent = Mathf.PI * 1.25f;
        private const float Pi175Percent = Mathf.PI * 1.75f;
        private const float Pi200Percent = Mathf.PI * 2f;
        
        internal string name;
        internal string id;
        internal Texture2D previewTexture;
        
        /* 
        * Brush Samples are used for the actual modification of the terrain. The only different between the values from 
        * GenerateBrushSamples and GenerateTextureSamples is the the TextureSamples are multiplied by the brush speed and the falloff.
        */
        internal float[,] samples;
        internal float[,] samplesWithSpeed;
        
        // Texture Samples are used for the textures. They don't include the brush speed in their values
        internal abstract float[,] GenerateTextureSamples(int pixelsPerAxis);
        internal abstract Texture2D GetTypeIcon();

        private float[,] falloffSamples;
        private float halfSize;
        private AnimationCurve falloffCurve;

        protected float[,] GenerateFalloff(int size) {
            if(falloffSamples == null || falloffSamples.GetLength(0) != size || falloffSamples.GetLength(1) != size) {
                falloffSamples = new float[size, size];
            }
            halfSize = Mathf.Floor(size * 0.5f);
            
            if(halfSize == 0f) {
                return new float[,] { { 1f } };
            }

            falloffCurve = TerrainFormerEditor.GetCurrentToolSettings().brushFalloff;
            
            float roundness = TerrainFormerEditor.GetCurrentToolSettings().brushRoundness;
            if(roundness == 1f) {
                int sizeMinusOne = size - 1;
                float distance, sample;
                float halfSizeCoefficient = 1f / halfSize;
                int halfSizeInt = Mathf.CeilToInt(size * 0.5f);
                for(int x = 0; x < halfSizeInt; x++) {
                    for(int y = 0; y < halfSizeInt; y++) {
                        distance = (float)Math.Sqrt(Math.Pow(x - halfSize, 2f) + Math.Pow(y - halfSize, 2f));

                        falloffSamples[x, y] = sample = falloffCurve.Evaluate(1f - (distance * halfSizeCoefficient));

                        // Copy the first quadrant to the remaining three quadrants without wastefully doing to same work four times total.
                        falloffSamples[sizeMinusOne - x, y] = sample;
                        falloffSamples[x, sizeMinusOne - y] = sample;
                        falloffSamples[sizeMinusOne - x, sizeMinusOne - y] = sample;
                    }
                }
            }
            // TODO: Multithread this
            else {
                float distance;
                float midPointRoundnessOffset = halfSize - (roundness * halfSize);
                Vector2 midPointRoundnessCircle = new Vector2(midPointRoundnessOffset, midPointRoundnessOffset);
                Vector2 newPoint;
                // If the edge points are beyond halfRoundnessDelta (the radius of the brush), then they aren't within the roundness circle
                float halfRoundnessDelta = halfSize - (roundness * halfSize);
                float roundnessHalfSize = roundness * halfSize;

                PointRotator pointRotator = new PointRotator(new Vector2(size * 0.5f, size * 0.5f));

                for(int x = 0; x < size; x++) {
                    for(int y = 0; y < size; y++) {
                        newPoint = pointRotator.Rotate(new Vector2(x, y));

                        Vector2 edgePoint = RadialIntersection((float)Math.Atan2(newPoint.x - halfSize, newPoint.y - halfSize), halfSize);
                        
                        distance = CalculateDistance(x - halfSize, y - halfSize);

                        /**
                        * If the edge points lay within the rounded angle, subtract the cornerDistance from the edgePoint's length. This itself
                        * is the sole reason the edges become rounded.
                        */
                        if(Math.Abs(edgePoint.x) >= halfRoundnessDelta && Math.Abs(edgePoint.y) >= halfRoundnessDelta) {
                            float cornerDistance = Vector2.Distance(midPointRoundnessCircle, new Vector2(Math.Abs(edgePoint.x), Math.Abs(edgePoint.y))) - roundnessHalfSize;
                            falloffSamples[x, y] = falloffCurve.Evaluate(1f - (distance / (edgePoint.magnitude - cornerDistance)));
                        } else {
                            falloffSamples[x, y] = falloffCurve.Evaluate(1f - distance / edgePoint.magnitude);
                        }
                    }
                }
            }
            
            return falloffSamples;
        }

        internal void UpdateSamplesAndMainTexture(int pixelsPerAxis) {
            if(TerrainFormerEditor.brushProjectorTexture == null) {
                TerrainFormerEditor.brushProjectorTexture = new Texture2D(pixelsPerAxis, pixelsPerAxis, TextureFormat.Alpha8, true);
                TerrainFormerEditor.brushProjectorTexture.filterMode = FilterMode.Trilinear;
                TerrainFormerEditor.brushProjectorTexture.hideFlags = HideFlags.HideAndDontSave;
                TerrainFormerEditor.brushProjectorTexture.wrapMode = TextureWrapMode.Clamp;
            } else if(TerrainFormerEditor.brushProjectorTexture.width != pixelsPerAxis && TerrainFormerEditor.brushProjectorTexture.height != pixelsPerAxis) {
                TerrainFormerEditor.brushProjectorTexture.Resize(pixelsPerAxis, pixelsPerAxis);
            }

            samples = GenerateTextureSamples(pixelsPerAxis);
            
            if(samplesWithSpeed == null || samplesWithSpeed.GetLength(0) != pixelsPerAxis || samplesWithSpeed.GetLength(1) != pixelsPerAxis) {
                samplesWithSpeed = new float[pixelsPerAxis, pixelsPerAxis];
            }
            
            float brushSpeed = GetBrushSpeed();

            Color[] colours = new Color[pixelsPerAxis * pixelsPerAxis];
            for(int x = 0; x < pixelsPerAxis; x++) {
                for(int y = 0; y < pixelsPerAxis; y++) {
                    samplesWithSpeed[x, y] = samples[x, y] * brushSpeed;

                    // Make the texture have transparent borders to stop texture wrapping artifacts.
                    if(x != 0 && y != 0 && x < pixelsPerAxis - 1 && y < pixelsPerAxis - 1) {
                        colours[y * pixelsPerAxis + x] = new Color(1f, 1f, 1f, samples[x, y]);
                    }
                }
            }
            TerrainFormerEditor.brushProjectorTexture.SetPixels(colours);

            TerrainFormerEditor.brushProjectorTexture.Apply();
        }

        internal void CreatePreviewTexture() {
            if(TerrainFormerEditor.IsToolSculptive(TerrainFormerEditor.Instance.CurrentTool) == false) return;

            if(previewTexture == null) {
                previewTexture = new Texture2D(Settings.cached.brushPreviewSize, Settings.cached.brushPreviewSize, TextureFormat.Alpha8, false);
                previewTexture.wrapMode = TextureWrapMode.Clamp;
                previewTexture.hideFlags = HideFlags.HideAndDontSave;
            } else if(previewTexture.width != Settings.cached.brushPreviewSize || previewTexture.height != Settings.cached.brushPreviewSize) {
                previewTexture.Resize(Settings.cached.brushPreviewSize, Settings.cached.brushPreviewSize);
            }

			float[,] previewSamples = GenerateTextureSamples(Settings.cached.brushPreviewSize);

            // It's possible to have a null response due to the brush being deleted at the same time others are added.
            if(previewSamples == null) return;

            for(int x = 0; x < Settings.cached.brushPreviewSize; x++) {
                for(int y = 0; y < Settings.cached.brushPreviewSize; y++) {
                    previewTexture.SetPixel(x, y, new Color(1f, 1f, 1f, previewSamples[x, y]));
                }
            }

            previewTexture.Apply();
        }

        internal void UpdateSamplesWithSpeed(int pixelsPerAxis) {
            samplesWithSpeed = GenerateTextureSamples(pixelsPerAxis);

            float brushSpeed = GetBrushSpeed();

            for(int x = 0; x < pixelsPerAxis; x++) {
                for(int y = 0; y < pixelsPerAxis; y++) {
                    samplesWithSpeed[x, y] *= brushSpeed;
                }
            }
        }

        private static float CalculateDistance(float x, float y) {
            return Mathf.Sqrt(Mathf.Pow(x, 2f) + Mathf.Pow(y, 2f));
        }

        private static float GetBrushSpeed() {
            float brushSpeed = TerrainFormerEditor.GetCurrentToolSettings().brushSpeed * GlobalBrushSpeedFactor;
            
            switch(TerrainFormerEditor.Instance.CurrentTool) { 
                case Tool.Smooth:
                case Tool.Mould:
                    brushSpeed *= 10f;
                    break;
                case Tool.Flatten:
                case Tool.SetHeight:
                    brushSpeed *= 18f;
                    break;
                case Tool.PaintTexture:
                    brushSpeed *= 10f;
                    break;
            }
            
            return brushSpeed;
        }

        private static Vector2 RadialIntersection(float radians, float halfSize) {
            radians = (float)Math.IEEERemainder(radians, Pi200Percent);

            if(radians < 0) {
                radians += 2 * Mathf.PI;
            }

            return RadialIntersectionWithConstrainedRadians(radians, halfSize);
        }

        // This method requires 0 <= radians < 2 * π.
        private static Vector2 RadialIntersectionWithConstrainedRadians(float radians, float halfSize) {
            float tangent = Mathf.Tan(radians);
            float y = halfSize * tangent;
            float x = halfSize / tangent;

            // An infinite line passing through the center at angle `radians`
            // intersects the right edge at Y coordinate `y` and the left edge
            // at Y coordinate `-y`.

            // Left
            if(radians > Pi125Percent && radians < Pi175Percent) {
                return new Vector2(-halfSize, -x);
            }
            // Bottom
            else if(radians >= Pi175Percent || radians < Pi25Percent) {
                return new Vector2(y, halfSize);
            }
            // Right
            else if(radians >= Pi25Percent && radians <= Pi75Percent) {
                return new Vector2(halfSize, x);
            }
            // Top
            else {
                return new Vector2(-y, -halfSize);
            }
        }
    }
}