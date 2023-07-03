using UnityEngine;
using UnityEngine.Assertions;
using CENTIS.UnityModuledNet.Utilities;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet.Modules
{
#if UNITY_EDITOR
    [InitializeOnLoad()]
#endif
    public class ClientVisualiserSettings : ModuleSettings<ClientVisualiserModule>
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

        public int SyncDelay = 100;
        public ClientVisualiser ClientVisualiser = null;
        public Material Material = null;
        public bool Lerp = false;

        static ClientVisualiserSettings()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                Assert.IsNotNull(Settings);
            };
#endif
        }
        private ClientVisualiserSettings()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                Module.FixOrphanedVisualiser();
            };
#endif
        }

        protected override void DrawModuleSettings()
        {
#if UNITY_EDITOR
            CheckForClientVisualizer();

            Settings.SyncDelay = EditorGUILayout.IntField("Delay", Settings.SyncDelay);
            Settings.ClientVisualiser = (ClientVisualiser)EditorGUILayout.ObjectField("Prefab", Settings.ClientVisualiser, typeof(ClientVisualiser), false);
            Settings.Material = (Material)EditorGUILayout.ObjectField("Material", Settings.Material, typeof(Material), false);
            Settings.Lerp = EditorGUILayout.Toggle("Lerp Movement (buggy)", Settings.Lerp);
#endif
        }

        private void CheckForClientVisualizer()
        {
            if (ClientVisualiser == null)
                ClientVisualiser = AssetUtilities.FindPrefabAssetInProjectFolder<ClientVisualiser>("ClientVisualiser");
            if (Material == null)
                Material = AssetUtilities.FindMaterialAssetInProjectFolder("ClientVisualiser");
        }
    }
}
