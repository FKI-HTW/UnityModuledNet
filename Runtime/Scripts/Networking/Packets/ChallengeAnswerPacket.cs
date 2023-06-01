using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal class ChallengeAnswerPacket : AConnectionNetworkPacket
	{
		public byte[] ChallengeAnswer { get; private set; }
		public string Username { get; private set; }
		public Color32 Color { get; private set; }

		public ChallengeAnswerPacket(ulong challenge, string username, Color32 color)
		{
			Type = EPacketType.ChallengeAnswer;
			using SHA256 h = SHA256.Create();
			ChallengeAnswer = h.ComputeHash(BitConverter.GetBytes(challenge));
			Username = username;
			Color = color;
		}

		public ChallengeAnswerPacket(byte[] packet)
		{
			Type = EPacketType.ChallengeAnswer;
			Bytes = packet;
		}

		public override byte[] Serialize()
		{
			byte[] username = Encoding.ASCII.GetBytes(Username);

			byte[] bytes = new byte[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + username.Length + 3];
			bytes[ModuledNetSettings.CRC32_LENGTH] = (byte)Type;
			Array.Copy(ChallengeAnswer, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.CHALLENGE_ANSWER_LENGTH);
			bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH] = (byte)username.Length;
			Array.Copy(username, 0, bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, username.Length);
			int position = ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + username.Length;
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

				ChallengeAnswer = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH, ModuledNetSettings.CHALLENGE_ANSWER_LENGTH);
				byte usernameLength = Bytes[ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH];
				byte[] usernameBytes = GetBytesFromArray(Bytes, ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH, usernameLength);
				Username = Encoding.ASCII.GetString(usernameBytes);
				int position = ModuledNetSettings.CRC32_LENGTH + ModuledNetSettings.PACKET_TYPE_LENGTH + ModuledNetSettings.CHALLENGE_ANSWER_LENGTH + ModuledNetSettings.DATA_FLAG_LENGTH + usernameLength;
				Color = new(Bytes[position], Bytes[position + 1], Bytes[position + 2], 255);

				return true;
			}
			catch (Exception) { return false; }
		}
	}
}