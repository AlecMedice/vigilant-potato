// -----------------------------------------------------------------------------
// UnderwaterVeil — swaps the fog when the camera goes under.
//
// Only really seen when the bow dips in a swell or the camera clips the surface,
// but without it those moments look like a rendering bug. Peat-stained water goes
// almost opaque within a metre, which is the honest look and also the reason the
// crew are dependent on sonar.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace LochNess.World
{
    public sealed class UnderwaterVeil : MonoBehaviour
    {
        private static readonly Color AboveColour = new Color(0.42f, 0.47f, 0.50f);
        private static readonly Color BelowColour = new Color(0.03f, 0.07f, 0.07f);
        private const float AboveDensity = 0.0042f;
        private const float BelowDensity = 0.16f;

        private bool _submerged;
        private bool _known;

        private void LateUpdate()
        {
            Vector3 p = transform.position;
            bool submerged = p.y < WaterSurface.SampleHeight(p.x, p.z);
            if (_known && submerged == _submerged) return;

            _known = true;
            _submerged = submerged;

            RenderSettings.fogColor = submerged ? BelowColour : AboveColour;
            RenderSettings.fogDensity = submerged ? BelowDensity : AboveDensity;
        }

        private void OnDisable()
        {
            // Leave the world as we found it, or a destroyed camera strands the whole
            // scene in underwater fog.
            RenderSettings.fogColor = AboveColour;
            RenderSettings.fogDensity = AboveDensity;
        }
    }
}
