namespace CENTIS.UnityModuledNet.Networking.Packets
{
	internal abstract class ASequencedNetworkPacket : ANetworkPacket
	{
		public ushort Sequence { get; protected set; }

		public abstract byte[] Serialize(ushort sequence);
		public abstract bool TryDeserialize();
	}
}
