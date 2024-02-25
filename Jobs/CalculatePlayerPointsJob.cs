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

        foreach (KeyValuePair<int, int> kvp in pointsPerPlayer)
        {
            PlayerPoints? playerPoints = await db.PlayerPoints.FirstOrDefaultAsync(x => x.User == kvp.Key);

            if (playerPoints != null)
            {
                playerPoints.Points = kvp.Value;
            }
            else
            {
                playerPoints = new PlayerPoints
                {
                    User = kvp.Key,
                    Points = kvp.Value
                };

                db.PlayerPoints.Add(playerPoints);
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
