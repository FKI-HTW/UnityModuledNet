using System;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ConnectionDeniedPacket : AConnectionNetworkPacket
	{
		public ConnectionDeniedPacket()
		{
			Type = EPacketType.ConnectionDenied;
		}

		public ConnectionDeniedPacket(byte[] packet)
		{
			Type = EPacketType.ConnectionDenied;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CLIENT_ID_LENGTH];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;

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

				return true;
			}
			catch (Exception) { return false; }
		}
	}
}