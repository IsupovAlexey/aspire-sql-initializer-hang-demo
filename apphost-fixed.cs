#:sdk Aspire.AppHost.Sdk@13.5.2
#:property AspireUseCliBundle=false
#:package Aspire.Hosting.SqlServer@13.5.2

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = DistributedApplication.CreateBuilder(args);
var saPassword = builder.AddParameter("mssql-sa-password", "Passw0rd!");
var sql = builder.AddSqlServer("sql", saPassword);

builder.Services.AddSingleton<FixedReproInitializer>();
builder.Services.AddSingleton(
    new FixedReproRegistration(sql.Resource.Name, sql.Resource.ConnectionStringExpression)
);
builder.Services.AddHostedService<FixedReproHostedService>();
builder.Services.AddHealthChecks().Add(
    new HealthCheckRegistration(
        "db-ready",
        sp => new FixedReproHealthCheck(sp.GetRequiredService<FixedReproInitializer>()),
        null,
        null,
        null
    )
);

sql.WithHealthCheck("db-ready");

builder.Build().Run();

sealed record FixedReproRegistration(string ResourceName, ReferenceExpression ConnectionStringExpression);

sealed class FixedReproInitializer
{
    public bool Ready { get; private set; }

    public async Task MarkReadyAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);
        Ready = true;
    }
}

sealed class FixedReproHostedService(
    FixedReproRegistration registration,
    FixedReproInitializer initializer,
    ResourceNotificationService notifications,
    ILogger<FixedReproHostedService> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = RunAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await notifications.WaitForResourceAsync(
                registration.ResourceName,
                KnownResourceStates.Running,
                cancellationToken
            );
            var connectionString = await registration.ConnectionStringExpression.GetValueAsync(cancellationToken);
            logger.LogInformation("SQL connection string resolved: {HasValue}", connectionString is not null);
            await initializer.MarkReadyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fixed repro hosted service failed");
        }
    }
}

sealed class FixedReproHealthCheck(FixedReproInitializer initializer) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            initializer.Ready ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy()
        );
}
