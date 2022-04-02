using System;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension { 
    internal class SavedColor {
        internal Action ValueChanged;

        internal readonly string prefsKey;

        private Color value;
        internal Color Value {
            get {
                return value;
            }
            set {
                if(this.value == value) return;
                this.value = value;
                EditorPrefs.SetFloat(prefsKey + "_R", value.r);
                EditorPrefs.SetFloat(prefsKey + "_G", value.g);
                EditorPrefs.SetFloat(prefsKey + "_B", value.b);
                EditorPrefs.SetFloat(prefsKey + "_A", value.a);

                if(ValueChanged != null) ValueChanged();
            }
        }

        public SavedColor(string prefsKey, Color defaultValue) {
            this.prefsKey = prefsKey;
            value.r = EditorPrefs.GetFloat(prefsKey + "_R", defaultValue.r);
            value.g = EditorPrefs.GetFloat(prefsKey + "_G", defaultValue.g);
            value.b = EditorPrefs.GetFloat(prefsKey + "_B", defaultValue.b);
            value.a = EditorPrefs.GetFloat(prefsKey + "_A", defaultValue.a);
        }

        public static implicit operator Color(SavedColor s) {
            return s.Value;
        }
    }
}