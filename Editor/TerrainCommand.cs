using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Math = System.Math;

namespace JesseStiller.TerrainFormerExtension {
    internal abstract class TerrainCommand {
        protected abstract bool GetUsesShift();
        protected abstract bool GetUsesControl();
        internal abstract string GetName();
        internal abstract void OnClick(object data);
        protected abstract void OnShiftClick(object data);
        protected abstract void OnShiftClickDown();
        protected abstract void OnControlClick();
        
        internal float[,] brushSamples;

        private List<Object> objectsToRegisterForUndo = new List<Object>();
        
        protected static CommandArea globalCommandArea;
        public static ManualResetEvent[] manualResetEvents;
        
        internal TerrainCommand(float[,] brushSamples) {
            this.brushSamples = brushSamples;
        }

        internal void Execute(Event currentEvent, CommandArea commandArea) {
            globalCommandArea = commandArea;
            if(this is PaintTextureCommand && TerrainFormerEditor.splatPrototypes.Length == 0) return;
            
            objectsToRegisterForUndo.Clear();
            foreach(TerrainInfo ti in TerrainFormerEditor.Instance.terrainInfos) {
                if(ti.commandArea == null) continue;

                if(this is PaintTextureCommand) {
                    objectsToRegisterForUndo.AddRange(ti.terrainData.alphamapTextures);
                } else {
                    objectsToRegisterForUndo.Add(ti.terrainData);
                }
            }

            if(objectsToRegisterForUndo.Count == 0) return;
            
            Undo.RegisterCompleteObjectUndo(objectsToRegisterForUndo.ToArray(), GetName());
            
            if(this is SmoothCommand || this is MouldCommand) {
                OnClick(null);
                return;
            }

            WaitCallback callback;

            // OnControlClick
            if(currentEvent.control) {
                if(GetUsesControl() == false) return;
                OnControlClick();
                return;
            }
            // OnShiftClick and OnShiftClickDown
            else if(currentEvent.shift) {
                OnShiftClickDown();

                if(GetUsesShift() == false) return;

                callback = OnShiftClick;
            }
            else {
                // OnClick
                callback = OnClick;
            }

            // Multithreaded terrain commands
            foreach(TerrainInfo ti in TerrainFormerEditor.Instance.terrainInfos) { 
                if(ti.commandArea == null) continue;
                int jobCount = Math.Max(Math.Min(System.Environment.ProcessorCount, globalCommandArea.height * 4), 1);
                int verticalSpan = Mathf.CeilToInt((float)globalCommandArea.height / jobCount);
                jobCount = Math.Min(jobCount, Mathf.CeilToInt((float)globalCommandArea.height / verticalSpan));

                if(manualResetEvents == null) {
                    manualResetEvents = new ManualResetEvent[jobCount];
                } else if(manualResetEvents.Length != jobCount) {
                    System.Array.Resize(ref manualResetEvents, jobCount);
                }

                int brushXOffset = ti.commandArea.x - ti.commandArea.clippedLeft;
                int brushYOffset = ti.commandArea.y - ti.commandArea.clippedBottom;

                for(int i = 0; i < jobCount; i++) {
                    if(manualResetEvents[i] == null) {
                        manualResetEvents[i] = new ManualResetEvent(false);
                    } else {
                        manualResetEvents[i].Reset();
                    }

                    int xStart = ti.commandArea.x;
                    int xEnd = xStart + ti.commandArea.width;
                    int yStart = ti.commandArea.y + i * verticalSpan;
                    int yEnd = Mathf.Min(yStart + verticalSpan, ti.commandArea.y + ti.commandArea.height);
                    
                    TerrainJobData data = new TerrainJobData(ti.heightsCache, ti.alphamapsCache, xStart, xEnd, yStart, yEnd, brushXOffset, brushYOffset, manualResetEvents[i]);
                    ThreadPool.QueueUserWorkItem(callback, data);
                }
                WaitHandle.WaitAll(manualResetEvents);
            }
        }

        internal struct TerrainJobData {
            public int xStart, xEnd, yStart, yEnd, brushXOffset, brushYOffset;
            public HeightsCacheBlock heightsCache;
            public AlphamapsCacheBlock alphamapsCache;
            public ManualResetEvent reset;
            public TerrainJobData(HeightsCacheBlock heightsCache, AlphamapsCacheBlock alphamapsCache, int xStart, int xEnd, int yStart, int yEnd, int brushXOffset, int brushYOffset, ManualResetEvent reset) {
                this.heightsCache = heightsCache;
                this.alphamapsCache = alphamapsCache;
                this.xStart = xStart;
                this.yStart = yStart;
                this.xEnd = xEnd;
                this.yEnd = yEnd;
                this.brushXOffset = brushXOffset;
                this.brushYOffset = brushYOffset;
                this.reset = reset;
            }
        }
    }
}