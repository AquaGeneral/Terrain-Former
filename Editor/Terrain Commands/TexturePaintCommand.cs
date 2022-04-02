using System.Threading;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class PaintTextureCommand : TerrainCommand {
        private const float alphamapsSampleEpsilon = 1f / 255f;

        private int selectedTextureIndex, layerCount;
        private float targetOpacity;
        
        internal override string GetName() {
            return "Paint Texture";
        }

        protected override bool GetUsesShift() {
            return false;
        }

        protected override bool GetUsesControl() {
            return false;
        }

        internal PaintTextureCommand(float[,] brushSamples, int selectedTextureIndex, int layerCount, float targetOpacity) : base(brushSamples) { 
            this.selectedTextureIndex = selectedTextureIndex;
            this.layerCount = layerCount;
            this.targetOpacity = targetOpacity;
        }

        internal override void OnClick(object data) {
            TerrainJobData d = (TerrainJobData)data;

            float brushSample;
            float newSample;

            for(int x = d.xStart; x < d.xEnd; x++) {
                for(int y = d.yStart; y < d.yEnd; y++) {
                    brushSample = brushSamples[x - d.brushXOffset, y - d.brushYOffset];
                    if(brushSample < TerrainFormerEditor.brushSampleEpsilon) continue;

                    newSample = d.alphamapsCache.data[y, x, selectedTextureIndex];

                    if(targetOpacity > newSample) {
                        newSample += brushSample;
                        if(newSample > targetOpacity) newSample = targetOpacity;
                    } else {
                        newSample -= brushSample;
                        if(newSample < targetOpacity) newSample = targetOpacity;
                    }
                    d.alphamapsCache.data[y, x, selectedTextureIndex] = newSample;

                    float sum = 0f;
                    for(int l = 0; l < layerCount; l++) {
                        if(l == selectedTextureIndex) continue;
                        sum += d.alphamapsCache.data[y, x, l];
                    }

                    if(sum > 0.01f) {
                        float sumCoefficient = (1f - newSample) / sum;
                        for(int l = 0; l < layerCount; l++) {
                            if(l == selectedTextureIndex) continue;
                            d.alphamapsCache.data[y, x, l] *= sumCoefficient;
                        }
                    } else {
                        for(int l = 0; l < layerCount; l++) {
                            d.alphamapsCache.data[y, x, l] = l == selectedTextureIndex ? 1f : 0f;
                        }
                    }

                    
                }
            }

            d.reset.Set();
        }

        protected override void OnControlClick() { }

        protected override void OnShiftClick(object data) { }

        protected override void OnShiftClickDown() { }
    }
}
