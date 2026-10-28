using AspNetCore.Pipeline.Filters;
using AspNetCore.Pipeline.Infrastructure;
using AspNetCore.Pipeline.Models;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCore.Pipeline.Controllers;

[ApiController]
[Route("filters")]
[PipelineTrace("controller")]
public sealed class FiltersController : ControllerBase
{
    [HttpGet("order")]
    [PipelineTrace("action")]
    public IActionResult GetOrder()
    {
        HttpContext.Trace("★ ACTION GetOrder()");
        return Ok(new
        {
            note = "Типы фильтров: Authorization → Resource → [model binding] → Action → [action] → Result (Exception — только при ошибке). " +
                   "Внутри одного типа: global → controller → action на входе и в обратном порядке на выходе.",
        });
    }

    [HttpGet("order-override")]
    [PipelineTrace("action, Order = -1", Order = -1)]
    public IActionResult GetOrderOverride()
    {
        HttpContext.Trace("★ ACTION GetOrderOverride()");
        return Ok(new { note = "IOrderedFilter.Order важнее scope: фильтр action с Order = -1 выполнился раньше global и controller (у них Order = 0)." });
    }

    [HttpGet("cached/{id:int}")]
    [ServiceFilter(typeof(CacheResourceFilter))]
    public IActionResult GetCached(int id)
    {
        HttpContext.Trace($"★ ACTION GetCached({id}) — дорогое вычисление");
        return Ok(new { id, generatedAt = DateTime.UtcNow, note = "Вызовите ещё раз: ответ придёт из resource-фильтра, action не выполнится." });
    }

    [HttpPost("validate")]
    [PipelineTrace("action")]
    public IActionResult Validate(CreateOrderRequest request)
    {
        HttpContext.Trace("★ ACTION Validate() — модель валидна");
        return Ok(new { request, note = "Отправьте Quantity = 0 или без Product: [ApiController] вернёт 400 до вызова action." });
    }

    [HttpGet("throw-in-action")]
    [HandleException]
    public IActionResult ThrowInAction()
    {
        HttpContext.Trace("★ ACTION ThrowInAction(): бросаю исключение");
        throw new InvalidOperationException("Исключение в action");
    }

    [HttpGet("throw-in-result")]
    [HandleException]
    [ThrowInResultFilter]
    public IActionResult ThrowInResult()
    {
        HttpContext.Trace("★ ACTION ThrowInResult() — отработал нормально");
        return Ok(new { never = "этот ответ не дойдёт до клиента" });
    }

    [HttpGet("short-circuit-action")]
    [ShortCircuitAction]
    public IActionResult ShortCircuitAction()
    {
        HttpContext.Trace("★ ACTION ShortCircuitAction() — этой строки не будет в трассировке");
        return Ok();
    }

    [HttpGet("forbidden")]
    [Deny]
    public IActionResult Forbidden()
    {
        HttpContext.Trace("★ ACTION Forbidden() — этой строки не будет в трассировке");
        return Ok();
    }

    [HttpGet("di")]
    [ServiceFilter(typeof(AuditFilter))]
    [TypeFilter(typeof(TaggedFilter), Arguments = new object[] { "admin" })]
    public IActionResult WithDependencies()
    {
        HttpContext.Trace("★ ACTION WithDependencies()");
        return Ok(new
        {
            note = "Атрибут-фильтр — один экземпляр на все запросы (состояние в нём хранить нельзя) и без DI в конструкторе. " +
                   "ServiceFilter берёт фильтр из контейнера (нужна регистрация, lifetime ваш), TypeFilter создаёт через ActivatorUtilities " +
                   "и позволяет передать аргументы. Оба — IFilterFactory.",
        });
    }
}
