﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Artemis.Core;
using Artemis.Core.Services;
using Artemis.UI.Exceptions;
using Artemis.UI.Ninject.Factories;
using Artemis.UI.Screens.ProfileEditor.Dialogs;
using Artemis.UI.Shared.Services;
using Stylet;

namespace Artemis.UI.Screens.ProfileEditor.ProfileTree.TreeItem
{
    public abstract class TreeItemViewModel : Conductor<TreeItemViewModel>.Collection.AllActive, IDisposable
    {
        private readonly IDialogService _dialogService;
        private readonly IProfileEditorService _profileEditorService;
        private readonly IProfileTreeVmFactory _profileTreeVmFactory;
        private readonly ILayerBrushService _layerBrushService;
        private readonly ISurfaceService _surfaceService;
        private ProfileElement _profileElement;

        protected TreeItemViewModel(ProfileElement profileElement,
            IProfileEditorService profileEditorService,
            IDialogService dialogService,
            IProfileTreeVmFactory profileTreeVmFactory,
            ILayerBrushService layerBrushService,
            ISurfaceService surfaceService)
        {
            _profileEditorService = profileEditorService;
            _dialogService = dialogService;
            _profileTreeVmFactory = profileTreeVmFactory;
            _layerBrushService = layerBrushService;
            _surfaceService = surfaceService;

            ProfileElement = profileElement;

            Subscribe();
            UpdateProfileElements();
        }

        public ProfileElement ProfileElement
        {
            get => _profileElement;
            set => SetAndNotify(ref _profileElement, value);
        }

        public abstract bool SupportsChildren { get; }

        public void Dispose()
        {
            Unsubscribe();
        }

        public List<TreeItemViewModel> GetAllChildren()
        {
            var children = new List<TreeItemViewModel>();
            foreach (var childFolder in Items)
            {
                // Add all children in this element
                children.Add(childFolder);
                // Add all children of children inside this element
                children.AddRange(childFolder.GetAllChildren());
            }

            return children;
        }

        public void SetElementInFront(TreeItemViewModel source)
        {
            var sourceParent = (TreeItemViewModel) source.Parent;
            var parent = (TreeItemViewModel) Parent;

            // If the parents are different, remove the element from the old parent and add it to the new parent
            if (source.Parent != Parent)
            {
                sourceParent.RemoveExistingElement(source);
                parent.AddExistingElement(source);
            }

            parent.Unsubscribe();
            parent.ProfileElement.RemoveChild(source.ProfileElement);
            parent.ProfileElement.AddChild(source.ProfileElement, ProfileElement.Order);
            parent.Subscribe();

            parent.UpdateProfileElements();
        }

        public void SetElementBehind(TreeItemViewModel source)
        {
            var sourceParent = (TreeItemViewModel) source.Parent;
            var parent = (TreeItemViewModel) Parent;
            if (source.Parent != Parent)
            {
                sourceParent.RemoveExistingElement(source);
                parent.AddExistingElement(source);
            }

            parent.Unsubscribe();
            parent.ProfileElement.RemoveChild(source.ProfileElement);
            parent.ProfileElement.AddChild(source.ProfileElement, ProfileElement.Order + 1);
            parent.Subscribe();

            parent.UpdateProfileElements();
        }

        public void RemoveExistingElement(TreeItemViewModel treeItem)
        {
            if (!SupportsChildren)
                throw new ArtemisUIException("Cannot remove a child from a profile element of type " + ProfileElement.GetType().Name);

            ProfileElement.RemoveChild(treeItem.ProfileElement);
            treeItem.Parent = null;
            treeItem.Dispose();
        }

        public void AddExistingElement(TreeItemViewModel treeItem)
        {
            if (!SupportsChildren)
                throw new ArtemisUIException("Cannot add a child to a profile element of type " + ProfileElement.GetType().Name);

            ProfileElement.AddChild(treeItem.ProfileElement);
            treeItem.Parent = this;
        }

        public void AddFolder()
        {
            if (!SupportsChildren)
                throw new ArtemisUIException("Cannot add a folder to a profile element of type " + ProfileElement.GetType().Name);

            var _ = new Folder(ProfileElement, "New folder");
            _profileEditorService.UpdateSelectedProfile();
        }

