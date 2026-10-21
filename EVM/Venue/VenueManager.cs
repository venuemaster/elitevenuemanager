using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace EVM.Venue;

public class Guest
{
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string World = string.Empty;
    public DateTime Arrived;
    public DateTime LastSeen;
    public bool WasHereWhenIArrived;
    public bool WelcomeQueued;
}

public class VisitEvent
{
    public DateTime Time;
    public string Name = string.Empty;
    public string World = string.Empty;
    public bool Entered;
    public bool FirstVisit;
}

public enum RequestStatus { New, Queued, Played, Skipped }

public class SongRequest
{
    public DateTime Time;
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string World = string.Empty;
    public string Song = string.Empty;
    public RequestStatus Status;
}

public enum OrderStatus { Waiting, Served, Cancelled }

public class DrinkOrder
{
    public DateTime Time;
    public string Key = string.Empty;
    public string Name = string.Empty;
    public string World = string.Empty;
    public string Item = string.Empty;
    public int Price;
    public string Channel = string.Empty;
    public OrderStatus Status;
    public DateTime? ServedAt;
}

/// <summary>Everything for running the venue: who is in the house, welcome tells, timed shouts,
/// drink orders, song requests and the party finder timer.</summary>
public class VenueManager
{
    private readonly Plugin plugin;
    private VenueConfig Config => plugin.Config.Venue;

    public bool InRegisteredHouse { get; private set; }
    public RegisteredHouse? CurrentHouse { get; private set; }
    public string CurrentHouseLabel { get; private set; } = string.Empty;
    public ulong CurrentHouseKey { get; private set; }
    public bool InsideAnyHouse { get; private set; }

    public readonly Dictionary<string, Guest> Guests = new();
    public readonly List<VisitEvent> Log = new();
    public readonly List<SongRequest> Songs = new();
    public readonly List<DrinkOrder> Orders = new();

    private DateTime trackingStarted = DateTime.MinValue;
    private ulong lastLabelKey = ulong.MaxValue;
    private DateTime nextScan = DateTime.MinValue;
    private DateTime nextAd = DateTime.MinValue;
    private int adIndex;
    public DateTime? PfPostedAt { get; private set; }
    private bool pfWarned, pfExpiredWarned;

    private static readonly TimeSpan LeaveAfter = TimeSpan.FromSeconds(20);

    public VenueManager(Plugin plugin) => this.plugin = plugin;

    public string Fill(string t, string name = "", int visits = 0, string item = "", string song = "", string menu = "")
        => t.Replace("{venue}", Config.VenueName).Replace("{name}", name).Replace("{visits}", visits.ToString())
            .Replace("{item}", item).Replace("{song}", song).Replace("{menu}", menu);

    // ======================= housing =======================
    /// <summary>Key for the house you are standing in, or 0 if you're not inside one.</summary>
    public static unsafe ulong ReadCurrentHouse(out int ward, out int plot, out ushort territory, out ushort world)
    {
        ward = plot = 0; territory = world = 0;
        var hm = HousingManager.Instance();
        if (hm == null || !hm->IsInside()) return 0;
        var id = hm->GetCurrentIndoorHouseId().Id;
        if (id == 0 || id == ulong.MaxValue) return 0;
        var unit = id & 0xFF;
        var wardIdx = (id >> 16) & 0x3F;
        territory = (ushort)((id >> 32) & 0xFFFF);
        world = (ushort)((id >> 48) & 0xFFFF);
        ward = (int)wardIdx + 1;
        plot = (unit & 0x80) != 0 ? 0 : (int)(unit & 0x7F) + 1;
        return unit | (wardIdx << 8) | ((ulong)territory << 16) | ((ulong)world << 32);
    }

    public static string DescribeHouse(int ward, int plot, ushort territory, ushort world)
    {
        string district = "Housing", server = string.Empty;
        try { district = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault(territory)?.PlaceName.Value.Name.ExtractText() ?? district; } catch { }
        try { server = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(world)?.Name.ExtractText() ?? string.Empty; } catch { }
        var where = plot > 0 ? $"{district} Ward {ward}, Plot {plot}" : $"{district} Ward {ward} apartment";
        return string.IsNullOrEmpty(server) ? where : $"{where} ({server})";
    }

