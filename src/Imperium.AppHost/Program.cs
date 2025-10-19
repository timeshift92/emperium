
using Imperium.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args).AddServiceDefaults();

Console.WriteLine("Imperium.AppHost — Orchestration stub. Configure projects/services here.");
Console.WriteLine("Environment: " + builder.Environment.EnvironmentName);
Console.WriteLine("Set OPENAI_API_KEY in env vars for Imperium.Api.");

await Task.CompletedTask;
