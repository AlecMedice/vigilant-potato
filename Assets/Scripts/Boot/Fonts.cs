// -----------------------------------------------------------------------------
// Fonts — the one built-in font, resolved once.
//
// Unity renamed its built-in font from "Arial.ttf" to "LegacyRuntime.ttf" in
// 2022.2. Since this project ships no font assets, both names are tried so the
// same source builds on either. If neither resolves, callers fall back to leaving
// `font` null, which renders nothing but does not throw.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace LochNess.Boot
{
    public static class Fonts
    {
        private static Font _builtin;
        private static bool _resolved;

        public static Font Builtin
        {
            get
            {
                if (_resolved) return _builtin;
                _resolved = true;

                _builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_builtin == null) _builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_builtin == null) Debug.LogWarning("[Fonts] No built-in font found; text will not render.");

                return _builtin;
            }
        }
    }
}
