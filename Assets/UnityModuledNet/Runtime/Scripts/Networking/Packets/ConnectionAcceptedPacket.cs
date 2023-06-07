using System;
using System.Text;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ConnectionAcceptedPacket : AConnectionNetworkPacket
	{
		public byte ClientID { get; private set; }
		public string Servername { get; private set; }
		public byte MaxNumberConnectedClients { get; private set; }

		public ConnectionAcceptedPacket(byte clientID, string servername, byte maxNumberConnectedClients)
		{
			Type = EPacketType.ConnectionAccepted;
			ClientID = clientID;
			Servername = servername;
			MaxNumberConnectedClients = maxNumberConnectedClients;
		}

		public ConnectionAcceptedPacket(byte[] packet)
		{
			Type = EPacketType.ConnectionAccepted;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] servername = Encoding.ASCII.GetBytes(Servername);

			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servername.Length + ModuledNetSettings.NUMBER_CLIENTS_LENGTH];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH] = ClientID;
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH] = (byte)servername.Length;
			Array.Copy(servername, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, servername.Length);
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servername.Length] = MaxNumberConnectedClients;

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

				ClientID = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH];
				int servernameLength = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH];
				byte[] servernameBytes = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, servernameLength);
				Servername = Encoding.ASCII.GetString(servernameBytes);
				MaxNumberConnectedClients = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + servernameLength];
			
				return true;
			}
			catch (Exception) { return false; }
		}
	}
}