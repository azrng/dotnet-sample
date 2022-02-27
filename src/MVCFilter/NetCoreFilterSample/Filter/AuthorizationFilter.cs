using Common.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace NetCoreFilterSample.Filter;

/// <summary>
/// 权限控制过滤器
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class Authorization_01Filter : Attribute, IAuthorizationFilter
{
    /*
     权限控制器过滤器，可以通过Authonization可以实现复杂的权限角色认证、登录授权等操作实现事例

     猜想：是否到底这一步的都应该是已经经过身份认证的用户，这边只是做一些授权操作，还是说这个地方做认证以及授权操作
     */

    private readonly ILogger<Authorization_01Filter> _logger;

    public Authorization_01Filter(ILogger<Authorization_01Filter> logger)
    {
        _logger = logger;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        _logger.LogInformation("进入授权过滤器");

        /*
         实现效果：自定义身份验证，当用户调用登录接口的时候，会查询数据库并且将该用户信息存入redis(格式Wie：key：随机数，value：用户信息)，然后返回随机数
         当请求其它接口的时候，验证请求头中是否传输了Authorization，如果没传直接返回401
         当传输了token，那么就拿着值去查询redis，然后验证通过后将用户信息存入上下文的User中

         */
        if (!context.HttpContext.Request.Headers.Any(t => t.Key == "Authorization"))
            context.Result = new JsonResult(new ResultModel { Code = "401", Message = "认证失败" });

        var token = context.HttpContext.Request.Headers.FirstOrDefault(t => t.Key == "Authorization").Value;
        if (token != "123456")//这里替换查询redis操作
        {
            context.Result = new JsonResult(new ResultModel { Code = "401", Message = "认证失败" });
        }

        //如果查询到上面传输的信息，那么就存储到上下文中
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "zyp"),
            new Claim(ClaimTypes.Role, "admin"),
        };
        var claimIdentities = new List<ClaimsIdentity>
        {
            new ClaimsIdentity(claims)
        };
        context.HttpContext.User.AddIdentities(claimIdentities);
    }
}

/// <summary>
/// 异步方案
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class Authorization_01AsyncFilter : Attribute, IAsyncAuthorizationFilter
{
    private readonly ILogger<Authorization_01Filter> _logger;

    public Authorization_01AsyncFilter(ILogger<Authorization_01Filter> logger)
    {
        _logger = logger;
    }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        _logger.LogInformation("进入授权过滤器");
        if (!context.HttpContext.Request.Headers.Any(t => t.Key == "Authorization"))
            context.Result = new JsonResult(new ResultModel { Code = "401", Message = "认证失败" });

        var token = context.HttpContext.Request.Headers.First(t => t.Key == "Authorization").Value;
        if (token != "123456")//这里替换查询redis操作
        {
            context.Result = new JsonResult(new ResultModel { Code = "401", Message = "认证失败" });
        }

        //如果查询到上面传输的信息，那么就存储到上下文中
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "zyp"),
            new Claim(ClaimTypes.Role, "admin"),
        };
        var claimIdentities = new List<ClaimsIdentity>
        {
            new ClaimsIdentity(claims)
        };
        context.HttpContext.User.AddIdentities(claimIdentities);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 授权过滤器02
/// </summary>
public class Authonization_2Filter : AuthorizeFilter
{
    private readonly ILogger<Authorization_01Filter> _logger;

    public Authonization_2Filter(ILogger<Authorization_01Filter> logger)
    {
        _logger = logger;
    }

    public override Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        _logger.LogInformation("进入授权过滤器");
        //实现效果：当请求头中传递了Authorization，但是值不是123456，那么就提示认证失败
        if (context.HttpContext.Request.Headers.Any(t => t.Key == "Authorization"))
        {
            var token = context.HttpContext.Request.Headers.First(t => t.Key == "Authorization").Value;
            if (token != "123456")
            {
                context.Result = new JsonResult(new ResultModel { Code = "401", Message = "认证失败" });
            }
        }
        //上下文的用户名称不等于1，那么就调试认证失败
        //if (context.HttpContext.User.Identity.Name != "1")
        //{
        //    //没有权限时候跳转到没有权限的页面或者直接返回401错误
        //    //RedirectToActionResult content = new RedirectToActionResult("Login", "Home", null);
        //    //接口里面返回自定义数据
        //    context.Result = new JsonResult(new ResultModel { IsSuccess = false, Code = "401", Message = "认证失败" });
        //}
        return base.OnAuthorizationAsync(context);
    }
}