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

        List<KeyValuePair<int, int>> sortedPoints = userPoints
            .OrderByDescending(x => x.Value)
            .ToList();

        for (int i = 0; i < sortedPoints.Count; i++)
        {
            KeyValuePair<int, int> kvp = sortedPoints[i];
            int rank = i + 1;

            if (playerToPlayerPoints.TryGetValue(kvp.Key, out PlayerPoints? existingPlayerPoints))
            {
                if (existingPlayerPoints.Points == kvp.Value && existingPlayerPoints.Rank == rank)
                {
                    logger.LogInformation("Skipping user {UserId} because points and rank are the same", kvp.Key);
                    continue;
                }

                logger.LogInformation(
                    "Updating points from {OldPoints} to {NewPoints}, and rank from {OldRank} to {NewRank} for user {UserId}",
                    existingPlayerPoints.Points,
                    kvp.Value,
                    existingPlayerPoints.Rank,
                    rank,
                    kvp.Key);

                EntityEntry<PlayerPoints> entry = db.PlayerPoints.Attach(existingPlayerPoints);
                entry.Entity.Points = kvp.Value;
                entry.Entity.Rank = rank;
            }
            else
            {
                PlayerPoints newPlayerPoints = new()
                {
                    User = kvp.Key,
                    Points = kvp.Value,
                    Rank = rank
                };

                await db.PlayerPoints.AddAsync(newPlayerPoints, context.CancellationToken);
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
