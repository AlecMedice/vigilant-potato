// -----------------------------------------------------------------------------
// BoatStation — a post aboard the vessel that one crew member can occupy.
//
// Stations are what turn "several people on a boat" into a crew. Only one player
// can hold the helm, so somebody has to decide who drives; the sonar operator has
// information the helm does not; the bow watch is the only one who can confirm a
// surfacing with their own eyes. The game's coordination comes entirely from this
// division of labour, not from any scripted objective.
//
// This component is deliberately dumb. It marks a position and a role; all
// authority over who holds it lives in BoatController, where it can be a
// replicated NetworkVariable that the server arbitrates.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace LochNess.Vessel
{
    public enum StationRole : byte
    {
        /// <summary>Drives the boat. Throttle and rudder.</summary>
        Helm = 0,
        /// <summary>Works the sonar set. Pings, and reads the scope.</summary>
        Sonar = 1,
        /// <summary>Bow lookout. Swings the lamp and calls visual sightings.</summary>
        Watch = 2
    }

    public sealed class BoatStation : MonoBehaviour
    {
        [SerializeField] private StationRole role;
        [Tooltip("How close a crew member must stand to claim this station.")]
        [SerializeField] private float interactRadius = 1.9f;

        public StationRole Role => role;
        public float InteractRadius => interactRadius;

        /// <summary>Where an occupant stands, in the boat's local space.</summary>
        public Vector3 LocalStandPosition => transform.localPosition;

        /// <summary>The heading an occupant faces on taking the post, in boat-local space.</summary>
        public Quaternion LocalFacing => transform.localRotation;

        public void Configure(StationRole stationRole, float radius)
        {
            role = stationRole;
            interactRadius = radius;
        }

        public string Label
        {
            get
            {
                switch (role)
                {
                    case StationRole.Helm: return "HELM";
                    case StationRole.Sonar: return "SONAR";
                    default: return "BOW WATCH";
                }
            }
        }

        public string Verb
        {
            get
            {
                switch (role)
                {
                    case StationRole.Helm: return "take the helm";
                    case StationRole.Sonar: return "work the sonar";
                    default: return "stand watch";
                }
            }
        }
    }
}
