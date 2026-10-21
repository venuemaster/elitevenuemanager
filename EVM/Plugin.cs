using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EVM.Venue;
using EVM.Windows;

namespace EVM;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;

    private static readonly string[] Commands = { "/venue", "/evm" };

    public Configuration Config { get; }
    public ChatQueue Queue { get; }
    public VenueManager Venue { get; }

    private readonly WindowSystem windowSystem = new("EVM");
    private readonly VenueWindow window;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Queue = new ChatQueue(this);
        Venue = new VenueManager(this);

        window = new VenueWindow(this);
        windowSystem.AddWindow(window);

        foreach (var c in Commands)
            CommandManager.AddHandler(c, new CommandInfo((_, _) => window.Toggle())
            {
                HelpMessage = "Open the Elite Venue Manager: guests, bar orders, song requests, shouts and Party Finder timer.",
            });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        windowSystem.RemoveAllWindows();
        foreach (var c in Commands) CommandManager.RemoveHandler(c);
    }

    private void ToggleWindow() => window.Toggle();

    private void OnChatMessage(IHandleableChatMessage msg)
    {
        var type = msg.LogKind;
        var message = msg.Message;
        var c = (int)type & 0x7F;
        var playerChat = c is >= 10 and <= 30 or 32 or 37 or (>= 101 and <= 107);
        if (!playerChat && message.TextValue.StartsWith("Your message was not heard", System.StringComparison.OrdinalIgnoreCase))
        {
            Queue.OnBlocked();
            return;
        }
        try { Venue.OnChatMessage(type, msg.Sender, message); }
        catch (System.Exception ex) { Log.Error(ex, "Venue failed to read a chat message"); }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try { Queue.Update(); }
        catch (System.Exception ex) { Log.Error(ex, "Chat queue failed"); }
        try { Venue.Update(); }
        catch (System.Exception ex) { Log.Error(ex, "Venue update failed"); }
    }

    public void Save() => PluginInterface.SavePluginConfig(Config);

    public void Announce(string text)
    {
        var cmd = string.IsNullOrWhiteSpace(Config.OutputCommand) ? "/sh" : Config.OutputCommand.Trim();
        Queue.Enqueue($"{cmd} {text}");
    }

    public void SendLine(string line) => Queue.Enqueue(line);
}
