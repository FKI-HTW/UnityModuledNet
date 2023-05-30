using System.IO;
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet
{
    [System.Serializable]
    public class ModuledNetSettings : ScriptableObject
    {
        protected const string _settingsFilePath = "Assets/Resources/";
        protected const string _settingsName = "";
        protected const string _settingsNameFSuffix = "ModuledNetSettings";
        protected const string _settingsNameFileType = ".asset";

        private static ModuledNetSettings cachedSettings;

        private static readonly HashSet<ModuleSyncSettings> _moduleSettings = new();
        public HashSet<ModuleSyncSettings> ModuleSettings => _moduleSettings;

        // settings
        // user settings
        public string Username = "Username";
        public Color32 Color = new(255, 255, 255, 255);

        // server settings
        private byte _maxNumberClients = 253;
        public byte MaxNumberClients
        {
            get => _maxNumberClients;
            set
            {
                if (value < 253 && value > 1)
                    _maxNumberClients = value;
            }
        }

        // packet frequency settings
        public int ServerConnectionTimeout = 5000;
        public int ServerHeartbeatDelay = 1000;
        public int ServerDiscoveryTimeout = 3000;
        public int MaxNumberResendReliablePackets = 5;

        // debug settings
        public int Port = 26822;
        public int DiscoveryPort = 26823;
        private int _mtu = 1200;
        public int MTU
        {
            get => _mtu;
            set
            {
                if (ModuledNetManager.IsConnected)
                {
                    Debug.LogWarning("The MTU should not be changed while connected to a Server!");
                    return;
                }

                _mtu = value;
            }
        }
        // TODO : calculate rtt
        private int _rtt = 200;
        public int RTT
        {
            get => _rtt;
            set
            {
                if (ModuledNetManager.IsConnected)
                {
                    Debug.LogWarning("The RTT should not be changed while connected to a Server!");
                    return;
                }

                _rtt = value;
            }
        }

        // packet constants
        internal const uint PROTOCOL_ID = 876237842;
        internal const int PROTOCOL_ID_LENGTH = 4;          // length of the protocol id which is calculated from the package version
        internal const int CRC32_LENGTH = 4;                // checksum and contains protocol and server id
        internal const int PACKET_TYPE_LENGTH = 1;          // id of the packet type
        internal const int SEQUENCE_ID_LENGTH = 2;          // sequence id for preventing old updates to be consumed
        internal const int MODULE_HASH_LENGTH = 4;          // flag containing the hash of the used module
        internal const int NUMBER_OF_SLICES = 2;            // flag showing the number of packets the chunk consists of
        internal const int SLICE_NUMBER = NUMBER_OF_SLICES; // number of the current slice
        internal const int NUMBER_CLIENTS_LENGTH = 1;       // (max-) number of clients
        internal const int DATA_FLAG_LENGTH = 1;            // byte flag containing the length of the following byte data
        internal const int CHALLENGE_LENGTH = 8;            // length of the challenge values send to the connecting client
        internal const int CHALLENGE_ANSWER_LENGTH = 32;    // length of the challenge values send to the connecting client
        internal const int CLIENT_ID_LENGTH = 1;			// length of the hash used to identify the connected client

        public static string GetSettingsFileFullPath(string settingsName, string path = _settingsFilePath)
        {
            return _settingsFilePath + settingsName + _settingsNameFSuffix + _settingsNameFileType;
        }

        public static ModuledNetSettings GetOrCreateSettings()
        {
            return cachedSettings ?? (cachedSettings = GetOrCreateSettings<ModuledNetSettings>(_settingsName, _settingsFilePath));
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
				settings = ScriptableObject.CreateInstance<ModuledNetSettings>();
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
	internal class ModuledNetSettingsProvider : SettingsProvider
	{
		private ModuledNetSettings _settings;
		private Editor _settingsEditor;

		public override void OnGUI(string searchContext)
		{
			if (!_settings)
			{
				_settings = ModuledNetSettings.GetOrCreateSettings();
				_settingsEditor = Editor.CreateEditor(_settings);
			}
			_settingsEditor.OnInspectorGUI();
		}

		[SettingsProvider]
		public static SettingsProvider CreateSyncSettingsProvider()
		{
			ModuledNetSettings.GetOrCreateSettings();
			return new ModuledNetSettingsProvider("Project/UnitySync", SettingsScope.Project);
		}

		public ModuledNetSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null) 
			: base(path, scopes, keywords) { }
	}
#endif
}