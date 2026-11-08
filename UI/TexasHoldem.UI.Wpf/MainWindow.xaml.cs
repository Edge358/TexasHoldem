namespace TexasHoldem.UI.Wpf;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

using TexasHoldem.AI.DummyPlayer;
using TexasHoldem.AI.SmartPlayer;
using TexasHoldem.AI.SmartPlayer.Decision;
using TexasHoldem.AI.SmartPlayer.Models;
using TexasHoldem.Logic;
using TexasHoldem.Logic.GameMechanics;
using TexasHoldem.Logic.Players;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IHumanPlayerController, IHandPauseController
{
    private WpfTableView tableView;
    private Task? gameTask;
    private WpfHumanPlayer? humanPlayer;
    private readonly Dictionary<int, SmartPlayer> smartPlayersBySeat = new Dictionary<int, SmartPlayer>();
    private TaskCompletionSource<PlayerAction>? pendingAction;
    private IGetTurnContext? pendingContext;
    private TaskCompletionSource<bool>? nextHandTcs;
    private bool skipNextHandPause;
    private bool pausePending;
    private bool isHandPaused;
    private bool hasAdjustedInitialSize;
    private readonly List<GamePreset> gamePresets = new List<GamePreset>();
    private GamePreset? selectedGamePreset;
    private bool isUpdatingGameSizeSelection;
    private bool restartRequested;
    private string? restartMessage;
    private readonly List<CardVisibilityOption> cardVisibilityOptions = new List<CardVisibilityOption>();
    private bool isUpdatingCardVisibilitySelection;
    private CardVisibilityOption? selectedCardVisibility;

    public MainWindow()
    {
        InitializeComponent();
        DecisionLogger.Enabled = this.AiDecisionLogToggle.IsChecked == true;
        this.ContentRendered += this.MainWindow_ContentRendered;
        this.SizeChanged += this.MainWindow_SizeChanged;
        this.SourceInitialized += this.MainWindow_SourceInitialized;
        this.Closing += this.MainWindow_Closing;
        this.ApplyHotkeyLabels();
        this.tableView = new WpfTableView(
            this.Dispatcher,
            new[]
            {
                this.Seat1NameTextBox,
                this.Seat2NameTextBox,
                this.Seat3NameTextBox,
                this.Seat4NameTextBox,
                this.Seat5NameTextBox,
                this.Seat6NameTextBox,
                this.Seat7NameTextBox,
                this.Seat8NameTextBox,
            },
            new[]
            {
                this.Seat1StackTextBox,
                this.Seat2StackTextBox,
                this.Seat3StackTextBox,
                this.Seat4StackTextBox,
                this.Seat5StackTextBox,
                this.Seat6StackTextBox,
                this.Seat7StackTextBox,
                this.Seat8StackTextBox,
            },
            new[]
            {
                this.Seat1CardsTextBlock,
                this.Seat2CardsTextBlock,
                this.Seat3CardsTextBlock,
                this.Seat4CardsTextBlock,
                this.Seat5CardsTextBlock,
                this.Seat6CardsTextBlock,
                this.Seat7CardsTextBlock,
                this.Seat8CardsTextBlock,
            },
            new[]
            {
                this.Seat1InfoBorder,
                this.Seat2InfoBorder,
                this.Seat3InfoBorder,
                this.Seat4InfoBorder,
                this.Seat5InfoBorder,
                this.Seat6InfoBorder,
                this.Seat7InfoBorder,
                this.Seat8InfoBorder,
            },
            new[]
            {
                this.Seat1CardsBorder,
                this.Seat2CardsBorder,
                this.Seat3CardsBorder,
                this.Seat4CardsBorder,
                this.Seat5CardsBorder,
                this.Seat6CardsBorder,
                this.Seat7CardsBorder,
                this.Seat8CardsBorder,
            },
            new[]
            {
                this.Seat1BetTextBox,
                this.Seat2BetTextBox,
                this.Seat3BetTextBox,
                this.Seat4BetTextBox,
                this.Seat5BetTextBox,
                this.Seat6BetTextBox,
                this.Seat7BetTextBox,
                this.Seat8BetTextBox,
            },
            new[]
            {
                this.Seat1StatusTextBox,
                this.Seat2StatusTextBox,
                this.Seat3StatusTextBox,
                this.Seat4StatusTextBox,
                this.Seat5StatusTextBox,
                this.Seat6StatusTextBox,
                this.Seat7StatusTextBox,
                this.Seat8StatusTextBox,
            },
            this.CommunityCardsTextBlock,
            this.PotTextBox,
            this.ActionLogTextBox,
            this.TurnInfoTextBlock,
            this.DealerButton,
            this.HandCounterTextBlock,
            this.BuyInCounterTextBlock,
            this.AiRecommendationTextBox);

        this.SetActionControlsEnabled(false);
        this.NextHandButton.IsEnabled = true;
        this.InitializeGamePresets();
        this.InitializeCardVisibilityOptions();
        this.AttachSeatCardHoverHandlers();
        this.ApplySavedUiState();
        this.UpdateGameSizeAvailability();
        this.InitializeSeatArchetypeMenus();
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (this.hasAdjustedInitialSize)
        {
            return;
        }

        this.hasAdjustedInitialSize = true;

        var workArea = SystemParameters.WorkArea;
        var targetWidth = Math.Min(workArea.Width * 0.85, 1200);
        var targetHeight = Math.Min(workArea.Height * 0.85, 800);

        if (!double.IsNaN(this.MinWidth))
        {
            targetWidth = Math.Max(targetWidth, this.MinWidth);
        }

        if (!double.IsNaN(this.MinHeight))
        {
            targetHeight = Math.Max(targetHeight, this.MinHeight);
        }

        var maxWidth = Math.Max(200, workArea.Width - 20);
        var maxHeight = Math.Max(200, workArea.Height - 20);
        // Stay within the visible WorkArea even if it dips under MinWidth/MinHeight.
        targetWidth = Math.Min(targetWidth, maxWidth);
        targetHeight = Math.Min(targetHeight, maxHeight);

        this.Width = targetWidth;
        this.Height = targetHeight;
        this.Left = workArea.Left + Math.Max(0, (workArea.Width - targetWidth) / 2);
        this.Top = workArea.Top + Math.Max(0, (workArea.Height - targetHeight) / 2);

        this.ContentRendered -= this.MainWindow_ContentRendered;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        if (Properties.Settings.Default.PersistWindowPlacement && this.TryRestoreWindowPlacement())
        {
            this.hasAdjustedInitialSize = true;
            this.ContentRendered -= this.MainWindow_ContentRendered;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        var settings = Properties.Settings.Default;
        if (settings.PersistWindowPlacement)
        {
            settings.WindowState = (int)this.WindowState;
            if (this.WindowState == WindowState.Normal)
            {
                settings.WindowWidth = this.Width;
                settings.WindowHeight = this.Height;
                settings.WindowLeft = this.Left;
                settings.WindowTop = this.Top;
            }

            settings.GameSizeSelectionIndex = this.GameSizeComboBox.SelectedIndex;
            settings.CardVisibilitySelectionIndex = this.CardVisibilityComboBox.SelectedIndex;
            settings.ShowRaiseControls = this.ShowRaiseControlsCheckBox.IsChecked == true;
            settings.ShowActionLog = this.ActionLogToggle.IsChecked == true;
            settings.EnableAiDecisionLog = this.AiDecisionLogToggle.IsChecked == true;
            settings.ShowAiRecommendations = this.AiRecommendationToggle.IsChecked == true;
        }

        settings.Save();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        this.UpdateAiRecommendationPanelPosition();
        this.UpdateActionLogPanelPosition();
    }

    private bool TryRestoreWindowPlacement()
    {
        var settings = Properties.Settings.Default;
        var savedState = (WindowState)settings.WindowState;
        if (!Enum.IsDefined(typeof(WindowState), savedState) || savedState == WindowState.Minimized)
        {
            savedState = WindowState.Normal;
        }

        var width = settings.WindowWidth;
        var height = settings.WindowHeight;
        var left = settings.WindowLeft;
        var top = settings.WindowTop;

        if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height)
            || double.IsNaN(left) || double.IsNaN(top))
        {
            return false;
        }

        var workArea = SystemParameters.WorkArea;
        var maxLeft = workArea.Right - width;
        var maxTop = workArea.Bottom - height;
        left = maxLeft < workArea.Left ? workArea.Left : Math.Clamp(left, workArea.Left, maxLeft);
        top = maxTop < workArea.Top ? workArea.Top : Math.Clamp(top, workArea.Top, maxTop);

        this.Width = width;
        this.Height = height;
        this.Left = left;
        this.Top = top;
        this.WindowState = savedState;
        return true;
    }

    public PlayerAction RequestAction(IGetTurnContext context)
    {
        var tcs = new TaskCompletionSource<PlayerAction>();
        this.pendingContext = context;
        this.pendingAction = tcs;

        this.Dispatcher.Invoke(() => this.ShowTurnControls(context));
        var action = tcs.Task.GetAwaiter().GetResult();
        this.Dispatcher.Invoke(this.HideTurnControls);
        return action;
    }

    private void StartGame()
    {
        if (this.gameTask != null)
        {
            return;
        }

        var preset = this.selectedGamePreset ?? this.gamePresets[0];
        this.restartRequested = false;
        this.restartMessage = null;
        this.tableView.ClearLog();
        this.tableView.SetTurnInfo("Starting game...");
        this.NextHandButton.IsEnabled = true;
        this.NextHandButton.Content = CreateUnderlinedContent("Next Hand", 'N');
        this.SetNextHandHighlight(false);

        var players = this.CreatePlayers();
        var game = new TexasHoldemGame(players, preset.StartingStack, preset.SmallBlind, preset.StartingStack);
        this.UpdateGameSizeAvailability();

        this.gameTask = Task.Run(() =>
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
                this.Dispatcher.Invoke(() =>
                {
                    this.tableView.SetTurnInfo($"Game stopped: {ex.Message}");
                    this.SetActionControlsEnabled(false);
                });
            }
            finally
            {
                this.Dispatcher.Invoke(() => this.OnGameStopped(wasRestart));
            }
        });
    }

    private IList<IPlayer> CreatePlayers()
    {
        var seats = new List<IPlayer>();
        this.humanPlayer = new WpfHumanPlayer("You", -1, this);

        this.smartPlayersBySeat.Clear();
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
            if (seats[i] is SmartPlayer smartPlayer)
            {
                this.smartPlayersBySeat[seatIndex] = smartPlayer;
            }

            this.tableView.SetSeatName(seatIndex, this.GetDisplayName(seatIndex, seats[i]));
            this.tableView.SetSeatStatus(seatIndex, "Waiting");
            decorated.Add(new WpfUiDecorator(seats[i], seatIndex, this.tableView, this));
        }

        return decorated;
    }

    private string GetDisplayName(int seatIndex, IPlayer player)
    {
        var suffix = player switch
        {
            SmartPlayer smartPlayer => GetArchetypeLabel(smartPlayer.Archetype),
            DummyPlayer => "D",
            WpfHumanPlayer => "H",
            _ => string.Empty,
        };

        return string.IsNullOrWhiteSpace(suffix)
            ? $"Player{seatIndex}"
            : $"Player{seatIndex} {suffix}";
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

    private void ShowTurnControls(IGetTurnContext context)
    {
        this.SetActionControlsEnabled(true);
        var checkCallLabel = context.MoneyToCall > 0
            ? $"Call {context.MoneyToCall}"
            : "Check";
        this.SetCheckCallLabel(checkCallLabel);
        var canRaise = context.CanRaise;
        this.RaiseButton.IsEnabled = canRaise;
        this.OneThirdPotButton.IsEnabled = canRaise;
        this.HalfPotButton.IsEnabled = canRaise;
        this.TwoThirdsPotButton.IsEnabled = canRaise;
        this.PotButton.IsEnabled = canRaise;
        this.OnePointFivePotButton.IsEnabled = canRaise;
        this.TwoXPotButton.IsEnabled = canRaise;
        this.ThreeXPotButton.IsEnabled = canRaise;
        this.AllInButton.IsEnabled = context.MoneyLeft > 0;
        this.RaiseAmountTextBox.Text = context.MinRaise.ToString();
        var facingLabel = GetFacingLabel(context);
        this.tableView.SetTurnInfo(
            $"{facingLabel}     Call:  {context.MoneyToCall}     MRaise:  {context.MinRaise}");
    }

    private void SetCheckCallLabel(string label)
    {
        this.CheckCallButton.Content = CreateUnderlinedContent(label, 'C');
        this.CheckCallRightButton.Content = CreateUnderlinedContent(label, 'C');
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

    private void HideTurnControls()
    {
        this.SetActionControlsEnabled(false);
        this.tableView.SetTurnInfo("Waiting for other players...");
    }

    private void SetActionControlsEnabled(bool enabled)
    {
        this.CheckCallButton.IsEnabled = enabled;
        this.CheckCallRightButton.IsEnabled = enabled;
        this.RaiseButton.IsEnabled = enabled;
        this.OneThirdPotButton.IsEnabled = enabled;
        this.HalfPotButton.IsEnabled = enabled;
        this.TwoThirdsPotButton.IsEnabled = enabled;
        this.PotButton.IsEnabled = enabled;
        this.OnePointFivePotButton.IsEnabled = enabled;
        this.TwoXPotButton.IsEnabled = enabled;
        this.ThreeXPotButton.IsEnabled = enabled;
        this.AllInButton.IsEnabled = enabled;
        this.FoldButton.IsEnabled = enabled;
        this.RaiseAmountTextBox.IsEnabled = enabled;
    }

    private void ApplyHotkeyLabels()
    {
        this.FoldLeftButton.Content = CreateUnderlinedContent("Fold", 'F');
        this.FoldButton.Content = CreateUnderlinedContent("Fold", 'F');
        this.CheckCallButton.Content = CreateUnderlinedContent("Check/Call", 'C');
        this.CheckCallRightButton.Content = CreateUnderlinedContent("Check/Call", 'C');
        this.OneThirdPotButton.Content = CreateUnderlinedContent("1/3", '1');
        this.HalfPotButton.Content = CreateUnderlinedContent("1/2", '2');
        this.TwoThirdsPotButton.Content = CreateUnderlinedContent("2/3", '3');
        this.PotButton.Content = CreateUnderlinedContent("POT", 'P');
        this.AllInButton.Content = CreateUnderlinedContent("ALL IN", 'A');
        this.NextHandButton.Content = CreateUnderlinedContent("Start Game", 'S');
        this.ActionLogToggle.Content = CreateUnderlinedContent("Log", 'L');
        this.AiRecommendationToggle.Content = CreateUnderlinedContent("AI", 'I');
    }

    private static object CreateUnderlinedContent(string text, char underlineChar)
    {
        var index = text.IndexOf(underlineChar.ToString(), StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return text;
        }

        var textBlock = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
        };

        if (index > 0)
        {
            textBlock.Inlines.Add(new Run(text[..index]));
        }

        textBlock.Inlines.Add(new Run(text.Substring(index, 1))
        {
            TextDecorations = TextDecorations.Underline,
        });

        if (index + 1 < text.Length)
        {
            textBlock.Inlines.Add(new Run(text[(index + 1)..]));
        }

        return textBlock;
    }

    private void CheckCallButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.pendingAction == null)
        {
            return;
        }

        this.pendingAction.TrySetResult(PlayerAction.CheckOrCall());
        this.pendingAction = null;
        this.pendingContext = null;
    }

    private void FoldButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.pendingAction == null)
        {
            if (this.TryAdvanceHandPause())
            {
                return;
            }

            return;
        }

        if (this.pendingContext != null && this.pendingContext.MoneyToCall == 0)
        {
            this.pendingAction.TrySetResult(PlayerAction.CheckOrCall());
        }
        else
        {
            this.pendingAction.TrySetResult(PlayerAction.Fold());
        }
        this.pendingAction = null;
        this.pendingContext = null;
    }

    private void AllInButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.pendingAction == null || this.pendingContext == null)
        {
            return;
        }

        var raiseAmount = this.pendingContext.MoneyLeft > 0
            ? this.pendingContext.MoneyLeft - this.pendingContext.MoneyToCall
            : 0;

        var action = raiseAmount > 0
            ? PlayerAction.Raise(raiseAmount)
            : PlayerAction.CheckOrCall();

        this.pendingAction.TrySetResult(action);
        this.pendingAction = null;
        this.pendingContext = null;
    }

    private void RaiseButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.pendingAction == null || this.pendingContext == null)
        {
            return;
        }

        if (!int.TryParse(this.RaiseAmountTextBox.Text, out var raiseAmount))
        {
            this.tableView.SetTurnInfo("Enter a numeric raise amount.");
            return;
        }

        var maxRaise = this.pendingContext.MoneyLeft - this.pendingContext.MoneyToCall;
        if (maxRaise <= 0)
        {
            this.pendingAction.TrySetResult(PlayerAction.CheckOrCall());
            this.pendingAction = null;
            this.pendingContext = null;
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

        this.pendingAction.TrySetResult(action);
        this.pendingAction = null;
        this.pendingContext = null;
    }

    private void OneThirdPotButton_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPotFraction(1m / 3m);
    }

    private void HalfPotButton_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPotFraction(1m / 2m);
    }

    private void TwoThirdsPotButton_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPotFraction(2m / 3m);
    }

    private void PotButton_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPotFraction(1m);
    }

    private void OnePointFivePotButton_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPotFraction(1.5m);
    }

    private void TwoXPotButton_Click(object sender, RoutedEventArgs e)
    {
        this.ApplyPotFraction(2m);
    }

    private void ThreeXPotButton_Click(object sender, RoutedEventArgs e)
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

        this.RaiseAmountTextBox.Text = raiseAmount.ToString();
        this.RaiseButton_Click(this, new RoutedEventArgs());
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
        this.isHandPaused = true;

        this.Dispatcher.Invoke(() =>
        {
            this.tableView.SetTurnInfo("Waiting for next hand...");
            this.FoldButton.IsEnabled = true;
            this.SetNextHandHighlight(true);
            this.UpdateGameSizeAvailability();
        });

        tcs.Task.GetAwaiter().GetResult();
        this.nextHandTcs = null;

        if (this.restartRequested)
        {
            this.pausePending = false;
            this.isHandPaused = false;
            this.skipNextHandPause = false;
            this.Dispatcher.Invoke(() =>
            {
                this.FoldButton.IsEnabled = false;
                this.SetNextHandHighlight(false);
                this.UpdateGameSizeAvailability();
            });
            throw new OperationCanceledException("Game reset.");
        }

        this.Dispatcher.Invoke(() =>
        {
            this.tableView.SetTurnInfo("Waiting for other players...");
            this.FoldButton.IsEnabled = false;
            this.SetNextHandHighlight(false);
            this.UpdateGameSizeAvailability();
        });

        this.pausePending = false;
        this.isHandPaused = false;
    }

    public void RequestHandPause()
    {
        this.pausePending = true;
    }

    private void NextHandButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.gameTask == null)
        {
            this.StartGame();
            return;
        }

        if (this.pendingAction != null)
        {
            this.skipNextHandPause = true;
            this.pendingAction.TrySetResult(PlayerAction.Fold());
            this.pendingAction = null;
            this.pendingContext = null;
        }

        if (this.nextHandTcs == null)
        {
            this.skipNextHandPause = true;
            this.SetNextHandHighlight(false);
            return;
        }

        this.nextHandTcs.TrySetResult(true);
        this.nextHandTcs = null;
        this.SetNextHandHighlight(false);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.OriginalSource is TextBox textBox && !textBox.IsReadOnly && textBox.IsEnabled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.F:
            case Key.Space:
                this.FoldButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.C:
                this.CheckCallButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.P:
            case Key.R:
                this.PotButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.A:
                this.AllInButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.N:
                if (this.gameTask != null)
                {
                    this.NextHandButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                break;
            case Key.S:
                if (this.gameTask == null)
                {
                    this.NextHandButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                break;
            case Key.I:
                this.AiRecommendationToggle.IsChecked = !(this.AiRecommendationToggle.IsChecked == true);
                e.Handled = true;
                break;
            case Key.L:
                this.ActionLogToggle.IsChecked = !(this.ActionLogToggle.IsChecked == true);
                e.Handled = true;
                break;
            case Key.Q:
                this.Close();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                this.OneThirdPotButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                this.HalfPotButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                this.TwoThirdsPotButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                this.OnePointFivePotButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                this.TwoXPotButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                this.ThreeXPotButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private bool TryAdvanceHandPause()
    {
        if (this.nextHandTcs == null)
        {
            return false;
        }

        this.nextHandTcs.TrySetResult(true);
        this.nextHandTcs = null;
        this.SetNextHandHighlight(false);
        return true;
    }

    private void SetNextHandHighlight(bool enabled)
    {
        this.NextHandButton.Tag = enabled ? "Highlight" : null;
    }

    private void InitializeSeatArchetypeMenus()
    {
        var seatBoxes = new (int SeatIndex, System.Windows.Controls.TextBox NameBox)[]
        {
            (1, this.Seat1NameTextBox),
            (2, this.Seat2NameTextBox),
            (3, this.Seat3NameTextBox),
            (4, this.Seat4NameTextBox),
            (5, this.Seat5NameTextBox),
            (6, this.Seat6NameTextBox),
            (7, this.Seat7NameTextBox),
            (8, this.Seat8NameTextBox),
        };

        foreach (var entry in seatBoxes)
        {
            var menu = new System.Windows.Controls.ContextMenu();
            menu.Opened += this.ArchetypeMenu_Opened;
            foreach (PlayerArchetype archetype in Enum.GetValues(typeof(PlayerArchetype)))
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = archetype.ToString(),
                    IsCheckable = true,
                    Tag = Tuple.Create(entry.SeatIndex, archetype),
                };
                item.Click += this.ArchetypeMenuItem_Click;
                menu.Items.Add(item);
            }

            entry.NameBox.ContextMenu = menu;
        }
    }

    private void ArchetypeMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (!(sender is System.Windows.Controls.ContextMenu menu))
        {
            return;
        }

        foreach (var item in menu.Items)
        {
            if (!(item is System.Windows.Controls.MenuItem menuItem))
            {
                continue;
            }

            if (!(menuItem.Tag is Tuple<int, PlayerArchetype> selection))
            {
                continue;
            }

            menuItem.IsEnabled = this.IsArchetypeChangeAllowed();

            if (!this.smartPlayersBySeat.TryGetValue(selection.Item1, out var smartPlayer))
            {
                menuItem.IsChecked = false;
                continue;
            }

            menuItem.IsChecked = smartPlayer.Archetype == selection.Item2;
        }
    }

    private void ArchetypeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!(sender is System.Windows.Controls.MenuItem menuItem))
        {
            return;
        }

        if (!(menuItem.Tag is Tuple<int, PlayerArchetype> selection))
        {
            return;
        }

        if (!this.IsArchetypeChangeAllowed())
        {
            this.tableView.SetTurnInfo("Archetype changes are only available between hands.");
            return;
        }

        var seatIndex = selection.Item1;
        if (!this.smartPlayersBySeat.TryGetValue(seatIndex, out var smartPlayer))
        {
            return;
        }

        var profile = CreateProfile(selection.Item2);
        smartPlayer.UpdateProfile(profile);
        this.tableView.SetSeatName(seatIndex, this.GetDisplayName(seatIndex, smartPlayer));
    }

    private bool IsArchetypeChangeAllowed()
    {
        return this.gameTask == null || this.isHandPaused;
    }

    private bool IsGameSizeChangeAllowed()
    {
        return this.gameTask == null || this.isHandPaused;
    }

    private void UpdateGameSizeAvailability()
    {
        this.GameSizeComboBox.IsEnabled = this.IsGameSizeChangeAllowed();
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

        this.isUpdatingGameSizeSelection = true;
        this.GameSizeComboBox.ItemsSource = this.gamePresets;
        this.GameSizeComboBox.DisplayMemberPath = nameof(GamePreset.Label);
        this.GameSizeComboBox.SelectedIndex = 0;
        this.isUpdatingGameSizeSelection = false;

        this.selectedGamePreset = this.gamePresets[0];
    }

    private void InitializeCardVisibilityOptions()
    {
        this.cardVisibilityOptions.Clear();
        this.cardVisibilityOptions.Add(new CardVisibilityOption("ALL", WpfTableView.CardVisibilityMode.ShowAll));
        this.cardVisibilityOptions.Add(new CardVisibilityOption("Human", WpfTableView.CardVisibilityMode.HumanOnly));
        this.cardVisibilityOptions.Add(new CardVisibilityOption("Peek", WpfTableView.CardVisibilityMode.PeekOnHover));

        this.isUpdatingCardVisibilitySelection = true;
        this.CardVisibilityComboBox.ItemsSource = this.cardVisibilityOptions;
        this.CardVisibilityComboBox.DisplayMemberPath = nameof(CardVisibilityOption.Label);
        this.CardVisibilityComboBox.SelectedIndex = 0;
        this.isUpdatingCardVisibilitySelection = false;

        this.selectedCardVisibility = this.cardVisibilityOptions[0];
        this.tableView.SetCardVisibilityMode(this.selectedCardVisibility.Mode);
    }

    private void ApplySavedUiState()
    {
        var settings = Properties.Settings.Default;
        if (!settings.PersistWindowPlacement)
        {
            this.isUpdatingGameSizeSelection = true;
            this.GameSizeComboBox.SelectedIndex = 0;
            this.isUpdatingGameSizeSelection = false;
            this.selectedGamePreset = this.gamePresets[0];

            this.isUpdatingCardVisibilitySelection = true;
            this.CardVisibilityComboBox.SelectedIndex = 0;
            this.isUpdatingCardVisibilitySelection = false;
            this.selectedCardVisibility = this.cardVisibilityOptions[0];
            this.tableView.SetCardVisibilityMode(this.selectedCardVisibility.Mode);

            this.ShowRaiseControlsCheckBox.IsChecked = false;
            this.SetRaiseControlsVisible(false);
            return;
        }

        var gameIndex = ClampIndex(settings.GameSizeSelectionIndex, this.gamePresets.Count);
        this.isUpdatingGameSizeSelection = true;
        this.GameSizeComboBox.SelectedIndex = gameIndex;
        this.isUpdatingGameSizeSelection = false;
        this.selectedGamePreset = this.gamePresets[gameIndex];

        var cardIndex = ClampIndex(settings.CardVisibilitySelectionIndex, this.cardVisibilityOptions.Count);
        this.isUpdatingCardVisibilitySelection = true;
        this.CardVisibilityComboBox.SelectedIndex = cardIndex;
        this.isUpdatingCardVisibilitySelection = false;
        this.selectedCardVisibility = this.cardVisibilityOptions[cardIndex];
        this.tableView.SetCardVisibilityMode(this.selectedCardVisibility.Mode);

        this.ShowRaiseControlsCheckBox.IsChecked = settings.ShowRaiseControls;
        this.SetRaiseControlsVisible(settings.ShowRaiseControls);

        this.AiDecisionLogToggle.IsChecked = settings.EnableAiDecisionLog;
        this.ActionLogToggle.IsChecked = settings.ShowActionLog;
        this.AiRecommendationToggle.IsChecked = settings.ShowAiRecommendations;
    }

    private static int ClampIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (index < 0)
        {
            return 0;
        }

        return index >= count ? count - 1 : index;
    }

    private void AttachSeatCardHoverHandlers()
    {
        this.AttachSeatCardHover(1, this.Seat1CardsBorder);
        this.AttachSeatCardHover(2, this.Seat2CardsBorder);
        this.AttachSeatCardHover(3, this.Seat3CardsBorder);
        this.AttachSeatCardHover(4, this.Seat4CardsBorder);
        this.AttachSeatCardHover(5, this.Seat5CardsBorder);
        this.AttachSeatCardHover(6, this.Seat6CardsBorder);
        this.AttachSeatCardHover(7, this.Seat7CardsBorder);
        this.AttachSeatCardHover(8, this.Seat8CardsBorder);
    }

    private void AttachSeatCardHover(int seatIndex, Border border)
    {
        border.MouseEnter += (_, __) => this.tableView.SetSeatHover(seatIndex, true);
        border.MouseLeave += (_, __) => this.tableView.SetSeatHover(seatIndex, false);
    }

    private static GamePreset CreatePreset(string label, int smallBlind)
    {
        var bigBlind = smallBlind * 2;
        var startingStack = bigBlind * 100;
        return new GamePreset(label, smallBlind, bigBlind, startingStack);
    }

    private void GameSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.isUpdatingGameSizeSelection)
        {
            return;
        }

        if (this.GameSizeComboBox.SelectedItem is not GamePreset preset)
        {
            return;
        }

        if (!this.IsGameSizeChangeAllowed())
        {
            this.isUpdatingGameSizeSelection = true;
            this.GameSizeComboBox.SelectedItem = this.selectedGamePreset;
            this.isUpdatingGameSizeSelection = false;
            this.tableView.SetTurnInfo("Game size changes are only available between hands.");
            return;
        }

        this.selectedGamePreset = preset;

        if (this.gameTask != null)
        {
            this.restartMessage = $"Game size set to {preset.Label}. Click Start Game to restart.";
            this.RequestGameRestart();
            return;
        }

        this.tableView.SetTurnInfo($"Game size set to {preset.Label}. Click Start Game to begin.");
    }

    private void CardVisibilityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.isUpdatingCardVisibilitySelection)
        {
            return;
        }

        if (this.CardVisibilityComboBox.SelectedItem is not CardVisibilityOption option)
        {
            return;
        }

        this.selectedCardVisibility = option;
        this.tableView.SetCardVisibilityMode(option.Mode);
    }

    private void RequestGameRestart()
    {
        if (this.restartRequested)
        {
            return;
        }

        this.restartRequested = true;
        if (this.nextHandTcs != null)
        {
            this.nextHandTcs.TrySetResult(true);
            this.nextHandTcs = null;
        }
    }

    private void OnGameStopped(bool wasRestart)
    {
        this.gameTask = null;
        this.pendingAction = null;
        this.pendingContext = null;
        this.nextHandTcs = null;
        this.pausePending = false;
        this.isHandPaused = false;
        this.skipNextHandPause = false;

        this.SetActionControlsEnabled(false);
        this.NextHandButton.IsEnabled = true;
        this.NextHandButton.Tag = "StartGame";
        this.NextHandButton.Content = CreateUnderlinedContent("Start Game", 'S');
        this.UpdateGameSizeAvailability();

        if (wasRestart)
        {
            var message = string.IsNullOrWhiteSpace(this.restartMessage)
                ? "Game reset. Choose a game size and click Start Game."
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
    }

    private sealed class CardVisibilityOption
    {
        public CardVisibilityOption(string label, WpfTableView.CardVisibilityMode mode)
        {
            this.Label = label;
            this.Mode = mode;
        }

        public string Label { get; }

        public WpfTableView.CardVisibilityMode Mode { get; }
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

    private void AiDecisionLogToggle_Checked(object sender, RoutedEventArgs e)
    {
        DecisionLogger.Enabled = true;
    }

    private void AiDecisionLogToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        DecisionLogger.Enabled = false;
    }

    private void ActionLogToggle_Checked(object sender, RoutedEventArgs e)
    {
        this.ActionLogPanel.Visibility = Visibility.Visible;
        this.UpdateActionLogPanelPosition();
    }

    private void ActionLogToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        this.ActionLogPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowRaiseControlsCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        this.SetRaiseControlsVisible(true);
    }

    private void ShowRaiseControlsCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        this.SetRaiseControlsVisible(false);
    }

    private void SetRaiseControlsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        this.RaiseButton.Visibility = visibility;
        this.RaiseAmountTextBox.Visibility = visibility;
    }

    private void AiRecommendationToggle_Checked(object sender, RoutedEventArgs e)
    {
        this.AiRecommendationPanel.Visibility = Visibility.Visible;
        this.UpdateAiRecommendationPanelPosition();
    }

    private void AiRecommendationToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        this.AiRecommendationPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateAiRecommendationPanelPosition()
    {
        if (this.AiRecommendationPanel == null || this.AiRecommendationToggle == null || this.TableCanvas == null)
        {
            return;
        }

        if (this.AiRecommendationPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        this.AiRecommendationPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var panelHeight = this.AiRecommendationPanel.Height > 0
            ? this.AiRecommendationPanel.Height
            : this.AiRecommendationPanel.DesiredSize.Height;

        var togglePoint = this.AiRecommendationToggle.TransformToAncestor(this.TableCanvas)
            .Transform(new Point(0, 0));
        var left = togglePoint.X + this.AiRecommendationToggle.ActualWidth + 6;
        var top = togglePoint.Y + this.AiRecommendationToggle.ActualHeight - panelHeight;

        Canvas.SetLeft(this.AiRecommendationPanel, left);
        Canvas.SetTop(this.AiRecommendationPanel, top);
    }

    private void UpdateActionLogPanelPosition()
    {
        if (this.ActionLogPanel == null || this.ActionLogToggle == null || this.TableCanvas == null)
        {
            return;
        }

        if (this.ActionLogPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        this.ActionLogPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var panelWidth = this.ActionLogPanel.Width > 0
            ? this.ActionLogPanel.Width
            : this.ActionLogPanel.DesiredSize.Width;
        if (this.ActionLogPanel.Width <= 0 && panelWidth > 0)
        {
            this.ActionLogPanel.Width = panelWidth;
        }

        var togglePoint = this.ActionLogToggle.TransformToVisual(this.TableCanvas)
            .Transform(new Point(0, 0));
        var gap = 8;
        var toggleRight = togglePoint.X + this.ActionLogToggle.ActualWidth;
        var toggleTop = togglePoint.Y;
        var left = toggleRight - panelWidth;
        var maxLeft = Math.Max(0, this.TableCanvas.Width - panelWidth);
        if (left < 0)
        {
            left = 0;
        }
        else if (left > maxLeft)
        {
            left = maxLeft;
        }

        var availableHeight = Math.Max(0, toggleTop - gap);
        this.ActionLogPanel.Height = availableHeight;

        Canvas.SetLeft(this.ActionLogPanel, left);
        Canvas.SetTop(this.ActionLogPanel, 0);
    }
}
