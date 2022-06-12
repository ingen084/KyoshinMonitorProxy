using KyoshinMonitorProxy;
using Sharprompt;
using System.Diagnostics;
using System.Net;
using System.Runtime;
using System.Text.Json;
using Timer = System.Threading.Timer;

var config = await Settings.LoadAsync();

if (args.Length > 0 && args.Contains("--service"))
{
}
else
{
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());

    //Console.Title = "KyoshinMonitorProxy 直接起動メニュー";
    //var sel = Prompt.Select("なにをしますか？", new[] {
    //    "1. サービスのインストール",
    //    "2. サービスのアンインストール",
    //    "3. 証明書の削除",
    //});

    //if (sel.StartsWith("1."))
    //{
    //    var p1 = Process.Start(new ProcessStartInfo("sc.exe", $"create \"KyoshinMonitorProxy\" start=auto binpath=\"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KyoshinMonitorProxy.exe")} --service\" displayname=\"強震モニタ プロキシサービス\""));
    //    if (p1 != null)
    //        await p1.WaitForExitAsync();
    //    var p2 = Process.Start(new ProcessStartInfo("sc.exe", $"start \"KyoshinMonitorProxy\""));
    //    if (p2 != null)
    //        await p2.WaitForExitAsync();
    //    return;
    //}
    //else if (sel.StartsWith("2."))
    //{
    //    var p2 = Process.Start(new ProcessStartInfo("sc.exe", $"stop \"KyoshinMonitorProxy\""));
    //    if (p2 != null)
    //        await p2.WaitForExitAsync();
    //    var p1 = Process.Start(new ProcessStartInfo("sc.exe", $"delete \"KyoshinMonitorProxy\""));
    //    if (p1 != null)
    //        await p1.WaitForExitAsync();
    //    return;
    //}
    //else if (sel.StartsWith("3."))
    //{
    //    CertManager.RemoveCert(config);
    //    return;
    //}
    //else
        return;
}

const string HOSTS_LINE = "127.0.0.100 smi.lmoniexp.bosai.go.jp www.lmoni.bosai.go.jp www.kmoni.bosai.go.jp";

AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    HostsController.RemoveAsync(HOSTS_LINE).Wait();
};

Host.CreateDefaultBuilder(args)
    .UseWindowsService().ConfigureServices(services =>
    {
        services.AddHostedService<WindowsBackgroundService>();
    }).Build().Run();

public sealed class WindowsBackgroundService : BackgroundService
{
    const string HOSTS_LINE = "127.0.0.100 smi.lmoniexp.bosai.go.jp www.lmoni.bosai.go.jp www.kmoni.bosai.go.jp";
    private Timer? Timer { get; set; }

    private ILogger<WindowsBackgroundService> Logger { get; }

    public WindowsBackgroundService(ILogger<WindowsBackgroundService> logger)
    {
        Logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = await Settings.LoadAsync();
        var cert = CertManager.CreateOrGetCert(config);
        await config.SaveAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddMemoryCache(options =>
        {
            options.SizeLimit = 100; // キャッシュは100ファイルまでに制限する
            options.CompactionPercentage = .5;
            options.ExpirationScanFrequency = TimeSpan.FromSeconds(10);
        });
        builder.Services.AddSingleton<CacheController>();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(IPAddress.Parse("127.0.0.100"), 80);
            serverOptions.Listen(IPAddress.Parse("127.0.0.100"), 443, configure =>
            {
                configure.UseHttps(cert);
            });
        });
        builder.UseSentry(o =>
        {
            o.Dsn = "https://e1d8569e89f34778a6fee01d3e37e7cc@sentry.ingen084.net/5";
            // When configuring for the first time, to see what the SDK is doing:
            o.Debug = true;
            // Set TracesSampleRate to 1.0 to capture 100% of transactions for performance monitoring.
            // We recommend adjusting this value in production.
            o.TracesSampleRate = 1.0;
        });
        await using var app = builder.Build();

        app.Lifetime.ApplicationStopped.Register(() =>
        {
            HostsController.RemoveAsync(HOSTS_LINE).Wait();
        });

        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();

        var controller = app.Services.GetRequiredService<CacheController>();

        app.Use(async (HttpContext c, Func<Task> _) =>
        {
            Task BadRequest(string message)
            {
                c.Response.StatusCode = 400;
                c.Response.Headers.ContentType = "text/plain";
                return c.Response.WriteAsync($"400 Bad Request / {message}(KyoshinMonitorProxy)");
            }

            controller.CacheStats.RemoveAll(s => (DateTime.Now - s.reqTime) >= TimeSpan.FromMinutes(1));
            if (c.Request.Path.HasValue && c.Request.Path.Value == "/kmp-status")
            {
                c.Response.StatusCode = 200;
                c.Response.Headers.ContentType = "text/plain; charset=\"UTF-8\"";
                await c.Response.WriteAsync("KyoshinMonitorProxy Ver.0.0.2\n");
                await c.Response.WriteAsync($"統計情報({DateTime.Now:yyyy/MM/dd HH:mm:ss}現在 過去1分間)\n");
                var total = controller.CacheStats.Count;
                var hitTotal = controller.CacheStats.Count(s => s.isHit);
                var missTotal = controller.CacheStats.Count(s => !s.isHit);
                await c.Response.WriteAsync($"リクエスト数: {total:#,0}\n");
                await c.Response.WriteAsync($"キャッシュヒット数: {hitTotal:#,0} ({(total == 0 ? 0 : (hitTotal / (double)total)):P2})\n");
                await c.Response.WriteAsync($"キャッシュミス数: {missTotal:#,0} ({(total == 0 ? 0 : (missTotal / (double)total)):P2})\n");
                await c.Response.WriteAsync($"メモリ使用量: {GC.GetTotalMemory(true):#,0}bytes\n");

                await c.Response.WriteAsync("\nソースコード: https://github.com/ingen084/KyoshinMonitorProxy\n更新情報: https://github.com/ingen084/KyoshinMonitorProxy/releases");
                return;
            }
            else if (c.Request.Path.HasValue && c.Request.Path.Value == "/kmp-status.json")
            {
                c.Response.StatusCode = 200;
                await JsonSerializer.SerializeAsync(c.Response.Body, new StatusModel
                {
                    Version = "0.0.2",
                    RequestCount = controller.CacheStats.Count,
                    HitCacheCount = controller.CacheStats.Count(s => s.isHit),
                    MissCacheCount = controller.CacheStats.Count(s => !s.isHit),
                    UsedMemoryBytes = GC.GetTotalMemory(true),
                }, StatusModelContext.Default.StatusModel);
                return;
            }

            else if (c.Request.Host.HasValue && c.Request.Host.Value.EndsWith("bosai.go.jp"))
            {
                await controller.FetchAndWriteAsync(c);
                return;
            }
            await BadRequest("cannot proxied host name");
        });

        Timer = new Timer(_ =>
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
#if DEBUG
            Logger.LogWarning("LOH GC Before: {memory}", GC.GetTotalMemory(false));
#endif
            GC.Collect(2, GCCollectionMode.Optimized, true, true);
#if DEBUG
            Logger.LogWarning("LOH GC After: {memory}", GC.GetTotalMemory(true));
#endif
            GC.KeepAlive(Timer);
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        await HostsController.AddAsync(HOSTS_LINE);
        await app.RunAsync(stoppingToken);
        GC.KeepAlive(Timer);
    }
}

