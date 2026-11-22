using Godot;
using Tabletop.Client.Core;

namespace Tabletop.Client.Table;

/// <summary>The audio buses (Options → Audio): Master and Effects (the table's sounds, SoundPlayer), which sends to Master.
/// <c>default_bus_layout.tres</c> declares them; <see cref="Ensure"/> adds any missing. Volumes are 0–100 linear; Mute in background
/// silences Master while the window has no focus.</summary>
public static class AudioBuses
{
    public const string Master = "Master", Effects = "Effects";

    public static void Ensure()
    {
        if (AudioServer.GetBusIndex(Effects) >= 0) return;
        AudioServer.AddBus();
        var i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, Effects); AudioServer.SetBusSend(i, Master);
    }

    public static void Apply(ClientSettings s, bool focused)
    {
        Set(Master, s.Volume, s.MuteInBackground && !focused);
        Set(Effects, s.EffectsVolume, false);
    }

    static void Set(string bus, int volume, bool mute)
    {
        var i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusVolumeDb(i, volume <= 0 ? -80f : Mathf.LinearToDb(volume / 100f));
        AudioServer.SetBusMute(i, mute || volume <= 0);
    }
}
