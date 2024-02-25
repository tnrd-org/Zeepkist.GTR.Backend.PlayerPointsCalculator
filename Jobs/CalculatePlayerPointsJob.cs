using Microsoft.EntityFrameworkCore;
using Quartz;
using TNRD.Zeepkist.GTR.Database;
using TNRD.Zeepkist.GTR.Database.Models;

namespace TNRD.Zeepkist.GTR.Backend.PlayerPointsCalculator.Jobs;

public class CalculatePlayerPointsJob : IJob
{
    public static readonly JobKey JobKey = new(nameof(CalculatePlayerPointsJob));

    private readonly GTRContext db;

    public CalculatePlayerPointsJob(GTRContext db)
    {
        this.db = db;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        List<IGrouping<string, PersonalBest>> groups = await db.PersonalBests
            .AsNoTracking()
            .Include(x => x.RecordNavigation)
            .OrderBy(x => x.Id)
            .GroupBy(x => x.Level)
            .ToListAsync(context.CancellationToken);

        Dictionary<int, int> pointsPerPlayer = new();

        foreach (IGrouping<string, PersonalBest> group in groups)
        {
            List<PersonalBest> personalBests = group.OrderBy(x => x.RecordNavigation!.Time).ToList();

            for (int i = 0; i < personalBests.Count; i++)
            {
                PersonalBest personalBest = personalBests[i];
                pointsPerPlayer.TryAdd(personalBest.User, 0);
                pointsPerPlayer[personalBest.User] += Math.Max(0, group.Count() - i);
            }
        }

        List<PlayerPoints> playerPointsList = await db.PlayerPoints.ToListAsync(context.CancellationToken);
        Dictionary<int, PlayerPoints> idToPlayerPoints = playerPointsList.ToDictionary(x => x.User);

        List<KeyValuePair<int, int>> orderedPoints = pointsPerPlayer.OrderByDescending(x => x.Value).ToList();
        for (int i = 0; i < orderedPoints.Count; i++)
        {
            KeyValuePair<int, int> kvp = orderedPoints[i];
            int newRank = i + 1;

            if (idToPlayerPoints.TryGetValue(kvp.Key, out PlayerPoints? playerPoints))
            {
                if (playerPoints.Points != kvp.Value || playerPoints.Rank != newRank)
                {
                    playerPoints.Points = kvp.Value;
                    playerPoints.Rank = newRank;
                    playerPoints.DateUpdated = DateTime.UtcNow;
                }
            }
            else
            {
                playerPoints = new PlayerPoints
                {
                    User = kvp.Key,
                    Points = kvp.Value,
                    Rank = newRank,
                    DateUpdated = DateTime.UtcNow,
                    DateCreated = DateTime.UtcNow
                };

                db.PlayerPoints.Add(playerPoints);
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
