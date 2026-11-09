using Microsoft.AspNetCore.Authorization;

namespace OshimaBot.Security
{
    /// <summary>
    /// 旧版直接比对全局明文 Server:ApiKey；S6 起改为标准 AuthenticationScheme + Policy，
    /// 由 ApiKeyAuthenticationHandler 查库校验哈希，支持吊销、过期与分权。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequireApiKeyAttribute : AuthorizeAttribute
    {
        public RequireApiKeyAttribute()
        {
            AuthenticationSchemes = ApiKeyDefaults.Scheme;
            Policy = ApiKeyDefaults.Policy;
        }
    }
}
