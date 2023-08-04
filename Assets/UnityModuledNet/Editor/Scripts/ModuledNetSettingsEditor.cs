using UnityEngine;
using UnityEditor;
using CENTIS.UnityModuledNet.Managing;

namespace CENTIS.UnityModuledNet
{
    [CustomEditor(typeof(ModuledNetSettings))]
    [CanEditMultipleObjects]
    internal class ModuledNetSettingsEditor : Editor
    {
        private bool _packetSettingsIsVisible = false;
        private bool _serverDiscoverySettingsIsVisible = false;
        private bool _debugSettingsIsVisible = false;

        private string[] cachedIpAddresses = ModuledNetManager.GetLocalIPAddresses().ToArray();

        // TODO : add descriptions to labels, was too lazy
        public override void OnInspectorGUI()
        {
            var settings = ModuledNetSettings.Settings;

            // user settings
            settings.Username = EditorGUILayout.TextField(new GUIContent("Username", "The username of the client."), settings.Username);
            settings.Color = EditorGUILayout.ColorField(new GUIContent("Color", "The color of the client."), settings.Color);
            settings.ReconnectAfterRecompile = EditorGUILayout.Toggle(
                new GUIContent("Reconnect after recompile", " Should the client reconnect after recompile?"), settings.ReconnectAfterRecompile);

            // packet frequency settings
            _packetSettingsIsVisible = EditorGUILayout.Foldout(_packetSettingsIsVisible, "Packet Settings", EditorStyles.foldoutHeader);
            if (_packetSettingsIsVisible)
            {
                EditorGUI.indentLevel++;
                settings.ServerConnectionTimeout = EditorGUILayout.IntField("Connection Timeout:", settings.ServerConnectionTimeout);
                settings.ServerHeartbeatDelay = EditorGUILayout.IntField("Heartbeat Delay:", settings.ServerHeartbeatDelay);
                settings.ServerDiscoveryTimeout = EditorGUILayout.IntField("ServerDiscovery Timeout:", settings.ServerDiscoveryTimeout);
                settings.MaxNumberResendReliablePackets = EditorGUILayout.IntField("Number of Resends of Reliable Packets: ", settings.MaxNumberResendReliablePackets);
                EditorGUI.indentLevel--;
            }

            _serverDiscoverySettingsIsVisible = EditorGUILayout.Foldout(_serverDiscoverySettingsIsVisible, "Server Discovery Settings", EditorStyles.foldoutHeader);
            if (_serverDiscoverySettingsIsVisible)
			{
                EditorGUI.indentLevel++;
                settings.MulticastAddress = EditorGUILayout.TextField(new GUIContent("Multicast Address:", "The Multicast Address used for the Server Discovery"), settings.MulticastAddress);
                settings.DiscoveryPort = EditorGUILayout.IntField("Server Discovery Port:", settings.DiscoveryPort);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                if (GUILayout.Button("Start Server Discovery"))
                    ModuledNetManager.StartServerDiscovery();
                if (GUILayout.Button("End Server Discovery"))
                    ModuledNetManager.EndServerDiscovery();
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }

            // debug settings
            _debugSettingsIsVisible = EditorGUILayout.Foldout(_debugSettingsIsVisible, "Debug Settings", EditorStyles.foldoutHeader);
            if (_debugSettingsIsVisible)
            {
                EditorGUI.indentLevel++;
                settings.Debug = EditorGUILayout.Toggle(new GUIContent("Debug Mode:", "Allows the display of debug messages."), settings.Debug);
                settings.AllowLocalConnection = EditorGUILayout.Toggle(new GUIContent("Allow Local Connection:", "Allows connection from the local IP."), settings.AllowLocalConnection);
                var cachedAllowVirtualIPs = settings.AllowVirtualIPs;
                settings.AllowVirtualIPs = EditorGUILayout.Toggle("Allow Virtual IPs:", settings.AllowVirtualIPs);

                EditorGUILayout.BeginHorizontal();
                settings.IPAddressIndex = EditorGUILayout.Popup("Local IP Address:", settings.IPAddressIndex, cachedIpAddresses);
                if (GUILayout.Button("Update"))
                    cachedIpAddresses = ModuledNetManager.GetLocalIPAddresses(!settings.AllowVirtualIPs).ToArray();
                if (cachedAllowVirtualIPs != settings.AllowVirtualIPs)
                    cachedIpAddresses = ModuledNetManager.GetLocalIPAddresses(!settings.AllowVirtualIPs).ToArray();
                settings.IPAddressIndex = Mathf.Clamp(settings.IPAddressIndex, 0, cachedIpAddresses.Length - 1);
                EditorGUILayout.EndHorizontal();

                settings.MTU = EditorGUILayout.IntField("MTU:", settings.MTU);
                settings.RTT = EditorGUILayout.IntField("RTT:", settings.RTT);
                EditorGUI.indentLevel--;
            }

            EditorUtility.SetDirty(settings);
        }
    }
}
