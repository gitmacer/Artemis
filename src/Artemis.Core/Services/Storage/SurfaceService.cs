﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Artemis.Storage.Repositories.Interfaces;
using RGB.NET.Core;
using Serilog;

namespace Artemis.Core.Services
{
    internal class SurfaceService : ISurfaceService
    {
        private readonly ILogger _logger;
        private readonly IPluginService _pluginService;
        private readonly PluginSetting<double> _renderScaleSetting;
        private readonly IRgbService _rgbService;
        private readonly List<ArtemisSurface> _surfaceConfigurations;
        private readonly ISurfaceRepository _surfaceRepository;

        public SurfaceService(ILogger logger, ISurfaceRepository surfaceRepository, IRgbService rgbService, IPluginService pluginService, ISettingsService settingsService)
        {
            _logger = logger;
            _surfaceRepository = surfaceRepository;
            _rgbService = rgbService;
            _pluginService = pluginService;
            _surfaceConfigurations = new List<ArtemisSurface>();
            _renderScaleSetting = settingsService.GetSetting("Core.RenderScale", 0.5);

            LoadFromRepository();

            _rgbService.DeviceLoaded += RgbServiceOnDeviceLoaded;
            _renderScaleSetting.SettingChanged += RenderScaleSettingOnSettingChanged;
        }

        public ArtemisSurface ActiveSurface { get; private set; }
        public ReadOnlyCollection<ArtemisSurface> SurfaceConfigurations => _surfaceConfigurations.AsReadOnly();

        public ArtemisSurface CreateSurfaceConfiguration(string name)
        {
            // Create a blank config
            var configuration = new ArtemisSurface(_rgbService.Surface, name, _renderScaleSetting.Value);

            // Add all current devices
            foreach (var rgbDevice in _rgbService.LoadedDevices)
            {
                var plugin = _pluginService.GetPluginByDevice(rgbDevice);
                configuration.Devices.Add(new ArtemisDevice(rgbDevice, plugin, configuration));
            }

            lock (_surfaceConfigurations)
            {
                _surfaceRepository.Add(configuration.SurfaceEntity);
                _surfaceConfigurations.Add(configuration);

                UpdateSurfaceConfiguration(configuration, true);
                return configuration;
            }
        }

        public void SetActiveSurfaceConfiguration(ArtemisSurface surface)
        {
            if (ActiveSurface == surface)
                return;

            // Set the new entity
            ActiveSurface = surface;

            // Ensure only the new entity is marked as active
            lock (_surfaceConfigurations)
            {
                // Mark only the new surface as active
                foreach (var configuration in _surfaceConfigurations)
                {
                    configuration.IsActive = configuration == ActiveSurface;
                    configuration.ApplyToEntity();

                    _surfaceRepository.Save(configuration.SurfaceEntity);
                }
            }

            // Apply the active surface entity to the devices
            if (ActiveSurface != null)
            {
                foreach (var device in ActiveSurface.Devices)
                    device.ApplyToRgbDevice();
            }

            // Update the RGB service's graphics decorator to work with the new surface entity
            _rgbService.UpdateSurfaceLedGroup();
            OnActiveSurfaceConfigurationChanged(new SurfaceConfigurationEventArgs(ActiveSurface));
        }

        public void UpdateSurfaceConfiguration(ArtemisSurface surface, bool includeDevices)
        {
            surface.ApplyToEntity();
            if (includeDevices)
            {
                foreach (var deviceConfiguration in surface.Devices)
                {
                    deviceConfiguration.ApplyToEntity();
                    if (surface.IsActive)
                        deviceConfiguration.ApplyToRgbDevice();
                }
            }

            _surfaceRepository.Save(surface.SurfaceEntity);
            _rgbService.UpdateSurfaceLedGroup();
            OnSurfaceConfigurationUpdated(new SurfaceConfigurationEventArgs(surface));
        }

        public void DeleteSurfaceConfiguration(ArtemisSurface surface)
        {
            if (surface == ActiveSurface)
                throw new ArtemisCoreException($"Cannot delete surface entity '{surface.Name}' because it is active.");

            lock (_surfaceConfigurations)
            {
                var entity = surface.SurfaceEntity;
                _surfaceConfigurations.Remove(surface);
                _surfaceRepository.Remove(entity);
            }
        }

