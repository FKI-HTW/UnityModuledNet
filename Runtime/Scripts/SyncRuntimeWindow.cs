using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CENTIS.UnityModuledNet
{
	public class SyncRuntimeWindow : MonoBehaviour
	{
		[SerializeField] private TMPro.TMP_InputField _port;
		[SerializeField] private TMPro.TMP_InputField _exportSceneName;
		[SerializeField] private TMPro.TMP_InputField _importSceneName;
		[SerializeField] private Transform _viewPort;

		[SerializeField] private GameObject _debugGameObject;

		private void Awake()
		{
		}

		private void OnDestroy()
		{
		}

		public void ConnectClient()
		{
		}

		public void CloseClient()
		{
		}

		private void AddDebug(string text)
		{
			GameObject go = Instantiate(_debugGameObject, _viewPort);
			TMPro.TMP_Text textGo = go.GetComponent<TMPro.TMP_Text>();
			textGo.text = text;
		}
	}
}
