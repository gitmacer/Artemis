﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Artemis.Core.LayerBrushes;
using Artemis.Core.LayerEffects;
using Artemis.Storage.Entities.Profile;
using Artemis.Storage.Entities.Profile.Abstract;
using SkiaSharp;

namespace Artemis.Core
{
    /// <summary>
    ///     Represents a layer in a <see cref="Profile" />
    /// </summary>
    public sealed class Layer : RenderProfileElement
    {
        private LayerGeneralProperties _general;
        private SKBitmap _layerBitmap;
        private BaseLayerBrush _layerBrush;
        private LayerShape _layerShape;
        private List<ArtemisLed> _leds;
        private LayerTransformProperties _transform;

        /// <summary>
        ///     Creates a new instance of the <see cref="Layer" /> class and adds itself to the child collection of the provided
        ///     <paramref name="parent" />
        /// </summary>
        /// <param name="parent">The parent of the layer</param>
        /// <param name="name">The name of the layer</param>
        public Layer(ProfileElement parent, string name)
        {
            LayerEntity = new LayerEntity();
            EntityId = Guid.NewGuid();

            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            Profile = Parent.Profile;
            Name = name;
            Enabled = true;
            DisplayContinuously = true;
            General = new LayerGeneralProperties();
            Transform = new LayerTransformProperties();

            _layerEffects = new List<BaseLayerEffect>();
            _leds = new List<ArtemisLed>();
            _expandedPropertyGroups = new List<string>();

            Initialize();
            ApplyRenderElementDefaults();

            Parent.AddChild(this);
        }

        internal Layer(Profile profile, ProfileElement parent, LayerEntity layerEntity)
        {
            LayerEntity = layerEntity;
            Profile = profile;
            Parent = parent;
            General = new LayerGeneralProperties();
            Transform = new LayerTransformProperties();

            _layerEffects = new List<BaseLayerEffect>();
            _leds = new List<ArtemisLed>();
            _expandedPropertyGroups = new List<string>();

            Load();
            Initialize();
        }

        internal LayerEntity LayerEntity { get; set; }

        /// <inheritdoc />
        public override List<ILayerProperty> GetAllLayerProperties()
        {
            var result = new List<ILayerProperty>();
            result.AddRange(General.GetAllLayerProperties());
            result.AddRange(Transform.GetAllLayerProperties());
            if (LayerBrush?.BaseProperties != null) 
                result.AddRange(LayerBrush.BaseProperties.GetAllLayerProperties());
            foreach (var layerEffect in LayerEffects)
            {
                if (layerEffect.BaseProperties != null)
                    result.AddRange(layerEffect.BaseProperties.GetAllLayerProperties());
            }

            return result;
        }

        internal override RenderElementEntity RenderElementEntity => LayerEntity;

        /// <summary>
        ///     A collection of all the LEDs this layer is assigned to.
        /// </summary>
        public ReadOnlyCollection<ArtemisLed> Leds => _leds.AsReadOnly();

        /// <summary>
        ///     Defines the shape that is rendered by the <see cref="LayerBrush" />.
        /// </summary>
        public LayerShape LayerShape
        {
            get => _layerShape;
            set
            {
                SetAndNotify(ref _layerShape, value);
                if (Path != null)
                    CalculateRenderProperties();
            }
        }

        [PropertyGroupDescription(Name = "General", Description = "A collection of general properties")]
        public LayerGeneralProperties General
        {
            get => _general;
            set => SetAndNotify(ref _general, value);
        }

        [PropertyGroupDescription(Name = "Transform", Description = "A collection of transformation properties")]
        public LayerTransformProperties Transform
        {
            get => _transform;
            set => SetAndNotify(ref _transform, value);
        }

