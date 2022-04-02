using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class TerrainMismatchManager {
        private List<TerrainInfo> terrainInfos;
        private bool heightmapResolutionsAreIdentical;
        private int heightmapResolution = 1025;
        
        private int[] terrainIndexesWithSplatPrototypes;
        private string[] terrainNamesWithSplatPrototypes;
        private bool splatPrototypesAreIdentical;
        private int splatPrototypesIndex = -1;
        private Texture2D[] splatPrototypePreviews;

        internal bool IsInitialized { get; set; }
        internal bool IsMismatched { get; private set; }
        
        internal void Initialize(List<TerrainInfo> terrainInfos) {
            if(IsMismatched) return;

            this.terrainInfos = terrainInfos;
            IsMismatched = false;
            splatPrototypesAreIdentical = true;
            heightmapResolutionsAreIdentical = true;

            TerrainData firstTerrainData = terrainInfos[0].terrainData;
            heightmapResolution = firstTerrainData.heightmapResolution;

#if UNITY_2018_3_OR_NEWER
            TerrainLayer[] firstSplatPrototypes = firstTerrainData.terrainLayers;
#else
            SplatPrototype[] firstSplatPrototypes = firstTerrainData.splatPrototypes;
#endif
            
            for(int i = 1; i < terrainInfos.Count; i++) {
                // Heightmap Resolution check
                if(heightmapResolutionsAreIdentical && heightmapResolution != terrainInfos[i].terrainData.heightmapResolution) {
                    SetMismatch(ref heightmapResolutionsAreIdentical);
                }
                
                // Splat Prototypes check
                if(splatPrototypesAreIdentical) {
#if UNITY_2018_3_OR_NEWER
                    if(firstSplatPrototypes.Length != terrainInfos[i].terrainData.terrainLayers.Length) {
#else
                    if(firstSplatPrototypes.Length != terrainInfos[i].terrainData.splatPrototypes.Length) {
#endif
                        SetMismatch(ref splatPrototypesAreIdentical);
                    }

#if UNITY_2018_3_OR_NEWER
                    else { 
                        TerrainLayer layerA, layerB;

                        for(int layer = 0; layer < firstSplatPrototypes.Length; layer++) {
                            layerA = firstSplatPrototypes[layer];
                            layerB = terrainInfos[i].terrainData.terrainLayers[layer];

                            if(layerA != layerB && (layerA == null || layerB == null)) {
                                SetMismatch(ref splatPrototypesAreIdentical);
                            }
                        }
                    }
#endif
                }
            }
            
            List<string> terrainNamesWithSplatPrototypesList = new List<string>();
            List<int> terrainIndexesWithSplatPrototypesList = new List<int>();
            for(int i = 0; i < terrainInfos.Count; i++) {
                string terrainName = terrainInfos[i].terrain.name;
                firstTerrainData = terrainInfos[i].terrainData;

#if UNITY_2018_3_OR_NEWER
                if(firstTerrainData.terrainLayers.Length != 0) {
#else
                if(firstTerrainData.splatPrototypes.Length != 0) {
#endif
                    terrainIndexesWithSplatPrototypesList.Add(i);
                    terrainNamesWithSplatPrototypesList.Add(terrainName);

                    if(splatPrototypesIndex == -1) {
                        splatPrototypesIndex = i;
                    }
                }
            }

            terrainNamesWithSplatPrototypes = terrainNamesWithSplatPrototypesList.ToArray();
            terrainIndexesWithSplatPrototypes = terrainIndexesWithSplatPrototypesList.ToArray();
            
            UpdateSplatPrototypesPreviews();

            IsInitialized = true;
        }

        internal bool DoTerrainsHaveMatchingSettings(TerrainData a, TerrainData b) {
            if(a.heightmapResolution != b.heightmapResolution) return false;
#if UNITY_2018_3_OR_NEWER
            if(a.terrainLayers.Length != b.terrainLayers.Length) return false;
#else
            if(a.splatPrototypes.Length != b.splatPrototypes.Length) return false;
#endif

            return true;
        }

        private void SetMismatch(ref bool paramater) {
            paramater = false;
            IsMismatched = true;
        }

        private void UpdateSplatPrototypesPreviews() {
            if(splatPrototypesIndex == -1) return;

#if UNITY_2018_3_OR_NEWER
            splatPrototypePreviews = new Texture2D[terrainInfos[splatPrototypesIndex].terrainData.terrainLayers.Length];
#else
            splatPrototypePreviews = new Texture2D[terrainInfos[splatPrototypesIndex].terrainData.splatPrototypes.Length];
#endif
            Texture2D splatTexture;
            for(int i = 0; i < splatPrototypePreviews.Length; i++) {
#if UNITY_2018_3_OR_NEWER
                if(terrainInfos[splatPrototypesIndex].terrainData.terrainLayers[i] == null) {
                    splatTexture = null; 
                } else { 
                    splatTexture = terrainInfos[splatPrototypesIndex].terrainData.terrainLayers[i].diffuseTexture;
                }
#else
                splatTexture = terrainInfos[splatPrototypesIndex].terrainData.splatPrototypes[i].texture;
#endif
                splatPrototypePreviews[i] = AssetPreview.GetAssetPreview(splatTexture) ?? splatTexture;
            }
        }
        
        internal void Draw() {
            if(IsMismatched == false) return;
            GUIUtilities.ActionableHelpBox("There are differences between the terrains in the current terrain grid which must be fixed before sculpting and painting is allowed.", MessageType.Warning, 
                () => {
                    EditorGUILayout.LabelField("Terrain Grid Settings", EditorStyles.boldLabel);
                    if(heightmapResolutionsAreIdentical == false) {
                        heightmapResolution = EditorGUILayout.IntPopup(TerrainSettings.heightmapResolutionContent, heightmapResolution, TerrainSettings.heightmapResolutionsContents, TerrainSettings.heightmapResolutions);
                    }
                    
                    if(splatPrototypesAreIdentical == false) {
                        int newIndex = EditorGUILayout.IntPopup("Terrain Layers", splatPrototypesIndex, terrainNamesWithSplatPrototypes, terrainIndexesWithSplatPrototypes);
                        if(newIndex != splatPrototypesIndex) {
                            splatPrototypesIndex = newIndex;
                            UpdateSplatPrototypesPreviews();
                        }
                        DrawPreviewGrid(splatPrototypePreviews);
                    }
                    EditorGUILayout.Space();
                    if(GUILayout.Button("Apply to Terrain Grid")) {
                        Apply();
                    }
                }
            );
        }
        
        private void Apply() {
            List<TerrainData> allModifiedTerrainDatas = new List<TerrainData>();
            for(int i = 0; i < terrainInfos.Count; i++) {
                if(terrainInfos[i].terrainData.heightmapResolution == heightmapResolution && 
                    splatPrototypesAreIdentical) continue;
                allModifiedTerrainDatas.Add(terrainInfos[i].terrainData);
            }
            Undo.RegisterCompleteObjectUndo(allModifiedTerrainDatas.ToArray(), "Fixed terrain grid settings mismatch");

            Vector3 originalSize = terrainInfos[0].terrainData.size;

            for(int i = 0; i < allModifiedTerrainDatas.Count; i++) {
                if(heightmapResolutionsAreIdentical == false) {
                    allModifiedTerrainDatas[i].heightmapResolution = heightmapResolution;
                    allModifiedTerrainDatas[i].size = originalSize; // Unity changes the size if the heightmapResolution has changed
                }
                if(splatPrototypesAreIdentical == false) {
#if UNITY_2018_3_OR_NEWER
                    allModifiedTerrainDatas[i].terrainLayers = allModifiedTerrainDatas[splatPrototypesIndex].terrainLayers;
#else
                    allModifiedTerrainDatas[i].splatPrototypes = allModifiedTerrainDatas[splatPrototypesIndex].splatPrototypes;
#endif
                }
            }
            
            IsMismatched = false;
            splatPrototypesAreIdentical = true;
            heightmapResolutionsAreIdentical = true;

            TerrainFormerEditor.Last.OnEnable();
        }

        private void DrawPreviewGrid(Texture2D[] previews) {
            float size = 70f;
            int columnsPerRow = Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 30f) / size);
            int rows = Math.Max(Mathf.CeilToInt((float)previews.Length / columnsPerRow), 1);
            int currentRow = 0;
            int currentColumn = 0;
            GUI.BeginGroup(GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 42f, rows * size), GUI.skin.box);
            for(int i = 0; i < previews.Length; i++) {
                Rect rect = new Rect(currentColumn * 67f + 3f, currentRow * 64f + 3f, 64f, 64f);

                if(previews[i] == null) {
                    GUI.Box(rect, "Empty");
                } else { 
                    GUI.DrawTexture(rect, previews[i]);
                }
                if(++currentColumn >= columnsPerRow) {
                    currentColumn = 0;
                    currentRow++;
                }
            }
            GUI.EndGroup();
        }
    }
}