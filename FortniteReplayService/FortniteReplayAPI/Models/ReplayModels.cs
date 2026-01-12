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
        public bool IsWinner { get; set; } // Nuevo: Indica explícitamente si ganó (Top 1)
        public string? EliminatedBy { get; set; } // Nuevo: Quién lo eliminó

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
        public bool IsWinner { get; set; } // Nuevo: Indica si el equipo ganó esta partida
        
        public int TotalKills { get; set; }
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
        
        // Tablas de posiciones ACUMULADAS (La suma de todas las partidas)
        public List<TournamentTeamResult> OverallTeamLeaderboard { get; set; } = new();
        public List<TournamentPlayerResult> OverallPlayerLeaderboard { get; set; } = new();
        
        // Detalle por partida (opcional, para ver el desglose si se necesita)
        public List<MatchAnalysisResponse> MatchDetails { get; set; } = new();
    }

    public class TournamentTeamResult
    {
        public int TeamId { get; set; }
        public List<string> MemberNames { get; set; } = new();
        
        // Estadísticas acumuladas
        public int MatchesPlayed { get; set; }
        public int Wins { get; set; } // Cantidad de victorias (Top 1) en el torneo
        public int TotalKills { get; set; }
        
        public int TotalPlacementPoints { get; set; }
        public int TotalKillPoints { get; set; }
        public int TotalPoints { get; set; }
        
        public double AverageRank { get; set; }
        public double AverageKills { get; set; }
    }

    public class TournamentPlayerResult
    {
        public string Id { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        
        public int MatchesPlayed { get; set; }
        public int Wins { get; set; }
        public int TotalKills { get; set; }
        
        public int TotalPlacementPoints { get; set; }
        public int TotalKillPoints { get; set; }
        public int TotalPoints { get; set; }
        
        public double AverageRank { get; set; }
    }

    // ==========================================
    // REGLAS DE PUNTUACIÓN
    // ==========================================

    public class ScoringRules
    {
        public int PointsPerKill { get; set; } = 1;
        
        // Si true, usa fórmula lineal: (TotalTeams - Rank) + Bonus
        // Si false, usa Thresholds o Ranges (estilo competitivo clásico)
        public bool UseLinearPlacement { get; set; } = true; 
        
        public int WinBonus { get; set; } = 5; // Puntos extra solo para el Top 1 (si es lineal)

        // Opciones Legacy (para torneos con reglas específicas)
        public List<PlacementThreshold>? Thresholds { get; set; }
        public List<PlacementRange>? Ranges { get; set; }
    }

    public class PlacementThreshold
    {
        public int ThresholdRank { get; set; } // Ej: Top 25
        public int Points { get; set; }        // Puntos otorgados
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