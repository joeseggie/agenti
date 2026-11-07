using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EastSeat.Agenti.IntegrationTests.Infrastructure;
using EastSeat.Agenti.Shared.Domain.Enums;
using EastSeat.Agenti.UnitTests.Helpers;
using EastSeat.Agenti.Web.Data;
using EastSeat.Agenti.Web.Features.BankStatements;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EastSeat.Agenti.E2ETests;

public sealed class AndroidDeviceFactAttribute : FactAttribute
{
    public AndroidDeviceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGENTI_ANDROID_TEST_APK")))
            Skip = "Requires the isolated reconciliation emulator and debug test APK; see DEVELOPMENT_GUIDE.";
    }
}

public sealed class ReconciliationAndroidTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private const string Package = "com.eastseat.agenti.reconciliationtest";

    [AndroidDeviceFact]
    [Trait("Category", "AndroidDevice")]
    public async Task Reconciliation_AndroidDraftRecoveryCaptureAndClosing()
    {
        var apk = Environment.GetEnvironmentVariable("AGENTI_ANDROID_TEST_APK")!;
        Assert.StartsWith(Package, Path.GetFileName(apk));
        var device = new BankingTestDevice(
            Environment.GetEnvironmentVariable("AGENTI_ADB_PATH") ?? "adb",
            Environment.GetEnvironmentVariable("AGENTI_ANDROID_SERIAL") ?? throw new InvalidOperationException("Set AGENTI_ANDROID_SERIAL."));
        Assert.Contains("agenti-reconciliation-", await device.CommandAsync("emu", "avd", "name"));
        await device.CommandAsync("shell", "input", "keyevent", "KEYCODE_WAKEUP");
        await device.CommandAsync("shell", "wm", "dismiss-keyguard");
        await device.CommandAsync("shell", "svc", "power", "stayon", "true");
        await using var db = database.CreateContext();
        using var scenario = new BankingScenario(db);
        scenario.AddApprovedOpening(withSettlement: true);
        scenario.Account.WorkflowEnabled = true;
        scenario.Account.ImportProfileVerified = true;
        scenario.Account.ImportProfileKey = UniversalBankCsvStatementProfile.ProfileKey;
        scenario.Account.BankIdentifier = "synthetic-test-bank";
        (await db.AppConfigs.SingleAsync(c => c.Key == "SetupComplete")).Value = "true";
        var password = "Banking" + Guid.NewGuid().ToString("N");
        ConfigureLogin(scenario.User, "agent@banking.invalid", password);
        await db.SaveChangesAsync();

        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generatedCertificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generatedCertificate.Export(X509ContentType.Pfx),
            password: null, keyStorageFlags: X509KeyStorageFlags.UserKeySet);
        await using var app = new BankingApplicationFactory(database.ConnectionString);
        app.UseKestrel(options => options.Listen(IPAddress.Loopback, 58731, endpoint => endpoint.UseHttps(certificate)));
        using var startup = app.CreateClient();
        Assert.Contains("https://127.0.0.1:58731",
            app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
        using var probe = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented?.Thumbprint == certificate.Thumbprint
        });
        using var ready = await probe.GetAsync("https://127.0.0.1:58731/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        await device.CommandAsync("reverse", "tcp:58731", "tcp:58731");
        try
        {
            await device.CommandAsync("shell", "am", "force-stop", Package);
            await device.CommandAsync("install", "--no-incremental", "-r", apk);
            await device.LaunchAsync(Package);
            await LoginAsync(device, scenario.User.Email!, password);
            await device.MenuAsync("Customer banking");
            await device.ClickTextAsync("Record customer transaction");
            await device.FillAsync("banking-amount", "50000");
            await device.FillAsync("banking-customer", "0012345");
            await device.CommandAsync("shell", "am", "force-stop", Package);
            await device.LaunchAsync(Package);
            await device.MenuAsync("Customer banking");
            await device.ClickTextAsync("Record customer transaction");
            Assert.Equal("50000", await device.ValueAsync("banking-amount"));
            Assert.Equal("0012345", await device.ValueAsync("banking-customer"));
            await device.FillAsync("banking-reference", "SYNTHETIC-DEPOSIT");
            await device.FillAsync("banking-time", "2026-11-02 12:00:00");
            await device.ClickTextAsync("Save transaction");
            await device.WaitTextAsync("Customer completed:");
            Assert.Equal(1, await db.CustomerTransactions.CountAsync());

            await device.MenuAsync("Dashboard");
            await device.ClickTextAsync("Cash Count");
            await device.ClickTextAsync("Closing count");
            await device.FillAsync($"banking-amount-{scenario.CashWallet.Id}", "150000");
            await device.FillAsync($"banking-observed-{scenario.CashWallet.Id}", "2026-11-02 13:00:00");
            await device.FillAsync($"banking-amount-{scenario.BankWallet.Id}", "950000");
            await device.FillAsync($"banking-observed-{scenario.BankWallet.Id}", "2026-11-02 13:00:00");
            await device.ClickTextAsync("Submit and end trading");
            await device.ClickTextAsync("Submit");
            await device.WaitTextAsync("Session ended");

            await using var final = database.CreateContext();
            Assert.Equal(1, await final.CustomerTransactions.CountAsync());
            Assert.Equal(CashCountStatus.PendingApproval, (await final.CashCounts.AsNoTracking().SingleAsync(c => !c.IsOpening)).Status);
            Assert.Equal(1, await final.SessionFloatSettlements.CountAsync(s => s.Stage == FloatSettlementStage.Closing));
            Assert.Equal(1_100_000m, (await final.Vaults.SingleAsync()).CurrentBalance);
            Assert.All(await final.Wallets.ToListAsync(), wallet => Assert.Equal(0m, wallet.Balance));
        }
        catch (Exception exception)
        {
            var pid = (await device.CommandAsync("shell", "pidof", Package)).Trim();
            var log = string.IsNullOrEmpty(pid) ? "The test application is not running." :
                await device.CommandAsync("logcat", "-d", "--pid=" + pid, "-t", "75");
            throw new InvalidOperationException("Native banking journey failed. Test-device diagnostics:\n" + log, exception);
        }
        finally
        {
            await device.CommandAsync("shell", "am", "force-stop", Package);
            await device.CommandAsync("uninstall", Package);
            await device.CommandAsync("reverse", "--remove", "tcp:58731");
        }
    }

    private static void ConfigureLogin(ApplicationUser user, string email, string password)
    {
        user.Email = user.UserName = email;
        user.NormalizedEmail = user.NormalizedUserName = email.ToUpperInvariant();
        user.EmailConfirmed = true;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, password);
    }

    private static async Task LoginAsync(BankingTestDevice device, string email, string password)
    {
        await device.FillAsync("login-email", email);
        await device.FillAsync("login-password", password);
        await device.ClickTextAsync("Sign In");
        await device.WaitTextAsync("Welcome");
    }
}

