using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IBS.Utility.MSAP.Constants;
using IBSWeb.MSAP;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Checks.MSAP
{
    internal static class DevelopmentStorageAccessCheck
    {
        public static async Task RunAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "msap-storage-access-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(root, "canary.txt"), "private attachment");
                await File.WriteAllTextAsync(Path.Combine(root, "canary.html"), "<script>document.documentElement.dataset.canary='executed'</script>");
                await File.WriteAllTextAsync(Path.Combine(root, "canary.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" />");
                await File.WriteAllBytesAsync(Path.Combine(root, "canary.png"), [137, 80, 78, 71, 13, 10, 26, 10]);
                await File.WriteAllTextAsync(Path.Combine(root, "canary.pdf"), "%PDF-1.7");
                foreach (var environment in new[] { "Development", "Production" })
                {
                    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
                    builder.Logging.ClearProviders();
                    builder.WebHost.UseUrls("http://127.0.0.1:0");
                    builder.Configuration["MSAP:LocalStoragePath"] = root;
                    builder.Services.AddAuthentication("StorageCheck")
                        .AddScheme<AuthenticationSchemeOptions, StorageAuthenticationHandler>("StorageCheck", null);
                    var accessGranted = true;
                    builder.Services.AddAuthorization(options => options.AddPolicy(MsapRoles.AccessPolicy, policy =>
                        policy.RequireAuthenticatedUser().RequireAssertion(context => accessGranted && context.User.HasClaim("access", "MSAP"))));
                    await using var app = builder.Build();
                    await app.UseMsapModuleAsync();
                    app.MapGet("/health", () => "healthy");
                    await app.StartAsync();
                    try
                    {
                        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                        using var client = new HttpClient { BaseAddress = new Uri(address) };
                        async Task ExpectAsync(string? account, HttpStatusCode expected)
                        {
                            client.DefaultRequestHeaders.Remove("X-Storage-Account");
                            if (account != null)
                            {
                                client.DefaultRequestHeaders.Add("X-Storage-Account", account);
                            }
                            using var response = await client.GetAsync("/msap-storage/canary.txt");
                            if (response.StatusCode != expected || expected == HttpStatusCode.OK && await response.Content.ReadAsStringAsync() != "private attachment")
                            {
                                throw new InvalidOperationException($"{environment} storage access for {account ?? "anonymous"}: {response.StatusCode}, expected {expected}.");
                            }
                        }
                        if (environment == "Development")
                        {
                            await ExpectAsync(null, HttpStatusCode.Unauthorized);
                            await ExpectAsync("unassigned", HttpStatusCode.Forbidden);
                            await ExpectAsync("MSAP", HttpStatusCode.OK);
                            foreach (var file in new[] { "canary.html", "canary.svg", "canary.png", "canary.pdf" })
                            {
                                using var response = await client.GetAsync("/msap-storage/" + file);
                                var sandboxed = response.Headers.TryGetValues("Content-Security-Policy", out var policies)
                                    && policies.Single() == "sandbox";
                                if (response.StatusCode != HttpStatusCode.OK
                                    || !response.Headers.TryGetValues("X-Content-Type-Options", out var options) || options.Single() != "nosniff"
                                    || sandboxed != (file.EndsWith(".html", StringComparison.Ordinal) || file.EndsWith(".svg", StringComparison.Ordinal)))
                                {
                                    throw new InvalidOperationException($"Attachment response isolation failed for {file}.");
                                }
                            }
                            accessGranted = false;
                            await ExpectAsync("MSAP", HttpStatusCode.Forbidden);
                        }
                        else
                        {
                            await ExpectAsync(null, HttpStatusCode.NotFound);
                            await ExpectAsync("MSAP", HttpStatusCode.NotFound);
                        }
                        client.DefaultRequestHeaders.Remove("X-Storage-Account");
                        if (await client.GetStringAsync("/health") != "healthy")
                        {
                            throw new InvalidOperationException("Storage authorization affected unrelated routes.");
                        }
                    }
                    finally
                    {
                        await app.StopAsync();
                    }
                }
                Console.WriteLine("PASS: development attachments require current MSAP access; anonymous, unassigned and revoked access is denied; active documents are sandboxed while PNG/PDF responses retain normal delivery; production has no local storage route.");
            }
            finally
            {
                foreach (var file in new[] { "canary.txt", "canary.html", "canary.svg", "canary.png", "canary.pdf" })
                {
                    File.Delete(Path.Combine(root, file));
                }
                Directory.Delete(root);
            }
        }

        private sealed class StorageAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public StorageAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
                : base(options, logger, encoder)
            {
            }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var account = Request.Headers["X-Storage-Account"].ToString();
                if (string.IsNullOrEmpty(account))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }
                var identity = new ClaimsIdentity([new Claim("access", account)], Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
            }
        }
    }
}
