namespace TexasHoldem.UI.Maui;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// The table has one fixed palette and every colour in it is explicit,
		// so the system theme has nothing to contribute. Pinning it stops the
		// template's AppThemeBinding defaults (button disabled colours, label
		// text) from changing under the palette when the tablet is in dark
		// mode. Review, 2026-09-11.
		this.UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}