        public void AddLayer()
        {
            if (!SupportsChildren)
                throw new ArtemisUIException("Cannot add a layer to a profile element of type " + ProfileElement.GetType().Name);

            var layer = new Layer(ProfileElement, "New layer");
            layer.ChangeLayerBrush(_layerBrushService.GetDefaultLayerBrush());
            layer.AddLeds(_surfaceService.ActiveSurface.Devices.SelectMany(d => d.Leds));
            _profileEditorService.UpdateSelectedProfile();
            _profileEditorService.ChangeSelectedProfileElement(layer);
        }

        // ReSharper disable once UnusedMember.Global - Called from view
        public async Task RenameElement()
        {
            var result = await _dialogService.ShowDialogAt<RenameViewModel>(
                "ProfileTreeDialog",
                new Dictionary<string, object>
                {
                    {"subject", ProfileElement is Folder ? "folder" : "layer"},
                    {"currentName", ProfileElement.Name}
                }
            );
            if (result is string newName)
            {
                ProfileElement.Name = newName;
                _profileEditorService.UpdateSelectedProfile();
            }
        }

        // ReSharper disable once UnusedMember.Global - Called from view
        public async Task DeleteElement()
        {
            var result = await _dialogService.ShowConfirmDialogAt(
                "ProfileTreeDialog",
                "Delete profile element",
                "Are you sure?"
            );

            if (!result)
                return;

            // Farewell, cruel world
            var parent = (TreeItemViewModel) Parent;
            ProfileElement.Parent?.RemoveChild(ProfileElement);
            parent.RemoveExistingElement(this);

            _profileEditorService.UpdateSelectedProfile();
            _profileEditorService.ChangeSelectedProfileElement(null);
        }

        public void UpdateProfileElements()
        {
            // Remove VMs that are no longer a child
            var toRemove = Items.Where(c => c.ProfileElement.Parent != ProfileElement).ToList();
            foreach (var treeItemViewModel in toRemove)
                Items.Remove(treeItemViewModel);

            // Order the children
            var vmsList = Items.OrderBy(v => v.ProfileElement.Order).ToList();
            for (var index = 0; index < vmsList.Count; index++)
            {
                var profileElementViewModel = vmsList[index];
                if (Items.IndexOf(profileElementViewModel) != index)
                    ((BindableCollection<TreeItemViewModel>) Items).Move(Items.IndexOf(profileElementViewModel), index);
            }

            // Ensure every child element has an up-to-date VM
            if (ProfileElement.Children == null)
                return;

            var newChildren = new List<TreeItemViewModel>();
            foreach (var profileElement in ProfileElement.Children.OrderBy(c => c.Order))
            {
                if (profileElement is Folder folder)
                {
                    if (Items.FirstOrDefault(p => p is FolderViewModel vm && vm.ProfileElement == folder) == null)
                        newChildren.Add(_profileTreeVmFactory.FolderViewModel(folder));
                }
                else if (profileElement is Layer layer)
                {
                    if (Items.FirstOrDefault(p => p is LayerViewModel vm && vm.ProfileElement == layer) == null)
                        newChildren.Add(_profileTreeVmFactory.LayerViewModel(layer));
                }
            }

            if (!newChildren.Any())
                return;

            // Add the new children in one call, prevent extra UI events
            foreach (var treeItemViewModel in newChildren)
            {
                treeItemViewModel.UpdateProfileElements();
                Items.Add(treeItemViewModel);
            }
        }

        public void EnableToggled()
        {
            _profileEditorService.UpdateSelectedProfile();
        }

        private void Subscribe()
        {
            ProfileElement.ChildAdded += ProfileElementOnChildAdded;
            ProfileElement.ChildRemoved += ProfileElementOnChildRemoved;
        }

        private void Unsubscribe()
        {
            ProfileElement.ChildAdded -= ProfileElementOnChildAdded;
            ProfileElement.ChildRemoved -= ProfileElementOnChildRemoved;
        }

        private void ProfileElementOnChildRemoved(object sender, EventArgs e)
        {
            UpdateProfileElements();
        }

        private void ProfileElementOnChildAdded(object sender, EventArgs e)
        {
            UpdateProfileElements();
        }
    }
}