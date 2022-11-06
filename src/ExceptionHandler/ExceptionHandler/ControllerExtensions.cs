using LanguageExt.Common;
using Microsoft.AspNetCore.Mvc;

namespace ExceptionHandler;

public static class ControllerExtensions
{
    public static IActionResult ToOk<TResult, TContract>(this Result<TResult> result,
        Func<TResult, TContract> mapper)
    {
        return result.Match<IActionResult>(obj =>
        {
            var response = mapper(obj);
            return new OkObjectResult(response);
        }, exception =>
        {
            if (exception is ArgumentException)
                return new BadRequestObjectResult(exception.Message);

            return new StatusCodeResult(500);
        });
    }
}