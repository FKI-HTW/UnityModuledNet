using System;
using System.Collections.Concurrent;
using System.Net;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Networking
{
	public class SyncConnectedClient
	{
		public IPAddress IP { get; private set; }
		public string IPString { get; private set; }
		public string Username { get; set; }
		public Color32 Color { get; set; }
		public DateTime LastHeartbeat { get; set; }

		internal readonly ConcurrentDictionary<ushort, SyncReceiverPacket> ReceivedPacketsBuffer = new();
		internal readonly ConcurrentDictionary<ushort, byte[]> SendPacketsBuffer = new();
		internal readonly ConcurrentDictionary<(ushort, ushort), byte[]> SendSlicesBuffer = new();

		internal ushort UnreliableLocalSequence { get; set; }
		internal ushort UnreliableRemoteSequence { get; set; }
		internal ushort ReliableLocalSequence { get; set; }
		internal ushort ReliableRemoteSequence { get; set; }

		public SyncConnectedClient(IPAddress ip, string username, Color32 color)
		{
			IP = ip;
			IPString = ip.ToString();
			Username = username;
			Color = color;
			LastHeartbeat = DateTime.Now;

			UnreliableLocalSequence = 0;
			UnreliableRemoteSequence = 0;
			ReliableLocalSequence = 0;
			ReliableRemoteSequence = 0;
		}

		public override bool Equals(object obj)
		{
			if ((obj == null) || !GetType().Equals(obj.GetType()))
			{
				return false;
			}
			else
			{
				return IP.Equals(((SyncConnectedClient)obj).IP);
			}
		}

		public override int GetHashCode()
		{
			return IPString.GetHashCode();
		}
	}
}
