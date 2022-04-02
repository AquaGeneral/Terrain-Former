using UnityEditor;

namespace JesseStiller.TerrainFormerExtension { 
    internal class SavedTool {
        internal readonly string preferencesKey;

        private Tool value;
        internal Tool Value {
            get {
                return value;
            }
            set {
                if(this.value == value) return;
                this.value = value;
                EditorPrefs.SetInt(preferencesKey, (int)value);
            }
        }

        public SavedTool(string preferencesKey, Tool defaultValue) {
            this.preferencesKey = preferencesKey;
            value = (Tool)EditorPrefs.GetInt(preferencesKey, (int)defaultValue);
        }
    }
}