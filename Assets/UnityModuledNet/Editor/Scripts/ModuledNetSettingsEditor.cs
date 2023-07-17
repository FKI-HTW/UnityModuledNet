using UnityEngine;
using UnityEditor;
using CENTIS.UnityModuledNet.Managing;

namespace CENTIS.UnityModuledNet
{
    [CustomEditor(typeof(ModuledNetSettings))]
    [CanEditMultipleObjects]
    internal class ModuledNetSettingsEditor : Editor
    {
        private ModuledNetSettings _settings;

        private bool _packetSettingsIsVisible = false;
        private bool _debugSettingsIsVisible = false;

        private void OnEnable()
        {
            _settings = ModuledNetSettings.GetOrCreateSettings();
        }

        private void OnValidate()
        {
            AssetDatabase.SaveAssets();
        }

        // TODO : add descriptions to labels, was too lazy
        public override void OnInspectorGUI()
        {
            // user settings
            _settings.Username = EditorGUILayout.TextField("Username:", _settings.Username);
            _settings.Color = EditorGUILayout.ColorField("Color:", _settings.Color);
            _settings.ReconnectAfterRecompile = EditorGUILayout.Toggle("Reconnect after recompile:", _settings.ReconnectAfterRecompile);

            // packet frequency settings
            _packetSettingsIsVisible = EditorGUILayout.Foldout(_packetSettingsIsVisible, "Packet Settings", EditorStyles.foldoutHeader);
            if (_packetSettingsIsVisible)
            {
                EditorGUI.indentLevel++;
                _settings.ServerConnectionTimeout = EditorGUILayout.IntField("Connection Timeout:", _settings.ServerConnectionTimeout);
                _settings.ServerHeartbeatDelay = EditorGUILayout.IntField("Heartbeat Delay:", _settings.ServerHeartbeatDelay);
                _settings.ServerDiscoveryTimeout = EditorGUILayout.IntField("ServerDiscovery Timeout:", _settings.ServerDiscoveryTimeout);
                _settings.MaxNumberResendReliablePackets = EditorGUILayout.IntField("Number of Resends of Reliable Packets: ", _settings.MaxNumberResendReliablePackets);
                EditorGUI.indentLevel--;
            }

            // debug settings
            _debugSettingsIsVisible = EditorGUILayout.Foldout(_debugSettingsIsVisible, "Debug Settings", EditorStyles.foldoutHeader);
            if (_debugSettingsIsVisible)
            {
                EditorGUI.indentLevel++;
                ModuledNetManager.IsDebug = EditorGUILayout.Toggle(new GUIContent("Is Debug:", "Allows the display of debug messages."), ModuledNetManager.IsDebug);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Local IP:", ModuledNetManager.LocalIP);
                if (GUILayout.Button("Update IP Address"))
                    ModuledNetManager.UpdateIPAddress();
                EditorGUILayout.EndHorizontal();
                _settings.Port = EditorGUILayout.IntField("Port:", _settings.Port);
                _settings.DiscoveryPort = EditorGUILayout.IntField("Server Discovery Port:", _settings.DiscoveryPort);
                _settings.MTU = EditorGUILayout.IntField("MTU:", _settings.MTU);
                _settings.RTT = EditorGUILayout.IntField("RTT:", _settings.RTT);
                if (GUILayout.Button("Reset Server Discovery"))
                    ModuledNetManager.ResetServerDiscovery();
                EditorGUI.indentLevel--;
            }

            EditorUtility.SetDirty(_settings);
        }
    }
}
