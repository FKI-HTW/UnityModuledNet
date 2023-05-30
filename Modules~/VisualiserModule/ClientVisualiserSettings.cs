using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CENTIS.UnityModuledNet.Modules
{
    [InitializeOnLoad()]
    public class ClientVisualiserSettings : ModuleSyncSettings
    {
        public static ClientVisualiserSettings Settings;

        public int ClientVisualiserDelay = 500;
        public ClientVisualiser ClientVisualiser;

        static ClientVisualiserSettings()
        {
            AssemblyReloadEvents.afterAssemblyReload += () =>
                Settings = ModuledNetSettings.GetOrCreateSettings<ClientVisualiserSettings>("ClientVisualiser");
        }

        protected override string SettingsName => "ClientVisualiserSettings";

		protected override void DrawModuleSettings()
		{
            Settings.ClientVisualiserDelay = EditorGUILayout.IntField("ClientVisualiser Delay:", Settings.ClientVisualiserDelay);
        }
    }
}
