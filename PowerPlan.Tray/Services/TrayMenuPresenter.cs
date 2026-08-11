using PowerPlan.Models;
using System.Runtime.InteropServices;

namespace PowerPlan.Tray.Services;

internal sealed class TrayMenuPresenter
{
    private const string OpenMainWindowIcon = "\u2302 ";
    private const string PowerPlanIcon = "\u26A1 ";
    private const string RefreshPlansIcon = "\u21BB ";
    private const string StartupIcon = "\u23FB ";
    private const string ExitIcon = "\u2715 ";
    private const uint FirstPlanCommandId = 1000;
    private const uint OpenMainWindowCommandId = 1;
    private const uint RefreshPlansCommandId = 2;
    private const uint ToggleStartupCommandId = 3;
    private const uint ExitCommandId = 4;
    private const uint ActivateHiddenUltimateCommandId = 5;

    private readonly ITrayLocalizer _localizer;

    public TrayMenuPresenter(ITrayLocalizer localizer)
    {
        _localizer = localizer;
    }

    public TrayMenuCommand? Show(nint window, nint packedPosition, TrayMenuContext context)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return null;
        }

        try
        {
            var commands = Build(menu, context);
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

    private Dictionary<uint, TrayMenuCommand> Build(nint menu, TrayMenuContext context)
    {
        var commands = new Dictionary<uint, TrayMenuCommand>();
        AppendMenuW(menu, MenuDisabled | MenuGrayed | MenuString, 0, _localizer.Get("App.WindowTitle"));
        AppendCommand(menu, commands, OpenMainWindowCommandId, OpenMainWindowIcon + _localizer.Get("Tray.Menu.OpenMainWindow"), new TrayMenuCommand(TrayMenuAction.OpenMainWindow));
        AppendMenuW(menu, MenuSeparator, 0, null);

        var commandId = FirstPlanCommandId;
        foreach (var plan in context.Plans)
        {
            var flags = MenuString | (plan.IsActive ? MenuChecked : MenuUnchecked);
            AppendMenuW(menu, flags, commandId, PowerPlanIcon + plan.Name);
            commands[commandId] = new TrayMenuCommand(TrayMenuAction.SwitchPlan, plan.Guid, plan.Name);
            commandId++;
        }

        if (!string.IsNullOrWhiteSpace(context.HiddenUltimatePlanGuid)
            && !context.Plans.Any(plan => string.Equals(plan.Guid, context.HiddenUltimatePlanGuid, StringComparison.OrdinalIgnoreCase)))
        {
            AppendCommand(
                menu,
                commands,
                ActivateHiddenUltimateCommandId,
                PowerPlanIcon + _localizer.Get("Tray.Menu.OpenHiddenUltimate"),
                new TrayMenuCommand(TrayMenuAction.ActivateHiddenUltimate, context.HiddenUltimatePlanGuid));
        }

        AppendMenuW(menu, MenuSeparator, 0, null);
        AppendCommand(menu, commands, RefreshPlansCommandId, RefreshPlansIcon + _localizer.Get("Tray.Menu.RefreshPlans"), new TrayMenuCommand(TrayMenuAction.RefreshPlans));
        AppendCommand(
            menu,
            commands,
            ToggleStartupCommandId,
            StartupIcon + (context.IsStartupEnabled
                ? _localizer.Get("Tray.Menu.DisableAutoStart")
                : _localizer.Get("Tray.Menu.EnableAutoStart")),
            new TrayMenuCommand(TrayMenuAction.ToggleStartup));
        AppendMenuW(menu, MenuSeparator, 0, null);
        AppendCommand(menu, commands, ExitCommandId, ExitIcon + _localizer.Get("Tray.Menu.Exit"), new TrayMenuCommand(TrayMenuAction.Exit));
        return commands;
    }

    private static void AppendCommand(
        nint menu,
        IDictionary<uint, TrayMenuCommand> commands,
        uint commandId,
        string text,
        TrayMenuCommand command)
    {
        AppendMenuW(menu, MenuString, commandId, text);
        commands[commandId] = command;
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
    private const uint MenuUnchecked = 0x00000000;
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

internal readonly record struct TrayMenuContext(
    IReadOnlyList<PowerPlanInfo> Plans,
    string? HiddenUltimatePlanGuid,
    bool IsStartupEnabled);

internal enum TrayMenuAction
{
    OpenMainWindow,
    SwitchPlan,
    ActivateHiddenUltimate,
    RefreshPlans,
    ToggleStartup,
    Exit
}

internal readonly record struct TrayMenuCommand(TrayMenuAction Action, string? PlanGuid = null, string? PlanName = null);
