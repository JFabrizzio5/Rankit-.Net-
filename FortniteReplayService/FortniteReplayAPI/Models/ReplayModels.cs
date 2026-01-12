using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FortniteReplayAPI.Models
{
    // Enum para definir la modalidad de juego
    public enum GameMode
    {
        Solos = 1,
        Duos = 2,
        Trios = 3,
        Squads = 4
    }

    /// <summary>
    /// Modelo principal que representa un archivo de Replay analizado.
    /// </summary>
    public class ReplayFile
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? FileName { get; set; }
        public string? Hash { get; set; }
        public DateTime UploadDate { get; set; } = DateTime.UtcNow;
        public string? UserId { get; set; }
        public MatchData? MatchInfo { get; set; }
    }

    /// <summary>
    /// Detalles específicos de la partida.
    /// </summary>
    public class MatchData
    {
        public DateTime MatchDate { get; set; }
        public int DurationInMilliseconds { get; set; }
        public string? GameVersion { get; set; }
        public int TotalPlayers { get; set; }
        public bool IsTournament { get; set; }
        public string? PlaylistId { get; set; }
        public List<PlayerStat> Players { get; set; } = new List<PlayerStat>();
        public List<EliminationEvent> KillFeed { get; set; } = new List<EliminationEvent>();
    }

    public class PlayerStat
    {
        public string? EpicId { get; set; }
        public string? Username { get; set; }
        public bool IsBot { get; set; }
        public int Placement { get; set; }
        public int Kills { get; set; }
        public int Assists { get; set; }
        public int DamageDealt { get; set; }
        public int DamageTaken { get; set; }
        public int MaterialsGathered { get; set; }
        public string? Platform { get; set; }
    }

    public class EliminationEvent
    {
        public int TimeOffset { get; set; }
        public string? KillerId { get; set; }
        public string? VictimId { get; set; }
        public string? Weapon { get; set; }
        public bool IsHeadshot { get; set; }
        public float Distance { get; set; }
    }

    public class ReplayUploadRequest
    {
        [Required]
        public string? FileName { get; set; }
        [Required]
        public byte[]? FileContent { get; set; }
        public string? UserNotes { get; set; }
    }

    /// <summary>
    /// Define las reglas de puntuación.
    /// </summary>
    public class ScoringRules
    {
        // Si es true, usa la fórmula: (TotalTeams - Rank) * Multiplicador
        public bool UseLinearPlacement { get; set; } = true; 
        
        public int PointsPerKill { get; set; } = 2;
        public int WinBonus { get; set; } = 5; // Puntos extra por ganar (Top 1)

        // Listas para lógica personalizada (si UseLinearPlacement es false)
        public List<RankThreshold> Thresholds { get; set; } = new List<RankThreshold>();
        public List<RankRange> Ranges { get; set; } = new List<RankRange>();
    }

    public class RankThreshold 
    {
        public int ThresholdRank { get; set; }
        public int Points { get; set; }
    }

    public class RankRange
    {
        public int StartRank { get; set; }
        public int EndRank { get; set; }
        public int PointsPerStep { get; set; }
    }

    /// <summary>
    /// Resultado de un jugador individual.
    /// </summary>
    public class MatchResult
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? PlayerName { get; set; }
        public int TeamId { get; set; } // ID del equipo (interno del replay)
        public bool IsBot { get; set; }
        public int Kills { get; set; }
        
        public int Rank { get; set; } // Posición final del equipo
        
        public int KillPoints { get; set; }
        public int PlacementPoints { get; set; } // Puntos por posición (ya multiplicados)
        public int TotalPoints { get; set; }
    }

    /// <summary>
    /// Resultado agrupado por equipo.
    /// </summary>
    public class TeamMatchResult
    {
        public int TeamId { get; set; }
        public int Rank { get; set; }
        public List<string> MemberNames { get; set; } = new List<string>();
        
        public int TotalKills { get; set; }
        
        public int KillPoints { get; set; }
        public int PlacementPoints { get; set; }
        public int TotalPoints { get; set; }
    }

    /// <summary>
    /// Respuesta completa de la API con ambas tablas.
    /// </summary>
    public class MatchAnalysisResponse
    {
        public string FileName { get; set; } = "";
        public DateTime ProcessedAt { get; set; }
        public int TotalTeams { get; set; }
        public int TotalPlayers { get; set; }
        public GameMode Mode { get; set; }
        
        public List<TeamMatchResult> TeamLeaderboard { get; set; } = new List<TeamMatchResult>();
        public List<MatchResult> PlayerLeaderboard { get; set; } = new List<MatchResult>();
    }
}