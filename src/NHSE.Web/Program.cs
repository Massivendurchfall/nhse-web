using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NHSE.Core;
using NHSE.Web;
using NHSE.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<SaveSession>();
builder.Services.AddScoped<ItemCatalog>();

// UI and item names follow the browser language: German or English.
L.Use(CultureInfo.CurrentUICulture.Name);
GameInfo.SetLanguage2Char(GameLanguage.GetLanguageIndex(L.German ? "de" : "en"));

await builder.Build().RunAsync();
