using System.Net;
using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Api.Models;
using Xunit;

namespace Imperium.Api.Tests
{
    public class EconomyItemDefsApiTests
    {
        [Fact]
        public async Task GetItemDefs_ReturnsSeedDefinitions()
        {
            var builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(new Imperium.Api.EconomyStateService(new[] { "grain" }));
                    services.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/economy/item-defs", async context =>
                        {
                            var state = context.RequestServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                            await context.Response.WriteAsJsonAsync(state.GetDefinitions());
                        });
                        endpoints.MapGet("/api/economy/item-defs/{name}", async context =>
                        {
                            var name = context.Request.RouteValues["name"]?.ToString() ?? string.Empty;
                            var state = context.RequestServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                            var d = state.GetDefinition(name);
                            if (d == null)
                            {
                                context.Response.StatusCode = 404;
                                return;
                            }
                            await context.Response.WriteAsJsonAsync(d);
                        });
                    });
                });

            using var server = new TestServer(builder);
            var client = server.CreateClient();

            var res = await client.GetAsync("/api/economy/item-defs");
            res.EnsureSuccessStatusCode();
            var defs = await res.Content.ReadFromJsonAsync<EconomyItemDefinition[]>();
            Assert.NotNull(defs);
            Assert.Contains(defs, d => d.Name == "grain");

            // test single item
            var one = await client.GetAsync("/api/economy/item-defs/grain");
            one.EnsureSuccessStatusCode();
            var g = await one.Content.ReadFromJsonAsync<EconomyItemDefinition>();
            Assert.NotNull(g);
            Assert.Equal("grain", g!.Name);
        }

        [Fact]
        public async Task PostItemDef_CreatesAndRetrievesDefinition()
        {
            var builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(new Imperium.Api.EconomyStateService(new[] { "grain" }));
                    services.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/api/economy/item-defs", async context =>
                        {
                            var def = await context.Request.ReadFromJsonAsync<EconomyItemDefinition>();
                            if (def == null || string.IsNullOrWhiteSpace(def.Name))
                            {
                                context.Response.StatusCode = 400;
                                await context.Response.WriteAsJsonAsync(new { error = "требуется имя (Name)" });
                                return;
                            }
                            var state = context.RequestServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                            state.AddOrUpdateDefinition(def);
                            await context.Response.WriteAsJsonAsync(def);
                        });
                        endpoints.MapGet("/api/economy/item-defs/{name}", async context =>
                        {
                            var name = context.Request.RouteValues["name"]?.ToString() ?? string.Empty;
                            var state = context.RequestServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                            var d = state.GetDefinition(name);
                            if (d == null)
                            {
                                context.Response.StatusCode = 404;
                                return;
                            }
                            await context.Response.WriteAsJsonAsync(d);
                        });
                    });
                });

            using var server = new TestServer(builder);
            var client = server.CreateClient();
            var newDef = new EconomyItemDefinition { Name = "silver", BasePrice = 100m, WeightPerUnit = 2m, StackSize = 10, Category = "currency" };
            var post = await client.PostAsJsonAsync("/api/economy/item-defs", newDef);
            post.EnsureSuccessStatusCode();
            var created = await post.Content.ReadFromJsonAsync<EconomyItemDefinition>();
            Assert.NotNull(created);
            Assert.Equal("silver", created!.Name);

            var get = await client.GetAsync("/api/economy/item-defs/silver");
            get.EnsureSuccessStatusCode();
            var retrieved = await get.Content.ReadFromJsonAsync<EconomyItemDefinition>();
            Assert.NotNull(retrieved);
            Assert.Equal(100m, retrieved!.BasePrice);
            Assert.Equal(2m, retrieved.WeightPerUnit);

            // Bad request when missing name
            var bad = new EconomyItemDefinition { Name = "" };
            var badRes = await client.PostAsJsonAsync("/api/economy/item-defs", bad);
            Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);

            // invalid numeric fields
            var badPrice = new EconomyItemDefinition { Name = "ore", BasePrice = -1m, WeightPerUnit = 1m, StackSize = 10 };
            var r1 = await client.PostAsJsonAsync("/api/economy/item-defs", badPrice);
            Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);

            var badWeight = new EconomyItemDefinition { Name = "ore", BasePrice = 1m, WeightPerUnit = 0m, StackSize = 10 };
            var r2 = await client.PostAsJsonAsync("/api/economy/item-defs", badWeight);
            Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);

            var badStack = new EconomyItemDefinition { Name = "ore", BasePrice = 1m, WeightPerUnit = 1m, StackSize = 0 };
            var r3 = await client.PostAsJsonAsync("/api/economy/item-defs", badStack);
            Assert.Equal(HttpStatusCode.BadRequest, r3.StatusCode);
        }

        [Fact]
        public async Task ItemsEndpoint_AddsItems()
        {
            var builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(new Imperium.Api.EconomyStateService(new[] { "grain" }));
                    services.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/api/economy/items", async context =>
                        {
                            var items = await context.Request.ReadFromJsonAsync<string[]>();
                            var state = context.RequestServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                            var added = state.AddItems(items ?? System.Array.Empty<string>());
                            await context.Response.WriteAsJsonAsync(new { added, total = state.GetItems().Count });
                        });
                    });
                });

            using var server = new TestServer(builder);
            var client = server.CreateClient();
            var post = await client.PostAsJsonAsync("/api/economy/items", new string[] { "silver", "gold" });
            post.EnsureSuccessStatusCode();
            var result = await post.Content.ReadFromJsonAsync<dynamic>();
            Assert.Equal(2, (int)result.added);
            Assert.True((int)result.total >= 3);
        }

        [Fact]
        public async Task ShocksEndpoint_AppliesAndLists()
        {
            var builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(new Imperium.Api.EconomyStateService(new[] { "grain" }));
                    services.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/api/economy/shocks", async context =>
                        {
                            var payload = await context.Request.ReadFromJsonAsync<dynamic>();
                            decimal factor = (decimal)payload.factor;
                            string item = (string)payload.item;
                            System.DateTime? expiresAt = payload.expiresAt == null ? (System.DateTime?)null : (System.DateTime)payload.expiresAt;
                            if (factor <= 0)
                            {
                                context.Response.StatusCode = 400;
                                await context.Response.WriteAsJsonAsync(new { error = "factor должен быть > 0" });
                                return;
                            }
                            var state = context.RequestServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                            state.SetShock(item, factor, expiresAt);
                            var which = string.IsNullOrWhiteSpace(item) ? "*" : item;
                            await context.Response.WriteAsJsonAsync(new { item = which, factor, expiresAt });
                        });
                        endpoints.MapGet("/api/economy/shocks", async context =>
                        {
                            var state = context.RequestServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                            await context.Response.WriteAsJsonAsync(state.GetShocks());
                        });
                    });
                });

            using var server = new TestServer(builder);
            var client = server.CreateClient();
            var ok = await client.PostAsJsonAsync("/api/economy/shocks", new { item = "grain", factor = 1.5m, expiresAt = (System.DateTime?)null });
            ok.EnsureSuccessStatusCode();
            var list = await (await client.GetAsync("/api/economy/shocks")).Content.ReadFromJsonAsync<object[]>();
            Assert.NotNull(list);
            Assert.Single(list);

            var bad = await client.PostAsJsonAsync("/api/economy/shocks", new { item = "grain", factor = 0m, expiresAt = (System.DateTime?)null });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
    }
}
