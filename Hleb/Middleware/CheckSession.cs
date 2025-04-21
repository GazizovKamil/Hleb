using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Hleb.Database;

namespace Hleb.Middleware
{
    public class CheckSession : ActionFilterAttribute
    {
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var sessionToken = context.HttpContext.Request.Headers["session-token"].FirstOrDefault();

            if (string.IsNullOrEmpty(sessionToken))
            {
                sessionToken = context.HttpContext.Request.Cookies["user_token"];

                if (string.IsNullOrEmpty(sessionToken))
                {
                    context.Result = new OkObjectResult(new { message = "Доступно только авторизованным!", status = false });
                    return;
                }
            }

            var dbContext = context.HttpContext.RequestServices.GetService<AppDbContext>();

            var session = await dbContext.Sessions
                .Where(s => s.AuthToken == sessionToken)
                .FirstOrDefaultAsync();

            if (session == null)
            {
                context.Result = new JsonResult(new { message = "Доступно только авторизованным!", status = false });
                return;
            }

            await next();
        }
    }
}
