using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
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
			if (cachedSettings)
				return cachedSettings;

			SyncSettings settings = Resources.Load<SyncSettings>(Path.GetFileNameWithoutExtension(_fileName));

#if UNITY_EDITOR
			if (!settings)
			{
				settings = AssetDatabase.LoadAssetAtPath<SyncSettings>(_settingsPath);
			}
			if (!settings)
			{
				string[] allSettings = AssetDatabase.FindAssets("t:SyncSettings");
				if (allSettings.Length > 0)
				{
					settings = AssetDatabase.LoadAssetAtPath<SyncSettings>(AssetDatabase.GUIDToAssetPath(allSettings[0]));
				}
			}
			if (!settings)
			{
				settings = CreateInstance<SyncSettings>();
				string dir = Path.GetDirectoryName(_settingsPath);
				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);
				AssetDatabase.CreateAsset(settings, _settingsPath);
				AssetDatabase.SaveAssets();
			}
#else
			if (!settings)
			{
				settings = ScriptableObject.CreateInstance<SyncSettings>();
			}
#endif
			cachedSettings = settings;
			return cachedSettings;
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
