using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using EVM.Venue;

namespace EVM.Windows;

public class VenueWindow : Window
{
    private readonly Plugin plugin;
    private VenueManager V => plugin.Venue;
    private VenueConfig Config => plugin.Config.Venue;

    private static readonly Vector4 Green = new(0.45f, 1f, 0.55f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 Grey = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Pink = new(1f, 0.4f, 0.8f, 1f);
    private static readonly Vector4 Red = new(1f, 0.45f, 0.45f, 1f);

    private string newAd = string.Empty;
    private string newItem = string.Empty;
    private int newPrice = 50_000;
    private string newDesc = string.Empty;
    private bool showDoneOrders;
    private bool showDoneSongs;

    public VenueWindow(Plugin plugin) : base("Elite Venue Manager###EVMMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(620, 460), MaximumSize = new Vector2(float.MaxValue, float.MaxValue) };
    }

    private void Save() => plugin.Save();

    public override void Draw()
    {
        DrawStatus();
        ImGui.Separator();
        if (!ImGui.BeginTabBar("##venuetabs")) return;
        var waiting = V.Orders.Count(o => o.Status == OrderStatus.Waiting);
        var newSongs = V.Songs.Count(s => s.Status == RequestStatus.New);
        if (ImGui.BeginTabItem($"Guests ({V.Guests.Count})###guests")) { DrawGuests(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem($"Bar{(waiting > 0 ? $" ({waiting})" : "")}###bar")) { DrawBar(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem($"Songs{(newSongs > 0 ? $" ({newSongs})" : "")}###songs")) { DrawSongs(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Shouts###ads")) { DrawAds(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Party Finder###pf")) { DrawPf(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Welcome tells###welcome")) { DrawWelcome(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("Setup###setup")) { DrawSetup(); ImGui.EndTabItem(); }
        ImGui.EndTabBar();
    }

    // ---------------------------------------------------------------- status
    private void DrawStatus()
    {
        if (V.InRegisteredHouse)
            ImGui.TextColored(Green, $"Open: {V.CurrentHouse!.Label}  -  {V.Guests.Count} guest(s) here");
        else if (V.InsideAnyHouse)
        {
            ImGui.TextColored(Gold, $"You're inside {V.CurrentHouseLabel}, which isn't registered.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Register this house")) V.RegisterCurrentHouse();
        }
        else if (Config.Houses.Count == 0)
            ImGui.TextColored(Gold, "Go inside your venue and press \"Register this house\" to start tracking guests.");
        else
            ImGui.TextColored(Grey, "Not inside a registered house - guest tracking is paused.");

        var left = V.PfLeft;
        if (left != null)
        {
            ImGui.SameLine();
            var m = (int)Math.Ceiling(left.Value.TotalMinutes);
            ImGui.TextColored(m <= Config.PfWarnMinutes ? Red : Grey, m > 0 ? $"   PF: {m} min left" : "   PF: EXPIRED");
        }
    }

    // ---------------------------------------------------------------- guests
    private void DrawGuests()
    {
        var now = DateTime.Now;
        ImGui.TextColored(Pink, "Here now");
        if (ImGui.BeginTable("##here", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 180)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Visits", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Here for", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("##t", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableHeadersRow();
            foreach (var g in V.Guests.Values.OrderBy(x => x.Arrived))
            {
                Config.Visitors.TryGetValue(g.Key, out var rec);
                ImGui.PushID(g.Key);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(rec?.Visits <= 1 ? Gold : new Vector4(1, 1, 1, 1), g.Name);
                ImGui.SameLine(); ImGui.TextColored(Grey, g.World);
                if (rec?.Visits <= 1) { ImGui.SameLine(); ImGui.TextColored(Gold, "NEW"); }
                ImGui.TableNextColumn(); ImGui.Text((rec?.Visits ?? 1).ToString());
                ImGui.TableNextColumn(); ImGui.Text(Dur(now - g.Arrived));
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("/tell")) ImGui.SetClipboardText($"/tell {VenueManager.Target(g.Name, g.World)} ");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copies \"/tell Name@World \" so you can paste it into chat.");
                ImGui.PopID();
            }
            ImGui.EndTable();
        }

        ImGui.TextColored(Pink, $"Tonight's log ({V.Log.Count(e => e.Entered)} arrivals, {V.Log.Count(e => e.Entered && e.FirstVisit)} first-timers)");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear log")) V.ClearTonight();
        if (ImGui.BeginTable("##log", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, Math.Max(100f, ImGui.GetContentRegionAvail().Y - 30f))))
        {
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Who", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 110);
            ImGui.TableHeadersRow();
            foreach (var e in V.Log)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextColored(Grey, e.Time.ToString("h:mm tt"));
                ImGui.TableNextColumn(); ImGui.Text($"{e.Name}  {e.World}");
                ImGui.TableNextColumn();
                if (e.Entered) ImGui.TextColored(e.FirstVisit ? Gold : Green, e.FirstVisit ? "arrived (first time)" : "arrived");
                else ImGui.TextColored(Grey, "left");
            }
            ImGui.EndTable();
        }
        ImGui.TextColored(Grey, $"{Config.Visitors.Count} different people have visited since you started tracking.");
    }

    private static string Dur(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";

    // ---------------------------------------------------------------- bar
    private void DrawBar()
    {
        var on = Config.OrdersEnabled;
        if (ImGui.Checkbox($"Take orders: guests type {Config.OrderCommand} <drink>   ({Config.MenuCommand} sends them the menu)", ref on)) { Config.OrdersEnabled = on; Save(); }
        if (ImGui.Button("Shout the menu"))
            plugin.Announce(V.Fill(Config.MenuShout, menu: V.MenuText()));

        ImGui.Separator();
        var waiting = V.Orders.Where(o => o.Status == OrderStatus.Waiting).ToList();
        ImGui.TextColored(Pink, $"Orders waiting ({waiting.Count})");
        ImGui.SameLine();
        ImGui.Checkbox("Show served", ref showDoneOrders);
        var list = showDoneOrders ? V.Orders.AsEnumerable().Reverse().ToList() : waiting;
        if (ImGui.BeginTable("##orders", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 200)))
        {
            ImGui.TableSetupColumn("Waiting", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Who", ImGuiTableColumnFlags.WidthFixed, 160);
            ImGui.TableSetupColumn("Drink", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("##b", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableHeadersRow();
            for (var i = 0; i < list.Count; i++)
            {
                var o = list[i];
                ImGui.PushID("o" + V.Orders.IndexOf(o));
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var w = (o.ServedAt ?? DateTime.Now) - o.Time;
                ImGui.TextColored(o.Status == OrderStatus.Waiting && w.TotalMinutes >= 5 ? Red : Grey, Dur(w));
                ImGui.TableNextColumn(); ImGui.Text(o.Name);
                ImGui.TableNextColumn();
                var known = Config.Menu.Any(m => m.Name == o.Item);
                ImGui.TextColored(known ? Green : Gold, o.Item + (known ? "" : " (not on menu)"));
                ImGui.TableNextColumn(); ImGui.Text(o.Price > 0 ? VenueManager.ShortGil(o.Price) : "-");
                ImGui.TableNextColumn();
                if (o.Status == OrderStatus.Waiting)
                {
                    if (ImGui.SmallButton("Served")) V.Serve(o);
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sends your serve line: " + V.Fill(Config.ServeLine, o.Name, item: o.Item));
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Cancel")) o.Status = OrderStatus.Cancelled;
                }
                else ImGui.TextColored(Grey, o.Status.ToString().ToLower());
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        var served = V.Orders.Where(o => o.Status == OrderStatus.Served).ToList();
        ImGui.TextColored(Grey, $"Tonight: {served.Count} served, {VenueManager.ShortGil(served.Sum(o => o.Price))} gil in drinks");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear finished")) V.Orders.RemoveAll(o => o.Status != OrderStatus.Waiting);

        if (ImGui.CollapsingHeader("Menu"))
        {
            DrinkItem? del = null;
            for (var i = 0; i < Config.Menu.Count; i++)
            {
                var m = Config.Menu[i];
                ImGui.PushID("m" + i);
                var oos = m.OutOfStock;
                if (ImGui.Checkbox("86", ref oos)) { m.OutOfStock = oos; Save(); }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Out of stock tonight - hidden from the menu and orders.");
                ImGui.SameLine(); ImGui.SetNextItemWidth(170);
                var n = m.Name; if (ImGui.InputText("##n", ref n, 60)) { m.Name = n; Save(); }
                ImGui.SameLine(); ImGui.SetNextItemWidth(110);
                var p = m.Price; if (ImGui.InputInt("##p", ref p, 0)) { m.Price = Math.Max(0, p); Save(); }
                ImGui.SameLine(); ImGui.SetNextItemWidth(-60);
                var d = m.Description; if (ImGui.InputText("##d", ref d, 120)) { m.Description = d; Save(); }
                ImGui.SameLine();
                if (ImGui.SmallButton("Del") && ImGui.GetIO().KeyCtrl) del = m;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
                ImGui.PopID();
            }
            if (del != null) { Config.Menu.Remove(del); Save(); }
            ImGui.SetNextItemWidth(170); ImGui.InputText("##newn", ref newItem, 60);
            ImGui.SameLine(); ImGui.SetNextItemWidth(110); ImGui.InputInt("##newp", ref newPrice, 0);
            ImGui.SameLine(); ImGui.SetNextItemWidth(-90); ImGui.InputText("##newd", ref newDesc, 120);
            ImGui.SameLine();
            if (ImGui.SmallButton("Add drink") && newItem.Trim().Length > 0)
            {
                Config.Menu.Add(new DrinkItem { Name = newItem.Trim(), Price = Math.Max(0, newPrice), Description = newDesc.Trim() });
                newItem = newDesc = string.Empty; Save();
            }
        }

        if (ImGui.CollapsingHeader("Bar messages"))
        {
            Text("Order command", Config.OrderCommand, v => Config.OrderCommand = v, 20);
            Text("Menu command", Config.MenuCommand, v => Config.MenuCommand = v, 20);
            var r = Config.OrderReplyTell;
            if (ImGui.Checkbox("Send a /tell when an order comes in", ref r)) { Config.OrderReplyTell = r; Save(); }
            Text("Order reply  {name} {item}", Config.OrderReply, v => Config.OrderReply = v);
            Text("Serve line (any chat command, e.g. /em or /say)  {name} {item}", Config.ServeLine, v => Config.ServeLine = v);
            Text("Menu message  {menu}", Config.MenuShout, v => Config.MenuShout = v);
        }
    }

    // ---------------------------------------------------------------- songs
    private void DrawSongs()
    {
        var on = Config.SongsEnabled;
        if (ImGui.Checkbox($"Take requests: guests type {Config.SongCommand} <song>", ref on)) { Config.SongsEnabled = on; Save(); }
        ImGui.SameLine();
        ImGui.Checkbox("Show played/skipped", ref showDoneSongs);

        var list = V.Songs.Where(s => showDoneSongs || s.Status is RequestStatus.New or RequestStatus.Queued).ToList();
        if (ImGui.BeginTable("##songs", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, Math.Max(150f, ImGui.GetContentRegionAvail().Y - 120f))))
        {
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 65);
            ImGui.TableSetupColumn("From", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Song", ImGuiTableColumnFlags.WidthStretch, 0);
            ImGui.TableSetupColumn("##b", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableHeadersRow();
            foreach (var s in list)
            {
                ImGui.PushID("s" + V.Songs.IndexOf(s));
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextColored(Grey, s.Time.ToString("h:mm tt"));
                ImGui.TableNextColumn(); ImGui.Text(s.Name);
                ImGui.TableNextColumn();
                var col = s.Status switch { RequestStatus.Queued => Green, RequestStatus.New => Gold, _ => Grey };
                ImGui.TextColored(col, s.Song);
                ImGui.TableNextColumn();
                switch (s.Status)
                {
                    case RequestStatus.New:
                        if (ImGui.SmallButton("Added to queue")) s.Status = RequestStatus.Queued;
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Skip")) s.Status = RequestStatus.Skipped;
                        break;
                    case RequestStatus.Queued:
                        ImGui.TextColored(Green, "in queue"); ImGui.SameLine();
                        if (ImGui.SmallButton("Played")) s.Status = RequestStatus.Played;
                        break;
                    default:
                        ImGui.TextColored(Grey, s.Status.ToString().ToLower()); ImGui.SameLine();
                        if (ImGui.SmallButton("Undo")) s.Status = RequestStatus.New;
                        break;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText(s.Song);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy the song name so you can search for it.");
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        if (ImGui.SmallButton("Clear played/skipped")) V.Songs.RemoveAll(s => s.Status is RequestStatus.Played or RequestStatus.Skipped);

        if (ImGui.CollapsingHeader("Song settings"))
        {
            Text("Request command", Config.SongCommand, v => Config.SongCommand = v, 20);
            var r = Config.SongReplyTell;
            if (ImGui.Checkbox("Send a /tell confirming the request", ref r)) { Config.SongReplyTell = r; Save(); }
            Text("Reply  {name} {song}", Config.SongReply, v => Config.SongReply = v);
            var max = Config.SongMaxPendingPerPerson;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("Max open requests per person", ref max, 1)) { Config.SongMaxPendingPerPerson = Math.Clamp(max, 1, 20); Save(); }
        }
    }

    // ---------------------------------------------------------------- ads
    private void DrawAds()
    {
        var on = Config.AdsEnabled;
        if (ImGui.Checkbox("Shout these automatically", ref on)) { Config.AdsEnabled = on; Save(); if (on) V.RestartAdTimer(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        var mins = Config.AdsIntervalMinutes;
        if (ImGui.InputInt("minutes apart", ref mins, 1)) { Config.AdsIntervalMinutes = Math.Clamp(mins, 5, 240); Save(); V.RestartAdTimer(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        var ch = Config.AdsChannel;
        if (ImGui.InputText("channel", ref ch, 16)) { Config.AdsChannel = ch; Save(); }
        var only = Config.AdsOnlyInHouse;
        if (ImGui.Checkbox("Only while I'm inside my registered house", ref only)) { Config.AdsOnlyInHouse = only; Save(); }

        if (Config.AdsEnabled)
        {
            var next = V.NextAdAt - DateTime.Now;
            ImGui.TextColored(Green, next.TotalSeconds > 0 ? $"Next shout in {(int)next.TotalMinutes}m {next.Seconds}s" : "Shouting soon...");
        }
        else ImGui.TextColored(Grey, "Automatic shouts are off.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Shout next one now")) V.ShoutNextAd();
        ImGui.TextColored(Grey, "They rotate in order. {venue} is replaced with your venue name.");

        ImGui.Separator();
        AdMessage? del = null;
        for (var i = 0; i < Config.Ads.Count; i++)
        {
            var a = Config.Ads[i];
            ImGui.PushID("ad" + i);
            var en = a.Enabled;
            if (ImGui.Checkbox("##en", ref en)) { a.Enabled = en; Save(); }
            ImGui.SameLine(); ImGui.SetNextItemWidth(-150);
            var t = a.Text; if (ImGui.InputText("##t", ref t, 400)) { a.Text = t; Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton("Shout")) V.ShoutAd(a);
            ImGui.SameLine();
            if (ImGui.SmallButton("Del") && ImGui.GetIO().KeyCtrl) del = a;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
            ImGui.PopID();
        }
        if (del != null) { Config.Ads.Remove(del); Save(); }
        ImGui.SetNextItemWidth(-90);
        ImGui.InputText("##newad", ref newAd, 400);
        ImGui.SameLine();
        if (ImGui.SmallButton("Add shout") && newAd.Trim().Length > 0) { Config.Ads.Add(new AdMessage { Text = newAd.Trim() }); newAd = string.Empty; Save(); }
    }

    // ---------------------------------------------------------------- party finder
    private void DrawPf()
    {
        ImGui.TextWrapped("Party Finder listings expire after an hour. Plugins can't press \"Update\" for you, so EVM times your listing, warns you in chat (with a sound) before it runs out, and opens Party Finder in one click.");
        ImGui.Spacing();
        var left = V.PfLeft;
        if (left == null) ImGui.TextColored(Grey, "No listing timer running.");
        else
        {
            var m = (int)Math.Ceiling(left.Value.TotalMinutes);
            ImGui.TextColored(m <= Config.PfWarnMinutes ? Red : Green, m > 0 ? $"Your listing ends in about {m} minute(s)." : "Your listing has expired!");
        }
        if (ImGui.Button("Open Party Finder")) V.OpenPartyFinder();
        ImGui.SameLine();
        if (ImGui.Button("I just posted / updated it - start timer")) V.PfPosted();
        ImGui.SameLine();
        ImGui.BeginDisabled(left == null);
        if (ImGui.Button("Stop timer")) V.PfStop();
        ImGui.EndDisabled();

        ImGui.Spacing();
        var lm = Config.PfListingMinutes;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Listing length (minutes)", ref lm, 5)) { Config.PfListingMinutes = Math.Clamp(lm, 5, 120); Save(); }
        var wm = Config.PfWarnMinutes;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Warn me this many minutes before", ref wm, 1)) { Config.PfWarnMinutes = Math.Clamp(wm, 0, 60); Save(); }
        var ao = Config.PfAutoOpen;
        if (ImGui.Checkbox("Open Party Finder automatically when the warning fires", ref ao)) { Config.PfAutoOpen = ao; Save(); }
        ImGui.Spacing();
        ImGui.TextColored(Grey, "To refresh: Open Party Finder > Recruit Members > Update, then press \"I just posted / updated it\".");
    }

    // ---------------------------------------------------------------- welcome
    private void DrawWelcome()
    {
        var on = Config.WelcomeEnabled;
        if (ImGui.Checkbox("Send a /tell to people when they walk in", ref on)) { Config.WelcomeEnabled = on; Save(); }
        ImGui.TextColored(Grey, "Only in your registered house. People already inside when you arrive aren't messaged.");
        var d = Config.WelcomeDelaySeconds;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Wait this many seconds after they arrive", ref d, 1)) { Config.WelcomeDelaySeconds = Math.Clamp(d, 0, 120); Save(); }
        var c = Config.WelcomeCooldownHours;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("Don't message the same person again for (hours)", ref c, 1)) { Config.WelcomeCooldownHours = Math.Clamp(c, 0, 168); Save(); }
        ImGui.Spacing();
        ImGui.TextColored(Grey, "Placeholders: {name} {venue} {visits}");
        Text("First visit", Config.WelcomeNew, v => Config.WelcomeNew = v);
        Text("Returning guest", Config.WelcomeBack, v => Config.WelcomeBack = v);
        Text("Never message these people (staff, friends) - names separated by commas", Config.NoTellNames, v => Config.NoTellNames = v);
    }

    // ---------------------------------------------------------------- setup
    private void DrawSetup()
    {
        Text("Venue name ({venue})", Config.VenueName, v => Config.VenueName = v, 60);
        ImGui.SetNextItemWidth(180);
        var gap = plugin.Config.SecondsBetweenMessages;
        if (ImGui.SliderFloat("Seconds between messages", ref gap, 1.0f, 8.0f, "%.1f s")) { plugin.Config.SecondsBetweenMessages = gap; Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Every tell and shout waits this long after the last one so the game doesn't block it.");
        if (plugin.Queue.BlockedCount > 0) { ImGui.SameLine(); ImGui.TextColored(Red, $"Game blocked {plugin.Queue.BlockedCount} (re-sent)"); }
        ImGui.SetNextItemWidth(60);
        var oc = plugin.Config.OutputCommand;
        if (ImGui.InputText("Channel for \"Shout the menu\"", ref oc, 16)) { plugin.Config.OutputCommand = oc; Save(); }
        ImGui.Spacing();
        ImGui.TextColored(Pink, "Registered houses");
        if (V.InsideAnyHouse && !V.InRegisteredHouse)
        {
            if (ImGui.Button($"Register this house: {V.CurrentHouseLabel}")) V.RegisterCurrentHouse();
        }
        else if (!V.InsideAnyHouse) ImGui.TextColored(Grey, "Go inside a house to register it.");
        RegisteredHouse? del = null;
        foreach (var h in Config.Houses)
        {
            ImGui.PushID("h" + h.Key);
            ImGui.BulletText(h.Label + (V.CurrentHouse == h ? "   (you're here)" : string.Empty));
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove") && ImGui.GetIO().KeyCtrl) del = h;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click");
            ImGui.PopID();
        }
        if (del != null) { Config.Houses.Remove(del); Save(); }

        ImGui.Spacing();
        ImGui.TextColored(Pink, "Read !order, !menu and !song from:");
        Check("/say", Config.ListenSay, v => Config.ListenSay = v); ImGui.SameLine();
        Check("/yell", Config.ListenYell, v => Config.ListenYell = v); ImGui.SameLine();
        Check("/shout", Config.ListenShout, v => Config.ListenShout = v); ImGui.SameLine();
        Check("/tell", Config.ListenTell, v => Config.ListenTell = v); ImGui.SameLine();
        Check("/party", Config.ListenParty, v => Config.ListenParty = v);

        ImGui.Spacing();
        if (ImGui.Button("Forget all visitor history") && ImGui.GetIO().KeyCtrl) { Config.Visitors.Clear(); Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl and click. Resets visit counts and first-timer tracking.");
    }

    private void Check(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (ImGui.Checkbox(label, ref v)) { set(v); Save(); }
    }

    private void Text(string label, string value, Action<string> set, int max = 400)
    {
        ImGui.Text(label);
        ImGui.SetNextItemWidth(-1);
        var v = value;
        if (ImGui.InputText("##" + label, ref v, max)) { set(v); Save(); }
    }
}
