#:sdk Aspire.AppHost.Sdk@13.5.2
#:property AspireUseCliBundle=false
#:package Aspire.Hosting.SqlServer@13.5.2

using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = DistributedApplication.CreateBuilder(args);
var saPassword = builder.AddParameter("mssql-sa-password", "Passw0rd!");
var sql = builder.AddSqlServer("sql", saPassword);

builder.Services.AddSingleton<HangReproState>();
builder.Services.AddHealthChecks().Add(
    new HealthCheckRegistration(
        "db-ready",
        sp => new HangReproHealthCheck(sp.GetServices<IDistributedApplicationEventingSubscriber>()),
        null,
        null,
        null
    )
);

sql.WithHealthCheck("db-ready");
builder.Services.TryAddEventingSubscriber<HangReproSqlSubscriber>();

builder.Build().Run();

sealed class HangReproState
{
    public bool Ready { get; set; }
}

sealed class HangReproSqlSubscriber(ResourceNotificationService notifications) : IDistributedApplicationEventingSubscriber
{
    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken
    )
    {
        eventing.Subscribe<ResourceEndpointsAllocatedEvent>(
            async (@event, _) =>
            {
                if (@event.Resource is SqlServerServerResource sqlResource)
                {
                    await notifications.WaitForResourceAsync(
                        sqlResource.Name,
                        KnownResourceStates.Running,
                        cancellationToken
                    );
                }
            }
        );

        return Task.CompletedTask;
    }
}

sealed class HangReproHealthCheck(IEnumerable<IDistributedApplicationEventingSubscriber> subscribers) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var hook = subscribers.SingleOrDefault(s => s is HangReproSqlSubscriber);
        return Task.FromResult(
            hook is not null ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy()
        );
    }
}