internal sealed class BankingTestDevice(string adb, string serial)
{
    public async Task<string> CommandAsync(params string[] arguments)
    {
        if (!serial.StartsWith("emulator-", StringComparison.Ordinal))
            throw new InvalidOperationException("Banking UI tests must not run against a physical device.");
        using var process = new Process
        {
            StartInfo = new(adb) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }
        };
        var serverPort = Environment.GetEnvironmentVariable("AGENTI_ADB_SERVER_PORT");
        if (!string.IsNullOrWhiteSpace(serverPort))
        {
            process.StartInfo.ArgumentList.Add("-P");
            process.StartInfo.ArgumentList.Add(serverPort);
        }
        process.StartInfo.ArgumentList.Add("-s");
        process.StartInfo.ArgumentList.Add(serial);
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("The isolated emulator command failed: " + await error);
        return await output + await error;
    }

    public Task LaunchAsync(string package) => CommandAsync("shell", "monkey", "-p", package, "-c", "android.intent.category.LAUNCHER", "1");

    private async Task<XDocument> WindowAsync()
    {
        var diagnostic = string.Empty;
        await CommandAsync("shell", "input", "keyevent", "KEYCODE_WAKEUP");
        for (var attempt = 0; attempt < 15; attempt++)
        {
            diagnostic = await CommandAsync("shell", "uiautomator", "dump", "/data/local/tmp/agenti-banking-window.xml");
            if (diagnostic.Contains("dumped to:", StringComparison.Ordinal))
            {
                var xml = await CommandAsync("exec-out", "cat", "/data/local/tmp/agenti-banking-window.xml");
                if (xml.TrimStart().StartsWith("<?xml", StringComparison.Ordinal))
                    return XDocument.Parse(xml);
                diagnostic += xml;
            }
            await Task.Delay(500);
        }
        throw new InvalidOperationException("The isolated emulator did not provide a current UI hierarchy: " + diagnostic);
    }

    private static bool HasId(XElement node, string id) =>
        node.Attribute("resource-id")?.Value.EndsWith(id, StringComparison.Ordinal) == true ||
        node.Attribute("content-desc")?.Value == id;

    private async Task<XElement> FindAsync(Func<XElement, bool> predicate, bool scroll)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var window = await WindowAsync();
            var node = window.Descendants("node").FirstOrDefault(n => predicate(n) && n.Attribute("enabled")?.Value == "true");
            if (node != null) return node;
            if (scroll && attempt >= 2) await CommandAsync("shell", "input", "swipe", "540", "1800", "540", "500", "350");
            else await Task.Delay(500);
        }
        var last = await WindowAsync();
        throw new InvalidOperationException("Banking control was not found. Visible controls: " +
            string.Join(" | ", last.Descendants("node").Select(n => $"{n.Attribute("text")?.Value} [{n.Attribute("resource-id")?.Value}]")));
    }

    private async Task TapAsync(XElement node)
    {
        var coordinates = Regex.Matches(node.Attribute("bounds")?.Value ?? "", @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        if (coordinates.Length != 4) throw new InvalidOperationException("The control has no usable bounds.");
        await CommandAsync("shell", "input", "tap", ((coordinates[0] + coordinates[2]) / 2).ToString(),
            ((coordinates[1] + coordinates[3]) / 2).ToString());
    }

    public async Task ClickTextAsync(string text) => await TapAsync(await FindAsync(
        n => string.Equals(n.Attribute("text")?.Value, text, StringComparison.OrdinalIgnoreCase), scroll: true));

    public async Task WaitTextAsync(string text) => _ = await FindAsync(
        n => n.Attribute("text")?.Value.Contains(text, StringComparison.OrdinalIgnoreCase) == true, scroll: false);

    public async Task FillAsync(string id, string value)
    {
        var node = await FindAsync(n => HasId(n, id), scroll: true);
        await TapAsync(node);
        await CommandAsync("shell", "input", "keyevent", "KEYCODE_MOVE_END");
        var deletes = Math.Min(256, node.Attribute("text")?.Value.Length ?? 0) + 3;
        await CommandAsync(new[] { "shell", "input", "keyevent" }.Concat(Enumerable.Repeat("67", deletes)).ToArray());
        await CommandAsync("shell", "input", "text", value.Replace(" ", "%s"));
        await CommandAsync("shell", "input", "keyevent", "4");
    }

    public async Task<string> ValueAsync(string id) =>
        (await FindAsync(n => HasId(n, id), scroll: true)).Attribute("text")?.Value ?? "";

    public async Task MenuAsync(string item)
    {
        var menu = await FindAsync(n => n.Attribute("content-desc")?.Value.Contains("navigation", StringComparison.OrdinalIgnoreCase) == true,
            scroll: false);
        await TapAsync(menu);
        await ClickTextAsync(item);
    }
}
