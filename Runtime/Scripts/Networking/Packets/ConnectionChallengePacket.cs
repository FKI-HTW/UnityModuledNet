using System;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ConnectionChallengePacket : AConnectionNetworkPacket
	{
		public ulong Challenge { get; private set; }

		public ConnectionChallengePacket()
		{
			Type = EPacketType.ConnectionChallenge;
			Random rnd = new();
			Challenge = (ulong)(rnd.NextDouble() * ulong.MaxValue);
		}

		public ConnectionChallengePacket(byte[] packet)
		{
			Type = EPacketType.ConnectionChallenge;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_LENGTH];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;

			Array.Copy(BitConverter.GetBytes(Challenge), 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.CHALLENGE_LENGTH);

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

				Challenge = BitConverter.ToUInt64(GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.CHALLENGE_LENGTH));

				return true;
			}
			catch (Exception) { return false; }
		}
	}
}