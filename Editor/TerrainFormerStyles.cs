using UnityEngine;
using UnityEditor;

namespace JesseStiller.TerrainFormerExtension {
    internal static class TerrainFormerStyles {
        public static GUIStyle largeBoldLabel;
        public static GUIStyle sceneViewInformationArea;
        public static GUIStyle brushNameAlwaysShowBrushSelection;
        public static GUIStyle gridList;
        public static GUIStyle miniLabelCentered;
        public static GUIStyle miniButtonWithoutMargin;
        public static GUIStyle neighboursCellBox;
        public static GUIStyle behaviourGroupFoldout;

        public static void Initialize() {
            if(largeBoldLabel == null) {
                largeBoldLabel = new GUIStyle(EditorStyles.largeLabel);
                largeBoldLabel.fontSize = 13;
                largeBoldLabel.fontStyle = FontStyle.Bold;
                largeBoldLabel.alignment = TextAnchor.MiddleCenter;
            }
            if(brushNameAlwaysShowBrushSelection == null) {
                brushNameAlwaysShowBrushSelection = new GUIStyle(GUI.skin.label);
                brushNameAlwaysShowBrushSelection.alignment = TextAnchor.MiddleRight;
            }
            if(gridList == null) {
                gridList = GUI.skin.GetStyle("GridList");
            }
            if(miniLabelCentered == null) {
                miniLabelCentered = new GUIStyle(EditorStyles.miniLabel);
                miniLabelCentered.alignment = TextAnchor.MiddleCenter;
                miniLabelCentered.margin = new RectOffset();
                miniLabelCentered.padding = new RectOffset();
                miniLabelCentered.wordWrap = true;
            }
            if(miniButtonWithoutMargin == null) {
                miniButtonWithoutMargin = EditorStyles.miniButton;
                miniButtonWithoutMargin.margin = new RectOffset();
            }
            if(neighboursCellBox == null) {
                neighboursCellBox = new GUIStyle(GUI.skin.box);
                neighboursCellBox.padding = new RectOffset();
                neighboursCellBox.margin = new RectOffset();
                neighboursCellBox.contentOffset = new Vector2();
                neighboursCellBox.alignment = TextAnchor.MiddleCenter;
                neighboursCellBox.fontSize = 10;
            }
            if(behaviourGroupFoldout == null) {
                behaviourGroupFoldout = new GUIStyle(EditorStyles.foldout);
                behaviourGroupFoldout.padding = new RectOffset(2, 2, 1, 2);
                behaviourGroupFoldout.margin = new RectOffset(4, 4, 0, 0);
                behaviourGroupFoldout.fontStyle = FontStyle.Bold;
            }
        }
    }
}