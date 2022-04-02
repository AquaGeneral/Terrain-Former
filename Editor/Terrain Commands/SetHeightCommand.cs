using System;
using System.Threading;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class SetHeightCommand : TerrainCommand {
        internal float setHeight;
        
        internal override string GetName() {
            return "Set Height";
        }

        protected override bool GetUsesShift() {
            return false;
        }

        protected override bool GetUsesControl() {
            return true;
        }

        internal SetHeightCommand(float[,] brushSamples) : base(brushSamples) {
            setHeight = Settings.cached.setHeight / TerrainFormerEditor.Instance.terrainSize.y;
        }

        internal override void OnClick(object data) {
            TerrainJobData d = (TerrainJobData)data;

            float brushSample;
            float height;
            float diff;

            for(int x = d.xStart; x < d.xEnd; x++) {
                for(int y = d.yStart; y < d.yEnd; y++) {
                    height = d.heightsCache.data[y, x];
                    if(height == setHeight) continue;

                    brushSample = brushSamples[x - d.brushXOffset, y - d.brushYOffset];
                    if(brushSample < TerrainFormerEditor.brushSampleEpsilon) continue;

                    diff = setHeight - height;
                    if(diff > 0f) {
                        height = Math.Min(height + diff * brushSample, setHeight);
                    } else {
                        height = Math.Max(height + diff * brushSample, setHeight);
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

                        heightsCache.data[y, x] =
                            Mathf.Clamp01(Mathf.Lerp(cachedHeight, setHeight,
                                -TerrainFormerEditor.Instance.currentTotalMouseDelta * brushSample));
                    }
                }
            }
        }

        protected override void OnShiftClick(object data) { }
        
        protected override void OnShiftClickDown() {
            TerrainFormerEditor.Instance.UpdateSetHeightAtMousePosition();
        }
    }
}