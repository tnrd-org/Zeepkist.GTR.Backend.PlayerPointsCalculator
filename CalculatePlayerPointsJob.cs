using Microsoft.EntityFrameworkCore;
using Quartz;
using TNRD.Zeepkist.GTR.Database;
using TNRD.Zeepkist.GTR.Database.Models;

namespace TNRD.Zeepkist.GTR.Backend.PlayerPointsCalculator;

public class CalculatePlayerPointsJob : IJob
{
    public static readonly JobKey JobKey = new(nameof(CalculatePlayerPointsJob));

    private static Dictionary<int, double> fibbonus = new()
    {
        { 0, 0.21 },
        { 1, 0.13 },
        { 2, 0.08 },
        { 3, 0.05 },
        { 4, 0.03 },
        { 5, 0.02 },
        { 6, 0.01 },
        { 7, 0.01 }
    };

    private readonly GTRContext db;

    public CalculatePlayerPointsJob(GTRContext db)
    {
        this.db = db;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        int totalPlayers = await db.Users.AsNoTracking().CountAsync(context.CancellationToken);

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

            int count = personalBests.Count;

            for (int i = 0; i < personalBests.Count; i++)
            {
                int placementPoints = Math.Max(0, count - i);
                double a = 1d / (totalPlayers / (double)count);
                int b = i + 1;
                double c = i < 8 ? fibbonus[i] : 0;
                double points = placementPoints * (1 + a / b) + c;

                PersonalBest personalBest = personalBests[i];
                pointsPerPlayer.TryAdd(personalBest.User, 0);
                pointsPerPlayer[personalBest.User] += (int)Math.Round(points);
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