    public void RegisterCurrentHouse()
    {
        var key = ReadCurrentHouse(out var ward, out var plot, out var terr, out var world);
        if (key == 0 || Config.Houses.Any(h => h.Key == key)) return;
        Config.Houses.Add(new RegisteredHouse { Key = key, Label = DescribeHouse(ward, plot, terr, world), Registered = DateTime.Now });
        plugin.Save();
    }

    // ======================= every frame =======================
    public void Update()
    {
        var now = DateTime.Now;
        if (now >= nextScan)
        {
            nextScan = now.AddSeconds(1);
            ScanHouse(now);
        }
        UpdateAds(now);
        UpdatePartyFinder(now);
    }

    private void ScanHouse(DateTime now)
    {
        int ward = 0, plot = 0; ushort terr = 0, world = 0;
        var key = Plugin.ClientState.IsLoggedIn ? ReadCurrentHouse(out ward, out plot, out terr, out world) : 0UL;
        InsideAnyHouse = key != 0;
        CurrentHouseKey = key;
        var house = key == 0 ? null : Config.Houses.FirstOrDefault(h => h.Key == key);
        if (key != lastLabelKey) { lastLabelKey = key; CurrentHouseLabel = key == 0 ? string.Empty : DescribeHouse(ward, plot, terr, world); }

        if (house == null)
        {
            if (InRegisteredHouse) EndTracking(now);
            InRegisteredHouse = false; CurrentHouse = null;
            return;
        }
        if (!InRegisteredHouse) { trackingStarted = now; Guests.Clear(); }
        InRegisteredHouse = true; CurrentHouse = house;

        var me = Plugin.ObjectTable.LocalPlayer;
        var seen = new HashSet<string>();
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj is not IPlayerCharacter pc) continue;
            if (me != null && pc.Address == me.Address) continue;
            var name = pc.Name.TextValue;
            if (string.IsNullOrEmpty(name)) continue;
            var w = string.Empty;
            try { w = pc.HomeWorld.Value.Name.ExtractText(); } catch { }
            var k = $"{name}@{w}";
            if (!seen.Add(k)) continue;

            if (Guests.TryGetValue(k, out var g)) { g.LastSeen = now; continue; }

