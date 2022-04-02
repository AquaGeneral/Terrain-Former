using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JesseStiller.TerrainFormerExtension {
    internal class AssetWatcher : AssetPostprocessor {
        public static Action<string[]> OnAssetsImported;
        public static Action<string[], string[]> OnAssetsMoved;
        public static Action<string[]> OnAssetsDeleted;
        public static Action<string[]> OnWillSaveAssetsAction;
        
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssetsDestination, string[] movedAssetsSource) {
            #if UNITY_2021_2_OR_NEWER
            SetIconOfTerrainFormerComponent(importedAssets);
            SetIconOfTerrainFormerComponent(movedAssetsDestination);
            #endif

            if(OnAssetsImported != null && importedAssets != null && importedAssets.Length != 0) {
                OnAssetsImported(importedAssets);
            }

            if(OnAssetsMoved != null && movedAssetsSource != null && movedAssetsSource.Length != 0) {
                OnAssetsMoved(movedAssetsSource, movedAssetsDestination);
            }

            if(OnAssetsDeleted != null && deletedAssets != null && deletedAssets.Length != 0) {
                OnAssetsDeleted(deletedAssets);
            }
        }

        private static string[] OnWillSaveAssets(string[] paths) {
            if(OnWillSaveAssetsAction != null) {
                OnWillSaveAssetsAction(paths);
            }

            return paths;
        }

        #if UNITY_2021_2_OR_NEWER
        private static void SetIconOfTerrainFormerComponent(string[] paths) {
            Settings.Create();
            if(Settings.cached == null) return;

            foreach(string path in paths) { 
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
                if(obj == null) continue;
                AssetImporter assetImporter = AssetImporter.GetAtPath(path);
                if(assetImporter == null) continue;
                MonoImporter monoImporter = assetImporter as MonoImporter;
                if(monoImporter == null) continue;

                if(path.EndsWith("TerrainFormer.cs", true, CultureInfo.InvariantCulture) == false) return;

                Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/Icon.png");
                monoImporter.SetIcon(icon);
            }
        }
        #endif

        private void OnPreprocessTexture() {
            // Return if the BrushCollection hasn't been initialized prior to this method being called
            if(string.IsNullOrEmpty(BrushCollection.localCustomBrushPath)) return;

            if(assetPath.StartsWith(BrushCollection.localCustomBrushPath, StringComparison.Ordinal)) {
                TextureImporter textureImporter = (TextureImporter)assetImporter;

                if(textureImporter.isReadable == false || textureImporter.wrapMode != TextureWrapMode.Clamp || textureImporter.textureCompression != TextureImporterCompression.Uncompressed) {
                    textureImporter.isReadable = true;
                    textureImporter.wrapMode = TextureWrapMode.Clamp;
                    textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                }
            }
        }
    }
}
