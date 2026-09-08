// -----------------------------------------------------------------------------
// SonarScope — the PPI display, drawn into a texture.
//
// A plan-position indicator: the boat at the centre, north up, a sweep going
// round, and returns painted where they came back. Contacts fade over about
// fifteen seconds, so the display is a short memory rather than a live feed —
// which matters, because the monster has moved by the time you reach the blip.
//
// Drawn pixel by pixel into a Texture2D rather than composed from UI images:
// there is no sprite atlas to author, the whole thing is a hundred lines, and it
// costs a fraction of a millisecond at 12 Hz.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using LochNess.Sim;
using LochNess.Vessel;
using UnityEngine;
using UnityEngine.UI;

namespace LochNess.UI
{
    public sealed class SonarScope : MonoBehaviour
    {
        private const int Resolution = 220;
        private const float RefreshInterval = 1f / 12f;
        private const float ContactLifetime = 15f;

        private struct Blip
        {
            public float Bearing;
            public float Range;
            public float Clarity;
            public ContactKind Kind;
            public float BornAt;
        }

        private RawImage _target;
        private Texture2D _texture;
        private Color32[] _pixels;
        private readonly List<Blip> _blips = new List<Blip>(24);

        private float _sweepAngle;
        private float _refreshTimer;
        private float _displayRange = 130f;

        public void Attach(RawImage target)
        {
            _target = target;

            _texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _pixels = new Color32[Resolution * Resolution];

            if (_target != null) _target.texture = _texture;
        }

        private void OnEnable() => SonarSet.OnContacts += Ingest;
        private void OnDisable() => SonarSet.OnContacts -= Ingest;

        private void Ingest(NetContact[] contacts)
        {
            if (contacts == null) return;

            var set = SonarSet.Instance;
            if (set != null) _displayRange = set.Range;

            for (int i = 0; i < contacts.Length; i++)
            {
                _blips.Add(new Blip
                {
                    Bearing = contacts[i].Bearing,
                    Range = contacts[i].Range,
                    Clarity = contacts[i].Clarity,
                    Kind = contacts[i].KindEnum,
                    BornAt = Time.time
                });
            }
        }

        private void Update()
        {
            _sweepAngle = (_sweepAngle + Time.deltaTime * 72f) % 360f;

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshInterval;

            Expire();
            Redraw();
        }

        private void Expire()
        {
            float now = Time.time;
            for (int i = _blips.Count - 1; i >= 0; i--)
            {
                if (now - _blips[i].BornAt > ContactLifetime) _blips.RemoveAt(i);
            }
        }

        private void Redraw()
        {
            if (_texture == null) return;

            const int centre = Resolution / 2;
            float radius = centre - 2f;

            // ---- Ground and range rings ----------------------------------------
            var scopeGround = new Color32(9, 20, 22, 255);
            var outside = new Color32(0, 0, 0, 0);
            var ring = new Color32(38, 82, 74, 255);

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    float dx = x - centre;
                    float dy = y - centre;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    if (d > radius) { _pixels[y * Resolution + x] = outside; continue; }

                    Color32 c = scopeGround;

                    // Rings every quarter of the display range, plus the crosshair.
                    for (int r = 1; r <= 4; r++)
                    {
                        if (Mathf.Abs(d - radius * r / 4f) < 0.8f) { c = ring; break; }
                    }
                    if (Mathf.Abs(dx) < 0.7f || Mathf.Abs(dy) < 0.7f) c = ring;

                    _pixels[y * Resolution + x] = c;
                }
            }

            DrawSweep(centre, radius);
            DrawBlips(centre, radius);

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private void DrawSweep(int centre, float radius)
        {
            // A short trailing fan behind the sweep line, so it reads as rotating
            // rather than as a spoke that jumps.
            for (float trail = 0f; trail < 42f; trail += 1.5f)
            {
                float angle = (_sweepAngle - trail) * Mathf.Deg2Rad;
                float intensity = 1f - trail / 42f;

                var colour = new Color32(
                    (byte)(40 + 90 * intensity),
                    (byte)(120 + 130 * intensity),
                    (byte)(110 + 110 * intensity),
                    (byte)(60 + 190 * intensity));

                for (float r = 2f; r < radius; r += 0.7f)
                {
                    int x = centre + Mathf.RoundToInt(Mathf.Sin(angle) * r);
                    int y = centre + Mathf.RoundToInt(Mathf.Cos(angle) * r);
                    Plot(x, y, colour);
                }
            }
        }

        private void DrawBlips(int centre, float radius)
        {
            float now = Time.time;

            for (int i = 0; i < _blips.Count; i++)
            {
                Blip blip = _blips[i];
                float age = Mathf.Clamp01((now - blip.BornAt) / ContactLifetime);
                float fade = 1f - age * age; // holds bright, then drops away quickly

                float normalised = Mathf.Clamp01(blip.Range / Mathf.Max(1f, _displayRange));
                float angle = blip.Bearing * Mathf.Deg2Rad;

                int bx = centre + Mathf.RoundToInt(Mathf.Sin(angle) * normalised * radius);
                int by = centre + Mathf.RoundToInt(Mathf.Cos(angle) * normalised * radius);

                // A confirmed return is drawn in alarm red and larger. Clutter and faint
                // anomalies are the same phosphor green — the operator is NOT told which
                // is which, because working that out is the game.
                bool confirmed = blip.Kind == ContactKind.Confirmed;
                float strength = Mathf.Clamp01(0.35f + blip.Clarity * 0.65f) * fade;

                var colour = confirmed
                    ? new Color32((byte)(230 * strength), (byte)(95 * strength), (byte)(70 * strength), (byte)(255 * fade))
                    : new Color32((byte)(110 * strength), (byte)(230 * strength), (byte)(200 * strength), (byte)(255 * fade));

                int size = confirmed ? 3 : (blip.Clarity > 0.5f ? 2 : 1);
                for (int dy = -size; dy <= size; dy++)
                {
                    for (int dx = -size; dx <= size; dx++)
                    {
                        if (dx * dx + dy * dy > size * size) continue;
                        Plot(bx + dx, by + dy, colour);
                    }
                }
            }
        }

        private void Plot(int x, int y, Color32 colour)
        {
            if (x < 0 || y < 0 || x >= Resolution || y >= Resolution) return;
            _pixels[y * Resolution + x] = colour;
        }
    }
}
