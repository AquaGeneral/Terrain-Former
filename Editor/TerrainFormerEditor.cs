using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
/**
* A lot of the code (especially the Terrain Commands) is heavily optimized since it's not optimized by the compiler (seemingly all user 
* Editor code isn't). It's necessity is further cemented by the fact that SetHeights and SetAlphamaps can be slower than running
* even the fairly involved Smooth tool on the CPU with non-JIT-optimized code.
* 
* Edit: As of Unity 2020.1, Unity supports optimisation of editor code (by switching from Debug to Release mode): 
* https://docs.unity3d.com/2020.1/Documentation/Manual/ManagedCodeDebugging.html
* 
* IMPORTANT NOTE:
* Unity's terrain data co-ordinates are not setup as you might expect.
* Assuming the terrain is not rotated, this is the terrain strides vs axis:
* [  0,      0  ] = -X, -Z
* [width,    0  ] = +X, -Z
* [  0,   height] = -X, +Z
* [width, height] = +X, +Z
* 
* This means that the that X goes out to the 3D Z-Axis, and Y goes out into the 3D X-Axis.
* This also means that a world space position such as the mouse position from a raycast needs 
*   its worldspace X-Axis position mapped to Z, and the worldspace Y-Axis mapped to X
*/

namespace JesseStiller.TerrainFormerExtension {
    [CustomEditor(typeof(TerrainFormer))]
    internal class TerrainFormerEditor : Editor {
        private bool commandsFirstFrame = true;

        internal const int MaxBoxFilterSize = 15;

        // Brush fields
        private const float minBrushSpeed = 0.1f;
        private const float maxBrushSpeed = 100f;
        // If the brush samples is lower than this, than no operation will take place at the current area
        internal const float brushSampleEpsilon = 0.0000001f;

        internal static float[,] toolScratchArray = new float[32, 32];

        // Reflection fields
        private List<object> unityTerrainInspectors = new List<object>();
        private static PropertyInfo unityTerrainSelectedTool;
        private readonly static PropertyInfo guiUtilityTextFieldInput;
        private readonly static MethodInfo terrainDataSetBasemapDirtyMethodInfo;
        private readonly static MethodInfo inspectorWindowRepaintAllInspectors;
        
        // Instance/Editor related fields
        private static int activeInspectorInstanceID = 0;
        internal static TerrainFormerEditor Instance;
        internal static TerrainFormerEditor Last;
        private TerrainFormer terrainFormer;
        
        internal static AlphamapsCacheBlock[] alphamapsCacheBlocks;
        internal static HeightsCacheBlock[] heightsCacheBlocks;
        internal static float[,] heightsCopy1; // Used for interactive Raise/Lower, etc plus Smooth and Mould
        internal static float[,] heightsCopy2; // Use for Smooth and Mould

        internal static TerrainLayer[] splatPrototypes;
        internal int heightmapWidth, heightmapHeight; // Grid heightmap samples
        internal int toolSamplesHorizontally, toolSamplesVertically; // Heightmap samples for sculpting, splatmap samples for painting
        private int heightmapResolution;
        private int alphamapResolution;
        private int currentToolsResolution;
        internal Vector3 terrainSize; // Size of a single terrain (not a terrain grid)

        // Terrain Grid specific fields
        internal List<TerrainInfo> terrainInfos;
        internal int numberOfTerrainsHorizontally = 1;
        internal int numberOfTerrainsVertically = 1;

        // The first terrain (either the bottom left most one in the grid, or the only terrain).
        internal Transform firstTerrainTransform;
        private Terrain firstTerrain;
        private Vector3 terrainGridBottomLeft;
        internal TerrainData firstTerrainData;

        private TerrainCommand currentCommand;
        internal CommandArea globalCommandArea;

        private bool isTerrainGridParentSelected = false;
        
        // Heightfield fields
        private Texture2D heightmapTexture;

        private TerrainMismatchManager mismatchManager;

        // States and Information
        private int lastHeightmapResolultion;
        internal bool isSelectingBrush = false;
        private bool behaviourGroupUnfolded = true;

        private SamplesDirty samplesDirty = SamplesDirty.None;
        
        // Projector and other visual cursor-like fields
        private GameObject cylinderCursor;
        private Material cylinderCursorMaterial;
        private GameObject gridPlane;
        private Material gridPlaneMaterial;
        private GameObject topPlaneGameObject; // Used to show the current height of "Flatten" and "Set Height"
        private Material topPlaneMaterial;
        internal static Texture2D brushProjectorTexture;
        
        private static GUIContent[] toolsGUIContents;
        
        // Most UnityEngine.Objects need to be disposed on manually and this saves the need to call DestroyImmediate for each and every one.
        private List<UnityEngine.Object> trackedObjects = new List<UnityEngine.Object>(16);

        // Mouse related fields
        private bool mouseIsDown; 
        private Vector2 mousePosition = new Vector2(); // The current screen-space position of the mouse. This position is used for raycasting
        private Vector2 lastMousePosition;
        private Vector3 lastWorldspaceMousePosition;
        private float mouseSpacingDistance = 0f;
        private Vector3 lastClickPosition; // The point of the terrain the mouse clicked on
        
        private float randomSpacing;
        internal float currentTotalMouseDelta = 0f;

        private SavedTool currentTool;
        internal Tool CurrentTool {
            get {
                if(Tools.current != UnityEditor.Tool.None || GetInstanceID() != activeInspectorInstanceID) {
                    currentTool.Value = Tool.None;
                    
                }
                return currentTool.Value;
            }
            private set {
                if(value == CurrentTool) return;
                if(value != Tool.None) Tools.current = UnityEditor.Tool.None;

                Tool previousTool = currentTool.Value;
                currentTool.Value = value;
                CurrentToolChanged(previousTool);
            }
        }
        
        private Brush CurrentBrush {
            get {
                Brush brush = BrushCollection.GetBrushById(Settings.cached.modeSettings[CurrentTool].selectedBrushId);
                if(brush == null) {
                    brush = BrushCollection.brushes[0];
                    UseDefaultBrush();
                }
                return brush;
            }
        }

        // The minimum brush size is set to the total length of five heightmap segments (with one segment being the length from one sample to its neighbour)
        private float MinBrushSize {
            get {
                return Mathf.CeilToInt(terrainSize.x / heightmapResolution * 1f);
            }
        }
        
        private float[,] temporarySamples;
        private int halfBrushSizeInSamples;
        private int brushSizeInSamples;
        private int BrushSizeInSamples {
            set {
                value = Mathf.Min(value, currentToolsResolution - 1); // HACK: There are errors around terrain borders in terrain grid unless we do this

                Debug.Assert(value > 0);

                if(brushSizeInSamples == value) return;
                brushSizeInSamples = value;
                halfBrushSizeInSamples = brushSizeInSamples / 2;
            }
        }
        
