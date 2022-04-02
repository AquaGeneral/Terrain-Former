using System;
using System.Collections.Generic;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    public class HeightsCacheBlock : CacheBlock {
        public float[,] data;

        public HeightsCacheBlock(int length) : base(length) {
            data = new float[length, length];
        }

        internal void ReInitializeBlock(int heightmapResolution) {
            data = new float[heightmapResolution, heightmapResolution];
            Free();
        }
    }

    public class AlphamapsCacheBlock : CacheBlock {
        public float[,,] data;

        public AlphamapsCacheBlock(int length, int layerCount) : base(length) {
            data = new float[length, length, layerCount];
        }

        internal void ReInitializeBlock(int alphamapResolution, int layerCount) {
            if(layerCount == 0) return;
            data = new float[alphamapResolution, alphamapResolution, layerCount];
            Free();
        }
    }

    public abstract class CacheBlock {
        private const ushort SubRegionsPerAxis = 32;
        internal const ushort SubRegionSize = 129; // The length of each subRegion 

        private static readonly List<IntBounds> regions = new List<IntBounds>(4);

        public bool isFree = true;
        
        public bool[,] subRegionsLoaded = new bool[SubRegionsPerAxis, SubRegionsPerAxis]; // Loading multiple rows at a time makes things a little simpler, and is more cache friendly
        protected int length;
        
        public CacheBlock(int length) {
            SetAllRegions(false);
            this.length = length;
        }

        public void Free() {
            SetAllRegions(false);
            isFree = true;
        }

        /// <summary>
        /// Update the internal representation of which heightmap/alphamap regions have been loaded.
        /// </summary>
        /// <param name="rowStart">The beginning sub row region to load</param>
        /// <param name="rowEnd">The last sub row region to load</param>
        internal List<IntBounds> GetUnloadedIntersectingRegions(CommandArea commandArea) {
            regions.Clear();

            int xStart, xEnd, yStart, yEnd;
            xStart = Mathf.FloorToInt((float)commandArea.x / SubRegionSize);
            xEnd = Mathf.FloorToInt(((float)commandArea.x + commandArea.width) / SubRegionSize);
            yStart = Mathf.FloorToInt((float)commandArea.y / SubRegionSize);
            yEnd = Mathf.FloorToInt(((float)commandArea.y + commandArea.height) / SubRegionSize);

            for(int y = yStart; y <= yEnd; y++) {
                for(int x = xStart; x <= xEnd; x++) {                    
                    if(subRegionsLoaded[x, y]) continue;

                    subRegionsLoaded[x, y] = true;

                    regions.Add(new IntBounds(x * SubRegionSize, y * SubRegionSize, SubRegionSize));
                }
            }

            return regions;
        }

        internal void SetAllRegions(bool loaded) {
            for(int y = 0; y < SubRegionsPerAxis; y++) { 
                for(int x = 0; x < SubRegionsPerAxis; x++) { 
                    subRegionsLoaded[x, y] = loaded;
                }
            }
        }
    }
}
