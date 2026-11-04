using MudBlazor;

namespace EastSeat.Agenti.Web.Features.Theme;

public static class AppThemes
{
    public static MudTheme LightTheme => new()
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#174b8f",
            Secondary = "#07866f",
            AppbarBackground = "#12243a",
            AppbarText = "#ffffff",
            Background = "#f4f7fb",
            Surface = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = "#26384d",
            DrawerIcon = "#66788a",
            Success = "#16835d",
            Info = "#2879c7",
            Warning = "#b87512",
            Error = "#c43d4d",
            TextPrimary = "#1a2b40",
            TextSecondary = "#64748b",
            ActionDefault = "#64748b",
            ActionDisabled = "rgba(26,43,64, 0.26)",
            Divider = "rgba(26,43,64, 0.10)",
        }
    };

    public static MudTheme DarkTheme => new()
    {
        PaletteDark = new PaletteDark()
        {
            Primary = "#8ab8f5",
            Secondary = "#55d6b3",
            AppbarBackground = "#101b2a",
            AppbarText = "#f5f8fc",
            Background = "#111a27",
            Surface = "#1a2738",
            DrawerBackground = "#152132",
            DrawerText = "#e3ebf5",
            DrawerIcon = "#a9b8cb",
            Success = "#5cd6a3",
            Info = "#70b8ff",
            Warning = "#ffc46b",
            Error = "#ff8792",
            TextPrimary = "#e8eef7",
            TextSecondary = "#a9b8cb",
            ActionDefault = "#a9b8cb",
            ActionDisabled = "rgba(232,238,247, 0.30)",
            Divider = "rgba(232,238,247, 0.10)",
        }
    };
}
