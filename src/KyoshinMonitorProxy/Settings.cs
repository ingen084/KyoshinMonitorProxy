using System.Text.Json;

namespace KyoshinMonitorProxy
{
	public class Settings
	{
		public string? RootThumbprint { get; set; }
		public string? PersonalThumbprint { get; set; }

		public static async Task<Settings> LoadAsync()
		{
			if (!File.Exists("config.json"))
				return new();
			using var stream = File.OpenRead("config.json");
			return await JsonSerializer.DeserializeAsync<Settings>(stream) ?? new();
		}
		public async Task SaveAsync()
		{
			using var stream = File.OpenWrite("config.json");
			await JsonSerializer.SerializeAsync(stream, this);
		}
	}
}
