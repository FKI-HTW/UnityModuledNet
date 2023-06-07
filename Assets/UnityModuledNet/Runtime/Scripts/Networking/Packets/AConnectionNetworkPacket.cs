namespace CENTIS.UnityModuledNet.Networking.Packets
{
    internal abstract class AConnectionNetworkPacket : ANetworkPacket
    {
        public abstract byte[] Serialize();
        public abstract bool TryDeserialize();
    }
}
