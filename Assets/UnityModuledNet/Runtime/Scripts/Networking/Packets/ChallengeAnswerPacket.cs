using System;
using System.Security.Cryptography;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ChallengeAnswerPacket : AConnectionNetworkPacket
	{
		public byte[] ChallengeAnswer { get; private set; }

		public ChallengeAnswerPacket(ulong challenge)
		{
			Type = EPacketType.ChallengeAnswer;
			using SHA256 h = SHA256.Create();
			ChallengeAnswer = h.ComputeHash(BitConverter.GetBytes(challenge));
		}

		public ChallengeAnswerPacket(byte[] packet)
		{
			Type = EPacketType.ChallengeAnswer;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			Array.Copy(ChallengeAnswer, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.CHALLENGE_ANSWER_LENGTH);

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

				ChallengeAnswer = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.CHALLENGE_ANSWER_LENGTH);
			
				return true;
			}
			catch (Exception) { return false; }
		}
	}
}