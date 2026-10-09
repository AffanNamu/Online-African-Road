using ARO.NetCore;
using Unity.Netcode;

namespace ARO.Multiplayer
{
    /// <summary>Gameplay-relevant vehicle state replicated to everyone (5 bytes). Quantisation lives in the unit-tested VehicleNetCodec.</summary>
    public struct NetVehicleState : INetworkSerializable, System.IEquatable<NetVehicleState>
    {
        public byte Flags; public sbyte Steer; public ushort Speed; public byte Damage;

        public static NetVehicleState From(bool lights, int indicator, bool horn, bool brake, bool reverse, float steer, float kmh, float damagePct) =>
            new NetVehicleState
            {
                Flags = VehicleNetCodec.PackFlags(lights, indicator, horn, brake, reverse),
                Steer = VehicleNetCodec.QuantizeSteer(steer), Speed = VehicleNetCodec.QuantizeSpeed(kmh), Damage = VehicleNetCodec.QuantizeDamage(damagePct)
            };

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        { s.SerializeValue(ref Flags); s.SerializeValue(ref Steer); s.SerializeValue(ref Speed); s.SerializeValue(ref Damage); }

        public bool Equals(NetVehicleState o) => Flags == o.Flags && Steer == o.Steer && Speed == o.Speed && Damage == o.Damage;
    }
}
