using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using CENTIS.UnityModuledNet.Modules;

public class TestModule : ReliableModule
{
	public override string ModuleID => "TestModule";

	public override void OnReceiveData(byte sender, byte[] data)
	{
		Debug.Log($"Data received {Encoding.ASCII.GetString(data)}!");
	}

	public void SendData()
	{
		SendData(Encoding.ASCII.GetBytes("testing"), (success) => Debug.Log($"Data send {success}!"));
	}
}
