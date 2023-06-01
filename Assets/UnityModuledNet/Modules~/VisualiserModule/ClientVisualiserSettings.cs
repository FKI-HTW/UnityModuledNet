using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet.Modules
{
#if UNITY_EDITOR
    [InitializeOnLoad()]
#endif
    public class ClientVisualiserSettings : ModuleSettings
    {
        public static ClientVisualiserSettings Settings;

        protected override string SettingsName => "ClientVisualiserSettings";

        public int ClientVisualiserDelay = 100;
        public ClientVisualiser ClientVisualiser;

        static ClientVisualiserSettings()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.afterAssemblyReload += () =>
                Settings = ModuledNetSettings.GetOrCreateSettings<ClientVisualiserSettings>("ClientVisualiser");
#endif
        }

		protected override void DrawModuleSettings()
		{
#if UNITY_EDITOR
            Settings.ClientVisualiserDelay = EditorGUILayout.IntField("ClientVisualiser Delay:", Settings.ClientVisualiserDelay);
            Settings.ClientVisualiser = (ClientVisualiser)EditorGUILayout.ObjectField("Client Visualiser Prefab:", Settings.ClientVisualiser, typeof(ClientVisualiser), false);
#endif
        }
    }
}
