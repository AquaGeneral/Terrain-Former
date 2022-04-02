using System;
using System.Threading;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class SmoothCommand : TerrainCommand {
        private readonly int smoothRadius, totalTerrainWidth, totalTerrainHeight;

        internal override string GetName() {
            return "Smooth";
        }

        protected override bool GetUsesShift() {
            return false;
        }

        protected override bool GetUsesControl() {
            return false;
        }

        internal SmoothCommand(float[,] brushSamples, int smoothRadius) : base(brushSamples) {
            this.smoothRadius = smoothRadius;
        }

        // Heavily optimized due to this code not being optimized neither AOT nor JIT.
        internal override void OnClick(object data) {
            int jobCount = Math.Max(Math.Min(Environment.ProcessorCount, globalCommandArea.height * 4), 1);
            int verticalSpan = Mathf.CeilToInt((float)globalCommandArea.height / jobCount);
            jobCount = Math.Min(jobCount, Mathf.CeilToInt((float)globalCommandArea.height / verticalSpan));
            int maxY = globalCommandArea.height + smoothRadius;

            Thread[] threads = new Thread[jobCount];

            int yStart = smoothRadius;
            for(int i = 0; i < jobCount; i++) {
                threads[i] = new Thread(() => HorizontalBlurPass(yStart, Mathf.Min(yStart += verticalSpan, maxY)));
                threads[i].Start();
            }
            for(int i = 0; i < jobCount; i++) threads[i].Join();

            yStart = smoothRadius;
            for(int i = 0; i < jobCount; i++) {
                threads[i] = new Thread(() => VerticalBlurPass(yStart, Mathf.Min(yStart += verticalSpan, maxY)));
                threads[i].Start();
            }
            for(int i = 0; i < jobCount; i++) threads[i].Join();
        }

        private void HorizontalBlurPass(int yStart, int yEnd) {
            float heightSum;
            int x, y, i, iMin, iMax;
            int brushBoundsX = globalCommandArea.width + smoothRadius;
            int smoothBounds = globalCommandArea.width + smoothRadius * 2;

            y = yStart;
            for(; y < yEnd; y++) {
                x = smoothRadius;
                for(; x < brushBoundsX; x++) {
                    iMin = x - smoothRadius;
                    if(iMin < 0) {
                        iMin = 0;
                    }
                    iMax = x + smoothRadius;
                    if(iMax > smoothBounds) {
                        iMax = smoothBounds;
                    }

                    heightSum = 0f;
                    for(i = iMin; i <= iMax; i++) {
                        heightSum += TerrainFormerEditor.heightsCopy1[y, i];
                    }

                    TerrainFormerEditor.heightsCopy2[y, x] = heightSum / (iMax + 1 - iMin);
                }
            }
        }

        private void VerticalBlurPass(int yStart, int yEnd) {
            float heightSum;
            int x, y, i, iMin, iMax;
            int brushBoundsX = globalCommandArea.width + smoothRadius;
            int smoothBounds = globalCommandArea.height + smoothRadius * 2;

            y = yStart;
            for(; y < yEnd; y++) {
                x = smoothRadius;
                for(; x < brushBoundsX; x++) {
                    iMin = y - smoothRadius;
                    if(iMin < 0) {
                        iMin = 0;
                    }
                    iMax = y + smoothRadius;
                    if(iMax > smoothBounds) {
                        iMax = smoothBounds;
                    }
                    
                    heightSum = 0f;
                    for(i = iMin; i <= iMax; i++) {
                        heightSum += TerrainFormerEditor.heightsCopy2[i, x];
                    }

                    TerrainFormerEditor.heightsCopy1[y, x] = heightSum / (iMax + 1 - iMin);
                }
            }
        }

        protected override void OnControlClick() { }

        protected override void OnShiftClickDown() { }

        protected override void OnShiftClick(object data) { }
    }
}