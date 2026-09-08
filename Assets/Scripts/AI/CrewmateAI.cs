// -----------------------------------------------------------------------------
// CrewmateAI — the sonar operator who sails with you in single player.
//
// A DELIBERATE LIMIT: SHE REPORTS, SHE NEVER SCORES.
// She works the sonar, she calls out what comes back, and she never logs a
// confirmed sighting — only a player can do that, from the bow watch. Without
// that limit a solo player can win by standing at the helm doing nothing while
// the AI hunts, which is not a game. With it, she is what she should be: a source
// of information you still have to act on.
//
// She also gives up the sonar the moment a player wants it, so nothing she does
// can block a human from playing the part they prefer.
//
// Runs on the SERVER only. In single player that is the same machine as the
// player, but the code does not know or care.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using LochNess.Core;
using LochNess.Sim;
using LochNess.Vessel;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace LochNess.AI
{
    public sealed class CrewmateAI : NetworkBehaviour
    {
        [Header("Behaviour")]
        [Tooltip("Seconds between her pings. Slower than a player can manage, on purpose.")]
        [SerializeField] private float pingInterval = 7f;
        [Tooltip("Clarity below which she keeps a contact to herself rather than crying wolf.")]
        [SerializeField] private float reportThreshold = 0.3f;
        [SerializeField] private float radioCooldown = 6f;
        [SerializeField] private float walkSpeed = 1.9f;

        private Transform _body;
        private Text _nameTag;

        private float _nextPingAt;
        private float _nextRadioAt;
        private Vector3 _targetLocal;
        private bool _holdsSonar;

        public void Bind(Transform body, Text nameTag)
        {
            _body = body;
            _nameTag = nameTag;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                // On a client she is pure decoration driven by NetworkTransform. This
                // guard matters: without it the client-side instance would also try to
                // claim stations and fight the server's simulation.
                enabled = false;
                return;
            }

            _targetLocal = BoatController.CrewSpawnLocal(3);
            transform.localPosition = _targetLocal;
            _nextPingAt = Time.time + 4f;

            SonarSet.OnContacts += HandleContacts;
        }

        public override void OnNetworkDespawn()
        {
            SonarSet.OnContacts -= HandleContacts;
            if (IsServer && _holdsSonar) BoatController.Instance?.ReleaseAllFor(BoatController.CrewmateId);
        }

        private void Update()
        {
            if (!IsServer) return;

            var boat = BoatController.Instance;
            if (boat == null) return;

            UpdateStation(boat);
            MoveTowardPost(Time.deltaTime);
            UpdatePinging();
        }

        private void UpdateStation(BoatController boat)
        {
            ulong occupant = boat.OccupantOf(StationRole.Sonar);

            if (occupant == BoatController.Nobody && !_holdsSonar)
            {
                // Free set: take it.
                _holdsSonar = boat.ClaimStation(StationRole.Sonar, BoatController.CrewmateId);
            }
            else if (occupant != BoatController.CrewmateId && _holdsSonar)
            {
                // Somebody took it from under her — most likely the player. Step aside.
                _holdsSonar = false;
            }

            if (_holdsSonar)
            {
                BoatStation station = boat.StationFor(StationRole.Sonar);
                if (station != null) _targetLocal = station.LocalStandPosition;
            }
            else
            {
                // Out of the way, on the rail, where she is visible but not in the
                // wheelhouse doorway.
                _targetLocal = new Vector3(1.35f, BoatBuilder.DeckY, 1.4f);
            }
        }

        private void MoveTowardPost(float dt)
        {
            Vector3 here = transform.localPosition;
            Vector3 next = Vector3.MoveTowards(here, _targetLocal, walkSpeed * dt);
            transform.localPosition = DeckBounds.Clamp(next);

            // Face the way she is walking; face forward once she arrives.
            Vector3 delta = next - here;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.0001f)
            {
                transform.localRotation = Quaternion.Slerp(transform.localRotation,
                    Quaternion.LookRotation(delta.normalized, Vector3.up), 1f - Mathf.Exp(-8f * dt));
            }
        }

        private void UpdatePinging()
        {
            if (!_holdsSonar) return;

            var set = SonarSet.Instance;
            if (set == null || Time.time < _nextPingAt) return;

            if (set.ServerPing()) _nextPingAt = Time.time + pingInterval;
        }

        /// <summary>
        /// Turn the strongest return into a spoken bearing. She reports what the set
        /// says, uncertainty and all — including clutter, because she cannot tell the
        /// difference either, and neither can the player.
        /// </summary>
        private void HandleContacts(NetContact[] contacts)
        {
            if (!IsServer || !_holdsSonar || contacts == null || contacts.Length == 0) return;
            if (Time.time < _nextRadioAt) return;

            NetContact best = default;
            bool found = false;
            for (int i = 0; i < contacts.Length; i++)
            {
                if (contacts[i].Clarity < reportThreshold) continue;
                if (!found || contacts[i].Clarity > best.Clarity) { best = contacts[i]; found = true; }
            }

            if (!found)
            {
                if (Random.value < 0.35f) Say("Nothing but weed on that sweep.");
                _nextRadioAt = Time.time + radioCooldown;
                return;
            }

            _nextRadioAt = Time.time + radioCooldown;

            string bearing = Mathf.RoundToInt(best.Bearing).ToString("000");
            int metres = Mathf.RoundToInt(best.Range / 5f) * 5;

            if (best.KindEnum == ContactKind.Confirmed || best.Clarity > 0.6f)
                Say($"Strong return, bearing {bearing}, {metres} metres. That's no fish.");
            else if (best.Clarity > 0.42f)
                Say($"Contact bearing {bearing}, about {metres} metres out.");
            else
                Say($"Something faint on {bearing}. Could be anything.");
        }

        private void Say(string line) => MatchState.Server?.RadioAll($"{CrewmateBuilder.CrewmateName}: {line}");
    }
}
