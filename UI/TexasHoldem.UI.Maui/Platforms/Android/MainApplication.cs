using Android.App;
using Android.Runtime;

namespace TexasHoldem.UI.Maui;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	/// <summary>
	/// See FixedConfiguration. The activity wraps its own context; this seeds
	/// MAUI's density cache before anything else can. AttachBaseContext cannot
	/// be overridden here: .NET for Android has not started when Android calls
	/// it, and the override crashed on launch with UnsatisfiedLinkError
	/// (2026-09-11).
	/// </summary>
	public override void OnCreate()
	{
		FixedConfiguration.SeedMauiDensity(this);
		base.OnCreate();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
