using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;

var dbPath = Path.GetFullPath("d:\\Projects\\WORK\\other\\Imperium\\src\\Imperium.Api\\data\\imperium.db");
Console.WriteLine($"DB path: {dbPath}");

var options = new DbContextOptionsBuilder<ImperiumDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

using var db = new ImperiumDbContext(options);
var count = await db.Characters.CountAsync();
Console.WriteLine($"Characters count: {count}");

var rows = await db.Characters.OrderBy(c => c.Name).Take(5).ToListAsync();
Console.WriteLine("Sample rows:");
foreach (var c in rows)
{
    Console.WriteLine($"{c.Id} | {c.Name} | Age={c.Age} | Status={c.Status} | SkillsJson={c.SkillsJson}");
}
