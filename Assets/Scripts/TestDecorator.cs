using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEditor;
using CENTIS.UnityModuledNet.Managing;
using CENTIS.UnityModuledNet.Modules;

namespace CENTIS.UnityModuledNet
{
	public class TestDecorator : EditorWindow
	{
		public ClientVisualiserModule _visualiser;
		public TestModule _test;

		[MenuItem("Window/ModuledNet/ModuledNet Test")]
		public static void ShowWindow()
		{
			GetWindow(typeof(TestDecorator), false, "ModuledNet Test");
		}

		private void OnEnable()
		{
			_visualiser = new();
			_test = new();
		}

		public void SendTestData()
		{
			_test.SendData();
		}

		private void OnGUI()
		{
			if (GUILayout.Button("Send"))
				SendTestData();
		}
	}
}
