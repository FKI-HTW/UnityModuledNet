using UnityEngine;

namespace CENTIS.UnityModuledNet.Modules
{
    [System.Serializable]
    public class ClientVisualiser : MonoBehaviour
    {
        [SerializeField] private new Renderer renderer;
        [SerializeField] private TMPro.TMP_Text usernameObject;
        [SerializeField] private Material material;

        public void UpdateVisualiser(byte id, string username, Color color)
	    {
            name = $"{id}#{username}";
            usernameObject.text = username;
            usernameObject.color = color;
            if (material == null)
                material = new(renderer.sharedMaterial);
            material.SetColor("_Color", color);
            renderer.sharedMaterial = material;
        }
    }
}
