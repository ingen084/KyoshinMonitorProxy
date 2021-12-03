using KyoshinMonitorProxy;
using Sharprompt;
using System.Diagnostics;
using System.Net;
using System.Runtime;

var config = await Settings.LoadAsync();

#if !DEBUG
if (args.Length > 0 && args.Contains("--service"))
{
}
else
{
	Console.Title = "KyoshinMonitorProxy 直接起動メニュー";
	var sel = Prompt.Select("なにをしますか？", new[] {
		"1. サービスのインストール",
		"2. サービスのアンインストール",
		"3. 証明書の削除",
	});

	if (sel.StartsWith("1."))
	{
		var p1 = Process.Start(new ProcessStartInfo("sc.exe", $"create \"KyoshinMonitorProxy\" start=auto binpath=\"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KyoshinMonitorProxy.exe")} --service\" displayname=\"強震モニタ プロキシサービス\""));
		if (p1 != null)
			await p1.WaitForExitAsync();
		var p2 = Process.Start(new ProcessStartInfo("sc.exe", $"start \"KyoshinMonitorProxy\""));
		if (p2 != null)
			await p2.WaitForExitAsync();
		return;
	}
	else if (sel.StartsWith("2."))
	{
		var p1 = Process.Start(new ProcessStartInfo("sc.exe", $"delete \"KyoshinMonitorProxy\""));
		if (p1 != null)
			await p1.WaitForExitAsync();
		return;
	}
	else if (sel.StartsWith("3."))
	{
		CertManager.RemoveCert(config);
		return;
	}
	else
		return;
}
#endif

Console.WriteLine("証明書を確認しています");

Host.CreateDefaultBuilder(args)
	.UseWindowsService().ConfigureServices(services =>
	{
		services.AddHostedService<WindowsBackgroundService>();
	}).Build().Run();

public sealed class WindowsBackgroundService : BackgroundService
{
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
		await using var app = builder.Build();

		var hostsLine = "127.0.0.100 www.lmoni.bosai.go.jp www.kmoni.bosai.go.jp";

		app.Lifetime.ApplicationStopped.Register(() =>
		{
			HostsController.RemoveAsync(hostsLine).Wait();
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

			if (c.Request.Host.HasValue && c.Request.Host.Value.EndsWith("bosai.go.jp"))
			{
				await controller.FetchAndWriteAsync(c);
				return;
			}
			await BadRequest("cannot proxied host name");
		});

		var timer = new Timer(_ =>
		{
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
#if DEBUG
			Logger.LogWarning("LOH GC Before: {memory}", GC.GetTotalMemory(false));
#endif
			GC.Collect(2, GCCollectionMode.Optimized, true, true);
#if DEBUG
			Logger.LogWarning("LOH GC After: {memory}", GC.GetTotalMemory(true));
#endif
		}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

		await HostsController.AddAsync(hostsLine);
		await app.RunAsync(stoppingToken);
	}
}

