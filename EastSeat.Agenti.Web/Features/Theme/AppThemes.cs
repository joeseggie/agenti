using MudBlazor;

namespace EastSeat.Agenti.Web.Features.Theme;

public static class AppThemes
{
    public static MudTheme LightTheme => new()
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#1976d2",
            Secondary = "#424242",
            AppbarBackground = "#1976d2",
            Background = "#ffffff",
            Surface = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = "rgba(0,0,0, 0.87)",
            DrawerIcon = "rgba(0,0,0, 0.54)",
            Success = "#4caf50",
            Info = "#2196f3",
            Warning = "#ff9800",
            Error = "#f44336",
            TextPrimary = "rgba(0,0,0, 0.87)",
            TextSecondary = "rgba(0,0,0, 0.60)",
            ActionDefault = "rgba(0,0,0, 0.54)",
            ActionDisabled = "rgba(0,0,0, 0.26)",
            Divider = "rgba(0,0,0, 0.12)",
        }
    };

    public static MudTheme DarkTheme => new()
    {
        PaletteDark = new PaletteDark()
        {
            Primary = "#bbdefb",
            Secondary = "#ce93d8",
            AppbarBackground = "#1e1e1e",
            Background = "#121212",
            Surface = "#1e1e1e",
            DrawerBackground = "#1e1e1e",
            DrawerText = "rgba(255,255,255, 0.87)",
            DrawerIcon = "rgba(255,255,255, 0.7)",
            Success = "#66bb6a",
            Info = "#42a5f5",
            Warning = "#ffa726",
            Error = "#ef5350",
            TextPrimary = "rgba(255,255,255, 0.87)",
            TextSecondary = "rgba(255,255,255, 0.60)",
            ActionDefault = "rgba(255,255,255, 0.70)",
            ActionDisabled = "rgba(255,255,255, 0.30)",
            Divider = "rgba(255,255,255, 0.12)",
        }
    };
}
