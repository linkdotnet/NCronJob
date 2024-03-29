using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NCrontab;

namespace LinkDotNet.NCronJob;

/// <summary>
/// Extensions for the <see cref="IServiceCollection"/> to add cron jobs.
/// </summary>
public static class NCronJobExtensions
{
    /// <summary>
    /// Adds NCronJob services to the service container.
    /// </summary>
    /// <param name="services">The service collection used to register the services.</param>
    /// <param name="options">Configures the scheduler engine.</param>
    public static IServiceCollection AddNCronJob(
        this IServiceCollection services,
        Action<NCronJobOptions>? options = null)
    {
        NCronJobOptions option = new();
        options?.Invoke(option);

        services.AddHostedService<CronScheduler>();
        services.AddSingleton<CronRegistry>();
        services.AddSingleton<IInstantJobRegistry>(c => c.GetRequiredService<CronRegistry>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(option);

        return services;
    }

    /// <summary>
    /// Adds a job to the service collection that gets executed based on the given cron expression.
    /// </summary>
    /// <param name="services">The service collection used to register the job.</param>
    /// <param name="options">Configures the option, like the cron expression or parameters that get passed down.</param>
    /// <typeparam name="T">The job type.</typeparam>
    /// <exception cref="ArgumentException">Throws if the cron expression is invalid.</exception>
    /// <remarks>The cron expression is evaluated against UTC timezone.</remarks>
    public static IServiceCollection AddCronJob<T>(this IServiceCollection services, Action<JobOption>? options = null)
        where T : class, IJob
    {
        JobOption option = new();
        options?.Invoke(option);

        var cron = string.IsNullOrEmpty(option.CronExpression)
            ? null
            : GetCronExpression(services, option);

        if (cron is not null)
        {
            var entry = new RegistryEntry(typeof(T), new(option.Parameter), option.IsolationLevel, cron);
            services.AddSingleton(entry);
        }

        services.TryAddScoped<T>();

        return services;
    }

    /// <summary>
    /// Adds a notification handler for a given <see cref="IJob"/>.
    /// </summary>
    /// <param name="services">The service collection used to register the handler.</param>
    /// <typeparam name="TJobNotificationHandler">The handler-type that is used to handle the job.</typeparam>
    /// <typeparam name="TJob">The job type.</typeparam>
    /// <remarks>
    /// The given <see cref="IJobNotificationHandler{TJob}"/> instance is registered as a scoped service sharing the same scope as the job.
    /// Also only one handler per job is allowed. If multiple handlers are registered, only the first one will be executed.
    /// </remarks>
    public static IServiceCollection AddNotificationHandler<TJobNotificationHandler, TJob>(this IServiceCollection services)
        where TJobNotificationHandler : class, IJobNotificationHandler<TJob>
        where TJob : class, IJob
    {
        services.TryAddScoped<IJobNotificationHandler<TJob>, TJobNotificationHandler>();
        return services;
    }

    private static CrontabSchedule GetCronExpression(IServiceCollection services, JobOption option)
    {
        using var serviceProvider = services.BuildServiceProvider();

        var cronParseOptions = new CrontabSchedule.ParseOptions
        {
            IncludingSeconds = option.EnableSecondPrecision
        };

        return CrontabSchedule.TryParse(option.CronExpression, cronParseOptions)
                   ?? throw new InvalidOperationException("Invalid cron expression");
    }
}
