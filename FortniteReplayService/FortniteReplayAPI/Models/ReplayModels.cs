using System;
using System.Collections.Generic;

namespace FortniteReplayAPI.Models
{
    // ==========================================
    // RESULTADOS DE UNA SOLA PARTIDA (MATCH)
    // ==========================================

    public class MatchAnalysisResponse
    {
        public string FileName { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public int TotalTeams { get; set; }
        public int TotalPlayers { get; set; }
        public GameMode Mode { get; set; }
        public List<TeamMatchResult> TeamLeaderboard { get; set; } = new();
        public List<MatchResult> PlayerLeaderboard { get; set; } = new();
    }

    // Resultado individual en UNA partida
    public class MatchResult
    {
        public string Id { get; set; } = string.Empty; // Epic ID
        public string PlayerName { get; set; } = string.Empty;
        public bool IsBot { get; set; }
        public int TeamId { get; set; }
        public int Rank { get; set; }

        public int Kills { get; set; }
        public int Knocks { get; set; } // <--- NUEVO: Contador de derribos

        public bool IsWinner { get; set; }
        public string? EliminatedBy { get; set; }

        // Puntos desglosados
        public int KillPoints { get; set; }
        public int PlacementPoints { get; set; }
        public int TotalPoints { get; set; }
    }

    // Resultado de equipo en UNA partida
    public class TeamMatchResult
    {
        public int TeamId { get; set; }
        public int Rank { get; set; }
        public List<string> MemberNames { get; set; } = new();
        public bool IsWinner { get; set; }

        public int TotalKills { get; set; }
        public int TotalKnocks { get; set; } // <--- NUEVO: Derribos totales del equipo

        // Puntos desglosados
        public int KillPoints { get; set; }
        public int PlacementPoints { get; set; }
        public int TotalPoints { get; set; }
    }

    // ==========================================
    // RESULTADOS DEL TORNEO (RESUMEN GLOBAL)
    // ==========================================

    public class TournamentAnalysisResponse
    {
        public int TotalMatches { get; set; }
        public List<string> ProcessedFiles { get; set; } = new();

        // Tablas de posiciones ACUMULADAS
        public List<TournamentTeamResult> OverallTeamLeaderboard { get; set; } = new();
        public List<TournamentPlayerResult> OverallPlayerLeaderboard { get; set; } = new();

        // Opcional: Detalles por partida si se necesitan en el frontend
        public List<MatchAnalysisResponse> MatchDetails { get; set; } = new();
    }

    public class TournamentTeamResult
    {
        public int TeamId { get; set; } // ID generado o hash para el torneo
        public List<string> MemberNames { get; set; } = new();

        // Estadísticas acumuladas
        public int MatchesPlayed { get; set; }
        public int Wins { get; set; }
        public int TotalKills { get; set; }
        public int TotalKnocks { get; set; } // <--- NUEVO

        // Puntos acumulados desglosados
        public int PlacementPoints { get; set; }
        public int KillPoints { get; set; }
        public int TotalPoints { get; set; }

        // Promedios
        public double AverageRank { get; set; }
        public double AverageKills { get; set; }
        public double AverageKnocks { get; set; } // <--- NUEVO
    }

    public class TournamentPlayerResult
    {
        public string Id { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;

        // Estadísticas acumuladas
        public int MatchesPlayed { get; set; }
        public int Wins { get; set; }
        public int TotalKills { get; set; }
        public int TotalKnocks { get; set; } // <--- NUEVO

        // Puntos acumulados desglosados
        public int PlacementPoints { get; set; }
        public int KillPoints { get; set; }
        public int TotalPoints { get; set; }

        // Promedios
        public double AverageRank { get; set; }
        public double AverageKills { get; set; }
        public double AverageKnocks { get; set; } // <--- NUEVO
    }

    // ==========================================
    // REGLAS DE PUNTUACIÓN
    // ==========================================

    public class ScoringRules
    {
        public int PointsPerKill { get; set; } = 1;
        public bool UseLinearPlacement { get; set; } = true; 
        public int WinBonus { get; set; } = 5; 
        public List<PlacementThreshold>? Thresholds { get; set; }
        public List<PlacementRange>? Ranges { get; set; }
    }

    public class PlacementThreshold
    {
        public int ThresholdRank { get; set; } 
        public int Points { get; set; }        
    }

    public class PlacementRange
    {
        public int StartRank { get; set; }
        public int EndRank { get; set; }
        public int PointsPerStep { get; set; }
    }

    public enum GameMode
    {
        Solos = 1,
        Duos = 2,
        Trios = 3,
        Squads = 4
    }
}