        #region Repository

        private void LoadFromRepository()
        {
            var configs = _surfaceRepository.GetAll();
            foreach (var surfaceEntity in configs)
            {
                // Create the surface entity
                var surfaceConfiguration = new ArtemisSurface(_rgbService.Surface, surfaceEntity, _renderScaleSetting.Value);
                foreach (var position in surfaceEntity.DeviceEntities)
                {
                    var device = _rgbService.Surface.Devices.FirstOrDefault(d => d.GetDeviceIdentifier() == position.DeviceIdentifier);
                    if (device != null)
                    {
                        var plugin = _pluginService.GetPluginByDevice(device);
                        surfaceConfiguration.Devices.Add(new ArtemisDevice(device, plugin, surfaceConfiguration, position));
                    }
                }

                // Finally, add the surface config to the collection
                lock (_surfaceConfigurations)
                {
                    _surfaceConfigurations.Add(surfaceConfiguration);
                }
            }

            // When all surface configs are loaded, apply the active surface config
            var active = SurfaceConfigurations.FirstOrDefault(c => c.IsActive);
            if (active != null)
                SetActiveSurfaceConfiguration(active);
            else
            {
                active = SurfaceConfigurations.FirstOrDefault();
                if (active != null)
                    SetActiveSurfaceConfiguration(active);
                else
                    SetActiveSurfaceConfiguration(CreateSurfaceConfiguration("Default"));
            }
        }

        #endregion

        #region Utilities

        private void AddDeviceIfMissing(IRGBDevice rgbDevice, ArtemisSurface surface)
        {
            var deviceIdentifier = rgbDevice.GetDeviceIdentifier();
            var device = surface.Devices.FirstOrDefault(d => d.DeviceEntity.DeviceIdentifier == deviceIdentifier);

            if (device != null)
                return;

            // Find an existing device config and use that
            var existingDeviceConfig = surface.SurfaceEntity.DeviceEntities.FirstOrDefault(d => d.DeviceIdentifier == deviceIdentifier);
            if (existingDeviceConfig != null)
            {
                var plugin = _pluginService.GetPluginByDevice(rgbDevice);
                device = new ArtemisDevice(rgbDevice, plugin, surface, existingDeviceConfig);
            }
            // Fall back on creating a new device
            else
            {
                _logger.Information(
                    "No device config found for {deviceInfo}, device hash: {deviceHashCode}. Adding a new entry.",
                    rgbDevice.DeviceInfo,
                    deviceIdentifier
                );
                var plugin = _pluginService.GetPluginByDevice(rgbDevice);
                device = new ArtemisDevice(rgbDevice, plugin, surface);
            }

            surface.Devices.Add(device);
        }

        #endregion

        #region Event handlers

        private void RgbServiceOnDeviceLoaded(object sender, DeviceEventArgs e)
        {
            lock (_surfaceConfigurations)
            {
                foreach (var surfaceConfiguration in _surfaceConfigurations)
                    AddDeviceIfMissing(e.Device, surfaceConfiguration);
            }

            UpdateSurfaceConfiguration(ActiveSurface, true);
        }

        private void RenderScaleSettingOnSettingChanged(object sender, EventArgs e)
        {
            foreach (var surfaceConfiguration in SurfaceConfigurations)
            {
                surfaceConfiguration.UpdateScale(_renderScaleSetting.Value);
                OnSurfaceConfigurationUpdated(new SurfaceConfigurationEventArgs(surfaceConfiguration));
            }
        }

        #endregion

        #region Events

        public event EventHandler<SurfaceConfigurationEventArgs> ActiveSurfaceConfigurationSelected;
        public event EventHandler<SurfaceConfigurationEventArgs> SurfaceConfigurationUpdated;

        protected virtual void OnActiveSurfaceConfigurationChanged(SurfaceConfigurationEventArgs e)
        {
            ActiveSurfaceConfigurationSelected?.Invoke(this, e);
        }

        protected virtual void OnSurfaceConfigurationUpdated(SurfaceConfigurationEventArgs e)
        {
            SurfaceConfigurationUpdated?.Invoke(this, e);
        }

        #endregion
    }
}