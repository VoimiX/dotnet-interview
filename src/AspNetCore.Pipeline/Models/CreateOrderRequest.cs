using System.ComponentModel.DataAnnotations;

namespace AspNetCore.Pipeline.Models;

// Класс, а не positional record: у record атрибуты валидации нужно вешать на параметры конструктора,
// а Validator.TryValidateObject (endpoint-фильтр) читает их со свойств.
public sealed class CreateOrderRequest
{
    [Required]
    public string? Product { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }
}
