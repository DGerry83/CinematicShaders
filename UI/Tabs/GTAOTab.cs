using CinematicShaders.Core;
using CinematicShaders.Shaders.GTAO;
using UnityEngine;

namespace CinematicShaders.UI.Tabs
{
    public class GTAOTab
    {
        public void Draw()
        {
            GUILayout.Label(CinematicShadersUIStrings.GTAO.SectionHeader, HighLogic.Skin.label);
            GUILayout.Space(4);

            bool isDeferred = IsDeferredRenderingActive();
            bool wasEnabled = GUI.enabled;

            if (!isDeferred)
                GUI.enabled = false;

            // Main GTAO Enable Toggle
            GUIStyle toggleStyle = GTAOSettings.EnableGTAO ?
                CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;

            bool newEnableGTAO = GUILayout.Toggle(GTAOSettings.EnableGTAO,
                CinematicShadersUIStrings.GTAO.EnableToggle, toggleStyle);

            if (newEnableGTAO != GTAOSettings.EnableGTAO)
            {
                GTAOSettings.EnableGTAO = newEnableGTAO;
                GTAOManager.OnToggleChanged();
            }

            GUILayout.Space(4);

            // Raw AO Output Toggle (only enabled when GTAO is on)
            if (!GTAOSettings.EnableGTAO)
                GUI.enabled = false;

            GUIStyle rawAOToggleStyle = GTAOSettings.GTAORawAOOutput ?
                CinematicShadersUIResources.Styles.ToggleActive() : HighLogic.Skin.toggle;

            bool newRawAO = GUILayout.Toggle(GTAOSettings.GTAORawAOOutput,
                CinematicShadersUIStrings.GTAO.RawAOToggle, rawAOToggleStyle);

            if (newRawAO != GTAOSettings.GTAORawAOOutput)
            {
                GTAOSettings.GTAORawAOOutput = newRawAO;
            }

            GUI.enabled = wasEnabled;

            GUILayout.Space(8);

            // Help text
            GUIStyle helpStyle = CinematicShadersUIResources.Styles.Help();

            if (!isDeferred)
            {
                GUILayout.Label(CinematicShadersUIStrings.GTAO.DeferredWarning, helpStyle);
            }
            else if (GTAOSettings.EnableGTAO)
            {
                GUILayout.Label(GTAOSettings.GTAORawAOOutput ?
                    CinematicShadersUIStrings.GTAO.RawAOTooltip :
                    CinematicShadersUIStrings.GTAO.EnableTooltip, helpStyle);
            }
        }

        private bool IsDeferredRenderingActive()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
                mainCamera = Object.FindObjectOfType<Camera>();

            if (mainCamera == null)
                return false;

            return mainCamera.actualRenderingPath == RenderingPath.DeferredShading;
        }
    }
}