// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Beatmaps.ControlPoints
{
    public class EffectControlPoint : ControlPoint, IEquatable<EffectControlPoint>
    {
        /// <summary>
        /// Whether this control point enables Kiai mode.
        /// </summary>
        public bool KiaiMode;

        /// <summary>
        /// Whether the first bar line of this control point is ignored.
        /// </summary>
        public bool OmitFirstBarLine;

        public bool Equals(EffectControlPoint other)
            => base.Equals(other)
               && KiaiMode == other?.KiaiMode && OmitFirstBarLine == other.OmitFirstBarLine;
    }
}