        /// <summary>
        ///     The brush that will fill the <see cref="LayerShape" />.
        /// </summary>
        public BaseLayerBrush LayerBrush
        {
            get => _layerBrush;
            internal set => SetAndNotify(ref _layerBrush, value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[Layer] {nameof(Name)}: {Name}, {nameof(Order)}: {Order}";
        }

        #region IDisposable

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            _disposed = true;

            // Brush first in case it depends on any of the other disposables during it's own disposal
            _layerBrush?.Dispose();

            _general?.Dispose();
            _layerBitmap?.Dispose();
            _transform?.Dispose();

            base.Dispose(disposing);
        }

        #endregion

        private void Initialize()
        {
            LayerBrushStore.LayerBrushAdded += LayerBrushStoreOnLayerBrushAdded;
            LayerBrushStore.LayerBrushRemoved += LayerBrushStoreOnLayerBrushRemoved;

            // Layers have two hardcoded property groups, instantiate them
            var generalAttribute = Attribute.GetCustomAttribute(
                GetType().GetProperty(nameof(General)),
                typeof(PropertyGroupDescriptionAttribute)
            );
            var transformAttribute = Attribute.GetCustomAttribute(
                GetType().GetProperty(nameof(Transform)),
                typeof(PropertyGroupDescriptionAttribute)
            );
            General.GroupDescription = (PropertyGroupDescriptionAttribute) generalAttribute;
            General.Initialize(this, "General.", Constants.CorePluginInfo);
            Transform.GroupDescription = (PropertyGroupDescriptionAttribute) transformAttribute;
            Transform.Initialize(this, "Transform.", Constants.CorePluginInfo);

            General.ShapeType.CurrentValueSet += ShapeTypeOnCurrentValueSet;
            ApplyShapeType();
            ActivateLayerBrush();
        }

        #region Storage

        internal override void Load()
        {
            EntityId = LayerEntity.Id;
            Name = LayerEntity.Name;
            Enabled = LayerEntity.Enabled;
            Order = LayerEntity.Order;

            _expandedPropertyGroups.AddRange(LayerEntity.ExpandedPropertyGroups);
            LoadRenderElement();
        }

        internal override void Save()
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            // Properties
            LayerEntity.Id = EntityId;
            LayerEntity.ParentId = Parent?.EntityId ?? new Guid();
            LayerEntity.Order = Order;
            LayerEntity.Enabled = Enabled;
            LayerEntity.Name = Name;
            LayerEntity.ProfileId = Profile.EntityId;
            LayerEntity.ExpandedPropertyGroups.Clear();
            LayerEntity.ExpandedPropertyGroups.AddRange(_expandedPropertyGroups);

            General.ApplyToEntity();
            Transform.ApplyToEntity();
            LayerBrush?.BaseProperties.ApplyToEntity();

            // LEDs
            LayerEntity.Leds.Clear();
            foreach (var artemisLed in Leds)
            {
                var ledEntity = new LedEntity
                {
                    DeviceIdentifier = artemisLed.Device.RgbDevice.GetDeviceIdentifier(),
                    LedName = artemisLed.RgbLed.Id.ToString()
                };
                LayerEntity.Leds.Add(ledEntity);
            }

            SaveRenderElement();
        }

        #endregion

        #region Shape management

        private void ShapeTypeOnCurrentValueSet(object sender, EventArgs e)
        {
            ApplyShapeType();
        }

