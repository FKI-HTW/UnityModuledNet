using System.IO;
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet
{
    [System.Serializable]
    public class SyncSettings : ScriptableObject
    {
        protected const string _settingsFilePath = "Assets/Resources/";
        protected const string _settingsName = "";
        protected const string _settingsNameFSuffix = "SyncSettings";
        protected const string _settingsNameFileType = ".asset";

        private static SyncSettings cachedSettings;

        private static readonly HashSet<ModuleSyncSettings> _moduleSettings = new();
        public HashSet<ModuleSyncSettings> ModuleSettings => _moduleSettings;

        // settings
        // user settings
        public string Username = "Username";
        public Color32 Color = new(255, 255, 255, 255);

        // packet frequency settings
        public int HeartbeatDelay = 1000;
        public int ClientTimeoutDelay = 3000;
        public int ResendReliablePacketsDelay = 250;
        public int MaxNumberResendReliablePackets = 5;

        // debug settings
        public int Port = 26822;

        public static string GetSettingsFileFullPath(string settingsName, string path = _settingsFilePath)
        {
            return _settingsFilePath + settingsName + _settingsNameFSuffix + _settingsNameFileType;
        }

        public static SyncSettings GetOrCreateSettings()
        {
            return cachedSettings ?? (cachedSettings = GetOrCreateSettings<SyncSettings>(_settingsName, _settingsFilePath));
        }

        public static T GetOrCreateSettings<T>(string settingsName, string path = _settingsFilePath) where T : ScriptableObject
        {
            T settings = Resources.Load<T>(Path.GetFileNameWithoutExtension(settingsName + _settingsNameFSuffix));
            string fullPath = GetSettingsFileFullPath(settingsName, path);

#if UNITY_EDITOR
            if (!settings)
            {
                settings = AssetDatabase.LoadAssetAtPath<T>(fullPath);
            }
            if (!settings)
            {
                string[] allSettings = AssetDatabase.FindAssets($"t:{settingsName}{_settingsNameFSuffix}");
                if (allSettings.Length > 0)
                {
                    settings = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(allSettings[0]));
                }
            }
            if (!settings)
            {
                settings = CreateInstance<T>();
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(settings, fullPath);
                AssetDatabase.SaveAssets();
            }
#else
			if (!settings)
			{
				settings = ScriptableObject.CreateInstance<SyncSettings>();
			}
#endif
            if (settings is ModuleSyncSettings moduleSyncSettings)
            {
                _moduleSettings.Add(moduleSyncSettings);
            }

            return settings;
        }
    }

#if UNITY_EDITOR && UNITY_IMGUI
	internal class SyncSettingsProvider : SettingsProvider
	{
		private SyncSettings _settings;
		private Editor _settingsEditor;

		public override void OnGUI(string searchContext)
		{
			if (!_settings)
			{
				_settings = SyncSettings.GetOrCreateSettings();
				_settingsEditor = Editor.CreateEditor(_settings);
			}
			_settingsEditor.OnInspectorGUI();
		}

		[SettingsProvider]
		public static SettingsProvider CreateSyncSettingsProvider()
		{
			SyncSettings.GetOrCreateSettings();
			return new SyncSettingsProvider("Project/UnitySync", SettingsScope.Project);
		}

		public SyncSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) 
			: base(path, scopes, keywords) { }
	}
#endif
}
