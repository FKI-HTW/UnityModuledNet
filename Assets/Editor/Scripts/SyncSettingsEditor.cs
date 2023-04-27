using UnityEngine;
using UnityEditor;

namespace CENTIS.UnityModuledNet
{
	[CustomEditor(typeof(SyncSettings))]
	[CanEditMultipleObjects]
	internal class SyncSettingsEditor : Editor
	{
		private SyncSettings _settings;

		private bool _packetSettingsIsVisible = false;
		private bool _debugSettingsIsVisible = false;

		private void OnEnable()
		{
			_settings = SyncSettings.GetOrCreateSettings();
		}

		// TODO : add descriptions to labels, was too lazy
		public override void OnInspectorGUI()
		{
			// user settings
			_settings.Username = EditorGUILayout.TextField("Username:", _settings.Username);
			_settings.Color = EditorGUILayout.ColorField("Color:", _settings.Color);

			// packet frequency settings
			_packetSettingsIsVisible = EditorGUILayout.Foldout(_packetSettingsIsVisible, "Packet Settings", EditorStyles.foldoutHeader);
			if (_packetSettingsIsVisible)
			{
				EditorGUI.indentLevel++;
				_settings.HeartbeatDelay = EditorGUILayout.IntField("Heartbeat Delay:", _settings.HeartbeatDelay);
				_settings.ClientTimeoutDelay = EditorGUILayout.IntField("Client Timeout Delay:", _settings.ClientTimeoutDelay);
				_settings.ResendReliablePacketsDelay = EditorGUILayout.IntField("Resend Reliable Packets Delay: ", _settings.ResendReliablePacketsDelay);
				_settings.MaxNumberResendReliablePackets = EditorGUILayout.IntField("Number of Resends of Reliable Packets: ", _settings.MaxNumberResendReliablePackets);
				EditorGUI.indentLevel--;
			}

			// debug settings
			_debugSettingsIsVisible = EditorGUILayout.Foldout(_debugSettingsIsVisible, "Debug Settings", EditorStyles.foldoutHeader);
			if (_debugSettingsIsVisible)
			{
				EditorGUI.indentLevel++;
				SyncManager.IsDebug = EditorGUILayout.Toggle(new GUIContent("Is Debug:", "Allows the display of debug messages."), SyncManager.IsDebug);
				SyncManager.IP = EditorGUILayout.TextField("Local IP:", SyncManager.IP);
				_settings.Port = EditorGUILayout.IntField("Port:", _settings.Port);
				EditorGUI.indentLevel--;
			}

			EditorUtility.SetDirty(_settings);
		}
	}
}
