using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class RaiseOrLowerCommand : TerrainCommand {
        internal override string GetName() {
            return "Raise/Lower";
        }

        protected override bool GetUsesShift() {
            return true;
        }

        protected override bool GetUsesControl() {
            return true;
        }

        internal RaiseOrLowerCommand(float[,] brushSamples) : base(brushSamples) { }

        internal override void OnClick(object data) {
            TerrainJobData d = (TerrainJobData)data;

            float brushSample;
            for(int x = d.xStart; x < d.xEnd; x++) {
                for(int y = d.yStart; y < d.yEnd; y++) {
                    brushSample = brushSamples[x - d.brushXOffset, y - d.brushYOffset];
                    if(brushSample < TerrainFormerEditor.brushSampleEpsilon) continue;

                    d.heightsCache.data[y, x] += brushSample;

                    if(d.heightsCache.data[y, x] > 1f) {
                        d.heightsCache.data[y, x] = 1f;
                    }
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

            float mouseYDelta = -TerrainFormerEditor.Instance.currentTotalMouseDelta * 0.2f;

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
                
                for(int x = xStart; x < xEnd; x++) {
                    for(int y = yStart; y < yEnd; y++) {
                        brushSample = brushSamples[x - brushXOffset, y - brushYOffset];
                        if(brushSample < TerrainFormerEditor.brushSampleEpsilon) continue;
                        
                        heightsCache.data[y, x] =
                            TerrainFormerEditor.heightsCopy1[y + ySamplesOffset, x + xSamplesOffset] + 
                            brushSample * mouseYDelta;

                        if(heightsCache.data[y, x] > 1f) {
                            heightsCache.data[y, x] = 1f;
                        } else if(heightsCache.data[y, x] < 0f) {
                            heightsCache.data[y, x] = 0f;
                        }
                    }
                }
            }
        }

        protected override void OnShiftClick(object data) {
            TerrainJobData d = (TerrainJobData)data;

            float brushSample;
            for(int x = d.xStart; x < d.xEnd; x++) {
                for(int y = d.yStart; y < d.yEnd; y++) {
                    brushSample = brushSamples[x - d.brushXOffset, y - d.brushYOffset];
                    if(brushSample < TerrainFormerEditor.brushSampleEpsilon) continue;

                    d.heightsCache.data[y, x] -= brushSample;

                    if(d.heightsCache.data[y, x] < 0f) {
                        d.heightsCache.data[y, x] = 0f;
                    }
                }
            }

            d.reset.Set();
        }

        protected override void OnShiftClickDown() { }
    }
}