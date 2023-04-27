using UnityEngine;

namespace CENTIS.UnityModuledNet.Modules
{
    [System.Serializable]
    public class SyncClientVisualiser : MonoBehaviour
    {
        [SerializeField] private new Renderer renderer;
        [SerializeField] private TMPro.TMP_Text usernameObject;
        [SerializeField] private Material material;

        public void UpdateVisualiser(string sender, string username, Color color)
	    {
            name = $"{username}@{sender}";
            usernameObject.text = username;
            usernameObject.color = color;
            if (material == null)
                material = new(renderer.sharedMaterial);
            material.SetColor("_Color", color);
            renderer.sharedMaterial = material;
        }
    }
}
