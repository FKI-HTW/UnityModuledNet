using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet
{
    [CustomEditor(typeof(SyncSettings))]
    [CanEditMultipleObjects]
    internal class SyncSettingsEditor : Editor
    {
        public static SyncSettingsEditor Instance { get; private set; }

        private SyncSettings _settings;

        private bool _packetSettingsIsVisible = false;
        private bool _debugSettingsIsVisible = false;

        public event Action DrawAdditianalSyncSettings = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
                DestroyImmediate(this);
            else
                Instance = this;
        }

        private void OnEnable()
        {
            _settings = SyncSettings.GetOrCreateSettings();
        }

        // TODO : add descriptions to labels, was too lazy
        public override void OnInspectorGUI()
        {
            // user settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("User", EditorStyles.boldLabel);
            _settings.Username = EditorGUILayout.TextField("Name", _settings.Username);
            _settings.Color = EditorGUILayout.ColorField("Color", _settings.Color);
            EditorGUILayout.EndVertical();

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

            DrawAdditianalSyncSettings?.Invoke();

            EditorUtility.SetDirty(_settings);
        }
    }
}
