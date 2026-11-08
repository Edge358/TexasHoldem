namespace TexasHoldem.UI.Maui;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using TexasHoldem.AI.DummyPlayer;
using TexasHoldem.AI.SmartPlayer;
using TexasHoldem.AI.SmartPlayer.Decision;
using TexasHoldem.AI.SmartPlayer.Models;
using TexasHoldem.Logic;
using TexasHoldem.Logic.GameMechanics;
using TexasHoldem.Logic.Players;
using TexasHoldem.Presentation;

/// <summary>
/// Port of MainWindow.xaml.cs. The game flow, the background engine task and the
/// TaskCompletionSource gating are copied from the WPF code-behind; only the control
/// calls differ. Windows-only members are not ported: window placement, hotkey
/// underlines, seat hover, and the seat archetype menus.
///
/// The layout lives in MainPage.xaml and holds the WPF Canvas coordinates verbatim.
/// </summary>
public partial class MainPage : ContentPage, IHumanPlayerController, IHandPauseController
{
    private const int SeatCount = 8;

    private const double DesignWidth = 1280;
    private const double DesignHeight = 720;

    private readonly Label[] seatNameLabels = new Label[SeatCount];
    private readonly Label[] seatStackLabels = new Label[SeatCount];
    private readonly Label[] seatCardLabels = new Label[SeatCount];
    private readonly Label[] seatBetLabels = new Label[SeatCount];
    private readonly Label[] seatStatusLabels = new Label[SeatCount];
    private readonly Border[] seatInfoBorders = new Border[SeatCount];
    private readonly Border[] seatCardBorders = new Border[SeatCount];

    private readonly List<GamePreset> gamePresets = new List<GamePreset>();
    private readonly List<CardVisibilityOption> cardVisibilityOptions = new List<CardVisibilityOption>();

    private MauiTableView tableView = null!;
    private Task? gameTask;
    private MauiHumanPlayer? humanPlayer;

    // The six fields below are written on one thread and read on the other: the
    // engine's background task sets them in RequestAction and WaitForNextHand, the UI
    // thread reads and clears them in the button handlers, and both directions matter.
    // WPF has the same pattern and gets away with it because it only ever runs on
    // x86/x64, where the strong memory model hides the problem. The tablet is arm64,
    // which is weakly ordered, and the x86_64 emulator cannot reproduce a stale read.
    // The failure would look like a tap on Fold doing nothing. volatile is free here.
    private volatile TaskCompletionSource<PlayerAction>? pendingAction;
    private volatile IGetTurnContext? pendingContext;
    private volatile TaskCompletionSource<bool>? nextHandTcs;
    private volatile bool skipNextHandPause;
    private volatile bool pausePending;
    private volatile bool restartRequested;
    private GamePreset? selectedGamePreset;

    // The preset the running game was started with. Back to Game compares it
    // to selectedGamePreset to decide whether the stakes actually changed;
    // null while no game is running. Session 10, 2026-09-12.
    private GamePreset? runningGamePreset;

    private int selectedGameSizeIndex;

    private int selectedCardVisibilityIndex;

    // Which decision is on screen, and when it appeared. Written and read only
    // on the UI thread, so neither needs to be volatile. See SubmitAction.
    private TaskCompletionSource<PlayerAction>? shownRequest;

    private long shownAtMs;

    private long lastNextHandTapMs;

    /// <summary>
    /// How long a new decision must have been on screen before a tap can answer
    /// it, and the minimum gap between two Next Hand taps.
    ///
    /// This exists for the bounce of a tremor or a stylus, two touches a few
    /// tens of milliseconds apart, where the second one would answer a decision
    /// the player has not seen. It was 400 ms until the 2026-09-11 review
    /// showed the cost: the AI seats act with no delay, so a player tapping
    /// Fold through hand after hand meets the next decision within
    /// milliseconds, and a tap inside the window is dropped with no feedback.
    /// 250 ms still swallows a bounce and drops far fewer real taps.
    /// </summary>
    private const long MinimumDecisionMs = 250;
    private string? restartMessage;
    private CardVisibilityOption? selectedCardVisibility;

    public MainPage()
    {
        this.InitializeComponent();
        this.CollectSeatControls();

        this.tableView = new MauiTableView(
            this.seatNameLabels,
            this.seatStackLabels,
            this.seatCardLabels,
            this.seatInfoBorders,
            this.seatCardBorders,
            this.seatBetLabels,
            this.seatStatusLabels,
            this.CommunityCardsLabel,
            this.PotLabel,
            this.ActionLogLabel,
            this.ActionLogScroll,
            this.TurnInfoLabel,
            this.DealerButton,
            this.HandCounterLabel,
            this.BuyInCounterLabel,
            this.AiRecommendationLabel);

        this.SetActionControlsEnabled(false);
        this.NextHandButton.IsEnabled = true;
        this.SetNextHandHighlight(true);
        this.InitializeGamePresets();
        this.InitializeCardVisibilityOptions();
        this.UpdateGameSizeAvailability();
        this.LoadSettings();
        this.LayoutActionBar();

        for (var seatIndex = 1; seatIndex <= SeatCount; seatIndex++)
        {
            this.tableView.SetSeatName(seatIndex, $"Player{seatIndex}");
            this.tableView.SetSeatStatus(seatIndex, "Waiting");
        }
    }

