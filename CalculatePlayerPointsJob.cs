﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    private readonly ILogger<CalculatePlayerPointsJob> _logger;

    public CalculatePlayerPointsJob(GTRContext db, ILogger<CalculatePlayerPointsJob> logger)
    {
        this.db = db;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        int totalPlayers = await db.Users.AsNoTracking().CountAsync(context.CancellationToken);

        List<IGrouping<int, WorldRecord>> worldRecordGroups = await db.WorldRecords
            .AsNoTracking()
            .GroupBy(x => x.User)
            .ToListAsync(context.CancellationToken);

        Dictionary<int, int> worldRecordCountPerPlayer = worldRecordGroups.ToDictionary(x => x.Key, x => x.Count());

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

        List<PlayerPoints> playerPointsList = await db.PlayerPoints.AsTracking().ToListAsync(context.CancellationToken);
        Dictionary<int, PlayerPoints> idToPlayerPoints = playerPointsList.ToDictionary(x => x.User);

        List<KeyValuePair<int, int>> orderedPoints = pointsPerPlayer.OrderByDescending(x => x.Value).ToList();
        for (int i = 0; i < orderedPoints.Count; i++)
        {
            KeyValuePair<int, int> kvp = orderedPoints[i];
            int newRank = i + 1;

            if (idToPlayerPoints.TryGetValue(kvp.Key, out PlayerPoints? existingPlayerPoints))
            {
                existingPlayerPoints.Points = kvp.Value;
                existingPlayerPoints.Rank = newRank;
                existingPlayerPoints.WorldRecords = worldRecordCountPerPlayer.GetValueOrDefault(kvp.Key, 0);
            }
            else
            {
                PlayerPoints playerPoints = new()
                {
                    User = kvp.Key,
                    Points = kvp.Value,
                    Rank = newRank,
                    WorldRecords = worldRecordCountPerPlayer.GetValueOrDefault(kvp.Key, 0)
                };

                db.PlayerPoints.Add(playerPoints);
            }
        }

        int saveChangesAsync = await db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Saved {Count} changes", saveChangesAsync);
    }
}
