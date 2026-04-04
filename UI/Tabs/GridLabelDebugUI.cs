using CinematicShaders.Core;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    /// <summary>
    /// Debug UI for tuning grid label positioning parameters.
    /// Set EnableDebugUI = false to disable all debug sliders (for release builds).
    /// </summary>
    public static class GridLabelDebugUI
    {
        // Toggle to enable/disable all debug UI - set to false for release
        public static bool EnableDebugUI = true;
        
        /// <summary>
        /// Draws debug tuning sliders for a specific label.
        /// Call this from KartographerTab.DrawVisualSettings() for each label you want to tune.
        /// </summary>
        public static void DrawDebugSliders(GridLabelSystem labelSystem, string labelId)
        {
            if (!EnableDebugUI) return;
            if (labelSystem == null) return;
            
            var label = labelSystem.GetLabel(labelId);
            if (label == null) return;
            
            GUILayout.Space(10);
            GUILayout.Label($"<b>Debug Tuning: {labelId}</b>", HighLogic.Skin.label);
            
            // Rotation
            GUILayout.Label($"Rotation: {label.RotationDegrees:F1}°");
            float newRot = GUILayout.HorizontalSlider(label.RotationDegrees, -10f, 10f);
            if (!Mathf.Approximately(newRot, label.RotationDegrees))
            {
                label.RotationDegrees = newRot;
                label.PositionDirty = true;
            }
            
            // Left Padding
            GUILayout.Label($"Left Padding: {label.PaddingLeft:F2}");
            float newPadL = GUILayout.HorizontalSlider(label.PaddingLeft, 0f, 0.7f);
            if (!Mathf.Approximately(newPadL, label.PaddingLeft))
            {
                label.PaddingLeft = newPadL;
                label.PositionDirty = true;
            }
            
            // Bottom Padding
            GUILayout.Label($"Bottom Padding: {label.PaddingBottom:F2}");
            float newPadB = GUILayout.HorizontalSlider(label.PaddingBottom, 0f, 0.7f);
            if (!Mathf.Approximately(newPadB, label.PaddingBottom))
            {
                label.PaddingBottom = newPadB;
                label.PositionDirty = true;
            }
            
            // Font Size
            GUILayout.Label($"Font Size: {label.FontSizePixels:F0}");
            float newFont = GUILayout.HorizontalSlider(label.FontSizePixels, 8f, 48f);
            if (!Mathf.Approximately(newFont, label.FontSizePixels))
            {
                label.FontSizePixels = newFont;
                label.ForceTextureUpdate = true;
            }
            
            // Line Spacing
            GUILayout.Label($"Line Spacing: {label.LineSpacing:F1}");
            float newSpacing = GUILayout.HorizontalSlider(label.LineSpacing, 0f, 20f);
            if (!Mathf.Approximately(newSpacing, label.LineSpacing))
            {
                label.LineSpacing = newSpacing;
                label.ForceTextureUpdate = true;
            }
            
            // Reset button
            if (GUILayout.Button($"Reset {labelId} Tuning"))
            {
                label.RotationDegrees = -2f;
                label.PaddingLeft = 0.12f;
                label.PaddingBottom = 0.12f;
                label.FontSizePixels = 18f;
                label.LineSpacing = 6f;
                label.TextureDirty = true;
                label.PositionDirty = true;
            }
        }
    }
}
