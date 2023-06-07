using System;
using System.Text;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ClientInfoPacket : ASequencedNetworkPacket
	{
		public byte ClientID { get; private set; }
		public string Username { get; private set; }
		public Color32 Color { get; private set; }

		public ClientInfoPacket(byte clientID, string username, Color32 color)
		{
			Type = EPacketType.ClientInfo;
			ClientID = clientID;
			Username = username;
			Color = color;
		}

		public ClientInfoPacket(byte[] packet)
		{
			Type = EPacketType.ClientInfo;
			Bytes = packet;
		}

		public override byte[] Serialize(ushort sequence)
		{
			Sequence = sequence;
			byte[] username = Encoding.ASCII.GetBytes(Username);
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH
				+ ModuledNetSettings.DATA_FLAG_LENGTH + username.Length + 3];

			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			Array.Copy(BitConverter.GetBytes(Sequence), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH);
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH] = ClientID;
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH] = (byte)username.Length;
			Array.Copy(username, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, username.Length);
			int position = ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + username.Length;
			bytes[position + 0] = Color.r;
			bytes[position + 1] = Color.g;
			bytes[position + 2] = Color.b;

			CRC32 = CalculateChecksumBytes(bytes);
			Array.Copy(BitConverter.GetBytes(CRC32), 0, bytes, 0, ModuledNetSettings.CRC32_LENGTH);

			return bytes;
		}

		public override bool TryDeserialize()
		{
			try
			{
				CRC32 = BitConverter.ToUInt32(GetBytesFromArray(Bytes, 0, ModuledNetSettings.CRC32_LENGTH));
				if (!CheckCRC32Checksum(Bytes))
					return false;

				Sequence = BitConverter.ToUInt16(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.SEQUENCE_ID_LENGTH));
				ClientID = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH];
				int usernameLength = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH];
				byte[] username = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, usernameLength);
				Username = Encoding.ASCII.GetString(username);
				int position = ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.SEQUENCE_ID_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + username.Length;
				Color = new (Bytes[position], Bytes[position + 1], Bytes[position + 2], 255);

				return true;
			}
			catch (Exception) { return false; }
		}
	}
}