        static TerrainFormerEditor() {
            guiUtilityTextFieldInput = typeof(GUIUtility).GetProperty("textFieldInput", BindingFlags.NonPublic | BindingFlags.Static);
            inspectorWindowRepaintAllInspectors = Assembly.GetAssembly(typeof(Editor)).GetType("UnityEditor.InspectorWindow").GetMethod(
                "RepaintAllInspectors", BindingFlags.Static | BindingFlags.NonPublic);
            terrainDataSetBasemapDirtyMethodInfo = typeof(TerrainData).GetMethod("SetBasemapDirty", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        // Simple initialization logic that doesn't rely on any secondary data
        internal void OnEnable() {
            if(EditorApplication.isPlaying) return;

            Last = this;

            // Sometimes it's possible Terrain Former thinks the mouse is still pressed down as not every event is detected by Terrain Former
            mouseIsDown = false; 
            terrainFormer = (TerrainFormer)target;
            currentTool = new SavedTool("TerrainFormer/CurrentTool", Tool.None);
            
            // Forcibly re-initialize just in case variables were lost during an assembly reload
            if(Initialize(true) == false) return;
            
            // Set the Terrain Former component icon
            #if !UNITY_2021_2_OR_NEWER
            Type editorGUIUtilityType = typeof(EditorGUIUtility);
            MethodInfo setIcon = editorGUIUtilityType.GetMethod("SetIconForObject", BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.NonPublic, null, 
                new Type[] { typeof(UnityEngine.Object), typeof(Texture2D) }, null);
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icon.png");
            setIcon.Invoke(null, new object[] { target, icon});
            #endif

            Undo.undoRedoPerformed += UndoRedoPerformed;
#if UNITY_2019_1_OR_NEWER
            SceneView.duringSceneGui += OnSceneGUICallback;
#else
            SceneView.onSceneGUIDelegate += OnSceneGUICallback;
#endif

            if(activeInspectorInstanceID == 0) {
                CurrentToolChanged(Tool.Ignore);
            }
        }

        /**
        * Initialize contains logic that is intrinsically tied to this entire terrain tool. If any of these fields and 
        * other things are missing, then the entire editor will break. An attempt will be made every GUI frame to find them.
        * Returns true if the initialization was successful or if everything is already initialized, false otherwise.
        * If the user moves Terrain Former's Editor folder away and brings it back, the brushProjector dissapears. This is why
        * it is checked for on Initialization.
        */
        private bool Initialize(bool forceReinitialize = false) {
            if(forceReinitialize == false && terrainFormer != null && cylinderCursor != null) {
                return true;
            }
            
            /**
            * If there is more than one object selected, do not even bother initializing. This also fixes a strange 
            * exception occurance when two terrains or more are selected; one with Terrain Former and one without
            */
            if(Selection.objects.Length != 1) return false;

            // Make sure there is only ever one Terrain Former on the current object
            TerrainFormer[] terrainFormerInstances = terrainFormer.GetComponents<TerrainFormer>();
            if(terrainFormerInstances.Length > 1) {
                for(int i = terrainFormerInstances.Length - 1; i > 0; i--) {
                    DestroyImmediate(terrainFormerInstances[i]);
                }
                EditorUtility.DisplayDialog("Terrain Former", "You can't add multiple Terrain Former components to a single Terrain object.", "Close");
                return false;
            }
            
            Settings.Create();
            if(Settings.cached == null) return false;
            
            InitialiseToolsGUIContents();

            Settings.cached.AlwaysShowBrushSelectionChanged = AlwaysShowBrushSelectionValueChanged;
            Settings.cached.brushColour.ValueChanged = BrushColourChanged;

            if(UpdateTerrainRelatedFields() == false) return false;
                        
            BrushCollection.Initilize();

            CreateProjector();

            CreateGridPlane();

            /**
            * Get an instance of the built-in Unity Terrain Inspector so we can override the selectedTool property
            * when the user selects a different tool in Terrain Former. This makes it so the user can't accidentally
            * use two terain tools at once (eg. Unity Terrain's raise/lower, and Terrain Former's raise/lower)
            */
            Type unityTerrainInspectorType = Assembly.GetAssembly(typeof(Editor)).GetType("UnityEditor.TerrainInspector");
            unityTerrainSelectedTool = unityTerrainInspectorType.GetProperty("selectedTool", BindingFlags.NonPublic | BindingFlags.Instance);
            
            UnityEngine.Object[] terrainInspectors = Resources.FindObjectsOfTypeAll(unityTerrainInspectorType);
            // Iterate through each Unity terrain inspector to find the Terrain Inspector(s) that belongs to this object
            foreach(UnityEngine.Object inspector in terrainInspectors) {
                Editor inspectorAsEditor = (Editor)inspector;
                GameObject inspectorGameObject = ((Terrain)inspectorAsEditor.target).gameObject;
                
                if(inspectorGameObject == terrainFormer.gameObject) {
                    unityTerrainInspectors.Add(inspector);
                }
            }

            heightsCacheBlocks = new HeightsCacheBlock[4];
            for(int i = 0; i < heightsCacheBlocks.Length; i++) {
                heightsCacheBlocks[i] = new HeightsCacheBlock(heightmapResolution);
            }

            alphamapsCacheBlocks = new AlphamapsCacheBlock[4];
            for(int i = 0; i < alphamapsCacheBlocks.Length; i++) {
                alphamapsCacheBlocks[i] = new AlphamapsCacheBlock(alphamapResolution, splatPrototypes.Length);
            }
            
            AssetWatcher.OnAssetsImported = OnAssetsImported;
            AssetWatcher.OnAssetsMoved = OnAssetsMoved;
            AssetWatcher.OnAssetsDeleted = OnAssetsDeleted;
            AssetWatcher.OnWillSaveAssetsAction = OnWillSaveAssets;
            
            return true;
        }

        private static void InitialiseToolsGUIContents() {
            toolsGUIContents = new GUIContent[] {
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/RaiseLower.png"  ), "Raise/Lower"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/Smooth.png"      ), "Smooth"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/SetHeight.png"   ), "Set Height"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/Flatten.png"     ), "Flatten"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/Mould.psd"       ), "Mould"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/PaintTexture.psd"), "Paint Texture"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/Heightmap.psd"   ), "Heightmap"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/Generate.png"    ), "Generate"),
                new GUIContent(null, AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icons/Settings.png"    ), "Settings")
            };
        }

        // Returns false if there was an error.
        private bool UpdateTerrainRelatedFields() {
            Terrain selectedTerrainComponent = terrainFormer.GetComponent<Terrain>();
            terrainInfos = new List<TerrainInfo>();

            if(selectedTerrainComponent != null) {
                // Find all matching adjacent terrains that form a terrain grid (across all scenes too)
                foreach(Terrain terrain in FindObjectsOfType<Terrain>()) {
                    if(terrain.terrainData == null) continue;
                    if(DoesTerrainBelongInGrid(terrain, selectedTerrainComponent) == false) continue;

                    /**
                    * To avoid gathering terrains that users don't actually want to be considered, only terrains of the same 
                    * heightmap resolution, etc will be considered when they don't share the same parent.
                    */
                    if(terrain.transform.parent != null && terrain.transform.parent != terrainFormer.transform.parent && 
                        mismatchManager.DoTerrainsHaveMatchingSettings(selectedTerrainComponent.terrainData, terrain.terrainData) == false) {
                        continue;
                    }

                    terrainInfos.Add(new TerrainInfo(terrain));
                }
            } else {
                //If Terrain Former is attached to a game object with children that contain Terrains, allow Terrain Former to look into the child terrain objects.
                Terrain[] terrainChildren = terrainFormer.GetComponentsInChildren<Terrain>();
                if(terrainChildren != null && terrainChildren.Length > 0) {
                    isTerrainGridParentSelected = true;
                } else {
                    return false;
                }

                foreach(Terrain terrain in terrainChildren) {
                    if(terrain.terrainData == null) continue;
                    if(DoesTerrainBelongInGrid(terrain, terrainChildren[0]) == false) continue;

                    terrainInfos.Add(new TerrainInfo(terrain));
                }
            }

            if(terrainInfos.Count == 0) return false;

            // Assume the first terrain information has the correct parameters
            terrainSize = terrainInfos[0].terrainData.size;
            heightmapResolution = terrainInfos[0].terrainData.heightmapResolution;
            alphamapResolution = terrainInfos[0].terrainData.alphamapResolution;
            lastHeightmapResolultion = heightmapResolution;

            if(terrainInfos.Count > 1) {
                // Find the bottom-left most terrain
                firstTerrainTransform = terrainInfos[0].transform;
                terrainGridBottomLeft = firstTerrainTransform.position;

                for(int i = 1; i < terrainInfos.Count; i++) {
                    if(terrainGridBottomLeft.x > terrainInfos[i].transform.position.x) terrainGridBottomLeft.x = terrainInfos[i].transform.position.x;
                    if(terrainGridBottomLeft.z > terrainInfos[i].transform.position.z) terrainGridBottomLeft.z = terrainInfos[i].transform.position.z;

                    if(  terrainInfos[i].transform.position.x <  firstTerrainTransform.position.x || 
                        (terrainInfos[i].transform.position.x <= firstTerrainTransform.position.x && 
                         terrainInfos[i].transform.position.z <  firstTerrainTransform.position.z)) {
                        firstTerrainTransform = terrainInfos[i].transform;
                    }
                }

                foreach(TerrainInfo info in terrainInfos) {
                    info.gridCellX = Mathf.RoundToInt((info.transform.position.x - terrainGridBottomLeft.x) / terrainSize.x);
                    info.gridCellY = Mathf.RoundToInt((info.transform.position.z - terrainGridBottomLeft.z) / terrainSize.z);

                    numberOfTerrainsHorizontally = Mathf.Max(numberOfTerrainsHorizontally, info.gridCellX);
                    numberOfTerrainsVertically = Mathf.Max(numberOfTerrainsVertically, info.gridCellY);
                }
                numberOfTerrainsHorizontally++;
                numberOfTerrainsVertically++;
            } else {
                if(selectedTerrainComponent) {
                    firstTerrainTransform = selectedTerrainComponent.transform;
                } else {
                    firstTerrainTransform = terrainInfos[0].transform;
                }
                terrainGridBottomLeft = firstTerrainTransform.position;
            }

            firstTerrain = firstTerrainTransform.GetComponent<Terrain>();
            if(firstTerrain == null) return false;
            firstTerrainData = firstTerrain.terrainData;
            if(firstTerrainData == null) return false;

            heightmapWidth  = numberOfTerrainsHorizontally * heightmapResolution - (numberOfTerrainsHorizontally - 1);
            heightmapHeight =   numberOfTerrainsVertically * heightmapResolution - (numberOfTerrainsVertically   - 1);

            if(mismatchManager == null) mismatchManager = new TerrainMismatchManager();
            mismatchManager.Initialize(terrainInfos);
            if(mismatchManager.IsMismatched) return false;

            splatPrototypes = firstTerrainData.terrainLayers;
            return true;
        }

        private void OnDisable() {
            if(Settings.cached != null) Settings.cached.Save();
            
            foreach(UnityEngine.Object o in trackedObjects) {
                if(o == null) continue;
                DestroyImmediate(o);
            }
            trackedObjects.Clear(); // TODO: Is this **too** paranoid?

            Undo.undoRedoPerformed              -= UndoRedoPerformed;
            AssetWatcher.OnAssetsImported       -= OnAssetsImported;
            AssetWatcher.OnAssetsMoved          -= OnAssetsMoved;
            AssetWatcher.OnAssetsDeleted        -= OnAssetsDeleted;
            AssetWatcher.OnWillSaveAssetsAction -= OnWillSaveAssets;

            if(Settings.cached != null) {
                Settings.cached.AlwaysShowBrushSelectionChanged = null;
                Settings.cached.brushColour.ValueChanged = null;
            }
            
            Instance = null;
            if(activeInspectorInstanceID == GetInstanceID()) activeInspectorInstanceID = 0;

#if UNITY_2019_1_OR_NEWER
            SceneView.duringSceneGui -= OnSceneGUICallback;
#else
            SceneView.onSceneGUIDelegate -= OnSceneGUICallback;
#endif
        }

        private bool neighboursFoldout = false;
        public override void OnInspectorGUI() {
            bool displayingProblem = false;
            
            // Stop if the initialization was unsuccessful
            if(terrainInfos == null || terrainInfos.Count == 0) {
                EditorGUILayout.HelpBox("There is no terrain attached to this object, nor are there any terrain objects as children to this object.", MessageType.Info);
                return;
            }
            else if(firstTerrainData == null) {
                EditorGUILayout.HelpBox("Missing terrain data asset. Reassign the terrain asset in the Unity Terrain component.", MessageType.Error);
                displayingProblem = true;
            }

            bool containsAtleastOneTerrainCollider = false;
            bool hasOneOrMoreTerrainCollidersDisabled = false;
            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.collider == null) continue;
                containsAtleastOneTerrainCollider = true;

                if(ti.collider.enabled == false) hasOneOrMoreTerrainCollidersDisabled = true;

                break;
            }
            if(containsAtleastOneTerrainCollider == false) {
                if(terrainInfos.Count > 1) {
                    EditorGUILayout.HelpBox("There aren't any terrain colliders attached to any of the terrains in the terrain grid.", MessageType.Error);
                } else {
                    EditorGUILayout.HelpBox("This terrain object doesn't have a terrain collider attached to it.", MessageType.Error);
                }
                displayingProblem = true;
            }
            
            if(hasOneOrMoreTerrainCollidersDisabled) {
                EditorGUILayout.HelpBox("There is at least one terrain that has an inactive collider. Terrain editing functionality won't work on the affected terrain(s).", MessageType.Warning);
                displayingProblem = true;
            }

            if(target == null) {
                EditorGUILayout.HelpBox("There is no target object. Make sure Terrain Former is a component of a terrain object.", MessageType.Error);
                displayingProblem = true;
            }

            if(mismatchManager.IsInitialized) mismatchManager.Draw();

            if(Settings.cached == null) {
                EditorGUILayout.HelpBox("The Settings.tf file couldn't load and attempts to create a new one failed.", MessageType.Error);
                displayingProblem = true;
            }
            
            if(displayingProblem) return;
            
            if(Initialize() == false) return;
            
            TerrainFormerStyles.Initialize();
            
            EditorGUIUtility.labelWidth = CurrentTool == Tool.Settings ? 188f : 128f;

            CheckKeyboardShortcuts(Event.current);
            
            // The user couldn't modified the heightmap resolution outside of Terrain Former, so check for it here
            int heightmapResolution = firstTerrainData.heightmapResolution;
            if(lastHeightmapResolultion != -1 && lastHeightmapResolultion != heightmapResolution) {
                BrushSizeChanged();
                lastHeightmapResolultion = heightmapResolution;
            }
            
            /** 
            * Get the current Unity Terrain Inspector tool, and set the Terrain Former tool to none if the Unity Terrain
            * Inspector tool is not none.
            */
            if(unityTerrainInspectors != null && CurrentTool != Tool.None) {
                foreach(object inspector in unityTerrainInspectors) {
                    int unityTerrainTool = (int)unityTerrainSelectedTool.GetValue(inspector, null);
                    // If the tool is not "None" (-1), then the Terrain Former tool must be set to none
                    if(unityTerrainTool != -1) {
                        currentTool.Value = Tool.None;
                    }
                }
            }

            /*
            * Draw the toolbar
            */
            Rect toolbarRect = EditorGUILayout.GetControlRect(false, 22f, GUILayout.MaxWidth(285f));
            toolbarRect.x = Mathf.Round(EditorGUIUtility.currentViewWidth * 0.5f - toolbarRect.width * 0.5f - 5f); // Rounding is required to stop blurriness in older versions of Unity
            CurrentTool = (Tool)GUI.Toolbar(toolbarRect, (int)CurrentTool, toolsGUIContents);

            if(CurrentTool == Tool.None || activeInspectorInstanceID != GetInstanceID()) return;

            if(CurrentTool != Tool.None && CurrentTool < Tool.FirstNonMouse && (Event.current.type == EventType.MouseUp || Event.current.type == EventType.KeyUp)) {
                UpdateDirtyBrushSamples();
            }
            
            // Big bold label showing the current tool
            GUILayout.Label(toolsGUIContents[(int)CurrentTool].tooltip, TerrainFormerStyles.largeBoldLabel);

            switch(CurrentTool) {
                case Tool.Smooth:
                    Settings.cached.boxFilterSize = (EditorGUILayout.IntSlider(GUIContents.boxFilterSize, Settings.cached.boxFilterSize * 2 + 1, 3, MaxBoxFilterSize) -1) / 2;
                    Settings.cached.smoothingIterations = EditorGUILayout.IntSlider(GUIContents.smoothAllIterations, Settings.cached.smoothingIterations, 1, 10);
                    break;
                case Tool.SetHeight:
                    if(GUIUtilities.LeftFillAndRightButton(
                        fillControl: r => {
                            Settings.cached.setHeight = EditorGUI.Slider(r, "Set Height", Settings.cached.setHeight, 0f, terrainSize.y);
                        },
                        buttonContent: new GUIContent("Apply to Terrain"),
                        buttonWidth: 116
                    )) {
                        SetHeightAll(Settings.cached.setHeight / terrainSize.y);
                    }

                    break;
                case Tool.Flatten:
                    Settings.cached.flattenMode = (FlattenMode)EditorGUILayout.EnumPopup(GUIContents.flattenMode, Settings.cached.flattenMode);
                    break;
                case Tool.Mould:
                    Settings.cached.mouldToolBoxFilterSize = (EditorGUILayout.IntSlider(GUIContents.boxFilterSize, Settings.cached.mouldToolBoxFilterSize * 2 + 1, 3, MaxBoxFilterSize) - 1) / 2;

                    Settings.cached.mouldToolRaycastOffset = EditorGUILayout.FloatField(GUIContents.mouldHeightOffset, Settings.cached.mouldToolRaycastOffset);
                    Settings.cached.mouldToolRaycastTopDown = GUIUtilities.RadioButtonsControl(GUIContents.mouldToolRaycastTopDownContent, 
                        Settings.cached.mouldToolRaycastTopDown ? 0 : 1, GUIContents.mouldToolRaycastDirectionContents) == 0;
                    Settings.cached.mouldAllIterations = EditorGUILayout.IntSlider(GUIContents.mouldAllIterations, Settings.cached.mouldAllIterations, 1, 10);

                    break;
                case Tool.PaintTexture:
                    EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel);

                    Texture2D[] splatIcons = new Texture2D[splatPrototypes.Length];
                    for(int i = 0; i < splatIcons.Length; ++i) {
                        splatIcons[i] = AssetPreview.GetAssetPreview(splatPrototypes[i].diffuseTexture) ?? splatPrototypes[i].diffuseTexture;
                    }

                    Settings.cached.selectedTextureIndex = GUIUtilities.TextureSelectionGrid(Settings.cached.selectedTextureIndex, splatIcons);

                    Settings.cached.targetOpacity = EditorGUILayout.Slider("Target Opacity", Settings.cached.targetOpacity, 0f, 1f);
                    
                    break;
                case Tool.Heightmap:
                    EditorGUILayout.LabelField("Modification", EditorStyles.boldLabel);
                    if(GUIUtilities.LeftFillAndRightButton(
                        fillControl: r => {
                            Settings.cached.heightmapHeightOffset = EditorGUI.FloatField(r, "Offset Height", Settings.cached.heightmapHeightOffset);
                        },
                        buttonContent: new GUIContent("Apply to Terrain"),
                        buttonWidth: 115
                    )) {
                        OffsetTerrainGridHeight(Settings.cached.heightmapHeightOffset);
                    }

                    EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);

                    Settings.cached.heightmapSourceIsAlpha = GUIUtilities.RadioButtonsControl(new GUIContent("Source"), Settings.cached.heightmapSourceIsAlpha ? 1 : 0, GUIContents.heightmapSources) == 1;
                    
                    // Calling the Layout version of ObjectField will make a 64px sized picker with lots of dead space.
                    heightmapTexture = (Texture2D)EditorGUI.ObjectField(EditorGUILayout.GetControlRect(), "Heightmap Texture", heightmapTexture, typeof(Texture2D), false);

                    GUILayout.Space(4f);

                    GUI.enabled = heightmapTexture != null;
                    Rect importHeightmapButtonRect = EditorGUILayout.GetControlRect(GUILayout.Width(140f), GUILayout.Height(22f));
                    importHeightmapButtonRect.x = EditorGUIUtility.currentViewWidth * 0.5f - 70f;
                    if(GUI.Button(importHeightmapButtonRect, "Import Heightmap")) {
                        ImportHeightmap();
                    }
                    GUI.enabled = true;

                    break;
                case Tool.Generate:
                    Settings.cached.generateRampCurve = EditorGUILayout.CurveField("Falloff", Settings.cached.generateRampCurve);
                    if(Event.current.commandName == "CurveChanged") { 
                        ClampAnimationCurve(Settings.cached.generateRampCurve);
                        if(Event.current.type != EventType.Used && Event.current.type != EventType.Layout) Event.current.Use();
                    }
                    
                    Settings.cached.generateHeight = EditorGUILayout.Slider("Max Height", Settings.cached.generateHeight, 0f, terrainSize.y);

                    EditorGUILayout.LabelField("Linear Ramp", EditorStyles.boldLabel);
                    Settings.cached.generateRampCurveInXAxis = GUIUtilities.RadioButtonsControl(new GUIContent("Ramp Axis"), Settings.cached.generateRampCurveInXAxis ? 0 : 1,
                        GUIContents.generateRampCurveOptions) == 0;
                    Rect createLinearRampRect = EditorGUILayout.GetControlRect(GUILayout.Height(22f));
                    if(GUI.Button(new Rect(createLinearRampRect.xMax - 150f, createLinearRampRect.y, 145f, 22f), "Create Linear Ramp")) {
                        CreateLinearRamp(Settings.cached.generateHeight);
                    }

                    EditorGUILayout.LabelField("Circular Ramp", EditorStyles.boldLabel);
                    Rect createCircularRampRect = EditorGUILayout.GetControlRect(GUILayout.Height(22f));
                    if(GUI.Button(new Rect(createCircularRampRect.xMax - 160f, createCircularRampRect.y, 155f, 22f), "Create Circular Ramp")) {
                        CreateCircularRamp(Settings.cached.generateHeight);
                    }
                    
                    break;
                case Tool.Settings:
                    Rect goToPreferencesButtonRect = EditorGUILayout.GetControlRect(false, 22f);
                    goToPreferencesButtonRect.xMin = goToPreferencesButtonRect.xMax - 190f;
                    if(GUI.Button(goToPreferencesButtonRect, "Terrain Former Preferences")) {
                        SettingsService.OpenUserPreferences("Preferences/Terrain Former");
                    }
                    
                    EditorGUILayout.LabelField("Size", EditorStyles.boldLabel);

                    float newTerrainLateralSize = Mathf.Max(DelayedFloatField("Terrain Width/Length", firstTerrainData.size.x), 0f);
                    float newTerrainHeight = Mathf.Max(DelayedFloatField("Terrain Height", firstTerrainData.size.y), 0f);

                    bool terrainSizeChangedLaterally = newTerrainLateralSize != firstTerrainData.size.x;
                    if(terrainSizeChangedLaterally || newTerrainHeight != firstTerrainData.size.y) {
                        List<UnityEngine.Object> objectsThatWillBeModified = new List<UnityEngine.Object>();
                        
                        foreach(TerrainInfo ti in terrainInfos) {
                            objectsThatWillBeModified.Add(ti.terrainData);
                            if(terrainSizeChangedLaterally) objectsThatWillBeModified.Add(ti.transform);
                        }

                        // Calculate the center of the terrain grid and use that to decide where how to resposition the terrain grid cells.
                        Vector2 previousTerrainGridSize = new Vector2(numberOfTerrainsHorizontally * terrainSize.x, numberOfTerrainsVertically * terrainSize.z);
                        Vector3 centerOfTerrainGrid = new Vector3(terrainGridBottomLeft.x + previousTerrainGridSize.x * 0.5f, terrainGridBottomLeft.y,
                            terrainGridBottomLeft.z + previousTerrainGridSize.y * 0.5f);
                        Vector3 newTerrainGridSizeHalf = new Vector3(numberOfTerrainsHorizontally * newTerrainLateralSize * 0.5f, 0f, 
                            numberOfTerrainsVertically * newTerrainLateralSize * 0.5f);
                        
                        Undo.RegisterCompleteObjectUndo(objectsThatWillBeModified.ToArray(), terrainInfos.Count == 1 ? "Terrain Size Changed" : "Terrain Sizes Changed");
                        
                        foreach(TerrainInfo ti in terrainInfos) {
                            // Reposition the terrain grid (if there is more than one terrain) because the terrain size has changed laterally
                            if(terrainSizeChangedLaterally) {
                                ti.transform.position = new Vector3(
                                    centerOfTerrainGrid.x - newTerrainGridSizeHalf.x + ti.gridCellX * newTerrainLateralSize, 
                                    ti.transform.position.y,
                                    centerOfTerrainGrid.z - newTerrainGridSizeHalf.z + ti.gridCellY * newTerrainLateralSize
                                );
                            }

                            ti.terrainData.size = new Vector3(newTerrainLateralSize, newTerrainHeight, newTerrainLateralSize);
                        }

                        terrainSize = new Vector3(newTerrainLateralSize, newTerrainHeight, newTerrainLateralSize);
                    }

                    /**
                    * The following code is highly repetitive, but it must be written in this fashion. Writing this code in a more generalized fashion
                    * requires Reflection, but unfortunately virtually all properties are attributed with "MethodImplOptions.InternalCall", which as far as I
                    * know are not possible to be invoked using Reflection. As such, these properties must be set the manual way for all of their behaviours 
                    * to be executed.
                    */

                    EditorGUI.BeginChangeCheck();

                    // Base Terrain
                    bool newDrawHeightmap = EditorGUILayout.BeginToggleGroup("Base Terrain", firstTerrain.drawHeightmap);
                    if(firstTerrain.drawHeightmap != newDrawHeightmap) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.drawHeightmap = newDrawHeightmap;
                    }

                    EditorGUI.indentLevel = 1;
                    float newHeightmapPixelError = EditorGUILayout.Slider("Pixel Error", firstTerrain.heightmapPixelError, 1f, 200f);
                    if(firstTerrain.heightmapPixelError != newHeightmapPixelError) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.heightmapPixelError = newHeightmapPixelError;
                    }
                    
                    #if UNITY_2019_1_OR_NEWER
                    ShadowCastingMode newShadowMode = (ShadowCastingMode)EditorGUILayout.EnumPopup("Shadow Casting Mode", firstTerrain.shadowCastingMode);
                    if(firstTerrain.shadowCastingMode != newShadowMode) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.shadowCastingMode = newShadowMode;
                    }
                    #else
                    bool newCastShadows = EditorGUILayout.Toggle("Cast Shadows", firstTerrain.castShadows);
                    if(firstTerrain.castShadows != newCastShadows) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.castShadows = newCastShadows;
                    }
                    #endif
                    
                    #if UNITY_2019_2_OR_NEWER
                    Material newMaterialTemplate = (Material)EditorGUILayout.ObjectField("Material", firstTerrain.materialTemplate, typeof(Material), false);
                    if(firstTerrain.materialTemplate != newMaterialTemplate) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.materialTemplate = newMaterialTemplate;
                    }
                    #else
                    Terrain.MaterialType newMaterialType = (Terrain.MaterialType)EditorGUILayout.EnumPopup("Material Type", firstTerrain.materialType);
                    if(firstTerrain.materialType != newMaterialType) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.materialType = newMaterialType;
                    }

                    switch(newMaterialType) {
                        case Terrain.MaterialType.BuiltInLegacySpecular:
                            EditorGUI.indentLevel++;
                            Color newLegacySpecular = EditorGUILayout.ColorField("Specular Colour", firstTerrain.legacySpecular);
                            if(firstTerrain.legacySpecular != newLegacySpecular) {
                                foreach(TerrainInfo ti in terrainInfos) ti.terrain.legacySpecular = newLegacySpecular;
                            }

                            float newLegacyShininess = EditorGUILayout.Slider("Shininess", firstTerrain.legacyShininess, 0.03f, 1f);
                            if(firstTerrain.legacyShininess != newLegacyShininess) {
                                foreach(TerrainInfo ti in terrainInfos) ti.terrain.legacyShininess = newLegacyShininess;
                            }
                            EditorGUI.indentLevel--;
                            break;
                        case Terrain.MaterialType.Custom:
                            EditorGUI.indentLevel++;
                            Material newMaterialTemplate = (Material)EditorGUILayout.ObjectField("Custom Material", firstTerrain.materialTemplate, typeof(Material), false);
                            if(firstTerrain.materialTemplate != newMaterialTemplate) {
                                foreach(TerrainInfo ti in terrainInfos) ti.terrain.materialTemplate = newMaterialTemplate;
                            }

                            if(firstTerrain.materialTemplate != null && TerrainSettings.ShaderHasTangentChannel(firstTerrain.materialTemplate.shader))
                                EditorGUILayout.HelpBox("Materials with shaders that require tangent geometry shouldn't be used on terrains. " +
                                    "Instead, use one of the shaders found under Nature/Terrain.", MessageType.Warning, true);
                            EditorGUI.indentLevel--;
                            break;
                    }

                    if(newMaterialType == Terrain.MaterialType.BuiltInStandard || newMaterialType == Terrain.MaterialType.Custom) {
                    #endif
                        ReflectionProbeUsage newReflectionProbeUsage = (ReflectionProbeUsage)EditorGUILayout.EnumPopup("Reflection Probes", firstTerrain.reflectionProbeUsage);

                        List<ReflectionProbeBlendInfo> tempClosestReflectionProbes = new List<ReflectionProbeBlendInfo>();
                        foreach(TerrainInfo ti in terrainInfos) {
                            ti.terrain.reflectionProbeUsage = newReflectionProbeUsage;
                        }
                        
                        if(firstTerrain.reflectionProbeUsage != ReflectionProbeUsage.Off) {
                            GUI.enabled = false;

                            foreach(TerrainInfo ti in terrainInfos) {
                                ti.terrain.GetClosestReflectionProbes(tempClosestReflectionProbes);
                                
                                for(int i = 0; i < tempClosestReflectionProbes.Count; i++) {
                                    Rect controlRect = EditorGUILayout.GetControlRect(GUILayout.Height(16f));
                                    
                                    float xOffset = controlRect.x + 32f;
                                    
                                    if(terrainInfos.Count > 1) {
                                        GUI.Label(new Rect(xOffset, controlRect.y, 105f, 16f), new GUIContent(ti.terrain.name, ti.terrain.name), EditorStyles.miniLabel);
                                        xOffset += 105f;
                                    } else {
                                        GUI.Label(new Rect(xOffset, controlRect.y, 16f, 16f), "#" + i, EditorStyles.miniLabel);
                                        xOffset += 16f;
                                    }
                                    
                                    float objectFieldWidth = controlRect.width - 50f - xOffset;
                                    EditorGUI.ObjectField(new Rect(xOffset, controlRect.y, objectFieldWidth, 16f), tempClosestReflectionProbes[i].probe, typeof(ReflectionProbe), true);
                                    xOffset += objectFieldWidth;
                                    GUI.Label(new Rect(xOffset, controlRect.y, 65f, 16f), "Weight " + tempClosestReflectionProbes[i].weight.ToString("f2"), EditorStyles.miniLabel);
                                }
                            }
                            GUI.enabled = true;
                        }
                    #if !UNITY_2019_2_OR_NEWER
                    }
                    #endif

                    #if !UNITY_2019_3_OR_NEWER
                    float newThickness = EditorGUILayout.FloatField("Thickness", firstTerrainData.thickness);
                    if(firstTerrainData.thickness != newThickness) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrainData.thickness = newThickness;
                    }
                    #endif
                    EditorGUI.indentLevel = 0;

                    EditorGUILayout.EndToggleGroup();

                    // Tree and Detail Objects
                    bool newDrawTreesAndFoliage = EditorGUILayout.BeginToggleGroup("Tree and Detail Objects", firstTerrain.drawTreesAndFoliage);
                    if(firstTerrain.drawTreesAndFoliage != newDrawTreesAndFoliage) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.drawTreesAndFoliage = newDrawTreesAndFoliage;
                    }

                    EditorGUI.indentLevel = 1;
                    bool newBakeLightProbesForTrees = EditorGUILayout.Toggle("Bake Light Probes for Trees", firstTerrain.bakeLightProbesForTrees);
                    if(firstTerrain.bakeLightProbesForTrees != newBakeLightProbesForTrees) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.bakeLightProbesForTrees = newBakeLightProbesForTrees;
                    }

                    float newDetailObjectDistance = EditorGUILayout.Slider("Detail Distance", firstTerrain.detailObjectDistance, 0f, 250f);
                    if(firstTerrain.detailObjectDistance != newDetailObjectDistance) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.detailObjectDistance = newDetailObjectDistance;
                    }

                    bool newCollectDetailPatches = EditorGUILayout.Toggle(GUIContents.collectDetailPatches, firstTerrain.collectDetailPatches);
                    if(firstTerrain.collectDetailPatches != newCollectDetailPatches) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.collectDetailPatches = newCollectDetailPatches;
                    }

                    float newDetailObjectDensity = EditorGUILayout.Slider("Detail Density", firstTerrain.detailObjectDensity, 0f, 1f);
                    if(firstTerrain.detailObjectDensity != newDetailObjectDensity) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.detailObjectDensity = newDetailObjectDensity;
                    }

                    float newTreeDistance = EditorGUILayout.Slider("Tree Distance", firstTerrain.treeDistance, 0f, 2000f);
                    if(firstTerrain.treeDistance != newTreeDistance) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.treeDistance = newTreeDistance;
                    }
                    
                    float newTreeBillboardDistance = EditorGUILayout.Slider("Billboard Start", firstTerrain.treeBillboardDistance, 5f, 2000f);
                    if(firstTerrain.treeBillboardDistance != newTreeBillboardDistance) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.treeBillboardDistance = newTreeBillboardDistance;
                    }

                    float newTreeCrossFadeLength = EditorGUILayout.Slider("Fade Length", firstTerrain.treeCrossFadeLength, 0f, 200f);
                    if(firstTerrain.treeCrossFadeLength != newTreeCrossFadeLength) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.treeCrossFadeLength = newTreeCrossFadeLength;
                    }

                    int newTreeMaximumFullLODCount = EditorGUILayout.IntSlider("Max. Mesh Trees", firstTerrain.treeMaximumFullLODCount, 0, 10000);
                    if(firstTerrain.treeMaximumFullLODCount != newTreeMaximumFullLODCount) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.treeMaximumFullLODCount = newTreeMaximumFullLODCount;
                    }

                    EditorGUI.indentLevel = 0;

                    EditorGUILayout.EndToggleGroup();
                    // If any tree/detail/base terrain settings have changed, redraw the scene view
                    if(EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

                    GUILayout.Label("Wind Settings for Grass", EditorStyles.boldLabel);

                    float newWavingGrassStrength = EditorGUILayout.Slider("Strength", firstTerrainData.wavingGrassStrength, 0f, 1f);
                    if(firstTerrainData.wavingGrassStrength != newWavingGrassStrength) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrainData.wavingGrassStrength = newWavingGrassStrength;
                    }

                    float newWavingGrassSpeed = EditorGUILayout.Slider("Speed", firstTerrainData.wavingGrassSpeed, 0f, 1f);
                    if(firstTerrainData.wavingGrassSpeed != newWavingGrassSpeed) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrainData.wavingGrassSpeed = newWavingGrassSpeed;
                    }

                    float newWavingGrassAmount = EditorGUILayout.Slider("Bending", firstTerrainData.wavingGrassAmount, 0f, 1f);
                    if(firstTerrainData.wavingGrassAmount != newWavingGrassAmount) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrainData.wavingGrassAmount = newWavingGrassAmount;
                    }

                    Color newWavingGrassTint = EditorGUILayout.ColorField("Tint", firstTerrainData.wavingGrassTint);
                    if(firstTerrainData.wavingGrassTint != newWavingGrassTint) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrainData.wavingGrassTint = newWavingGrassTint;
                    }
                    
                    GUILayout.Label("Resolution", EditorStyles.boldLabel);

                    int newHeightmapResolution = EditorGUILayout.IntPopup(TerrainSettings.heightmapResolutionContent, firstTerrainData.heightmapResolution, 
                        TerrainSettings.heightmapResolutionsContents, TerrainSettings.heightmapResolutions);
                    if(firstTerrainData.heightmapResolution != newHeightmapResolution && 
                        EditorUtility.DisplayDialog("Terrain Former", "Changing the heightmap resolution will reset the heightmap.\n\nDo " +
                            "you want to change the heightmap resolution?", "Change Anyway", "Cancel")) {
                        RegisterUndoForTerrainGrid("Changed heightmap resolution");
                        foreach(TerrainInfo ti in terrainInfos) {
                            ti.terrainData.heightmapResolution = newHeightmapResolution;
                            ti.terrainData.size = terrainSize;
                        }
                        heightmapResolution = newHeightmapResolution;
                        Initialize(true);
                    }

                    int newAlphamapResolution = EditorGUILayout.IntPopup(TerrainSettings.alphamapResolutionContent, firstTerrainData.alphamapResolution, 
                        TerrainSettings.validTextureResolutionsContent, TerrainSettings.validTextureResolutions);
                    if(firstTerrainData.alphamapResolution != newAlphamapResolution &&
                        EditorUtility.DisplayDialog("Terrain Former", "Changing the alphamap resolution will reset the alphamap.\n\nDo you " +
                            "want to change the alphamap resolution?", "Change Anyway", "Cancel")) {
                        RegisterUndoForTerrainGrid("Changed alphmap resolution");
                        foreach(TerrainInfo ti in terrainInfos) ti.terrainData.alphamapResolution = newAlphamapResolution;
                        alphamapResolution = newAlphamapResolution;
                        Initialize(true);
                    }

                    int newBaseMapResolution = EditorGUILayout.IntPopup(TerrainSettings.basemapResolutionContent, firstTerrainData.baseMapResolution, 
                        TerrainSettings.validTextureResolutionsContent, TerrainSettings.validTextureResolutions);
                    if(firstTerrainData.baseMapResolution != newBaseMapResolution) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrainData.baseMapResolution = newBaseMapResolution;
                    }
                    
                    float newBasemapDistance = EditorGUILayout.Slider(TerrainSettings.basemapDistanceContent, firstTerrain.basemapDistance, 0f, 2000f);
                    if(firstTerrain.basemapDistance != newBasemapDistance) {
                        foreach(TerrainInfo ti in terrainInfos) ti.terrain.basemapDistance = newBasemapDistance;
                    }

                    // Detail Resolution
                    int newDetailResolution = Utilities.RoundToNearestAndClamp(DelayedIntField(TerrainSettings.detailResolutionContent, firstTerrainData.detailResolution),
                        8, 0, 4048);
                    // Update all detail layers if the detail resolution has changed.
                    if(newDetailResolution != firstTerrainData.detailResolution &&
                        EditorUtility.DisplayDialog("Terrain Former", "Changing the detail map resolution will clear all details.\n\nDo you " +
                            "want to change the detail map resolution?", "Change Anyway", "Cancel")) {
                        List<int[,]> detailLayers = new List<int[,]>();
                        for(int i = 0; i < firstTerrainData.detailPrototypes.Length; i++) {
                            detailLayers.Add(firstTerrainData.GetDetailLayer(0, 0, firstTerrainData.detailWidth, firstTerrainData.detailHeight, i));
                        }
                        foreach(TerrainInfo ti in terrainInfos) {
                            ti.terrainData.SetDetailResolution(newDetailResolution, 8);
                            for(int i = 0; i < detailLayers.Count; i++) {
                                ti.terrainData.SetDetailLayer(0, 0, i, detailLayers[i]);
                            }
                        }
                    }

                    // Detail Resolution Per Patch
                    int currentDetailResolutionPerPatch = TerrainSettings.GetDetailResolutionPerPatch(firstTerrainData);
                    int newDetailResolutionPerPatch = Mathf.Clamp(DelayedIntField(TerrainSettings.detailResolutionPerPatchContent, currentDetailResolutionPerPatch), 8, 128);
                    if(newDetailResolutionPerPatch != currentDetailResolutionPerPatch) {
                        foreach(TerrainInfo ti in terrainInfos) {
                            ti.terrainData.SetDetailResolution(firstTerrainData.detailResolution, newDetailResolutionPerPatch);
                        }
                    }

                    #if !UNITY_2019_2_OR_NEWER
                    if(firstTerrain.materialType != Terrain.MaterialType.Custom) {
                        firstTerrain.materialTemplate = null;
                    }
                    #endif

                    if(terrainInfos.Count == 1) {
                        terrainInfos[0].terrainData = (TerrainData)EditorGUILayout.ObjectField("Terrain Data Asset", terrainInfos[0].terrainData, typeof(TerrainData), false);
                    } else {
                        EditorGUILayout.LabelField("Terrain Data Assets", EditorStyles.boldLabel);
                        foreach(TerrainInfo ti in terrainInfos) {
                            ti.terrainData = (TerrainData)EditorGUILayout.ObjectField(ti.transform.name, ti.terrainData, typeof(TerrainData), false);
                        }
                    }

                    // Draw the terrain informations as visual representations
                    if(terrainInfos.Count > 1) {
                        GUILayout.Space(5f);
                        neighboursFoldout = GUIUtilities.FullClickRegionFoldout("Neighbours", neighboursFoldout, EditorStyles.foldout);
                        if(neighboursFoldout) {
                            Rect hoverRect = new Rect();
                            string hoverText = null;

                            const int neighboursCellSize = 30;
                            const int neighboursCellSizeMinusOne = neighboursCellSize - 1;

                            Rect neighboursGridRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 35f, numberOfTerrainsVertically * neighboursCellSize + 15f);
                            int neighboursGridRectWidth = neighboursCellSizeMinusOne * numberOfTerrainsHorizontally;
                            int neighboursGridRectHeight = neighboursCellSizeMinusOne * numberOfTerrainsVertically;
                            neighboursGridRect.yMin += 15f;
                            neighboursGridRect.xMin = EditorGUIUtility.currentViewWidth * 0.5f - neighboursGridRectWidth * 0.5f;
                            neighboursGridRect.width = neighboursGridRectWidth;

                            if(neighboursGridRect.Contains(Event.current.mousePosition)) Repaint();

                            GUIStyle boldLabelWithoutPadding = new GUIStyle(EditorStyles.boldLabel);
                            boldLabelWithoutPadding.padding = new RectOffset();
                            boldLabelWithoutPadding.alignment = TextAnchor.MiddleCenter;
                            // Axis Labels
                            GUI.Label(new Rect(EditorGUIUtility.currentViewWidth * 0.5f - 9f, neighboursGridRect.y - 15f, 20f, 10f), "Z", boldLabelWithoutPadding);
                            GUI.Label(new Rect(neighboursGridRect.xMax + 7f, neighboursGridRect.y + neighboursGridRectHeight * 0.5f - 6f, 10f, 10f), "X", boldLabelWithoutPadding);

                            Terrain selectedTerrain = null;
                            if(Selection.activeGameObject != null) {
                                selectedTerrain = Selection.activeGameObject.GetComponent<Terrain>();
                            }

                            foreach(TerrainInfo ti in terrainInfos) {
                                GUI.color = ti.terrain == selectedTerrain && !isTerrainGridParentSelected ? new Color(0.4f, 0.4f, 0.75f) : Color.white;
                                Rect cellRect = new Rect(neighboursGridRect.x + ti.gridCellX * neighboursCellSizeMinusOne, neighboursGridRect.y + 
                                    (numberOfTerrainsVertically - 1 - ti.gridCellY) * neighboursCellSizeMinusOne, neighboursCellSize, neighboursCellSize);
                                
                                if(cellRect.Contains(Event.current.mousePosition)) {
                                    if(Event.current.type == EventType.MouseUp) {
                                        EditorGUIUtility.PingObject(ti.terrain.gameObject);
                                    } else {
                                        hoverText = ti.terrain.name;
                                        if(isTerrainGridParentSelected == false && ti.terrain == selectedTerrain) hoverText += " (selected)";
                                        Vector2 calculatedSize = GUI.skin.box.CalcSize(new GUIContent(hoverText));
                                        hoverRect = new Rect(Mathf.Max(cellRect.x + 15f - calculatedSize.x * 0.5f, 0f), cellRect.y + calculatedSize.y + 5f, calculatedSize.x, calculatedSize.y);
                                    }
                                }
                                
                                if(numberOfTerrainsHorizontally >= 10 || numberOfTerrainsVertically >= 10) {
                                    TerrainFormerStyles.neighboursCellBox.fontSize = 8;
                                } else {
                                    TerrainFormerStyles.neighboursCellBox.fontSize = 8;
                                }
                                GUI.Box(cellRect, ti.gridCellX + 1 + "x" + (ti.gridCellY + 1), TerrainFormerStyles.neighboursCellBox);
                            }

                            GUI.color = Color.white;

                            if(hoverText != null) {
                                GUI.Box(hoverRect, hoverText);
                            }
                        }
                    }
                    break;
            }

            float lastLabelWidth = EditorGUIUtility.labelWidth;
            
            if(CurrentTool >= Tool.FirstNonMouse) return;

            GUILayout.Space(3f);

            /**
            * Brush Selection
            */
            if(Settings.cached.AlwaysShowBrushSelection || isSelectingBrush) {
                Rect brushesTitleRect = EditorGUILayout.GetControlRect();
                GUI.Label(brushesTitleRect, Settings.cached.AlwaysShowBrushSelection ? "Brushes" : "Select Brush", EditorStyles.boldLabel);

                if(Settings.cached.AlwaysShowBrushSelection) {
                    brushesTitleRect.xMin = brushesTitleRect.xMax - 300f;
                    GUI.Label(brushesTitleRect, CurrentBrush.name, TerrainFormerStyles.brushNameAlwaysShowBrushSelection);
                }
                
                if(Settings.cached.brushSelectionDisplayType == BrushSelectionDisplayType.Tabbed) {
                    string newBrushTab = GUIUtilities.BrushTypeToolbar(Settings.cached.modeSettings[CurrentTool].selectedBrushTab);
                    if(newBrushTab != Settings.cached.modeSettings[CurrentTool].selectedBrushTab) {
                        Settings.cached.modeSettings[CurrentTool].selectedBrushTab = newBrushTab;
                    }
                }

                string newlySelectedBrush = GUIUtilities.BrushSelectionGrid(Settings.cached.modeSettings[CurrentTool].selectedBrushId);
                if(newlySelectedBrush != Settings.cached.modeSettings[CurrentTool].selectedBrushId) {
                    Settings.cached.modeSettings[CurrentTool].selectedBrushId = newlySelectedBrush;
                    UpdateBrushTextures();
                }
            }

            if(Settings.cached.AlwaysShowBrushSelection) {
                GUILayout.Space(6f);
            } else if(isSelectingBrush) return;

            if(CurrentTool != Tool.RaiseOrLower) {
                GUILayout.Label("Brush", EditorStyles.boldLabel);
            }

            // The width of the area used to show the button to select a brush. Only applicable when AlwaysShowBrushSelection is false.
            float brushSelectionWidth = Mathf.Clamp(Settings.cached.brushPreviewSize + 28f, 80f, 84f);

            EditorGUILayout.BeginHorizontal(); // Brush Parameter Editor Horizontal Group
            
            // Draw Brush Paramater Editor
            if(Settings.cached.AlwaysShowBrushSelection) {
                EditorGUILayout.BeginVertical();
            } else {
                EditorGUILayout.BeginVertical(GUILayout.Width(EditorGUIUtility.currentViewWidth - brushSelectionWidth - 15f));
            }

            bool isBrushProcedural = CurrentBrush is ImageBrush == false;
            float maxBrushSize = terrainSize.x;
            if(CurrentTool == Tool.Smooth) {
                maxBrushSize -= Mathf.CeilToInt(Settings.cached.boxFilterSize * 2f / heightmapResolution * terrainSize.x);
            } else if(CurrentTool == Tool.Mould) {
                maxBrushSize -= Mathf.CeilToInt(Settings.cached.mouldToolBoxFilterSize * 2f / heightmapResolution * terrainSize.x);
            }
            float newBrushSize = EditorGUILayout.Slider("Size", Settings.cached.modeSettings[CurrentTool].brushSize, MinBrushSize, maxBrushSize);
            if(newBrushSize != Settings.cached.modeSettings[CurrentTool].brushSize) {
                Settings.cached.modeSettings[CurrentTool].brushSize = newBrushSize;
                BrushSizeChanged();
            }

            float newBrushSpeed = EditorGUILayout.Slider(CurrentTool == Tool.PaintTexture ? "Strength" : "Speed", 
                Settings.cached.modeSettings[CurrentTool].brushSpeed, minBrushSpeed, maxBrushSpeed);
            
            if(newBrushSpeed != Settings.cached.modeSettings[CurrentTool].brushSpeed) {
                Settings.cached.modeSettings[CurrentTool].brushSpeed = newBrushSpeed;
                BrushSpeedChanged();
            }
            
            GUIUtilities.LeftFillAndRightControl(
                fillControl: r => {
                    if(isBrushProcedural) {
                        EditorGUI.PrefixLabel(r, new GUIContent("Falloff"));
                    } else { 
                        Rect falloffToggleRect = new Rect(r);
                        falloffToggleRect.xMax = EditorGUIUtility.labelWidth;
                        bool newUseFalloffForCustomBrushes = EditorGUI.Toggle(falloffToggleRect, Settings.cached.modeSettings[CurrentTool].useFalloffForCustomBrushes);
                        if(newUseFalloffForCustomBrushes != Settings.cached.modeSettings[CurrentTool].useFalloffForCustomBrushes) {
                            Settings.cached.modeSettings[CurrentTool].useFalloffForCustomBrushes = newUseFalloffForCustomBrushes;
                            UpdateAllNecessaryPreviewTextures();
                            UpdateBrushProjectorTextureAndSamples();
                        }

                        Rect falloffToggleLabelRect = new Rect(falloffToggleRect);
                        falloffToggleLabelRect.xMin += 15f;
                        EditorGUI.PrefixLabel(falloffToggleLabelRect, new GUIContent("Falloff"));
                    }
                    Rect falloffAnimationCurveRect = new Rect(r);
                    falloffAnimationCurveRect.xMin = EditorGUIUtility.labelWidth + 14f;
                    Settings.cached.modeSettings[CurrentTool].brushFalloff = EditorGUI.CurveField(falloffAnimationCurveRect, Settings.cached.modeSettings[CurrentTool].brushFalloff);
                    if(Event.current.commandName == "CurveChanged") {
                        BrushFalloffChanged();
                        if(Event.current.type != EventType.Used && Event.current.type != EventType.Layout) Event.current.Use();
                    }
                },
                rightControl: r => {
                    using(new GUIUtilities.GUIEnabledBlock(isBrushProcedural || Settings.cached.modeSettings[CurrentTool].useFalloffForCustomBrushes)) {
                        Rect alphaFalloffLabelRect = new Rect(r);
                        alphaFalloffLabelRect.xMin += 13;
                        alphaFalloffLabelRect.xMax += 3;
                        GUI.Label(alphaFalloffLabelRect, "Invert");

                        Rect alphaFalloffRect = new Rect(r);
                        alphaFalloffRect.xMin--;
                        bool newUseAlphaFalloff = EditorGUI.Toggle(alphaFalloffRect, Settings.cached.modeSettings[CurrentTool].invertFalloff);
                        if(newUseAlphaFalloff != Settings.cached.modeSettings[CurrentTool].invertFalloff) {
                            Settings.cached.modeSettings[CurrentTool].invertFalloff = newUseAlphaFalloff;
                            UpdateAllNecessaryPreviewTextures();
                            UpdateBrushProjectorTextureAndSamples();
                        }
                    }
                },
                rightControlWidth: 54
            );

            if(isBrushProcedural == false && Settings.cached.modeSettings[CurrentTool].useFalloffForCustomBrushes == false) {
                GUI.enabled = false;
            }

            EditorGUI.indentLevel = 1;
            float newBrushRoundness = EditorGUILayout.Slider("Roundness", Settings.cached.modeSettings[CurrentTool].brushRoundness, 0f, 1f);
            if(newBrushRoundness != Settings.cached.modeSettings[CurrentTool].brushRoundness) {
                Settings.cached.modeSettings[CurrentTool].brushRoundness = newBrushRoundness;
                BrushRoundnessChanged();
            }
            EditorGUI.indentLevel = 0;

            if(isBrushProcedural == false && Settings.cached.modeSettings[CurrentTool].useFalloffForCustomBrushes == false) {
                GUI.enabled = true;
            }

            /**
            * Custom Brush Angle
            */
            GUI.enabled = CanBrushRotate();
            float newBrushAngle = EditorGUILayout.Slider("Angle", Settings.cached.modeSettings[CurrentTool].brushAngle, -180f, 180f);
            if(newBrushAngle != Settings.cached.modeSettings[CurrentTool].brushAngle) {
                float delta = Settings.cached.modeSettings[CurrentTool].brushAngle - newBrushAngle;
                Settings.cached.modeSettings[CurrentTool].brushAngle = newBrushAngle;
                BrushAngleDeltaChanged(delta);
            }
            GUI.enabled = true;

            /**
            * Invert Brush (for custom brushes only)
            */
            if(isBrushProcedural == false) {
                if(Settings.cached.invertBrushTexturesGlobally) {
                    GUI.enabled = false;
                    EditorGUILayout.Toggle("Invert", true);
                    GUI.enabled = true;
                } else {
                    bool newInvertBrushTexture = EditorGUILayout.Toggle("Invert", Settings.cached.modeSettings[CurrentTool].invertBrushTexture);
                    if(newInvertBrushTexture != Settings.cached.modeSettings[CurrentTool].invertBrushTexture) {
                        Settings.cached.modeSettings[CurrentTool].invertBrushTexture = newInvertBrushTexture;
                        InvertBrushTextureChanged();
                    }
                }
            }

            /**
            * Noise Brush Parameters
            */
            if(CurrentBrush is PerlinNoiseBrush) {
                EditorGUILayout.LabelField("Perlin Noise Brush", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                Settings.cached.perlinNoiseScale = EditorGUILayout.Slider("Scale", Settings.cached.perlinNoiseScale, 5f, 750f);
                if(EditorGUI.EndChangeCheck()) {
                    samplesDirty |= SamplesDirty.ProjectorTexture;
                    UpdateAllNecessaryPreviewTextures();
                }
                
                bool perlinNoiseMinMaxChanged = GUIUtilities.MinMaxWithFloatFields("Clipping", ref Settings.cached.perlinNoiseMin, ref Settings.cached.perlinNoiseMax, 0f, 1f, 3);
                if(perlinNoiseMinMaxChanged) {
                    samplesDirty |= SamplesDirty.ProjectorTexture;
                    UpdateAllNecessaryPreviewTextures();
                }
            }
            
            EditorGUILayout.EndVertical();

            if(Settings.cached.AlwaysShowBrushSelection == false) {
                GUILayout.Space(-4f);
                EditorGUILayout.BeginVertical(GUILayout.Width(brushSelectionWidth));
                GUILayout.Space(-2f);

                using(new EditorGUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();
                    GUILayout.Box(CurrentBrush.name, TerrainFormerStyles.miniLabelCentered, GUILayout.Width(brushSelectionWidth - 17f), GUILayout.Height(24f));
                    GUILayout.FlexibleSpace();
                }

                // Draw Brush Preview
                using(new EditorGUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();
                    if(GUILayout.Button(CurrentBrush.previewTexture, GUIStyle.none)) {
                        ToggleSelectingBrush();
                    }
                    GUILayout.FlexibleSpace();
                }

                // Draw Select/Cancel Brush Selection Button
                using(new EditorGUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();
                    if(GUILayout.Button("Select", TerrainFormerStyles.miniButtonWithoutMargin, GUILayout.Width(60f), GUILayout.Height(18f))) {
                        ToggleSelectingBrush();
                    }
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.EndHorizontal(); // Brush Parameter Editor Horizontal Group

            /**
            * Behaviour
            */
            behaviourGroupUnfolded = GUIUtilities.FullClickRegionFoldout("Behaviour", behaviourGroupUnfolded, TerrainFormerStyles.behaviourGroupFoldout);

            if(behaviourGroupUnfolded) {
                /**
                * TODO: Versions older than Unity 2017.1 don't handle out and ref keywords in blocks with parameters labels for some reason, so no
                * pretty parameter labels :(
                */
                const float minSpacingBounds = 0.1f;
                const float maxSpacingBounds = 30f;
                if(GUIUtilities.TogglMinMaxWithFloatFields(
                    "Random Spacing",                                       // label
                    ref Settings.cached.modeSettings[CurrentTool].useBrushSpacing, // toggleValue
                    ref Settings.cached.modeSettings[CurrentTool].minBrushSpacing, // minValue
                    ref Settings.cached.modeSettings[CurrentTool].maxBrushSpacing, // maxValue
                    minSpacingBounds,                                       // minValueBoundary
                    maxSpacingBounds,                                       // maxValueBoundary
                    5                                                       // significantDigits
                )) {
                    // If the min/max values were changed, assume the user wants brush spacing to be enabled.
                    Settings.cached.modeSettings[CurrentTool].useBrushSpacing = true;
                }

                const float minRandomOffset = 0.001f;
                float maxRandomOffset = Mathf.Min(firstTerrainData.heightmapResolution, firstTerrainData.heightmapResolution) * 0.5f;
                GUIUtilities.ToggleAndControl(
                    new GUIContent("Random Offset"),                        // label
                    ref Settings.cached.modeSettings[CurrentTool].useRandomOffset, // enableFillControl
                    r => {                                                // fillControl
                    EditorGUI.BeginChangeCheck();
                        Settings.cached.modeSettings[CurrentTool].randomOffset = EditorGUI.Slider(r, Settings.cached.modeSettings[CurrentTool].randomOffset, minRandomOffset, maxRandomOffset);
                        if(EditorGUI.EndChangeCheck()) {
                            Settings.cached.modeSettings[CurrentTool].useRandomOffset = true;
                        }
                    }
                );

                GUI.enabled = CanBrushRotate();
                if(GUIUtilities.TogglMinMaxWithFloatFields(
                    "Random Rotation",                                        // label
                    ref Settings.cached.modeSettings[CurrentTool].useRandomRotation, // toggleValue
                    ref Settings.cached.modeSettings[CurrentTool].minRandomRotation, // minValue
                    ref Settings.cached.modeSettings[CurrentTool].maxRandomRotation, // maxValue
                    -180f,                                  // minValueBoundary
                    180f,                                  // maxValueBoundary
                    5                                                         // significantDigits
                )) {
                    Settings.cached.modeSettings[CurrentTool].useRandomRotation = true;
                }
                GUI.enabled = true;
            }
            mismatchManager.Draw();

            EditorGUIUtility.labelWidth = lastLabelWidth;
        }

        private void OnSceneGUICallback(SceneView sceneView) {
            if(this == null) {
                OnDisable();
                return;
            }

            // There are magical times where Terrain Former didn't receive the OnDisable message and continues to subscribe to OnSceneGUI
            if(terrainFormer == null) {
                OnDisable();
                return;
            }
            if(Initialize() == false) return;

            InitialiseToolsGUIContents();

            if(currentTool.Value < 0) return;

            if(CurrentTool == Tool.None) {
                SetCursorEnabled(false);
            } else if((Event.current.control && mouseIsDown) == false) {
                UpdateProjector();
            }
            
            Event currentEvent = Event.current;
            
            int terrainEditorHash = "TerrainFormerEditor".GetHashCode();
            int controlId = GUIUtility.GetControlID(terrainEditorHash, FocusType.Passive);
            
            /**
            * Draw scene-view information
            */
            if(IsToolSculptive(currentTool.Value) && Settings.cached.showSceneViewInformation && 
                (Settings.cached.displaySceneViewCurrentHeight || Settings.cached.displaySceneViewCurrentTool || Settings.cached.displaySceneViewSculptOntoMode)) {
                /**
                * For some reason this must be set for the SceneViewPanel to be rendered correctly - this won't be an issue if it was simple called in a 
                * OnSceneGUI "message". However multiple OnSceneGUI calls don't come through if there are multiple inspectors tabs/windwos at once.
                */
                GUI.skin = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector);

                Handles.BeginGUI();

                if(TerrainFormerStyles.sceneViewInformationArea == null) {
                    TerrainFormerStyles.sceneViewInformationArea = new GUIStyle(GUI.skin.box);
                    TerrainFormerStyles.sceneViewInformationArea.padding = new RectOffset(5, 0, 5, 0);
                }
                if(TerrainFormerStyles.sceneViewInformationArea.normal.background == null || TerrainFormerStyles.sceneViewInformationArea.normal.background.name == "OL box") {
                    TerrainFormerStyles.sceneViewInformationArea.normal.background = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/SceneInfoPanel.PSD");
                    TerrainFormerStyles.sceneViewInformationArea.border = new RectOffset(12, 12, 12, 12);
                }

                int lines = Settings.cached.displaySceneViewCurrentHeight ? 1 : 0;
                lines += Settings.cached.displaySceneViewCurrentTool ? 1 : 0;
                lines += Settings.cached.displaySceneViewSculptOntoMode ? 1 : 0;

                GUILayout.BeginArea(new Rect(5f, 5f, 190f, 15f * lines + 18f), TerrainFormerStyles.sceneViewInformationArea);

                const float parameterLabelOffset = 7f;
                const float valueParameterLeftOffset = 90f;
                float yOffset = 7f;

                if(Settings.cached.displaySceneViewCurrentTool) {
                    GUI.Label(new Rect(parameterLabelOffset, yOffset, 135f, 18f), "Tool:");
                    string toolNameDisplay = toolsGUIContents[(int)currentTool.Value].tooltip;
                    if(CurrentTool == Tool.Flatten) toolNameDisplay += string.Format(" ({0})", Settings.cached.flattenMode);
                    GUI.Label(new Rect(valueParameterLeftOffset, yOffset, 135f, 18f), toolNameDisplay);
                    yOffset += 15f;
                }
                if(Settings.cached.displaySceneViewCurrentHeight) {
                    float height;
                    float heightAtMouse;
                    GUI.Label(new Rect(parameterLabelOffset, yOffset, 135f, 18f), "Height:");
                    if(currentEvent.control && mouseIsDown)                     height = lastClickPosition.y;
                    else if(GetTerrainHeightAtMousePosition(out heightAtMouse)) height = heightAtMouse;
                    else                                                        height = 0f;

                    GUI.Label(new Rect(valueParameterLeftOffset, yOffset, 135f, 18f), height.ToString("0.00"));
                    yOffset += 15f;
                }
                if(Settings.cached.displaySceneViewSculptOntoMode) {
                    GUI.Label(new Rect(parameterLabelOffset, yOffset, 135f, 18f), "Sculpt Onto:");
                    GUIContent sculptProjectionMode;
                    if(CurrentTool == Tool.SetHeight || CurrentTool == Tool.Flatten) {
                        sculptProjectionMode = new GUIContent("Plane (locked)");
                    } else {
                        sculptProjectionMode = GUIContents.raycastModes[Settings.cached.raycastOntoFlatPlane ? 0 : 1];
                    }
                    GUI.Label(new Rect(valueParameterLeftOffset, yOffset, 135f, 18f), sculptProjectionMode);
                }
                GUILayout.EndArea();
                Handles.EndGUI();
            }

            if(firstTerrain == null || firstTerrainData == null) return;

            CheckKeyboardShortcuts(currentEvent);

            if(GUIUtility.hotControl != 0 && GUIUtility.hotControl != controlId) return;
            if(IsToolSculptive(CurrentTool) == false) return;
            
            Vector3 mouseWorldspacePosition;
            bool doesMouseRaycastHit = GetMousePositionInWorldSpace(out mouseWorldspacePosition); 

            /**
            * Frame Selected (Shortcut: F)
            */
            if(currentEvent.type == EventType.ExecuteCommand && currentEvent.commandName == "FrameSelected") {
                if(doesMouseRaycastHit) {
                    SceneView.currentDrawingSceneView.LookAt(mouseWorldspacePosition, SceneView.currentDrawingSceneView.rotation, 
                        GetCurrentToolSettings().brushSize * 1.2f);
                } else {
                    float largestTerrainAxis = Mathf.Max(numberOfTerrainsHorizontally * terrainSize.x, numberOfTerrainsVertically * terrainSize.z);
                    Vector3 centerOfTerrainGrid = terrainGridBottomLeft + new Vector3(numberOfTerrainsHorizontally * terrainSize.x * 0.5f, 0f, 
                        numberOfTerrainsVertically * terrainSize.z * 0.5f);
                    SceneView.currentDrawingSceneView.LookAt(centerOfTerrainGrid, SceneView.currentDrawingSceneView.rotation, largestTerrainAxis * 1f);
                }
                currentEvent.Use();
            }

            EventType editorEventType = currentEvent.GetTypeForControl(controlId);
            // Update mouse-related fields
            if(currentEvent.isMouse) {
                if(mousePosition == Vector2.zero) {
                    lastMousePosition = currentEvent.mousePosition;
                } else {
                    lastMousePosition = mousePosition;
                }

                mousePosition = currentEvent.mousePosition;

                if(editorEventType == EventType.MouseDown) {
                    currentTotalMouseDelta = 0;
                } else if(mouseIsDown) {
                    currentTotalMouseDelta += mousePosition.y - lastMousePosition.y;
                }
            }
            
            // Only accept left clicks
            if(currentEvent.button != 0) return;

            switch(editorEventType) {
                case EventType.MouseMove:
                    // TODO: Only repaint if the brush cursor is visible
                    HandleUtility.Repaint();
                    break;
                // MouseDown will execute the same logic as MouseDrag
                case EventType.MouseDown:
                case EventType.MouseDrag:
                    /*
                    * Break if any of the following rules are true:
                    * 1) The event happening for this window is a MouseDrag event and the hotControl isn't this window
                    * 2) Alt + Click have been executed
                    * 3) The HandleUtllity finds a control closer to this control
                    */
                    if((editorEventType == EventType.MouseDrag && GUIUtility.hotControl != controlId) ||
                        currentEvent.alt || currentEvent.button != 0 ||
                        HandleUtility.nearestControl != controlId) {
                        break;
                    }

                    if(currentEvent.type != EventType.MouseDown) {
                        currentEvent.Use();
                        break;
                    }
                    currentEvent.Use();

                    commandsFirstFrame = true;

                    /**
                    * To make sure the initial press down always sculpts the terrain while spacing is active, set 
                    * the mouseSpacingDistance to a high value to always activate it straight away
                    */
                    mouseSpacingDistance = float.MaxValue;

                    UpdateRandomSpacing();
                    GUIUtility.hotControl = controlId;

                    switch(CurrentTool) {
                        case Tool.RaiseOrLower:
                            currentCommand = new RaiseOrLowerCommand(GetBrushSamplesWithSpeed());
                            break;
                        case Tool.Smooth:
                            currentCommand = new SmoothCommand(GetBrushSamplesWithSpeed(), Settings.cached.boxFilterSize);
                            break;
                        case Tool.SetHeight:
                            currentCommand = new SetHeightCommand(GetBrushSamplesWithSpeed());
                            break;
                        case Tool.Flatten:
                            currentCommand = new FlattenCommand(GetBrushSamplesWithSpeed(), (mouseWorldspacePosition.y - firstTerrainTransform.position.y) / terrainSize.y);
                            break;
                        case Tool.Mould:
                            currentCommand = new MouldCommand(GetBrushSamplesWithSpeed(), Settings.cached.mouldToolBoxFilterSize);
                            break;
                        case Tool.PaintTexture:
                            currentCommand = new PaintTextureCommand(GetBrushSamplesWithSpeed(), Settings.cached.selectedTextureIndex, splatPrototypes.Length, Settings.cached.targetOpacity);
                            break;
                    }

                    Vector3 hitPosition;
                    Vector2 uv;
                    if(Raycast(out hitPosition, out uv)) {
                        lastWorldspaceMousePosition = hitPosition;
                        lastClickPosition = hitPosition;
                        mouseIsDown = true;
                    } else if(currentEvent.shift && CurrentTool == Tool.SetHeight) {
                        mouseIsDown = true;
                    }
                    break;
                case EventType.MouseUp:
                    // Reset the hotControl to nothing as long as it matches the TerrainEditor controlID
                    if(GUIUtility.hotControl != controlId) break;

                    GUIUtility.hotControl = 0;
                    
                    foreach(TerrainInfo ti in terrainInfos) {
                        if(ti.commandArea == null) continue;

                        // Render all aspects of terrain (heightmap, trees and details)
                        ti.terrain.editorRenderFlags = TerrainRenderFlags.All;

                        if(CurrentTool == Tool.PaintTexture) {
                            ti.terrainData.SetBaseMapDirty();
                        }

                        if(Settings.cached.alwaysUpdateTerrainLODs == false) {
#if UNITY_2019_1_OR_NEWER
                            ti.terrainData.SyncHeightmap();
#else
                            ti.terrain.ApplyDelayedHeightmapModification();
#endif
                        }
                    }

                    gridPlane.SetActive(false);
                    
                    mouseIsDown = false;

                    currentCommand = null;
                    lastClickPosition = Vector3.zero;
                    
                    currentEvent.Use();
                    break;
                case EventType.KeyUp:
                    // If a key has been released, make sure any keyboard shortcuts have their changes applied via UpdateDirtyBrushSamples
                    UpdateDirtyBrushSamples();
                    break;
                case EventType.Repaint:
                    SetCursorEnabled(false);
                    break;
                case EventType.Layout:
                    if(CurrentTool == Tool.None) break;

                    // Sets the ID of the default control. If there is no other handle being hovered over, it will choose this value
                    HandleUtility.AddDefaultControl(controlId);
                    break;
            }

            if(mouseIsDown == false || doesMouseRaycastHit == false || editorEventType != EventType.Repaint) return;

            if(Settings.cached.modeSettings[CurrentTool].useBrushSpacing) {
                mouseSpacingDistance += (new Vector2(lastWorldspaceMousePosition.x, lastWorldspaceMousePosition.z) -
                    new Vector2(mouseWorldspacePosition.x, mouseWorldspacePosition.z)).magnitude;
            }

            Vector3 finalMousePosition;
            if(CurrentTool < Tool.FirstNonMouse && currentEvent.control) {
                finalMousePosition = lastClickPosition;
            } else {
                finalMousePosition = mouseWorldspacePosition;
            }

            // Apply the random offset to the mouse position (if necessary)
            if(currentEvent.control == false && Settings.cached.modeSettings[CurrentTool].useRandomOffset) {
                Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * Settings.cached.modeSettings[CurrentTool].randomOffset;
                finalMousePosition += new Vector3(randomOffset.x, 0f, randomOffset.y);
            }

            UpdateGlobalCommandCoordinates(finalMousePosition);

            /**
            * Calculate the command coordinates for each terrain information which determines which area of a given terrain (if at all) 
            * will have the current command applied to it
            */
            UpdateCommandCoordinatesForAllTerrains(finalMousePosition);

            /**
            * Update the grid position
            */
            if(Settings.cached.showSculptingGridPlane) {
                if(gridPlane.activeSelf == false) {
                    gridPlane.SetActive(true);
                }

                Vector3 gridPosition;
                // If the current tool is interactive, keep the grid at the lastGridPosition
                if(currentEvent.control) {
                    gridPosition = new Vector3(lastClickPosition.x, lastClickPosition.y + 0.01f, lastClickPosition.z);
                } else {
                    gridPosition = new Vector3(mouseWorldspacePosition.x, lastClickPosition.y + 0.01f, mouseWorldspacePosition.z);
                }
                float gridPlaneDistance = Mathf.Abs(lastClickPosition.y - SceneView.currentDrawingSceneView.camera.transform.position.y);
                float gridPlaneSize = Settings.cached.modeSettings[CurrentTool].brushSize * 1.2f;
                gridPlane.transform.position = gridPosition;
                gridPlane.transform.localScale = Vector3.one * gridPlaneSize;

                // Get the Logarithm of base 10 from the distance to get a power to mutliple the grid scale by
                float power = Mathf.Round(Mathf.Log10(gridPlaneDistance) - 1);

                // Make the grid appear as if it's being illuminated by the cursor but keeping the grids remain within unit size tiles
                gridPlaneMaterial.mainTextureOffset = new Vector2(gridPosition.x, gridPosition.z) / Mathf.Pow(10f, power);

                gridPlaneMaterial.mainTextureScale = new Vector2(gridPlaneSize, gridPlaneSize) / Mathf.Pow(10f, power);
            }

            /**
            * Only allow the various Behaviours to be active when control isn't pressed to make these behaviours 
            * not occur while using interactive tools
            */
            if(currentEvent.control == false) {
                float spacing = Settings.cached.modeSettings[CurrentTool].brushSize * randomSpacing;

                // If brush spacing is enabled, do not update the current command until the cursor has exceeded the required distance
                if(Settings.cached.modeSettings[CurrentTool].useBrushSpacing && mouseSpacingDistance < spacing) {
                    lastWorldspaceMousePosition = mouseWorldspacePosition;
                    return;
                } else {
                    UpdateRandomSpacing();
                    mouseSpacingDistance = 0f;
                }

                if(Settings.cached.modeSettings[CurrentTool].useRandomRotation && CanBrushRotate()) {
                    RotateTemporaryBrushSamples();
                    currentCommand.brushSamples = temporarySamples;
                }
            }

            UpdateDirtyBrushSamples();

            // Update the raycast mask before running the jobs since raycasting currently only can be done on the main thread.
            if(globalCommandArea != null && CurrentTool == Tool.Mould) {    
                int[] previousTerrainLayers = new int[terrainInfos.Count];
                for(int t = 0; t < terrainInfos.Count; t++) {
                    previousTerrainLayers[t] = terrainInfos[t].terrain.gameObject.layer;
                    terrainInfos[t].terrain.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
                }

                UpdateMouldToolRaycastMask();

                // Reset all terrain layers to their previous values.
                for(int t = 0; t < terrainInfos.Count; t++) {
                    terrainInfos[t].terrain.gameObject.layer = previousTerrainLayers[t];
                }
            }

            if((currentEvent.control && commandsFirstFrame && (CurrentTool == Tool.RaiseOrLower || CurrentTool == Tool.Flatten || CurrentTool == Tool.SetHeight)) ||
                CurrentTool == Tool.Mould || CurrentTool == Tool.Smooth) {
                UpdateHeightsCopies();
            }

            /**
            * Execute the current event
            */
            if(globalCommandArea != null) {
                int iterations;
                switch(CurrentTool) {
                    case Tool.Smooth:
                        iterations = Settings.cached.smoothingIterations;
                        break;
                    case Tool.Mould:
                        iterations = Settings.cached.mouldAllIterations;
                        break;
                    default:
                        iterations = 1;
                        break;
                }

                for(int i = 0; i < iterations; i++) { 
                    currentCommand.Execute(currentEvent, globalCommandArea);
                }
            }

            // Copy the results of Smooth and Mould here since they rely on a seperate heightsCopy1 array
            if(CurrentTool == Tool.Smooth || CurrentTool == Tool.Mould) {
                int smoothRadius = CurrentTool == Tool.Smooth ? Settings.cached.boxFilterSize : Settings.cached.mouldToolBoxFilterSize;
                int leftMostGridPos = int.MaxValue;
                int bottomMostGridPos = int.MaxValue;
                foreach(TerrainInfo ti in terrainInfos) {
                    if(ti.commandArea == null) continue;
                    if(ti.gridCellX < leftMostGridPos) {
                        leftMostGridPos = ti.gridCellX;
                    }
                    if(ti.gridCellY < bottomMostGridPos) {
                        bottomMostGridPos = ti.gridCellY;
                    }
                }

                float[,] brushSamples = GetBrushSamplesWithSpeed();
                float brushSample;
                HeightsCacheBlock heightsCache;

                foreach(TerrainInfo ti in terrainInfos) {
                    if(ti.commandArea == null) continue;

                    heightsCache = ti.heightsCache;
                    int xStart = ti.commandArea.x;
                    int xEnd = xStart + ti.commandArea.width;
                    int yStart = ti.commandArea.y;
                    int yEnd = yStart + ti.commandArea.height;

                    int xSamplesOffset = Math.Max(xStart + ti.toolOffsetX - globalCommandArea.x, 0) - xStart + smoothRadius;
                    int ySamplesOffset = Math.Max(yStart + ti.toolOffsetY - globalCommandArea.y, 0) - yStart + smoothRadius;
                    int brushXOffset = ti.commandArea.x - ti.commandArea.clippedLeft;
                    int brushYOffset = ti.commandArea.y - ti.commandArea.clippedBottom;

                    int y, x;
                    for(x = xStart; x < xEnd; x++) {
                        for(y = yStart; y < yEnd; y++) {
                            brushSample = brushSamples[x - brushXOffset, y - brushYOffset];
                            if(brushSample < brushSampleEpsilon) continue;

                            heightsCache.data[y, x] -= (heightsCache.data[y, x] - heightsCopy1[y + ySamplesOffset, x + xSamplesOffset]) * brushSample;
                        }
                    }
                }
            }

            cylinderCursor.SetActive(true);

            commandsFirstFrame = false;

            ApplyTerrainCommandChanges(false);

            lastWorldspaceMousePosition = mouseWorldspacePosition;
        }

        private bool CanBrushRotate() {
            return CurrentBrush.GetType() != typeof(FalloffBrush) || Settings.cached.modeSettings[CurrentTool].brushRoundness != 1f;
        }

        private void UpdateMouldToolRaycastMask() {
            ResizeToolScratchArray(globalCommandArea.width, globalCommandArea.height);

            float gridPositionX = 1f / (heightmapWidth - 1) * numberOfTerrainsHorizontally * terrainSize.x;
            float gridPositionY = 1f / (heightmapHeight - 1) * numberOfTerrainsVertically * terrainSize.z;
                        
            const int boxCastSizeInSamples = 8;
            const float halfBoxCastSizeInSamples = boxCastSizeInSamples / 2f;
            const float extraBoxCastGap = 0.2501f; // There must be a slight gap since things at the exact y-position of the boxcast will be ignored.
            float boxCastLength = terrainSize.y + extraBoxCastGap;
            float boxCastHalfSize = halfBoxCastSizeInSamples / heightmapResolution * terrainSize.x;
            Vector3 boxCastExtents = new Vector3(boxCastHalfSize, 0.125f, boxCastHalfSize);

            float raycastYOrigin = Settings.cached.mouldToolRaycastTopDown ? terrainGridBottomLeft.y + terrainSize.y : terrainGridBottomLeft.y;

            float brushSample;
            RaycastHit hitInfo;
            int tilesY = Mathf.CeilToInt((float)globalCommandArea.height / boxCastSizeInSamples);
            int tilesX = Mathf.CeilToInt((float)globalCommandArea.width / boxCastSizeInSamples);
            for(int tY = 0; tY < tilesY; tY++) {
                for(int tX = 0; tX < tilesX; tX++) {
                    int yStart = tY * boxCastSizeInSamples;
                    int yEnd = Math.Min(yStart + boxCastSizeInSamples, globalCommandArea.height);
                    int xStart = tX * boxCastSizeInSamples;
                    int xEnd = Math.Min(xStart + boxCastSizeInSamples, globalCommandArea.width);

                    // Instead of doing a raycast per segment, do a boxcast first to see if there is even anything there
                    Vector3 boxCastHighOrigin = new Vector3(
                        x: terrainGridBottomLeft.x + (tX * boxCastSizeInSamples + halfBoxCastSizeInSamples + globalCommandArea.x) * gridPositionX,
                        y: terrainGridBottomLeft.y + terrainSize.y + extraBoxCastGap,
                        z: terrainGridBottomLeft.z + (tY * boxCastSizeInSamples + halfBoxCastSizeInSamples + globalCommandArea.y) * gridPositionY
                    );
                    Vector3 boxCastLowOrigin = new Vector3(
                        x: terrainGridBottomLeft.x + (tX * boxCastSizeInSamples + halfBoxCastSizeInSamples + globalCommandArea.x) * gridPositionX,
                        y: terrainGridBottomLeft.y,
                        z: terrainGridBottomLeft.z + (tY * boxCastSizeInSamples + halfBoxCastSizeInSamples + globalCommandArea.y) * gridPositionY
                    );
                    
                    /**
                    * Unfortunately we need to box cast in both directions to handle the cases where the box cast fails while raycasts would hit such as 
                    * when there is protruding objects going past the bounds of the terrain
                    */
                    if(Physics.BoxCast(boxCastHighOrigin, boxCastExtents, Utilities.downDirection, out hitInfo, Quaternion.identity, boxCastLength,
                        Utilities.ignoreRaycastLayerMask, QueryTriggerInteraction.Ignore) == false &&
                        Physics.BoxCast(boxCastLowOrigin, boxCastExtents, Utilities.upDirection, out hitInfo, Quaternion.identity, boxCastLength,
                        Utilities.ignoreRaycastLayerMask, QueryTriggerInteraction.Ignore) == false
                        ) {
                        for(int y = yStart; y < yEnd; y++) {
                            for(int x = xStart; x < xEnd; x++) {
                                toolScratchArray[x, y] = -1f;
                            }
                        }
                        continue;
                    }

                    float heightCoefficient = 1f / terrainSize.y;

                    for(int y = yStart; y < yEnd; y++) {
                        for(int x = xStart; x < xEnd; x++) {
                            brushSample = currentCommand.brushSamples[x + globalCommandArea.clippedLeft, y + globalCommandArea.clippedBottom];
                            if(brushSample == 0f) continue;

                            if(Physics.defaultPhysicsScene.Raycast(
                                new Vector3(                                                                        // origin
                                    x: terrainGridBottomLeft.x + (x + globalCommandArea.x) * gridPositionX,
                                    y: raycastYOrigin,
                                    z: terrainGridBottomLeft.z + (y + globalCommandArea.y) * gridPositionY),
                                Settings.cached.mouldToolRaycastTopDown ? Utilities.downDirection : Utilities.upDirection, // direction
                                out hitInfo,                                                                        // hitInfo
                                terrainSize.y,                                                                      // maxDistance
                                Utilities.ignoreRaycastLayerMask                                                    // layerMask
                                ,QueryTriggerInteraction.Ignore                                                     // queryTriggerInteraction
                                )) {
                                toolScratchArray[x, y] = (hitInfo.point.y - terrainGridBottomLeft.y) * heightCoefficient;
                            } else {
                                toolScratchArray[x, y] = -1f;
                            }
                        }
                    }
                }
            }
        }

        internal static void ResizeToolScratchArray(int width, int height) {
            // Grow the generic tool scratch like a list, but we still have the performance of an array.
            if(width > toolScratchArray.GetLength(0) || height > toolScratchArray.GetLength(1)) {
                int longestCommandAreaAxis = Math.Max(width, height);
                int nearestRoundedSize = (int)Math.Pow(2d, Math.Ceiling(Math.Log(longestCommandAreaAxis - 1, 2d))) + 1;
                toolScratchArray = new float[nearestRoundedSize, nearestRoundedSize];
            }
        }

        // We don't want to allocate this every frame, but we do have to pollute this class even more with a one use field
        private static float[,,] newAlphamaps;
        private static float[,] newHeights;
        private void ApplyTerrainCommandChanges(bool overrideAlwaysUpdateTerrainLODs) {
            // Update each terrainInfo's updated terrain region
            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.commandArea == null) continue;

                ti.terrain.editorRenderFlags = TerrainRenderFlags.Heightmap;
                CommandArea commandArea = ti.commandArea;

                if(currentCommand is PaintTextureCommand) {
                    AlphamapsCacheBlock alphamapsCache = ti.alphamapsCache;

                    if(newAlphamaps == null || newAlphamaps.GetLength(0) != ti.commandArea.height || newAlphamaps.GetLength(1) != ti.commandArea.width ||
                        newAlphamaps.GetLength(2) != firstTerrainData.alphamapLayers) {
                        newAlphamaps = new float[ti.commandArea.height, ti.commandArea.width, firstTerrainData.alphamapLayers];
                    }
                    for(int l = 0; l < firstTerrainData.alphamapLayers; l++) {
                        for(int x = 0; x < ti.commandArea.width; x++) {
                            for(int y = 0; y < ti.commandArea.height; y++) {
                                newAlphamaps[y, x, l] = alphamapsCache.data[commandArea.y + y, commandArea.x + x, l];
                            }
                        }
                    }

                    ti.terrainData.SetAlphamaps(ti.commandArea.x, ti.commandArea.y, newAlphamaps);
                    ti.terrainData.SetBaseMapDirty();
                } else { 
                    HeightsCacheBlock heightsCache = ti.heightsCache;

                    if(newHeights == null || newHeights.GetLength(0) != commandArea.height || newHeights.GetLength(1) != commandArea.width) {
                        newHeights = new float[commandArea.height, commandArea.width];
                    }

                    for(int x = 0; x < commandArea.width; x++) {
                        for(int y = 0; y < commandArea.height; y++) {
                            newHeights[y, x] = heightsCache.data[commandArea.y + y, commandArea.x + x];
                        }
                    }

                    if(Settings.cached.alwaysUpdateTerrainLODs || overrideAlwaysUpdateTerrainLODs) {
                        ti.terrainData.SetHeights(commandArea.x, commandArea.y, newHeights);
                    } else {
                        ti.terrainData.SetHeightsDelayLOD(commandArea.x, commandArea.y, newHeights);
                    }
                }
            }
        }

        internal static bool IsToolSculptive(Tool tool) {
            return tool > Tool.None && tool < Tool.FirstNonMouse;
        }

        [SettingsProvider]
        private static SettingsProvider SettingsProvider() {
            var provider = new SettingsProvider("Preferences/Terrain Former", SettingsScope.User) {
                label = "Terrain Former",
                guiHandler = (searchContext) => { 
                    DrawPreferences();
                },
                keywords = new HashSet<string>(new[] { "Terrain" })
            };

            return provider;
        }

        private static Vector2 preferencesItemScrollPosition;
        private static void DrawPreferences() {
            Settings.Create();

            if(Settings.cached == null) {
                EditorGUILayout.HelpBox("There was a problem in initializing Terrain Former's Settings.cached.", MessageType.Warning);
                return;
            }

            EditorGUIUtility.labelWidth = 185f;

            preferencesItemScrollPosition = EditorGUILayout.BeginScrollView(preferencesItemScrollPosition);
            GUILayout.Label("General", EditorStyles.boldLabel);

            // Raycast Onto Plane
            Settings.cached.raycastOntoFlatPlane = GUIUtilities.RadioButtonsControl(GUIContents.raycastModeLabel, Settings.cached.raycastOntoFlatPlane ? 
                0 : 1, GUIContents.raycastModes) == 0;
                        
            // Show Sculpting Grid Plane
            EditorGUI.BeginChangeCheck();
            Settings.cached.showSculptingGridPlane = EditorGUILayout.Toggle(GUIContents.showSculptingGridPlane, Settings.cached.showSculptingGridPlane);
            if(EditorGUI.EndChangeCheck()) {
                SceneView.RepaintAll();
            }

            EditorGUIUtility.fieldWidth += 5f;
            Settings.cached.brushColour.Value = EditorGUILayout.ColorField("Brush Colour", Settings.cached.brushColour);
            EditorGUIUtility.fieldWidth -= 5f;
            
            Settings.cached.alwaysUpdateTerrainLODs = GUIUtilities.RadioButtonsControl(GUIContents.alwaysUpdateTerrainLODs, Settings.cached.alwaysUpdateTerrainLODs ? 
                0 : 1, GUIContents.alwaysUpdateTerrain) == 0;

            bool newInvertBrushTexturesGlobally = EditorGUILayout.Toggle("Invert Brush Textures Globally", Settings.cached.invertBrushTexturesGlobally);
            if(newInvertBrushTexturesGlobally != Settings.cached.invertBrushTexturesGlobally) {
                Settings.cached.invertBrushTexturesGlobally = newInvertBrushTexturesGlobally;
                if(Instance != null && IsToolSculptive(Instance.CurrentTool)) {
                    Instance.UpdateAllNecessaryPreviewTextures();
                    Instance.UpdateBrushProjectorTextureAndSamples();
                    Instance.Repaint();
                }
            }

            GUILayout.Label("User Interface", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            bool newAlwaysShowBrushSelection = EditorGUILayout.Toggle(GUIContents.alwaysShowBrushSelection, Settings.cached.AlwaysShowBrushSelection);
            if(newAlwaysShowBrushSelection != Settings.cached.AlwaysShowBrushSelection) {
                Settings.cached.AlwaysShowBrushSelection = newAlwaysShowBrushSelection;
                if(Instance != null) Instance.UpdateAllNecessaryPreviewTextures();
            }

            Settings.cached.brushSelectionDisplayType = (BrushSelectionDisplayType)EditorGUILayout.Popup("Brush Selection Display Type",
                (int)Settings.cached.brushSelectionDisplayType, GUIContents.brushSelectionDisplayTypeLabels);

            Rect previewSizeRect = EditorGUILayout.GetControlRect();
            Rect previewSizePopupRect = EditorGUI.PrefixLabel(previewSizeRect, new GUIContent("Brush Preview Size"));
            previewSizePopupRect.xMax -= 2;
            
            int newBrushPreviewSize = EditorGUI.IntPopup(previewSizePopupRect, Settings.cached.brushPreviewSize, GUIContents.previewSizesContent, new int[] { 32, 48, 64 });
            if(newBrushPreviewSize != Settings.cached.brushPreviewSize) {
                Settings.cached.brushPreviewSize = newBrushPreviewSize;
                if(Instance != null) Instance.UpdateAllNecessaryPreviewTextures();
            }
            if(EditorGUI.EndChangeCheck()) {
                if(Instance != null) Instance.Repaint();
            }

            GUILayout.Space(2f);

            EditorGUI.BeginChangeCheck();
            Settings.cached.showSceneViewInformation = EditorGUILayout.BeginToggleGroup("Show Scene View Information", Settings.cached.showSceneViewInformation);
            EditorGUI.indentLevel = 1;
            GUI.enabled = Settings.cached.showSceneViewInformation;
            Settings.cached.displaySceneViewCurrentTool = EditorGUILayout.Toggle("Display Current Tool", Settings.cached.displaySceneViewCurrentTool);
            Settings.cached.displaySceneViewCurrentHeight = EditorGUILayout.Toggle("Display Current Height", Settings.cached.displaySceneViewCurrentHeight);
            Settings.cached.displaySceneViewSculptOntoMode = EditorGUILayout.Toggle("Display Sculpt Onto", Settings.cached.displaySceneViewSculptOntoMode);
            EditorGUILayout.EndToggleGroup();
            EditorGUI.indentLevel = 0;
            GUI.enabled = true;
            if(EditorGUI.EndChangeCheck()) {
                SceneView.RepaintAll();
            }
            
            GUILayout.Label("Shortcuts", EditorStyles.boldLabel);
            foreach(Shortcut shortcut in Shortcut.Shortcuts.Values) {
                shortcut.DoShortcutField();
            }

            using(new EditorGUILayout.HorizontalScope()) {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Press Spacebar/Enter to unbind shortcut, Escape to cancel", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            // If all the settings are at their default value, disable the "Restore Defaults"
            bool shortcutsNotDefault = false;
            foreach(Shortcut shortcut in Shortcut.Shortcuts.Values) {
                if(shortcut.Binding != shortcut.defaultBinding) {
                    shortcutsNotDefault = true;
                    break;
                }
            }
            
            if(Settings.cached.AreSettingsDefault() && shortcutsNotDefault == false) {
                GUI.enabled = false;
            }
            if(GUILayout.Button("Restore Defaults", GUILayout.Width(120f), GUILayout.Height(20))) {
                if(EditorUtility.DisplayDialog("Restore Defaults", "Are you sure you want to restore all settings to their defaults?", "Restore Defaults", "Cancel")) {
                    Settings.cached.RestoreDefaultSettings();

                    // Reset shortcuts to defaults
                    foreach(Shortcut shortcut in Shortcut.Shortcuts.Values) {
                        shortcut.waitingForInput = false;
                        shortcut.Binding = shortcut.defaultBinding;
                    }

                    if(Instance != null) Instance.Repaint();
                }
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }
        
        /**
        * Command Coordinates for Terrain Grid returns coordinates taking into account the entire terrain grid, and not taking into account per-terrain coordinates
        * which vary due to the fact the terrain grid has 1 redundant sample per axis. For code comments refer to UpdateCommandCoordinatesForAllTerrains.
        */
        private void UpdateGlobalCommandCoordinates(Vector3 mousePosition) {
            float terrainGridHorizontalSize = numberOfTerrainsHorizontally * terrainSize.x;
            float terrainGridVerticalSize = numberOfTerrainsVertically * terrainSize.z;

            Vector2 mousePositionVersusBottomLeftPosition = new Vector2(mousePosition.x - terrainGridBottomLeft.x,
                mousePosition.z - terrainGridBottomLeft.z);
            
            int cursorLeft = Mathf.RoundToInt(mousePositionVersusBottomLeftPosition.x / terrainGridHorizontalSize * (toolSamplesHorizontally - numberOfTerrainsHorizontally));
            int cursorBottom = Mathf.RoundToInt(mousePositionVersusBottomLeftPosition.y / terrainGridVerticalSize * (toolSamplesVertically - numberOfTerrainsVertically));
            
            int leftOffset = Mathf.Max(cursorLeft - halfBrushSizeInSamples, 0);
            int bottomOffset = Mathf.Max(cursorBottom - halfBrushSizeInSamples, 0);
            
            if(leftOffset >= toolSamplesHorizontally || bottomOffset >= toolSamplesVertically || cursorLeft + halfBrushSizeInSamples < 0 || 
                cursorBottom + halfBrushSizeInSamples < 0) {
                globalCommandArea = null;
                return;
            }

            if(globalCommandArea == null) globalCommandArea = new CommandArea();

            int clippedLeft;
            if(cursorLeft - halfBrushSizeInSamples < 0) {
                clippedLeft = Mathf.CeilToInt(Mathf.Abs(cursorLeft - halfBrushSizeInSamples));
            } else clippedLeft = 0;

            int clippedBottom;
            if(cursorBottom - halfBrushSizeInSamples < 0) {
                clippedBottom = Mathf.CeilToInt(Mathf.Abs(cursorBottom - halfBrushSizeInSamples));
            } else clippedBottom = 0;
            
            if(leftOffset + brushSizeInSamples > toolSamplesHorizontally) {
                globalCommandArea.width = toolSamplesHorizontally - leftOffset - clippedLeft;
            } else {
                globalCommandArea.width = brushSizeInSamples - clippedLeft;
            }

            if(bottomOffset + brushSizeInSamples > toolSamplesVertically) {
                globalCommandArea.height = toolSamplesVertically - bottomOffset - clippedBottom;
            } else {
                globalCommandArea.height = brushSizeInSamples - clippedBottom;
            }

            globalCommandArea.x = leftOffset;
            globalCommandArea.y = bottomOffset;
            globalCommandArea.clippedLeft = clippedLeft;
            globalCommandArea.clippedBottom = clippedBottom;
        }

        private void UpdateCommandCoordinatesForAllTerrains(Vector3 mousePosition) {
            foreach(TerrainInfo ti in terrainInfos) {
                float terrainGridHorizontalSize = numberOfTerrainsHorizontally * terrainSize.x;
                float terrainGridVerticalSize = numberOfTerrainsVertically * terrainSize.z;

                Vector2 mousePositionVersusBottomLeftPosition = new Vector2(mousePosition.x - terrainGridBottomLeft.x,
                    mousePosition.z - terrainGridBottomLeft.z);
            
                int gridX = Mathf.FloorToInt(mousePositionVersusBottomLeftPosition.x / terrainSize.x);
                int gridY = Mathf.FloorToInt(mousePositionVersusBottomLeftPosition.y / terrainSize.z);

                ti.gridRelativeOffsetX = Mathf.RoundToInt(mousePositionVersusBottomLeftPosition.x / terrainGridHorizontalSize * (toolSamplesHorizontally - 1 - gridX));
                ti.gridRelativeOffsetY = Mathf.RoundToInt(mousePositionVersusBottomLeftPosition.y /   terrainGridVerticalSize * (toolSamplesVertically - 1 - gridY));

                ti.gridRelativeOffsetX = Mathf.Max(ti.gridRelativeOffsetX - halfBrushSizeInSamples, 0);
                ti.gridRelativeOffsetY = Mathf.Max(ti.gridRelativeOffsetX - halfBrushSizeInSamples, 0);

                // Note the decrement of one at the end of both cursor calculations, this is because it's 0 based, not 1 based
                int cursorLeft = Mathf.RoundToInt((mousePosition.x - ti.transform.position.x) / terrainSize.x * (currentToolsResolution - 1));
                int cursorBottom = Mathf.RoundToInt((mousePosition.z - ti.transform.position.z) / terrainSize.z * (currentToolsResolution - 1));

                // The bottom-left segments of where the brush samples will start.
                int leftOffset = Mathf.Max(cursorLeft - halfBrushSizeInSamples, 0);
                int bottomOffset = Mathf.Max(cursorBottom - halfBrushSizeInSamples, 0);

                // Check if there aren't any segments that will even be sculpted/painted
                if(leftOffset >= currentToolsResolution || bottomOffset >= currentToolsResolution || cursorLeft + halfBrushSizeInSamples < 0 || cursorBottom + halfBrushSizeInSamples < 0) {
                    ti.commandArea = null;
                    continue;
                }

                if(ti.commandArea == null) ti.commandArea = new CommandArea();

                /** 
                * Create a paint patch used for offsetting the terrain samples.
                * Clipped left contains how many segments are being clipped to the left side of the terrain. The value is 0 if there 
                * are no segments being clipped. This same pattern applies to clippedBottom, clippedWidth, and clippedHeight respectively.
                */
                int clippedLeft;
                if(cursorLeft - halfBrushSizeInSamples < 0) {
                    clippedLeft = Mathf.CeilToInt(Mathf.Abs(cursorLeft - halfBrushSizeInSamples));
                } else clippedLeft = 0;

                int clippedBottom;
                if(cursorBottom - halfBrushSizeInSamples < 0) {
                    clippedBottom = Mathf.CeilToInt(Mathf.Abs(cursorBottom - halfBrushSizeInSamples));
                } else clippedBottom = 0;
                
                if(leftOffset + brushSizeInSamples > currentToolsResolution) {
                    ti.commandArea.width = currentToolsResolution - leftOffset - clippedLeft;
                } else {
                    ti.commandArea.width = brushSizeInSamples - clippedLeft;
                }
                
                if(bottomOffset + brushSizeInSamples > currentToolsResolution) {
                    ti.commandArea.height = currentToolsResolution - bottomOffset - clippedBottom;
                } else {
                    ti.commandArea.height = brushSizeInSamples - clippedBottom;
                }

                ti.commandArea.x = leftOffset;
                ti.commandArea.y = bottomOffset;
                ti.commandArea.clippedLeft = clippedLeft;
                ti.commandArea.clippedBottom = clippedBottom;

                if(ti.gridCellX > 0 && ti.commandArea.x == 0) {
                    ti.commandArea.clippedLeft = Math.Max(ti.commandArea.clippedLeft , 0);
                }
                if(ti.gridCellY > 0 && ti.commandArea.y == 0) {
                    ti.commandArea.clippedBottom = Math.Max(ti.commandArea.clippedBottom , 0);
                }

                if(ti.commandArea.width < 1 || ti.commandArea.height < 1) {
                    ti.commandArea = null;
                    continue;
                }
            }

            // Load all necessary heightmap or alphamaps samples on demand.
            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.commandArea == null) continue;

                if(CurrentTool == Tool.PaintTexture) {
                    if(ti.alphamapsCache == null) ti.alphamapsCache = GetEmptyAlphamapCacheBlock(ti);
                } else if(ti.heightsCache == null) {
                    ti.heightsCache = GetEmptyHeightsCacheBlock(ti); 
                }

                if(CurrentTool == Tool.PaintTexture) {
                    foreach(IntBounds region in ti.alphamapsCache.GetUnloadedIntersectingRegions(ti.commandArea)) {
                        float[,,] alphamaps = null;
                        alphamaps = ti.terrainData.GetAlphamaps(region.xMin, region.yMin, Math.Min(region.xMax, alphamapResolution) - region.xMin, Math.Min(region.yMax, alphamapResolution) - region.yMin);

                        for(int l = 0; l < alphamaps.GetLength(2); l++) {
                            for(int x = region.xMin; x < Math.Min(region.xMax, alphamapResolution); x++) {
                                for(int y = region.yMin; y < Math.Min(region.yMax, alphamapResolution); y++) {
                                    ti.alphamapsCache.data[y, x, l] = alphamaps[y - region.yMin, x - region.xMin, l];
                                }
                            }
                        }
                    }
                } else {
                    foreach(IntBounds region in ti.heightsCache.GetUnloadedIntersectingRegions(ti.commandArea)) {
                        float[,] heights = null;
                        heights = ti.terrainData.GetHeights(region.xMin, region.yMin, Math.Min(region.xMax, heightmapResolution) - region.xMin, Math.Min(region.yMax, heightmapResolution) - region.yMin);

                        for(int x = region.xMin; x < Math.Min(region.xMax, heightmapResolution); x++) {
                            for(int y = region.yMin; y < Math.Min(region.yMax, heightmapResolution); y++) {
                                ti.heightsCache.data[y, x] = heights[y - region.yMin, x - region.xMin];
                            }
                        }
                    }
                }
            }
        }

        private AlphamapsCacheBlock GetEmptyAlphamapCacheBlock(TerrainInfo tiToIgnore) {
            foreach(AlphamapsCacheBlock cacheBlock in alphamapsCacheBlocks) {
                if(cacheBlock.isFree == false) continue;
                cacheBlock.SetAllRegions(false);
                cacheBlock.isFree = false;
                return cacheBlock;
            }

            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.terrainData == tiToIgnore.terrainData || ti.commandArea != null || ti.alphamapsCache == null) continue;
                var result = ti.alphamapsCache;
                ti.alphamapsCache.SetAllRegions(false);
                ti.alphamapsCache = null;
                return result;
            }

            Debug.LogError("Unable to find an empty alphmaps cache block");
            return null;
        }

        private HeightsCacheBlock GetEmptyHeightsCacheBlock(TerrainInfo tiToIgnore) {
            foreach(HeightsCacheBlock cacheBlock in heightsCacheBlocks) {
                if(cacheBlock.isFree == false) continue;
                cacheBlock.SetAllRegions(false);
                cacheBlock.isFree = false;
                return cacheBlock;
            }

            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.terrainData == tiToIgnore.terrainData || ti.commandArea != null || ti.heightsCache == null) continue;
                var result = ti.heightsCache;
                ti.heightsCache.SetAllRegions(false);
                ti.heightsCache = null;
                return result;
            }

            Debug.LogError("Unable to find an empty heights cache block");
            return null;
        }

        private void UpdateRandomSpacing() {
            randomSpacing = UnityEngine.Random.Range(Settings.cached.modeSettings[CurrentTool].minBrushSpacing, Settings.cached.modeSettings[CurrentTool].maxBrushSpacing);
        }

        /**
        * Caching the terrain brush is especially useful for RotateTemporaryBrushSamples. It would take >500ms when accessing the terrain brush
        * through the property. Using it in when it's been cached makes roughly a 10x speedup and doesn't allocated ~3 MB of garbage.
        */
        private Brush cachedTerrainBrush;
        private void RotateTemporaryBrushSamples() {
            cachedTerrainBrush = BrushCollection.GetBrushById(Settings.cached.modeSettings[CurrentTool].selectedBrushId);

            if(temporarySamples == null || temporarySamples.GetLength(0) != brushSizeInSamples) {
                temporarySamples = new float[brushSizeInSamples, brushSizeInSamples];
            }
            
            float angle = Settings.cached.modeSettings[CurrentTool].brushAngle + UnityEngine.Random.Range(Settings.cached.modeSettings[CurrentTool].minRandomRotation, 
                Settings.cached.modeSettings[CurrentTool].maxRandomRotation);

            Vector2 newPoint;
            PointRotator pointRotator = new PointRotator(angle, new Vector2(brushSizeInSamples * 0.5f, brushSizeInSamples * 0.5f));
            float brushSpeed = Settings.cached.modeSettings[CurrentTool].brushSpeed * Brush.GlobalBrushSpeedFactor;

            for(int x = 0; x < brushSizeInSamples; x++) {
                for(int y = 0; y < brushSizeInSamples; y++) {
                    newPoint = pointRotator.Rotate(new Vector2(x, y));
                    temporarySamples[x, y] = GetInteropolatedBrushSample(newPoint.x, newPoint.y) * brushSpeed;
                }
            }
        }

        private float GetInteropolatedBrushSample(float x, float y) {
            int flooredX = Mathf.FloorToInt(x);
            int flooredY = Mathf.FloorToInt(y);
            int flooredXPlus1 = flooredX + 1;
            int flooredYPlus1 = flooredY + 1;

            if(flooredX < 0 || flooredX >= brushSizeInSamples || flooredY < 0 || flooredY >= brushSizeInSamples) return 0f;

            float topLeftSample = cachedTerrainBrush.samples[flooredX, flooredY];
            float topRightSample = 0f;
            float bottomLeftSample = 0f;
            float bottomRightSample = 0f;

            if(flooredXPlus1 < brushSizeInSamples) {
                topRightSample = cachedTerrainBrush.samples[flooredXPlus1, flooredY];
            }

            if(flooredYPlus1 < brushSizeInSamples) {
                bottomLeftSample = cachedTerrainBrush.samples[flooredX, flooredYPlus1];

                if(flooredXPlus1 < brushSizeInSamples) {
                    bottomRightSample = cachedTerrainBrush.samples[flooredXPlus1, flooredYPlus1];
                }
            }

            return Utilities.LerpUnclamped(Utilities.LerpUnclamped(topLeftSample, topRightSample, x % 1f), 
                Utilities.LerpUnclamped(bottomLeftSample, bottomRightSample, x % 1f), y % 1f);
        }

        private void UpdateDirtyBrushSamples() {
            if(samplesDirty == SamplesDirty.None || CurrentBrush == null) return;

            if(( samplesDirty & SamplesDirty.ProjectorTexture ) == SamplesDirty.ProjectorTexture) {
                UpdateBrushProjectorTextureAndSamples();
            } else if((samplesDirty & SamplesDirty.BrushSamples) == SamplesDirty.BrushSamples) {
                CurrentBrush.UpdateSamplesWithSpeed(brushSizeInSamples);
            }
            
            if((samplesDirty & SamplesDirty.InspectorTexture) == SamplesDirty.InspectorTexture) {
                CurrentBrush.CreatePreviewTexture();
            }
            
            // Since the underlying burhshProjector pixels have been rotated, set the temporary rotation to zero.
            topPlaneGameObject.transform.eulerAngles = new Vector3(90f, 0f, 0f);

            samplesDirty = SamplesDirty.None;
        }
        
        private void CheckKeyboardShortcuts(Event currentEvent) {
            if(GUIUtility.hotControl != 0) return;
            if(activeInspectorInstanceID != 0 && activeInspectorInstanceID != GetInstanceID()) return;
            if(currentEvent.type != EventType.KeyDown) return;

            // Only check for shortcuts when no terrain command is active
            if(currentCommand != null) return;

            /**
            * Check to make sure there is no textField focused. This will ensure that shortcut strokes will not override
            * typing in text fields. Through testing however, all textboxes seem to mark the event as Used. This is simply
            * here as a precaution.
            */
            if((bool)guiUtilityTextFieldInput.GetValue(null, null)) return;

            Shortcut.wasExecuted = false;

            if(Shortcut.Shortcuts["Select Raise/Lower Tool"].WasExecuted(currentEvent)) {
                CurrentTool = Tool.RaiseOrLower;
            } else if(Shortcut.Shortcuts["Select Smooth Tool"].WasExecuted(currentEvent)) {
                CurrentTool = Tool.Smooth;
            } else if(Shortcut.Shortcuts["Select Set Height Tool"].WasExecuted(currentEvent)) {
                CurrentTool = Tool.SetHeight;
            } else if(Shortcut.Shortcuts["Select Flatten Tool"].WasExecuted(currentEvent)) {
                CurrentTool = Tool.Flatten;
            } else if(Shortcut.Shortcuts["Select Paint Texture Tool"].WasExecuted(currentEvent)) {
                CurrentTool = Tool.PaintTexture;
            } else if(Shortcut.Shortcuts["Select Mould Tool"].WasExecuted(currentEvent)) {
                CurrentTool = Tool.Mould;
            } else if(Shortcut.Shortcuts["Select Settings Tool"].WasExecuted(currentEvent)) {
                CurrentTool = Tool.Settings;
            }

            // Tool centric shortcuts
            if(CurrentTool == Tool.None || CurrentTool >= Tool.FirstNonMouse) {
                if(Shortcut.wasExecuted) {
                    Repaint();
                    currentEvent.Use();
                }
                return;
            }

            if(Shortcut.Shortcuts["Decrease Brush Size"].WasExecuted(currentEvent)) {
                Settings.cached.modeSettings[CurrentTool].brushSize = 
                    Mathf.Clamp(Settings.cached.modeSettings[CurrentTool].brushSize - GetBrushSizeIncrement(Settings.cached.modeSettings[CurrentTool].brushSize), MinBrushSize, terrainSize.x);
                Settings.cached.modeSettings[CurrentTool].brushSize = Mathf.Round(Settings.cached.modeSettings[CurrentTool].brushSize / 0.1f) * 0.1f;
                BrushSizeChanged();
            } else if(Shortcut.Shortcuts["Increase Brush Size"].WasExecuted(currentEvent)) {
                Settings.cached.modeSettings[CurrentTool].brushSize = 
                    Mathf.Clamp(Settings.cached.modeSettings[CurrentTool].brushSize + GetBrushSizeIncrement(Settings.cached.modeSettings[CurrentTool].brushSize), MinBrushSize, terrainSize.x);
                Settings.cached.modeSettings[CurrentTool].brushSize = Mathf.Round(Settings.cached.modeSettings[CurrentTool].brushSize / 0.1f) * 0.1f;
                BrushSizeChanged();
            } else if(Shortcut.Shortcuts["Decrease Brush Speed"].WasExecuted(currentEvent)) {
                Settings.cached.modeSettings[CurrentTool].brushSpeed = 
                    Mathf.Clamp(Mathf.Round((Settings.cached.modeSettings[CurrentTool].brushSpeed - GetBrushSpeedIncrement(Settings.cached.modeSettings[CurrentTool].brushSpeed)) / 0.001f) * 0.001f, 
                    minBrushSpeed, maxBrushSpeed);
                BrushSpeedChanged();
            } else if(Shortcut.Shortcuts["Increase Brush Speed"].WasExecuted(currentEvent)) {
                Settings.cached.modeSettings[CurrentTool].brushSpeed = 
                    Mathf.Clamp(Mathf.Round((Settings.cached.modeSettings[CurrentTool].brushSpeed + GetBrushSpeedIncrement(Settings.cached.modeSettings[CurrentTool].brushSpeed)) / 0.001f) * 0.001f, 
                    minBrushSpeed, maxBrushSpeed);
                BrushSpeedChanged();
            } else if(Shortcut.Shortcuts["Next Brush"].WasExecuted(currentEvent)) {
                IncrementSelectedBrush(1);
            } else if(Shortcut.Shortcuts["Previous Brush"].WasExecuted(currentEvent)) {
                IncrementSelectedBrush(-1);
            }

            // Brush angle doesn't apply to a circular falloff brush
            if(CurrentBrush != null && CanBrushRotate()) {
                if(Shortcut.Shortcuts["Reset Brush Rotation"].WasExecuted(currentEvent)) {
                    float angleDeltaChange = Settings.cached.modeSettings[CurrentTool].brushAngle;
                    Settings.cached.modeSettings[CurrentTool].brushAngle = 0f;
                    if(angleDeltaChange != 0f) BrushAngleDeltaChanged(angleDeltaChange);
                } else if(Shortcut.Shortcuts["Rotate Brush Anticlockwise"].WasExecuted(currentEvent)) {
                    float newBrushAngle = WrapDegrees(Settings.cached.modeSettings[CurrentTool].brushAngle + 2f);
                    if(newBrushAngle != Settings.cached.modeSettings[CurrentTool].brushAngle) {
                        float delta = Settings.cached.modeSettings[CurrentTool].brushAngle - newBrushAngle;
                        Settings.cached.modeSettings[CurrentTool].brushAngle = newBrushAngle;
                        BrushAngleDeltaChanged(delta);
                    }
                } else if(Shortcut.Shortcuts["Rotate Brush Clockwise"].WasExecuted(currentEvent)) {
                    float newBrushAngle = WrapDegrees(Settings.cached.modeSettings[CurrentTool].brushAngle - 2f);
                    if(newBrushAngle != Settings.cached.modeSettings[CurrentTool].brushAngle) {
                        float delta = Settings.cached.modeSettings[CurrentTool].brushAngle - newBrushAngle;
                        Settings.cached.modeSettings[CurrentTool].brushAngle = newBrushAngle;
                        BrushAngleDeltaChanged(delta);
                    }
                }
            }

            if(Shortcut.Shortcuts["Toggle Sculpt Onto Mode"].WasExecuted(currentEvent)) {
                Settings.cached.raycastOntoFlatPlane = !Settings.cached.raycastOntoFlatPlane;
            } else if(Shortcut.Shortcuts["Flatten Terrain"].WasExecuted(currentEvent)) {
                SetHeightAll(0f);
            }

            switch(CurrentTool) {
                case Tool.Flatten:
                    int totalFlattenModeValues = Enum.GetValues(typeof(FlattenMode)).Length;
                    if(Shortcut.Shortcuts["Previous Flatten Mode"].WasExecuted(currentEvent)) {
                        if(--Settings.cached.flattenMode < 0) Settings.cached.flattenMode = (FlattenMode)(totalFlattenModeValues - 1);
                    } else if(Shortcut.Shortcuts["Next Flatten Mode"].WasExecuted(currentEvent)) {
                        if((int)++Settings.cached.flattenMode >= totalFlattenModeValues) Settings.cached.flattenMode = 0;
                    }
                    break;
                case Tool.PaintTexture:
                    int splatPrototypesCount = splatPrototypes.Length;
                    if(Shortcut.Shortcuts["Previous Texture"].WasExecuted(currentEvent)) {
                        if(--Settings.cached.selectedTextureIndex < 0) Settings.cached.selectedTextureIndex = splatPrototypesCount - 1;
                    } else if(Shortcut.Shortcuts["Next Texture"].WasExecuted(currentEvent)) {
                        if(++Settings.cached.selectedTextureIndex >= splatPrototypesCount) Settings.cached.selectedTextureIndex = 0;
                    }
                    break;
            }

            if(Shortcut.wasExecuted) {
                Repaint();
                currentEvent.Use();
            }
        }

        private float GetBrushSizeIncrement(float currentBrushSize) {
            float brushSizeNormalized = currentBrushSize / terrainSize.x;

            if(brushSizeNormalized > 0.5f) {
                return terrainSize.x * 0.025f;
            } else if(brushSizeNormalized > 0.25f) {
                return terrainSize.x * 0.01f;
            } else if(brushSizeNormalized > 0.1f) {
                return terrainSize.x * 0.005f;
            } else if(brushSizeNormalized > 0.08f) {
                return terrainSize.x * 0.002f;
            } else {
                return terrainSize.x * 0.001f;
            }
        }

        private float GetBrushSpeedIncrement(float currentBrushSpeed) {
            if(currentBrushSpeed > 50f) {
                return 4f;
            } else if(currentBrushSpeed > 25f) {
                return 2.5f;
            } else if(currentBrushSpeed > 9f) {
                return 1.2f;
            } else if(currentBrushSpeed > 1f) {
                return 0.5f;
            } else {
                return 0.05f;
            }
        }

        private void IncrementSelectedBrush(int incrementFactor) {
            if(BrushCollection.brushes.Count == 0) return;

            string selectedBrushId = Settings.cached.modeSettings[CurrentTool].selectedBrushId;

            for(int i = 0; i < BrushCollection.brushes.Count; i++) {
                if(selectedBrushId != BrushCollection.brushes[i].id) continue;

                int newIndex = i + incrementFactor;
                if(newIndex > BrushCollection.brushes.Count - 1 || newIndex < 0) return;

                Settings.cached.modeSettings[CurrentTool].selectedBrushId = BrushCollection.brushes[newIndex].id;
                UpdateBrushTextures();
                return;
            }

            // If this part is reached then we couldn't find the current selected index in the BrushCollection, so set it to the default value
            UseDefaultBrush();
        }

        private void UseDefaultBrush() {
            Settings.cached.modeSettings[CurrentTool].selectedBrushId = BrushCollection.defaultFalloffBrushId;
            UpdateBrushTextures();
        }

        private void BrushFalloffChanged() {
            ClampAnimationCurve(Settings.cached.modeSettings[CurrentTool].brushFalloff);

            samplesDirty |= SamplesDirty.ProjectorTexture;

            if(Settings.cached.AlwaysShowBrushSelection) {
                BrushCollection.UpdatePreviewTextures();
            } else {
                CurrentBrush.CreatePreviewTexture();
            }
        }

        private void ToggleSelectingBrush() {
            isSelectingBrush = !isSelectingBrush;

            // Update the brush previews if the user is now selecting brushes
            if(isSelectingBrush) {
                BrushCollection.UpdatePreviewTextures();
            }
        }

        private void CurrentToolChanged(Tool previousValue) {
            // Sometimes it's possible Terrain Former thinks the mouse is still pressed down as not every event is detected by Terrain Former
            mouseIsDown = false;

            // If the built-in Unity tools were active, make them inactive by setting their tool to None (-1)
            foreach(object terrainInspector in unityTerrainInspectors) {
                if((int)unityTerrainSelectedTool.GetValue(terrainInspector, null) != -1) {
                    unityTerrainSelectedTool.SetValue(terrainInspector, -1, null);
                }
            }
                        
            /**
            * All inspector windows must be updated to reflect across multiple inspectors that there is only one Terrain Former instance active at
            * once, and that also stops those Terrain Former instance(s) that are no longer active to not call OnInspectorGUI.
            */
            inspectorWindowRepaintAllInspectors.Invoke(null, null);

            Tools.current = UnityEditor.Tool.None;
            activeInspectorInstanceID = GetInstanceID();
            Instance = this;

            if(Settings.cached == null) return;

            if(CurrentTool == Tool.None) {
                if(cylinderCursor.activeSelf) cylinderCursor.SetActive(false);
                return;
            }
            
            if(previousValue == Tool.None) Initialize(true);
            if(CurrentTool >= Tool.FirstNonMouse) return;
            
            splatPrototypes = firstTerrainData.terrainLayers;
            
            Settings.cached.modeSettings[CurrentTool] = Settings.cached.modeSettings[CurrentTool];
            
            switch(CurrentTool) {
                case Tool.PaintTexture:
                    currentToolsResolution = firstTerrainData.alphamapResolution;
                    break;
                default:
                    currentToolsResolution = heightmapResolution;
                    break;
            }
            toolSamplesHorizontally = currentToolsResolution * numberOfTerrainsHorizontally;
            toolSamplesVertically = currentToolsResolution * numberOfTerrainsVertically;
            
            foreach(TerrainInfo ti in terrainInfos) {
                ti.toolOffsetX = ti.gridCellX * currentToolsResolution - ti.gridCellX;
                ti.toolOffsetY = ti.gridCellY * currentToolsResolution - ti.gridCellY;
            }
            
            float brushSize = Settings.cached.modeSettings[CurrentTool].brushSize;
            topPlaneGameObject.transform.localScale = new Vector3(brushSize, brushSize, 1f);
            BrushSizeInSamples = GetSegmentsFromUnits(Settings.cached.modeSettings[CurrentTool].brushSize);
            
            UpdateAllNecessaryPreviewTextures();
            UpdateBrushProjectorTextureAndSamples();
        }
                
        private void InvertBrushTextureChanged() {
            UpdateBrushTextures();

            if(Settings.cached.AlwaysShowBrushSelection) BrushCollection.UpdatePreviewTextures();
        }

        private void BrushSpeedChanged() {
            samplesDirty |= SamplesDirty.BrushSamples;
        }

        private void BrushColourChanged() {
            cylinderCursorMaterial.color = Settings.cached.brushColour;
            topPlaneMaterial.color = Settings.cached.brushColour.Value * 0.9f;
        }

        private void BrushSizeChanged() {
            if(CurrentTool == Tool.None || CurrentTool >= Tool.FirstNonMouse) return;

            BrushSizeInSamples = Mathf.RoundToInt(GetSegmentsFromUnits(Settings.cached.modeSettings[CurrentTool].brushSize));

            /**
            * HACK: Another spot where objects are seemingly randomly destroyed. The top plane and projector are (seemingly) destroyed between
            * switching from one terrain with Terrain Former to another.
            */
            if(topPlaneGameObject == null || cylinderCursor == null) {
                CreateProjector();
            }

            float brushSize = Settings.cached.modeSettings[CurrentTool].brushSize;
            topPlaneGameObject.transform.localScale = new Vector3(brushSize, brushSize, 1f);

            samplesDirty |= SamplesDirty.ProjectorTexture;
        }

        private void BrushRoundnessChanged() {
            samplesDirty |= SamplesDirty.ProjectorTexture;

            UpdateAllNecessaryPreviewTextures();
        }

        private void BrushAngleDeltaChanged(float delta) {
            UpdateAllNecessaryPreviewTextures();

            topPlaneGameObject.transform.eulerAngles = new Vector3(90f, topPlaneGameObject.transform.eulerAngles.y - delta, 0f);

            samplesDirty = SamplesDirty.BrushSamples | SamplesDirty.ProjectorTexture;
        }

        private void AlwaysShowBrushSelectionValueChanged() {
            /**
            * If the brush selection should always be shown, make sure isSelectingBrush is set to false because
            * when changing to AlwaysShowBrushSelection while the brush selection was active, it will return back to
            * selecting a brush.
            */
            if(Settings.cached.AlwaysShowBrushSelection) {
                isSelectingBrush = false;
            }
        }
        
        private void UpdateAllNecessaryPreviewTextures() {
            if(CurrentTool == Tool.None || CurrentTool >= Tool.FirstNonMouse) return;

            if(Settings.cached.AlwaysShowBrushSelection || isSelectingBrush) {
                BrushCollection.UpdatePreviewTextures();
            } else {
                CurrentBrush.CreatePreviewTexture();
            }
        }
        
        internal void ApplySplatPrototypes(Vector2 tileOffset, int updatedPrototype = -1) {
            foreach(TerrainInfo ti in terrainInfos) {
                TerrainLayer[] prototypes = (TerrainLayer[])splatPrototypes.Clone();

                if(updatedPrototype >= 0 && updatedPrototype < prototypes.Length) { 
                    // Make sure that tiling textures that have non-integer scales are seamless.
                    prototypes[updatedPrototype].tileOffset = new Vector2(
                        x: (terrainSize.x - (Mathf.Ceil(terrainSize.x / prototypes[updatedPrototype].tileSize.x) * 
                            prototypes[updatedPrototype].tileSize.x)) * ti.gridCellX, 
                        y: (terrainSize.y - (Mathf.Ceil(terrainSize.y / prototypes[updatedPrototype].tileSize.y) * 
                            prototypes[updatedPrototype].tileSize.y)) * ti.gridCellY);
                    prototypes[updatedPrototype].tileOffset += tileOffset;
                }

                ti.terrainData.terrainLayers = prototypes;
            }

            for(int i = 0; i < alphamapsCacheBlocks.Length; i++) alphamapsCacheBlocks[i].ReInitializeBlock(alphamapResolution, splatPrototypes.Length);
        }

        /**
        * Update the heights and alphamaps every time an Undo or Redo occurs - since we must rely on storing and managing the 
        * heights data manually for better editing performance.
        * This is quite slow, and I've seriously run into a dead end into trying to fix it perfectly.
        */
        private void UndoRedoPerformed() {
            if(target == null) return;
            
            if(terrainSize != firstTerrainData.size || heightmapResolution != firstTerrainData.heightmapResolution || alphamapResolution != firstTerrainData.alphamapResolution) {
                UpdateTerrainRelatedFields();
                CurrentToolChanged(CurrentTool);
            }
            
            lastHeightmapResolultion = heightmapResolution;

            if(splatPrototypes.Length != firstTerrainData.terrainLayers.Length) { 
                for(int i = 0; i < 4; i++) {
                    alphamapsCacheBlocks[i].ReInitializeBlock(firstTerrainData.alphamapResolution, firstTerrainData.terrainLayers.Length);
                }
            }

            splatPrototypes = firstTerrainData.terrainLayers;
            
            if(heightsCacheBlocks != null) ResetAllCacheBlockRegions(heightsCacheBlocks);
            
            if(CurrentTool != Tool.PaintTexture) return;

            if(alphamapsCacheBlocks != null) ResetAllCacheBlockRegions(alphamapsCacheBlocks);
            foreach(TerrainInfo ti in terrainInfos) {
                #if UNITY_2019_1_OR_NEWER
                ti.terrainData.SetBaseMapDirty();
                #else
                terrainDataSetBasemapDirtyMethodInfo.Invoke(ti.terrainData, new object[] { true });
                #endif
            }
        }

        internal void RegisterUndoForTerrainGrid(string description, bool includeAlphamapTextures = false, List<UnityEngine.Object> secondaryObjectsToUndo = null) {
            List<UnityEngine.Object> objectsToRegister = new List<UnityEngine.Object>();
            if(secondaryObjectsToUndo != null) objectsToRegister.AddRange(secondaryObjectsToUndo);

            for(int i = 0; i < terrainInfos.Count; i++) {
                objectsToRegister.Add(terrainInfos[i].terrainData);
                                
                if(includeAlphamapTextures) {
                    objectsToRegister.AddRange(terrainInfos[i].terrainData.alphamapTextures);
                }
            }
            
            Undo.RegisterCompleteObjectUndo(objectsToRegister.ToArray(), description);
        }

        private void CreateGridPlane() {
            if(gridPlane == null) {
                gridPlane = TrackObject(GameObject.CreatePrimitive(PrimitiveType.Quad));
                gridPlane.name = "GridPlane";
                gridPlane.transform.Rotate(90f, 0f, 0f);
                gridPlane.transform.localScale = Vector3.one * 20f;
                gridPlane.hideFlags = HideFlags.HideAndDontSave;
                gridPlane.SetActive(false);
            }

            Shader gridShader = Shader.Find("Hidden/TerrainFormer/Grid");
            if(gridShader == null) {
                Debug.LogError("Terrain Former couldn't find its grid shader.");
                return;
            }

            if(gridPlaneMaterial == null) {
                gridPlaneMaterial = TrackObject(new Material(gridShader));
                gridPlaneMaterial.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Tile.psd");
                gridPlaneMaterial.mainTexture.wrapMode = TextureWrapMode.Repeat;
                gridPlaneMaterial.shader.hideFlags = HideFlags.HideAndDontSave;
                gridPlaneMaterial.hideFlags = HideFlags.HideAndDontSave;
                gridPlaneMaterial.mainTextureScale = new Vector2(8f, 8f); // Set texture scale to create 8x8 tiles
                gridPlane.GetComponent<Renderer>().sharedMaterial = gridPlaneMaterial;
            }
        }

        private void CreateProjector() {
            Texture2D outlineTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/BrushOutline.png");

            if(cylinderCursorMaterial == null) {
                cylinderCursorMaterial = TrackObject(new Material(Shader.Find("Hidden/TerrainFormer/BrushPlaneTop")));
                cylinderCursorMaterial.hideFlags = HideFlags.HideAndDontSave;
            }
            cylinderCursorMaterial.color = Settings.cached.brushColour * new Color(1.1f, 1.1f, 1.1f, 0.1f);

            if(cylinderCursor == null) {
                cylinderCursor = TrackObject(GameObject.CreatePrimitive(PrimitiveType.Cylinder));
                cylinderCursor.hideFlags = HideFlags.HideAndDontSave;
                cylinderCursor.name = "Cylinder Cursor";
                cylinderCursor.GetComponent<MeshRenderer>().sharedMaterial = cylinderCursorMaterial;
                DestroyImmediate(cylinderCursor.GetComponent<CapsuleCollider>());
            }
            
            /**
            * Create the top plane
            */
            if(topPlaneGameObject == null) {
                topPlaneGameObject = TrackObject(GameObject.CreatePrimitive(PrimitiveType.Quad));
                topPlaneGameObject.name = "Top Plane";
                topPlaneGameObject.hideFlags = HideFlags.HideAndDontSave;
                DestroyImmediate(topPlaneGameObject.GetComponent<MeshCollider>());
                topPlaneGameObject.transform.Rotate(90f, 0f, 0f);
            }

            if(topPlaneMaterial == null) {
                topPlaneMaterial = TrackObject(new Material(Shader.Find("Hidden/TerrainFormer/BrushPlaneTop")));
                topPlaneMaterial.hideFlags = HideFlags.HideAndDontSave;
                topPlaneMaterial.color = Settings.cached.brushColour.Value * 0.9f;
                topPlaneMaterial.SetTexture("_OutlineTex", outlineTexture);
                topPlaneGameObject.GetComponent<MeshRenderer>().sharedMaterial = topPlaneMaterial;
            }

            SetCursorEnabled(false);
        }

        private void UpdateProjector() {
            if(cylinderCursor == null) return;

            if(CurrentTool == Tool.None || CurrentTool >= Tool.FirstNonMouse) {
                SetCursorEnabled(false);
                return;
            }
            
            Vector3 position;
            if(GetMousePositionInWorldSpace(out position)) {                
                float minHeightDifferenceToShowTopPlane = firstTerrainData.heightmapScale.y * 0.002f;

                cylinderCursor.SetActive(true);
                float topOfCylinder = position.y - firstTerrainTransform.position.y;
                float brushSize = Settings.cached.modeSettings[CurrentTool].brushSize;
                cylinderCursor.transform.position = new Vector3(position.x, position.y - topOfCylinder * 0.5f + 0.001f, position.z);
                cylinderCursor.transform.localScale = new Vector3(brushSize, topOfCylinder * 0.5f, brushSize);

                if(CurrentTool == Tool.Flatten) {
                    topPlaneGameObject.SetActive(position.y >= minHeightDifferenceToShowTopPlane);
                    topPlaneGameObject.transform.position = new Vector3(position.x, position.y, position.z);
                } else if(CurrentTool == Tool.SetHeight) {
                    topPlaneGameObject.SetActive(Settings.cached.setHeight >= minHeightDifferenceToShowTopPlane);
                    topPlaneGameObject.transform.position = new Vector3(position.x, firstTerrainTransform.position.y + Settings.cached.setHeight, position.z);
                } else {
                    topPlaneGameObject.SetActive(true);
                    topPlaneGameObject.transform.position = new Vector3(position.x, position.y + 0.001f, position.z);
                }
            } else {
                SetCursorEnabled(false);
            }
            
            HandleUtility.Repaint();
        }

        private void UpdateBrushTextures() {
            CurrentBrush.CreatePreviewTexture();
            UpdateBrushProjectorTextureAndSamples();
        }

        private void UpdateBrushProjectorTextureAndSamples() {
            CurrentBrush.UpdateSamplesAndMainTexture(brushSizeInSamples);

            // HACK: Projector objects are destroyed seemingly randomly, so recreate them if necessary
            if(cylinderCursor == null || cylinderCursorMaterial == null) {
                CreateProjector();
            }

            topPlaneMaterial.mainTexture = brushProjectorTexture;

            if(currentCommand != null) {
                currentCommand.brushSamples = GetBrushSamplesWithSpeed();
            }
        }

        internal void RemoveSplatTexture(int indexToDelete) {
            RegisterUndoForTerrainGrid("Remove Splat Texture", true);

            int textureCount = firstTerrainData.alphamapLayers;
            int newTextureCount = textureCount - 1;
            float[,,] newalphamap = new float[alphamapResolution, alphamapResolution, newTextureCount];

            foreach(TerrainInfo ti in terrainInfos) { 
                float[,,] alphamap = ti.terrainData.GetAlphamaps(0, 0, alphamapResolution, alphamapResolution);
                
                for(int y = 0; y < alphamapResolution; y++) {
                    for(int x = 0; x < alphamapResolution; x++) {
                        for(int a = 0; a < indexToDelete; a++)
                            newalphamap[y, x, a] = alphamap[y, x, a];
                        for(int a = indexToDelete + 1; a < textureCount; a++)
                            newalphamap[y, x, a - 1] = alphamap[y, x, a];
                    }
                }

                for(int y = 0; y < alphamapResolution; y++) {
                    for(int x = 0; x < alphamapResolution; x++) {
                        float sum = 0f;
                        for(int a = 0; a < newTextureCount; a++)
                            sum += newalphamap[y, x, a];
                        if(sum >= 0.01f) {
                            float multiplier = 1f / sum;
                            for(int a = 0; a < newTextureCount; a++)
                                newalphamap[y, x, a] *= multiplier;
                        } else {
                            for(int a = 0; a < newTextureCount; a++)
                                newalphamap[y, x, a] = (a == 0) ? 1f : 0f;
                        }
                    }
                }

                TerrainLayer[] splats = ti.terrainData.terrainLayers;
                TerrainLayer[] newSplats = new TerrainLayer[splats.Length - 1];
                for(int a = 0; a < indexToDelete; a++) { 
                    newSplats[a] = splats[a];
                }
                for(int a = indexToDelete + 1; a < textureCount; a++) { 
                    newSplats[a - 1] = splats[a];
                }
                ti.terrainData.terrainLayers = newSplats;
                ti.terrainData.SetAlphamaps(0, 0, newalphamap);
            }

            List<TerrainLayer> splatPrototypesList = new List<TerrainLayer>(splatPrototypes);
            splatPrototypesList.RemoveAt(indexToDelete);
            splatPrototypes = splatPrototypesList.ToArray();
            ApplySplatPrototypes(Vector2.zero);
        }

        private void SetHeightAll(float setHeight) {
            RegisterUndoForTerrainGrid("Flatten Terrain");

            float[,] newHeights = new float[heightmapResolution, heightmapResolution];

            for(int y = 0; y < heightmapResolution; y++) {
                for(int x = 0; x < heightmapResolution; x++) {
                    newHeights[x, y] = setHeight;
                }
            }

            foreach(TerrainInfo ti in terrainInfos) {
                ti.terrainData.SetHeights(0, 0, newHeights);

                if(ti.heightsCache == null) continue;

                ti.heightsCache.SetAllRegions(true);
                for(int y = 0; y < heightmapResolution; y++) {
                    for(int x = 0; x < heightmapResolution; x++) {
                        ti.heightsCache.data[x, y] = setHeight;
                    }
                }
            }
        }

        private void CreateLinearRamp(float maxHeight) {
            RegisterUndoForTerrainGrid("Created Ramp");
            
            float heightCoefficient = maxHeight / terrainSize.y;
            float height;
            float[,] newHeights = new float[heightmapResolution, heightmapResolution];
            foreach(TerrainInfo ti in terrainInfos) { 
                if(Settings.cached.generateRampCurveInXAxis) {
                    for(int x = 0; x < heightmapResolution; x++) {
                        height = Settings.cached.generateRampCurve.Evaluate((float)(x + ti.gridCellX * heightmapResolution - ti.gridCellX) / heightmapWidth) * heightCoefficient;
                        for(int y = 0; y < heightmapResolution; y++) {
                            newHeights[y, x] = height;
                        }
                    }
                } else {
                    for(int y = 0; y < heightmapResolution; y++) {
                        height = Settings.cached.generateRampCurve.Evaluate((float)(y + ti.gridCellY * heightmapResolution - ti.gridCellY) / heightmapHeight) * heightCoefficient;
                        for(int x = 0; x < heightmapResolution; x++) {
                            newHeights[y, x] = height;
                        }
                    }
                }

                ti.terrainData.SetHeights(0, 0, newHeights);

                if(ti.heightsCache == null) continue;
                ti.heightsCache.SetAllRegions(true);
                ti.heightsCache.data = (float[,])newHeights.Clone();
            }
        }
        
        private void CreateCircularRamp(float maxHeight) {
            RegisterUndoForTerrainGrid("Created Circular Ramp");
            
            float heightCoefficient = maxHeight / terrainSize.y;
            float halfTotalTerrainSize = Mathf.Min(heightmapWidth, heightmapHeight) * 0.5f;
            float distance;
            float[,] newHeights = new float[heightmapResolution, heightmapResolution];

            foreach(TerrainInfo ti in terrainInfos) { 
                for(int x = 0; x < heightmapResolution; x++) {
                    for(int y = 0; y < heightmapResolution; y++) {
                        float deltaX = x + ti.gridCellX * heightmapResolution - ti.gridCellX - halfTotalTerrainSize;
                        float deltaY = y + ti.gridCellY * heightmapResolution - ti.gridCellY - halfTotalTerrainSize;
                        distance = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

                        newHeights[y, x] = Settings.cached.generateRampCurve.Evaluate(1f - (distance / halfTotalTerrainSize)) * heightCoefficient;
                    }
                }

                ti.terrainData.SetHeights(0, 0, newHeights);

                if(ti.heightsCache == null) continue;
                ti.heightsCache.SetAllRegions(true);
                ti.heightsCache.data = (float[,])newHeights.Clone();
            }
        }

        private void OffsetTerrainGridHeight(float heightmapHeightOffset) {
            RegisterUndoForTerrainGrid("Height Offset");

            heightmapHeightOffset /= terrainSize.y;

            float[,] newHeights;

            foreach(TerrainInfo ti in terrainInfos) {
                newHeights = ti.terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);
                for(int x = 0; x < heightmapResolution; x++) {
                    for(int y = 0; y < heightmapResolution; y++) {
                        newHeights[y, x] = Mathf.Clamp(newHeights[y, x] + heightmapHeightOffset, 0f, 1f);
                    }
                }

                ti.terrainData.SetHeights(0, 0, newHeights);

                if(ti.heightsCache == null) continue;
                ti.heightsCache.SetAllRegions(true);
                ti.heightsCache.data = (float[,])newHeights.Clone();
            }
        }

        private void ExportHeightmap(ref Texture2D tex) {
            Color[] pixels = new Color[heightmapWidth * heightmapHeight];

            float[,] heights;
            float grey;
            foreach(TerrainInfo ti in terrainInfos) {
                heights = ti.terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);

                int yOffset = ti.gridCellX * heightmapResolution - ti.gridCellX;
                int xOffset = ti.gridCellY * heightmapResolution - ti.gridCellY;

                for(int x = 0; x < heightmapResolution; x++) {
                    for(int y = 0; y < heightmapResolution; y++) {
                        grey = heights[x, y];
                        
                        pixels[
                            (xOffset + x) * heightmapHeight + 
                            yOffset + y] = new Color(grey, grey, grey);
                    }
                }
            }
            
            tex.SetPixels(pixels);
            tex.Apply();
        }
                
        private void ImportHeightmap() {
            TextureImporter heightmapTextureImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(heightmapTexture));
            if(heightmapTextureImporter.isReadable == false) {
                heightmapTextureImporter.isReadable = true;
                heightmapTextureImporter.SaveAndReimport();
            }

            float terrainsProcessed = 0f;
            float uPosition;
            float vPosition = 0f;
            Color bilinearSample;
            const float oneThird = 1f / 3f;
            float[,] newHeights = new float[heightmapResolution, heightmapResolution];
            foreach(TerrainInfo ti in terrainInfos) {
                for(int y = 0; y < heightmapResolution; y++) {
                    for(int x = 0; x < heightmapResolution; x++) {
                        uPosition = (float)(x + ti.gridCellX * heightmapResolution - ti.gridCellX) / heightmapWidth;
                        vPosition = (float)(y + ti.gridCellY * heightmapResolution - ti.gridCellY) / heightmapHeight;
                        if(Settings.cached.heightmapSourceIsAlpha) {
                            newHeights[y, x] = heightmapTexture.GetPixelBilinear(uPosition, vPosition).a;
                        } else {
                            bilinearSample = heightmapTexture.GetPixelBilinear(uPosition, vPosition);
                            newHeights[y, x] = (bilinearSample.r + bilinearSample.g + bilinearSample.b) * oneThird;
                        }
                    }

                    if(y % 50 != 0) continue; 
                    
                    if(EditorUtility.DisplayCancelableProgressBar("Terrain Former", "Applying heightmap to terrain", 
                        progress: terrainsProcessed / terrainInfos.Count + y / heightmapResolution)) {
                        EditorUtility.ClearProgressBar();
                        return;
                    }
                }

                if(ti.heightsCache != null) {
                    ti.heightsCache.data = (float[,])newHeights.Clone();
                }

                terrainsProcessed++;
                ti.terrainData.SetHeights(0, 0, newHeights);
            }

            EditorUtility.ClearProgressBar();
        }

        // If there have been changes to a given terrain in Terrain Former, don't reimport its heights on OnAssetsImported.
        private void OnWillSaveAssets(string[] assetPaths) {
            if(Settings.cached != null) Settings.cached.Save();
        }
        
        private void OnAssetsImported(string[] assetPaths) {
            // There's a possibility of no terrainInformations because of being no terrains on the object.
            if(terrainInfos == null) return;

            List<string> customBrushPaths = new List<string>();

            Type texture2DType = typeof(Texture2D);

            foreach(string path in assetPaths) {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if(asset == null) continue;
                if(asset.GetType() != texture2DType) continue;

                // If there are custom textures that have been update, keep a list of which onces have changed and update the brushCollection.
                if(path.StartsWith(BrushCollection.localCustomBrushPath) == false) continue;
                customBrushPaths.Add(path);
            }

            if(customBrushPaths.Count > 0) {
                BrushCollection.RefreshCustomBrushes(customBrushPaths.ToArray());
                BrushCollection.UpdatePreviewTextures();
            }
        }
        
        // Check if the terrain asset has been moved.
        private void OnAssetsMoved(string[] sourcePaths, string[] destinationPaths) {
            for(int i = 0; i < sourcePaths.Length; i++) {
                foreach(TerrainInfo ti in terrainInfos) {
                    if(sourcePaths[i] == ti.terrainAssetPath) {
                        ti.terrainAssetPath = destinationPaths[i];
                    }
                }
            }
        }

        private void OnAssetsDeleted(string[] paths) {
            List<string> deletedCustomBrushPaths = new List<string>();

            foreach(string path in paths) {
                if(path.StartsWith(BrushCollection.localCustomBrushPath)) {
                    deletedCustomBrushPaths.Add(path);
                }
            }

            if(deletedCustomBrushPaths.Count > 0) {
                BrushCollection.RemoveDeletedBrushes(deletedCustomBrushPaths.ToArray());
                BrushCollection.UpdatePreviewTextures();
            }
        }

        /// <summary>
        /// Checks if the secondary Terrain will be able to become part of a contiguous grid to the primary Terrain object.
        /// </summary>
        private static bool DoesTerrainBelongInGrid(Terrain gridSource, Terrain other) {
            const float maxBorderDistance = 0.0001f; // The maximum amount of distance the terrain can have from a bordering terrain.
            Vector3 terrainSize = gridSource.terrainData.size;
            return
                other.terrainData.size == terrainSize &&
                other.terrainData.heightmapResolution ==  gridSource.terrainData.heightmapResolution &&
                other.terrainData.alphamapResolution  ==  gridSource.terrainData.alphamapResolution  &&
                Mathf.Abs(gridSource.transform.position.x - other.transform.position.x) % terrainSize.x < maxBorderDistance &&
                Mathf.Abs(gridSource.transform.position.z - other.transform.position.z) % terrainSize.z < maxBorderDistance &&
                other.transform.position.y == gridSource.transform.position.y;
        }

        // Clamp the falloff curve's values from time 0-1 and value 0-1
        private static void ClampAnimationCurve(AnimationCurve curve) {
            for(int i = 0; i < curve.keys.Length; i++) {
                Keyframe keyframe = curve.keys[i];
                curve.MoveKey(i, new Keyframe(Mathf.Clamp01(keyframe.time), Mathf.Clamp01(keyframe.value), keyframe.inTangent, keyframe.outTangent));
            }
        }

        /**
        * A modified version of the LinePlaneIntersection method from the 3D Math Functions script found on the Unify site 
        * Credit to Bit Barrel Media: http://wiki.unity3d.com/index.php?title=3d_Math_functions
        * This code has been modified to fit my needs and coding style.
        *---
        * Get the intersection between a line and a XZ facing plane. 
        * If the line and plane are not parallel, the function outputs true, otherwise false.
        */
        private bool LinePlaneIntersection(out Vector3 intersectingPoint) {
            Vector3 planePoint = new Vector3(0f, lastClickPosition.y, 0f);

            Ray mouseRay = HandleUtility.GUIPointToWorldRay(mousePosition);

            // Calculate the distance between the linePoint and the line-plane intersection point
            float dotNumerator = Vector3.Dot(planePoint - mouseRay.origin, Vector3.up);
            float dotDenominator = Vector3.Dot(mouseRay.direction, Vector3.up);

            // Check if the line and plane are not parallel
            if(dotDenominator != 0f) {
                float length = dotNumerator / dotDenominator;

                // Create a vector from the linePoint to the intersection point and set the vector length by normalizing and multiplying by the length
                Vector3 vector = mouseRay.direction * length;

                // Get the coordinates of the line-plane intersection point
                intersectingPoint = mouseRay.origin + vector;

                return true;
            } else {
                intersectingPoint = Vector3.zero;
                return false;
            }
        }
        
        // Checks if the cursor is hovering over the terrain
        private bool Raycast(out Vector3 pos, out Vector2 uv) {
            RaycastHit hitInfo;

            float closestSqrDistance = float.MaxValue;
            pos = Vector3.zero;
            uv = Vector2.zero;
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.collider.Raycast(mouseRay, out hitInfo, float.PositiveInfinity)) {
                    float sqrDistance = (mouseRay.origin - hitInfo.point).sqrMagnitude;
                    if(sqrDistance < closestSqrDistance) {
                        closestSqrDistance = sqrDistance;
                        pos = hitInfo.point;
                        uv = hitInfo.textureCoord;
                    }
                }
            }

            return closestSqrDistance != float.MaxValue;
        }

        private static float WrapDegrees(float value) {
            const float min = -180f;
            const float max = 180f;
            if(value < min) {
                float deltaFromBounds = value - min;
                return max + deltaFromBounds;
            } else if(value > max) {
                float deltaFromBounds = value - max;
                return min + deltaFromBounds;
            } 
            return value;
        }

        internal void UpdateSetHeightAtMousePosition() {
            RaycastHit hitInfo;
            if(Physics.Raycast(HandleUtility.GUIPointToWorldRay(Event.current.mousePosition), out hitInfo)) {
                Settings.cached.setHeight = hitInfo.point.y - firstTerrainTransform.position.y;
                Repaint();
            }
        }

        internal float[,] GetBrushSamplesWithSpeed() { 
            return BrushCollection.GetBrushById(Settings.cached.modeSettings[CurrentTool].selectedBrushId).samplesWithSpeed;
        }

        private bool GetTerrainHeightAtMousePosition(out float height) {
            RaycastHit hitInfo;
            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.collider.Raycast(HandleUtility.GUIPointToWorldRay(mousePosition), out hitInfo, float.PositiveInfinity)) {
                    height = hitInfo.point.y - ti.transform.position.y;
                    return true;
                }
            }
            
            height = 0f;
            return false;
        }

        /**
        * Gets the mouse position in world space. This is a utlity method used to automatically get the position of 
        * the mouse depending on if it's being held down or not. Returns true if the terrain or plane was hit, 
        * returns false otherwise.
        */
        private bool GetMousePositionInWorldSpace(out Vector3 position) {
            if(mouseIsDown && (CurrentTool == Tool.SetHeight || CurrentTool == Tool.Flatten || Settings.cached.raycastOntoFlatPlane)) {
                if(LinePlaneIntersection(out position) == false) {
                    SetCursorEnabled(false);
                    return false;
                }
            } else {
                Vector2 uv;
                if(Raycast(out position, out uv) == false) {
                    SetCursorEnabled(false);
                    return false;
                }
            }

            return true;
        }

        private void SetCursorEnabled(bool enabled) {
            cylinderCursor.SetActive(enabled);
            topPlaneGameObject.SetActive(enabled);
        }

        private int GetSegmentsFromUnits(float units) {
            float segmentDensity = currentToolsResolution / terrainSize.x;

            return Mathf.RoundToInt(units * segmentDensity);
        }
        
        private void UpdateHeightsCopies() {
            int smoothSize;
            switch(CurrentTool) {
                case Tool.Smooth: 
                    smoothSize = Settings.cached.boxFilterSize;
                    break;
                case Tool.Mould:
                    smoothSize = Settings.cached.mouldToolBoxFilterSize;
                    break;
                default:
                    smoothSize = 0;
                    break;
            }
            int smoothAwareLength = brushSizeInSamples + (smoothSize * 2);
            
            if(heightsCopy1 == null || heightsCopy1.GetLength(0) < smoothAwareLength || 
                heightsCopy1.GetLength(1) < smoothAwareLength) {
                heightsCopy1 = new float[smoothAwareLength, smoothAwareLength];
            }

            int leftMostGridPos = int.MaxValue;
            int bottomMostGridPos = int.MaxValue;
            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.commandArea == null) continue;
                if(ti.gridCellX < leftMostGridPos) {
                    leftMostGridPos = ti.gridCellX;
                } 
                if(ti.gridCellY < bottomMostGridPos) {
                    bottomMostGridPos = ti.gridCellY;
                }
            }

            foreach(TerrainInfo ti in terrainInfos) {
                if(ti.commandArea == null) continue;

                CommandArea commandArea = new CommandArea(ti.commandArea);
                int xSamplesOffset = Math.Max(ti.commandArea.x + ti.toolOffsetX - globalCommandArea.x, 0);
                int ySamplesOffset = Math.Max(ti.commandArea.y + ti.toolOffsetY - globalCommandArea.y, 0);

                xSamplesOffset = Math.Max(xSamplesOffset, 0);
                ySamplesOffset = Math.Max(ySamplesOffset, 0);
                int x;
                int y = Math.Max(commandArea.y - smoothSize, 0);

                for(; y < Math.Min(commandArea.y + commandArea.height + smoothSize, heightmapResolution); y++) {
                    for(x = Math.Max(commandArea.x - smoothSize, 0); x < Math.Min(commandArea.x + commandArea.width + smoothSize, heightmapResolution); x++) {
                        heightsCopy1[y - commandArea.y + smoothSize + ySamplesOffset, x - commandArea.x + smoothSize + xSamplesOffset]
                            = ti.heightsCache.data[y, x];
                    }
                }
            }

            if(CurrentTool == Tool.Smooth || CurrentTool == Tool.Mould) { 
                heightsCopy2 = (float[,])heightsCopy1.Clone();
            }
        }

        internal static ModeSettings GetCurrentToolSettings() {
            return Settings.cached.modeSettings[Instance.CurrentTool];
        }

        // Warpper for the DelayedInt/Float fields, with older versions of Unity using normal non-delayed fields
        private float DelayedFloatField(string label, float value) {
            return EditorGUILayout.DelayedFloatField(label, value);
        }

        
        private int DelayedIntField(GUIContent content, int value) {
            return EditorGUILayout.DelayedIntField(content, value);
        }

        private static void ResetAllCacheBlockRegions(CacheBlock[] blocks) {
            for(int i = 0; i < blocks.Length; i++) blocks[i].SetAllRegions(false);
        }

        [Flags]
        private enum SamplesDirty : byte {
            None             = 0,
            InspectorTexture = 1,
            ProjectorTexture = 2,
            BrushSamples     = 4,
        }

        private T TrackObject<T>(T o) where T : UnityEngine.Object {
            trackedObjects.Add(o);
            return o;
        }
    }    
}