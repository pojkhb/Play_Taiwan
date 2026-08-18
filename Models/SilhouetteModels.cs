using System;

namespace TrafficSystem.Models;

public class Silhouette
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    public string? City { get; set; }
    public string? Category { get; set; }

    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}   