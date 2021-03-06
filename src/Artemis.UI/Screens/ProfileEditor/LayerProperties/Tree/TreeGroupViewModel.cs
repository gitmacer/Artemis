﻿using System;
using System.Collections.Generic;
using System.Linq;
using Artemis.Core;
using Artemis.Core.LayerBrushes;
using Artemis.Core.LayerEffects;
using Artemis.UI.Exceptions;
using Artemis.UI.Screens.ProfileEditor.Dialogs;
using Artemis.UI.Screens.ProfileEditor.Windows;
using Artemis.UI.Shared.Services;
using Ninject;
using Ninject.Parameters;
using Stylet;

namespace Artemis.UI.Screens.ProfileEditor.LayerProperties.Tree
{
    public class TreeGroupViewModel : PropertyChangedBase
    {
        private readonly IDialogService _dialogService;
        private readonly IKernel _kernel;
        private readonly IProfileEditorService _profileEditorService;
        private readonly IWindowManager _windowManager;

        public TreeGroupViewModel(
            LayerPropertyGroupViewModel layerPropertyGroupViewModel,
            IProfileEditorService profileEditorService,
            IDialogService dialogService,
            IWindowManager windowManager,
            IKernel kernel)
        {
            _profileEditorService = profileEditorService;
            _dialogService = dialogService;
            _windowManager = windowManager;
            _kernel = kernel;

            LayerPropertyGroupViewModel = layerPropertyGroupViewModel;
            LayerPropertyGroup = LayerPropertyGroupViewModel.LayerPropertyGroup;

            DetermineGroupType();
        }

        public LayerPropertyGroupViewModel LayerPropertyGroupViewModel { get; }
        public LayerPropertyGroup LayerPropertyGroup { get; }
        public LayerPropertyGroupType GroupType { get; set; }

        public void OpenBrushSettings()
        {
            var layerBrush = LayerPropertyGroup.LayerBrush;
            var configurationViewModel = layerBrush.ConfigurationDialog;
            if (configurationViewModel == null)
                return;

            try
            {
                // Limit to one constructor, there's no need to have more and it complicates things anyway
                var constructors = configurationViewModel.Type.GetConstructors();
                if (constructors.Length != 1)
                    throw new ArtemisUIException("Brush configuration dialogs must have exactly one constructor");

                // Find the BaseLayerBrush parameter, it is required by the base constructor so its there for sure
                var brushParameter = constructors.First().GetParameters().First(p => typeof(BaseLayerBrush).IsAssignableFrom(p.ParameterType));
                var argument = new ConstructorArgument(brushParameter.Name, layerBrush);
                var viewModel = (BrushConfigurationViewModel) layerBrush.PluginInfo.Kernel.Get(configurationViewModel.Type, argument);

                _windowManager.ShowDialog(new LayerBrushSettingsWindowViewModel(viewModel));
            }
            catch (Exception e)
            {
                _dialogService.ShowExceptionDialog("An exception occured while trying to show the brush's settings window", e);
            }
        }

        public void OpenEffectSettings()
        {
            var layerEffect = LayerPropertyGroup.LayerEffect;
            var configurationViewModel = layerEffect.ConfigurationDialog;
            if (configurationViewModel == null)
                return;

            try
            {
                // Limit to one constructor, there's no need to have more and it complicates things anyway
                var constructors = configurationViewModel.Type.GetConstructors();
                if (constructors.Length != 1)
                    throw new ArtemisUIException("Effect configuration dialogs must have exactly one constructor");

                var effectParameter = constructors.First().GetParameters().First(p => typeof(BaseLayerEffect).IsAssignableFrom(p.ParameterType));
                var argument = new ConstructorArgument(effectParameter.Name, layerEffect);
                var viewModel = (EffectConfigurationViewModel) layerEffect.PluginInfo.Kernel.Get(configurationViewModel.Type, argument);
                _windowManager.ShowDialog(new LayerEffectSettingsWindowViewModel(viewModel));
            }
            catch (Exception e)
            {
                _dialogService.ShowExceptionDialog("An exception occured while trying to show the effect's settings window", e);
                throw;
            }
        }

        public async void RenameEffect()
        {
            var result = await _dialogService.ShowDialogAt<RenameViewModel>(
                "PropertyTreeDialogHost",
                new Dictionary<string, object>
                {
                    {"subject", "effect"},
                    {"currentName", LayerPropertyGroup.LayerEffect.Name}
                }
            );
            if (result is string newName)
            {
                LayerPropertyGroup.LayerEffect.Name = newName;
                LayerPropertyGroup.LayerEffect.HasBeenRenamed = true;
                _profileEditorService.UpdateSelectedProfile();
            }
        }

        public void DeleteEffect()
        {
            if (LayerPropertyGroup.LayerEffect == null)
                return;

            LayerPropertyGroup.ProfileElement.RemoveLayerEffect(LayerPropertyGroup.LayerEffect);
            _profileEditorService.UpdateSelectedProfile();
        }

        public void EnableToggled()
        {
            _profileEditorService.UpdateSelectedProfile();
        }

        private void DetermineGroupType()
        {
            if (LayerPropertyGroup is LayerGeneralProperties)
                GroupType = LayerPropertyGroupType.General;
            else if (LayerPropertyGroup is LayerTransformProperties)
                GroupType = LayerPropertyGroupType.Transform;
            else if (LayerPropertyGroup.Parent == null && LayerPropertyGroup.LayerBrush != null)
                GroupType = LayerPropertyGroupType.LayerBrushRoot;
            else if (LayerPropertyGroup.Parent == null && LayerPropertyGroup.LayerEffect != null)
                GroupType = LayerPropertyGroupType.LayerEffectRoot;
            else
                GroupType = LayerPropertyGroupType.None;
        }

        public enum LayerPropertyGroupType
        {
            General,
            Transform,
            LayerBrushRoot,
            LayerEffectRoot,
            None
        }
    }
}