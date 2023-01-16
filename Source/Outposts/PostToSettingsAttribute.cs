using System;
using RimWorld;
using Verse;

namespace Outposts
{
    public class PostToSettingsAttribute : Attribute
    {
        public enum DrawMode
        {
            Checkbox,
            IntSlider,
            Slider,
            Percentage,
            Time
        }

        private readonly object ignore;
        private readonly float max;
        private readonly float min;
        private readonly bool shouldIgnore;

        public object Default;

        public string LabelKey;
        public DrawMode Mode;
        public string TooltipKey;

        public PostToSettingsAttribute(string label, DrawMode mode, object value = null, float min = 0f, float max = 0f, string tooltip = null, object dontShowAt = null)
        {
            LabelKey = label;
            Mode = mode;
            Default = value;
            this.min = min;
            this.max = max;
            TooltipKey = tooltip;
            ignore = dontShowAt;
            shouldIgnore = dontShowAt is not null;
        }

        public void Draw(Listing_Standard listing, ref object current)
        {
            if (shouldIgnore && Equals(current, ignore))
            {
                return;
            }

            switch (Mode)
            {
                case DrawMode.Checkbox:
                    var checkState = (bool) current;
                    listing.CheckboxLabeled(LabelKey.Translate(), ref checkState, TooltipKey?.Translate());
                    if (checkState != (bool) current) current = checkState;
                    break;
                case DrawMode.Slider:
                    listing.Label(LabelKey.Translate() + ": " + current);
                    current = listing.Slider((float) current, min, max);
                    break;
                case DrawMode.Percentage:
                    listing.Label(LabelKey.Translate() + ": " + ((float) current).ToStringPercent());
                    current = listing.Slider((float) current, min, max);
                    break;
                case DrawMode.IntSlider:
                    listing.Label(LabelKey.Translate() + ": " + current);
                    current = (int) listing.Slider((int) current, (int) min, (int) max);
                    break;
                case DrawMode.Time:
                    listing.Label(LabelKey.Translate() + ": " + ((int) current).ToStringTicksToPeriodVerbose());
                    current = (int) listing.Slider((int) current, GenDate.TicksPerHour, GenDate.TicksPerYear);
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Unknown DrawMode {Mode}");
            }
        }
    }
}