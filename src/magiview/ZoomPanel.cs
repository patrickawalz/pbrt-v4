// (not in pbrt-v4) ZoomPanel.cs
//
// A panel that shows its child (the film image) zoomed and panned, as the image window of Adobe Photoshop does: the zoom is
// a percentage of the actual pixels (100%: one pixel of the image on one pixel of the screen), it goes through Photoshop's
// zoom levels, the wheel zooms about the cursor, a drag pans, and "fit" keeps the whole image in the panel as the panel or
// the image changes size.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace magiview
{
    // ZoomPanel Definition
    public sealed class ZoomPanel : Border
    {
        // ZoomPanel Public Methods
        public ZoomPanel()
        {
            ClipToBounds = true;
            transform = new MatrixTransform();
            zoom = 1;
            fitted = true;
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
            Fit();
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

        // ZoomPanel Protected Methods

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
                    Fit();
                }
                else
                {
                    Apply();
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

        // A drag with the left or the middle button pans; a double click fits
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                ZoomToFit();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle)
            {
                panning = true;
                last = e.GetPosition(this);
                CaptureMouse();
                Cursor = Cursors.ScrollAll;
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (panning)
            {
                Point now = e.GetPosition(this);
                offsetX += now.X - last.X;
                offsetY += now.Y - last.Y;
                last = now;
                fitted = false;
                Apply();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (panning)
            {
                panning = false;
                ReleaseMouseCapture();
                Cursor = null;
                e.Handled = true;
            }
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
                if (levels[i] > zoom * 1.0001)
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
                if (levels[i] < zoom * 0.9999)
                {
                    return levels[i];
                }
            }
            return levels[0];
        }

        // The zoom that leaves the point of the panel on the same point of the image
        private void ZoomAbout(Point anchor, double factor)
        {
            factor = Math.Max(levels[0], Math.Min(levels[levels.Length - 1], factor));
            double before = zoom / ScreenScale(), after = factor / ScreenScale();
            double contentX = (anchor.X - offsetX) / before, contentY = (anchor.Y - offsetY) / before;
            offsetX = anchor.X - contentX * after;
            offsetY = anchor.Y - contentY * after;
            fitted = false;
            SetZoom(factor);
        }

        private void Fit()
        {
            if (contentSize.Width > 0 && contentSize.Height > 0 && viewSize.Width > 0 && viewSize.Height > 0)
            {
                double scale = Math.Min(viewSize.Width / contentSize.Width, viewSize.Height / contentSize.Height);
                SetZoom(scale * ScreenScale());
            }
        }

        private void SetZoom(double factor)
        {
            bool changed = factor != zoom;
            zoom = factor;
            Apply();
            if (changed && ZoomFactorChanged != null)
            {
                ZoomFactorChanged(this, EventArgs.Empty);
            }
        }

        // The transformation of the child: an image smaller than the panel is in its middle, a larger one does not leave a
        // gap at a side
        private void Apply()
        {
            UIElement child = Child;
            if (child == null)
            {
                return;
            }
            double scale = zoom / ScreenScale();
            double width = contentSize.Width * scale, height = contentSize.Height * scale;
            offsetX = width <= viewSize.Width ? 0.5 * (viewSize.Width - width) : Math.Max(viewSize.Width - width, Math.Min(0, offsetX));
            offsetY = height <= viewSize.Height ? 0.5 * (viewSize.Height - height) : Math.Max(viewSize.Height - height, Math.Min(0, offsetY));
            transform.Matrix = new Matrix(scale, 0, 0, scale, offsetX, offsetY);
            // (enlarged, the pixels are squares; reduced, they are averaged)
            RenderOptions.SetBitmapScalingMode(child, zoom > 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        }

        // ZoomPanel Private Members
        private static readonly double[] levels =
        {
            0.0625, 0.0833, 0.125, 0.1667, 0.25, 0.3333, 0.5, 0.6667, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 32, 64
        };

        private readonly MatrixTransform transform;
        private Size viewSize, contentSize;
        private double zoom, offsetX, offsetY;
        private bool fitted, panning;
        private Point last;
    }
}
