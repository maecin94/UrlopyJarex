namespace UrlopyJarex.Models;

public static class VacationTypeExtensions
{
    public static string ToPolishName(this VacationType type)
    {
        return type switch
        {
            VacationType.Vacation => "Urlop wypoczynkowy",
            _ => "Nieznany"
        };
    }
}