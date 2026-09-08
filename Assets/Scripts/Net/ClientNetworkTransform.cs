// -----------------------------------------------------------------------------
// ClientNetworkTransform — owner-authoritative transform replication.
//
// NGO's stock NetworkTransform is server-authoritative: a client's own movement
// would have to round-trip to the host before it appeared on their screen, which
// feels dreadful at any real latency. Overriding OnIsServerAuthoritative() flips
// that so the OWNER writes and everyone else — the server included — reads. This
// is the pattern from the NGO samples.
//
// USED BY CREW ONLY. The vessel and the monster are simulated on the server and
// use the stock, server-authoritative NetworkTransform: there is no owner to hand
// authority to, and in the vessel's case handing a client authority over the
// object the whole crew stands on would be indefensible.
//
// TRADE-OFF, ACCEPTED FOR THIS SLICE
// The owner is trusted with its own position on the deck. That is a small blast
// radius — crew are confined to a 14-metre boat by CrewController's local-space
// clamp, which runs on every peer, so the worst a modified client achieves is
// standing somewhere silly on its own screen. It cannot leave the vessel, reach
// the monster, or affect anyone else's simulation. Server-authoritative movement
// with client-side prediction is the hardening path if this ever ships.
// -----------------------------------------------------------------------------

using Unity.Netcode.Components;
using UnityEngine;

namespace LochNess.Net
{
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        /// <summary>Returning false hands write authority to the object's owner.</summary>
        protected override bool OnIsServerAuthoritative() => false;
    }
}
