﻿using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Artemis.Core;
using Artemis.Core.LayerEffects;
using Artemis.Core.Services;
using Artemis.UI.Shared.Services;
using Stylet;

namespace Artemis.UI.Screens.ProfileEditor.LayerProperties.LayerEffects
{
    public class EffectsViewModel : Conductor<LayerEffectDescriptor>.Collection.AllActive
    {
        private readonly IPluginService _pluginService;
        private readonly IProfileEditorService _profileEditorService;
        private LayerEffectDescriptor _selectedLayerEffectDescriptor;

        public EffectsViewModel(LayerPropertiesViewModel layerPropertiesViewModel, IPluginService pluginService, IProfileEditorService profileEditorService)
        {
            _pluginService = pluginService;
            _profileEditorService = profileEditorService;
            LayerPropertiesViewModel = layerPropertiesViewModel;
            PropertyChanged += HandleSelectedLayerEffectChanged;
        }

        public LayerPropertiesViewModel LayerPropertiesViewModel { get; }
        public bool HasLayerEffectDescriptors => Items.Any();

        public LayerEffectDescriptor SelectedLayerEffectDescriptor
        {
            get => _selectedLayerEffectDescriptor;
            set => SetAndNotify(ref _selectedLayerEffectDescriptor, value);
        }

        public void PopulateDescriptors()
        {
            var layerBrushProviders = _pluginService.GetPluginsOfType<LayerEffectProvider>();
            var descriptors = layerBrushProviders.SelectMany(l => l.LayerEffectDescriptors).ToList();
            Items.AddRange(descriptors.Except(Items));
            Items.RemoveRange(Items.Except(descriptors));

            // Sort by display name
            var index = 0;
            foreach (var layerEffectDescriptor in Items.OrderBy(d => d.DisplayName).ToList())
            {
                if (Items.IndexOf(layerEffectDescriptor) != index)
                    ((BindableCollection<LayerEffectDescriptor>) Items).Move(Items.IndexOf(layerEffectDescriptor), index);
                index++;
            }

            SelectedLayerEffectDescriptor = null;
            NotifyOfPropertyChange(nameof(HasLayerEffectDescriptors));
        }

        private void HandleSelectedLayerEffectChanged(object sender, PropertyChangedEventArgs e)
        {
            RenderProfileElement renderElement;
            if (LayerPropertiesViewModel.SelectedLayer != null)
                renderElement = LayerPropertiesViewModel.SelectedLayer;
            else if (LayerPropertiesViewModel.SelectedFolder != null)
                renderElement = LayerPropertiesViewModel.SelectedFolder;
            else
                return;

            if (e.PropertyName == nameof(SelectedLayerEffectDescriptor) && SelectedLayerEffectDescriptor != null)
            {
                // Let the fancy animation run
                Execute.PostToUIThread(async () =>
                {
                    await Task.Delay(500);
                    renderElement.AddLayerEffect(SelectedLayerEffectDescriptor);
                    _profileEditorService.UpdateSelectedProfileElement();
                });
            }
        }
    }
}