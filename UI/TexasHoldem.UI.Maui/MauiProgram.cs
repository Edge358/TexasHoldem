using Microsoft.Extensions.Logging;

namespace TexasHoldem.UI.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		ConfigureLabelRendering();
		ConfigureButtonWeight();

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	/// <summary>
	/// Android reserves extra vertical space above and below every line of text, and it
	/// is not symmetric: the gap above the cap height is larger than the gap below the
	/// baseline. Centring a line inside a fixed height box therefore leaves the glyphs
	/// sitting low, which is plainly visible in the 44px card slots. Turning that padding
	/// off makes the text centre on the glyphs instead.
	///
	/// Do not remove this as a tidy-up. The TranslationY on CardSlotLabelStyle in
	/// MainPage.xaml was calibrated with this padding already disabled, so deleting
	/// this silently pushes the hole cards off centre. Verify with
	/// Tools/measure_card_slot.py, which should report 10px above and 9px below.
	/// </summary>
	private static void ConfigureLabelRendering()
	{
#if ANDROID
		Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(
			"NoIncludeFontPadding",
			(handler, view) => handler.PlatformView.SetIncludeFontPadding(false));
#endif
	}

	/// <summary>
	/// Gives the action-bar buttons a genuinely heavy typeface.
	///
	/// This project ships only OpenSans-Regular and OpenSans-Semibold; there is
	/// no Bold face anywhere in it. So FontAttributes="Bold" has no real bold to
	/// resolve to and Android synthesises one, which renders visibly thinner
	/// than a true bold. The user spotted this on 2026-09-10 and it had been
	/// true since the MAUI template was generated.
	///
	/// Rather than add a font file, this uses Roboto Bold, weight 700, which is
	/// built into every Android device. Roboto Black, 900, was also tried on
	/// device; the user chose the lighter one. It is applied only to buttons whose
	/// ClassId is "BarButton", set by the eight action-bar styles in
	/// MainPage.xaml, so the gear, AI, Log and settings-sheet buttons keep the
	/// normal face.
	/// </summary>
	private static void ConfigureButtonWeight()
	{
#if ANDROID
		// Hooked to the Font mapping key, not a custom one. MAUI applies its own
		// typeface whenever the font changes, and LayoutActionBar sets FontSize
		// at runtime; a custom key runs once at handler creation and is then
		// silently overwritten. Verified by measuring glyph ink in a screenshot:
		// with a custom key the before and after images were pixel-identical.
		Microsoft.Maui.Handlers.ButtonHandler.Mapper.AppendToMapping(
			nameof(Microsoft.Maui.ITextStyle.Font),
			(handler, view) =>
			{
				if (view is Button button && button.ClassId == "BarButton")
				{
					// Three rungs exist here and only three: the project's own
					// OpenSans with a synthesised bold (lightest), Roboto Bold
					// at weight 700, and Roboto Black at 900. The user picked
					// the middle rung on 2026-09-10.
					handler.PlatformView.Typeface = Android.Graphics.Typeface.Create(
						"sans-serif",
						Android.Graphics.TypefaceStyle.Bold);
				}
			});
#endif
	}
}