        private void ApplyShapeType()
        {
            switch (General.ShapeType.CurrentValue)
            {
                case LayerShapeType.Ellipse:
                    LayerShape = new EllipseShape(this);
                    break;
                case LayerShapeType.Rectangle:
                    LayerShape = new RectangleShape(this);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion

        #region Rendering

        /// <inheritdoc />
        public override void Update(double deltaTime)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            if (!Enabled || LayerBrush?.BaseProperties == null || !LayerBrush.BaseProperties.PropertiesInitialized)
                return;

            // Ensure the layer must still be displayed
            UpdateDisplayCondition();

            // Update the layer timeline, this will give us a new delta time which could be negative in case the main segment wrapped back
            // to it's start
            UpdateTimeline(deltaTime);

            // No point updating further than this if the layer is not going to be rendered
            if (TimelinePosition > TimelineLength)
                return;

            General.Update(deltaTime);
            Transform.Update(deltaTime);
            LayerBrush.BaseProperties?.Update(deltaTime);
            LayerBrush.Update(deltaTime);

            foreach (var baseLayerEffect in LayerEffects.Where(e => e.Enabled))
            {
                baseLayerEffect.BaseProperties?.Update(deltaTime);
                baseLayerEffect.Update(deltaTime);
            }
        }

        protected internal override void UpdateTimelineLength()
        {
            TimelineLength = StartSegmentLength + MainSegmentLength + EndSegmentLength;
        }

        public override void OverrideProgress(TimeSpan timeOverride, bool stickToMainSegment)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            if (!Enabled || LayerBrush?.BaseProperties == null || !LayerBrush.BaseProperties.PropertiesInitialized)
                return;

            var beginTime = TimelinePosition;

            if (stickToMainSegment)
            {
                if (!DisplayContinuously)
                    TimelinePosition = StartSegmentLength + timeOverride;
                else
                {
                    var progress = timeOverride.TotalMilliseconds % MainSegmentLength.TotalMilliseconds;
                    if (progress > 0)
                        TimelinePosition = TimeSpan.FromMilliseconds(progress) + StartSegmentLength;
                    else
                        TimelinePosition = StartSegmentLength;
                }
            }
            else
                TimelinePosition = timeOverride;

            var delta = (TimelinePosition - beginTime).TotalSeconds;

            General.Update(delta);
            Transform.Update(delta);
            LayerBrush.BaseProperties?.Update(delta);
            LayerBrush.Update(delta);

            foreach (var baseLayerEffect in LayerEffects.Where(e => e.Enabled))
            {
                baseLayerEffect.BaseProperties?.Update(delta);
                baseLayerEffect.Update(delta);
            }
        }

        /// <inheritdoc />
        public override void Render(double deltaTime, SKCanvas canvas, SKImageInfo canvasInfo)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            if (!Enabled || TimelinePosition > TimelineLength)
                return;

            // Ensure the layer is ready
            if (Path == null || LayerShape?.Path == null || !General.PropertiesInitialized || !Transform.PropertiesInitialized)
                return;
            // Ensure the brush is ready
            if (LayerBrush?.BaseProperties?.PropertiesInitialized == false || LayerBrush?.BrushType != LayerBrushType.Regular)
                return;

            if (_layerBitmap == null)
                _layerBitmap = new SKBitmap(new SKImageInfo((int) Path.Bounds.Width, (int) Path.Bounds.Height));
            else if (_layerBitmap.Info.Width != (int) Path.Bounds.Width || _layerBitmap.Info.Height != (int) Path.Bounds.Height)
            {
                _layerBitmap.Dispose();
                _layerBitmap = new SKBitmap(new SKImageInfo((int) Path.Bounds.Width, (int) Path.Bounds.Height));
            }

            using var layerPath = new SKPath(Path);
            using var layerCanvas = new SKCanvas(_layerBitmap);
            using var layerPaint = new SKPaint
            {
                FilterQuality = SKFilterQuality.Low,
                Color = new SKColor(0, 0, 0, (byte) (Transform.Opacity.CurrentValue * 2.55f))
            };
            layerCanvas.Clear();

            layerPath.Transform(SKMatrix.MakeTranslation(layerPath.Bounds.Left * -1, layerPath.Bounds.Top * -1));

            foreach (var baseLayerEffect in LayerEffects.Where(e => e.Enabled))
                baseLayerEffect.PreProcess(layerCanvas, _layerBitmap.Info, layerPath, layerPaint);

            // No point rendering if the alpha was set to zero by one of the effects
            if (layerPaint.Color.Alpha == 0)
                return;
            
            if (!LayerBrush.SupportsTransformation)
                SimpleRender(layerCanvas, _layerBitmap.Info, layerPaint, layerPath);
            else if (General.ResizeMode.CurrentValue == LayerResizeMode.Normal)
                StretchRender(layerCanvas, _layerBitmap.Info, layerPaint, layerPath);
            else if (General.ResizeMode.CurrentValue == LayerResizeMode.Clip)
                ClipRender(layerCanvas, _layerBitmap.Info, layerPaint, layerPath);

