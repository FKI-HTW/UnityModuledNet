using System;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ConnectionAcceptedPacket : AConnectionNetworkPacket
	{
		public byte ClientID { get; private set; }

		public ConnectionAcceptedPacket(byte clientID)
		{
			Type = EPacketType.ConnectionAccepted;
			ClientID = clientID;
		}

		public ConnectionAcceptedPacket(byte[] packet)
		{
			Type = EPacketType.ChallengeAnswer;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH] = ClientID;

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
			
				return true;
			}
			catch (Exception) { return false; }
		}
	}
}