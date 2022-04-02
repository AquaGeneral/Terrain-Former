using System.Runtime.CompilerServices;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    public class TerrainFormer : MonoBehaviour {
        private void Awake() {
            Destroy(this);
        }

        #if UNITY_EDITOR && UNITY_2021_1_OR_NEWER
        public static string GetScriptFilePath() {
            return _InternalGetScriptFilePath();
        }

        private static string _InternalGetScriptFilePath([CallerFilePath] string path = "") {
            return path;
        }
        #endif
    }
}