            foreach (var baseLayerEffect in LayerEffects.Where(e => e.Enabled))
                baseLayerEffect.PostProcess(layerCanvas, _layerBitmap.Info, layerPath, layerPaint);

            var targetLocation = new SKPoint(0, 0);
            if (Parent is Folder parentFolder)
                targetLocation = Path.Bounds.Location - parentFolder.Path.Bounds.Location;

            using var canvasPaint = new SKPaint {BlendMode = General.BlendMode.CurrentValue};
            using var canvasPath = new SKPath(Path);
            canvasPath.Transform(SKMatrix.MakeTranslation(
                (canvasPath.Bounds.Left - targetLocation.X) * -1,
                (canvasPath.Bounds.Top - targetLocation.Y) * -1)
            );
            canvas.ClipPath(canvasPath);
            canvas.DrawBitmap(_layerBitmap, targetLocation, canvasPaint);
        }

        private void SimpleRender(SKCanvas canvas, SKImageInfo canvasInfo, SKPaint paint, SKPath layerPath)
        {
            using var renderPath = new SKPath(LayerShape.Path);
            LayerBrush.InternalRender(canvas, canvasInfo, renderPath, paint);
        }

        private void StretchRender(SKCanvas canvas, SKImageInfo canvasInfo, SKPaint paint, SKPath layerPath)
        {
            // Apply transformations
            var sizeProperty = Transform.Scale.CurrentValue;
            var rotationProperty = Transform.Rotation.CurrentValue;

            var anchorPosition = GetLayerAnchorPosition(layerPath);
            var anchorProperty = Transform.AnchorPoint.CurrentValue;

            // Translation originates from the unscaled center of the shape and is tied to the anchor
            var x = anchorPosition.X - layerPath.Bounds.MidX - anchorProperty.X * layerPath.Bounds.Width;
            var y = anchorPosition.Y - layerPath.Bounds.MidY - anchorProperty.Y * layerPath.Bounds.Height;

            // Apply these before translation because anchorPosition takes translation into account
            canvas.RotateDegrees(rotationProperty, anchorPosition.X, anchorPosition.Y);
            canvas.Scale(sizeProperty.Width / 100f, sizeProperty.Height / 100f, anchorPosition.X, anchorPosition.Y);
            canvas.Translate(x, y);

            using var renderPath = new SKPath(LayerShape.Path);
            LayerBrush.InternalRender(canvas, canvasInfo, renderPath, paint);
        }

        private void ClipRender(SKCanvas canvas, SKImageInfo canvasInfo, SKPaint paint, SKPath layerPath)
        {
            // Apply transformation
            var sizeProperty = Transform.Scale.CurrentValue;
            var rotationProperty = Transform.Rotation.CurrentValue;

            var anchorPosition = GetLayerAnchorPosition(layerPath);
            var anchorProperty = Transform.AnchorPoint.CurrentValue;

            // Translation originates from the unscaled center of the shape and is tied to the anchor
            var x = anchorPosition.X - layerPath.Bounds.MidX - anchorProperty.X * layerPath.Bounds.Width;
            var y = anchorPosition.Y - layerPath.Bounds.MidY - anchorProperty.Y * layerPath.Bounds.Height;

            using var clipPath = new SKPath(LayerShape.Path);
            clipPath.Transform(SKMatrix.MakeTranslation(x, y));
            clipPath.Transform(SKMatrix.MakeScale(sizeProperty.Width / 100f, sizeProperty.Height / 100f, anchorPosition.X, anchorPosition.Y));
            clipPath.Transform(SKMatrix.MakeRotationDegrees(rotationProperty, anchorPosition.X, anchorPosition.Y));
            canvas.ClipPath(clipPath);

            canvas.RotateDegrees(rotationProperty, anchorPosition.X, anchorPosition.Y);
            canvas.Translate(x, y);

            // Render the layer in the largest required bounds, this still creates stretching in some situations
            // but the only alternative I see right now is always forcing brushes to render on the entire canvas
            var boundsRect = new SKRect(
                Math.Min(clipPath.Bounds.Left - x, Bounds.Left - x),
                Math.Min(clipPath.Bounds.Top - y, Bounds.Top - y),
                Math.Max(clipPath.Bounds.Right - x, Bounds.Right - x),
                Math.Max(clipPath.Bounds.Bottom - y, Bounds.Bottom - y)
            );
            using var renderPath = new SKPath();
            renderPath.AddRect(boundsRect);

            LayerBrush.InternalRender(canvas, canvasInfo, renderPath, paint);
        }

        internal void CalculateRenderProperties()
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            if (!Leds.Any())
                Path = new SKPath();
            else
            {
                var path = new SKPath {FillType = SKPathFillType.Winding};
                foreach (var artemisLed in Leds)
                    path.AddRect(artemisLed.AbsoluteRenderRectangle);

                Path = path;
            }

            // This is called here so that the shape's render properties are up to date when other code
            // responds to OnRenderPropertiesUpdated
            LayerShape?.CalculateRenderProperties();

            // Folder render properties are based on child paths and thus require an update
            if (Parent is Folder folder)
                folder.CalculateRenderProperties();

            OnRenderPropertiesUpdated();
        }

        internal SKPoint GetLayerAnchorPosition(SKPath layerPath, bool zeroBased = false)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            var positionProperty = Transform.Position.CurrentValue;

            // Start at the center of the shape
            var position = zeroBased
                ? new SKPoint(layerPath.Bounds.MidX - layerPath.Bounds.Left, layerPath.Bounds.MidY - layerPath.Bounds.Top)
                : new SKPoint(layerPath.Bounds.MidX, layerPath.Bounds.MidY);

            // Apply translation
            position.X += positionProperty.X * layerPath.Bounds.Width;
            position.Y += positionProperty.Y * layerPath.Bounds.Height;

            return position;
        }

        /// <summary>
        ///     Excludes the provided path from the translations applied to the layer by applying translations that cancel the
        ///     layer translations out
        /// </summary>
        /// <param name="path"></param>
        public void IncludePathInTranslation(SKPath path, bool zeroBased)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            var sizeProperty = Transform.Scale.CurrentValue;
            var rotationProperty = Transform.Rotation.CurrentValue;

            var anchorPosition = GetLayerAnchorPosition(Path, zeroBased);
            var anchorProperty = Transform.AnchorPoint.CurrentValue;

            // Translation originates from the unscaled center of the shape and is tied to the anchor
            var x = anchorPosition.X - (zeroBased ? Bounds.MidX - Bounds.Left : Bounds.MidX) - anchorProperty.X * Bounds.Width;
            var y = anchorPosition.Y - (zeroBased ? Bounds.MidY - Bounds.Top : Bounds.MidY) - anchorProperty.Y * Bounds.Height;

            if (General.ResizeMode == LayerResizeMode.Normal)
            {
                path.Transform(SKMatrix.MakeTranslation(x, y));
                path.Transform(SKMatrix.MakeScale(sizeProperty.Width / 100f, sizeProperty.Height / 100f, anchorPosition.X, anchorPosition.Y));
                path.Transform(SKMatrix.MakeRotationDegrees(rotationProperty, anchorPosition.X, anchorPosition.Y));
            }
            else
            {
                path.Transform(SKMatrix.MakeTranslation(x, y));
                path.Transform(SKMatrix.MakeRotationDegrees(rotationProperty * -1, anchorPosition.X, anchorPosition.Y));
            }
        }

        /// <summary>
        ///     Excludes the provided path from the translations applied to the layer by applying translations that cancel the
        ///     layer translations out
        /// </summary>
        public void ExcludePathFromTranslation(SKPath path, bool zeroBased)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            var sizeProperty = Transform.Scale.CurrentValue;
            var rotationProperty = Transform.Rotation.CurrentValue;

            var anchorPosition = GetLayerAnchorPosition(Path, zeroBased);
            var anchorProperty = Transform.AnchorPoint.CurrentValue;

            // Translation originates from the unscaled center of the shape and is tied to the anchor
            var x = anchorPosition.X - (zeroBased ? Bounds.MidX - Bounds.Left : Bounds.MidX) - anchorProperty.X * Bounds.Width;
            var y = anchorPosition.Y - (zeroBased ? Bounds.MidY - Bounds.Top : Bounds.MidY) - anchorProperty.Y * Bounds.Height;

            var reversedXScale = 1f / (sizeProperty.Width / 100f);
            var reversedYScale = 1f / (sizeProperty.Height / 100f);

            if (General.ResizeMode == LayerResizeMode.Normal)
            {
                path.Transform(SKMatrix.MakeRotationDegrees(rotationProperty * -1, anchorPosition.X, anchorPosition.Y));
                path.Transform(SKMatrix.MakeScale(reversedXScale, reversedYScale, anchorPosition.X, anchorPosition.Y));
                path.Transform(SKMatrix.MakeTranslation(x * -1, y * -1));
            }
            else
            {
                path.Transform(SKMatrix.MakeRotationDegrees(rotationProperty * -1, anchorPosition.X, anchorPosition.Y));
                path.Transform(SKMatrix.MakeTranslation(x * -1, y * -1));
            }
        }

        /// <summary>
        ///     Excludes the provided canvas from the translations applied to the layer by applying translations that cancel the
        ///     layer translations out
        /// </summary>
        /// <returns>The number of transformations applied</returns>
        public int ExcludeCanvasFromTranslation(SKCanvas canvas, bool zeroBased)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            var sizeProperty = Transform.Scale.CurrentValue;
            var rotationProperty = Transform.Rotation.CurrentValue;

            var anchorPosition = GetLayerAnchorPosition(Path, zeroBased);
            var anchorProperty = Transform.AnchorPoint.CurrentValue;

            // Translation originates from the unscaled center of the shape and is tied to the anchor
            var x = anchorPosition.X - (zeroBased ? Bounds.MidX - Bounds.Left : Bounds.MidX) - anchorProperty.X * Bounds.Width;
            var y = anchorPosition.Y - (zeroBased ? Bounds.MidY - Bounds.Top : Bounds.MidY) - anchorProperty.Y * Bounds.Height;

            var reversedXScale = 1f / (sizeProperty.Width / 100f);
            var reversedYScale = 1f / (sizeProperty.Height / 100f);

            if (General.ResizeMode == LayerResizeMode.Normal)
            {
                canvas.Translate(x * -1, y * -1);
                canvas.Scale(reversedXScale, reversedYScale, anchorPosition.X, anchorPosition.Y);
                canvas.RotateDegrees(rotationProperty * -1, anchorPosition.X, anchorPosition.Y);

                return 3;
            }

            canvas.RotateDegrees(rotationProperty * -1, anchorPosition.X, anchorPosition.Y);
            canvas.Translate(x * -1, y * -1);
            return 2;
        }

        #endregion

        #region LED management

        /// <summary>
        ///     Adds a new <see cref="ArtemisLed" /> to the layer and updates the render properties.
        /// </summary>
        /// <param name="led">The LED to add</param>
        public void AddLed(ArtemisLed led)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            _leds.Add(led);
            CalculateRenderProperties();
        }

        /// <summary>
        ///     Adds a collection of new <see cref="ArtemisLed" />s to the layer and updates the render properties.
        /// </summary>
        /// <param name="leds">The LEDs to add</param>
        public void AddLeds(IEnumerable<ArtemisLed> leds)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            _leds.AddRange(leds);
            CalculateRenderProperties();
        }

        /// <summary>
        ///     Removes a <see cref="ArtemisLed" /> from the layer and updates the render properties.
        /// </summary>
        /// <param name="led">The LED to remove</param>
        public void RemoveLed(ArtemisLed led)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            _leds.Remove(led);
            CalculateRenderProperties();
        }

        /// <summary>
        ///     Removes all <see cref="ArtemisLed" />s from the layer and updates the render properties.
        /// </summary>
        public void ClearLeds()
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            _leds.Clear();
            CalculateRenderProperties();
        }

        internal void PopulateLeds(ArtemisSurface surface)
        {
            if (_disposed)
                throw new ObjectDisposedException("Layer");

            var leds = new List<ArtemisLed>();

            // Get the surface LEDs for this layer
            var availableLeds = surface.Devices.SelectMany(d => d.Leds).ToList();
            foreach (var ledEntity in LayerEntity.Leds)
            {
                var match = availableLeds.FirstOrDefault(a => a.Device.RgbDevice.GetDeviceIdentifier() == ledEntity.DeviceIdentifier &&
                                                              a.RgbLed.Id.ToString() == ledEntity.LedName);
                if (match != null)
                    leds.Add(match);
            }

            _leds = leds;
            CalculateRenderProperties();
        }

        #endregion

        #region Brush management

        /// <summary>
        ///     Changes the current layer brush to the brush described in the provided <paramref name="descriptor" />
        /// </summary>
        public void ChangeLayerBrush(LayerBrushDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            if (LayerBrush != null)
            {
                var brush = LayerBrush;
                LayerBrush = null;
                brush.Dispose();
            }

            // Ensure the brush reference matches the brush
            var current = General.BrushReference.BaseValue;
            if (!descriptor.MatchesLayerBrushReference(current))
                General.BrushReference.BaseValue = new LayerBrushReference(descriptor);

            ActivateLayerBrush();
        }

        /// <summary>
        ///     Removes the current layer brush from the layer
        /// </summary>
        public void RemoveLayerBrush()
        {
            if (LayerBrush == null)
                return;

            var brush = LayerBrush;
            DeactivateLayerBrush();
            LayerEntity.PropertyEntities.RemoveAll(p => p.PluginGuid == brush.PluginInfo.Guid && p.Path.StartsWith("LayerBrush."));
        }

        internal void ActivateLayerBrush()
        {
            var current = General.BrushReference.CurrentValue;
            if (current == null)
                return;

            var descriptor = LayerBrushStore.Get(current.BrushPluginGuid, current.BrushType)?.LayerBrushDescriptor;
            descriptor?.CreateInstance(this);

            OnLayerBrushUpdated();
        }

        internal void DeactivateLayerBrush()
        {
            if (LayerBrush == null)
                return;

            var brush = LayerBrush;
            LayerBrush = null;
            brush.Dispose();

            OnLayerBrushUpdated();
        }

        #endregion

        #region Event handlers

        private void LayerBrushStoreOnLayerBrushRemoved(object sender, LayerBrushStoreEvent e)
        {
            if (LayerBrush?.Descriptor == e.Registration.LayerBrushDescriptor)
                DeactivateLayerBrush();
        }

        private void LayerBrushStoreOnLayerBrushAdded(object sender, LayerBrushStoreEvent e)
        {
            if (LayerBrush != null || General.BrushReference?.CurrentValue == null)
                return;

            var current = General.BrushReference.CurrentValue;
            if (e.Registration.Plugin.PluginInfo.Guid == current.BrushPluginGuid &&
                e.Registration.LayerBrushDescriptor.LayerBrushType.Name == current.BrushType)
                ActivateLayerBrush();
        }

        #endregion

        #region Events

        public event EventHandler RenderPropertiesUpdated;
        public event EventHandler LayerBrushUpdated;

        private void OnRenderPropertiesUpdated()
        {
            RenderPropertiesUpdated?.Invoke(this, EventArgs.Empty);
        }

        internal void OnLayerBrushUpdated()
        {
            LayerBrushUpdated?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }

    public enum LayerShapeType
    {
        Ellipse,
        Rectangle
    }

    public enum LayerResizeMode
    {
        Normal,
        Clip
    }
}