    public PlayerAction RequestAction(IGetTurnContext context)
    {
        var tcs = new TaskCompletionSource<PlayerAction>();
        this.pendingContext = context;
        this.pendingAction = tcs;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Record exactly which decision is being shown, and when, so
            // SubmitAction can refuse a tap meant for an earlier one.
            this.shownRequest = tcs;
            this.shownAtMs = Environment.TickCount64;
            this.ShowTurnControls(context);
        });
        var action = tcs.Task.GetAwaiter().GetResult();
        MainThread.BeginInvokeOnMainThread(this.HideTurnControls);
        return action;
    }

    public void WaitForNextHand()
    {
        if (!this.pausePending)
        {
            return;
        }

        if (this.skipNextHandPause)
        {
            this.skipNextHandPause = false;
            this.pausePending = false;
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        this.nextHandTcs = tcs;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            this.tableView.SetTurnInfo("Waiting for next hand...");

            // Both Folds stay tappable so either one advances the hand, but
            // painted as inactive so they do not compete with Next Hand. See
            // PaintFoldButtons. Until the 2026-09-11 review only the right
            // Fold was re-enabled here; the left one was grey and dead between
            // hands, unlike Windows, where both deal the next hand.
            this.FoldButton.IsEnabled = true;
            this.FoldLeftButton.IsEnabled = true;
            this.PaintFoldButtons(false);
            this.SetNextHandHighlight(true);
            this.UpdateGameSizeAvailability();
        });

        tcs.Task.GetAwaiter().GetResult();
        this.nextHandTcs = null;

        if (this.restartRequested)
        {
            this.pausePending = false;
            this.skipNextHandPause = false;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                this.FoldButton.IsEnabled = false;
                this.FoldLeftButton.IsEnabled = false;
                this.SetNextHandHighlight(false);
                this.UpdateGameSizeAvailability();
            });
            throw new OperationCanceledException("Game reset.");
        }

        // Mark the hand live before queuing the UI update, not after, so the
        // queued UpdateGameSizeAvailability never sees a stale flag.
        //
        // skipNextHandPause is cleared here too. A Next Hand tap that lands
        // between the skip check at the top of this method and the assignment
        // of nextHandTcs sets the flag but does not release anything, so the
        // engine parks anyway and the player taps again. Without this line the
        // stale flag would then skip the following hand's showdown.
        this.pausePending = false;
        this.skipNextHandPause = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            this.tableView.SetTurnInfo("Waiting for other players...");
            this.FoldButton.IsEnabled = false;
            this.FoldLeftButton.IsEnabled = false;
            this.SetNextHandHighlight(false);
            this.UpdateGameSizeAvailability();
        });
    }

    public void RequestHandPause()
    {
        this.pausePending = true;
    }

    private static string GetArchetypeLabel(PlayerArchetype archetype)
    {
        switch (archetype)
        {
            case PlayerArchetype.CallingStation:
                return "CS";
            case PlayerArchetype.WeakTight:
                return "WT";
            case PlayerArchetype.AggroFish:
                return "AF";
            case PlayerArchetype.Maniac:
                return "M";
            case PlayerArchetype.LAG:
                return "LAG";
            case PlayerArchetype.TAG:
                return "TAG";
            case PlayerArchetype.Rock:
                return "Rock";
            case PlayerArchetype.Nit:
                return "Nit";
            case PlayerArchetype.Reg:
                return "Reg";
            default:
                return archetype.ToString();
        }
    }

    private static string GetFacingLabel(IGetTurnContext context)
    {
        if (context.MoneyToCall == 0)
        {
            return "Checked:";
        }

        var raiseCount = context.PreviousRoundActions?.Count(entry => entry.Action.Type == PlayerActionType.Raise) ?? 0;
        if (context.RoundType == GameRoundType.PreFlop && raiseCount == 0)
        {
            return "Limped:";
        }

        if (context.RoundType != GameRoundType.PreFlop && raiseCount <= 1 && context.MoneyToCall == context.CurrentMaxBet)
        {
            return "Bet:";
        }

        if (raiseCount <= 1)
        {
            return "Raised:";
        }

        return "Re-Raised:";
    }

    private static GamePreset CreatePreset(string label, int smallBlind)
    {
        var bigBlind = smallBlind * 2;
        var startingStack = bigBlind * 100;
        return new GamePreset(label, smallBlind, bigBlind, startingStack);
    }

    private static PlayerProfile CreateProfile(PlayerArchetype archetype)
    {
        switch (archetype)
        {
            case PlayerArchetype.Rock:
                return PlayerProfile.CreateRock();
            case PlayerArchetype.Nit:
                return PlayerProfile.CreateNit();
            case PlayerArchetype.WeakTight:
                return PlayerProfile.CreateWeakTight();
            case PlayerArchetype.CallingStation:
                return PlayerProfile.CreateCallingStation();
            case PlayerArchetype.TAG:
                return PlayerProfile.CreateTag();
            case PlayerArchetype.LAG:
                return PlayerProfile.CreateLag();
            case PlayerArchetype.Maniac:
                return PlayerProfile.CreateManiac();
            case PlayerArchetype.AggroFish:
                return PlayerProfile.CreateAggroFish();
            case PlayerArchetype.Reg:
                return PlayerProfile.CreateReg();
            default:
                return PlayerProfile.CreateReg();
        }
    }

    private void CollectSeatControls()
    {
        var nameLabels = new[]
        {
            this.Seat1NameLabel, this.Seat2NameLabel, this.Seat3NameLabel, this.Seat4NameLabel,
            this.Seat5NameLabel, this.Seat6NameLabel, this.Seat7NameLabel, this.Seat8NameLabel,
        };

        var stackLabels = new[]
        {
            this.Seat1StackLabel, this.Seat2StackLabel, this.Seat3StackLabel, this.Seat4StackLabel,
            this.Seat5StackLabel, this.Seat6StackLabel, this.Seat7StackLabel, this.Seat8StackLabel,
        };

        var cardLabels = new[]
        {
            this.Seat1CardsLabel, this.Seat2CardsLabel, this.Seat3CardsLabel, this.Seat4CardsLabel,
            this.Seat5CardsLabel, this.Seat6CardsLabel, this.Seat7CardsLabel, this.Seat8CardsLabel,
        };

        var betLabels = new[]
        {
            this.Seat1BetLabel, this.Seat2BetLabel, this.Seat3BetLabel, this.Seat4BetLabel,
            this.Seat5BetLabel, this.Seat6BetLabel, this.Seat7BetLabel, this.Seat8BetLabel,
        };

        var statusLabels = new[]
        {
            this.Seat1StatusLabel, this.Seat2StatusLabel, this.Seat3StatusLabel, this.Seat4StatusLabel,
            this.Seat5StatusLabel, this.Seat6StatusLabel, this.Seat7StatusLabel, this.Seat8StatusLabel,
        };

        var infoBorders = new[]
        {
            this.Seat1InfoBorder, this.Seat2InfoBorder, this.Seat3InfoBorder, this.Seat4InfoBorder,
            this.Seat5InfoBorder, this.Seat6InfoBorder, this.Seat7InfoBorder, this.Seat8InfoBorder,
        };

        var cardBorders = new[]
        {
            this.Seat1CardsBorder, this.Seat2CardsBorder, this.Seat3CardsBorder, this.Seat4CardsBorder,
            this.Seat5CardsBorder, this.Seat6CardsBorder, this.Seat7CardsBorder, this.Seat8CardsBorder,
        };

        for (var i = 0; i < SeatCount; i++)
        {
            this.seatNameLabels[i] = nameLabels[i];
            this.seatStackLabels[i] = stackLabels[i];
            this.seatCardLabels[i] = cardLabels[i];
            this.seatBetLabels[i] = betLabels[i];
            this.seatStatusLabels[i] = statusLabels[i];
            this.seatInfoBorders[i] = infoBorders[i];
            this.seatCardBorders[i] = cardBorders[i];
        }
    }

    private void TableHost_SizeChanged(object? sender, EventArgs e)
    {
        // The WPF Viewbox with Stretch="Uniform" in one line: scale the fixed design
        // surface by the smaller ratio and let the gradient show in the bands.
        var width = this.TableHost.Width;
        var height = this.TableHost.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        this.DesignSurface.Scale = Math.Min(width / DesignWidth, height / DesignHeight);
    }

    // ---------------------------------------------------------------------
    // Phase 6, the settings sheet.
    //
    // The card-visibility and game-size pickers used to sit on the table
    // surface and could not reach the 12 mm touch target there, so they live
    // behind the gear at top right. The four optional raise-size buttons are
    // switched on and off from the same sheet; the nine the user made mandatory
    // are always present.
    //
    // Two controls that were in this sheet are gone entirely. Show Raise went
    // with the Raise box it governed. The AI decision-log toggle went because
    // it did nothing on Android: DecisionLogger writes to the relative path
    // "ai_decisions.log", which an Android app cannot create, and the failure
    // is swallowed. Confirmed on device 2026-09-10, no such file in the app's
    // data directory. It remains a Windows-only developer feature.
    // ---------------------------------------------------------------------

    private void SettingsButton_Clicked(object? sender, EventArgs e)
    {
        this.UpdateGameSizeAvailability();
        this.SettingsOverlay.IsVisible = true;
    }

    /// <summary>
    /// Back to Game is where a stakes change takes effect. Tapping a stakes
    /// tile only lights it, so the sheet can be explored and put back with no
    /// harm done; only leaving the sheet with a tile lit that differs from the
    /// running game's stakes stops that game. The tiles are dimmed and inert
    /// during a live hand, so a differing selection can only exist between
    /// hands, where RequestGameRestart has a parked wait to release. Session
    /// 10, 2026-09-12.
    /// </summary>
    private void SettingsDoneButton_Clicked(object? sender, EventArgs e)
    {
        this.SettingsOverlay.IsVisible = false;

        var selected = this.selectedGamePreset;
        if (this.gameTask == null || selected == null || ReferenceEquals(selected, this.runningGamePreset))
        {
            return;
        }

        this.restartMessage = $"Game size set to {selected.Label}. Tap Start Game to begin.";
        this.RequestGameRestart();
    }

    /// <summary>
    /// Closes the app. Finishing the activity is what Back does at the root,
    /// and MainActivity.OnDestroy then ends the process, so the game is gone
    /// rather than parked in the background. Settings are already saved the
    /// moment they change, so there is nothing to flush here.
    /// </summary>
    private void QuitButton_Clicked(object? sender, EventArgs e)
    {
#if ANDROID
        Platform.CurrentActivity?.FinishAffinity();
#else
        Application.Current?.Quit();
#endif
    }

    private void SettingsOverlay_Tapped(object? sender, TappedEventArgs e)
    {
        // A tap on the dimmed backdrop closes the sheet. The panel itself
        // carries an empty recognizer so its own taps never arrive here.
        this.SettingsOverlay.IsVisible = false;
    }

    private void OneThirdToggle_Tapped(object? sender, TappedEventArgs e)
    {
        this.SetExtraRaiseButton(this.OneThirdPotButton, this.OneThirdToggle, this.OneThirdToggleLabel);
    }

    private void TwoThirdsToggle_Tapped(object? sender, TappedEventArgs e)
    {
        this.SetExtraRaiseButton(this.TwoThirdsPotButton, this.TwoThirdsToggle, this.TwoThirdsToggleLabel);
    }

    private void OnePointFiveToggle_Tapped(object? sender, TappedEventArgs e)
    {
        this.SetExtraRaiseButton(this.OnePointFivePotButton, this.OnePointFiveToggle, this.OnePointFiveToggleLabel);
    }

    private void ThreeXToggle_Tapped(object? sender, TappedEventArgs e)
    {
        this.SetExtraRaiseButton(this.ThreeXPotButton, this.ThreeXToggle, this.ThreeXToggleLabel);
    }

    /// <summary>
    /// Flips one optional raise-size button on or off and repaints its tile in
    /// the settings sheet so the tile reads like the button it controls.
    /// </summary>
    private void SetExtraRaiseButton(Button target, Border tile, Label tileLabel)
    {
        this.ApplyExtraRaiseButton(target, tile, tileLabel, !target.IsVisible);
        this.SaveSettings();
    }

    private void ApplyExtraRaiseButton(Button target, Border tile, Label tileLabel, bool enabled)
    {
        target.IsVisible = enabled;

        tile.BackgroundColor = enabled ? Color.FromArgb("#4B2A31") : Color.FromArgb("#1C1416");
        tile.Stroke = enabled ? Color.FromArgb("#B98A92") : Color.FromArgb("#4D3A3E");
        tileLabel.TextColor = enabled ? Color.FromArgb("#F2F2F2") : Color.FromArgb("#8A7A7D");

        this.LayoutActionBar();
    }

    // ---------------------------------------------------------------------
    // Settings persistence.
    //
    // Six values, held in MAUI Preferences. The two picker keys are spelled the
    // same as the WPF app's Properties.Settings entries so the two apps stay
    // legible side by side; the four button keys have no Windows equivalent,
    // because Windows shows all seven raise sizes all the time.
    //
    // Deliberately NOT persisted: the AI and Log panel toggles. Those are
    // transient view state, and a log panel left open by accident should not
    // still be covering the table on the next launch.
    // ---------------------------------------------------------------------

    private const string GameSizeKey = "GameSizeSelectionIndex";
    private const string CardVisibilityKey = "CardVisibilitySelectionIndex";
    private const string ShowOneThirdKey = "ShowOneThirdPot";
    private const string ShowTwoThirdsKey = "ShowTwoThirdsPot";
    private const string ShowOnePointFiveKey = "ShowOnePointFivePot";
    private const string ShowThreeXKey = "ShowThreeXPot";

    /// <summary>
    /// Applies the saved settings. Called once from the constructor, after the
    /// preset and card-visibility lists are built and before the first layout
    /// pass.
    ///
    /// It sets the selection indices directly and repaints the tiles, rather
    /// than going through SelectGameSize and SelectCardVisibility, so a restore
    /// never looks like the user tapping a tile: SelectGameSize refuses changes
    /// during a hand and would otherwise reject it.
    /// </summary>
    private void LoadSettings()
    {
        var cardIndex = Preferences.Get(CardVisibilityKey, 0);
        if (cardIndex >= 0 && cardIndex < this.cardVisibilityOptions.Count)
        {
            this.selectedCardVisibilityIndex = cardIndex;
            this.selectedCardVisibility = this.cardVisibilityOptions[cardIndex];
            this.tableView.SetCardVisibility(this.selectedCardVisibility.Mode, this.selectedCardVisibility.Reveal);
        }

        var sizeIndex = Preferences.Get(GameSizeKey, 0);
        if (sizeIndex >= 0 && sizeIndex < this.gamePresets.Count)
        {
            this.selectedGameSizeIndex = sizeIndex;
            this.selectedGamePreset = this.gamePresets[sizeIndex];
        }

        this.UpdateStakesLabel();
        this.PaintChoiceTiles();

        this.ApplyExtraRaiseButton(
            this.OneThirdPotButton, this.OneThirdToggle, this.OneThirdToggleLabel,
            Preferences.Get(ShowOneThirdKey, false));
        this.ApplyExtraRaiseButton(
            this.TwoThirdsPotButton, this.TwoThirdsToggle, this.TwoThirdsToggleLabel,
            Preferences.Get(ShowTwoThirdsKey, false));
        this.ApplyExtraRaiseButton(
            this.OnePointFivePotButton, this.OnePointFiveToggle, this.OnePointFiveToggleLabel,
            Preferences.Get(ShowOnePointFiveKey, false));
        this.ApplyExtraRaiseButton(
            this.ThreeXPotButton, this.ThreeXToggle, this.ThreeXToggleLabel,
            Preferences.Get(ShowThreeXKey, false));
    }

    private void SaveSettings()
    {
        Preferences.Set(GameSizeKey, this.selectedGameSizeIndex);
        Preferences.Set(CardVisibilityKey, this.selectedCardVisibilityIndex);
        Preferences.Set(ShowOneThirdKey, this.OneThirdPotButton.IsVisible);
        Preferences.Set(ShowTwoThirdsKey, this.TwoThirdsPotButton.IsVisible);
        Preferences.Set(ShowOnePointFiveKey, this.OnePointFivePotButton.IsVisible);
        Preferences.Set(ShowThreeXKey, this.ThreeXPotButton.IsVisible);
    }

    /// <summary>
    /// Lays the action bar out as one row of identical buttons.
    ///
    /// Two things force this to be computed rather than declared. First, a
    /// HorizontalStackLayout that runs out of width does not shrink its
    /// children, it drops the trailing ones: measured on the tablet, switching on
    /// all four optional raise sizes and the Raise box pushed the bar past
    /// 2560 px and Fold and Start Game vanished off the right edge, silently,
    /// Start Game being the control that starts the game and advances every
    /// hand. Second, the user asked for every button to be the same width, and
    /// the width that achieves that depends on how many are switched on.
    ///
    /// So the row is measured: the available width is divided evenly between
    /// whatever is visible, and the font is scaled down only as far as the
    /// longest label ("Check/Call") needs to stay inside its button. Nine
    /// buttons land at 270 px each at the full font; the full thirteen land
    /// at 182 px, where the font comes down to about 15.
    ///
    /// Heights never change. Every button stays at 66 units, which is 132
    /// physical pixels or 12.2 mm on the tablet, and that is the acceptance
    /// criterion. If you add a control to this bar, re-measure both extremes.
    /// </summary>
    private void LayoutActionBar()
    {
        var hostWidth = this.ActionBarHost.Width;
        if (hostWidth <= 0)
        {
            return;
        }

        var controls = new List<View>();
        foreach (var child in this.ActionBar.Children)
        {
            if (child is View view && view.IsVisible)
            {
                controls.Add(view);
            }
        }

        if (controls.Count == 0)
        {
            return;
        }

        const double Gap = 6d;
        const double HostPadding = 6d;

        // "Check/Call" is the longest label in the bar. At font size 18 it
        // measured 93 device-independent units wide, so roughly 5.2 units per
        // point of font size; the +12 leaves a little padding either side.
        const double LongestLabelUnitsPerPoint = 5.2d;
        const double LabelPadding = 12d;

        // Two units of slack, then floor. MAUI rounds each width up to a whole
        // pixel, so nine buttons that each gain a fraction push the last one off
        // the right edge and it gets squeezed instead.
        var available = hostWidth - (HostPadding * 2) - 2d;
        var width = Math.Floor((available - (Gap * (controls.Count - 1))) / controls.Count);
        if (width <= 0)
        {
            return;
        }

        var fontSize = (width - LabelPadding) / LongestLabelUnitsPerPoint;
        // Capped at 18. The formula above returns the largest size that would
        // fit the widest label, which at nine buttons is about 24 -- tried on
        // device 2026-09-10 and rejected as far too big. 18 in a 270 px button
        // can read as less bold than the original 14 did in a 127 px one, but
        // that is the buttons having grown, which was the point of the phase.
        fontSize = Math.Max(12d, Math.Min(18d, fontSize));

        for (var i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            control.WidthRequest = width;
            control.MinimumWidthRequest = width;
            control.Margin = new Thickness(0, 0, i == controls.Count - 1 ? 0 : Gap, 0);

            if (control is Button button)
            {
                button.Padding = new Thickness(2, 0);
                button.FontSize = fontSize;
            }
        }
    }

    private void ActionBarHost_SizeChanged(object? sender, EventArgs e)
    {
        this.LayoutActionBar();
    }

    private void AiRecommendationToggle_Clicked(object? sender, EventArgs e)
    {
        this.AiRecommendationPanel.IsVisible = !this.AiRecommendationPanel.IsVisible;
    }

    private void ActionLogToggle_Clicked(object? sender, EventArgs e)
    {
        this.ActionLogPanel.IsVisible = !this.ActionLogPanel.IsVisible;
    }

    private void StartGame()
    {
        if (this.gameTask != null)
        {
            return;
        }

        var preset = this.selectedGamePreset ?? this.gamePresets[0];
        this.runningGamePreset = preset;
        this.restartRequested = false;
        this.restartMessage = null;
        this.tableView.ClearLog();
        this.tableView.SetTurnInfo("Starting game...");
        this.NextHandButton.IsEnabled = true;
        this.NextHandButton.Text = "Next Hand";
        this.SetNextHandHighlight(false);

        var players = this.CreatePlayers();
        var game = new TexasHoldemGame(players, preset.StartingStack, preset.SmallBlind, preset.StartingStack);

        // LongRunning: the engine blocks this thread for the life of the game,
        // in RequestAction and WaitForNextHand. On a pool thread that is one
        // pool worker gone for good; a dedicated thread costs nothing.
        this.gameTask = Task.Factory.StartNew(
            () =>
        {
            var wasRestart = false;
            try
            {
                game.Start();
            }
            catch (OperationCanceledException)
            {
                wasRestart = true;
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    this.tableView.SetTurnInfo($"Game stopped: {ex.Message}");
                    this.SetActionControlsEnabled(false);
                });
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() => this.OnGameStopped(wasRestart));
            }
        },
            TaskCreationOptions.LongRunning);

        // After gameTask is assigned, not before: IsGameSizeChangeAllowed treats
        // a null gameTask as "no game running", which left the stakes tiles lit
        // for the whole first hand.
        this.UpdateGameSizeAvailability();
    }

    private IList<IPlayer> CreatePlayers()
    {
        var seats = new List<IPlayer>();
        this.humanPlayer = new MauiHumanPlayer("You", -1, this);

        seats.Add(new SmartPlayer(PlayerProfile.CreateRock()));
        seats.Add(new SmartPlayer(PlayerProfile.CreateNit()));
        seats.Add(new SmartPlayer(PlayerProfile.CreateWeakTight()));
        seats.Add(this.humanPlayer);
        seats.Add(new SmartPlayer(PlayerProfile.CreateCallingStation()));
        seats.Add(new SmartPlayer(PlayerProfile.CreateTag()));
        seats.Add(new SmartPlayer(PlayerProfile.CreateLag()));
        seats.Add(new SmartPlayer(PlayerProfile.CreateAggroFish()));

        var decorated = new List<IPlayer>();
        for (var i = 0; i < seats.Count; i++)
        {
            var seatIndex = i + 1;
            this.tableView.SetSeatName(seatIndex, this.GetDisplayName(seatIndex, seats[i]));
            this.tableView.SetSeatStatus(seatIndex, "Waiting");
            decorated.Add(new UiDecorator(seats[i], seatIndex, this.tableView, this));
        }

        return decorated;
    }

    private string GetDisplayName(int seatIndex, IPlayer player)
    {
        var suffix = player switch
        {
            SmartPlayer smartPlayer => GetArchetypeLabel(smartPlayer.Archetype),
            DummyPlayer => "D",
            MauiHumanPlayer => "H",
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(suffix)
            ? $"Player{seatIndex}"
            : $"Player{seatIndex} {suffix}";
    }

    private void ShowTurnControls(IGetTurnContext context)
    {
        this.SetActionControlsEnabled(true);
        var checkCallLabel = context.MoneyToCall > 0
            ? $"Call {context.MoneyToCall}"
            : "Check";
        this.SetCheckCallLabel(checkCallLabel);
        var canRaise = context.CanRaise;
        this.OneThirdPotButton.IsEnabled = canRaise;
        this.HalfPotButton.IsEnabled = canRaise;
        this.TwoThirdsPotButton.IsEnabled = canRaise;
        this.PotButton.IsEnabled = canRaise;
        this.OnePointFivePotButton.IsEnabled = canRaise;
        this.TwoXPotButton.IsEnabled = canRaise;
        this.ThreeXPotButton.IsEnabled = canRaise;
        this.AllInButton.IsEnabled = context.MoneyLeft > 0;
        var facingLabel = GetFacingLabel(context);
        this.tableView.SetTurnInfo(
            $"{facingLabel}     Call:  {context.MoneyToCall}     MRaise:  {context.MinRaise}");
    }

    private void SetCheckCallLabel(string label)
    {
        this.CheckCallButton.Text = label;
        this.CheckCallRightButton.Text = label;
    }

    private void HideTurnControls()
    {
        this.SetActionControlsEnabled(false);
        this.tableView.SetTurnInfo("Waiting for other players...");
    }

    private void SetActionControlsEnabled(bool enabled)
    {
        this.CheckCallButton.IsEnabled = enabled;
        this.CheckCallRightButton.IsEnabled = enabled;
        this.OneThirdPotButton.IsEnabled = enabled;
        this.HalfPotButton.IsEnabled = enabled;
        this.TwoThirdsPotButton.IsEnabled = enabled;
        this.PotButton.IsEnabled = enabled;
        this.OnePointFivePotButton.IsEnabled = enabled;
        this.TwoXPotButton.IsEnabled = enabled;
        this.ThreeXPotButton.IsEnabled = enabled;
        this.AllInButton.IsEnabled = enabled;
        this.FoldButton.IsEnabled = enabled;
        this.FoldLeftButton.IsEnabled = enabled;
        this.PaintFoldButtons(enabled);
    }

    /// <summary>
    /// Paints both Fold buttons, rather than leaving them to visual states.
    ///
    /// They have to be painted by hand because between hands they are a special
    /// case: **still tappable, but shown as inactive.** Tapping either there
    /// advances to the next hand, which is the whole reason the user built it
    /// that way on Windows -- you can sit with a finger in one place and tap
    /// Fold through hand after hand without ever moving to Next Hand. Disabling
    /// them would destroy that. But leaving them lit put a bright button
    /// labelled Fold beside Next Hand at the moment a pot had just been won,
    /// which read as alarming.
    ///
    /// So: dimmed, and still working. Do not "tidy" this into IsEnabled.
    /// </summary>
    private void PaintFoldButtons(bool lit)
    {
        var background = this.PaletteColor(lit ? "FoldColor" : "ButtonOffColor");
        var text = this.PaletteColor(lit ? "FoldTextColor" : "ButtonOffTextColor");
        this.FoldButton.BackgroundColor = background;
        this.FoldButton.TextColor = text;
        this.FoldLeftButton.BackgroundColor = background;
        this.FoldLeftButton.TextColor = text;
    }

    private void CheckCallButton_Clicked(object? sender, EventArgs e)
    {
        if (this.pendingAction == null)
        {
            return;
        }

        this.SubmitAction(PlayerAction.CheckOrCall());
    }

    private void FoldButton_Clicked(object? sender, EventArgs e)
    {
        if (this.pendingAction == null)
        {
            this.TryAdvanceHandPause();
            return;
        }

        // Fold never throws away a free hand: with nothing to call it checks.
        var nothingToCall = this.pendingContext != null && this.pendingContext.MoneyToCall == 0;
        this.SubmitAction(nothingToCall ? PlayerAction.CheckOrCall() : PlayerAction.Fold());
    }

    /// <summary>
    /// Hands the player's choice to the engine thread. Clears the pending fields
    /// first and releases the engine second, and the order is the whole point.
    ///
    /// The engine thread is parked in RequestAction on the TaskCompletionSource.
    /// TrySetResult wakes it, and it can run straight on into the next street
    /// and publish a fresh pendingAction for the human's next decision. If the
    /// fields were cleared after TrySetResult, as they were when this was
    /// ported, the UI thread could null that fresh request: the buttons would
    /// light up for the next decision and every tap would be ignored, with
    /// nothing able to release the engine. The game hangs.
    ///
    /// WPF never hits this because RequestAction there ends with a blocking
    /// Dispatcher.Invoke, so the engine cannot move on until the click handler
    /// has finished. MAUI's BeginInvokeOnMainThread does not block. The volatile
    /// keyword on these fields does not help: this is an ordering problem, not a
    /// visibility one. Found by code review, 2026-09-10.
    /// </summary>
    private bool SubmitAction(PlayerAction action, bool skipShowdown = false)
    {
        var request = this.pendingAction;
        if (request == null)
        {
            return false;
        }

        // Only answer the decision that is actually on screen, and only once it
        // has been there long enough to have been seen. Both checks exist for
        // double-taps: the engine has no delay between AI moves, so it can reach
        // the player's next decision within milliseconds of the first tap. A
        // second tap handled before that decision is drawn fails the first
        // check; one landing just after it is drawn fails the second. Found by
        // the second code review, 2026-09-10. Returns false when refused so a
        // caller can tell a dropped tap from an answered one.
        if (!ReferenceEquals(request, this.shownRequest)
            || Environment.TickCount64 - this.shownAtMs < MinimumDecisionMs)
        {
            return false;
        }

        if (skipShowdown)
        {
            // Set before the engine is released, or it could reach the end of
            // the hand first and pause anyway.
            this.skipNextHandPause = true;
        }

        this.shownRequest = null;
        this.pendingAction = null;
        this.pendingContext = null;
        request.TrySetResult(action);
        return true;
    }

    private void AllInButton_Clicked(object? sender, EventArgs e)
    {
        if (this.pendingAction == null || this.pendingContext == null)
        {
            return;
        }

        // Respect the engine's raise rule, as the pot-fraction buttons do. When
        // the player has already put in the last full raise and an opponent has
        // shoved short, CanRaise is false: the only legal moves are call and
        // fold, and "all in" means calling with everything. Without this gate
        // the button submitted a raise the engine accepts without checking, and
        // BettingLogic then re-opened the action for players who had closed
        // it. Found by the 2026-09-11 review.
        var raiseAmount = this.pendingContext.CanRaise
            ? this.pendingContext.MoneyLeft - this.pendingContext.MoneyToCall
            : 0;

        var action = raiseAmount > 0
            ? PlayerAction.Raise(raiseAmount)
            : PlayerAction.CheckOrCall();

        this.SubmitAction(action);
    }

    /// <summary>
    /// Submits a raise of the given size, clamped to the legal range.
    ///
    /// On Windows this logic sits behind a Raise button that reads an amount
    /// out of a text box. Android has no such box: the user removed it on
    /// 2026-09-10 because typing a number on a tablet is not something the
    /// intended player will do. Every raise on Android therefore arrives from
    /// one of the pot-fraction buttons, which pass the amount straight in.
    /// The Windows app is unchanged and keeps its text box.
    /// </summary>
    private void SubmitRaise(int raiseAmount)
    {
        if (this.pendingAction == null || this.pendingContext == null)
        {
            return;
        }

        var maxRaise = this.pendingContext.MoneyLeft - this.pendingContext.MoneyToCall;
        if (maxRaise <= 0)
        {
            this.SubmitAction(PlayerAction.CheckOrCall());
            return;
        }

        if (raiseAmount < this.pendingContext.MinRaise)
        {
            raiseAmount = this.pendingContext.MinRaise;
        }

        if (raiseAmount > maxRaise)
        {
            raiseAmount = maxRaise;
        }

        var action = raiseAmount > 0
            ? PlayerAction.Raise(raiseAmount)
            : PlayerAction.CheckOrCall();

        this.SubmitAction(action);
    }

    private void OneThirdPotButton_Clicked(object? sender, EventArgs e)
    {
        this.ApplyPotFraction(1m / 3m);
    }

    private void HalfPotButton_Clicked(object? sender, EventArgs e)
    {
        this.ApplyPotFraction(1m / 2m);
    }

    private void TwoThirdsPotButton_Clicked(object? sender, EventArgs e)
    {
        this.ApplyPotFraction(2m / 3m);
    }

    private void PotButton_Clicked(object? sender, EventArgs e)
    {
        this.ApplyPotFraction(1m);
    }

    private void OnePointFivePotButton_Clicked(object? sender, EventArgs e)
    {
        this.ApplyPotFraction(1.5m);
    }

    private void TwoXPotButton_Clicked(object? sender, EventArgs e)
    {
        this.ApplyPotFraction(2m);
    }

    private void ThreeXPotButton_Clicked(object? sender, EventArgs e)
    {
        this.ApplyPotFraction(3m);
    }

    private void ApplyPotFraction(decimal fraction)
    {
        if (this.pendingAction == null || this.pendingContext == null)
        {
            return;
        }

        var targetTotal = (int)Math.Round(
            this.pendingContext.CurrentPot * fraction,
            MidpointRounding.AwayFromZero);
        var raiseAmount = targetTotal - this.pendingContext.CurrentMaxBet;
        if (raiseAmount < 0)
        {
            raiseAmount = 0;
        }

        this.SubmitRaise(raiseAmount);
    }

    private void NextHandButton_Clicked(object? sender, EventArgs e)
    {
        if (this.gameTask == null)
        {
            this.StartGame();
            return;
        }

        // A second tap inside the double-tap window is ignored outright. At the
        // wait between hands the first tap deals the next hand; a second one
        // would either fold that hand before it was seen or, landing while the
        // AI players were still acting, silently switch off its showdown.
        var now = Environment.TickCount64;
        if (now - this.lastNextHandTapMs < MinimumDecisionMs)
        {
            return;
        }

        if (this.pendingAction != null)
        {
            // Mid-hand: fold and skip straight past the showdown. Refused, like
            // any action, if the decision has only just appeared. The tap time
            // is recorded only when the fold was accepted: recording a refused
            // tap made the retry fall inside the gap guard as well, so a
            // mid-hand Next Hand could take three taps. Review, 2026-09-11.
            if (this.SubmitAction(PlayerAction.Fold(), skipShowdown: true))
            {
                this.lastNextHandTapMs = now;
            }

            return;
        }

        this.lastNextHandTapMs = now;

        // Release the between-hands wait if the engine is parked in it.
        // TryAdvanceHandPause reads nextHandTcs once and clears it before
        // releasing, as SubmitAction does.
        if (this.TryAdvanceHandPause())
        {
            return;
        }

        // Not parked. If the hand has ended and the engine is on its way to the
        // wait, mark that wait to be skipped: the tap meant "next hand" and the
        // engine will honour it in a moment. If the hand is still being played
        // out, do nothing. This used to set the skip flag unconditionally, so a
        // tap while the AI seats were acting (with the human still in the hand)
        // silently switched off the showdown pause of the hand the player then
        // went on to play and possibly win. Review, 2026-09-11.
        if (this.pausePending)
        {
            this.skipNextHandPause = true;
            this.SetNextHandHighlight(false);
        }
    }

    private bool TryAdvanceHandPause()
    {
        // Clear before releasing, as in SubmitAction.
        var wait = this.nextHandTcs;
        if (wait == null)
        {
            return false;
        }

        this.nextHandTcs = null;
        wait.TrySetResult(true);
        this.SetNextHandHighlight(false);
        this.UpdateGameSizeAvailability();
        return true;
    }

    private void SetNextHandHighlight(bool enabled)
    {
        // Both colours come from the palette block in MainPage.xaml so the
        // between-hands highlight cannot drift away from the button's own
        // colour. It was a hard-coded WPF pale blue before Phase 6, which
        // fought the palette and overrode the button's visual state.
        this.NextHandButton.BackgroundColor = this.PaletteColor(
            enabled ? "NextHandGlowColor" : "NextHandColor");
        this.NextHandButton.TextColor = this.PaletteColor(
            enabled ? "NextHandGlowTextColor" : "NextHandTextColor");
    }

    private Color PaletteColor(string key)
    {
        return this.Resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : Colors.Transparent;
    }

    /// <summary>
    /// A stakes change is allowed with no game running, or while the engine is
    /// parked between hands. "Parked" is nextHandTcs being set. It used to be
    /// a separate flag that the engine thread cleared only after it woke, while
    /// the wait itself is created and released on the UI thread, so the flag
    /// lagged the truth. In that gap a tap on Next Hand followed by a
    /// stakes tile was accepted with nothing left to restart, the next hand
    /// played at the old stakes under the new label, and the Next Hand tap
    /// after it reset the game. Review, 2026-09-11.
    /// </summary>
    private bool IsGameSizeChangeAllowed()
    {
        return this.gameTask == null || this.nextHandTcs != null;
    }

    /// <summary>
    /// The stakes tiles dim while a hand is live, because the engine only
    /// accepts a size change between hands, and the section heading says so.
    /// The heading carries the explanation because the sheet covers the turn
    /// info line, so a message written there was never seen.
    /// </summary>
    private void UpdateGameSizeAvailability()
    {
        var allowed = this.IsGameSizeChangeAllowed();
        foreach (var tile in this.StakeTiles())
        {
            tile.Opacity = allowed ? 1d : 0.4d;
        }

        this.StakesSectionLabel.Text = allowed
            ? "STAKES  (applies on Back to Game)"
            : "STAKES  (changes only between hands)";
    }

    private IEnumerable<Border> StakeTiles()
    {
        yield return this.StakeOneTile;
        yield return this.StakeTwoTile;
        yield return this.StakeFiveTile;
        yield return this.StakeTenTile;
        yield return this.StakeFiftyTile;
        yield return this.StakeHundredTile;
    }

    private IEnumerable<Label> StakeTileLabels()
    {
        yield return this.StakeOneTileLabel;
        yield return this.StakeTwoTileLabel;
        yield return this.StakeFiveTileLabel;
        yield return this.StakeTenTileLabel;
        yield return this.StakeFiftyTileLabel;
        yield return this.StakeHundredTileLabel;
    }

    /// <summary>
    /// Repaints both single-choice tile groups. Same lit and unlit colours as
    /// the optional raise-button tiles, so all three groups in the sheet read as
    /// one control type. The difference is behavioural, not visual: these two
    /// groups always have exactly one tile lit, while the raise buttons are
    /// independent on/off switches.
    /// </summary>
    private void PaintChoiceTiles()
    {
        this.PaintChoiceTile(this.CardAlwaysTile, this.CardAlwaysTileLabel, this.selectedCardVisibilityIndex == 0);
        this.PaintChoiceTile(this.CardShowdownTile, this.CardShowdownTileLabel, this.selectedCardVisibilityIndex == 1);
        this.PaintChoiceTile(this.CardAllEndTile, this.CardAllEndTileLabel, this.selectedCardVisibilityIndex == 2);

        var index = 0;
        var labels = this.StakeTileLabels().ToList();
        foreach (var tile in this.StakeTiles())
        {
            this.PaintChoiceTile(tile, labels[index], this.selectedGameSizeIndex == index);
            index++;
        }
    }

    private void PaintChoiceTile(Border tile, Label tileLabel, bool selected)
    {
        tile.BackgroundColor = selected ? Color.FromArgb("#4B2A31") : Color.FromArgb("#1C1416");
        tile.Stroke = selected ? Color.FromArgb("#B98A92") : Color.FromArgb("#4D3A3E");
        tileLabel.TextColor = selected ? Color.FromArgb("#F2F2F2") : Color.FromArgb("#8A7A7D");
    }

    private void InitializeGamePresets()
    {
        this.gamePresets.Clear();
        this.gamePresets.Add(CreatePreset("1/2", 1));
        this.gamePresets.Add(CreatePreset("2/4", 2));
        this.gamePresets.Add(CreatePreset("5/10", 5));
        this.gamePresets.Add(CreatePreset("10/20", 10));
        this.gamePresets.Add(CreatePreset("50/100", 50));
        this.gamePresets.Add(CreatePreset("100/200", 100));

        this.selectedGameSizeIndex = 0;
        this.selectedGamePreset = this.gamePresets[0];
        this.UpdateStakesLabel();
    }

    private void UpdateStakesLabel()
    {
        this.StakesLabel.Text = $"Stakes: {this.selectedGamePreset?.Label ?? this.gamePresets[0].Label}";
    }

    private void InitializeCardVisibilityOptions()
    {
        // One three-way choice, "Other players' cards". PeekOnHover is a
        // mouse-hover mode with no touch equivalent, so Android does not offer
        // it. Index 0 and 1 keep the meaning a saved value had before ALL END
        // was added (ALL and Human), so an old preference still restores.
        // Decided 2026-09-11.
        this.cardVisibilityOptions.Clear();
        this.cardVisibilityOptions.Add(new CardVisibilityOption("ALWAYS", CardVisibilityMode.ShowAll, EndOfHandReveal.Showdown));
        this.cardVisibilityOptions.Add(new CardVisibilityOption("SHOWDOWN", CardVisibilityMode.HumanOnly, EndOfHandReveal.Showdown));
        this.cardVisibilityOptions.Add(new CardVisibilityOption("ALL END", CardVisibilityMode.HumanOnly, EndOfHandReveal.Everyone));

        this.selectedCardVisibilityIndex = 0;
        this.selectedCardVisibility = this.cardVisibilityOptions[0];
        this.tableView.SetCardVisibility(this.selectedCardVisibility.Mode, this.selectedCardVisibility.Reveal);
    }

    private void StakeOneTile_Tapped(object? sender, TappedEventArgs e) => this.SelectGameSize(0);

    private void StakeTwoTile_Tapped(object? sender, TappedEventArgs e) => this.SelectGameSize(1);

    private void StakeFiveTile_Tapped(object? sender, TappedEventArgs e) => this.SelectGameSize(2);

    private void StakeTenTile_Tapped(object? sender, TappedEventArgs e) => this.SelectGameSize(3);

    private void StakeFiftyTile_Tapped(object? sender, TappedEventArgs e) => this.SelectGameSize(4);

    private void StakeHundredTile_Tapped(object? sender, TappedEventArgs e) => this.SelectGameSize(5);

    /// <summary>
    /// Picks a stakes level. Replaces the game-size Picker, which opened a white
    /// Android AlertDialog over the dark table; the tiles render inside the app
    /// and are a far bigger touch target than a dialog row.
    /// </summary>
    private void SelectGameSize(int index)
    {
        if (index < 0 || index >= this.gamePresets.Count)
        {
            return;
        }

        var preset = this.gamePresets[index];

        if (!this.IsGameSizeChangeAllowed())
        {
            // The tile is dimmed and the section heading explains why; see
            // UpdateGameSizeAvailability.
            return;
        }

        this.selectedGameSizeIndex = index;
        this.PaintChoiceTiles();

        this.selectedGamePreset = preset;
        this.UpdateStakesLabel();
        this.SaveSettings();

        // A running game is not touched here; Back to Game applies the change.
        // See SettingsDoneButton_Clicked. Session 10, 2026-09-12.
        if (this.gameTask == null)
        {
            this.tableView.SetTurnInfo($"Game size set to {preset.Label}. Tap Start Game to begin.");
        }
    }

    private void CardAlwaysTile_Tapped(object? sender, TappedEventArgs e) => this.SelectCardVisibility(0);

    private void CardShowdownTile_Tapped(object? sender, TappedEventArgs e) => this.SelectCardVisibility(1);

    private void CardAllEndTile_Tapped(object? sender, TappedEventArgs e) => this.SelectCardVisibility(2);

    /// <summary>
    /// Picks an Other players' cards option, which sets both the visibility
    /// mode during the hand and the reveal at the end of it. Same reasoning as
    /// SelectGameSize: tiles instead of a Picker, so nothing opens an Android
    /// dialog.
    /// </summary>
    private void SelectCardVisibility(int index)
    {
        if (index < 0 || index >= this.cardVisibilityOptions.Count)
        {
            return;
        }

        var option = this.cardVisibilityOptions[index];

        this.selectedCardVisibilityIndex = index;
        this.selectedCardVisibility = option;
        this.PaintChoiceTiles();
        this.tableView.SetCardVisibility(option.Mode, option.Reveal);
        this.SaveSettings();
    }

    private void RequestGameRestart()
    {
        if (this.restartRequested)
        {
            return;
        }

        // Only latch the restart when there is a parked wait to release. The
        // engine checks restartRequested once, right after that wait, so a
        // latch with nothing to release would sit until the next hand ended and
        // then reset the game from under the player.
        var wait = this.nextHandTcs;
        if (wait == null)
        {
            return;
        }

        this.restartRequested = true;
        this.nextHandTcs = null;
        wait.TrySetResult(true);
    }

    private void OnGameStopped(bool wasRestart)
    {
        this.gameTask = null;
        this.pendingAction = null;
        this.pendingContext = null;
        this.shownRequest = null;
        this.nextHandTcs = null;
        this.pausePending = false;
        this.skipNextHandPause = false;

        this.runningGamePreset = null;
        this.SetActionControlsEnabled(false);
        this.SetCheckCallLabel("Check/Call");
        this.NextHandButton.IsEnabled = true;
        this.NextHandButton.Text = "Start Game";
        this.SetNextHandHighlight(true);
        this.UpdateGameSizeAvailability();

        // A stopped game shows the launch screen, not the last hand: the table
        // is wiped and the seats go back to their plain names with Waiting, as
        // the constructor set them. The turn info line is set below. Before
        // this the old cards, pot and Winner tag stayed up under Start Game
        // and read as a game still in progress. Session 10, 2026-09-12.
        this.tableView.ResetToLaunchLook();
        for (var seatIndex = 1; seatIndex <= SeatCount; seatIndex++)
        {
            this.tableView.SetSeatName(seatIndex, $"Player{seatIndex}");
            this.tableView.SetSeatStatus(seatIndex, "Waiting");
        }

        if (wasRestart)
        {
            var message = string.IsNullOrWhiteSpace(this.restartMessage)
                ? "Game reset. Choose a game size and tap Start Game."
                : this.restartMessage;
            this.tableView.SetTurnInfo(message);
        }

        this.restartRequested = false;
        this.restartMessage = null;
    }

    private sealed class GamePreset
    {
        public GamePreset(string label, int smallBlind, int bigBlind, int startingStack)
        {
            this.Label = label;
            this.SmallBlind = smallBlind;
            this.BigBlind = bigBlind;
            this.StartingStack = startingStack;
        }

        public string Label { get; }

        public int SmallBlind { get; }

        public int BigBlind { get; }

        public int StartingStack { get; }

        public override string ToString() => this.Label;
    }

    private sealed class CardVisibilityOption
    {
        public CardVisibilityOption(string label, CardVisibilityMode mode, EndOfHandReveal reveal)
        {
            this.Label = label;
            this.Mode = mode;
            this.Reveal = reveal;
        }

        public string Label { get; }

        public CardVisibilityMode Mode { get; }

        public EndOfHandReveal Reveal { get; }

        public override string ToString() => this.Label;
    }
}
