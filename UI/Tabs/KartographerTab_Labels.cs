using CinematicShaders.Core;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    /// <summary>
    /// UI for grid labels - demonstrates extensible label system.
    /// This would replace the current DrawVisualSettings() label section.
    /// </summary>
    public partial class KartographerTab
    {
        // Label system instance
        private GridLabelSystem _labelSystem;
        
        // UI foldout states
        private bool _showSystemLabels = true;
        private bool _showSOILabels = false;
        private bool _showOrbitLabels = false;
        private bool _showCustomLabels = false;
        
        private void InitializeLabelSystem()
        {
            if (_labelSystem == null)
            {
                _labelSystem = new GridLabelSystem(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            }
        }
        
        /// <summary>
        /// Draws the Labels section in Visual Settings.
        /// Easy to add new label types here!
        /// </summary>
        private void DrawLabelsSection()
        {
            InitializeLabelSystem();
            
            GUILayout.Space(5);
            GUILayout.Label("Grid Labels", HighLogic.Skin.label);
            GUILayout.BeginVertical(HighLogic.Skin.box);
            
            // System Labels (HUCK, etc.)
            DrawLabelCategory("System", ref _showSystemLabels, GridLabelType.System);
            
            // SOI Labels (dynamic based on current flight)
            DrawLabelCategory("SOI Markers", ref _showSOILabels, GridLabelType.SOI);
            
            // Orbit Info Labels
            DrawLabelCategory("Orbit Info", ref _showOrbitLabels, GridLabelType.OrbitInfo);
            
            // Custom/User Labels
            DrawLabelCategory("Custom Markers", ref _showCustomLabels, GridLabelType.Custom);
            
            GUILayout.EndVertical();
        }
        
        private void DrawLabelCategory(string categoryName, ref bool showFoldout, GridLabelType type)
        {
            showFoldout = GUILayout.Toggle(showFoldout, showFoldout ? " ▼ " + categoryName : " ▶ " + categoryName, 
                HighLogic.Skin.button);
            
            if (!showFoldout) return;
            
            GUILayout.BeginVertical();
            
            foreach (var label in _labelSystem.GetLabelsByType(type))
            {
                GUILayout.BeginHorizontal();
                
                // Enable toggle
                bool wasEnabled = label.Enabled;
                bool isEnabled = GUILayout.Toggle(wasEnabled, "", HighLogic.Skin.toggle, GUILayout.Width(20));
                if (isEnabled != wasEnabled)
                {
                    _labelSystem.SetLabelEnabled(label.Id, isEnabled);
                }
                
                // Label text
                GUILayout.Label(label.Text, HighLogic.Skin.label);
                
                // Position info (for debug)
                if (type == GridLabelType.Debug)
                {
                    GUILayout.Label($"({label.Latitude:F1}°, {label.Longitude:F1}°)", 
                        HighLogic.Skin.label, GUILayout.Width(100));
                }
                
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            
            // Empty state
            bool hasLabels = false;
            foreach (var _ in _labelSystem.GetLabelsByType(type)) { hasLabels = true; break; }
            
            if (!hasLabels)
            {
                GUI.enabled = false;
                GUILayout.Label($"  No {categoryName.ToLower()} labels active", HighLogic.Skin.label);
                GUI.enabled = true;
            }
            
            GUILayout.EndVertical();
        }
        
        /// <summary>
        /// Call this from Update() to refresh all labels.
        /// </summary>
        private void UpdateLabels()
        {
            if (_labelSystem != null && StarfieldSettings.EnableKartographer)
            {
                _labelSystem.UpdateAllLabels();
            }
        }
        
        // =================================================================
        // EXAMPLE: Adding SOI labels dynamically
        // Call these from flight scene events
        // =================================================================
        
        /// <summary>
        /// Call when entering a new SOI to add a label for that body.
        /// </summary>
        public void OnEnterSOI(string bodyName, Vector3d bodyPosition)
        {
            InitializeLabelSystem();
            
            // Convert body position to lat/lon on the holographic grid
            Vector3 dir = bodyPosition.normalized;
            float latitude = Mathf.Asin((float)dir.y) * Mathf.Rad2Deg;
            float longitude = Mathf.Atan2((float)dir.z, (float)dir.x) * Mathf.Rad2Deg;
            
            _labelSystem.AddSOILabel(bodyName, latitude, longitude);
            Debug.Log($"[Kartographer] Added SOI label for {bodyName} at ({latitude:F1}°, {longitude:F1}°)");
        }
        
        /// <summary>
        /// Call when leaving an SOI to remove its label.
        /// </summary>
        public void OnExitSOI(string bodyName)
        {
            _labelSystem?.RemoveSOILabel(bodyName);
        }
        
        /// <summary>
        /// Update SOI label positions (if bodies move relative to grid).
        /// </summary>
        public void UpdateSOILabelPosition(string bodyName, Vector3d bodyPosition)
        {
            Vector3 dir = bodyPosition.normalized;
            float latitude = Mathf.Asin((float)dir.y) * Mathf.Rad2Deg;
            float longitude = Mathf.Atan2((float)dir.z, (float)dir.x) * Mathf.Rad2Deg;
            
            _labelSystem?.UpdateLabelPosition($"soi_{bodyName.ToLower()}", latitude, longitude);
        }
    }
}
