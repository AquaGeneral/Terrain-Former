using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using System.Linq;

namespace JesseStiller.TerrainFormerExtension {
    internal static class BrushCollection {
        internal const string defaultFalloffBrushId = "_DefaultFalloffBrushName";
        internal const string defaultPerlinNoiseBrushId = "_DefaultPerlinNoiseBrushName";

        internal static string absoluteCustomBrushPath;
        internal static string localCustomBrushPath;

        public static List<Brush> brushes;

        public static Dictionary<string, Type> terrainBrushTypes;

        private static bool initialized = false;

        private class TerrainBrushTypesInfo {
            internal int sortOrder;
            internal string prettyTypeName;
            internal Type type;

            internal TerrainBrushTypesInfo(int sortOrder, string prettyTypeName, Type type) {
                this.sortOrder = sortOrder;
                this.prettyTypeName = prettyTypeName;
                this.type = type;
            }
        }

        public static void Initilize() {
            if(initialized == true) return;

            absoluteCustomBrushPath = Path.Combine(Utilities.GetAbsolutePathFromLocalPath(Settings.cached.mainDirectory), "Textures/Brushes");
            localCustomBrushPath = Utilities.GetLocalPathFromAbsolutePath(absoluteCustomBrushPath);

            brushes = new List<Brush>();
            brushes.Add(new FalloffBrush("Falloff Brush", defaultFalloffBrushId));
            brushes.Add(new PerlinNoiseBrush("Perlin Noise Brush", defaultPerlinNoiseBrushId));

            RefreshCustomBrushes();

            terrainBrushTypes = new Dictionary<string, Type>();
            terrainBrushTypes.Add("All", null);

            List<TerrainBrushTypesInfo> terrainBrushTypesInfo = new List<TerrainBrushTypesInfo>();

            Type[] allAssemblyTypes = typeof(TerrainFormerEditor).Assembly.GetTypes();
            // Gather all classes that derrive from TerrainBrush
            foreach(Type type in allAssemblyTypes) {
                if(type.IsSubclassOf(typeof(Brush)) == false) continue;

                BindingFlags nonPublicStaticBindingFlags = BindingFlags.NonPublic | BindingFlags.Static;
                FieldInfo prettyTypeNameFieldInfo = type.GetField("prettyTypeName", nonPublicStaticBindingFlags);
                string prettyTypeName = prettyTypeNameFieldInfo == null ? type.Name : (string)prettyTypeNameFieldInfo.GetValue(null);

                FieldInfo typeSortOrderFieldInfo = type.GetField("typeSortOrder", nonPublicStaticBindingFlags);
                int typeSortOrder = typeSortOrderFieldInfo == null ? 10 : (int)typeSortOrderFieldInfo.GetValue(null);

                terrainBrushTypesInfo.Add(new TerrainBrushTypesInfo(typeSortOrder, prettyTypeName, type));
            }

            terrainBrushTypesInfo.Sort(delegate (TerrainBrushTypesInfo x, TerrainBrushTypesInfo y) {
                if(x.sortOrder < y.sortOrder) return x.sortOrder;
                else return y.sortOrder;
            });

            foreach(TerrainBrushTypesInfo t in terrainBrushTypesInfo) {
                terrainBrushTypes.Add(t.prettyTypeName, t.type);
            }

            initialized = true;
        }

        // The parameter UpdatedBrushes requires local Unity assets paths
        internal static void RefreshCustomBrushes(string[] updatedBrushes = null) {
            // If there is no data on which brushes need to be updated, assume every brush must be updated
            if(updatedBrushes == null) {
                updatedBrushes = Directory.GetFiles(absoluteCustomBrushPath, "*", SearchOption.AllDirectories);

                for(int i = 0; i < updatedBrushes.Length; i++) {
                    updatedBrushes[i] = Utilities.GetLocalPathFromAbsolutePath(updatedBrushes[i]);
                }
            }

            // Get the custom brush textures
            foreach(string path in updatedBrushes) {
                if(path.EndsWith(".meta")) continue;

                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if(tex == null) continue;

                TextureImporter textureImporter = (TextureImporter)AssetImporter.GetAtPath(path);
                if(textureImporter.isReadable == false || textureImporter.wrapMode != TextureWrapMode.Clamp ||
                    textureImporter.textureCompression != TextureImporterCompression.Uncompressed) {

                    textureImporter.isReadable = true;
                    textureImporter.wrapMode = TextureWrapMode.Clamp;
                    textureImporter.textureCompression = TextureImporterCompression.Uncompressed;

                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                    // Reload the texture with the updated settings
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }

                if(tex.width != tex.height) continue;

                string textureGUID = AssetDatabase.AssetPathToGUID(path);
                ImageBrush imageBrush = GetBrushById(textureGUID) as ImageBrush;
                if(imageBrush == null) {
                    brushes.Add(new ImageBrush(tex.name, textureGUID, tex));
                } else {
                    // TODO: This might not be necessary
                    imageBrush.sourceTexture = tex;
                }
            }

            brushes.OrderBy(brush => brush.name);
        }

        internal static Brush GetBrushById(string id) {
            if(string.IsNullOrEmpty(id)) return null;
            Brush brush = null;
            foreach(Brush b in brushes) {
                if(b.id != id) continue;
                brush = b as Brush;
                break;
            }
            return brush;
        }

        internal static void UpdatePreviewTextures() {
            foreach(Brush terrainBrush in brushes) {
                terrainBrush.CreatePreviewTexture();
            }
        }

        internal static void RemoveDeletedBrushes(string[] deletedBrushes) {
            for(int d = 0; d < deletedBrushes.Length; d++) {
                for(int i = brushes.Count - 1; i >= 0; i--) {
                    string deletedBrushId = AssetDatabase.AssetPathToGUID(deletedBrushes[d]);
                    if(brushes[i].id == deletedBrushId) {
                        brushes.RemoveAt(i);
                    }
                }
            }
        }
    }
}
