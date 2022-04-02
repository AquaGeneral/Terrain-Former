using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class PaintTextureEditorWindow : EditorWindow {
        private const float defaultWidth = 300f;
        private const float defaultHeight = 245f;

        private static readonly MethodInfo hasAlphaTextureFormatMethod;

        private GUIStyle centeredLabel;

        private TerrainLayer terrainLayer;
        private float normalScale;
        private Texture2D mask;
        private bool workingWithAssetParams;

        private int selectedTextureIndex;
        private Texture2D diffuse;
        private Texture2D normalMap;
        private Vector2 tileSize;
        private Vector2 tileOffset;
        private Color specularColour;
        private float metallicness;
        private float smoothness;

        private bool isAddingNewSplatPrototype = false;

        static PaintTextureEditorWindow() {
            Type textureUtil = Assembly.GetAssembly(typeof(Editor)).GetType("UnityEditor.TextureUtil");
            hasAlphaTextureFormatMethod = textureUtil.GetMethod("HasAlphaTextureFormat", BindingFlags.Static | BindingFlags.Public);
        }

        private static PaintTextureEditorWindow InitializeWindow() {
            PaintTextureEditorWindow paintTextureEditor = GetWindow<PaintTextureEditorWindow>(true, "Terrain Former", true);
            paintTextureEditor.minSize = new Vector2(defaultWidth, defaultHeight);
            paintTextureEditor.maxSize = new Vector2(defaultWidth, defaultHeight);
            paintTextureEditor.workingWithAssetParams = true;
            return paintTextureEditor;
        }

        public static void CreateAndShowForAdditions() {
            PaintTextureEditorWindow paintTextureEditor = InitializeWindow();
            paintTextureEditor.isAddingNewSplatPrototype = true;
            paintTextureEditor.tileSize = new Vector2(1f, 1f);
            paintTextureEditor.tileOffset = Vector2.zero;
            paintTextureEditor.diffuse = null;
            paintTextureEditor.normalMap = null;
            paintTextureEditor.normalScale = 1f;
            paintTextureEditor.selectedTextureIndex = 0;
        }

        public static void CreateAndShow(int selectedTextureIndex) {
            PaintTextureEditorWindow paintTextureEditor = InitializeWindow();

            if(selectedTextureIndex >= 0) {
                paintTextureEditor.terrainLayer = TerrainFormerEditor.splatPrototypes[selectedTextureIndex];
            }

            paintTextureEditor.selectedTextureIndex = selectedTextureIndex;
            paintTextureEditor.isAddingNewSplatPrototype = false;
            paintTextureEditor.normalMap = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].normalMapTexture;
            paintTextureEditor.mask = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].maskMapTexture;
            paintTextureEditor.normalScale = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].normalScale;
            paintTextureEditor.diffuse = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].diffuseTexture;
            paintTextureEditor.tileSize = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].tileSize;
            paintTextureEditor.specularColour = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].specular;
            paintTextureEditor.metallicness = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].metallic;
            paintTextureEditor.smoothness = TerrainFormerEditor.splatPrototypes[selectedTextureIndex].smoothness;

            paintTextureEditor.tileOffset = TerrainFormerEditor.Instance.firstTerrainData.terrainLayers[selectedTextureIndex].tileOffset;
            
            paintTextureEditor.Show();
        }


        private void OnEnable() {
            Selection.selectionChanged += SelectionChanged;
        }

        private void OnDisable() {
            Selection.selectionChanged -= SelectionChanged;
        }


        private void SelectionChanged() {
            if(TerrainFormerEditor.Instance == null) Close();
        }

        private void OnGUI() {
            SelectionChanged();

            if(centeredLabel == null) {
                centeredLabel = new GUIStyle(GUI.skin.label);
                centeredLabel.alignment = TextAnchor.MiddleCenter;
            }

            EditorGUIUtility.labelWidth = 120f;

            GUILayout.BeginVertical(GUIStyle.none);

            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Toggle(workingWithAssetParams == false, isAddingNewSplatPrototype ? "Add existing Terrain Layer" : "Replace with existing Terrain Layer", EditorStyles.radioButton)) {
                workingWithAssetParams = false;
            }
                 
            GUI.enabled = workingWithAssetParams == false;
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = 1;
            terrainLayer = (TerrainLayer)EditorGUILayout.ObjectField(terrainLayer, typeof(TerrainLayer), false);
            EditorGUI.indentLevel = 0;

            GUI.enabled = true;

            /**
            * Or seperator
            */
            GUILayout.Space(2f);
            using(new EditorGUILayout.HorizontalScope()) { 
                if(itallicBoldLabel == null) {
                    itallicBoldLabel = new GUIStyle(EditorStyles.boldLabel);
                    itallicBoldLabel.fontStyle = FontStyle.BoldAndItalic;
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label("or...", itallicBoldLabel);
                GUILayout.FlexibleSpace();
            }
            GUILayout.Space(2f);

            if(GUILayout.Toggle(workingWithAssetParams, isAddingNewSplatPrototype ? "Create New Terrain Layer" : "Edit Terrain Layer", EditorStyles.radioButton)) { 
                workingWithAssetParams = true;
            }

            GUI.enabled = workingWithAssetParams;
            EditorGUI.indentLevel = 1;

            EditorGUILayout.BeginHorizontal();
            /**
            * Main/Albedo/Diffuse Texture
            */
            
            EditorGUI.indentLevel = 0;
            EditorGUILayout.BeginVertical();

            GUILayout.Label("Albedo (RGB)\nSmoothness (A)", centeredLabel);

#if UNITY_2018_3_OR_NEWER
            float texObjectFieldWidth = EditorGUIUtility.currentViewWidth / 3f;
#else
            float texObjectFieldWidth = EditorGUIUtility.currentViewWidth / 2f;
#endif

            using(new EditorGUILayout.HorizontalScope(GUILayout.Width(texObjectFieldWidth))) {
                GUILayout.FlexibleSpace();
                diffuse = (Texture2D)EditorGUI.ObjectField(GUILayoutUtility.GetRect(64f, 64f), GUIContent.none, diffuse, typeof(Texture2D), false);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndVertical();

            /**
            * Normal Texture
            */
            EditorGUILayout.BeginVertical();
            GUILayout.Label("\nNormal", centeredLabel);
            using(new EditorGUILayout.HorizontalScope(GUILayout.Width(texObjectFieldWidth))) {
                GUILayout.FlexibleSpace();
                normalMap = (Texture2D)EditorGUI.ObjectField(GUILayoutUtility.GetRect(64f, 64f), GUIContent.none, normalMap, typeof(Texture2D), false);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndVertical();

            /**
            * Mask Texture
            */
            EditorGUILayout.BeginVertical();
            GUILayout.Label("\nMask", centeredLabel);
            using(new EditorGUILayout.HorizontalScope(GUILayout.Width(texObjectFieldWidth))) {
                GUILayout.FlexibleSpace();
                mask = (Texture2D)EditorGUI.ObjectField(GUILayoutUtility.GetRect(64f, 64f), GUIContent.none, mask, typeof(Texture2D), false);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel = 1;

            tileSize = EditorGUILayout.Vector2Field("Tile Size", tileSize);
            tileOffset = EditorGUILayout.Vector2Field("Tile Offset", tileOffset);
            metallicness = EditorGUILayout.Slider("Metallic", metallicness, 0f, 1f);
            if(diffuse != null && (bool)hasAlphaTextureFormatMethod.Invoke(null, new object[] { diffuse.format }) == false) {
                smoothness = EditorGUILayout.Slider("Smoothness", smoothness, 0f, 1f);
            }
            normalScale = EditorGUILayout.FloatField("Normal Scale", normalScale);

            GUILayout.Space(10f);

            GUI.enabled = true;

            using(new GUIUtilities.GUIEnabledBlock(ValidateMainTexture())) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if(GUILayout.Button("OK", GUILayout.Width(75f), GUILayout.Height(22f))) {
                    Apply();
                    Close();
                    if(TerrainFormerEditor.Instance != null) TerrainFormerEditor.Instance.Repaint();
                }
                if(GUILayout.Button("Cancel", GUILayout.Width(75f), GUILayout.Height(22f))) {
                    Close();
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            if(Event.current.type != EventType.Layout && Event.current.type != EventType.Used) {
                Rect lastRect = GUILayoutUtility.GetLastRect();
                minSize = new Vector2(defaultWidth, lastRect.height + 5f);
                maxSize = new Vector2(defaultWidth, lastRect.height + 5f);
            }
        }

        private void Apply() {
            TerrainFormerEditor.Instance.RegisterUndoForTerrainGrid(isAddingNewSplatPrototype ? "Added Terrain Texture" : "Modified Terrain Texture", true);

            TerrainLayer splatPrototype;
            if(isAddingNewSplatPrototype) {
                Array.Resize(ref TerrainFormerEditor.splatPrototypes, TerrainFormerEditor.splatPrototypes.Length + 1);
                splatPrototype = new TerrainLayer();
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/NewLayer.terrainlayer");
                AssetDatabase.CreateAsset(splatPrototype, path);
            } else {
                splatPrototype = TerrainFormerEditor.splatPrototypes[selectedTextureIndex];
            }
            splatPrototype.diffuseTexture = diffuse;
            splatPrototype.normalMapTexture = normalMap;
            splatPrototype.metallic = metallicness;
            splatPrototype.smoothness = smoothness;
            splatPrototype.specular = specularColour;
            splatPrototype.tileOffset = tileOffset;
            splatPrototype.tileSize = tileSize;
            splatPrototype.normalScale = normalScale;
            splatPrototype.maskMapTexture = mask;

            if(isAddingNewSplatPrototype) {
                TerrainFormerEditor.splatPrototypes[TerrainFormerEditor.splatPrototypes.Length - 1] = splatPrototype;
            }

            TerrainFormerEditor.Instance.ApplySplatPrototypes(tileOffset, selectedTextureIndex);
        }

        private StringBuilder invalidationDescription;
        private GUIStyle itallicBoldLabel;

        private bool ValidateMainTexture() {
            if(diffuse == null) {
                EditorGUILayout.HelpBox("A main texture must be assigned.", MessageType.Warning);
                return false;
            }

            bool isValid = true;
            if(diffuse.wrapMode != TextureWrapMode.Repeat || diffuse.width != Mathf.ClosestPowerOfTwo(diffuse.width) || diffuse.height != Mathf.ClosestPowerOfTwo(diffuse.height) ||
                diffuse.mipmapCount <= 1) {
                isValid = false;
                invalidationDescription = new StringBuilder();
            }

            if(diffuse.wrapMode != TextureWrapMode.Repeat) {
                invalidationDescription.AppendLine("  • The main texture must have wrap mode set to \"Repeat\".");
            }
            if(diffuse.width != Mathf.ClosestPowerOfTwo(diffuse.width) || diffuse.height != Mathf.ClosestPowerOfTwo(diffuse.height)) {
                invalidationDescription.AppendLine("  • The main texture's size must be a power of two (eg, 512x512, 1024x1024).");
            }
            if(diffuse.mipmapCount <= 1) {
                invalidationDescription.AppendLine("  • The main texture must have mipmaps.");
            }

            if(isValid == false) {
                invalidationDescription.Insert(0, "The following issues must be resolved in order to apply any changes:\n");

                GUIUtilities.ActionableHelpBox(invalidationDescription.ToString(), MessageType.Warning, () => {
                    if(GUILayout.Button("Fix All", GUILayout.Width(70f), GUILayout.Height(20f))) {
                        TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(diffuse));
                        textureImporter.wrapMode = TextureWrapMode.Repeat;
                        textureImporter.npotScale = TextureImporterNPOTScale.ToNearest;
                        textureImporter.mipmapEnabled = true;
                        textureImporter.SaveAndReimport();
                    }
                });
            }

            return isValid;
        }
    }
}