            var already = (now - trackingStarted).TotalSeconds < 5;
            g = new Guest { Key = k, Name = name, World = w, Arrived = now, LastSeen = now, WasHereWhenIArrived = already };
            Guests[k] = g;
            OnArrive(g, now);
        }

        foreach (var g in Guests.Values.Where(x => !seen.Contains(x.Key) && now - x.LastSeen > LeaveAfter).ToList())
        {
            Guests.Remove(g.Key);
            Log.Insert(0, new VisitEvent { Time = now, Name = g.Name, World = g.World, Entered = false });
        }
        if (Log.Count > 1000) Log.RemoveRange(1000, Log.Count - 1000);
    }

    private void OnArrive(Guest g, DateTime now)
    {
        var first = !Config.Visitors.TryGetValue(g.Key, out var rec);
        if (rec == null)
        {
            rec = new VisitorRecord { Name = g.Name, World = g.World, FirstSeen = now };
            Config.Visitors[g.Key] = rec;
        }
        // A new visit if we haven't seen them in the last 2 hours.
        if (first || now - rec.LastSeen > TimeSpan.FromHours(2)) rec.Visits++;
        rec.LastSeen = now;
        plugin.Save();

        Log.Insert(0, new VisitEvent { Time = now, Name = g.Name, World = g.World, Entered = true, FirstVisit = first });

        if (Config.WelcomeEnabled && !g.WasHereWhenIArrived && !IsNoTell(g.Name)
            && now - rec.LastWelcomed > TimeSpan.FromHours(Math.Max(0, Config.WelcomeCooldownHours)))
        {
            g.WelcomeQueued = true;
            rec.LastWelcomed = now;
            var text = Fill(rec.Visits <= 1 ? Config.WelcomeNew : Config.WelcomeBack, g.Name, rec.Visits);
            var delay = Math.Clamp(Config.WelcomeDelaySeconds, 0, 120);
            pendingWelcomes.Add((now.AddSeconds(delay), g.Key, $"/tell {Target(g.Name, g.World)} {text}"));
        }
    }

    private readonly List<(DateTime When, string Key, string Line)> pendingWelcomes = new();

    private void EndTracking(DateTime now)
    {
        foreach (var g in Guests.Values)
            Log.Insert(0, new VisitEvent { Time = now, Name = g.Name, World = g.World, Entered = false });
        Guests.Clear();
        pendingWelcomes.Clear();
    }

    public bool IsNoTell(string name)
        => Config.NoTellNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string Target(string name, string world) => string.IsNullOrEmpty(world) ? name : $"{name}@{world}";

    public void ClearTonight() { Log.Clear(); }

    // ======================= timed shouts =======================
    private void UpdateAds(DateTime now)
    {
        // welcome tells waiting for their delay
        for (var i = pendingWelcomes.Count - 1; i >= 0; i--)
        {
            var p = pendingWelcomes[i];
            if (now < p.When) continue;
            pendingWelcomes.RemoveAt(i);
            if (Guests.ContainsKey(p.Key)) plugin.SendLine(p.Line);   // only if they're still here
        }

        if (!Config.AdsEnabled) return;
        if (Config.AdsOnlyInHouse && !InRegisteredHouse) return;
        if (nextAd == DateTime.MinValue) { nextAd = now.AddMinutes(Math.Max(5, Config.AdsIntervalMinutes)); return; }
        if (now < nextAd) return;
        ShoutNextAd();
    }

    public DateTime NextAdAt => nextAd;

    public void RestartAdTimer() => nextAd = DateTime.Now.AddMinutes(Math.Max(5, Config.AdsIntervalMinutes));

    public void ShoutNextAd()
    {
        RestartAdTimer();
        var list = Config.Ads.Where(a => a.Enabled && a.Text.Trim().Length > 0).ToList();
        if (list.Count == 0) return;
        adIndex %= list.Count;
        var ad = list[adIndex];
        adIndex = (adIndex + 1) % list.Count;
        ShoutAd(ad);
    }

    public void ShoutAd(AdMessage ad)
    {
        var ch = string.IsNullOrWhiteSpace(Config.AdsChannel) ? "/sh" : Config.AdsChannel.Trim();
        plugin.SendLine($"{ch} {Fill(ad.Text)}");
    }

    // ======================= party finder =======================
    public void PfPosted() { PfPostedAt = DateTime.Now; pfWarned = pfExpiredWarned = false; }
    public void PfStop() { PfPostedAt = null; }
    public TimeSpan? PfLeft => PfPostedAt == null ? null : PfPostedAt.Value.AddMinutes(Config.PfListingMinutes) - DateTime.Now;

    public void OpenPartyFinder() => Chat.Send("/partyfinder");

    private void UpdatePartyFinder(DateTime now)
    {
        var left = PfLeft;
        if (left == null) return;
        if (!pfWarned && left.Value.TotalMinutes <= Config.PfWarnMinutes)
        {
            pfWarned = true;
            Chat.Send($"/echo [EVM] Your Party Finder listing ends in {Math.Max(0, (int)Math.Ceiling(left.Value.TotalMinutes))} min -- time to refresh it! <se.6>");
            if (Config.PfAutoOpen) OpenPartyFinder();
        }
        if (!pfExpiredWarned && left.Value <= TimeSpan.Zero)
        {
            pfExpiredWarned = true;
            Chat.Send("/echo [EVM] Your Party Finder listing has EXPIRED. Open Party Finder and post it again. <se.6>");
        }
    }

    // ======================= chat commands =======================
    public void OnChatMessage(XivChatType type, SeString sender, SeString message)
    {
        if (!Listening(type, out var channel)) return;
        var text = message.TextValue.Trim();
        if (text.Length == 0 || text[0] != '!') return;

        var pp = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (pp == null) return;   // the host's own lines
        var name = pp.PlayerName;
        var world = string.Empty;
        try { world = pp.World.Value.Name.ExtractText(); } catch { }
        var key = $"{name}@{world}";

        if (Config.SongsEnabled && TryCommand(text, Config.SongCommand, out var song))
        {
            if (song.Length == 0) return;
            if (Songs.Count(s => s.Key == key && s.Status is RequestStatus.New or RequestStatus.Queued) >= Math.Max(1, Config.SongMaxPendingPerPerson)) return;
            Songs.Add(new SongRequest { Time = DateTime.Now, Key = key, Name = name, World = world, Song = song, Status = RequestStatus.New });
            if (Config.SongReplyTell) plugin.SendLine($"/tell {Target(name, world)} {Fill(Config.SongReply, name, song: song)}");
            return;
        }

        if (Config.OrdersEnabled && TryCommand(text, Config.MenuCommand, out _))
        {
            plugin.SendLine($"/tell {Target(name, world)} {Fill(Config.MenuShout, name, menu: MenuText())}");
            return;
        }

        if (Config.OrdersEnabled && TryCommand(text, Config.OrderCommand, out var wanted))
        {
            if (wanted.Length == 0) return;
            var item = FindItem(wanted);
            Orders.Add(new DrinkOrder
            {
                Time = DateTime.Now, Key = key, Name = name, World = world, Channel = channel,
                Item = item?.Name ?? wanted, Price = item?.Price ?? 0, Status = OrderStatus.Waiting,
            });
            if (Config.OrderReplyTell) plugin.SendLine($"/tell {Target(name, world)} {Fill(Config.OrderReply, name, item: item?.Name ?? wanted)}");
        }
    }

    private static bool TryCommand(string text, string command, out string rest)
    {
        rest = string.Empty;
        var c = command.Trim();
        if (c.Length == 0 || !text.StartsWith(c, StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Length > c.Length && !char.IsWhiteSpace(text[c.Length])) return false;
        rest = text[c.Length..].Trim();
        return true;
    }

    public DrinkItem? FindItem(string wanted)
    {
        var w = Norm(wanted);
        var avail = Config.Menu.Where(m => !m.OutOfStock).ToList();
        return avail.FirstOrDefault(m => Norm(m.Name) == w)
            ?? avail.FirstOrDefault(m => Norm(m.Name).Contains(w) || w.Contains(Norm(m.Name)))
            ?? avail.OrderBy(m => Distance(Norm(m.Name), w)).FirstOrDefault(m => Distance(Norm(m.Name), w) <= Math.Max(2, w.Length / 4));
    }

    private static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    public string MenuText()
        => string.Join(" | ", Config.Menu.Where(m => !m.OutOfStock).Select(m => m.Price > 0 ? $"{m.Name} {ShortGil(m.Price)}" : m.Name))
           + $" -- order with {Config.OrderCommand} <drink>";

    public static string ShortGil(int g) => g >= 1_000_000 ? $"{g / 1_000_000.0:0.#}m" : g >= 1000 ? $"{g / 1000.0:0.#}k" : g.ToString();

    public void Serve(DrinkOrder o)
    {
        o.Status = OrderStatus.Served;
        o.ServedAt = DateTime.Now;
        if (!string.IsNullOrWhiteSpace(Config.ServeLine)) plugin.SendLine(Fill(Config.ServeLine, o.Name, item: o.Item));
    }

    private bool Listening(XivChatType t, out string channel)
    {
        channel = t switch
        {
            XivChatType.Say => "Say", XivChatType.Yell => "Yell", XivChatType.Shout => "Shout",
            XivChatType.TellIncoming => "Tell", XivChatType.Party => "Party", XivChatType.CrossParty => "Party",
            _ => string.Empty,
        };
        return t switch
        {
            XivChatType.Say => Config.ListenSay,
            XivChatType.Yell => Config.ListenYell,
            XivChatType.Shout => Config.ListenShout,
            XivChatType.TellIncoming => Config.ListenTell,
            XivChatType.Party or XivChatType.CrossParty => Config.ListenParty,
            _ => false,
        };
    }
}
