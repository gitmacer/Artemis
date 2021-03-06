﻿using System;
using Artemis.Storage.Entities.Plugins;
using Artemis.Storage.Repositories.Interfaces;
using Newtonsoft.Json;
using Stylet;

namespace Artemis.Core
{
    /// <summary>
    ///     Represents a setting tied to a plugin of type <typeparamref name="T" />
    /// </summary>
    /// <typeparam name="T">The value type of the setting</typeparam>
    public class PluginSetting<T> : PropertyChangedBase
    {
        // ReSharper disable once NotAccessedField.Local
        private readonly PluginInfo _pluginInfo;
        private readonly IPluginRepository _pluginRepository;
        private readonly PluginSettingEntity _pluginSettingEntity;
        private T _value;

        internal PluginSetting(PluginInfo pluginInfo, IPluginRepository pluginRepository, PluginSettingEntity pluginSettingEntity)
        {
            _pluginInfo = pluginInfo;
            _pluginRepository = pluginRepository;
            _pluginSettingEntity = pluginSettingEntity;

            Name = pluginSettingEntity.Name;
            try
            {
                Value = JsonConvert.DeserializeObject<T>(pluginSettingEntity.Value);
            }
            catch (JsonReaderException)
            {
                Value = default;
            }
        }

        /// <summary>
        ///     The name of the setting, unique to this plugin
        /// </summary>
        public string Name { get; }

        /// <summary>
        ///     The value of the setting
        /// </summary>
        public T Value
        {
            get => _value;
            set
            {
                if (Equals(_value, value)) return;

                _value = value;
                OnSettingChanged();
                NotifyOfPropertyChange(nameof(Value));

                if (AutoSave)
                    Save();
            }
        }

        /// <summary>
        ///     Determines whether the setting has been changed
        /// </summary>
        public bool HasChanged => JsonConvert.SerializeObject(Value) != _pluginSettingEntity.Value;

        /// <summary>
        ///     Gets or sets whether changes must automatically be saved
        ///     <para>Note: When set to <c>true</c> <see cref="HasChanged" /> is always <c>false</c></para>
        /// </summary>
        public bool AutoSave { get; set; }

        /// <summary>
        ///     Resets the setting to the last saved value
        /// </summary>
        public void RejectChanges()
        {
            Value = JsonConvert.DeserializeObject<T>(_pluginSettingEntity.Value);
        }

        /// <summary>
        ///     Saves the setting
        /// </summary>
        public void Save()
        {
            if (!HasChanged)
                return;

            _pluginSettingEntity.Value = JsonConvert.SerializeObject(Value);
            _pluginRepository.SaveSetting(_pluginSettingEntity);
        }

        /// <summary>
        ///     Occurs when the value of the setting has been changed
        /// </summary>
        public event EventHandler<EventArgs> SettingChanged;

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{nameof(Name)}: {Name}, {nameof(Value)}: {Value}, {nameof(HasChanged)}: {HasChanged}";
        }

        /// <summary>
        ///     Invokes the <see cref="SettingChanged" /> event
        /// </summary>
        protected internal virtual void OnSettingChanged()
        {
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}