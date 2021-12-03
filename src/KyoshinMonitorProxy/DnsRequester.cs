using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Serialization;

namespace KyoshinMonitorProxy
{
	public class DnsRequester
	{
		private ConcurrentDictionary<string, CacheEntry> ResponseCache = new();

		record class CacheEntry(DateTime ExpireTime, IPAddress Address);

		private HttpClient Client { get; } = new();

		public async Task<IPAddress?> ResolveAsync(string hostName)
		{
			if (ResponseCache.TryGetValue(hostName, out var c) && c.ExpireTime > DateTime.Now)
				return c.Address;

			var resp = await Client.GetFromJsonAsync<DohJsonResponse>($"https://dns.google.com/resolve?name={hostName}&type=1");
			if (resp == null)
				return null;
			if (resp.Answer?.FirstOrDefault(a => a.Type == 1) is not Answer data)
				return null;
			var addr = IPAddress.Parse(data.Data);
			ResponseCache[hostName] = new CacheEntry(DateTime.Now.AddSeconds(data.TTL), addr);
			return addr;
		}
	}


	public class DohJsonResponse
	{
		public int Status { get; set; }
		//public bool TC { get; set; }
		//public bool RD { get; set; }
		//public bool RA { get; set; }
		//public bool AD { get; set; }
		//public bool CD { get; set; }
		// public Question[] Question { get; set; }
		public Answer[]? Answer { get; set; }
		// public string Comment { get; set; }
	}

	//public class Question
	//{
	//	public string name { get; set; }
	//	public int type { get; set; }
	//}

	public class Answer
	{
		[JsonPropertyName("name")]
		public string Name { get; set; }
		[JsonPropertyName("type")]
		public int Type { get; set; }
		[JsonPropertyName("TTL")]
		public int TTL { get; set; }
		[JsonPropertyName("data")]
		public string Data { get; set; }
	}
}
