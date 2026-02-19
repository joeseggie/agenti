using Microsoft.Extensions.Logging;
using EastSeat.Agenti.Android.Features.Auth;
using EastSeat.Agenti.Android.Features.Dashboard;
using EastSeat.Agenti.Android.Features.CashSessions;
using EastSeat.Agenti.Android.Features.CashCounts;
using Microsoft.Extensions.DependencyInjection;

namespace EastSeat.Agenti.Android;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		// Register the named HttpClient for Agenti API
		builder.Services.AddHttpClient("AgentiApi", client =>
		{
			// Base address can be configured via app settings or environment
			var baseUrl = DeviceInfo.Platform == DevicePlatform.Android
				? "http://10.0.2.2:5113/"  // Android emulator loopback for localhost
				: "http://localhost:5113/";
			client.BaseAddress = new Uri(baseUrl);
		})
		.ConfigurePrimaryHttpMessageHandler(() =>
		{
#if DEBUG
			// Allow self-signed certificates in debug builds
			return new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (_, _, _, _) => true
			};
#else
			return new HttpClientHandler();
#endif
		});

		// Register application services with the named HttpClient
		builder.Services.AddScoped<IAuthService>(sp =>
		{
			var factory = sp.GetRequiredService<IHttpClientFactory>();
			return new AuthService(factory.CreateClient("AgentiApi"));
		});
		builder.Services.AddScoped<IDashboardService>(sp =>
		{
			var factory = sp.GetRequiredService<IHttpClientFactory>();
			return new DashboardService(factory.CreateClient("AgentiApi"));
		});
		builder.Services.AddScoped<ICashSessionService>(sp =>
		{
			var factory = sp.GetRequiredService<IHttpClientFactory>();
			return new CashSessionService(factory.CreateClient("AgentiApi"));
		});
		builder.Services.AddScoped<ICashCountService>(sp =>
		{
			var factory = sp.GetRequiredService<IHttpClientFactory>();
			return new CashCountService(factory.CreateClient("AgentiApi"));
		});

		return builder.Build();
	}
}
