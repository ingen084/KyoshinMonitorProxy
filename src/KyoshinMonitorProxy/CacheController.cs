﻿using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System;
using MessagePack;
using Microsoft.Extensions.Caching.Memory;

namespace KyoshinMonitorProxy
{
	public class CacheController
	{
		private HttpClient Client { get; }
		private DnsRequester Dns { get; }

		public CacheController(IMemoryCache cache)
		{
			var httpClientHandler = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
				{
					return true;
				},
				AutomaticDecompression = DecompressionMethods.All
			};
			Client = new HttpClient(httpClientHandler);
			Dns = new DnsRequester();
			Cache = cache;
		}

		private ConcurrentDictionary<string, ManualResetEventSlim> LockCache { get; } = new();

		// キャッシュに保存させないサイズ
		const uint CACHE_CONTENT_SIZE = 80000;

		public record class CacheEntry(
			HttpStatusCode StatusCode,
			HttpResponseHeaders Headers,
			HttpContentHeaders ContentHeaders,
			byte[] Body);

		private IMemoryCache Cache { get; }

		public async Task FetchAndWriteAsync(HttpContext context)
		{
			// キーとなるリクエストURLを生成する
			string url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}";

			CacheEntry? cache;

			// キャッシュされていればキャッシュを返す
			if (Cache.TryGetValue(url, out cache) && cache != null)
			{
				Console.WriteLine("キャッシュ利用: " + url);
				await Write(context, cache);
				return;
			}

			// 現在取得中のスレッドが存在する場合待機して再帰呼び出し
			if (LockCache.TryGetValue(url, out var mre))
			{
				Console.WriteLine("キャッシュ待機中: " + url);
				await Task.Run(() => mre.Wait(10000), context.RequestAborted);
				await FetchAndWriteAsync(context);
				return;
			}

			// 取得を開始する
			mre = new(false);
			// ロックキャッシュに挿入、できなければすでに他のスレッドが取得を開始しているので再帰呼び出し
			if (!LockCache.TryAdd(url, mre))
			{
				Console.WriteLine("ロックキャッシュ再起: " + url);
				await FetchAndWriteAsync(context);
				return;
			}

			Console.WriteLine("フェッチ開始: " + url);
			//await Semaphore.WaitAsync(10000, context.RequestAborted);
			try
			{
				var ipaddr = await Dns.ResolveAsync(context.Request.Host.Value);
				if (ipaddr == null)
					throw new Exception("cannot resolve IP addr: " + context.Request.Host.Value);

				using var response = await Client.SendAsync(context.CreateProxiedHttpRequest(context.Request.Scheme + "://" + ipaddr.ToString()), HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
				// GETではない、もしくはコンテンツのサイズが大きすぎる場合
				if (!HttpMethods.IsGet(context.Request.Method) || response.Content.Headers.ContentLength > CACHE_CONTENT_SIZE)
                {
					await context.WriteProxiedHttpResponseAsync(response);
					return;
                }
				var cacheObject = new CacheEntry(response.StatusCode, response.Headers, response.Content.Headers, await response.Content.ReadAsByteArrayAsync());

				// ネガティブキャッシュはしない
				if (response.StatusCode != HttpStatusCode.NotFound)
					Cache.Set(url, cacheObject, new MemoryCacheEntryOptions() 
					{
						AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
						Size = 1,
					});

				await Write(context, cacheObject);
			}
			finally
			{
				// ロック開放
				mre.Set();
				//Console.WriteLine("Semaphore: " + Semaphore.Release());
				LockCache.Remove(url, out _);
			}
		}

		private static async Task Write(HttpContext context, CacheEntry entry)
		{
			var response = context.Response;

			response.StatusCode = (int)entry.StatusCode;
			foreach (var header in entry.Headers)
				response.Headers[header.Key] = header.Value.ToArray();

			foreach (var header in entry.ContentHeaders)
				response.Headers[header.Key] = header.Value.ToArray();

			response.Headers.Remove("transfer-encoding");
			response.Headers.Remove("connection");

			await response.Body.WriteAsync(entry.Body.AsMemory(0, entry.Body.Length), context.RequestAborted);
		}
	}
}
