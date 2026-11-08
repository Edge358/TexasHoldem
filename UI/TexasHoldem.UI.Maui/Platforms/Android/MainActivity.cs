using Android.App;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace TexasHoldem.UI.Maui;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// See FixedConfiguration. The application seeds MAUI's density to match.
    /// </summary>
    protected override void AttachBaseContext(Android.Content.Context? baseContext)
    {
        base.AttachBaseContext(FixedConfiguration.Wrap(baseContext));
    }

    /// <summary>
    /// Phase 6, the touch pass. Three device behaviours, all approved by the
    /// user on 2026-09-10:
    ///
    /// 1. Landscape only. The tablet lives in a folio stand and is never
    ///    rotated. SensorLandscape rather than Landscape so a 180-degree flip
    ///    still works and the charging port can face either way.
    /// 2. The screen stays on. The player can sit and think for minutes at a
    ///    time and Android's display timeout would blank the table mid-hand.
    ///    The flag is scoped to this window, so leaving the app restores the
    ///    device's normal timeout.
    /// 3. Immersive fullscreen. The status bar owned the top 72 physical
    ///    pixels and clipped the table's top-left corner, where the AI
    ///    decision-log toggle sat until Phase 6 removed it.
    /// </summary>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        this.Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        // Android still reserves the hidden status bar's inset in some layout
        // passes, and the default window background behind it is white. Paint
        // it the same burgundy as the page so any uncovered band disappears.
        this.Window?.SetBackgroundDrawable(new ColorDrawable(Android.Graphics.Color.ParseColor("#2F151A")));

        this.ApplyImmersiveMode();
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Android restores the system bars whenever the activity comes back to
        // the foreground, so immersive mode has to be re-applied rather than
        // set once in OnCreate.
        this.ApplyImmersiveMode();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        if (hasFocus)
        {
            this.ApplyImmersiveMode();
        }
    }

    /// <summary>
    /// Ends the process when the activity is finished, which is what Back at
    /// the root does.
    ///
    /// The game engine runs on a dedicated thread that blocks in RequestAction
    /// and WaitForNextHand until a button on this page releases it. It has no
    /// cancellation path. If the activity finished but the process lived on,
    /// as Android normally arranges, the engine stayed parked against a page
    /// that no longer existed, and the next launch built a second page and a
    /// second game on top of it, one leaked thread and one leaked page per
    /// Back press. With the process gone the next launch is a clean start.
    /// Settings are written to Preferences the moment they change, and Android
    /// flushes pending Preferences writes before OnDestroy, so nothing is
    /// lost. Found by the 2026-09-11 review.
    ///
    /// Home and the recents screen do not finish the activity, so they still
    /// leave the game where it was.
    /// </summary>
    protected override void OnDestroy()
    {
        var finishing = this.IsFinishing;
        base.OnDestroy();

        if (finishing)
        {
            Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
        }
    }

    private void ApplyImmersiveMode()
    {
        var window = this.Window;
        if (window is null)
        {
            return;
        }

        WindowCompat.SetDecorFitsSystemWindows(window, false);

        var controller = WindowCompat.GetInsetsController(window, window.DecorView);
        if (controller is null)
        {
            return;
        }

        controller.Hide(WindowInsetsCompat.Type.SystemBars());

        // A swipe from an edge reveals the bars briefly and then hides them
        // again on its own. Without this the bars would stay up for good after
        // the first accidental swipe.
        controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
    }
}
