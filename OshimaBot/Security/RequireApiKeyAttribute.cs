using System.Security.Cryptography;
using System.Text;
using OshimaBot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace OshimaBot.Security
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequireApiKeyAttribute : Attribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            ServerOptions options = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<ServerOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Title = "第三方接口尚未配置 API Key。",
                    Status = StatusCodes.Status503ServiceUnavailable
                })
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable
                };
                return;
            }

            string provided = context.HttpContext.Request.Headers["X-API-Key"].ToString();
            if (string.IsNullOrWhiteSpace(provided))
            {
                string authorization = context.HttpContext.Request.Headers.Authorization.ToString();
                if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    provided = authorization[7..].Trim();
                }
            }

            byte[] expectedBytes = Encoding.UTF8.GetBytes(options.ApiKey);
            byte[] providedBytes = Encoding.UTF8.GetBytes(provided);
            if (providedBytes.Length == 0
                || providedBytes.Length != expectedBytes.Length
                || !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            {
                context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
