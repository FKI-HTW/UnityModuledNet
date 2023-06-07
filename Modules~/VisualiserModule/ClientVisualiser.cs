using UnityEngine;

namespace CENTIS.UnityModuledNet.Modules
{
    [System.Serializable]
    public class ClientVisualiser : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private TMPro.TMP_Text _usernameObject;
        
        private Material _material;

        public void UpdateVisualiser(byte id, string username, Color color)
	    {
            name = $"{id}#{username}";
            _usernameObject.text = username;
            _usernameObject.color = color;
            if (_material == null)
			{
                _material = Instantiate(ClientVisualiserSettings.Settings.ClientVisualiserMaterial) as Material;
                _renderer.material = _material;
			}
            _material.SetColor("_Color", color);
        }
    }
}
