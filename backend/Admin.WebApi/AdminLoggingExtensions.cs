namespace Admin.WebApi;

internal static class AdminLoggingExtensions
{
    internal static void AddAdminLogging(this IHostApplicationBuilder builder)
    {
        builder.AddServiceMantleSerilog(options =>
        {
            options.MinimumLevel = LogLevel.Information;
            options.MinimumLevelOverrides = new Dictionary<string, LogLevel>
            {
                ["Microsoft.AspNetCore"] = LogLevel.Warning,
                ["Microsoft.EntityFrameworkCore.Database.Command"] = LogLevel.Warning
            };
            options.IncludeScopes = true;
        });
        var address = builder.Configuration["Loki:Uri"];
        // Invalid configuration is passed to the package validator without exposing the value.
        Uri? endpoint = null;
        if (!string.IsNullOrWhiteSpace(address)) Uri.TryCreate(address, UriKind.Absolute, out endpoint);
        builder.AddServiceMantleGrafanaLoki(options =>
        {
            options.Enabled = !string.IsNullOrWhiteSpace(address);
            options.Endpoint = endpoint;
            // Explicit acceptance of the existing trusted-network HTTP deployment contract.
            options.AllowInsecureHttp = true;
            options.Labels = new Dictionary<string, string> { ["service"] = "Ruoyu.Admin" };
        });
    }
}
