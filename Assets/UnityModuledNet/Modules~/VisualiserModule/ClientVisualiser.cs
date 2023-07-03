using System.Collections;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Modules
{
    [System.Serializable, ExecuteInEditMode]
    public class ClientVisualiser : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private TMPro.TMP_Text _usernameObject;

        private Material _material;

        private Vector3 _aimedPosition, _aimedRotation;
        private Coroutine _transformLerpCoroutine = null;

        public void UpdateVisualiser(byte id, string username, Color color)
        {
            name = $"{id}#{username}";
            _usernameObject.text = username;
            _usernameObject.color = color;
            if (_material == null)
            {
                _material = Instantiate(ClientVisualiserSettings.Settings.Material);
                _renderer.material = _material;
            }
            _material.SetColor("_Color", color);
        }

        public void SetTransform(Vector3 position, Vector3 rotation, bool lerp = false)
        {
            if (lerp)
            {
                _aimedPosition = position;
                _aimedRotation = rotation;
                if (_transformLerpCoroutine == null)
                    _transformLerpCoroutine = StartCoroutine(LerpTransform());
            }
            else
            {
                transform.position = position;
                transform.eulerAngles = rotation;
            }
        }
        private IEnumerator LerpTransform()
        {
            bool move = true, rotate = false; // ToDo: Rotation does not work like expected yet
            while (move || rotate)
            {
                if (move)
                {
                    transform.position = Vector3.Lerp(transform.position, _aimedPosition, Time.deltaTime * 3); // ToDo: Values could be improved

                    if (Vector3.Distance(transform.position, _aimedPosition) < 0.1)
                    {
                        transform.position = _aimedPosition;
                        move = false;
                    }
                }

                transform.eulerAngles = _aimedRotation; // ToDo: Remove when fixed
                if (rotate)
                {
                    transform.eulerAngles = Vector3.Lerp(transform.eulerAngles, _aimedRotation, Time.deltaTime);

                    if (Vector3.Distance(transform.eulerAngles, _aimedRotation) < 0.1)
                    {
                        transform.eulerAngles = _aimedRotation;
                        rotate = false;
                    }
                }

                yield return null;
            }

            _transformLerpCoroutine = null;
        }
    }
}
