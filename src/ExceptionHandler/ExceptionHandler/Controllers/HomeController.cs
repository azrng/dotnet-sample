using ExceptionHandler.Model;
using Microsoft.AspNetCore.Mvc;

namespace ExceptionHandler.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class HomeController : ControllerBase
{
    private readonly IUserService _userService;

    public HomeController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost]
    public IActionResult AddUser(AddUser request)
    {
        var result = _userService.Add(request);
        return result.ToOk(t => t);
    }
}