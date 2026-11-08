using Android.Content;
using Android.Content.Res;
using Android.Util;
using Microsoft.Maui.Platform;

namespace TexasHoldem.UI.Maui;

/// <summary>
/// Makes the app immune to the tablet's Display size and Font size settings.
///
/// The activity's context is wrapped so that it reports the device's
/// physical density and a font scale of 1.0, whatever the user has chosen in
/// Settings, and MAUI's own density is seeded to match. Every layout is
/// then measured exactly as it is on the emulator,
/// and the tablet's settings stop being an input to the game. The home screen
/// and every other app keep the user's settings; only this app ignores them.
///
/// Two places apply it, and both are needed. MainActivity wraps its base
/// context, which pins everything measured through the activity, including
/// every font size. MainApplication seeds MAUI's density cache in OnCreate:
/// MAUI reads pixels-per-unit once, from whichever context asks first, and
/// keeps that value for the life of the process (ContextExtensions in
/// Microsoft.Maui, s_displayDensity). With the activity alone the font scale
/// was fixed but the action bar stayed at the enlarged density, and pinning
/// the application's Resources in place worked on the emulator but not on
/// the Lenovo, where the system had reverted it before MAUI's first read.
/// Asking first, from a wrapped context, does not depend on that ordering.
/// Verified on the emulator on 2026-09-11 with `wm density 382` and
/// `font_scale 1.3`, then on the tablet.
///
/// Found on the tablet on 2026-09-11: with Display size and Font size at
/// maximum the bar grew to 158 physical pixels, "Start Game" clipped to
/// "Start", and the seat 4 and 5 boxes ran under the bar. Nothing here names
/// the tablet's numbers; DensityDeviceStable is whatever the running device
/// really is.
/// </summary>
internal static class FixedConfiguration
{
    public static Context? Wrap(Context? context)
    {
        if (context?.Resources?.Configuration is not { } current)
        {
            return context;
        }

        var configuration = Pin(current);

        return context.CreateConfigurationContext(configuration) ?? context;
    }

    /// <summary>
    /// Fills MAUI's process-wide density cache from a wrapped context, so
    /// that every later conversion uses the physical density. Must run before
    /// anything in MAUI converts a size; MainApplication.OnCreate is the
    /// earliest managed code there is.
    /// </summary>
    public static void SeedMauiDensity(Context context)
    {
        Wrap(context).GetDisplayDensity();
    }

    /// <summary>
    /// The physical density is only reported from Android 7 (API 24). The
    /// project's minimum is 21, so below that only the font scale is pinned.
    /// Every device this app is meant for is far newer.
    /// </summary>
    private static Configuration Pin(Configuration current)
    {
        var configuration = new Configuration(current)
        {
            FontScale = 1.0f,
        };

        if (OperatingSystem.IsAndroidVersionAtLeast(24))
        {
            configuration.DensityDpi = DisplayMetrics.DensityDeviceStable;
        }

        return configuration;
    }
}
