using BookStore.Application.Common.Interfaces;
using BookStore.Core.Domain.Authentication;
using BookStore.Core.Domain.Books;
using BookStore.Infrastructure.Authentication;
using BookStore.Infrastructure.BackgroundJobs;
using BookStore.Infrastructure.Email;
using BookStore.Infrastructure.Persistence;
using BookStore.Infrastructure.Persistence.Interceptors;
using BookStore.Infrastructure.Persistence.Repositories;
using BookStore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace BookStore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<PublishDomainEventsInterceptor>();

        services.AddDbContext<BookStoreDbContext>((serviceProvider, options) =>
        {
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection"));
            options.AddInterceptors(serviceProvider.GetRequiredService<PublishDomainEventsInterceptor>());
        });

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        services.AddOptions<LocalFileStorageOptions>()
            .Bind(configuration.GetSection(LocalFileStorageOptions.SectionName));
        services.AddScoped<IFileStorage, LocalFileStorage>();

        services.AddOptions<SmtpSettings>()
            .Bind(configuration.GetSection(SmtpSettings.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .Validate(settings =>
                !string.IsNullOrWhiteSpace(settings.Secret) &&
                settings.Secret.Length >= 32 &&
                !string.IsNullOrWhiteSpace(settings.Issuer) &&
                !string.IsNullOrWhiteSpace(settings.Audience),
                "JwtSettings must define a Secret (min 32 chars), Issuer, and Audience.")
            .ValidateOnStart();

        services.AddQuartz(configuration =>
        {
            var jobKey = new JobKey(nameof(ProcessOutboxMessagesJob));

            configuration.AddJob<ProcessOutboxMessagesJob>(jobKey);

            configuration.AddTrigger(trigger => trigger
                .ForJob(jobKey)
                .WithSimpleSchedule(schedule =>
                    schedule.WithIntervalInSeconds(30).RepeatForever()));
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}
