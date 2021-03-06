﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Artemis.UI.Shared.Screens.Dialogs;
using Artemis.UI.Shared.Screens.Exceptions;
using MaterialDesignThemes.Wpf;
using Ninject;
using Ninject.Parameters;
using Stylet;

namespace Artemis.UI.Shared.Services
{
    // TODO: Become plugin-aware and use plugin kernel if injected into a plugin
    internal class DialogService : IDialogService
    {
        private readonly IKernel _kernel;
        private readonly IViewManager _viewManager;
        private readonly IWindowManager _windowManager;

        public DialogService(IKernel kernel, IViewManager viewManager, IWindowManager windowManager)
        {
            _kernel = kernel;
            _viewManager = viewManager;
            _windowManager = windowManager;
        }

        public async Task<bool> ShowConfirmDialog(string header, string text, string confirmText = "Confirm", string cancelText = "Cancel")
        {
            var arguments = new IParameter[]
            {
                new ConstructorArgument("header", header),
                new ConstructorArgument("text", text),
                new ConstructorArgument("confirmText", confirmText.ToUpper()),
                new ConstructorArgument("cancelText", cancelText.ToUpper())
            };
            var result = await ShowDialog<ConfirmDialogViewModel>(arguments);
            return (bool) result;
        }

        public async Task<bool> ShowConfirmDialogAt(string identifier, string header, string text, string confirmText = "Confirm", string cancelText = "Cancel")
        {
            var arguments = new IParameter[]
            {
                new ConstructorArgument("header", header),
                new ConstructorArgument("text", text),
                new ConstructorArgument("confirmText", confirmText.ToUpper()),
                new ConstructorArgument("cancelText", cancelText.ToUpper())
            };
            var result = await ShowDialogAt<ConfirmDialogViewModel>(identifier, arguments);
            return (bool) result;
        }

        public async Task<object> ShowDialog<T>() where T : DialogViewModelBase
        {
            return await ShowDialog("RootDialog", _kernel.Get<T>());
        }

        public Task<object> ShowDialog<T>(Dictionary<string, object> parameters) where T : DialogViewModelBase
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var paramsArray = parameters.Select(kv => new ConstructorArgument(kv.Key, kv.Value)).Cast<IParameter>().ToArray();
            return ShowDialog<T>(paramsArray);
        }

        public async Task<object> ShowDialog<T>(IParameter[] parameters) where T : DialogViewModelBase
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            return await ShowDialog("RootDialog", _kernel.Get<T>(parameters));
        }

        public async Task<object> ShowDialogAt<T>(string identifier) where T : DialogViewModelBase
        {
            return await ShowDialog(identifier, _kernel.Get<T>());
        }

        public async Task<object> ShowDialogAt<T>(string identifier, Dictionary<string, object> parameters) where T : DialogViewModelBase
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var paramsArray = parameters.Select(kv => new ConstructorArgument(kv.Key, kv.Value)).Cast<IParameter>().ToArray();
            return await ShowDialogAt<T>(identifier, paramsArray);
        }

        public async Task<object> ShowDialogAt<T>(string identifier, IParameter[] parameters) where T : DialogViewModelBase
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            return await ShowDialog(identifier, _kernel.Get<T>(parameters));
        }

        public void ShowExceptionDialog(string message, Exception exception)
        {
            _windowManager.ShowDialog(new ExceptionViewModel(message, exception));
        }

        private async Task<object> ShowDialog(string identifier, DialogViewModelBase viewModel)
        {
            Task<object> result = null;
            await Execute.OnUIThreadAsync(() =>
            {
                var view = _viewManager.CreateViewForModel(viewModel);
                _viewManager.BindViewToModel(view, viewModel);

                if (identifier == null)
                    result = DialogHost.Show(view, viewModel.OnDialogOpened, viewModel.OnDialogClosed);
                else
                    result = DialogHost.Show(view, identifier, viewModel.OnDialogOpened, viewModel.OnDialogClosed);
            });

            return await result;
        }
    }
}