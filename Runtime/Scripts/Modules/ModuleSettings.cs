using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CENTIS.UnityModuledNet.Modules
{
    public interface IModuleSettings
    {
        void DrawModuleSettingsFoldout();
    }

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
    public abstract class ModuleSettings<T> : ScriptableObject, IModuleSettings where T : ModuledNetModule, new()
    {
        private bool _settingsVisibleInGUI = false;

        [SerializeField] private bool _isModuleActive = true;

        private T _module;

        protected bool IsModuleActive
        {
            get => _isModuleActive;
            set
            {
                _isModuleActive = value;
                if (_isModuleActive) InstatiateModule();
                else _module = null;
            }
        }

        protected T Module => _module;

        protected abstract string SettingsName { get; }

        protected ModuleSettings()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.afterAssemblyReload += InstatiateModule;
#endif
        }

        private void OnEnable()
        {
            InstatiateModule();
        }

        ~ModuleSettings()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.afterAssemblyReload -= InstatiateModule;
#endif
        }

        private void InstatiateModule()
        {
            if (_module == null && IsModuleActive) _module = new T();
        }

        public virtual void DrawModuleSettingsFoldout()
        {
#if UNITY_EDITOR
            _settingsVisibleInGUI = EditorGUILayout.Foldout(_settingsVisibleInGUI, SettingsName, EditorStyles.foldoutHeader);
            if (_settingsVisibleInGUI)
            {
                EditorGUI.indentLevel++;

                GUILayout.BeginHorizontal();
                IsModuleActive = EditorGUILayout.Toggle("Active", IsModuleActive);

                GUI.enabled = false;
                var _ = EditorGUILayout.ObjectField(this, this.GetType(), false);
                GUI.enabled = true;

                GUILayout.EndHorizontal();

                DrawModuleSettings();

                EditorGUI.indentLevel--;

                EditorUtility.SetDirty(this);
            }
#endif
        }

        protected abstract void DrawModuleSettings();
    }
}
