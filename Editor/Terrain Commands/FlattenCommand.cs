using System;
using System.Threading;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class FlattenCommand : TerrainCommand {
        private FlattenMode mode;
        private float flattenHeight;
        
        internal override string GetName() {
            return "Flatten";
        }
        
        protected override bool GetUsesShift() {
            const bool usesShift = false;
            return usesShift;
        }
        
        protected override bool GetUsesControl() {
            const bool usesControl = true;
            return usesControl;
        }

        internal FlattenCommand(float[,] brushSamples, float flattenHeight) : base(brushSamples) {
            mode = Settings.cached.flattenMode;
            this.flattenHeight = flattenHeight;
        }
        
        internal override void OnClick(object data) {
            TerrainJobData d = (TerrainJobData)data;

            float brushSample;
            float height;
            float diff;

            for(int x = d.xStart; x < d.xEnd; x++) {
                for(int y = d.yStart; y < d.yEnd; y++) {
                    height = d.heightsCache.data[y, x];
                    if(height == flattenHeight) continue;

                    brushSample = brushSamples[x - d.brushXOffset, y - d.brushYOffset];
                    if(brushSample < TerrainFormerEditor.brushSampleEpsilon) continue;

                    if((mode == FlattenMode.Flatten && height < flattenHeight) ||
                        (mode == FlattenMode.Extend && height > flattenHeight)) continue;

                    diff = flattenHeight - height;
                    if(diff > 0f) {
                        height = Math.Min(height + diff * brushSample, flattenHeight);
                    } else {
                        height = Math.Max(height + diff * brushSample, flattenHeight);
                    }
                    
                    d.heightsCache.data[y, x] = height;
                }
            }

            d.reset.Set();
        }

        protected override void OnControlClick() {
            HeightsCacheBlock heightsCache;

            float brushSample;

            int leftMostGridPos = int.MaxValue;
            int bottomMostGridPos = int.MaxValue;
            foreach(TerrainInfo ti in TerrainFormerEditor.Instance.terrainInfos) {
                if(ti.commandArea == null) continue;
                if(ti.gridCellX < leftMostGridPos) {
                    leftMostGridPos = ti.gridCellX;
                }
                if(ti.gridCellY < bottomMostGridPos) {
                    bottomMostGridPos = ti.gridCellY;
                }
            }

            foreach(TerrainInfo ti in TerrainFormerEditor.Instance.terrainInfos) {
                if(ti.commandArea == null) continue;

                heightsCache = ti.heightsCache;

                int xStart = ti.commandArea.x;
                int xEnd = xStart + ti.commandArea.width;
                int yStart = ti.commandArea.y;
                int yEnd = yStart + ti.commandArea.height;
                int brushXOffset = ti.commandArea.x - ti.commandArea.clippedLeft;
                int brushYOffset = ti.commandArea.y - ti.commandArea.clippedBottom;

                int xSamplesOffset = Math.Max(xStart + ti.toolOffsetX - globalCommandArea.x, 0) - xStart;
                int ySamplesOffset = Math.Max(yStart + ti.toolOffsetY - globalCommandArea.y, 0) - yStart;
                
                float cachedHeight;

                for(int x = xStart; x < xEnd; x++) {
                    for(int y = yStart; y < yEnd; y++) {
                        brushSample = brushSamples[x - brushXOffset, y - brushYOffset];
                        if(brushSample < TerrainFormerEditor.brushSampleEpsilon) continue;
                        
                        cachedHeight = TerrainFormerEditor.heightsCopy1[y + ySamplesOffset, x + xSamplesOffset];

                        if((mode == FlattenMode.Flatten && cachedHeight <= flattenHeight) ||
                            (mode == FlattenMode.Extend && cachedHeight >= flattenHeight)) continue;

                        heightsCache.data[y, x] =
                            Mathf.Clamp01(Mathf.Lerp(cachedHeight, flattenHeight,
                                -TerrainFormerEditor.Instance.currentTotalMouseDelta * brushSample));
                    }
                }
            }
        }

        protected override void OnShiftClick(object data) { }

        protected override void OnShiftClickDown() { }
    }
}