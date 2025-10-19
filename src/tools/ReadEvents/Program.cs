using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;

var dbPath = Path.GetFullPath(Path.Combine("d:\\Projects\\WORK\\other\\Imperium\\src\\Imperium.Api\\data\\imperium.db"));
Console.WriteLine($"DB path: {dbPath}");

var options = new DbContextOptionsBuilder<ImperiumDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

using var db = new ImperiumDbContext(options);
var events = await db.GameEvents.OrderByDescending(e => e.Timestamp).Take(30).ToListAsync();

Console.WriteLine($"Last {events.Count} events:\n");
foreach (var e in events)
{
    Console.WriteLine($"{e.Timestamp:O} | {e.Type} | {e.Location} | {e.PayloadJson}");
}
