using System;
using System.Collections.Concurrent;
using System.Net;
using UnityEngine;
using CENTIS.UnityModuledNet.Networking.Packets;

namespace CENTIS.UnityModuledNet.Networking
{
	public class ClientInformation
	{
		public readonly byte ID;

		public string Username = "Username";
		public Color32 Color = new(255, 255, 255, 255);
		public bool IsHost => ID == 1;

		public ClientInformation(byte id)
		{
			ID = id;
		}

		public ClientInformation(byte id, string username, Color32 color) : this(id)
		{
			Username = username;
			Color = color;
		}

		public override string ToString()
		{
			return $"{ID}#{Username}";
		}

		public override bool Equals(object obj)
		{
			if ((obj == null) || !GetType().Equals(obj.GetType()))
			{
				return false;
			}
			else
			{
				return ID.Equals(((ClientInformation)obj).ID);
			}
		}

		public override int GetHashCode()
		{
			return ID;
		}
	}

	internal class ClientInformationSocket : ClientInformation
	{
		public IPAddress IP { get; private set; }
		public DateTime LastHeartbeat { get; set; }

		internal readonly ConcurrentDictionary<ushort, ASequencedNetworkPacket> ReceivedPacketsBuffer = new();
		internal readonly ConcurrentDictionary<ushort, ConcurrentDictionary<ushort, DataPacket>> ReceivedChunksBuffer = new();

		internal readonly ConcurrentDictionary<ushort, byte[]> SendPacketsBuffer = new();
		internal readonly ConcurrentDictionary<(ushort, ushort), byte[]> SendChunksBuffer = new();

		internal ushort UnreliableLocalSequence { get; set; }
		internal ushort UnreliableRemoteSequence { get; set; }
		internal ushort ReliableLocalSequence { get; set; }
		internal ushort ReliableRemoteSequence { get; set; }
		
		public ClientInformationSocket(byte id, IPAddress ip) : base(id) 
		{
			IP = ip;
			LastHeartbeat = DateTime.Now;

			UnreliableLocalSequence = 0;
			UnreliableRemoteSequence = 0;
			ReliableLocalSequence = 0;
			ReliableRemoteSequence = 0;
		}

		public ClientInformationSocket(byte id, IPAddress ip, string username, Color32 color) : base(id, username, color)
		{
			IP = ip;
			LastHeartbeat = DateTime.Now;

			UnreliableLocalSequence = 0;
			UnreliableRemoteSequence = 0;
			ReliableLocalSequence = 0;
			ReliableRemoteSequence = 0;
		}

	}
}
