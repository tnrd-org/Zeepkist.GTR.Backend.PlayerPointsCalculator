using Quartz;
using Serilog;
using TNRD.Zeepkist.GTR.Backend.PlayerPointsCalculator.Jobs;
using TNRD.Zeepkist.GTR.Database;

namespace TNRD.Zeepkist.GTR.Backend.PlayerPointsCalculator;

internal class Program
{
    public static void Main(string[] args)
    {
        IHost host = Host.CreateDefaultBuilder(args)
            .UseSerilog((context, configuration) =>
            {
                configuration
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Source", "PlayerPointsCalculator")
                    .MinimumLevel.Debug()
                    .WriteTo.Seq(context.Configuration["Seq:Url"], apiKey: context.Configuration["Seq:Key"])
                    .WriteTo.Console();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddMemoryCache();
                services.AddNpgsql<GTRContext>(context.Configuration["Database:ConnectionString"]);
                services.AddQuartz(q =>
                {
                    q.AddJob<UpdateJob>(UpdateJob.JobKey, options => options.StoreDurably());
                    q.AddJob<CalculateJob>(CalculateJob.JobKey)
                        .AddTrigger(options =>
                        {
                            options
                                .ForJob(CalculateJob.JobKey)
                                .WithIdentity(CalculateJob.JobKey.Name + "-Trigger")
                                .WithCronSchedule("0 0 0 ? * * *");
                        });
                });

                services.AddQuartzHostedService(options =>
                {
                    options.AwaitApplicationStarted = true;
                    options.WaitForJobsToComplete = true;
                });
            })
            .Build();

        ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

        TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
        {
            logger.LogCritical(eventArgs.Exception, "Unobserved task exception");
        };

        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            logger.LogCritical(eventArgs.ExceptionObject as Exception, "Unhandled exception");
        };

        host.Run();
    }
}
