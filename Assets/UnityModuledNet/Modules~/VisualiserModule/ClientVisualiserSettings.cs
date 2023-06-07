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
        private static ClientVisualiserSettings _settings;

        public static ClientVisualiserSettings Settings
		{
			get
			{
                if (_settings == null)
                    _settings = ModuledNetSettings.GetOrCreateSettings<ClientVisualiserSettings>("ClientVisualiser");
                return _settings;
			}
		}

        protected override string SettingsName => "ClientVisualiserSettings";

        public int ClientVisualiserDelay = 100;
        public ClientVisualiser ClientVisualiser;
        public Material ClientVisualiserMaterial;

        static ClientVisualiserSettings()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.afterAssemblyReload += () =>
                _settings = ModuledNetSettings.GetOrCreateSettings<ClientVisualiserSettings>("ClientVisualiser");
#endif
        }

		protected override void DrawModuleSettings()
		{
#if UNITY_EDITOR
            Settings.ClientVisualiserDelay = EditorGUILayout.IntField("ClientVisualiser Delay:", Settings.ClientVisualiserDelay);
            Settings.ClientVisualiser = (ClientVisualiser)EditorGUILayout.ObjectField("ClientVisualiser Prefab:", Settings.ClientVisualiser, typeof(ClientVisualiser), false);
            Settings.ClientVisualiserMaterial = (Material)EditorGUILayout.ObjectField("ClientVisualiser Material:", Settings.ClientVisualiserMaterial, typeof(Material), false);
#endif
        }
    }
}
