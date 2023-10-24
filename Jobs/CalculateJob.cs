using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Quartz;
using Serilog.Data;
using TNRD.Zeepkist.GTR.Database;
using TNRD.Zeepkist.GTR.Database.Models;

namespace TNRD.Zeepkist.GTR.Backend.PlayerPointsCalculator.Jobs;

public class CalculateJob : IJob
{
    public static readonly JobKey JobKey = new("Calculate");

    private readonly ILogger<CalculateJob> logger;
    private readonly GTRContext db;

    public CalculateJob(GTRContext db, ILogger<CalculateJob> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        Dictionary<int, int> userToPoints = new();

        logger.LogInformation("Getting personal bests");
        List<IGrouping<string, PersonalBest>> groups = await db.PersonalBests
            .AsNoTracking()
            .Include(x => x.RecordNavigation)
            .OrderByDescending(x => x.RecordNavigation!.Time)
            .GroupBy(x => x.Level)
            .ToListAsync(context.CancellationToken);

        logger.LogInformation("Getting level points");
        Dictionary<string, int> levelToPoints = await db.LevelPoints
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Level, y => y.Points, context.CancellationToken);

        logger.LogInformation("Starting calculation");
        foreach (IGrouping<string, PersonalBest> group in groups)
        {
            string level = group.Key;
            List<PersonalBest> personalBests = group
                .OrderBy(x => x.RecordNavigation!.Time)
                .ToList();

            for (int i = 0; i < personalBests.Count; i++)
            {
                PersonalBest personalBest = personalBests[i];
                int pointsForRankForLevel =
                    (int)Math.Floor(CalculatePercentageYield(i + 1) * levelToPoints[level] / 100);
                int user = personalBest.User;
                userToPoints.TryAdd(user, 0);
                userToPoints[user] += pointsForRankForLevel;
            }
        }

        logger.LogInformation("Triggering update job");
        await context.Scheduler.TriggerJob(UpdateJob.JobKey,
            new JobDataMap
            {
                { UpdateJob.POINTS_KEY, userToPoints }
            },
            context.CancellationToken);
    }

    private static double CalculatePercentageYield(int position)
    {
        switch (position)
        {
            case 1:
                return 100;
            case >= 25:
                return 5;
            default:
            {
                double percentage = Math.Round(100 * Math.Exp(-0.15 * (position - 1)));
                return Math.Max(percentage, 5);
            }
        }
    }
}
