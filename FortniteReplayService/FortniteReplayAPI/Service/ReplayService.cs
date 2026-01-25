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

        public MatchAnalysisResponse ProcessReplay(string filePath, ScoringRules rules, GameMode mode)
        {
            // 1. Leer el archivo de replay
            var reader = new ReplayReader(_logger);
            var replay = reader.ReadReplay(filePath);

            if (replay == null) throw new Exception("No se pudo leer el archivo .replay (formato inválido o corrupto).");

            // 2. Mapear Jugadores y Equipos
            var playersDict = new Dictionary<string, MatchResult>(StringComparer.OrdinalIgnoreCase);

            // Contador para verificar la calidad de los datos oficiales
            int playersWithOfficialRank = 0;

            if (replay.PlayerData != null)
            {
                foreach (var p in replay.PlayerData)
                {
                    if (string.IsNullOrEmpty(p.EpicId)) continue;

                    string cleanId = p.EpicId.Trim();

                    if (!playersDict.ContainsKey(cleanId))
                    {
                        int officialRank = p.Placement ?? 0;
                        if (officialRank > 0) playersWithOfficialRank++;

                        // Corrección: TeamIndex nulo o -1 se convierte en equipo individual
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

            // 3. Procesar Eliminaciones (Kills/Knocks/Orden de Muerte)
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
                         // Guardamos el tiempo de última acción para desempate si sobreviven
                         if (lastInteractionTime.ContainsKey(eliminatorId))
                         {
                             if (eventTime > lastInteractionTime[eliminatorId]) lastInteractionTime[eliminatorId] = eventTime;
                         }
                         else { lastInteractionTime[eliminatorId] = eventTime; }
                    }

                    // A: KNOCK
                    if (elim.Knocked)
                    {
                        if (!string.IsNullOrEmpty(eliminatorId) && playersDict.ContainsKey(eliminatorId))
                            playersDict[eliminatorId].Knocks++;
                        continue;
                    }

                    // B: KILL
                    if (!string.IsNullOrEmpty(eliminatorId) && playersDict.ContainsKey(eliminatorId))
                        playersDict[eliminatorId].Kills++;

                    // Registrar Muerte para cálculo manual
                    if (!string.IsNullOrEmpty(victimId))
                    {
                        if (!playersDict.ContainsKey(victimId))
                        {
                            // Bot o jugador no registrado en PlayerData
                            playersDict[victimId] = new MatchResult { Id = victimId, PlayerName = "Unknown", IsBot = true, TeamId = -1, Rank = 999 };
                        }

                        deathOrder.Add(victimId);

                        if (eventTime >= maxDeathTime)
                        {
                            maxDeathTime = eventTime;
                            lastKillerId = eliminatorId;
                        }
                    }
                }
            }

            // 4. CALCULAR RANKING POR EQUIPOS
            var teams = playersDict.Values
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.Id).ToList());

            int totalTeams = teams.Count;
            int totalPlayers = playersDict.Count;

            // === DECISIÓN DE ESTRATEGIA ===
            // Solo usamos los datos oficiales si MÁS DEL 50% de los jugadores tienen ranking.
            // Esto evita el problema de tu JSON donde solo 1 tiene rank y el resto 999.
            bool dataQualityIsGood = totalPlayers > 0 && ((double)playersWithOfficialRank / totalPlayers) > 0.5;

            if (dataQualityIsGood)
            {
                // ESTRATEGIA A: DATOS OFICIALES (Confiables)
                foreach (var teamId in teams.Keys)
                {
                    var members = teams[teamId];
                    var validRanks = members
                        .Select(pid => playersDict[pid].Rank)
                        .Where(r => r > 0 && r < 999)
                        .ToList();

                    int bestRank = validRanks.Any() ? validRanks.Min() : 999;
                    SetTeamRank(playersDict, teamId, bestRank);
                }
            }
            else
            {
                // ESTRATEGIA B: CÁLCULO MANUAL (Cuando falla el replay)
                // Reconstruimos la historia de la partida.

                var activeTeams = new HashSet<int>(teams.Keys);
                var deadPlayers = new HashSet<string>(deathOrder);
                var wipedTeamsOrder = new List<int>(); // Equipos eliminados totalmente

                // Detectar equipos eliminados en orden cronológico
                foreach (var victimId in deathOrder)
                {
                    if (playersDict.TryGetValue(victimId, out var victim))
                    {
                        int tid = victim.TeamId;
                        if (activeTeams.Contains(tid))
                        {
                            var members = teams[tid];
                            // Si todos los miembros murieron, el equipo está fuera
                            if (members.All(mId => deadPlayers.Contains(mId)))
                            {
                                activeTeams.Remove(tid);
                                wipedTeamsOrder.Add(tid);
                            }
                        }
                    }
                }

                int currentRank = 1;

                // 1. Equipos Vivos (Ganadores o supervivientes al corte del replay)
                var sortedActiveTeams = activeTeams
                    .Select(tid => new
                    {
                        TeamId = tid,
                        // Bonus enorme si este equipo hizo la última kill (muy probable ganador)
                        CausedLastKill = !string.IsNullOrEmpty(lastKillerId) && teams[tid].Contains(lastKillerId),
                        // Desempate por tiempo de actividad
                        LastActionTime = teams[tid].Max(pid => lastInteractionTime.ContainsKey(pid) ? lastInteractionTime[pid] : 0f),
                        // Desempate por kills totales
                        TeamTotalKills = teams[tid].Sum(pid => playersDict.ContainsKey(pid) ? playersDict[pid].Kills : 0)
                    })
                    .OrderByDescending(t => t.CausedLastKill)
                    .ThenByDescending(t => t.TeamTotalKills) // Priorizar Kills para desempatar ganadores
                    .ThenByDescending(t => t.LastActionTime)
                    .ToList();

                foreach (var activeTeam in sortedActiveTeams)
                {
                    SetTeamRank(playersDict, activeTeam.TeamId, currentRank);
                    currentRank++;
                }

                // 2. Equipos Eliminados (El último en morir tiene mejor rank)
                wipedTeamsOrder.Reverse();
                foreach (var wipedTeamId in wipedTeamsOrder)
                {
                    SetTeamRank(playersDict, wipedTeamId, currentRank);
                    currentRank++;
                }
            }

            // 5. CALCULAR PUNTOS
            int multiplier = mode switch { GameMode.Duos => 2, GameMode.Trios => 3, GameMode.Squads => 4, _ => 1 };

            foreach (var player in playersDict.Values)
            {
                player.KillPoints = player.Kills * rules.PointsPerKill;

                int rawPlacementPoints = 0;

                // Corrección de seguridad: Si después de todo el rank sigue siendo 999,
                // lo tratamos como último lugar para no dar puntos negativos.
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

            // 6. GENERAR RESPUESTA
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

                int tPlacementPts = firstMember.PlacementPoints;
                int tKillPoints = members.Sum(p => p.KillPoints);

                return new TeamMatchResult
                {
                    TeamId = tid,
                    Rank = firstMember.Rank,
                    MemberNames = members.Select(m => m.PlayerName ?? "Unknown").ToList(),
                    TotalKills = members.Sum(p => p.Kills),
                    TotalKnocks = members.Sum(p => p.Knocks),
                    PlacementPoints = tPlacementPts,
                    KillPoints = tKillPoints,
                    TotalPoints = tPlacementPts + tKillPoints
                };
            })
            .OrderByDescending(t => t.TotalPoints)
            .ThenBy(t => t.Rank)
            .ThenByDescending(t => t.TotalKills)
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