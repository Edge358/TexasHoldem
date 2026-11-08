namespace TexasHoldem.UI.Wpf.Properties;

using System.CodeDom.Compiler;
using System.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[CompilerGenerated]
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Matches settings designer output.")]
[GeneratedCode("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.0.0.0")]
internal sealed partial class Settings : ApplicationSettingsBase
{
    private static readonly Settings defaultInstance =
        (Settings)Synchronized(new Settings());

    public static Settings Default => defaultInstance;

    [UserScopedSetting]
    [DefaultSettingValue("-1")]
    public double WindowWidth
    {
        get => (double)this["WindowWidth"];
        set => this["WindowWidth"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("-1")]
    public double WindowHeight
    {
        get => (double)this["WindowHeight"];
        set => this["WindowHeight"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("-1")]
    public double WindowLeft
    {
        get => (double)this["WindowLeft"];
        set => this["WindowLeft"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("-1")]
    public double WindowTop
    {
        get => (double)this["WindowTop"];
        set => this["WindowTop"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("0")]
    public int WindowState
    {
        get => (int)this["WindowState"];
        set => this["WindowState"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool PersistWindowPlacement
    {
        get => (bool)this["PersistWindowPlacement"];
        set => this["PersistWindowPlacement"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("0")]
    public int GameSizeSelectionIndex
    {
        get => (int)this["GameSizeSelectionIndex"];
        set => this["GameSizeSelectionIndex"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("0")]
    public int CardVisibilitySelectionIndex
    {
        get => (int)this["CardVisibilitySelectionIndex"];
        set => this["CardVisibilitySelectionIndex"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool ShowRaiseControls
    {
        get => (bool)this["ShowRaiseControls"];
        set => this["ShowRaiseControls"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool ShowActionLog
    {
        get => (bool)this["ShowActionLog"];
        set => this["ShowActionLog"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool EnableAiDecisionLog
    {
        get => (bool)this["EnableAiDecisionLog"];
        set => this["EnableAiDecisionLog"] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("False")]
    public bool ShowAiRecommendations
    {
        get => (bool)this["ShowAiRecommendations"];
        set => this["ShowAiRecommendations"] = value;
    }
}
