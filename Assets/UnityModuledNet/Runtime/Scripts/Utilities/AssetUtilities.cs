using UnityEditor;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Utilities
{
    public static class AssetUtilities
    {
        public static Object FindAssetInProjectFolder<T>(string name, params string[] folders)
        {
            var assetID = AssetDatabase.FindAssets(name, folders);
            string assetPath = string.Empty;
            foreach (var id in assetID)
            {
                var path = AssetDatabase.GUIDToAssetPath(id);
                if (AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(T))
                {
                    assetPath = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(assetPath)) return null;
            return AssetDatabase.LoadAssetAtPath(assetPath, typeof(T)) as Object;
        }
        public static GameObject FindPrefabAssetInProjectFolder(string name, params string[] folders)
        {
            return FindAssetInProjectFolder<GameObject>(name, folders) as GameObject;
        }
        public static T FindPrefabAssetInProjectFolder<T>(string name, params string[] folders) where T : Component
        {
            return FindPrefabAssetInProjectFolder(name, folders)?.GetComponent<T>();
        }

        public static Material FindMaterialAssetInProjectFolder(string name, params string[] folders)
        {
            return FindAssetInProjectFolder<Material>(name, folders) as Material;
        }
    }
}