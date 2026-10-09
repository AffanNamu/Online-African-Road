using System.Collections.Generic;
using ARO.NetCore;
using ARO.Vehicles;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ARO.Multiplayer
{
    /// <summary>
    /// Networked avatar of a player's vehicle. The OWNER's avatar follows the local physics vehicle and publishes its state;
    /// everyone else sees a non-colliding puppet. Transform is synced by OwnerNetworkTransform (interpolated).
    /// Not a trust boundary: money/rewards never depend on anything replicated here.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkVehicle : NetworkBehaviour
    {
        public static readonly List<NetworkVehicle> Active = new List<NetworkVehicle>();
        public static readonly Roster Players = new Roster(8);
        const float StateHz = 10f;

        public readonly NetworkVariable<NetVehicleState> State = new NetworkVariable<NetVehicleState>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<FixedString32Bytes> DefinitionId = new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        VehicleController _puppet; TextMesh _nameplate; float _nextSend; string _puppetDef;

        public string Key => OwnerClientId.ToString();

        public override void OnNetworkSpawn()
        {
            Active.Add(this);
            if (IsOwner)
            {
                PlayerName.Value = new FixedString32Bytes(Truncate(NetVisualContext.LocalName(), 28));
                DefinitionId.Value = new FixedString32Bytes(NetVisualContext.LocalVehicleDefinitionId());
            }
            PlayerName.OnValueChanged += (_, __) => RefreshRoster();
            RefreshRoster();
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);
            Players.Leave(Key);
            if (_puppet != null) Destroy(_puppet.gameObject);
        }

        static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "Driver" : (s.Length <= n ? s : s.Substring(0, n));
        void RefreshRoster() => Players.Join(Key, PlayerName.Value.ToString());

        void Update()
        {
            if (IsOwner) OwnerTick(); else PuppetTick();
        }

        void OwnerTick()
        {
            var v = NetVisualContext.LocalVehicle();
            if (v == null) return;
            // Follow the physics vehicle; OwnerNetworkTransform replicates this transform.
            transform.SetPositionAndRotation(v.transform.position, v.transform.rotation);
            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + 1f / StateHz;
            var st = NetVehicleState.From(v.LightsOn, v.Indicator, v.HornActive, v.BrakeLit, v.Reversing, 0f, v.SpeedKmh, v.damagePct);
            if (!st.Equals(State.Value)) State.Value = st;
            var id = NetVisualContext.LocalVehicleDefinitionId();
            if (DefinitionId.Value.ToString() != id) DefinitionId.Value = new FixedString32Bytes(id);
        }

        void PuppetTick()
        {
            string id = DefinitionId.Value.ToString();
            if (_puppet == null || id != _puppetDef) BuildPuppet(id);
            if (_puppet == null) return;
            var s = State.Value;
            _puppet.ApplyRemoteState(VehicleNetCodec.Has(s.Flags, VehicleNetCodec.Lights), VehicleNetCodec.IndicatorOf(s.Flags),
                VehicleNetCodec.Has(s.Flags, VehicleNetCodec.Brake), VehicleNetCodec.Has(s.Flags, VehicleNetCodec.Reverse),
                VehicleNetCodec.Has(s.Flags, VehicleNetCodec.Horn), VehicleNetCodec.DequantizeSpeed(s.Speed), VehicleNetCodec.DequantizeDamage(s.Damage));
            if (_nameplate != null && Camera.main != null)
                _nameplate.transform.rotation = Quaternion.LookRotation(_nameplate.transform.position - Camera.main.transform.position);
        }

        void BuildPuppet(string defId)
        {
            if (string.IsNullOrEmpty(defId) || !NetVisualContext.Definitions.TryGetValue(defId, out var def)) return;
            if (_puppet != null) Destroy(_puppet.gameObject);
            _puppetDef = defId;
            _puppet = TruckFactory.Create(def, transform.position, transform.rotation, NetVisualContext.BodyMaterial, NetVisualContext.WheelMaterial, localPlayer: false);
            if (_puppet == null) return;
            _puppet.MakeRemote();
            _puppet.transform.SetParent(transform, true);
            var go = new GameObject("Nameplate"); go.transform.SetParent(_puppet.transform, false); go.transform.localPosition = new Vector3(0, 5.2f, 0);
            _nameplate = go.AddComponent<TextMesh>();
            _nameplate.text = PlayerName.Value.ToString(); _nameplate.fontSize = 64; _nameplate.characterSize = 0.12f;
            _nameplate.anchor = TextAnchor.MiddleCenter; _nameplate.color = Color.white;
            PlayerName.OnValueChanged += (_, n) => { if (_nameplate != null) _nameplate.text = n.ToString(); };
        }
    }
}
