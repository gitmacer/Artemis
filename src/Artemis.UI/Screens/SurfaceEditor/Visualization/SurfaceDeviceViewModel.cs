﻿using System;
using System.Windows;
using System.Windows.Input;
using Artemis.Core;
using Stylet;

namespace Artemis.UI.Screens.SurfaceEditor.Visualization
{
    public class SurfaceDeviceViewModel : PropertyChangedBase
    {
        private Cursor _cursor;
        private ArtemisDevice _device;
        private double _dragOffsetX;
        private double _dragOffsetY;
        private SelectionStatus _selectionStatus;

        public SurfaceDeviceViewModel(ArtemisDevice device)
        {
            Device = device;
        }

        public ArtemisDevice Device
        {
            get => _device;
            set
            {
                if (SetAndNotify(ref _device, value)) return;
                NotifyOfPropertyChange(nameof(DeviceRectangle));
            }
        }

        public SelectionStatus SelectionStatus
        {
            get => _selectionStatus;
            set => SetAndNotify(ref _selectionStatus, value);
        }

        public Cursor Cursor
        {
            get => _cursor;
            set => SetAndNotify(ref _cursor, value);
        }

        public Rect DeviceRectangle => Device.RgbDevice == null
            ? new Rect()
            : new Rect(Device.X, Device.Y, Device.RgbDevice.DeviceRectangle.Size.Width, Device.RgbDevice.DeviceRectangle.Size.Height);

        public void StartMouseDrag(Point mouseStartPosition)
        {
            _dragOffsetX = Device.X - mouseStartPosition.X;
            _dragOffsetY = Device.Y - mouseStartPosition.Y;
        }

        public void UpdateMouseDrag(Point mousePosition)
        {
            var roundedX = Math.Round((mousePosition.X + _dragOffsetX) / 10, 0, MidpointRounding.AwayFromZero) * 10;
            var roundedY = Math.Round((mousePosition.Y + _dragOffsetY) / 10, 0, MidpointRounding.AwayFromZero) * 10;
            Device.X = Math.Max(0, roundedX);
            Device.Y = Math.Max(0, roundedY);
        }

        // ReSharper disable once UnusedMember.Global - Called from view
        public void MouseEnter(object sender, MouseEventArgs e)
        {
            if (SelectionStatus == SelectionStatus.None)
            {
                SelectionStatus = SelectionStatus.Hover;
                Cursor = Cursors.Hand;
            }
        }

        // ReSharper disable once UnusedMember.Global - Called from view
        public void MouseLeave()
        {
            if (SelectionStatus == SelectionStatus.Hover)
            {
                SelectionStatus = SelectionStatus.None;
                Cursor = Cursors.Arrow;
            }
        }

        public MouseDevicePosition GetMouseDevicePosition(Point position)
        {
            if ((new Point(0, 0) - position).LengthSquared < 5) return MouseDevicePosition.TopLeft;

            return MouseDevicePosition.Regular;
        }
    }

    public enum MouseDevicePosition
    {
        Regular,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public enum SelectionStatus
    {
        None,
        Hover,
        Selected
    }
}