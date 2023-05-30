using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CENTIS.UnityModuledNet.Modules;

namespace CENTIS.UnityModuledNet
{
#if UNITY_EDITOR
    [InitializeOnLoad()]
#endif
	public class TestDecorator : ModuledNetManagerDecorator
    {
        private static TestDecorator _instance;

        private ClientVisualiserModule _clientVisualiser;

        static TestDecorator()
		{
            if (_instance == null)
                _instance = new TestDecorator();
		}

        protected override void Connected()
		{
            _clientVisualiser = new();
		}

		protected override void Disconnected()
		{
            _clientVisualiser.UnregisterModule();
		}
	}
}
