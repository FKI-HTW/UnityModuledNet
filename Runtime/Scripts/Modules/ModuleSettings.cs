using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet.Modules
{
    /// <summary>
    /// You need to implement the InitializeOnLoad class-attribute
    /// and this constructor in the child class 
    /// to ensure the creation of the settings asset:
    /// static SceneSyncSettings()
    /// {
    ///     AssemblyReloadEvents.afterAssemblyReload += () =>
    ///         _settings = ModuledNetSettings.GetOrCreateSettings<SceneSyncSettings>("Scene");
    /// }
    /// </summary>
    [System.Serializable]
    public abstract class ModuleSettings : ScriptableObject
    {
        private bool _settingsVisibleInGUI = false;

        protected abstract string SettingsName { get; }

        public virtual void DrawModuleSettingsFoldout()
        {
#if UNITY_EDITOR
            _settingsVisibleInGUI = EditorGUILayout.Foldout(_settingsVisibleInGUI, SettingsName, EditorStyles.foldoutHeader);
            if (_settingsVisibleInGUI)
            {
                EditorGUI.indentLevel++;
                DrawModuleSettings();
                EditorGUI.indentLevel--;
            }
#endif
        }

        protected abstract void DrawModuleSettings();
    }
}
