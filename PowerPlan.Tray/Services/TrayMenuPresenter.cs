using PowerPlan.Tray;
using System.Runtime.InteropServices;

namespace PowerPlan.Tray.Services;

internal sealed class TrayMenuPresenter(TrayMenuBuilder menuBuilder)
{
    public TrayMenuCommand? Show(nint window, nint packedPosition, TrayMenuContext context)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return null;
        }

        try
        {
            var commands = AppendItems(menu, menuBuilder.Build(context));
            var position = GetMenuPosition(packedPosition);
            _ = SetForegroundWindow(window);
            var commandId = TrackPopupMenuEx(
                menu,
                TrackPopupReturnCommand | TrackPopupRightButton,
                position.X,
                position.Y,
                window,
                nint.Zero);
            _ = PostMessageW(window, WmNull, nint.Zero, nint.Zero);

            return commandId != 0 && commands.TryGetValue(commandId, out var command)
                ? command
                : null;
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private static Dictionary<uint, TrayMenuCommand> AppendItems(nint menu, IReadOnlyList<TrayMenuItem> items)
    {
        var commands = new Dictionary<uint, TrayMenuCommand>();
        foreach (var item in items)
        {
            if (item.Kind == TrayMenuItemKind.Separator)
            {
                AppendMenuW(menu, MenuSeparator, 0, null);
                continue;
            }

            var flags = MenuString;
            if (!item.IsEnabled)
            {
                flags |= MenuDisabled | MenuGrayed;
            }

            if (item.IsChecked)
            {
                flags |= MenuChecked;
            }

            AppendMenuW(menu, flags, item.CommandId, item.Text);
            if (item.Command is TrayMenuCommand command)
            {
                commands[item.CommandId] = command;
            }
        }

        return commands;
    }

    private static NativePoint GetMenuPosition(nint packedPosition)
    {
        var x = (short)((nuint)packedPosition & ushort.MaxValue);
        var y = (short)(((nuint)packedPosition >> 16) & ushort.MaxValue);
        if (x != -1 || y != -1)
        {
            return new NativePoint { X = x, Y = y };
        }

        return GetCursorPos(out var cursorPosition)
            ? cursorPosition
            : default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private const uint WmNull = 0x0000;
    private const uint MenuString = 0x00000000;
    private const uint MenuDisabled = 0x00000002;
    private const uint MenuGrayed = 0x00000001;
    private const uint MenuChecked = 0x00000008;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackPopupReturnCommand = 0x0100;
    private const uint TrackPopupRightButton = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
