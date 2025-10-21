using System;
using Microsoft.Data.Sqlite;

var dbPath = Path.GetFullPath(Path.Combine("src", "Imperium.Api", "data", "imperium.db"));
if (!System.IO.File.Exists(dbPath)) { Console.Error.WriteLine($"DB not found: {dbPath}"); return 1; }
using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();
using var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT MigrationId, ProductVersion FROM \"__EFMigrationsHistory\" ORDER BY MigrationId";
			using var reader = cmd.ExecuteReader();
			while(reader.Read())
			{
				Console.WriteLine(reader.GetString(0) + " | " + reader.GetString(1));
			}
			// delete TempSync if exists
			var del = conn.CreateCommand();
			del.CommandText = "DELETE FROM \"__EFMigrationsHistory\" WHERE MigrationId = '20251021151030_TempSync'; SELECT changes();";
			var changes = del.ExecuteScalar();
			Console.WriteLine($"Delete executed. Result: {changes}");
return 0;
