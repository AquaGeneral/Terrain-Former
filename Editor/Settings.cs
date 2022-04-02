using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using System;

using Include = UnityEngine.SerializeField;
using Exclude = System.NonSerializedAttribute;

namespace JesseStiller.TerrainFormerExtension {
    [Serializable]
    internal class Settings : ISerializationCallbackReceiver {
        public static Settings cached;

        private const bool showSculptingGridPlaneDefault = false;
        [Include]
        internal bool showSculptingGridPlane = showSculptingGridPlaneDefault;

        private const bool raycastOntoFlatPlaneDefault = true;
        [Include]
        internal bool raycastOntoFlatPlane = raycastOntoFlatPlaneDefault;

        private const bool showSceneViewInformationDefault = true;
        [Include]
        internal bool showSceneViewInformation = showSceneViewInformationDefault;

        private const bool displaySceneViewSculptOntoModeDefault = true;
        [Include]
        internal bool displaySceneViewSculptOntoMode = displaySceneViewSculptOntoModeDefault;

        private const bool displaySceneViewCurrentToolDefault = true;
        [Include]
        internal bool displaySceneViewCurrentTool = displaySceneViewCurrentToolDefault;

        private const bool displaySceneViewCurrentHeightDefault = true;
        [Include]
        internal bool displaySceneViewCurrentHeight = displaySceneViewCurrentHeightDefault;

        private const int brushPreviewSizeDefault = 48;
        [Include]
        internal int brushPreviewSize = brushPreviewSizeDefault;

        private const int texurePreviewSizeDefault = 64;
        [Include]
        internal int texurePreviewSize = texurePreviewSizeDefault;

        private const bool alwaysShowBrushSelectionDefault = false;
        [Include]
        internal bool alwaysShowBrushSelection = alwaysShowBrushSelectionDefault;
        internal bool AlwaysShowBrushSelection {
            get {
                return alwaysShowBrushSelection;
            }
            set {
                if(value == alwaysShowBrushSelection) return;

                alwaysShowBrushSelection = value;
                if(AlwaysShowBrushSelectionChanged != null) AlwaysShowBrushSelectionChanged();
            }
        }
        internal Action AlwaysShowBrushSelectionChanged;

        private const bool alwaysUpdateTerrainLODsDefault = true;
        [Include]
        internal bool alwaysUpdateTerrainLODs = alwaysUpdateTerrainLODsDefault;

        private const bool invertBrushTexturesGloballyDefault = false;
        [Include]
        internal bool invertBrushTexturesGlobally = invertBrushTexturesGloballyDefault;

        private const ushort cacheSizeDefault = 7000;
        [Include]
        internal ushort cacheSize = cacheSizeDefault;

        private const BrushSelectionDisplayType brushSelectionDisplayTypeDefault = BrushSelectionDisplayType.Tabbed;
        [Include]
        internal BrushSelectionDisplayType brushSelectionDisplayType = brushSelectionDisplayTypeDefault;

        [Exclude]
        internal string path;
        
        [Include]
        internal Dictionary<Tool, ModeSettings> modeSettings; // TODO: I can't change this to the more correct "toolSettings" since it will break Settings.tf files

        [Include]
        private ModeSettingPairs[] fauxModeSettings; // A version of Dictionary<Tool, ToolSettings> that Unity will be able to serialize.
        [Exclude]
        private static readonly Keyframe[] defaultSmoothBrushFalloffKeys = new Keyframe[] { new Keyframe(0f, 0f, 2f, 2f), new Keyframe(1f, 1f, 0f, 0f) };
        [Exclude]
        private static readonly Color brushColourDefault = new Color(51f / 255f, 178f / 255f, 1f, 155f / 255f);
        [Exclude]
        internal SavedColor brushColour;
        
        // Perlin Noise Brush
        [Include]
        internal float perlinNoiseScale = 20f;
        [Include]
        internal float perlinNoiseMin = 0f;
        [Include]
        internal float perlinNoiseMax = 1f;

