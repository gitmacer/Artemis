﻿using System;
using System.Diagnostics;
using Stylet;

namespace Artemis.UI.Screens.Home
{
    public class HomeViewModel : Screen, IMainScreenViewModel
    {
        public HomeViewModel()
        {
            DisplayName = "Home";
        }

        public void OpenUrl(string url)
        {
            // Don't open anything but valid URIs
            if (Uri.IsWellFormedUriString(url, UriKind.RelativeOrAbsolute))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") {CreateNoWindow = true});
            }
        }
    }
}