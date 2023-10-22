using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Quartz;
using TNRD.Zeepkist.GTR.Database;
using TNRD.Zeepkist.GTR.Database.Models;

namespace TNRD.Zeepkist.GTR.Backend.PlayerPointsCalculator.Jobs;

public class UpdateJob : IJob
{
    public const string POINTS_KEY = "Points";

    public static readonly JobKey JobKey = new("Update");

    private readonly ILogger<UpdateJob> logger;
    private readonly GTRContext db;

    public UpdateJob(ILogger<UpdateJob> logger, GTRContext db)
    {
        this.logger = logger;
        this.db = db;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        Dictionary<int, int> userPoints = (Dictionary<int, int>)context.MergedJobDataMap.Get(POINTS_KEY);

        logger.LogInformation("Updating points for {UserCount} users", userPoints.Count);

        Dictionary<int, PlayerPoints> playerToPlayerPoints = await db.PlayerPoints
            .AsNoTracking()
            .ToDictionaryAsync(x => x.User, y => y, context.CancellationToken);

        foreach (KeyValuePair<int, int> kvp in userPoints)
        {
            if (playerToPlayerPoints.TryGetValue(kvp.Key, out PlayerPoints? existingPlayerPoints))
            {
                if (existingPlayerPoints.Points == kvp.Value)
                {
                    logger.LogInformation("Skipping user {UserId} because points are the same", kvp.Key);
                    continue;
                }

                logger.LogInformation("Updating points from {OldPoints} to {NewPoints} for user {UserId}",
                    existingPlayerPoints.Points,
                    kvp.Value,
                    kvp.Key);

                EntityEntry<PlayerPoints> entry = db.PlayerPoints.Attach(existingPlayerPoints);
                entry.Entity.Points = kvp.Value;
            }
            else
            {
                PlayerPoints newPlayerPoints = new()
                {
                    User = kvp.Key,
                    Points = kvp.Value
                };

                await db.PlayerPoints.AddAsync(newPlayerPoints, context.CancellationToken);
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
