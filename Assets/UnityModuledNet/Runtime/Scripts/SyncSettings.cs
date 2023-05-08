using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet
{
    [System.Serializable]
    public class SyncSettings : ScriptableObject
    {
		private const string _fileName = "UnitySyncSettings.asset";
		private const string _settingsPath = "Assets/Resources/" + _fileName;

		internal static SyncSettings cachedSettings;
        public static SyncSettings GetOrCreateSettings()
        {
            return cachedSettings ?? (cachedSettings = GetOrCreateSettings<SyncSettings>(_fileName, _settingsPath));
        }

        public static T GetOrCreateSettings<T>(string fileName, string path = _settingsPath) where T : ScriptableObject

        {
            T settings = Resources.Load<T>(Path.GetFileNameWithoutExtension(fileName));

#if UNITY_EDITOR
            if (!settings)
            {
                settings = AssetDatabase.LoadAssetAtPath<T>(path);
            }
            if (!settings)
            {
                string[] allSettings = AssetDatabase.FindAssets("t:SyncSettings");
                if (allSettings.Length > 0)
                {
                    settings = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(allSettings[0]));
                }
            }
            if (!settings)
            {
                settings = CreateInstance<T>();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(settings, path);
                AssetDatabase.SaveAssets();
            }
#else
			if (!settings)
			{
				settings = ScriptableObject.CreateInstance<SyncSettings>();
			}
#endif
            return settings;
        }

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
