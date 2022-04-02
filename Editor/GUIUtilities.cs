using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace JesseStiller.TerrainFormerExtension {
    internal static class GUIUtilities {
        private static readonly int radioButtonsControlHash;
        private static readonly MethodInfo getHelpIconMethodInfo;

        private const int textureSelectionGridPadding = 4;
        private const int textureSelectionGridPaddingHalf = textureSelectionGridPadding / 2;
        
        private static Texture2D lineTexture;
        
        private static class Styles {
            internal static bool initialized = false;
            internal static GUIStyle gridList;
            internal static GUIStyle helpBoxWithoutTheBox;
            internal static GUIStyle labelCenteredVertically;

            internal static void LoadIfNecessary() {
                if(initialized) return;

                gridList = "GridList";
                
                helpBoxWithoutTheBox = new GUIStyle(EditorStyles.helpBox);
                helpBoxWithoutTheBox.normal.background = null;
                helpBoxWithoutTheBox.padding = new RectOffset();

                labelCenteredVertically = new GUIStyle(GUI.skin.label);
                labelCenteredVertically.alignment = TextAnchor.MiddleLeft;

                initialized = true;
            }
        }

        static GUIUtilities() {
            getHelpIconMethodInfo = typeof(EditorGUIUtility).GetMethod("GetHelpIcon", BindingFlags.Static | BindingFlags.NonPublic);
            radioButtonsControlHash = "TerrainFormer.RadioButtons".GetHashCode();
        }

        internal class GUIEnabledBlock : IDisposable {
            private bool enabled;

            public GUIEnabledBlock(bool enabled) {
                this.enabled = enabled;
                if(enabled) return;

                GUI.enabled = false;
            }

            public void Dispose() {
                if(enabled) return;

                GUI.enabled = true;
            }
        }
        
        internal static void LeftFillAndRightControl(Action<Rect> fillControl, Action<Rect> rightControl, GUIContent labelContent = null, int rightControlWidth = 0) {
            Rect baseRect = EditorGUILayout.GetControlRect();

            if(labelContent != null) {
                GUI.Label(baseRect, labelContent);
            }

            Rect fillRect = new Rect(baseRect);
            if(labelContent != null) {
                fillRect.xMin += EditorGUIUtility.labelWidth;
            }
            fillRect.xMax -= rightControlWidth;

            if(fillControl == null) {
                Debug.LogError("A \"Fill Control\" wasn't passed");
                return;
            }
            fillControl(fillRect);

            if(rightControl != null) {
                Rect rightControlRect = new Rect(baseRect);
                rightControlRect.xMin = fillRect.xMax + 4f;
                rightControl(rightControlRect);
            }
        }

        /// <returns>Returns weather the button was pressed or not.</returns>
        internal static bool LeftFillAndRightButton(Action<Rect> fillControl, GUIContent buttonContent, float buttonWidth) {
            Rect baseRect = EditorGUILayout.GetControlRect();

            Rect controlRect = new Rect(baseRect.x, baseRect.y, baseRect.width - buttonWidth - 4f, baseRect.height);
            fillControl(controlRect);

            return GUI.Button(new Rect(controlRect.xMax + 4f, baseRect.y - 2f, buttonWidth, baseRect.height + 4f), buttonContent);
        }

        internal static void ToggleAndControl(GUIContent label, ref bool enableFillControl, Action<Rect> fillControl) {
            Rect controlRect = EditorGUILayout.GetControlRect();

            Rect toggleRect = new Rect(controlRect);
            toggleRect.xMax = EditorGUIUtility.labelWidth;
            toggleRect.yMin -= 1f;
            enableFillControl = EditorGUI.ToggleLeft(toggleRect, label, enableFillControl);

            if(enableFillControl == false) {
                GUI.enabled = false;
            }
            Rect fillRect = new Rect(controlRect);
            fillRect.xMin = EditorGUIUtility.labelWidth + 14f;
            fillControl(fillRect);

            if(enableFillControl == false) {
                GUI.enabled = true;
            }
        }

        /// <summary>
        /// An EditorGUI control with a label, a toggle, min/max slider and min/max float fields.
        /// </summary>
        /// <returns>Returns a bool indicating if the controls' min/max values have changed or not.</returns>
        internal static bool TogglMinMaxWithFloatFields(string label, ref bool toggleValue, ref float minValue, ref float maxValue, float minValueBoundary, 
            float maxValueBoundary, int significantDigits) {
            Rect controlRect = EditorGUILayout.GetControlRect();

            Rect toggleRect = new Rect(controlRect);
            toggleRect.xMax = EditorGUIUtility.labelWidth + 14;
            toggleRect.yMin -= 1f;
            toggleValue = EditorGUI.ToggleLeft(toggleRect, label, toggleValue);

            EditorGUI.BeginChangeCheck();
            Rect fillRect = new Rect(controlRect);
            fillRect.xMin = EditorGUIUtility.labelWidth + 14;
            
            Rect leftRect = new Rect(fillRect);
            leftRect.width = 50f;
            minValue = Utilities.FloorToSignificantDigits(Mathf.Clamp(EditorGUI.FloatField(leftRect, minValue), minValueBoundary, maxValueBoundary), significantDigits);

            Rect middleRect = new Rect(fillRect);
            middleRect.x = leftRect.x + 55;
            middleRect.xMax = fillRect.xMax - 55f;
            EditorGUI.MinMaxSlider(middleRect, ref minValue, ref maxValue, minValueBoundary, maxValueBoundary);

            Rect rightRect = new Rect(fillRect);
            rightRect.xMin = rightRect.xMax - 50;
            maxValue = Utilities.FloorToSignificantDigits(Mathf.Clamp(EditorGUI.FloatField(rightRect, maxValue), minValueBoundary, maxValueBoundary), significantDigits);
            return EditorGUI.EndChangeCheck();
        }

        /// <summary>
        /// An EditorGUI control with a label, a min/max slider and min/max float fields.
        /// </summary>
        /// <returns>Returns a bool indicating if the controls' min/max values have changed or not.</returns>
        internal static bool MinMaxWithFloatFields(string label, ref float minValue, ref float maxValue, float minValueBoundary, float maxValueBoundary, int significantDigits) {
            Rect controlRect = EditorGUILayout.GetControlRect();

            Rect labelRect = new Rect(controlRect);
            labelRect.xMax = EditorGUIUtility.labelWidth + 14;
            labelRect.yMin -= 1f;
            EditorGUI.LabelField(labelRect, label);

            EditorGUI.BeginChangeCheck();
            Rect fillRect = new Rect(controlRect);
            fillRect.xMin = EditorGUIUtility.labelWidth + 14;

            Rect leftRect = new Rect(fillRect);
            leftRect.xMax = leftRect.x + 50f;
            minValue = Utilities.FloorToSignificantDigits(Mathf.Clamp(EditorGUI.FloatField(leftRect, minValue), minValueBoundary, maxValueBoundary), significantDigits);

            Rect middleRect = new Rect(fillRect);
            middleRect.x = leftRect.x + 55;
            middleRect.xMax = fillRect.xMax - 55f;
            EditorGUI.MinMaxSlider(middleRect, ref minValue, ref maxValue, minValueBoundary, maxValueBoundary);

            Rect rightRect = new Rect(fillRect);
            rightRect.xMin = rightRect.xMax - 50;
            maxValue = Utilities.FloorToSignificantDigits(Mathf.Clamp(EditorGUI.FloatField(rightRect, maxValue), minValueBoundary, maxValueBoundary), significantDigits);
            return EditorGUI.EndChangeCheck();
        }

        internal static void ActionableHelpBox(string message, MessageType messageType, Action DrawActions) {
            Styles.LoadIfNecessary();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField(GUIContent.none, new GUIContent(message, (Texture2D)getHelpIconMethodInfo.Invoke(null, new object[] { messageType })), Styles.helpBoxWithoutTheBox, null);

            GUILayout.Space(-10f);

            if(DrawActions != null) DrawActions();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        internal static bool FullClickRegionFoldout(string header, bool folded, GUIStyle style) {
            Rect clickRect = GUILayoutUtility.GetRect(new GUIContent(header), EditorStyles.foldout);
            float defaultLeftMargin = clickRect.xMin;
            clickRect.xMin = 0f;

            Rect labelRect = new Rect(clickRect);
            labelRect.xMin = defaultLeftMargin;

            GUI.Box(labelRect, header, EditorStyles.label);

            Rect toggleRect = new Rect(2f, clickRect.y, EditorGUIUtility.labelWidth - EditorGUI.indentLevel, clickRect.height);
            if(Event.current.type == EventType.Repaint) {
                style.Draw(toggleRect, false, false, folded, false);
            }

            Event currentEvent = Event.current;
            if(currentEvent.type == EventType.MouseDown && clickRect.Contains(currentEvent.mousePosition)) {
                folded = !folded;
                currentEvent.Use();
            }
            return folded;
        }

        internal static string BrushSelectionGrid(string previouslySelected) {
            Debug.Assert(BrushCollection.brushes != null);
            Debug.Assert(BrushCollection.brushes.Count != 0);

            List<Brush> terrainBrushesOfCurrentType = new List<Brush>();
            string selectedBrushTab = TerrainFormerEditor.GetCurrentToolSettings().selectedBrushTab;
            if(selectedBrushTab != "All" && BrushCollection.terrainBrushTypes.ContainsKey(selectedBrushTab)) {
                Type typeToDisplay = BrushCollection.terrainBrushTypes[selectedBrushTab];
                foreach(Brush terrainBrush in BrushCollection.brushes) {
                    if(terrainBrush.GetType() != typeToDisplay) continue;

                    terrainBrushesOfCurrentType.Add(terrainBrush);
                }
            } else {
                terrainBrushesOfCurrentType.AddRange(BrushCollection.brushes);
            }
            
            Styles.LoadIfNecessary();
            int brushesToDisplay = terrainBrushesOfCurrentType.Count;
            int brushPreviewSize = Settings.cached.brushPreviewSize;
            int padding = 4;
            int halfPadding = padding / 2;
            int brushPreviewSizeWithPadding = brushPreviewSize + padding;
            
            int brushesPerRow = Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 20f) / brushPreviewSizeWithPadding);
            int rows = Math.Max(Mathf.CeilToInt((float)brushesToDisplay / brushesPerRow), 1);

            Rect controlRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, rows * brushPreviewSizeWithPadding);
            
            Event currentEvent = Event.current;

            GUI.BeginGroup(controlRect, GUI.skin.box);
            int currentColumn = 0;
            int currentRow = 0;
            
            if(currentEvent.type == EventType.MouseUp) {
                int selectedColumn = Mathf.FloorToInt(currentEvent.mousePosition.x / brushPreviewSizeWithPadding);
                int selectedRow = Mathf.FloorToInt(currentEvent.mousePosition.y / brushPreviewSizeWithPadding);
                int selectedItem = selectedRow * brushesPerRow + selectedColumn;

                TerrainFormerEditor.Instance.isSelectingBrush = false;
                currentEvent.Use();
                
                if(selectedItem < terrainBrushesOfCurrentType.Count) {
                    return terrainBrushesOfCurrentType[selectedItem].id;
                } else {
                    return previouslySelected;
                }
            }

            int numberOfBrushesInSelectedType = 0;
            foreach(Brush terrainBrush in terrainBrushesOfCurrentType) {
                Rect selectionRect = new Rect(currentColumn * brushPreviewSizeWithPadding, currentRow * brushPreviewSizeWithPadding, brushPreviewSizeWithPadding, brushPreviewSizeWithPadding);
                Rect imageRect = new Rect(selectionRect.x + halfPadding, selectionRect.y + halfPadding, brushPreviewSize, brushPreviewSize);
                
                // Draw the texture
                if(currentEvent.type == EventType.Repaint) {
                    // The selected area is bigger
                    Styles.gridList.Draw(selectionRect, GUIContent.none, false, false, previouslySelected == terrainBrush.id, false);
                    Styles.gridList.Draw(imageRect, terrainBrush.previewTexture, false, false, false, false);
                }
                
                if(Settings.cached.brushSelectionDisplayType == BrushSelectionDisplayType.ImageWithTypeIcon) {
                    GUI.DrawTexture(new Rect(imageRect.x + brushPreviewSizeWithPadding - 18f, imageRect.y - halfPadding, 16f, 16f), terrainBrush.GetTypeIcon());
                }
                if(currentColumn++ == brushesPerRow - 1) {
                    currentColumn = 0;
                    currentRow++;
                }
                numberOfBrushesInSelectedType++;
            }

            if(numberOfBrushesInSelectedType == 0) {
                GUI.Label(new Rect(10f, brushPreviewSize * 0.5f - 7f, 400f, 19f), "There are no brushes in this group.");
            }
            
            GUI.EndGroup();
            
            return previouslySelected;
        }
        
        internal static int TextureSelectionGrid(int previouslySelected, Texture2D[] icons) {
            Styles.LoadIfNecessary();

            int brushPreviewSize = Settings.cached.brushPreviewSize;
            int brushPreviewSizeWithPadding = brushPreviewSize + textureSelectionGridPadding;

            int brushesPerRow = Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 20f) / brushPreviewSizeWithPadding);
            int rows = Mathf.CeilToInt((float)icons.Length / brushesPerRow);

            Rect selectionGridRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, Mathf.Max(rows * brushPreviewSizeWithPadding, 30f));
            
            Event currentEvent = Event.current;

            GUIStyle addAndRemovePanel = new GUIStyle();
            addAndRemovePanel.normal.background = AssetDatabase.LoadAssetAtPath<Texture2D>(Settings.cached.mainDirectory + "Textures/SelectionGridAddAndRemovePanel.psd");
            addAndRemovePanel.border = new RectOffset(2, 2, 2, 0);

            GUIStyle preButton = "RL FooterButton";
            GUIContent iconToolbarPlus = EditorGUIUtility.IconContent("Toolbar Plus", "Add texture…");
            GUIContent iconToolbarMinus = EditorGUIUtility.IconContent("Toolbar Minus", "Delete selected texture");

            Rect addAndRemoveFooterRect = new Rect(selectionGridRect);
            addAndRemoveFooterRect.yMin -= 15f;
            addAndRemoveFooterRect.xMin = addAndRemoveFooterRect.xMax - 56f;
            GUI.Box(addAndRemoveFooterRect, GUIContent.none, addAndRemovePanel);

            Rect addButtonRect = new Rect(addAndRemoveFooterRect);
            addButtonRect.width = 28f;
            addButtonRect.height = 16f;
            if(GUI.Button(addButtonRect, iconToolbarPlus, preButton)) {
                PaintTextureEditorWindow.CreateAndShowForAdditions();
            }

            Rect minusButtonRect = new Rect(addButtonRect);
            minusButtonRect.xMin += 28f;
            minusButtonRect.xMax += 28f;
            using(new GUIEnabledBlock(TerrainFormerEditor.splatPrototypes.Length > 0)) {
                if(GUI.Button(minusButtonRect, iconToolbarMinus, preButton)) {
                    TerrainFormerEditor.Instance.RemoveSplatTexture(previouslySelected);
                }
            }

            GUI.BeginGroup(selectionGridRect, GUI.skin.box);
            int currentColumn = 0;
            int currentRow = 0;
            
            if(icons.Length == 0) {
                GUI.Label(new Rect(5f, 0f, selectionGridRect.width, selectionGridRect.height), "No textures have been defined.", Styles.labelCenteredVertically);
            }

            if(currentEvent.type == EventType.MouseUp || (currentEvent.type == EventType.MouseDown && currentEvent.clickCount == 2)) {
                int selectedColumn = Mathf.FloorToInt(currentEvent.mousePosition.x / brushPreviewSizeWithPadding);
                int selectedRow = Mathf.FloorToInt(currentEvent.mousePosition.y / brushPreviewSizeWithPadding);
                int selectedItem = selectedRow * brushesPerRow + selectedColumn;
                
                // Double clicking on an empty area in the rect is a shortcut to add an item.
                if(currentEvent.clickCount == 2 && selectedItem >= icons.Length) {
                    PaintTextureEditorWindow.CreateAndShowForAdditions();
                }

                if(icons.Length == 0) {
                    GUI.EndGroup();
                    return 0;
                }

                if(currentEvent.clickCount == 2 && selectedItem >= 0 && selectedItem < TerrainFormerEditor.splatPrototypes.Length) {
                    PaintTextureEditorWindow.CreateAndShow(selectedItem);
                }

                currentEvent.Use();
                if(selectedItem <= icons.Length) {
                    return Mathf.Clamp(selectedItem, 0, icons.Length - 1);
                } else {
                    return Mathf.Clamp(previouslySelected, 0, icons.Length - 1);
                }
            }
            
            for(int i = 0; i < icons.Length; i++) { 
                Rect selectionBoxRect = new Rect(currentColumn * brushPreviewSizeWithPadding, currentRow * brushPreviewSizeWithPadding, brushPreviewSizeWithPadding, brushPreviewSizeWithPadding);
                Rect imageRect = new Rect(selectionBoxRect.x + textureSelectionGridPaddingHalf, selectionBoxRect.y + textureSelectionGridPaddingHalf, brushPreviewSize, brushPreviewSize);
                
                if(currentEvent.type == EventType.Repaint) {
                    // The selection rect is bigger to show the selected colour 
                    Styles.gridList.Draw(selectionBoxRect, GUIContent.none, false, false, i == previouslySelected, false);
                    EditorGUI.DrawPreviewTexture(imageRect, icons[i], null, ScaleMode.StretchToFill);
                }

                if(currentColumn++ == brushesPerRow - 1) {
                    currentColumn = 0;
                    currentRow++;
                }
            }
            
            GUI.EndGroup();
            
            return Mathf.Clamp(previouslySelected, 0, icons.Length - 1);
        }
        
        // Returns the selected brush tab
        internal static string BrushTypeToolbar(string selectedBrushTab) {
            Rect controlRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 10f, 18f);
            float tabWidth = controlRect.width / BrushCollection.terrainBrushTypes.Keys.Count;
            GUIStyle tabButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
            GUIStyle selectedTabButtonStyle = new GUIStyle(EditorStyles.toolbarButton);
            selectedTabButtonStyle.normal.background = EditorStyles.toolbarButton.onNormal.background;

            GUI.BeginGroup(controlRect);
            int i = 0;
            foreach(string typeName in BrushCollection.terrainBrushTypes.Keys) {
                Rect typeRect = new Rect(i * tabWidth, 0f, tabWidth, 25f);

                if(Event.current.type == EventType.MouseUp && typeRect.Contains(Event.current.mousePosition)) {
                    Event.current.Use();
                    return typeName;
                }

                GUI.Box(typeRect, typeName, selectedBrushTab == typeName ? selectedTabButtonStyle : tabButtonStyle);
                i++;
            }
            GUI.EndGroup();

            return selectedBrushTab;
        }

        /// <summary>
        /// A radio button control that doesn't cutoff certain characters and with intelligent spacing between options.
        /// </summary>
        /// <returns>The index of the selected radio button.</returns>
        internal static int RadioButtonsControl(GUIContent labelContent, int selectedIndex, GUIContent[] radioButtonOptions) {
            Rect controlRect = EditorGUILayout.GetControlRect();
            Rect toolbarRect = EditorGUI.PrefixLabel(controlRect, labelContent);

            if(radioButtonOptions.Length == 0) return selectedIndex;

            int toolbarOptionsCount = radioButtonOptions.Length;
            float[] widths = new float[toolbarOptionsCount];
            bool useEqualSpacing = true;
            float totalContentsWidths = 0f;
            float maxWidthPerContent = toolbarRect.width / toolbarOptionsCount;

            // Calculate widths of the options and check if the options can be displayed with equal width without anything being cutoff.
            for(int i = 0; i < toolbarOptionsCount; i++) {
                widths[i] = EditorStyles.radioButton.CalcSize(radioButtonOptions[i]).x;
                totalContentsWidths += widths[i];
                
                // If the width of the GUIContent extends the max width per content while mantaining equal spacing then equal spacing cannot be maintained.
                if(useEqualSpacing && widths[i] > maxWidthPerContent) {
                    useEqualSpacing = false;
                }
            }

            float gapPerOption = (toolbarRect.width - totalContentsWidths) / (toolbarOptionsCount - 1);

            // Find the selected option
            float optionOffset = toolbarRect.x;
            int newSelectedIndex = -1;
            for(int i = 0; i < toolbarOptionsCount; i++) {
                float width = useEqualSpacing ? maxWidthPerContent : gapPerOption + widths[i];
                if(Event.current.mousePosition.x >= optionOffset && Event.current.mousePosition.x <= optionOffset + width) {
                    newSelectedIndex = i;
                }
                optionOffset += width;
            }
            
            int controlId = GUIUtility.GetControlID(radioButtonsControlHash, FocusType.Passive, controlRect);
            switch(Event.current.GetTypeForControl(controlId)) {
                case EventType.MouseDown:
                    if(toolbarRect.Contains(Event.current.mousePosition) == false) break;
                    GUIUtility.hotControl = controlId;
                    Event.current.Use();
                    break;
                case EventType.MouseUp:
                    if(GUIUtility.hotControl != controlId) break;
                    GUIUtility.hotControl = 0;
                    Event.current.Use();
                    GUI.changed = true;

                    if(newSelectedIndex == -1) return 0;

                    return newSelectedIndex;
                case EventType.MouseDrag:
                    if(GUIUtility.hotControl == controlId) Event.current.Use();
                    break;
                case EventType.Repaint:
                    float xOffset = toolbarRect.x;
                    for(int i = 0; i < toolbarOptionsCount; i++) {
                        EditorStyles.radioButton.Draw(
                            position: new Rect(xOffset, toolbarRect.y - 1, widths[i], toolbarRect.height),
                            content: radioButtonOptions[i],
                            isHover: i == newSelectedIndex && (GUI.enabled || controlId == GUIUtility.hotControl) && (controlId == GUIUtility.hotControl || GUIUtility.hotControl == 0),
                            isActive: GUIUtility.hotControl == controlId && GUI.enabled,
                            on: i == selectedIndex,
                            hasKeyboardFocus: false);

                        if(useEqualSpacing) {
                            xOffset += maxWidthPerContent;
                        } else {
                            xOffset += gapPerOption + widths[i];
                        }
                    }
                    break;
            }

            return selectedIndex;
        }
    }
}