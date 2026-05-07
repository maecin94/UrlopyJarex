namespace UrlopyJarex.Models;

public class HolidayDefinition
{
    public int Id { get; set; }

    public int MonthNumber { get; set; }

    public int DayNumber { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public string DateText => $"{DayNumber:00}.{MonthNumber:00}";
}