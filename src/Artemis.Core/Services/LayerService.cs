﻿using System;
using System.Collections.Generic;
using System.Linq;
using Artemis.Core.Exceptions;
using Artemis.Core.Models.Profile;
using Artemis.Core.Plugins.Abstract;
using Artemis.Core.Plugins.LayerBrush.Abstract;
using Artemis.Core.Plugins.LayerEffect;
using Artemis.Core.Plugins.LayerEffect.Abstract;
using Artemis.Core.Services.Interfaces;
using Artemis.Storage.Entities.Profile;
using Ninject;
using Serilog;

namespace Artemis.Core.Services
{
    public class LayerService : ILayerService
    {
        private readonly IKernel _kernel;
        private readonly ILogger _logger;
        private readonly IPluginService _pluginService;

        public LayerService(IKernel kernel, ILogger logger, IPluginService pluginService)
        {
            _kernel = kernel;
            _logger = logger;
            _pluginService = pluginService;
        }

        public Layer CreateLayer(Profile profile, ProfileElement parent, string name)
        {
            var layer = new Layer(profile, parent, name);
            parent.AddChild(layer);

            // Layers have two hardcoded property groups, instantiate them
            layer.General.InitializeProperties(this, layer, "General.");
            layer.Transform.InitializeProperties(this, layer, "Transform.");

            // With the properties loaded, the layer brush and effect can be instantiated
            InstantiateLayerBrush(layer);
            InstantiateLayerEffects(layer);

            return layer;
        }

        public void RemoveLayerBrush(Layer layer)
        {
            layer.RemoveLayerBrush();
            layer.OnLayerBrushUpdated();
        }

        public BaseLayerBrush InstantiateLayerBrush(Layer layer)
        {
            if (layer.LayerBrush != null)
                throw new ArtemisCoreException("Layer already has an instantiated layer brush");

            var descriptorReference = layer.General.BrushReference?.CurrentValue;
            if (descriptorReference == null)
                return null;

            // Get a matching descriptor
            var layerBrushProviders = _pluginService.GetPluginsOfType<LayerBrushProvider>();
            var descriptors = layerBrushProviders.SelectMany(l => l.LayerBrushDescriptors).ToList();
            var descriptor = descriptors.FirstOrDefault(d => d.LayerBrushProvider.PluginInfo.Guid == descriptorReference.BrushPluginGuid &&
                                                             d.LayerBrushType.Name == descriptorReference.BrushType);

            if (descriptor == null)
                return null;

            var brush = (BaseLayerBrush) _kernel.Get(descriptor.LayerBrushType);
            brush.Layer = layer;
            brush.Descriptor = descriptor;
            brush.Initialize(this);
            brush.Update(0);

            layer.LayerBrush = brush;
            layer.OnLayerBrushUpdated();

            return brush;
        }

        public BaseLayerEffect AddLayerEffect(EffectProfileElement effectElement, LayerEffectDescriptor layerEffectDescriptor)
        {
            // Create the effect with dependency injection
            var effect = (BaseLayerEffect) _kernel.Get(layerEffectDescriptor.LayerEffectType);

            effect.ProfileElement = effectElement;
            effect.EntityId = Guid.NewGuid();
            effect.Enabled = true;
            effect.Order = effectElement.LayerEffects.Count + 1;
            effect.Descriptor = layerEffectDescriptor;

            effect.Initialize(this);
            effect.Update(0);

            effectElement.AddLayerEffect(effect);
            _logger.Debug("Added layer effect with root path {rootPath}", effect.PropertyRootPath);
            return effect;
        }

        public void RemoveLayerEffect(BaseLayerEffect layerEffect)
        {
            layerEffect.ProfileElement.RemoveLayerEffect(layerEffect);
        }

        public void InstantiateLayerEffects(EffectProfileElement effectElement)
        {
            if (effectElement.LayerEffects.Any())
                throw new ArtemisCoreException("Effect element (layer/folder) already has instantiated layer effects");

            var layerEffectProviders = _pluginService.GetPluginsOfType<LayerEffectProvider>();
            var descriptors = layerEffectProviders.SelectMany(l => l.LayerEffectDescriptors).ToList();
            var entities = effectElement.EffectsEntity.LayerEffects.OrderByDescending(e => e.Order).ToList();
            
            foreach (var layerEffectEntity in entities)
            {
                // Get a matching descriptor
                var descriptor = descriptors.FirstOrDefault(d => d.LayerEffectProvider.PluginInfo.Guid == layerEffectEntity.PluginGuid &&
                                                                 d.LayerEffectType.Name == layerEffectEntity.EffectType);
                if (descriptor == null)
                    continue;

                // Create the effect with dependency injection
                var effect = (BaseLayerEffect) _kernel.Get(descriptor.LayerEffectType);

                effect.ProfileElement = effectElement;
                effect.EntityId = layerEffectEntity.Id;
                effect.Order = layerEffectEntity.Order;
                effect.Name = layerEffectEntity.Name;
                effect.Enabled = layerEffectEntity.Enabled;
                effect.Descriptor = descriptor;

                effect.Initialize(this);
                effect.Update(0);

                effectElement.AddLayerEffect(effect);
                _logger.Debug("Instantiated layer effect with root path {rootPath}", effect.PropertyRootPath);
            }
        }
    }
}