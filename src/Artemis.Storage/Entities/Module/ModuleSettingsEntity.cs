﻿using System;

namespace Artemis.Storage.Entities.Module
{
    public class ModuleSettingsEntity
    {
        public ModuleSettingsEntity()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }
        public Guid PluginGuid { get; set; }
        public int PriorityCategory { get; set; }
        public int Priority { get; set; }
    }
}