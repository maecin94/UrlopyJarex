namespace UrlopyJarex.Models;

public class Holiday
{
    public int Id { get; set; }

    public DateTime HolidayDate { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}