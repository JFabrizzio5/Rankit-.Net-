using FortniteReplayAPI.Models;
using FortniteReplayReader;
using System.Globalization;

namespace FortniteReplayAPI.Services
{
    public class ReplayService
    {
        private readonly ILogger<ReplayReader> _logger;

        public ReplayService(ILogger<ReplayReader> logger)
        {
            _logger = logger;
        }

        // --- MÉTODO CORREGIDO: Lógica para el Resumen ---
        public ReplaySummaryResponse GetReplaySummary(string filePath)
        {
            var reader = new ReplayReader(_logger);
            var replay = reader.ReadReplay(filePath);

            if (replay == null) throw new Exception("No se pudo leer el archivo .replay");

            // 1. MATCH ID
            string matchId = replay.GameData?.GameSessionId ?? "UnknownSession";

            // 2. CREAR DICCIONARIO DE NOMBRES (ID -> Nombre)
            // Esto es crucial para traducir el "EliminatedBy" de ID a Nombre legible
            var playerNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (replay.PlayerData != null)
            {
                foreach (var p in replay.PlayerData)
                {
                    if (!string.IsNullOrEmpty(p.EpicId) && !string.IsNullOrEmpty(p.PlayerName))
                    {
                        playerNames[p.EpicId.Trim()] = p.PlayerName;
                    }
                }
            }

            // 3. IDENTIFICAR AL DUEÑO (Lógica Mejorada)
            // En replays locales, usualmente SOLO el dueño tiene 'Placement' registrado en AthenaMatchStats.
            // Buscamos primero alguien con Placement > 0 y que no sea Bot.
            var ownerData = replay.PlayerData?.FirstOrDefault(p => !p.IsBot && (p.Placement ?? 0) > 0);

            // Si falla, buscamos al jugador con más kills (heurística de fallback) o el primero humano
            if (ownerData == null)
            {
                ownerData = replay.PlayerData?.OrderByDescending(p => p.Kills).FirstOrDefault(p => !p.IsBot);
            }

            // Si todo falla, tomamos el primero
            if (ownerData == null) ownerData = replay.PlayerData?.FirstOrDefault();

            // Datos del dueño
            string ownerName = ownerData?.PlayerName ?? "Unknown";
            string? ownerId = ownerData?.EpicId;

            // 4. ESTADÍSTICAS
            int rank = (int)(ownerData?.Placement ?? 0);
            int kills = (int)(ownerData?.Kills ?? 0);

            int knocks = 0;
            float? deathTime = null;
            string? eliminatedById = null;
            string? eliminatedByName = null;
            bool isWinner = (rank == 1);

            if (replay.Eliminations != null && !string.IsNullOrEmpty(ownerId))
            {
                foreach (var elim in replay.Eliminations)
                {
                    string? eliminator = elim.Eliminator?.Trim();
                    string? victim = elim.Eliminated?.Trim();
                    bool isKnock = elim.Knocked;

                    // Si el dueño eliminó a alguien (knock)
                    if (!string.IsNullOrEmpty(eliminator) && eliminator.Equals(ownerId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (isKnock) knocks++;
                        // Las kills ya las tomamos de ownerData (oficiales), pero si ownerData.Kills es 0 y aquí hay datos, actualizamos
                        else if (kills == 0) kills++;
                    }

                    // Si el dueño fue la víctima (Muerte definitiva)
                    if (!string.IsNullOrEmpty(victim) && victim.Equals(ownerId, StringComparison.OrdinalIgnoreCase) && !isKnock)
                    {
                        deathTime = ParseTime(elim.Time);
                        eliminatedById = eliminator; // Guardamos ID para buscar nombre después
                    }
                }
            }

            // 5. RESOLVER NOMBRE DEL ASESINO
            if (!string.IsNullOrEmpty(eliminatedById))
            {
                if (playerNames.TryGetValue(eliminatedById, out string? nameFound))
                {
                    eliminatedByName = nameFound;
                }
                else
                {
                    // Si no encontramos el nombre, devolvemos el ID o "Unknown"
                    eliminatedByName = eliminatedById;
                }
            }

            return new ReplaySummaryResponse
            {
                MatchId = matchId,
                ReplayOwnerName = ownerName,
                Rank = rank,
                Kills = kills,
                Knocks = knocks,
                IsWinner = isWinner,
                DeathTime = deathTime,
                EliminatedBy = eliminatedByName, // Ahora devuelve el nombre real si existe
                Message = ownerData != null ? "Datos extraídos correctamente." : "No se detectaron estadísticas oficiales en la replay."
            };
        }

        // --- MÉTODO ORIGINAL: Lógica de análisis completo ---
        public MatchAnalysisResponse ProcessReplay(string filePath, ScoringRules rules, GameMode mode)
        {
            var reader = new ReplayReader(_logger);
            var replay = reader.ReadReplay(filePath);

            if (replay == null) throw new Exception("No se pudo leer el archivo .replay (formato inválido o corrupto).");

            var playersDict = new Dictionary<string, MatchResult>(StringComparer.OrdinalIgnoreCase);
            int playersWithOfficialRank = 0;

            if (replay.PlayerData != null)
            {
                foreach (var p in replay.PlayerData)
                {
                    if (string.IsNullOrEmpty(p.EpicId)) continue;
                    string cleanId = p.EpicId.Trim();

                    if (!playersDict.ContainsKey(cleanId))
                    {
                        int officialRank = (int)(p.Placement ?? 0);
                        if (officialRank > 0) playersWithOfficialRank++;
                        int rawTeamId = p.TeamIndex ?? -1;
                        int effectiveTeamId = rawTeamId != -1 ? rawTeamId : cleanId.GetHashCode();

                        playersDict[cleanId] = new MatchResult
                        {
                            Id = cleanId,
                            PlayerName = string.IsNullOrEmpty(p.PlayerName) ? cleanId : p.PlayerName,
                            IsBot = p.IsBot,
                            TeamId = effectiveTeamId,
                            Rank = officialRank > 0 ? officialRank : 999,
                            Kills = 0,
                            Knocks = 0
                        };
                    }
                }
            }

            var deathOrder = new List<string>();
            var lastInteractionTime = new Dictionary<string, float>();
            string? lastKillerId = null;
            float maxDeathTime = -1f;

            if (replay.Eliminations != null)
            {
                foreach (var elim in replay.Eliminations)
                {
                    float eventTime = ParseTime(elim.Time);
                    string eliminatorId = elim.Eliminator?.Trim() ?? "";
                    string victimId = elim.Eliminated?.Trim() ?? "";

                    if (!string.IsNullOrEmpty(eliminatorId))
                    {
                         if (lastInteractionTime.ContainsKey(eliminatorId))
                         {
                             if (eventTime > lastInteractionTime[eliminatorId]) lastInteractionTime[eliminatorId] = eventTime;
                         }
                         else { lastInteractionTime[eliminatorId] = eventTime; }
                    }

                    if (elim.Knocked)
                    {
                        if (!string.IsNullOrEmpty(eliminatorId) && playersDict.ContainsKey(eliminatorId))
                            playersDict[eliminatorId].Knocks++;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(eliminatorId) && playersDict.ContainsKey(eliminatorId))
                        playersDict[eliminatorId].Kills++;

                    if (!string.IsNullOrEmpty(victimId))
                    {
                        if (!playersDict.ContainsKey(victimId))
                        {
                            playersDict[victimId] = new MatchResult { Id = victimId, PlayerName = "Unknown", IsBot = true, TeamId = -1, Rank = 999 };
                        }

                        playersDict[victimId].EliminatedBy = eliminatorId;
                        deathOrder.Add(victimId);

                        if (eventTime >= maxDeathTime)
                        {
                            maxDeathTime = eventTime;
                            lastKillerId = eliminatorId;
                        }
                    }
                }
            }

            var teams = playersDict.Values
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

            int totalTeams = teams.Count;
            int totalPlayers = playersDict.Count;

            bool dataQualityIsGood = totalPlayers > 0 && ((double)playersWithOfficialRank / totalPlayers) > 0.5;

            if (dataQualityIsGood)
            {
                foreach (var teamId in teams.Keys)
                {
                    var members = teams[teamId];
                    var validRanks = members.Select(pid => playersDict[pid].Rank).Where(r => r > 0 && r < 999).ToList();
                    int bestRank = validRanks.Any() ? validRanks.Min() : 999;
                    SetTeamRank(playersDict, teamId, bestRank);
                }
            }
            else
            {
                var activeTeams = new HashSet<int>(teams.Keys);
                var deadPlayers = new HashSet<string>(deathOrder);
                var wipedTeamsOrder = new List<int>();

                foreach (var victimId in deathOrder)
                {
                    if (playersDict.TryGetValue(victimId, out var victim))
                    {
                        int tid = victim.TeamId;
                        if (activeTeams.Contains(tid))
                        {
                            var members = teams[tid];
                            if (members.All(mId => deadPlayers.Contains(mId)))
                            {
                                activeTeams.Remove(tid);
                                wipedTeamsOrder.Add(tid);
                            }
                        }
                    }
                }

                int currentRank = 1;
                var sortedActiveTeams = activeTeams
                    .Select(tid => new
                    {
                        TeamId = tid,
                        CausedLastKill = !string.IsNullOrEmpty(lastKillerId) && teams[tid].Contains(lastKillerId),
                        LastActionTime = teams[tid].Max(pid => lastInteractionTime.ContainsKey(pid) ? lastInteractionTime[pid] : 0f),
                        TeamTotalKills = teams[tid].Sum(pid => playersDict.ContainsKey(pid) ? playersDict[pid].Kills : 0)
                    })
                    .OrderByDescending(t => t.CausedLastKill)
                    .ThenByDescending(t => t.TeamTotalKills)
                    .ThenByDescending(t => t.LastActionTime)
                    .ToList();

                foreach (var activeTeam in sortedActiveTeams)
                {
                    SetTeamRank(playersDict, activeTeam.TeamId, currentRank);
                    currentRank++;
                }

                wipedTeamsOrder.Reverse();
                foreach (var wipedTeamId in wipedTeamsOrder)
                {
                    SetTeamRank(playersDict, wipedTeamId, currentRank);
                    currentRank++;
                }
            }

            int multiplier = mode switch { GameMode.Duos => 2, GameMode.Trios => 3, GameMode.Squads => 4, _ => 1 };

            foreach (var player in playersDict.Values)
            {
                player.KillPoints = player.Kills * rules.PointsPerKill;
                int rawPlacementPoints = 0;
                int safeRank = (player.Rank > 900) ? totalTeams : player.Rank;

                if (rules.UseLinearPlacement)
                {
                    int points = (totalTeams - safeRank);
                    if (points < 0) points = 0;
                    if (safeRank == 1) points += rules.WinBonus;
                    rawPlacementPoints = points;
                }
                else
                {
                    rawPlacementPoints = CalculateLegacyPoints(safeRank, rules);
                }

                player.PlacementPoints = rawPlacementPoints * multiplier;
                player.TotalPoints = player.KillPoints + player.PlacementPoints;
            }

            var playerLeaderboard = playersDict.Values
                .OrderByDescending(p => p.TotalPoints)
                .ThenByDescending(p => p.Kills)
                .ThenByDescending(p => p.Knocks)
                .ToList();

            for (int i = 0; i < playerLeaderboard.Count; i++) playerLeaderboard[i].LeaderboardRank = i + 1;

            var teamLeaderboard = teams.Keys.Select(tid =>
            {
                var members = playersDict.Values.Where(p => p.TeamId == tid).ToList();
                var firstMember = members.First();
                return new TeamMatchResult
                {
                    TeamId = tid,
                    Rank = firstMember.Rank,
                    MemberNames = members.Select(m => m.PlayerName ?? "Unknown").ToList(),
                    TotalKills = members.Sum(p => p.Kills),
                    TotalKnocks = members.Sum(p => p.Knocks),
                    PlacementPoints = firstMember.PlacementPoints,
                    KillPoints = members.Sum(p => p.KillPoints),
                    TotalPoints = firstMember.PlacementPoints + members.Sum(p => p.KillPoints)
                };
            })
            .OrderByDescending(t => t.TotalPoints)
            .ToList();

            for (int i = 0; i < teamLeaderboard.Count; i++) teamLeaderboard[i].LeaderboardRank = i + 1;

            return new MatchAnalysisResponse
            {
                FileName = Path.GetFileName(filePath),
                ProcessedAt = DateTime.UtcNow,
                TotalTeams = totalTeams,
                TotalPlayers = playersDict.Count,
                Mode = mode,
                TeamLeaderboard = teamLeaderboard,
                PlayerLeaderboard = playerLeaderboard
            };
        }

        private void SetTeamRank(Dictionary<string, MatchResult> dict, int teamId, int rank)
        {
            foreach (var p in dict.Values.Where(x => x.TeamId == teamId))
            {
                p.Rank = rank;
            }
        }

        private int CalculateLegacyPoints(int rank, ScoringRules rules)
        {
            int pts = 0;
            if (rules.Thresholds != null)
            {
                foreach (var t in rules.Thresholds) if (rank <= t.ThresholdRank) pts += t.Points;
            }
            if (rules.Ranges != null)
            {
                foreach (var r in rules.Ranges)
                {
                    if (rank > r.StartRank) continue;
                    int effectiveEnd = Math.Max(rank, r.EndRank);
                    int steps = (r.StartRank - effectiveEnd) + 1;
                    if (steps > 0) pts += (steps * r.PointsPerStep);
                }
            }
            return pts;
        }

        private float ParseTime(string? timeString)
        {
            if (string.IsNullOrEmpty(timeString)) return 0f;
            if (float.TryParse(timeString, NumberStyles.Any, CultureInfo.InvariantCulture, out float result)) return result;
            return 0f;
        }
    }
}