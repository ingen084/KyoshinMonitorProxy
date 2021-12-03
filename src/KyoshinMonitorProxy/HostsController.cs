namespace KyoshinMonitorProxy
{
	public static class HostsController
	{
		public static async Task AddAsync(string entry)
		{
			var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
			if ((await File.ReadAllLinesAsync(hosts)).Contains(entry))
				return;
			await File.AppendAllLinesAsync(hosts, new[] { entry });
		}

		public static async Task RemoveAsync(string entry)
		{
			var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
			var lines = await File.ReadAllLinesAsync(hosts);
			if (!lines.Contains(entry))
				return;
			await File.WriteAllLinesAsync(hosts, lines.Where(l => l != entry));
		}
	}
}
