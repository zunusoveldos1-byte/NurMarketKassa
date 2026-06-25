using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NurMarketKassa.Views.Dialogs;

internal static class PosDialogLayout
{
    public static void AttachOverlayToOwner(Window dialog, Window owner)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.SourceInitialized += (_, _) => FitOverlayToOwner(dialog, owner);
        dialog.Loaded += (_, _) => FitOverlayToOwner(dialog, owner);
    }

    public static void FitOverlayToOwner(Window dialog, Window? owner)
    {
        if (owner == null)
        {
            dialog.Left = SystemParameters.WorkArea.Left;
            dialog.Top = SystemParameters.WorkArea.Top;
            dialog.Width = SystemParameters.WorkArea.Width;
            dialog.Height = SystemParameters.WorkArea.Height;
            return;
        }

        var bounds = GetOwnerBoundsInDip(owner);
        dialog.Left = bounds.X;
        dialog.Top = bounds.Y;
        dialog.Width = Math.Max(1, bounds.Width);
        dialog.Height = Math.Max(1, bounds.Height);
    }

    private static Rect GetOwnerBoundsInDip(Window owner)
    {
        if (!owner.IsLoaded)
        {
            return new Rect(owner.Left, owner.Top, owner.Width, owner.Height);
        }

        var source = PresentationSource.FromVisual(owner);
        if (source?.CompositionTarget is not CompositionTarget target)
        {
            return new Rect(owner.Left, owner.Top,
                owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width,
                owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);
        }

        var fromDevice = target.TransformFromDevice;
        var topLeft = fromDevice.Transform(owner.PointToScreen(new Point(0, 0)));
        var bottomRight = fromDevice.Transform(
            owner.PointToScreen(new Point(owner.ActualWidth, owner.ActualHeight)));

        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;

        if (owner.WindowState == WindowState.Maximized && width > 1 && height > 1)
            return new Rect(topLeft.X, topLeft.Y, width, height);

        if (width < 1 || height < 1)
        {
            var helper = new WindowInteropHelper(owner);
            if (helper.Handle != IntPtr.Zero)
            {
                if (GetWindowRect(helper.Handle, out var rect))
                {
                    topLeft = fromDevice.Transform(new Point(rect.Left, rect.Top));
                    bottomRight = fromDevice.Transform(new Point(rect.Right, rect.Bottom));
                    width = bottomRight.X - topLeft.X;
                    height = bottomRight.Y - topLeft.Y;
                }
            }
        }

        return new Rect(topLeft.X, topLeft.Y, Math.Max(1, width), Math.Max(1, height));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
