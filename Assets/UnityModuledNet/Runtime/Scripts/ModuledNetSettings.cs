using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using CENTIS.UnityModuledNet.Managing;
using CENTIS.UnityModuledNet.Modules;
using System.Linq;
using System.Threading.Tasks;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet
{
    [Serializable]
    public class ModuledNetSettings : ScriptableObject
    {
        private static ModuledNetSettings _settings = null;
        public static ModuledNetSettings Settings
        {
            get
            {
                if (_settings == null)
                    _settings = GetOrCreateSettings<ModuledNetSettings>(_settingsName);
                return _settings;
            }
        }

        protected const string _settingsFilePath = "Assets/Resources/";
        protected const string _settingsName = "";
        protected const string _settingsNameFSuffix = "ModuledNetSettings";
        protected const string _settingsNameFileType = ".asset";

        private static readonly Dictionary<Type, IModuleSettings> _moduleSettings = new();
        public HashSet<IModuleSettings> ModuleSettings => _moduleSettings.Values.ToHashSet();

        // settings
        // user settings
        [SerializeField] private string username = "Username";
        [SerializeField] private Color32 color = new(255, 255, 255, 255);
        [SerializeField] private bool reconnectAfterRecompile = false;

        public string Username { get => username; set => username = value; }
        public Color32 Color { get => color; set => color = value; }
        public bool ReconnectAfterRecompile { get => reconnectAfterRecompile; set => reconnectAfterRecompile = value; }

        // server settings
        [SerializeField] private byte _maxNumberClients = 253;
        public byte MaxNumberClients
        {
            get => _maxNumberClients;
            set
            {
                if (value < 254 && value > 1 && _maxNumberClients != value)
                    _maxNumberClients = value;
            }
        }

        // packet frequency settings
        [SerializeField] private int serverConnectionTimeout = 5000;
        [SerializeField] private int serverHeartbeatDelay = 1000;
        [SerializeField] private int serverDiscoveryTimeout = 3000;
        [SerializeField] private int maxNumberResendReliablePackets = 5;

        public int ServerConnectionTimeout { get => serverConnectionTimeout; set => serverConnectionTimeout = value; }
        public int ServerHeartbeatDelay { get => serverHeartbeatDelay; set => serverHeartbeatDelay = value; }
        public int ServerDiscoveryTimeout { get => serverDiscoveryTimeout; set => serverDiscoveryTimeout = value; }
        public int MaxNumberResendReliablePackets { get => maxNumberResendReliablePackets; set => maxNumberResendReliablePackets = value; }

        // debug settings
        [SerializeField] private bool debug = false;
        [SerializeField] private int ipAddressIndex = 0;
        [SerializeField] private bool allowVirtualIPs = false;
        [SerializeField] private int port = 26822;
        [SerializeField] private int discoveryPort = 26823;
        [SerializeField] private int mtu = 1200;
        [SerializeField] private int rtt = 200;

        public bool Debug { get => debug; set => debug = value; }
        public int IPAddressIndex { get => ipAddressIndex; set => ipAddressIndex = value; }
        public bool AllowVirtualIPs
        {
            get => allowVirtualIPs; set
            {
                if (allowVirtualIPs != value)
                    ModuledNetManager.SetIPAddressDirty();
                allowVirtualIPs = value;
            }
        }
        public int Port
        {
            get => port;
            set => port = value;
        }
        public int DiscoveryPort { get => discoveryPort; set => discoveryPort = value; }
        public int MTU
        {
            get => mtu;
            set
            {
                if (mtu == value) return;

                if (ModuledNetManager.IsConnected)
                {
                    UnityEngine.Debug.LogError("The MTU should not be changed while connected to a Server!");
                    return;
                }

                mtu = value;
            }
        }
        public int RTT
        {
            get => rtt;
            set
            {
                if (rtt == value) return;

                if (ModuledNetManager.IsConnected)
                {
                    UnityEngine.Debug.LogError("The RTT should not be changed while connected to a Server!");
                    return;
                }

                rtt = value;
            }
        }

        // packet constants
        internal const uint PROTOCOL_ID = 876237842;
        internal const int PROTOCOL_ID_LENGTH = 4;          // length of the protocol id which is calculated from the package version
        internal const int CRC32_LENGTH = 4;                // checksum and contains protocol and server id
        internal const int PACKET_TYPE_LENGTH = 1;          // id of the packet type
        internal const int SEQUENCE_ID_LENGTH = 2;          // sequence id for preventing old updates to be consumed
        internal const int MODULE_ID_LENGTH = 30;          // flag containing the hash of the used module
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

        public static T GetOrCreateSettings<T>(string settingsName, string path = _settingsFilePath) where T : ScriptableObject
        {
            T settings = Resources.Load<T>(Path.GetFileNameWithoutExtension(settingsName + _settingsNameFSuffix));
            string fullPath = GetSettingsFileFullPath(settingsName, path);

#if UNITY_EDITOR
            if (!settings)
            {
                if (EditorApplication.isCompiling)
                {
                    UnityEngine.Debug.LogError("Can not load settings when editor is compiling!");
                    return null;
                }
                if (EditorApplication.isUpdating)
                {
                    UnityEngine.Debug.LogError("Can not load settings when editor is updating!");
                    return null;
                }

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
				settings = ScriptableObject.CreateInstance<T>();
			}
#endif
            if (settings is IModuleSettings moduleSyncSettings
                && _moduleSettings.ContainsKey(settings.GetType()) is false)
            {
                _moduleSettings.Add(moduleSyncSettings.GetType(), moduleSyncSettings);
            }

            return settings;
        }
    }


#if UNITY_EDITOR && UNITY_IMGUI
    internal class ModuledNetSettingsProvider : SettingsProvider
    {
        private Editor _settingsEditor;

        public override void OnGUI(string searchContext)
        {
            if (!_settingsEditor)
                _settingsEditor = Editor.CreateEditor(ModuledNetSettings.Settings);
            _settingsEditor.OnInspectorGUI();
        }

        [SettingsProvider]
        public static SettingsProvider CreateSyncSettingsProvider()
        {
            return new ModuledNetSettingsProvider("Project/ModuledNet", SettingsScope.Project);
        }

        public ModuledNetSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords) { }
    }
#endif
}
