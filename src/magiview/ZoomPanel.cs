// (not in pbrt-v4) ZoomPanel.cs
//
// A panel that shows its child (the film image) zoomed and panned, as the image window of Adobe Photoshop does.
//
//   * The zoom is a percentage of the actual pixels (100%: one pixel of the image on one pixel of the screen) and goes through
//     Photoshop's zoom levels, between MinZoomFactor and MaxZoomFactor.
//   * ZoomMode says what the left button does: Move pans (a double click fits), Zoom zooms in a level about the click (out with
//     Alt), Rectangle draws a marquee and zooms to it. The middle button, and the left one with the space bar held, pan in
//     every mode. The wheel zooms about the cursor. Two fingers pan and pinch.
//   * A pan that is let go while moving keeps its velocity and slows down (IsInertial, Friction): with the mouse and with
//     touch.
//   * A change of zoom is animated (IsAnimated, AnimationSeconds) about the point that stays where it is.
//   * "Fit" keeps the whole image in the panel as the panel or the image changes size, until another zoom or a pan.

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace magiview
{
    // ZoomPanelMode Definition
    public enum ZoomPanelMode
    {
        Move,
        Zoom,
        Rectangle
    }

    // ZoomPanel Definition
    public sealed class ZoomPanel : Border
    {
        // ZoomPanel Public Methods
        public ZoomPanel()
        {
            ClipToBounds = true;
            IsManipulationEnabled = true;
            transform = new MatrixTransform();
            overlay = new DrawingVisual();
            AddVisualChild(overlay);
            clock = Stopwatch.StartNew();
            zoom = 1;
            targetZoom = 1;
            fitted = true;
            ZoomMode = ZoomPanelMode.Move;
            IsAnimated = true;
            AnimationSeconds = 0.2;
            IsInertial = true;
            Friction = 4;
            MinZoomFactor = levels[0];
            MaxZoomFactor = levels[levels.Length - 1];
        }

        // The zoom levels of Photoshop (1: 100%)
        public static int LevelCount
        {
            get { return levels.Length; }
        }

        public static double Level(int index)
        {
            return levels[index];
        }

        // What the left button does
        public ZoomPanelMode ZoomMode { get; set; }

        // A change of zoom takes AnimationSeconds
        public bool IsAnimated { get; set; }

        public double AnimationSeconds { get; set; }

        // A pan let go while moving goes on and loses its velocity at the rate Friction (per second)
        public bool IsInertial { get; set; }

        public double Friction { get; set; }

        // The least and the largest zoom (the fit may be less than the least)
        public double MinZoomFactor { get; set; }

        public double MaxZoomFactor { get; set; }

        // The zoom: 1 is 100%, a pixel of the image on a pixel of the screen
        public double ZoomFactor
        {
            get { return zoom; }
        }

        // Raised when the zoom has changed
        public event EventHandler ZoomFactorChanged;

        // The whole image in the panel; stays so when the panel or the image changes size, until another zoom or a pan
        public void ZoomToFit()
        {
            fitted = true;
            Fit(true);
        }

        // The zoom about the middle of the panel
        public void ZoomTo(double factor)
        {
            ZoomAbout(new Point(0.5 * viewSize.Width, 0.5 * viewSize.Height), factor);
        }

        public void Zoom100Percent()
        {
            ZoomTo(1);
        }

        // The next zoom level above and below the zoom
        public void ZoomIn()
        {
            ZoomTo(LevelAbove());
        }

        public void ZoomOut()
        {
            ZoomTo(LevelBelow());
        }

        // The image moved by the distance (in the units of the panel)
        public void MoveBy(double dx, double dy)
        {
            fitted = false;
            GoTo(targetZoom, targetX + dx, targetY + dy, true);
        }

        // The part of the image (in the units of the image) as large as the panel has room for, in its middle
        public void SetViewbox(Rect part)
        {
            if (part.Width > 0 && part.Height > 0 && viewSize.Width > 0 && viewSize.Height > 0)
            {
                double factor = Math.Min(MaxZoomFactor, Math.Min(viewSize.Width / part.Width, viewSize.Height / part.Height) * ScreenScale());
                double scale = factor / ScreenScale();
                fitted = false;
                GoTo(factor, 0.5 * viewSize.Width - (part.X + 0.5 * part.Width) * scale, 0.5 * viewSize.Height - (part.Y + 0.5 * part.Height) * scale, true);
            }
        }

        // ZoomPanel Protected Methods

        // The child, and over it the marquee
        protected override int VisualChildrenCount
        {
            get { return base.VisualChildrenCount + 1; }
        }

        protected override Visual GetVisualChild(int index)
        {
            return index < base.VisualChildrenCount ? base.GetVisualChild(index) : overlay;
        }

        // The child has the size it asks for (an image: its pixels), whatever the size of the panel
        protected override Size MeasureOverride(Size constraint)
        {
            UIElement child = Child;
            if (child != null)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            viewSize = finalSize;
            UIElement child = Child;
            if (child != null)
            {
                contentSize = child.DesiredSize;
                child.Arrange(new Rect(new Point(0, 0), contentSize));
                if (child.RenderTransform != transform)
                {
                    child.RenderTransform = transform;
                }
                if (fitted)
                {
                    Fit(false);
                }
                else
                {
                    GoTo(targetZoom, targetX, targetY, false);
                }
            }
            return finalSize;
        }

        // The wheel: a zoom level in or out, about the cursor
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            ZoomAbout(e.GetPosition(this), e.Delta > 0 ? LevelAbove() : LevelBelow());
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            bool left = e.ChangedButton == MouseButton.Left;
            bool pans = e.ChangedButton == MouseButton.Middle || (left && (ZoomMode == ZoomPanelMode.Move || Keyboard.IsKeyDown(Key.Space)));
            Point position = e.GetPosition(this);
            if (left && e.ClickCount == 2 && ZoomMode == ZoomPanelMode.Move)
            {
                ZoomToFit();
            }
            else if (pans)
            {
                // (whatever was moving stops under the hand)
                Settle();
                drag = Drag.Pan;
                last = position;
                lastTime = clock.Elapsed.TotalSeconds;
                velocityX = 0;
                velocityY = 0;
                CaptureMouse();
                Cursor = Cursors.ScrollAll;
            }
            else if (left && ZoomMode == ZoomPanelMode.Zoom)
            {
                ZoomAbout(position, (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? LevelBelow() : LevelAbove());
            }
            else if (left && ZoomMode == ZoomPanelMode.Rectangle)
            {
                Settle();
                drag = Drag.Marquee;
                last = position;
                CaptureMouse();
                Cursor = Cursors.Cross;
            }
            else
            {
                return;
            }
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point position = e.GetPosition(this);
            if (drag == Drag.Pan)
            {
                double now = clock.Elapsed.TotalSeconds, elapsed = now - lastTime;
                double dx = position.X - last.X, dy = position.Y - last.Y;
                if (elapsed > 0)
                {
                    // The velocity of the hand: that of the last moves, the newest counting the most
                    double weight = 1 - Math.Exp(-elapsed / 0.04);
                    velocityX += (dx / elapsed - velocityX) * weight;
                    velocityY += (dy / elapsed - velocityY) * weight;
                }
                last = position;
                lastTime = now;
                fitted = false;
                GoTo(zoom, offsetX + dx, offsetY + dy, false);
            }
            else if (drag == Drag.Marquee)
            {
                DrawingContext context = overlay.RenderOpen();
                context.DrawRectangle(marqueeFill, marqueePen, new Rect(last, position));
                context.Close();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (drag == Drag.None)
            {
                return;
            }
            Drag ended = drag;
            drag = Drag.None;
            ReleaseMouseCapture();
            Cursor = null;
            if (ended == Drag.Pan)
            {
                // Let go while moving (and not after the hand has come to rest): the image goes on
                double speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
                if (IsInertial && clock.Elapsed.TotalSeconds - lastTime < 0.1 && speed > 60)
                {
                    coasting = true;
                    Run();
                }
            }
            else
            {
                overlay.RenderOpen().Close();
                Rect marquee = new Rect(last, e.GetPosition(this));
                if (marquee.Width > 4 && marquee.Height > 4)
                {
                    double scale = zoom / ScreenScale();
                    SetViewbox(new Rect((marquee.X - offsetX) / scale, (marquee.Y - offsetY) / scale, marquee.Width / scale, marquee.Height / scale));
                }
            }
            e.Handled = true;
        }

        // Touch: fingers pan and pinch, and the pan goes on when they leave
        protected override void OnManipulationStarting(ManipulationStartingEventArgs e)
        {
            Settle();
            e.ManipulationContainer = this;
            e.Mode = ManipulationModes.Translate | ManipulationModes.Scale;
            e.Handled = true;
        }

        protected override void OnManipulationInertiaStarting(ManipulationInertiaStartingEventArgs e)
        {
            // (10 inches a second, each second: the deceleration is in the units of the panel a millisecond squared)
            e.TranslationBehavior.DesiredDeceleration = IsInertial ? 10.0 * 96.0 / (1000.0 * 1000.0) : 1.0;
            e.Handled = true;
        }

        protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
        {
            double factor = Math.Max(Math.Min(MinZoomFactor, zoom), Math.Min(MaxZoomFactor, zoom * e.DeltaManipulation.Scale.X));
            double before = zoom / ScreenScale(), after = factor / ScreenScale();
            double x = e.ManipulationOrigin.X - (e.ManipulationOrigin.X - offsetX) / before * after + e.DeltaManipulation.Translation.X;
            double y = e.ManipulationOrigin.Y - (e.ManipulationOrigin.Y - offsetY) / before * after + e.DeltaManipulation.Translation.Y;
            double oldZoom = zoom, oldX = offsetX, oldY = offsetY;
            fitted = false;
            GoTo(factor, x, y, false);
            if (e.IsInertial && zoom == oldZoom && offsetX == oldX && offsetY == oldY)
            {
                // (against the edge: nothing left to coast)
                e.Complete();
            }
            e.Handled = true;
        }

        // ZoomPanel Private Methods

        // The units of the panel for a pixel of the screen
        private double ScreenScale()
        {
            return VisualTreeHelper.GetDpi(this).DpiScaleX;
        }

        private double LevelAbove()
        {
            for (int i = 0; i < levels.Length; ++i)
            {
                if (levels[i] > targetZoom * 1.0001)
                {
                    return levels[i];
                }
            }
            return levels[levels.Length - 1];
        }

        private double LevelBelow()
        {
            for (int i = levels.Length - 1; i >= 0; --i)
            {
                if (levels[i] < targetZoom * 0.9999)
                {
                    return levels[i];
                }
            }
            return Math.Min(levels[0], targetZoom);
        }

        // The zoom that leaves the point of the panel on the same point of the image (from where the image is going to, if it
        // is on its way: the turns of the wheel add up)
        private void ZoomAbout(Point anchor, double factor)
        {
            factor = Math.Max(Math.Min(MinZoomFactor, targetZoom), Math.Min(MaxZoomFactor, factor));
            double before = targetZoom / ScreenScale(), after = factor / ScreenScale();
            fitted = false;
            GoTo(factor, anchor.X - (anchor.X - targetX) / before * after, anchor.Y - (anchor.Y - targetY) / before * after, true);
        }

        private void Fit(bool animate)
        {
            if (contentSize.Width > 0 && contentSize.Height > 0 && viewSize.Width > 0 && viewSize.Height > 0)
            {
                double scale = Math.Min(viewSize.Width / contentSize.Width, viewSize.Height / contentSize.Height);
                GoTo(scale * ScreenScale(), 0, 0, animate);
            }
        }

        // Where the image may be at the zoom: one smaller than the panel is in its middle, a larger one does not leave a gap at
        // a side
        private void Limit(double factor, ref double x, ref double y)
        {
            double scale = factor / ScreenScale();
            double width = contentSize.Width * scale, height = contentSize.Height * scale;
            x = width <= viewSize.Width ? 0.5 * (viewSize.Width - width) : Math.Max(viewSize.Width - width, Math.Min(0, x));
            y = height <= viewSize.Height ? 0.5 * (viewSize.Height - height) : Math.Max(viewSize.Height - height, Math.Min(0, y));
        }

        // The image at the zoom and the place, at once or over AnimationSeconds
        private void GoTo(double factor, double x, double y, bool animate)
        {
            Limit(factor, ref x, ref y);
            targetZoom = factor;
            targetX = x;
            targetY = y;
            coasting = false;
            if (animate && IsAnimated && AnimationSeconds > 0 && (factor != zoom || x != offsetX || y != offsetY))
            {
                startZoom = zoom;
                startX = offsetX;
                startY = offsetY;
                animationStart = clock.Elapsed.TotalSeconds;
                animating = true;
                Run();
            }
            else
            {
                animating = false;
                Show(factor, x, y);
            }
        }

        // What is moving stops where it is
        private void Settle()
        {
            animating = false;
            coasting = false;
            targetZoom = zoom;
            targetX = offsetX;
            targetY = offsetY;
        }

        private void Show(double factor, double x, double y)
        {
            bool changed = factor != zoom;
            zoom = factor;
            offsetX = x;
            offsetY = y;
            UIElement child = Child;
            if (child != null)
            {
                double scale = zoom / ScreenScale();
                transform.Matrix = new Matrix(scale, 0, 0, scale, offsetX, offsetY);
                // (enlarged, the pixels are squares; reduced, they are averaged)
                RenderOptions.SetBitmapScalingMode(child, zoom > 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
            }
            if (changed && ZoomFactorChanged != null)
            {
                ZoomFactorChanged(this, EventArgs.Empty);
            }
        }

        // The frames of an animation or of a coasting pan
        private void Run()
        {
            if (!running)
            {
                running = true;
                lastFrame = clock.Elapsed.TotalSeconds;
                CompositionTarget.Rendering += OnFrame;
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (animating)
            {
                double t = (now - animationStart) / AnimationSeconds;
                if (t >= 1)
                {
                    animating = false;
                    Show(targetZoom, targetX, targetY);
                }
                else
                {
                    // (fast at first, then coming to rest: 1 - (1 - t)^5)
                    double rest = 1 - t;
                    double eased = 1 - rest * rest * rest * rest * rest;
                    if (startZoom != targetZoom)
                    {
                        // The zoom in equal ratios, about the point of the panel that shows the same point of the image before
                        // and after
                        double factor = startZoom * Math.Pow(targetZoom / startZoom, eased);
                        double fixedX = (startX * targetZoom - targetX * startZoom) / (targetZoom - startZoom);
                        double fixedY = (startY * targetZoom - targetY * startZoom) / (targetZoom - startZoom);
                        Show(factor, fixedX + (startX - fixedX) * factor / startZoom, fixedY + (startY - fixedY) * factor / startZoom);
                    }
                    else
                    {
                        Show(zoom, startX + (targetX - startX) * eased, startY + (targetY - startY) * eased);
                    }
                }
            }
            else if (coasting)
            {
                double elapsed = now - lastFrame;
                double x = offsetX + velocityX * elapsed, y = offsetY + velocityY * elapsed;
                double wantedX = x, wantedY = y;
                Limit(zoom, ref x, ref y);
                // (against an edge the image stops that way)
                velocityX = x != wantedX ? 0 : velocityX * Math.Exp(-Friction * elapsed);
                velocityY = y != wantedY ? 0 : velocityY * Math.Exp(-Friction * elapsed);
                Show(zoom, x, y);
                targetX = x;
                targetY = y;
                if (velocityX * velocityX + velocityY * velocityY < 10 * 10)
                {
                    coasting = false;
                }
            }
            lastFrame = now;
            if (!animating && !coasting)
            {
                running = false;
                CompositionTarget.Rendering -= OnFrame;
            }
        }

        // ZoomPanel Private Members
        private enum Drag
        {
            None,
            Pan,
            Marquee
        }

        private static readonly double[] levels =
        {
            0.0625, 0.0833, 0.125, 0.1667, 0.25, 0.3333, 0.5, 0.6667, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 32, 64
        };

        private static readonly Brush marqueeFill = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
        private static readonly Pen marqueePen = new Pen(Brushes.White, 1);

        private readonly MatrixTransform transform;
        private readonly DrawingVisual overlay;
        private readonly Stopwatch clock;
        private Size viewSize, contentSize;
        // What is shown, and what it is going to
        private double zoom, offsetX, offsetY;
        private double targetZoom, targetX, targetY;
        private bool fitted;
        // The animation
        private bool running, animating;
        private double startZoom, startX, startY, animationStart, lastFrame;
        // The drag and the velocity it leaves (units of the panel a second)
        private Drag drag;
        private Point last;
        private double lastTime, velocityX, velocityY;
        private bool coasting;
    }
}
