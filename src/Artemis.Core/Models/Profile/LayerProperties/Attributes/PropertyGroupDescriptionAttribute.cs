﻿using System;

namespace Artemis.Core.Models.Profile.LayerProperties.Attributes
{
    public class PropertyGroupDescriptionAttribute : Attribute
    {
        /// <summary>
        ///     The user-friendly name for this property, shown in the UI.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     The user-friendly description for this property, shown in the UI.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        ///     Whether to expand this property by default, this is useful for important parent properties.
        /// </summary>
        public bool ExpandByDefault { get; set; }
    }
}