        // Generate
        [Exclude]
        internal AnimationCurve generateRampCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0f, 1f));
        [Include]
        internal bool generateRampCurveInXAxis = true;
        [Include]
        internal float generateHeight = 5f;
        
        // Flatten
        [Include]
        internal FlattenMode flattenMode = FlattenMode.Flatten;

        // Set Height
        [Include]
        internal float setHeight = 10f;
        
        /**
        * Smooth
        * ----
        * Note that this is used for the radius + 1 to give us the actual size. 
        * It allows for the smoothing loop to start at -boxFilterSize and end at positive boxFilterSize - meaning the total size is: r * 2 + 1
        */
        [Include]
        internal int boxFilterSize = 7; 
        [Include]
        internal int smoothingIterations = 1;

        // Mould
        [Include]
        internal float mouldToolRaycastOffset = 0f;
        [Include]
        internal bool mouldToolRaycastTopDown = false;
        [Include]
        internal int mouldToolBoxFilterSize = 7;
        [Include]
        internal int mouldAllIterations = 3;

        // Paint Texture
        [Include]
        internal float targetOpacity = 1f;
        [Include]
        internal int selectedTextureIndex = 0;
        
        // Heightmap
        [Exclude]
        internal bool heightmapSourceIsAlpha = false;
        [Exclude]
        internal float heightmapHeightOffset = 20f;

        [Include]
        internal string mainDirectory; // The path to the main folder of Terrain Former

        public static void Create(bool readFile = true) {
            if(cached != null) return;

            // Look for the main directory by finding the path of the Terrain Former script.
            #if UNITY_2021_1_OR_NEWER
            string absolutePath = TerrainFormer.GetScriptFilePath();
            string terrainFormerPath = Utilities.GetLocalPathFromAbsolutePath(absolutePath);
            #else
            GameObject temporaryGameObject = EditorUtility.CreateGameObjectWithHideFlags("TerrainFormerTemporaryObject", HideFlags.HideAndDontSave);
            TerrainFormer terrainFormerComponent = temporaryGameObject.AddComponent<TerrainFormer>();
            string terrainFormerPath = AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(terrainFormerComponent));
            UnityEngine.Object.DestroyImmediate(terrainFormerComponent);
            UnityEngine.Object.DestroyImmediate(temporaryGameObject);
            #endif

            string mainDirectory = Path.GetDirectoryName(terrainFormerPath).Replace('\\', '/') + "/";
            string settingsPath = Utilities.GetAbsolutePathFromLocalPath(Path.Combine(mainDirectory, "Settings.tf"));
            
            if(string.IsNullOrEmpty(mainDirectory) || Directory.Exists(mainDirectory) == false) {
                Debug.LogError("Terrain Former wasn't able to find its main directory.");
                return;
            }
            
            Settings newSettings = null;

            if(readFile && File.Exists(settingsPath)) {
                string jsonFileText = File.ReadAllText(settingsPath);
                try {
                    newSettings = JsonUtility.FromJson<Settings>(jsonFileText);    
                } catch {
                    if(jsonFileText.Contains("JesseStiller.TerrainFormerExtension.Settings")) {
                        Debug.LogWarning("Terrain Former is upgrading your Settings.tf file from using TinyJSON to Unity's built-in JSON utility. All existing settings are reset.");
                    } else {
                        // If the settings file couldn't be read, just create a new settings file so we don't bother the user and since an invalid settings file is no use at all, just create a new one.
                        Debug.LogWarning("Terrain Former has created a new settings file because the last one was invalid or malformed.");
                    }
                }
            }

            if(newSettings == null) { 
                newSettings = new Settings();
                newSettings.modeSettings = new Dictionary<Tool, ModeSettings>();
            }

            // Add the following mode settings as needed.
            if(newSettings.modeSettings.ContainsKey(Tool.RaiseOrLower) == false) {
                newSettings.modeSettings = new Dictionary<Tool, ModeSettings>();
                newSettings.modeSettings.Add(Tool.RaiseOrLower, new ModeSettings());
            }

            if(newSettings.modeSettings.ContainsKey(Tool.SetHeight) == false) {
                newSettings.modeSettings.Add(Tool.SetHeight, new ModeSettings());
                newSettings.modeSettings[Tool.SetHeight].brushSpeed = 100f;
            }

            if(newSettings.modeSettings.ContainsKey(Tool.Flatten) == false) {
                newSettings.modeSettings.Add(Tool.Flatten, new ModeSettings());
                newSettings.modeSettings[Tool.Flatten].brushSpeed = 100f;
            }

            if(newSettings.modeSettings.ContainsKey(Tool.Smooth) == false) {
                newSettings.modeSettings.Add(Tool.Smooth, new ModeSettings());
                // Have a thicker falloff shape to make smoothing quicker overall
                newSettings.modeSettings[Tool.Smooth].brushFalloff = new AnimationCurve(defaultSmoothBrushFalloffKeys);
                newSettings.modeSettings[Tool.Smooth].brushSpeed = 100f;
            }

            if(newSettings.modeSettings.ContainsKey(Tool.Mould) == false) {
                newSettings.modeSettings.Add(Tool.Mould, new ModeSettings());
                newSettings.modeSettings[Tool.Mould].brushFalloff = new AnimationCurve(defaultSmoothBrushFalloffKeys);
                newSettings.modeSettings[Tool.Mould].brushSpeed = 100f;
            }

            if(newSettings.modeSettings.ContainsKey(Tool.PaintTexture) == false) {
                newSettings.modeSettings.Add(Tool.PaintTexture, new ModeSettings());
                newSettings.modeSettings[Tool.PaintTexture].brushFalloff = new AnimationCurve(defaultSmoothBrushFalloffKeys);
                newSettings.modeSettings[Tool.PaintTexture].brushSpeed = 100f;
            }

            newSettings.mainDirectory = mainDirectory;
            newSettings.path = settingsPath;
            newSettings.brushColour = new SavedColor("TerrainFormer/BrushColour", brushColourDefault);
            
            newSettings.Save();

            cached = newSettings;
        }

        public void Save() {
            // If the the setting's directory doesn't exist, return since we assume this means that Terrain Former has been moved.
            if(Directory.Exists(Path.GetDirectoryName(path)) == false) return;

            File.WriteAllText(path, EditorJsonUtility.ToJson(this, true));
        }

        internal bool AreSettingsDefault() {
            return brushColour.Value == brushColourDefault &&
                showSculptingGridPlane == showSculptingGridPlaneDefault &&
                raycastOntoFlatPlane == raycastOntoFlatPlaneDefault &&
                showSceneViewInformation == showSceneViewInformationDefault &&
                displaySceneViewSculptOntoMode == displaySceneViewSculptOntoModeDefault &&
                displaySceneViewCurrentTool == displaySceneViewCurrentToolDefault &&
                displaySceneViewCurrentHeight == displaySceneViewCurrentHeightDefault &&
                brushPreviewSize == brushPreviewSizeDefault &&
                texurePreviewSize == texurePreviewSizeDefault &&
                alwaysShowBrushSelection == alwaysShowBrushSelectionDefault &&
                alwaysUpdateTerrainLODs == alwaysUpdateTerrainLODsDefault &&
                invertBrushTexturesGlobally == invertBrushTexturesGloballyDefault &&
                brushSelectionDisplayType == brushSelectionDisplayTypeDefault;
        }

        internal void RestoreDefaultSettings() {
            brushColour.Value = brushColourDefault;
            showSculptingGridPlane = showSculptingGridPlaneDefault;
            raycastOntoFlatPlane = raycastOntoFlatPlaneDefault;
            showSceneViewInformation = showSceneViewInformationDefault;
            displaySceneViewSculptOntoMode = displaySceneViewSculptOntoModeDefault;
            displaySceneViewCurrentTool = displaySceneViewCurrentToolDefault;
            displaySceneViewCurrentHeight = displaySceneViewCurrentHeightDefault;
            brushPreviewSize = brushPreviewSizeDefault;
            texurePreviewSize = texurePreviewSizeDefault;
            alwaysShowBrushSelection = alwaysShowBrushSelectionDefault;
            alwaysUpdateTerrainLODs = alwaysUpdateTerrainLODsDefault;
            invertBrushTexturesGlobally = invertBrushTexturesGloballyDefault;
            brushSelectionDisplayType = brushSelectionDisplayTypeDefault;
        }

        public void OnBeforeSerialize() {
            fauxModeSettings = new ModeSettingPairs[modeSettings.Count];

            int i = 0;
            foreach(KeyValuePair<Tool, ModeSettings> pair in modeSettings) {
                fauxModeSettings[i] = new ModeSettingPairs(pair.Key, pair.Value);
                i++;
            }
        }

        public void OnAfterDeserialize() {
            if(fauxModeSettings == null) {
                Create(false);
                return;
            }

            modeSettings = new Dictionary<Tool, ModeSettings>();
            
            for(int i = 0; i < fauxModeSettings.Length; i++) {
                modeSettings.Add(fauxModeSettings[i].tool, fauxModeSettings[i].settings);
            }
        }
    }

    /**
    * Unity's JsonUtility can't serialize Dictionaries, so we must create a serializable version that can be
    * written to and read from
    */
    [Serializable]
    internal class ModeSettingPairs {
        public Tool tool;
        public ModeSettings settings;

        public ModeSettingPairs(Tool tool, ModeSettings settings) {
            this.tool = tool;
            this.settings = settings;
        }
    }
}