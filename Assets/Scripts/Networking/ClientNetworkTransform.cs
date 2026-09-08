// ---------------------------------------------------------------------------------------------
//  ClientNetworkTransform.cs
//  Role : Owner-authoritative transform replication for player boats.
//
//  WHY THIS FILE EXISTS
//  --------------------------------------------------------------------------------------------
//  NGO's stock NetworkTransform is server-authoritative: a client's own movement would have to
//  round-trip to the host before it moved on screen, which feels terrible at any real ping.
//  Overriding OnIsServerAuthoritative() flips replication so the OWNER writes and everyone else
//  (including the server) reads. This is the same pattern shipped in the NGO samples.
//
//  TRADE-OFF (accepted for the vertical slice, hardening tracked in the README):
//  The owner is trusted with its own position. PlayerController runs a server-side plausibility
//  check that flags impossible speeds; a shipping build would upgrade that to a correcting
//  server rewind or move to server-authoritative movement with client-side prediction.
//
//  NOTE: Nessie and the AI assistant deliberately use the STOCK, server-authoritative
//  NetworkTransform - the host owns all AI, so there is nothing to make owner-authoritative.
// ---------------------------------------------------------------------------------------------

using Unity.Netcode.Components;
using UnityEngine;

namespace LochNess
{
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        /// <summary>Returning false hands write authority to the object's owner.</summary>
        protected override bool OnIsServerAuthoritative() => false;
    }
}
