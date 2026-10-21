using System;
using Dalamud.Configuration;
using EVM.Venue;

namespace EVM;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Channel for "Shout the menu": /sh, /y, /s ...</summary>
    public string OutputCommand { get; set; } = "/sh";

    /// <summary>Every message waits this long after the previous one so the game's spam limit never blocks it.</summary>
    public float SecondsBetweenMessages { get; set; } = 3.0f;

    public VenueConfig Venue { get; set; } = new();
}
