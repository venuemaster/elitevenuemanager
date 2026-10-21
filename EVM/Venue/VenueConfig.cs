using System;
using System.Collections.Generic;

namespace EVM.Venue;

[Serializable]
public class RegisteredHouse
{
    public ulong Key { get; set; }          // world + district + ward + plot (room ignored, so chambers/basement count)
    public string Label { get; set; } = string.Empty;
    public DateTime Registered { get; set; }
}

[Serializable]
public class VisitorRecord
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int Visits { get; set; }
    public DateTime LastWelcomed { get; set; }
}

[Serializable]
public class AdMessage
{
    public bool Enabled { get; set; } = true;
    public string Text { get; set; } = string.Empty;
}

[Serializable]
public class DrinkItem
{
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool OutOfStock { get; set; }
}

[Serializable]
public class VenueConfig
{
    public string VenueName { get; set; } = "My Venue";
    public List<RegisteredHouse> Houses { get; set; } = new();
    public Dictionary<string, VisitorRecord> Visitors { get; set; } = new();

    // ---- welcome tells ----
    public bool WelcomeEnabled { get; set; } = false;
    public int WelcomeDelaySeconds { get; set; } = 5;
    public int WelcomeCooldownHours { get; set; } = 12;
    public string WelcomeNew { get; set; } = "Welcome to {venue}, {name}! Grab a drink -- type !menu for the menu or !song to request a song.";
    public string WelcomeBack { get; set; } = "Welcome back to {venue}, {name}! Visit #{visits} -- good to see you again.";
    public string NoTellNames { get; set; } = string.Empty;   // comma separated: staff, friends

    // ---- periodic shouts ----
    public bool AdsEnabled { get; set; } = false;
    public int AdsIntervalMinutes { get; set; } = 15;
    public string AdsChannel { get; set; } = "/sh";
    public bool AdsOnlyInHouse { get; set; } = true;
    public List<AdMessage> Ads { get; set; } = new()
    {
        new() { Text = "{venue} is OPEN! Drinks, music and good company -- come hang out!" },
        new() { Text = "Games and giveaways tonight at {venue} -- don't miss out!" },
        new() { Text = "Requests are open at {venue}! Type !song <name> to request a song." },
        new() { Text = "Thirsty? The bar at {venue} is serving -- type !menu to see the drinks." },
    };

    // ---- bartender ----
    public bool OrdersEnabled { get; set; } = true;
    public string OrderCommand { get; set; } = "!order";
    public string MenuCommand { get; set; } = "!menu";
    public bool OrderReplyTell { get; set; } = true;
    public string OrderReply { get; set; } = "Got your order for {item}, {name}! The bartender is on it.";
    public string ServeLine { get; set; } = "/em slides a {item} across the bar to {name}.";
    public string MenuShout { get; set; } = "{venue} MENU: {menu}";
    /// <summary>Paste a link to your menu (Carrd, Imgur, Google Doc...). When set, !menu and "Shout the menu" send the link instead of listing drinks.</summary>
    public string MenuLink { get; set; } = string.Empty;
    public string MenuLinkMessage { get; set; } = "Check out the {venue} menu: {link} -- order with !order <drink>";
    public List<DrinkItem> Menu { get; set; } = new()
    {
        new() { Name = "House Special", Price = 100_000, Description = "Ask the bartender" },
        new() { Name = "Margarita", Price = 50_000, Description = "Tequila, lime, salt rim" },
        new() { Name = "Mojito", Price = 50_000, Description = "Rum, mint, lime" },
        new() { Name = "Whiskey Sour", Price = 60_000, Description = "Whiskey, lemon, a little sweet" },
        new() { Name = "Shot of the Night", Price = 40_000, Description = "Changes every night" },
    };

    // ---- party finder ----
    public int PfListingMinutes { get; set; } = 60;
    public int PfWarnMinutes { get; set; } = 5;
    public bool PfAutoOpen { get; set; } = false;

    // ---- songs ----
    public bool SongsEnabled { get; set; } = true;
    public string SongCommand { get; set; } = "!song";
    public bool SongReplyTell { get; set; } = true;
    public string SongReply { get; set; } = "Got your request, {name}: {song}";
    public int SongMaxPendingPerPerson { get; set; } = 2;

    // ---- where commands are read ----
    public bool ListenSay { get; set; } = true;
    public bool ListenYell { get; set; } = true;
    public bool ListenShout { get; set; } = false;
    public bool ListenTell { get; set; } = true;
    public bool ListenParty { get; set; } = true;
}
