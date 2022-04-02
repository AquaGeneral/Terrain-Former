using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal class Shortcut {
        private static GUIStyle italicTextField;

        private static readonly int shortcutFieldHash = "ShortcutField".GetHashCode();
        internal static bool wasExecuted = false;

        internal static readonly Dictionary<string, Shortcut> Shortcuts = new Shortcut[] {
            new Shortcut("Increase Brush Speed", "="),
            new Shortcut("Decrease Brush Speed", "-"),
            new Shortcut("Increase Brush Size", "]"),
            new Shortcut("Decrease Brush Size", "["),
            new Shortcut("Rotate Brush Anticlockwise", "'"),
            new Shortcut("Rotate Brush Clockwise", ";"),
            new Shortcut("Reset Brush Rotation", "0"),
            new Shortcut("Next Brush", "p"),
            new Shortcut("Previous Brush", "o"),
            new Shortcut("Toggle Sculpt Onto Mode", "i"),
            new Shortcut("Previous Flatten Mode", "y"),
            new Shortcut("Next Flatten Mode", "u"),
            new Shortcut("Previous Texture", "y"),
            new Shortcut("Next Texture", "u"),
            new Shortcut("Select Raise/Lower Tool", "z"),
            new Shortcut("Select Smooth Tool", "x"),
            new Shortcut("Select Set Height Tool", "c"),
            new Shortcut("Select Flatten Tool", "v"),
            new Shortcut("Select Mould Tool", "b"),
            new Shortcut("Select Paint Texture Tool", "n"),
            new Shortcut("Select Heightmap Tool", ""),
            new Shortcut("Select Generate Tool", ""),
            new Shortcut("Select Settings Tool", "m"),
            new Shortcut("Flatten Terrain", "#g")
        }.ToDictionary(c => c.Name, c => c);

        public string Name { get; private set; }

        internal readonly string preferencesKey;
        internal readonly string defaultBinding;
        internal bool waitingForInput = false;
        private string bindingFormatted;

        private string binding;
        internal string Binding {
            get {
                return binding;
            }
            set {
                if(binding == value) return;

                EditorPrefs.SetString(preferencesKey, value);
                binding = value;
                UpdateBindingFormatted();
            }
        }

        internal Shortcut(string name, string defaultBinding) {
            Name = name;
            this.defaultBinding = defaultBinding;
            preferencesKey = name.Replace(" ", string.Empty);
            Binding = EditorPrefs.GetString(preferencesKey, defaultBinding);
        }
        
        internal void DoShortcutField() {
            if(italicTextField == null) {
                italicTextField = new GUIStyle(GUI.skin.textField);
                italicTextField.fontStyle = FontStyle.Italic;
            }

            Rect controlRect = EditorGUILayout.GetControlRect();
            Rect keyFieldRect = EditorGUI.PrefixLabel(controlRect, new GUIContent(Name));

#if UNITY_5_5_OR_NEWER
            int controlID = GUIUtility.GetControlID(shortcutFieldHash, FocusType.Passive, keyFieldRect);
#else
            int controlID = GUIUtility.GetControlID(shortcutFieldHash, FocusType.Native, keyFieldRect);
#endif

            Event current = Event.current;

            switch(current.type) {
                case EventType.MouseDown:
                    if(keyFieldRect.Contains(current.mousePosition)) {
                        waitingForInput = !waitingForInput;

                        GUIUtility.hotControl = controlID;
                        current.Use();
                    }
                    break;
                case EventType.MouseUp:
                case EventType.MouseDrag:
                    if(GUIUtility.hotControl == controlID) {
                        current.Use();
                    }
                    break;
                case EventType.KeyDown:
                    if(GUIUtility.hotControl == controlID && waitingForInput) {
                        bool inputValid = ValidateInput(current);
                        if(current.keyCode == KeyCode.Escape || inputValid) {
                            waitingForInput = false;
                            GUIUtility.hotControl = 0;
                        }

                        if(inputValid) {
                            EncodeBinding(current);
                        }

                        current.Use();
                    }
                    break;
                case EventType.Repaint:
                    if(GUIUtility.hotControl != controlID) {
                        waitingForInput = false;
                    }

                    GUIStyle textFieldStyle = string.IsNullOrEmpty(binding.Trim()) && waitingForInput == false ? italicTextField : GUI.skin.textField;
                    
                    textFieldStyle.Draw(keyFieldRect, new GUIContent(waitingForInput ? "Waiting for input…" : bindingFormatted), controlID);
                    break;
            }
        }

        internal bool WasExecuted(Event currentEvent) {
            if(string.IsNullOrEmpty(Binding.Trim())) return false;
            bool wasExecuted = currentEvent.Equals(Event.KeyboardEvent(Binding));
            if(wasExecuted) Shortcut.wasExecuted = true;
            return wasExecuted;
        }

        internal void EncodeBinding(Event evt) {
            StringBuilder sb = new StringBuilder();

            if(evt.control) {
                sb.Append("^");
            }
            if(evt.shift) {
                sb.Append("#");
            }
            if(evt.alt) {
                sb.Append("&");
            }
            if(evt.command) {
                sb.Append("%");
            }

            sb.Append(ConvertKeyCodeToString(evt.keyCode));
            Binding = sb.ToString();
        }

        private void UpdateBindingFormatted() {
            if(string.IsNullOrEmpty(binding.Trim())) {
                bindingFormatted = "Unbound";
                return;
            }

            StringBuilder sb = new StringBuilder();
            
            for(int i = 0; i < binding.Length; i++) {
                switch(binding[i]) {
                    case '^':
                        sb.Append("Ctrl+");
                        break;
                    case '&':
                        sb.Append("Alt+");
                        break;
                    case '#':
                        sb.Append("Shift+");
                        break;
                    case '%':
                        switch(Application.platform) {
                            case RuntimePlatform.OSXEditor:
                                sb.Append("⌘+");
                                break;
                            case RuntimePlatform.WindowsEditor:
                                sb.Append("⊞Win+");
                                break;
                            default:
                                sb.Append("%");
                                break;
                        }
                        break;
                    default:
                        sb.Append(char.ToUpper(binding[i]));
                        break;
                }
            }

            bindingFormatted = sb.ToString();
        }

        private static string ConvertKeyCodeToString(KeyCode keyCode) {
            switch(keyCode) {
                case KeyCode.Keypad0:
                    return "[0]";
                case KeyCode.Keypad1:
                    return "[1]";
                case KeyCode.Keypad2:
                    return "[2]";
                case KeyCode.Keypad3:
                    return "[3]";
                case KeyCode.Keypad4:
                    return "[4]";
                case KeyCode.Keypad5:
                    return "[5]";
                case KeyCode.Keypad6:
                    return "[6]";
                case KeyCode.Keypad7:
                    return "[7]";
                case KeyCode.Keypad8:
                    return "[8]";
                case KeyCode.Keypad9:
                    return "[9]";
                case KeyCode.KeypadPeriod:
                    return "[.]";
                case KeyCode.KeypadDivide:
                    return "[/]";
                case KeyCode.KeypadMinus:
                    return "[-]";
                case KeyCode.KeypadPlus:
                    return "[+]";
                case KeyCode.KeypadEnter:
                    return "Enter";
                case KeyCode.KeypadEquals:
                    return "[=]";
                case KeyCode.UpArrow:
                    return "Up";
                case KeyCode.DownArrow:
                    return "Down";
                case KeyCode.RightArrow:
                    return "Right";
                case KeyCode.LeftArrow:
                    return "Left";
                case KeyCode.Insert:
                    return "Insert";
                case KeyCode.Home:
                    return "Home";
                case KeyCode.End:
                    return "End";
                case KeyCode.PageUp:
                    return "Page Up";
                case KeyCode.PageDown:
                    return "Page Down";
                case KeyCode.None:
                    return string.Empty;
                default:
                    return ((char)(int)keyCode).ToString();
            }
        }

        private static bool ValidateInput(Event evt) {
            return evt.character != 0 ||
                (!evt.alt || evt.keyCode != KeyCode.AltGr && evt.keyCode != KeyCode.LeftAlt && evt.keyCode != KeyCode.RightAlt) &&
                (!evt.control || evt.keyCode != KeyCode.LeftControl && evt.keyCode != KeyCode.RightControl) &&
                (!evt.command || evt.keyCode != KeyCode.LeftCommand && evt.keyCode != KeyCode.RightCommand &&
                evt.keyCode != KeyCode.LeftWindows && evt.keyCode != KeyCode.RightWindows) &&
                (!evt.shift || evt.keyCode != KeyCode.LeftShift && evt.keyCode != KeyCode.RightShift && evt.keyCode != KeyCode.None) &&
                evt.keyCode != KeyCode.Escape;
        }
    }
}
