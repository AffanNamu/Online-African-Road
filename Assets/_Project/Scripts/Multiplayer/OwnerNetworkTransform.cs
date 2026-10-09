using Unity.Netcode.Components;

namespace ARO.Multiplayer
{
    /// <summary>Owner-authoritative transform sync (the owning client drives its own avatar; others interpolate).</summary>
    public class OwnerNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
